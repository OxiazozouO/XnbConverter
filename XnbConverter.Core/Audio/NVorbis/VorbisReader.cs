using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NVorbis.Ogg;

namespace NVorbis;

public class VorbisReader : IDisposable
{
	private int _streamIdx;

	private IContainerReader _containerReader;

	private List<VorbisStreamDecoder> _decoders;

	private List<int> _serials;

	private VorbisStreamDecoder ActiveDecoder
	{
		get
		{
			if (_decoders == null)
			{
				throw new ObjectDisposedException("VorbisReader");
			}
			return _decoders[_streamIdx];
		}
	}

	/// <summary>
	/// Gets the number of channels in the current selected Vorbis stream
	/// </summary>
	public int Channels => ActiveDecoder._channels;

	/// <summary>
	/// Gets the sample rate of the current selected Vorbis stream
	/// </summary>
	public int SampleRate => ActiveDecoder._sampleRate;

	/// <summary>
	/// Gets the encoder's upper bitrate of the current selected Vorbis stream
	/// </summary>
	public int UpperBitrate => ActiveDecoder._upperBitrate;

	/// <summary>
	/// Gets the encoder's nominal bitrate of the current selected Vorbis stream
	/// </summary>
	public int NominalBitrate => ActiveDecoder._nominalBitrate;

	/// <summary>
	/// Gets the encoder's lower bitrate of the current selected Vorbis stream
	/// </summary>
	public int LowerBitrate => ActiveDecoder._lowerBitrate;

	/// <summary>
	/// Gets the encoder's vendor string for the current selected Vorbis stream
	/// </summary>
	public string Vendor => ActiveDecoder._vendor;

	/// <summary>
	/// Gets the comments in the current selected Vorbis stream
	/// </summary>
	public string[] Comments => ActiveDecoder._comments;

	/// <summary>
	/// Gets whether the previous short sample count was due to a parameter change in the stream.
	/// </summary>
	public bool IsParameterChange => ActiveDecoder.IsParameterChange;

	/// <summary>
	/// Gets the number of bits read that are related to framing and transport alone
	/// </summary>
	public long ContainerOverheadBits => ActiveDecoder.ContainerBits;

	/// <summary>
	/// Gets or sets whether to automatically apply clipping to samples returned by <see cref="M:NVorbis.VorbisReader.ReadSamples(System.Single[],System.Int32,System.Int32)" />.
	/// </summary>
	public bool ClipSamples { get; set; }

	/// <summary>
	/// Gets stats from each decoder stream available
	/// </summary>
	public IVorbisStreamStatus[] Stats => _decoders.Select((VorbisStreamDecoder d) => d).Cast<IVorbisStreamStatus>().ToArray();

	/// <summary>
	/// Gets the currently-selected stream's index
	/// </summary>
	public int StreamIndex => _streamIdx;

	/// <summary>
	/// Returns the number of logical streams found so far in the physical container
	/// </summary>
	public int StreamCount => _decoders.Count;

	/// <summary>
	/// Gets or Sets the current timestamp of the decoder.  Is the timestamp before the next sample to be decoded
	/// </summary>
	public TimeSpan DecodedTime
	{
		get
		{
			return TimeSpan.FromSeconds((double)ActiveDecoder.CurrentPosition / (double)SampleRate);
		}
		set
		{
			ActiveDecoder.SeekTo((long)(value.TotalSeconds * (double)SampleRate));
		}
	}

	/// <summary>
	/// Gets or Sets the current position of the next sample to be decoded.
	/// </summary>
	public long DecodedPosition
	{
		get
		{
			return ActiveDecoder.CurrentPosition;
		}
		set
		{
			ActiveDecoder.SeekTo(value);
		}
	}

	/// <summary>
	/// Gets the total length of the current logical stream
	/// </summary>
	public TimeSpan TotalTime
	{
		get
		{
			VorbisStreamDecoder activeDecoder = ActiveDecoder;
			if (activeDecoder.CanSeek)
			{
				return TimeSpan.FromSeconds((double)activeDecoder.GetLastGranulePos() / (double)activeDecoder._sampleRate);
			}
			return TimeSpan.MaxValue;
		}
	}

	public long TotalSamples
	{
		get
		{
			VorbisStreamDecoder activeDecoder = ActiveDecoder;
			if (activeDecoder.CanSeek)
			{
				return activeDecoder.GetLastGranulePos();
			}
			return long.MaxValue;
		}
	}

	private VorbisReader()
	{
		ClipSamples = true;
		_decoders = new List<VorbisStreamDecoder>();
		_serials = new List<int>();
	}

