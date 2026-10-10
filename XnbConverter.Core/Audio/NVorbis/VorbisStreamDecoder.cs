using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace NVorbis;

internal class VorbisStreamDecoder : IVorbisStreamStatus, IDisposable
{
	internal int _upperBitrate;

	internal int _nominalBitrate;

	internal int _lowerBitrate;

	internal string _vendor;

	internal string[] _comments;

	internal int _channels;

	internal int _sampleRate;

	internal int Block0Size;

	internal int Block1Size;

	internal VorbisCodebook[] Books;

	internal VorbisTime[] Times;

	internal VorbisFloor[] Floors;

	internal VorbisResidue[] Residues;

	internal VorbisMapping[] Maps;

	internal VorbisMode[] Modes;

	private int _modeFieldBits;

	internal long _glueBits;

	internal long _metaBits;

	internal long _bookBits;

	internal long _timeHdrBits;

	internal long _floorHdrBits;

	internal long _resHdrBits;

	internal long _mapHdrBits;

	internal long _modeHdrBits;

	internal long _wasteHdrBits;

	internal long _modeBits;

	internal long _floorBits;

	internal long _resBits;

	internal long _wasteBits;

	internal long _samples;

	internal int _packetCount;

	internal Stopwatch _sw = new Stopwatch();

	private IPacketProvider _packetProvider;

	private DataPacket _parameterChangePacket;

	private List<int> _pagesSeen;

	private int _lastPageSeen;

	private bool _eosFound;

	private object _seekLock = new object();

	private float[] _prevBuffer;

	private RingBuffer _outputBuffer;

	private Queue<int> _bitsPerPacketHistory;

	private Queue<int> _sampleCountHistory;

	private int _preparedLength;

	internal bool _clipped;

	private Stack<DataPacket> _resyncQueue;

	private long _currentPosition;

	private long _reportedPosition;

	private VorbisMode _mode;

	private bool _prevFlag;

	private bool _nextFlag;

	private bool[] _noExecuteChannel;

	private VorbisFloor.PacketData[] _floorData;

	private float[][] _residue;

	private bool _isParameterChange;

	internal bool IsParameterChange
	{
		get
		{
			return _isParameterChange;
		}
		set
		{
			if (value)
			{
				throw new InvalidOperationException("Only clearing is supported!");
			}
			_isParameterChange = value;
		}
	}

	internal bool CanSeek => _packetProvider.CanSeek;

	internal long CurrentPosition
	{
		get
		{
			return _reportedPosition;
		}
		private set
		{
			_reportedPosition = value;
			_currentPosition = value;
			_preparedLength = 0;
			_eosFound = false;
			ResetDecoder(isFullReset: false);
			_prevBuffer = null;
		}
	}

	internal long ContainerBits => _packetProvider.ContainerBits;

	public int EffectiveBitRate
	{
		get
		{
			if (_samples == 0L)
			{
				return 0;
			}
			double num = (double)(_currentPosition - _preparedLength) / (double)_sampleRate;
			return (int)((double)AudioBits / num);
		}
	}

	public int InstantBitRate
	{
		get
		{
			int num = _sampleCountHistory.Sum();
			if (num > 0)
			{
				return (int)((long)_bitsPerPacketHistory.Sum() * (long)_sampleRate / num);
			}
			return -1;
		}
	}

	public TimeSpan PageLatency => TimeSpan.FromTicks(_sw.ElapsedTicks / PagesRead);

	public TimeSpan PacketLatency => TimeSpan.FromTicks(_sw.ElapsedTicks / _packetCount);

	public TimeSpan SecondLatency => TimeSpan.FromTicks(_sw.ElapsedTicks / _samples * _sampleRate);

	public long OverheadBits => _glueBits + _metaBits + _timeHdrBits + _wasteHdrBits + _wasteBits + _packetProvider.ContainerBits;

	public long AudioBits => _bookBits + _floorHdrBits + _resHdrBits + _mapHdrBits + _modeHdrBits + _modeBits + _floorBits + _resBits;

	public int PagesRead => _pagesSeen.IndexOf(_lastPageSeen) + 1;

	public int TotalPages => _packetProvider.GetTotalPageCount();

