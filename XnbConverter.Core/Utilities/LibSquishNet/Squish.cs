using XnbConverter.Utilities;

namespace Squish;

public class Squish : IDisposable
{
	private readonly BoolFlag boolFlag;

	private readonly ClusterFit clusterFit;

	private readonly ColourSet Colours;

	private readonly int Height;

	private readonly RangeFit rangeFit;

	private readonly SingleColourFit singleColourFit;

	private readonly int Width;

	public Squish(SquishFlags flags, int width, int height)
	{
		this.boolFlag = new BoolFlag(flags);
		BoolFlag boolFlag = this.boolFlag;
		if (boolFlag != null && !boolFlag.isDxt3 && !boolFlag.isDxt5)
		{
			this.boolFlag.isDxt1 = true;
		}
		boolFlag = this.boolFlag;
		if (boolFlag != null && !boolFlag.isColourRangeFit && !boolFlag.isColourIterativeClusterFit)
		{
			this.boolFlag.isColourClusterFit = true;
		}
		Colours = new ColourSet(this.boolFlag.isDxt1, this.boolFlag.isWeightColourByAlpha);
		singleColourFit = new SingleColourFit(Colours, this.boolFlag.isDxt1);
		rangeFit = new RangeFit(Colours, this.boolFlag.isDxt1);
		clusterFit = new ClusterFit(Colours, this.boolFlag.isDxt1, this.boolFlag.isColourIterativeClusterFit);
		Width = width;
		Height = height;
	}

	public void Dispose()
	{
		Colours.Dispose();
		singleColourFit.Dispose();
		clusterFit.Dispose();
		rangeFit.Dispose();
	}

	private static void FixRange(int min, int max, int steps)
	{
		if (max - min < steps)
		{
			max = Math.Min(min + steps, 255);
		}
		if (max - min < steps)
		{
			min = Math.Max(0, max - steps);
		}
	}

	private static int FitCodes(ReadOnlySpan<byte> rgba, int mask, byte[] codes, byte[] indices)
	{
		int num = 0;
		for (int i = 0; i < 16; i++)
		{
			int num2 = 1 << i;
			if ((mask & num2) == 0)
			{
				indices[i] = 0;
				continue;
			}
			int num3 = rgba[4 * i + 3];
			int num4 = int.MaxValue;
			int num5 = 0;
			for (int j = 0; j < 8; j++)
			{
				int num6 = num3 - codes[j];
				num6 *= num6;
				if (num6 < num4)
				{
					num4 = num6;
					num5 = j;
				}
			}
			indices[i] = (byte)num5;
			num += num4;
		}
		return num;
	}

	public int GetStorageRequirements()
	{
		int num = (Width + 3) / 4 * ((Height + 3) / 4);
		int num2 = boolFlag.isDxt1 ? 8 : 16;
		return num * num2;
	}

	public void DecompressImage(Span<byte> rgba, ReadOnlySpan<byte> blocks)
	{
		int num = 0;
		int num2 = boolFlag.isDxt1 ? 8 : 16;
		byte[] array = Pool.RentByte(64);
		Span<byte> rgba2 = array.AsSpan();
		for (int i = 0; i < Height; i += 4)
		{
			for (int j = 0; j < Width; j += 4)
			{
				Decompress(rgba2, blocks, num);
				int num3 = 0;
				for (int k = 0; k < 4; k++)
				{
					for (int l = 0; l < 4; l++)
					{
						int num4 = j + l;
						int num5 = i + k;
						if (num4 < Width && num5 < Height)
						{
							rgba2.Slice(num3, 4).CopyTo(rgba.Slice(4 * (Width * num5 + num4), 4));
						}
						num3 += 4;
					}
				}
				num += num2;
			}
		}
		Pool.Return(array);
	}

	private void Decompress(Span<byte> rgba, ReadOnlySpan<byte> block, int offset)
	{
		int num = offset;
		if (boolFlag.isDxt3 || boolFlag.isDxt5)
		{
			num += 8;
		}
		ColourBlock.DecompressColour(rgba, block.Slice(num, 8), boolFlag.isDxt1);
		if (boolFlag.isDxt3)
		{
			DecompressAlphaDxt3(rgba, block.Slice(offset, 8));
		}
		else if (boolFlag.isDxt5)
		{
			DecompressAlphaDxt5(rgba, block.Slice(offset, 8));
		}
	}