	public VorbisReader(string fileName)
		: this(File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read), closeStreamOnDispose: true)
	{
	}

	public VorbisReader(Stream stream, bool closeStreamOnDispose)
		: this()
	{
		BufferedReadStream bufferedReadStream = new BufferedReadStream(stream)
		{
			CloseBaseStream = closeStreamOnDispose
		};
		ContainerReader containerReader = new ContainerReader(bufferedReadStream, closeStreamOnDispose);
		if (!LoadContainer(containerReader))
		{
			bufferedReadStream.Close();
			throw new InvalidDataException("Could not determine container type!");
		}
		_containerReader = containerReader;
		if (_decoders.Count == 0)
		{
			throw new InvalidDataException("No Vorbis data found!");
		}
	}

	public VorbisReader(IContainerReader containerReader)
		: this()
	{
		if (!LoadContainer(containerReader))
		{
			throw new InvalidDataException("Container did not initialize!");
		}
		_containerReader = containerReader;
		if (_decoders.Count == 0)
		{
			throw new InvalidDataException("No Vorbis data found!");
		}
	}

	public VorbisReader(IPacketProvider packetProvider)
		: this()
	{
		NewStreamEventArgs e = new NewStreamEventArgs(packetProvider);
		NewStream(this, e);
		if (e.IgnoreStream)
		{
			throw new InvalidDataException("No Vorbis data found!");
		}
	}

	private bool LoadContainer(IContainerReader containerReader)
	{
		containerReader.NewStream += NewStream;
		if (!containerReader.Init())
		{
			containerReader.NewStream -= NewStream;
			return false;
		}
		return true;
	}

	private void NewStream(object sender, NewStreamEventArgs ea)
	{
		IPacketProvider packetProvider = ea.PacketProvider;
		VorbisStreamDecoder vorbisStreamDecoder = new VorbisStreamDecoder(packetProvider);
		if (vorbisStreamDecoder.TryInit())
		{
			_decoders.Add(vorbisStreamDecoder);
			_serials.Add(packetProvider.StreamSerial);
		}
		else
		{
			ea.IgnoreStream = true;
		}
	}

	public void Dispose()
	{
		if (_decoders != null)
		{
			foreach (VorbisStreamDecoder decoder in _decoders)
			{
				decoder.Dispose();
			}
			_decoders.Clear();
			_decoders = null;
		}
		if (_containerReader != null)
		{
			_containerReader.NewStream -= NewStream;
			_containerReader.Dispose();
			_containerReader = null;
		}
	}

	/// <summary>
	/// Reads decoded samples from the current logical stream
	/// </summary>
	/// <param name="buffer">The buffer to write the samples to</param>
	/// <param name="offset">The offset into the buffer to write the samples to</param>
	/// <param name="count">The number of samples to write</param>
	/// <returns>The number of samples written</returns>
	public int ReadSamples(float[] buffer, int offset, int count)
	{
		if (offset < 0)
		{
			throw new ArgumentOutOfRangeException("offset");
		}
		if (count < 0 || offset + count > buffer.Length)
		{
			throw new ArgumentOutOfRangeException("count");
		}
		count = ActiveDecoder.ReadSamples(buffer, offset, count);
		if (ClipSamples)
		{
			VorbisStreamDecoder vorbisStreamDecoder = _decoders[_streamIdx];
			int num = 0;
			while (num < count)
			{
				buffer[offset] = Utils.ClipValue(buffer[offset], ref vorbisStreamDecoder._clipped);
				num++;
				offset++;
			}
		}
		return count;
	}

	/// <summary>
	/// Clears the parameter change flag so further samples can be requested.
	/// </summary>
	public void ClearParameterChange()
	{
		ActiveDecoder.IsParameterChange = false;
	}

	/// <summary>
	/// Searches for the next stream in a concatenated file
	/// </summary>
	/// <returns><c>True</c> if a new stream was found, otherwise <c>false</c>.</returns>
	public bool FindNextStream()
	{
		if (_containerReader == null)
		{
			return false;
		}
		return _containerReader.FindNextStream();
	}

	/// <summary>
	/// Switches to an alternate logical stream.
	/// </summary>
	/// <param name="index">The logical stream index to switch to</param>
	/// <returns><c>True</c> if the properties of the logical stream differ from those of the one previously being decoded. Otherwise, <c>False</c>.</returns>
	public bool SwitchStreams(int index)
	{
		if (index < 0 || index >= StreamCount)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		if (_decoders == null)
		{
			throw new ObjectDisposedException("VorbisReader");
		}
		if (_streamIdx == index)
		{
			return false;
		}
		VorbisStreamDecoder vorbisStreamDecoder = _decoders[_streamIdx];
		_streamIdx = index;
		VorbisStreamDecoder vorbisStreamDecoder2 = _decoders[_streamIdx];
		if (vorbisStreamDecoder._channels == vorbisStreamDecoder2._channels)
		{
			return vorbisStreamDecoder._sampleRate != vorbisStreamDecoder2._sampleRate;
		}
		return true;
	}
}