	public bool Clipped => _clipped;

	internal VorbisStreamDecoder(IPacketProvider packetProvider)
	{
		_packetProvider = packetProvider;
		_packetProvider.ParameterChange += SetParametersChanging;
		_pagesSeen = new List<int>();
		_lastPageSeen = -1;
	}

	internal bool TryInit()
	{
		if (!ProcessStreamHeader(_packetProvider.PeekNextPacket()))
		{
			return false;
		}
		_packetProvider.GetNextPacket().Done();
		DataPacket nextPacket = _packetProvider.GetNextPacket();
		if (!LoadComments(nextPacket))
		{
			throw new InvalidDataException("Comment header was not readable!");
		}
		nextPacket.Done();
		nextPacket = _packetProvider.GetNextPacket();
		if (!LoadBooks(nextPacket))
		{
			throw new InvalidDataException("Book header was not readable!");
		}
		nextPacket.Done();
		InitDecoder();
		return true;
	}

	private void SetParametersChanging(object sender, ParameterChangeEventArgs e)
	{
		_parameterChangePacket = e.FirstPacket;
	}

	public void Dispose()
	{
		if (_packetProvider != null)
		{
			IPacketProvider packetProvider = _packetProvider;
			_packetProvider = null;
			packetProvider.ParameterChange -= SetParametersChanging;
			packetProvider.Dispose();
		}
	}

	private void ProcessParameterChange(DataPacket packet)
	{
		_parameterChangePacket = null;
		bool flag = false;
		bool isFullReset = false;
		if (ProcessStreamHeader(packet))
		{
			packet.Done();
			flag = true;
			isFullReset = true;
			packet = _packetProvider.PeekNextPacket();
			if (packet == null)
			{
				throw new InvalidDataException("Couldn't get next packet!");
			}
		}
		if (LoadComments(packet))
		{
			if (flag)
			{
				_packetProvider.GetNextPacket().Done();
			}
			else
			{
				packet.Done();
			}
			flag = true;
			packet = _packetProvider.PeekNextPacket();
			if (packet == null)
			{
				throw new InvalidDataException("Couldn't get next packet!");
			}
		}
		if (LoadBooks(packet))
		{
			if (flag)
			{
				_packetProvider.GetNextPacket().Done();
			}
			else
			{
				packet.Done();
			}
		}
		ResetDecoder(isFullReset);
	}

	private bool ProcessStreamHeader(DataPacket packet)
	{
		if (!Enumerable.SequenceEqual(packet.ReadBytes(7), new byte[7] { 1, 118, 111, 114, 98, 105, 115 }))
		{
			_glueBits += packet.Length * 8;
			return false;
		}
		if (!_pagesSeen.Contains(_lastPageSeen = packet.PageSequenceNumber))
		{
			_pagesSeen.Add(_lastPageSeen);
		}
		_glueBits += 56L;
		long bitsRead = packet.BitsRead;
		if (packet.ReadInt32() != 0)
		{
			throw new InvalidDataException("Only Vorbis stream version 0 is supported.");
		}
		_channels = packet.ReadByte();
		_sampleRate = packet.ReadInt32();
		_upperBitrate = packet.ReadInt32();
		_nominalBitrate = packet.ReadInt32();
		_lowerBitrate = packet.ReadInt32();
		Block0Size = 1 << (int)packet.ReadBits(4);
		Block1Size = 1 << (int)packet.ReadBits(4);
		if (_nominalBitrate == 0 && _upperBitrate > 0 && _lowerBitrate > 0)
		{
			_nominalBitrate = (_upperBitrate + _lowerBitrate) / 2;
		}
		_metaBits += packet.BitsRead - bitsRead + 8;
		_wasteHdrBits += 8 * packet.Length - packet.BitsRead;
		return true;
	}

