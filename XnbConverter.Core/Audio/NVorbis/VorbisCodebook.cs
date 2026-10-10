using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NVorbis;

internal class VorbisCodebook
{
	internal int BookNum;

	internal int Dimensions;

	internal int Entries;

	private int[] Lengths;

	private float[] LookupTable;

	internal int MapType;

	private HuffmanListNode PrefixOverflowTree;

	private List<HuffmanListNode> PrefixList;

	private int PrefixBitLength;

	private int MaxBits;

	internal float this[int entry, int dim] => LookupTable[entry * Dimensions + dim];

	internal static VorbisCodebook Init(VorbisStreamDecoder vorbis, DataPacket packet, int number)
	{
		VorbisCodebook vorbisCodebook = new VorbisCodebook();
		vorbisCodebook.BookNum = number;
		vorbisCodebook.Init(packet);
		return vorbisCodebook;
	}

	private VorbisCodebook()
	{
	}

	internal void Init(DataPacket packet)
	{
		if (packet.ReadBits(24) != 5653314)
		{
			throw new InvalidDataException();
		}
		Dimensions = (int)packet.ReadBits(16);
		Entries = (int)packet.ReadBits(24);
		Lengths = new int[Entries];
		InitTree(packet);
		InitLookupTable(packet);
	}

	private void InitTree(DataPacket packet)
	{
		int num = 0;
		bool flag;
		if (packet.ReadBit())
		{
			int num2 = (int)packet.ReadBits(5) + 1;
			int num3 = 0;
			while (num3 < Entries)
			{
				int num4 = (int)packet.ReadBits(Utils.ilog(Entries - num3));
				while (--num4 >= 0)
				{
					Lengths[num3++] = num2;
				}
				num2++;
			}
			num = 0;
			flag = false;
		}
		else
		{
			flag = packet.ReadBit();
			for (int i = 0; i < Entries; i++)
			{
				if (!flag || packet.ReadBit())
				{
					Lengths[i] = (int)packet.ReadBits(5) + 1;
					num++;
				}
				else
				{
					Lengths[i] = -1;
				}
			}
		}
		if ((MaxBits = Lengths.Max()) > -1)
		{
			int num5 = 0;
			int[] array = null;
			if (flag && num >= Entries >> 2)
			{
				array = new int[Entries];
				Array.Copy(Lengths, array, Entries);
				flag = false;
			}
			num5 = (flag ? num : 0);
			int num6 = num5;
			int[] array2 = null;
			int[] array3 = null;
			if (!flag)
			{
				array3 = new int[Entries];
			}
			else if (num6 != 0)
			{
				array = new int[num6];
				array3 = new int[num6];
				array2 = new int[num6];
			}
			if (!ComputeCodewords(flag, num6, array3, array, Lengths, Entries, array2))
			{
				throw new InvalidDataException();
			}
			PrefixList = Huffman.BuildPrefixedLinkedList(array2 ?? Enumerable.Range(0, array3.Length).ToArray(), array ?? Lengths, array3, out PrefixBitLength, out PrefixOverflowTree);
		}
	}

	private bool ComputeCodewords(bool sparse, int sortedEntries, int[] codewords, int[] codewordLengths, int[] len, int n, int[] values)
	{
		int num = 0;
		uint[] array = new uint[32];
		int i;
		for (i = 0; i < n && len[i] <= 0; i++)
		{
		}
		if (i == n)
		{
			return true;
		}
		AddEntry(sparse, codewords, codewordLengths, 0u, i, num++, len[i], values);
		for (int j = 1; j <= len[i]; j++)
		{
			array[j] = (uint)(1 << 32 - j);
		}
		for (int j = i + 1; j < n; j++)
		{
			int num2 = len[j];
			if (num2 <= 0)
			{
				continue;
			}
			while (num2 > 0 && array[num2] == 0)
			{
				num2--;
			}
			if (num2 == 0)
			{
				return false;
			}
			uint num3 = array[num2];
			array[num2] = 0u;
			AddEntry(sparse, codewords, codewordLengths, Utils.BitReverse(num3), j, num++, len[j], values);
			if (num2 != len[j])
			{
				for (int num4 = len[j]; num4 > num2; num4--)
				{
					array[num4] = num3 + (uint)(1 << 32 - num4);
				}
			}
		}
		return true;
	}

	private void AddEntry(bool sparse, int[] codewords, int[] codewordLengths, uint huffCode, int symbol, int count, int len, int[] values)
	{
		if (sparse)
		{
			codewords[count] = (int)huffCode;
			codewordLengths[count] = len;
			values[count] = symbol;
		}
		else
		{
			codewords[symbol] = (int)huffCode;
		}
	}

	private void InitLookupTable(DataPacket packet)
	{
		MapType = (int)packet.ReadBits(4);
		if (MapType == 0)
		{
			return;
		}
		float num = Utils.ConvertFromVorbisFloat32(packet.ReadUInt32());
		float num2 = Utils.ConvertFromVorbisFloat32(packet.ReadUInt32());
		int count = (int)packet.ReadBits(4) + 1;
		bool flag = packet.ReadBit();
		int num3 = Entries * Dimensions;
		float[] array = new float[num3];
		if (MapType == 1)
		{
			num3 = lookup1_values();
		}
		uint[] array2 = new uint[num3];
		for (int i = 0; i < num3; i++)
		{
			array2[i] = (uint)packet.ReadBits(count);
		}
		if (MapType == 1)
		{
			for (int j = 0; j < Entries; j++)
			{
				double num4 = 0.0;
				int num5 = 1;
				for (int k = 0; k < Dimensions; k++)
				{
					int num6 = j / num5 % num3;
					double num7 = (double)((float)array2[num6] * num2 + num) + num4;
					array[j * Dimensions + k] = (float)num7;
					if (flag)
					{
						num4 = num7;
					}
					num5 *= num3;
				}
			}
		}
		else
		{
			for (int l = 0; l < Entries; l++)
			{
				double num8 = 0.0;
				int num9 = l * Dimensions;
				for (int m = 0; m < Dimensions; m++)
				{
					double num10 = (double)((float)array2[num9] * num2 + num) + num8;
					array[l * Dimensions + m] = (float)num10;
					if (flag)
					{
						num8 = num10;
					}
					num9++;
				}
			}
		}
		LookupTable = array;
	}

	private int lookup1_values()
	{
		int num = (int)Math.Floor(Math.Exp(Math.Log(Entries) / (double)Dimensions));
		if (Math.Floor(Math.Pow(num + 1, Dimensions)) <= (double)Entries)
		{
			num++;
		}
		return num;
	}

	internal int DecodeScalar(DataPacket packet)
	{
		int index = (int)packet.TryPeekBits(PrefixBitLength, out var bitsRead);
		if (bitsRead == 0)
		{
			return -1;
		}
		HuffmanListNode huffmanListNode = PrefixList[index];
		if (huffmanListNode != null)
		{
			packet.SkipBits(huffmanListNode.Length);
			return huffmanListNode.Value;
		}
		index = (int)packet.TryPeekBits(MaxBits, out bitsRead);
		huffmanListNode = PrefixOverflowTree;
		do
		{
			if (huffmanListNode.Bits == (index & huffmanListNode.Mask))
			{
				packet.SkipBits(huffmanListNode.Length);
				return huffmanListNode.Value;
			}
		}
		while ((huffmanListNode = huffmanListNode.Next) != null);
		return -1;
	}
}
