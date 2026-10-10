using System;
using System.Collections.Generic;

namespace NVorbis;

internal static class Huffman
{
	private const int MAX_TABLE_BITS = 10;

	internal static List<HuffmanListNode> BuildPrefixedLinkedList(int[] values, int[] lengthList, int[] codeList, out int tableBits, out HuffmanListNode firstOverflowNode)
	{
		HuffmanListNode[] array = new HuffmanListNode[lengthList.Length];
		int num = 0;
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = new HuffmanListNode
			{
				Value = values[i],
				Length = ((lengthList[i] <= 0) ? 99999 : lengthList[i]),
				Bits = codeList[i],
				Mask = (1 << lengthList[i]) - 1
			};
			if (lengthList[i] > 0 && num < lengthList[i])
			{
				num = lengthList[i];
			}
		}
		Array.Sort(array, SortCallback);
		tableBits = ((num > 10) ? 10 : num);
		List<HuffmanListNode> list = new List<HuffmanListNode>(1 << tableBits);
		firstOverflowNode = null;
		for (int j = 0; j < array.Length && array[j].Length < 99999; j++)
		{
			if (firstOverflowNode == null)
			{
				int length = array[j].Length;
				if (length > tableBits)
				{
					firstOverflowNode = array[j];
					continue;
				}
				int num2 = 1 << tableBits - length;
				HuffmanListNode huffmanListNode = array[j];
				for (int k = 0; k < num2; k++)
				{
					int num3 = (k << length) | huffmanListNode.Bits;
					while (list.Count <= num3)
					{
						list.Add(null);
					}
					list[num3] = huffmanListNode;
				}
			}
			else
			{
				array[j - 1].Next = array[j];
			}
		}
		while (list.Count < 1 << tableBits)
		{
			list.Add(null);
		}
		return list;
	}

	private static int SortCallback(HuffmanListNode i1, HuffmanListNode i2)
	{
		int num = i1.Length - i2.Length;
		if (num == 0)
		{
			return i1.Bits - i2.Bits;
		}
		return num;
	}
}