	private bool LoadComments(DataPacket packet)
	{
		if (!Enumerable.SequenceEqual(packet.ReadBytes(7), new byte[7] { 3, 118, 111, 114, 98, 105, 115 }))
		{
			return false;
		}
		if (!_pagesSeen.Contains(_lastPageSeen = packet.PageSequenceNumber))
		{
			_pagesSeen.Add(_lastPageSeen);
		}
		_glueBits += 56L;
		_vendor = Encoding.UTF8.GetString(packet.ReadBytes(packet.ReadInt32()));
		_comments = new string[packet.ReadInt32()];
		for (int i = 0; i < _comments.Length; i++)
		{
			_comments[i] = Encoding.UTF8.GetString(packet.ReadBytes(packet.ReadInt32()));
		}
		_metaBits += packet.BitsRead - 56;
		_wasteHdrBits += 8 * packet.Length - packet.BitsRead;
		return true;
	}

	private bool LoadBooks(DataPacket packet)
	{
		if (!Enumerable.SequenceEqual(packet.ReadBytes(7), new byte[7] { 5, 118, 111, 114, 98, 105, 115 }))
		{
			return false;
		}
		if (!_pagesSeen.Contains(_lastPageSeen = packet.PageSequenceNumber))
		{
			_pagesSeen.Add(_lastPageSeen);
		}
		long bitsRead = packet.BitsRead;
		_glueBits += packet.BitsRead;
		Books = new VorbisCodebook[packet.ReadByte() + 1];
		for (int i = 0; i < Books.Length; i++)
		{
			Books[i] = VorbisCodebook.Init(this, packet, i);
		}
		_bookBits += packet.BitsRead - bitsRead;
		bitsRead = packet.BitsRead;
		Times = new VorbisTime[(int)packet.ReadBits(6) + 1];
		for (int j = 0; j < Times.Length; j++)
		{
			Times[j] = VorbisTime.Init(this, packet);
		}
		_timeHdrBits += packet.BitsRead - bitsRead;
		bitsRead = packet.BitsRead;
		Floors = new VorbisFloor[(int)packet.ReadBits(6) + 1];
		for (int k = 0; k < Floors.Length; k++)
		{
			Floors[k] = VorbisFloor.Init(this, packet);
		}
		_floorHdrBits += packet.BitsRead - bitsRead;
		bitsRead = packet.BitsRead;
		Residues = new VorbisResidue[(int)packet.ReadBits(6) + 1];
		for (int l = 0; l < Residues.Length; l++)
		{
			Residues[l] = VorbisResidue.Init(this, packet);
		}
		_resHdrBits += packet.BitsRead - bitsRead;
		bitsRead = packet.BitsRead;
		Maps = new VorbisMapping[(int)packet.ReadBits(6) + 1];
		for (int m = 0; m < Maps.Length; m++)
		{
			Maps[m] = VorbisMapping.Init(this, packet);
		}
		_mapHdrBits += packet.BitsRead - bitsRead;
		bitsRead = packet.BitsRead;
		Modes = new VorbisMode[(int)packet.ReadBits(6) + 1];
		for (int n = 0; n < Modes.Length; n++)
		{
			Modes[n] = VorbisMode.Init(this, packet);
		}
		_modeHdrBits += packet.BitsRead - bitsRead;
		if (!packet.ReadBit())
		{
			throw new InvalidDataException();
		}
		_glueBits++;
		_wasteHdrBits += 8 * packet.Length - packet.BitsRead;
		_modeFieldBits = Utils.ilog(Modes.Length - 1);
		return true;
	}

	private void InitDecoder()
	{
		_currentPosition = 0L;
		_resyncQueue = new Stack<DataPacket>();
		_bitsPerPacketHistory = new Queue<int>();
		_sampleCountHistory = new Queue<int>();
		ResetDecoder(isFullReset: true);
	}

	private void ResetDecoder(bool isFullReset)
	{
		if (_preparedLength > 0)
		{
			SaveBuffer();
		}
		if (isFullReset)
		{
			_noExecuteChannel = new bool[_channels];
			_floorData = new VorbisFloor.PacketData[_channels];
			_residue = new float[_channels][];
			for (int i = 0; i < _channels; i++)
			{
				_residue[i] = new float[Block1Size];
			}
			_outputBuffer = new RingBuffer(Block1Size * 2 * _channels);
			_outputBuffer.Channels = _channels;
		}
		else
		{
			_outputBuffer.Clear();
		}
		_preparedLength = 0;
	}

