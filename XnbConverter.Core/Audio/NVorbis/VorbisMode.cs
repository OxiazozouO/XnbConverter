using System;
using System.IO;

namespace NVorbis;

internal class VorbisMode
{
	private const float M_PI = (float)Math.PI;

	private const float M_PI2 = (float)Math.PI / 2f;

	private VorbisStreamDecoder _vorbis;

	private float[][] _windows;

	internal bool BlockFlag;

	internal int WindowType;

	internal int TransformType;

	internal VorbisMapping Mapping;

	internal int BlockSize;

	internal static VorbisMode Init(VorbisStreamDecoder vorbis, DataPacket packet)
	{
		VorbisMode vorbisMode = new VorbisMode(vorbis);
		vorbisMode.BlockFlag = packet.ReadBit();
		vorbisMode.WindowType = (int)packet.ReadBits(16);
		vorbisMode.TransformType = (int)packet.ReadBits(16);
		int num = (int)packet.ReadBits(8);
		if (vorbisMode.WindowType != 0 || vorbisMode.TransformType != 0 || num >= vorbis.Maps.Length)
		{
			throw new InvalidDataException();
		}
		vorbisMode.Mapping = vorbis.Maps[num];
		vorbisMode.BlockSize = (vorbisMode.BlockFlag ? vorbis.Block1Size : vorbis.Block0Size);
		if (vorbisMode.BlockFlag)
		{
			vorbisMode._windows = new float[4][];
			vorbisMode._windows[0] = new float[vorbis.Block1Size];
			vorbisMode._windows[1] = new float[vorbis.Block1Size];
			vorbisMode._windows[2] = new float[vorbis.Block1Size];
			vorbisMode._windows[3] = new float[vorbis.Block1Size];
		}
		else
		{
			vorbisMode._windows = new float[1][];
			vorbisMode._windows[0] = new float[vorbis.Block0Size];
		}
		vorbisMode.CalcWindows();
		return vorbisMode;
	}

	private VorbisMode(VorbisStreamDecoder vorbis)
	{
		_vorbis = vorbis;
	}

	private void CalcWindows()
	{
		for (int i = 0; i < _windows.Length; i++)
		{
			float[] array = _windows[i];
			int num = (((i & 1) == 0) ? _vorbis.Block0Size : _vorbis.Block1Size) / 2;
			int blockSize = BlockSize;
			int num2 = (((i & 2) == 0) ? _vorbis.Block0Size : _vorbis.Block1Size) / 2;
			int num3 = blockSize / 4 - num / 2;
			int num4 = blockSize - blockSize / 4 - num2 / 2;
			for (int j = 0; j < num; j++)
			{
				float num5 = (float)Math.Sin(((double)j + 0.5) / (double)num * 1.5707963705062866);
				num5 *= num5;
				array[num3 + j] = (float)Math.Sin(num5 * ((float)Math.PI / 2f));
			}
			for (int k = num3 + num; k < num4; k++)
			{
				array[k] = 1f;
			}
			for (int l = 0; l < num2; l++)
			{
				float num6 = (float)Math.Sin(((double)(num2 - l) - 0.5) / (double)num2 * 1.5707963705062866);
				num6 *= num6;
				array[num4 + l] = (float)Math.Sin(num6 * ((float)Math.PI / 2f));
			}
		}
	}

	internal float[] GetWindow(bool prev, bool next)
	{
		if (BlockFlag)
		{
			if (next)
			{
				if (prev)
				{
					return _windows[3];
				}
				return _windows[2];
			}
			if (prev)
			{
				return _windows[1];
			}
		}
		return _windows[0];
	}
}
