using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NVorbis;

internal static class Utils
{
	[StructLayout(LayoutKind.Explicit)]
	private struct FloatBits
	{
		[FieldOffset(0)]
		public float Float;

		[FieldOffset(0)]
		public uint Bits;
	}

	internal static int ilog(int x)
	{
		int num = 0;
		while (x > 0)
		{
			num++;
			x >>= 1;
		}
		return num;
	}

	internal static uint BitReverse(uint n)
	{
		return BitReverse(n, 32);
	}

	internal static uint BitReverse(uint n, int bits)
	{
		n = ((n & 0xAAAAAAAAu) >> 1) | ((n & 0x55555555) << 1);
		n = ((n & 0xCCCCCCCCu) >> 2) | ((n & 0x33333333) << 2);
		n = ((n & 0xF0F0F0F0u) >> 4) | ((n & 0xF0F0F0F) << 4);
		n = ((n & 0xFF00FF00u) >> 8) | ((n & 0xFF00FF) << 8);
		return ((n >> 16) | (n << 16)) >> 32 - bits;
	}

	internal static float ClipValue(float value, ref bool clipped)
	{
		FloatBits floatBits = default(FloatBits);
		floatBits.Bits = 0u;
		floatBits.Float = value;
		if ((floatBits.Bits & 0x7FFFFFFF) > 1065353215)
		{
			clipped = true;
			floatBits.Bits = 0x3F7FFFFF | (floatBits.Bits & 0x80000000u);
		}
		return floatBits.Float;
	}

	internal static float ConvertFromVorbisFloat32(uint bits)
	{
		int num = (int)bits >> 31;
		double y = (int)(((bits & 0x7FE00000) >> 21) - 788);
		return (float)(((bits & 0x1FFFFF) ^ num) + (num & 1)) * (float)Math.Pow(2.0, y);
	}

	internal static int Sum(Queue<int> queue)
	{
		int num = 0;
		for (int i = 0; i < queue.Count; i++)
		{
			int num2 = queue.Dequeue();
			num += num2;
			queue.Enqueue(num2);
		}
		return num;
	}
}