	private static void DecompressAlphaDxt3(Span<byte> rgba, ReadOnlySpan<byte> block)
	{
		for (int i = 0; i < 8; i++)
		{
			byte b = (byte)(block[i] & 0xFu);
			byte b2 = (byte)(block[i] & 0xF0u);
			rgba[8 * i + 3] = (byte)(b | (b << 4));
			rgba[8 * i + 7] = (byte)(b2 | (b2 >> 4));
		}
	}

	private static void DecompressAlphaDxt5(Span<byte> rgba, ReadOnlySpan<byte> block)
	{
		int num = block[0];
		int num2 = block[1];
		byte[] array = new byte[8]
		{
			(byte)num,
			(byte)num2,
			0,
			0,
			0,
			0,
			0,
			0
		};
		if (num > num2)
		{
			for (int i = 1; i < 7; i++)
			{
				array[1 + i] = (byte)(i * (num2 - num) / 7 + num);
			}
		}
		else
		{
			for (int j = 1; j < 5; j++)
			{
				array[1 + j] = (byte)(j * (num2 - num) / 5 + num);
			}
			array[6] = 0;
			array[7] = byte.MaxValue;
		}
		byte[] array2 = new byte[16];
		int num3 = 2;
		int num4 = 0;
		for (int k = 0; k < 2; k++)
		{
			int num5 = 0;
			for (int l = 0; l < 3; l++)
			{
				num5 |= block[num3++] << 8 * l;
			}
			for (int m = 0; m < 8; m++)
			{
				int num6 = (num5 >> 3 * m) & 7;
				array2[num4++] = (byte)num6;
			}
		}
		for (int n = 0; n < 16; n++)
		{
			rgba[4 * n + 3] = array[array2[n]];
		}
	}

	public void CompressImage(ReadOnlySpan<byte> rgba, Span<byte> blocks)
	{
		int num = 0;
		int num2 = boolFlag.isDxt1 ? 8 : 16;
		byte[] array = Pool.RentByte(64);
		Span<byte> span = array.AsSpan();
		for (int i = 0; i < Height; i += 4)
		{
			for (int j = 0; j < Width; j += 4)
			{
				int num3 = 0;
				int num4 = 0;
				for (int k = 0; k < 4; k++)
				{
					for (int l = 0; l < 4; l++)
					{
						int num5 = j + l;
						int num6 = i + k;
						Span<byte> destination = span.Slice(num3, 4);
						if (num5 < Width && num6 < Height)
						{
							rgba.Slice(4 * (Width * num6 + num5), 4).CopyTo(destination);
							num4 |= 1 << 4 * k + l;
						}
						else
						{
							destination.Fill(0);
						}
						num3 += 4;
					}
				}
				CompressMasked(span, num4, blocks, num);
				num += num2;
			}
		}
		Pool.Return(array);
	}

	private void CompressMasked(ReadOnlySpan<byte> rgba, int mask, Span<byte> block, int offset)
	{
		int num = offset;
		if (boolFlag.isDxt3 || boolFlag.isDxt5)
		{
			num += 8;
		}
		Colours.Init(rgba, mask);
		ColourFit colourFit = Colours.Count == 1 ? singleColourFit : !boolFlag.isColourRangeFit && Colours.Count != 0 ? (ColourFit)clusterFit : (ColourFit)rangeFit;
		colourFit.Init();
		ref Span<byte> reference = ref block;
		int num2 = num;
		colourFit.Compress(reference.Slice(num2, reference.Length - num2));
		if (boolFlag.isDxt3)
		{
			reference = ref block;
			num2 = offset;
			CompressAlphaDxt3(rgba, mask, reference.Slice(num2, reference.Length - num2));
		}
		else if (boolFlag.isDxt5)
		{
			reference = ref block;
			num2 = offset;
			CompressAlphaDxt5(rgba, mask, reference.Slice(num2, reference.Length - num2));
		}
	}