	private void SaveBuffer()
	{
		float[] array = new float[_preparedLength * _channels];
		ReadSamples(array, 0, array.Length);
		_prevBuffer = array;
	}

	private bool UnpackPacket(DataPacket packet)
	{
		if (packet.ReadBit())
		{
			return false;
		}
		int num = _modeFieldBits;
		_mode = Modes[(uint)packet.ReadBits(_modeFieldBits)];
		if (_mode.BlockFlag)
		{
			_prevFlag = packet.ReadBit();
			_nextFlag = packet.ReadBit();
			num += 2;
		}
		else
		{
			_prevFlag = (_nextFlag = false);
		}
		if (packet.IsShort)
		{
			return false;
		}
		long bitsRead = packet.BitsRead;
		int num2 = _mode.BlockSize / 2;
		for (int i = 0; i < _channels; i++)
		{
			_floorData[i] = _mode.Mapping.ChannelSubmap[i].Floor.UnpackPacket(packet, _mode.BlockSize, i);
			_noExecuteChannel[i] = !_floorData[i].ExecuteChannel;
			Array.Clear(_residue[i], 0, num2);
		}
		VorbisMapping.CouplingStep[] couplingSteps = _mode.Mapping.CouplingSteps;
		foreach (VorbisMapping.CouplingStep couplingStep in couplingSteps)
		{
			if (_floorData[couplingStep.Angle].ExecuteChannel || _floorData[couplingStep.Magnitude].ExecuteChannel)
			{
				_floorData[couplingStep.Angle].ForceEnergy = true;
				_floorData[couplingStep.Magnitude].ForceEnergy = true;
			}
		}
		long num3 = packet.BitsRead - bitsRead;
		bitsRead = packet.BitsRead;
		VorbisMapping.Submap[] submaps = _mode.Mapping.Submaps;
		foreach (VorbisMapping.Submap submap in submaps)
		{
			for (int k = 0; k < _channels; k++)
			{
				if (_mode.Mapping.ChannelSubmap[k] != submap)
				{
					_floorData[k].ForceNoEnergy = true;
				}
			}
			float[][] array = submap.Residue.Decode(packet, _noExecuteChannel, _channels, _mode.BlockSize);
			for (int l = 0; l < _channels; l++)
			{
				float[] array2 = _residue[l];
				float[] array3 = array[l];
				for (int m = 0; m < num2; m++)
				{
					array2[m] += array3[m];
				}
			}
		}
		_glueBits++;
		_modeBits += num;
		_floorBits += num3;
		_resBits += packet.BitsRead - bitsRead;
		_wasteBits += 8 * packet.Length - packet.BitsRead;
		_packetCount++;
		return true;
	}

	private void DecodePacket()
	{
		VorbisMapping.CouplingStep[] couplingSteps = _mode.Mapping.CouplingSteps;
		int num = _mode.BlockSize / 2;
		for (int num2 = couplingSteps.Length - 1; num2 >= 0; num2--)
		{
			if (_floorData[couplingSteps[num2].Angle].ExecuteChannel || _floorData[couplingSteps[num2].Magnitude].ExecuteChannel)
			{
				float[] array = _residue[couplingSteps[num2].Magnitude];
				float[] array2 = _residue[couplingSteps[num2].Angle];
				for (int i = 0; i < num; i++)
				{
					float num3;
					float num4;
					if (array[i] > 0f)
					{
						if (array2[i] > 0f)
						{
							num3 = array[i];
							num4 = array[i] - array2[i];
						}
						else
						{
							num4 = array[i];
							num3 = array[i] + array2[i];
						}
					}
					else if (array2[i] > 0f)
					{
						num3 = array[i];
						num4 = array[i] + array2[i];
					}
					else
					{
						num4 = array[i];
						num3 = array[i] - array2[i];
					}
					array[i] = num3;
					array2[i] = num4;
				}
			}
		}
		for (int j = 0; j < _channels; j++)
		{
			VorbisFloor.PacketData packetData = _floorData[j];
			float[] array3 = _residue[j];
			if (packetData.ExecuteChannel)
			{
				_mode.Mapping.ChannelSubmap[j].Floor.Apply(packetData, array3);
				Mdct.Reverse(array3, _mode.BlockSize);
			}
			else
			{
				Array.Clear(array3, num, num);
			}
		}
	}

