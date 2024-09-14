using System;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public class ColourSet : IDisposable
{
	private readonly bool IsDxt1;

	private readonly int[] Remap = Pool.RentNewInt(16);

	private readonly bool WeightByAlpha;

	public int Count;

	public bool IsTransparent;

	public Vector3[] Points = Pool.RentVector3(16);

	public float[] Weights = Pool.RentFloat(16);

	public ColourSet(bool isDxt1, bool weightByAlpha)
	{
		IsDxt1 = isDxt1;
		WeightByAlpha = weightByAlpha;
	}

	public void Dispose()
	{
		Pool.Return(Weights);
		Pool.Return(Remap);
		Pool.Return(Points);
	}

	public void Init(ReadOnlySpan<byte> rgba, int mask)
	{
		Weights.AsSpan().Fill(0f);
		Remap.AsSpan().Fill(0);
		Count = 0;
		IsTransparent = false;
		for (int i = 0; i < 16; i++)
		{
			int num = i * 4;
			int num2 = 1 << i;
			if ((mask & num2) == 0)
			{
				Remap[i] = -1;
				continue;
			}
			if (IsDxt1 && rgba[num + 3] < 128)
			{
				Remap[i] = -1;
				IsTransparent = true;
				continue;
			}
			int num3 = 0;
			while (true)
			{
				int num4 = num3 * 4;
				if (num3 == i)
				{
					Points[Count].X = (float)(int)rgba[num] / 255f;
					Points[Count].Y = (float)(int)rgba[num + 1] / 255f;
					Points[Count].Z = (float)(int)rgba[num + 2] / 255f;
					float num5 = (float)(rgba[num + 3] + 1) / 256f;
					Weights[Count] = (WeightByAlpha ? num5 : 1f);
					Remap[i] = Count;
					Count++;
					break;
				}
				int num6 = 1 << num3;
				if ((mask & num6) != 0 && rgba[num] == rgba[num4] && rgba[num + 1] == rgba[num4 + 1] && rgba[num + 2] == rgba[num4 + 2] && (rgba[num4 + 3] >= 128 || !IsDxt1))
				{
					int num7 = Remap[num3];
					float num8 = (WeightByAlpha ? ((float)(rgba[num + 3] + 1) / 256f) : 1f);
					Weights[num7] += num8;
					Remap[i] = num7;
					break;
				}
				num3++;
			}
		}
		for (int j = 0; j < Count; j++)
		{
			Weights[j] = (float)Math.Sqrt(Weights[j]);
		}
		for (int k = Count; k < 16; k++)
		{
			Points[k].Clear();
		}
	}

	public byte[] RemapIndices(byte[] source)
	{
		byte[] array = Pool.RentByte(16);
		for (int i = 0; i < 16; i++)
		{
			int num = Remap[i];
			array[i] = (byte)((num == -1) ? 3 : source[num]);
		}
		return array;
	}

	public byte[] RemapIndices(byte source)
	{
		byte[] array = Pool.RentByte(16);
		for (int i = 0; i < 16; i++)
		{
			array[i] = (byte)((Remap[i] == -1) ? 3 : source);
		}
		return array;
	}
}