	private static void CompressAlphaDxt3(ReadOnlySpan<byte> rgba, int mask, Span<byte> block)
	{
		for (int i = 0; i < 8; i++)
		{
			float a = (float)(int)rgba[8 * i + 3] * (1f / 17f);
			float a2 = (float)(int)rgba[8 * i + 7] * (1f / 17f);
			int num = a.ToInt(15);
			int num2 = a2.ToInt(15);
			int num3 = 1 << 2 * i;
			int num4 = 1 << 2 * i + 1;
			if ((mask & num3) == 0)
			{
				num = 0;
			}
			if ((mask & num4) == 0)
			{
				num2 = 0;
			}
			block[i] = (byte)(num | (num2 << 4));
		}
	}

	private static void CompressAlphaDxt5(ReadOnlySpan<byte> rgba, int mask, Span<byte> block)
	{
		int val = 255;
		int num = 0;
		int val2 = 255;
		int num2 = 0;
		for (int i = 0; i < 16; i++)
		{
			int num3 = 1 << i;
			if ((mask & num3) != 0)
			{
				int num4 = rgba[4 * i + 3];
				val2 = Math.Min(val2, num4);
				num2 = Math.Max(num2, num4);
				if (num4 != 0)
				{
					val = Math.Min(val, num4);
				}
				if (num4 != 255)
				{
					num = Math.Max(num, num4);
				}
			}
		}
		val = Math.Min(val, num);
		val2 = Math.Min(val2, num2);
		byte[] array = new byte[8]
		{
			(byte)val,
			(byte)num,
			0,
			0,
			0,
			0,
			0,
			0
		};
		for (int j = 1; j < 5; j++)
		{
			array[1 + j] = (byte)(((5 - j) * val + j * num) / 5);
		}
		array[6] = 0;
		array[7] = byte.MaxValue;
		byte[] array2 = new byte[8]
		{
			(byte)val2,
			(byte)num2,
			0,
			0,
			0,
			0,
			0,
			0
		};
		for (int k = 1; k < 7; k++)
		{
			array2[1 + k] = (byte)(((7 - k) * val2 + k * num2) / 7);
		}
		byte[] array3 = Pool.RentByte(16);
		byte[] array4 = Pool.RentByte(16);
		int num5 = FitCodes(rgba, mask, array, array3);
		int num6 = FitCodes(rgba, mask, array2, array4);
		if (num5 <= num6)
		{
			WriteAlphaBlock5(val, num, array3, block);
		}
		else
		{
			WriteAlphaBlock7(val2, num2, array4, block);
		}
		Pool.Return(array3);
		Pool.Return(array4);
	}

	private static void WriteAlphaBlock(int alpha0, int alpha1, byte[] indices, Span<byte> block)
	{
		block[0] = (byte)alpha0;
		block[1] = (byte)alpha1;
		int num = 2;
		int num2 = 0;
		for (int i = 0; i < 2; i++)
		{
			int num3 = 0;
			for (int j = 0; j < 8; j++)
			{
				int num4 = indices[num2];
				num3 |= num4 << 3 * j;
				num2++;
			}
			for (int k = 0; k < 3; k++)
			{
				block[num++] = (byte)((uint)(num3 >> 8 * k) & 0xFFu);
			}
		}
	}

	private static void WriteAlphaBlock5(int alpha0, int alpha1, byte[] indices, Span<byte> block)
	{
		// check the relative values of the endpoints
		if (alpha0 > alpha1)
		{
			(alpha0, alpha1) = (alpha1, alpha0);
			// swap the indices
			for (var i = 0; i < 16; ++i)
				indices[i] = indices[i] switch
				{
					0 => 1,
					1 => 0,
					<= 5 => (byte)(7 - indices[i]),
					_ => indices[i]
				};
		}

		// write the block
		WriteAlphaBlock(alpha0, alpha1, indices, block);
	}
	
	private static void WriteAlphaBlock7(int alpha0, int alpha1, byte[] indices, Span<byte> block)
	{
		if (alpha0 < alpha1)
		{
			int num = alpha1;
			alpha1 = alpha0;
			alpha0 = num;
			for (int i = 0; i < 16; i++)
			{
				int num2 = i;
				indices[num2] = indices[i] switch
				{
					0 => 1, 
					1 => 0, 
					_ => (byte)(9 - indices[i]), 
				};
			}
		}
		WriteAlphaBlock(alpha0, alpha1, indices, block);
	}
}