	private int OverlapSamples()
	{
		float[] window = _mode.GetWindow(_prevFlag, _nextFlag);
		int blockSize = _mode.BlockSize;
		int num = blockSize;
		int num2 = num >> 1;
		int num3 = 0;
		int num4 = -num2;
		int num5 = num2;
		if (_mode.BlockFlag)
		{
			if (!_prevFlag)
			{
				num3 = Block1Size / 4 - Block0Size / 4;
				num2 = num3 + Block0Size / 2;
				num4 = Block0Size / -2 - num3;
			}
			if (!_nextFlag)
			{
				num -= blockSize / 4 - Block0Size / 4;
				num5 = blockSize / 4 + Block0Size / 4;
			}
		}
		int index = _outputBuffer.Length / _channels + num4;
		for (int i = 0; i < _channels; i++)
		{
			_outputBuffer.Write(i, index, num3, num2, num, _residue[i], window);
		}
		int num6 = _outputBuffer.Length / _channels - num5;
		int result = num6 - _preparedLength;
		_preparedLength = num6;
		return result;
	}

	private void UpdatePosition(int samplesDecoded, DataPacket packet)
	{
		_samples += samplesDecoded;
		if (packet.IsResync)
		{
			_currentPosition = -packet.PageGranulePosition;
			_resyncQueue.Push(packet);
		}
		else
		{
			if (samplesDecoded <= 0)
			{
				return;
			}
			_currentPosition += samplesDecoded;
			packet.GranulePosition = _currentPosition;
			if (_currentPosition < 0)
			{
				if (packet.PageGranulePosition > -_currentPosition)
				{
					long num = _currentPosition - samplesDecoded;
					while (_resyncQueue.Count > 0)
					{
						DataPacket dataPacket = _resyncQueue.Pop();
						long num2 = dataPacket.GranulePosition + num;
						dataPacket.GranulePosition = num;
						num = num2;
					}
				}
				else
				{
					packet.GranulePosition = -samplesDecoded;
					_resyncQueue.Push(packet);
				}
			}
			else if (packet.IsEndOfStream && _currentPosition > packet.PageGranulePosition)
			{
				int num3 = (int)(_currentPosition - packet.PageGranulePosition);
				if (num3 >= 0)
				{
					_preparedLength -= num3;
					_currentPosition -= num3;
				}
				else
				{
					_preparedLength = 0;
				}
				packet.GranulePosition = packet.PageGranulePosition;
				_eosFound = true;
			}
		}
	}

	private void DecodeNextPacket()
	{
		_sw.Start();
		DataPacket dataPacket = null;
		try
		{
			IPacketProvider packetProvider = _packetProvider;
			if (packetProvider != null)
			{
				dataPacket = packetProvider.GetNextPacket();
			}
			if (dataPacket == null)
			{
				_eosFound = true;
				return;
			}
			if (!_pagesSeen.Contains(_lastPageSeen = dataPacket.PageSequenceNumber))
			{
				_pagesSeen.Add(_lastPageSeen);
			}
			if (dataPacket.IsResync)
			{
				ResetDecoder(isFullReset: false);
			}
			if (dataPacket == _parameterChangePacket)
			{
				_isParameterChange = true;
				ProcessParameterChange(dataPacket);
				return;
			}
			if (!UnpackPacket(dataPacket))
			{
				dataPacket.Done();
				_wasteBits += 8 * dataPacket.Length;
				return;
			}
			dataPacket.Done();
			DecodePacket();
			int num = OverlapSamples();
			if (!dataPacket.GranuleCount.HasValue)
			{
				dataPacket.GranuleCount = num;
			}
			UpdatePosition(num, dataPacket);
			int num2 = Utils.Sum(_sampleCountHistory) + num;
			_bitsPerPacketHistory.Enqueue((int)dataPacket.BitsRead);
			_sampleCountHistory.Enqueue(num);
			while (num2 > _sampleRate)
			{
				_bitsPerPacketHistory.Dequeue();
				num2 -= _sampleCountHistory.Dequeue();
			}
		}
		catch
		{
			dataPacket?.Done();
			throw;
		}
		finally
		{
			_sw.Stop();
		}
	}

