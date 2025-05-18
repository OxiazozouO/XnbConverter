using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

namespace Squish;

public static class ColourBlock
{
	private const int winSize = 16;

	public static int ToInt(this float a, int limit)
	{
		return Math.Clamp((int)(a + 0.5f), 0, limit);
	}

	public static byte ScaleToByte(this float a, int limit)
	{
		return (byte)Math.Clamp((int)((float)limit * a + 0.5f), 0, limit);
	}

	public static int To565(this Vector3 colour)
	{
		byte num = colour.X.ScaleToByte(31);
		int num2 = colour.Y.ScaleToByte(63);
		int num3 = colour.Z.ScaleToByte(31);
		return (num << 11) | (num2 << 5) | num3;
	}

	public static int To565(this Vector4 colour)
	{
		byte num = colour.X.ScaleToByte(31);
		int num2 = colour.Y.ScaleToByte(63);
		int num3 = colour.Z.ScaleToByte(31);
		return (num << 11) | (num2 << 5) | num3;
	}

	private static void WriteColourBlock(int a, int b, Span<byte> indices, Span<byte> block)
	{
		block[0] = (byte)((uint)a & 0xFFu);
		block[1] = (byte)(a >> 8);
		block[2] = (byte)((uint)b & 0xFFu);
		block[3] = (byte)(b >> 8);
		block[4] = (byte)(indices[0] | (indices[1] << 2) | (indices[2] << 4) | (indices[3] << 6));
		block[5] = (byte)(indices[4] | (indices[5] << 2) | (indices[6] << 4) | (indices[7] << 6));
		block[6] = (byte)(indices[8] | (indices[9] << 2) | (indices[10] << 4) | (indices[11] << 6));
		block[7] = (byte)(indices[12] | (indices[13] << 2) | (indices[14] << 4) | (indices[15] << 6));
	}

	public static void WriteColourBlock3(int a565, int b565, Span<byte> indices, Span<byte> block)
	{
		if (a565 > b565)
		{
			int num = b565;
			b565 = a565;
			a565 = num;
			for (int i = 0; i < 16; i++)
			{
				if (indices[i] == 0)
				{
					indices[i] = 1;
				}
				else if (indices[i] == 1)
				{
					indices[i] = 0;
				}
			}
		}
		WriteColourBlock(a565, b565, indices, block);
	}

	public static void WriteColourBlock4(int a565, int b565, Span<byte> indices, Span<byte> block)
	{
		if (a565 < b565)
		{
			int num = b565;
			b565 = a565;
			a565 = num;
			for (int i = 0; i < 16; i++)
			{
				indices[i] = (byte)((indices[i] ^ 1u) & 3u);
			}
		}
		else if (a565 == b565)
		{
			indices.Fill(0);
		}
		WriteColourBlock(a565, b565, indices, block);
	}

	private static int Unpack565(byte p1, byte p2, Span<byte> colour)
	{
		int num = p1 | (p2 << 8);
		byte b = (byte)((uint)(num >> 11) & 0x1Fu);
		byte b2 = (byte)((uint)(num >> 5) & 0x3Fu);
		byte b3 = (byte)((uint)num & 0x1Fu);
		colour[0] = (byte)((b << 3) | (b >> 2));
		colour[1] = (byte)((b2 << 2) | (b2 >> 4));
		colour[2] = (byte)((b3 << 3) | (b3 >> 2));
		colour[3] = byte.MaxValue;
		return num;
	}

	public static void DecompressColour(Span<byte> rgba, ReadOnlySpan<byte> block, bool isDxt1)
	{
		byte[] array = Pool.RentByte(16);
		Span<byte> colour = array.AsSpan();
		colour.Fill(0);
		int num = Unpack565(block[0], block[1], colour);
		byte p = block[2];
		byte p2 = block[3];
		ref Span<byte> reference = ref colour;
		int num2 = Unpack565(p, p2, reference.Slice(4, reference.Length - 4));
		if (isDxt1 && num <= num2)
		{
			for (int i = 0; i < 3; i++)
			{
				int num3 = colour[i];
				int num4 = colour[4 + i];
				colour[8 + i] = (byte)((num3 + num4) / 2);
				colour[12 + i] = 0;
			}
		}
		else
		{
			for (int j = 0; j < 3; j++)
			{
				int num5 = colour[j];
				int num6 = colour[4 + j];
				colour[8 + j] = (byte)((2 * num5 + num6) / 3);
				colour[12 + j] = (byte)((num5 + 2 * num6) / 3);
			}
		}
		colour[11] = byte.MaxValue;
		colour[15] = (byte)(!isDxt1 || num > num2 ? 255u : 0u);
		byte[] array2 = Pool.RentByte(16);
		int k = 4;
		int num7 = -1;
		for (; k < 8; k++)
		{
			byte b = block[k];
			array2[++num7] = (byte)(b & 3u);
			array2[++num7] = (byte)((uint)(b >> 2) & 3u);
			array2[++num7] = (byte)((uint)(b >> 4) & 3u);
			array2[++num7] = (byte)((uint)(b >> 6) & 3u);
		}
		for (int l = 0; l < 16; l++)
		{
			Span<byte> span = colour.Slice(4 * array2[l], 4);
			reference = ref rgba;
			int num8 = 4 * l;
			span.CopyTo(reference.Slice(num8, reference.Length - num8));
		}
		Pool.Return(array2);
		Pool.Return(array);
	}
}
