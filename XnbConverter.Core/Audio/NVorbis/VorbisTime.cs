using System.IO;

namespace NVorbis;

internal abstract class VorbisTime
{
	private class Time0 : VorbisTime
	{
		internal Time0(VorbisStreamDecoder vorbis)
			: base(vorbis)
		{
		}

		protected override void Init(DataPacket packet)
		{
		}
	}

	private VorbisStreamDecoder _vorbis;

	internal static VorbisTime Init(VorbisStreamDecoder vorbis, DataPacket packet)
	{
		int num = (int)packet.ReadBits(16);
		VorbisTime vorbisTime = null;
		if (num == 0)
		{
			vorbisTime = new Time0(vorbis);
		}
		if (vorbisTime == null)
		{
			throw new InvalidDataException();
		}
		vorbisTime.Init(packet);
		return vorbisTime;
	}

	protected VorbisTime(VorbisStreamDecoder vorbis)
	{
		_vorbis = vorbis;
	}

	protected abstract void Init(DataPacket packet);
}