	internal int GetPacketLength(DataPacket curPacket, DataPacket lastPacket)
	{
		if (lastPacket == null || curPacket.IsResync)
		{
			return 0;
		}
		if (curPacket.ReadBit())
		{
			return 0;
		}
		if (lastPacket.ReadBit())
		{
			return 0;
		}
		int num = (int)curPacket.ReadBits(_modeFieldBits);
		if (num < 0 || num >= Modes.Length)
		{
			return 0;
		}
		VorbisMode vorbisMode = Modes[num];
		num = (int)lastPacket.ReadBits(_modeFieldBits);
		if (num < 0 || num >= Modes.Length)
		{
			return 0;
		}
		VorbisMode vorbisMode2 = Modes[num];
		return vorbisMode.BlockSize / 4 + vorbisMode2.BlockSize / 4;
	}

	internal int ReadSamples(float[] buffer, int offset, int count)
	{
		int num = 0;
		lock (_seekLock)
		{
			if (_prevBuffer != null)
			{
				int num2 = Math.Min(count, _prevBuffer.Length);
				Buffer.BlockCopy(_prevBuffer, 0, buffer, offset, num2 * 4);
				if (num2 < _prevBuffer.Length)
				{
					float[] array = new float[_prevBuffer.Length - num2];
					Buffer.BlockCopy(_prevBuffer, num2 * 4, array, 0, (_prevBuffer.Length - num2) * 4);
					_prevBuffer = array;
				}
				else
				{
					_prevBuffer = null;
				}
				count -= num2;
				offset += num2;
				num = num2;
			}
			else if (_isParameterChange)
			{
				throw new InvalidOperationException("Currently pending a parameter change.  Read new parameters before requesting further samples!");
			}
			int size = count + Block1Size * _channels;
			_outputBuffer.EnsureSize(size);
			while (_preparedLength * _channels < count && !_eosFound && !_isParameterChange)
			{
				DecodeNextPacket();
				if (_prevBuffer != null)
				{
					return ReadSamples(buffer, offset, _prevBuffer.Length);
				}
			}
			if (_preparedLength * _channels < count)
			{
				count = _preparedLength * _channels;
			}
			_outputBuffer.CopyTo(buffer, offset, count);
			_preparedLength -= count / _channels;
			_reportedPosition = _currentPosition - _preparedLength;
		}
		return num + count;
	}

	internal void SeekTo(long granulePos)
	{
		if (!_packetProvider.CanSeek)
		{
			throw new NotSupportedException();
		}
		if (granulePos < 0)
		{
			throw new ArgumentOutOfRangeException("granulePos");
		}
		DataPacket dataPacket;
		if (granulePos > 0)
		{
			dataPacket = _packetProvider.FindPacket(granulePos, GetPacketLength);
			if (dataPacket == null)
			{
				throw new ArgumentOutOfRangeException("granulePos");
			}
		}
		else
		{
			dataPacket = _packetProvider.GetPacket(4);
		}
		lock (_seekLock)
		{
			_packetProvider.SeekToPacket(dataPacket, 1);
			DataPacket dataPacket2 = _packetProvider.PeekNextPacket();
			CurrentPosition = dataPacket2.GranulePosition;
			int num = (int)((granulePos - CurrentPosition) * _channels);
			if (num <= 0)
			{
				return;
			}
			float[] buffer = new float[num];
			while (num > 0)
			{
				int num2 = ReadSamples(buffer, 0, num);
				if (num2 == 0)
				{
					break;
				}
				num -= num2;
			}
		}
	}

	internal long GetLastGranulePos()
	{
		return _packetProvider.GetGranuleCount();
	}

	public void ResetStats()
	{
		_clipped = false;
		_packetCount = 0;
		_floorBits = 0L;
		_glueBits = 0L;
		_modeBits = 0L;
		_resBits = 0L;
		_wasteBits = 0L;
		_samples = 0L;
		_sw.Reset();
	}
}
