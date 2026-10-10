using System.IO;

namespace NVorbis;

internal abstract class VorbisMapping
{
	private class Mapping0 : VorbisMapping
	{
		internal Mapping0(VorbisStreamDecoder vorbis)
			: base(vorbis)
		{
		}

		protected override void Init(DataPacket packet)
		{
			int num = 1;
			if (packet.ReadBit())
			{
				num += (int)packet.ReadBits(4);
			}
			int num2 = 0;
			if (packet.ReadBit())
			{
				num2 = (int)packet.ReadBits(8) + 1;
			}
			int count = Utils.ilog(_vorbis._channels - 1);
			CouplingSteps = new CouplingStep[num2];
			for (int i = 0; i < num2; i++)
			{
				int num3 = (int)packet.ReadBits(count);
				int num4 = (int)packet.ReadBits(count);
				if (num3 == num4 || num3 > _vorbis._channels - 1 || num4 > _vorbis._channels - 1)
				{
					throw new InvalidDataException();
				}
				CouplingSteps[i] = new CouplingStep
				{
					Angle = num4,
					Magnitude = num3
				};
			}
			if (packet.ReadBits(2) != 0L)
			{
				throw new InvalidDataException();
			}
			int[] array = new int[_vorbis._channels];
			if (num > 1)
			{
				for (int j = 0; j < ChannelSubmap.Length; j++)
				{
					array[j] = (int)packet.ReadBits(4);
					if (array[j] >= num)
					{
						throw new InvalidDataException();
					}
				}
			}
			Submaps = new Submap[num];
			for (int k = 0; k < num; k++)
			{
				packet.ReadBits(8);
				int num5 = (int)packet.ReadBits(8);
				if (num5 >= _vorbis.Floors.Length)
				{
					throw new InvalidDataException();
				}
				if ((int)packet.ReadBits(8) >= _vorbis.Residues.Length)
				{
					throw new InvalidDataException();
				}
				Submaps[k] = new Submap
				{
					Floor = _vorbis.Floors[num5],
					Residue = _vorbis.Residues[num5]
				};
			}
			ChannelSubmap = new Submap[_vorbis._channels];
			for (int l = 0; l < ChannelSubmap.Length; l++)
			{
				ChannelSubmap[l] = Submaps[array[l]];
			}
		}
	}

	internal class Submap
	{
		internal VorbisFloor Floor;

		internal VorbisResidue Residue;

		internal Submap()
		{
		}
	}

	internal class CouplingStep
	{
		internal int Magnitude;

		internal int Angle;

		internal CouplingStep()
		{
		}
	}

	private VorbisStreamDecoder _vorbis;

	internal Submap[] Submaps;

	internal Submap[] ChannelSubmap;

	internal CouplingStep[] CouplingSteps;

	internal static VorbisMapping Init(VorbisStreamDecoder vorbis, DataPacket packet)
	{
		int num = (int)packet.ReadBits(16);
		VorbisMapping vorbisMapping = null;
		if (num == 0)
		{
			vorbisMapping = new Mapping0(vorbis);
		}
		if (vorbisMapping == null)
		{
			throw new InvalidDataException();
		}
		vorbisMapping.Init(packet);
		return vorbisMapping;
	}

	protected VorbisMapping(VorbisStreamDecoder vorbis)
	{
		_vorbis = vorbis;
	}

	protected abstract void Init(DataPacket packet);
}
