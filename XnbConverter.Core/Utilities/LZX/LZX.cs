using System;
using XnbConverter.Configurations;
using XnbConverter.Exceptions;
using XnbConverter.Readers;

namespace XnbConverter.Utilities.LZX;

public class LZX : IDisposable
{
	private struct LzxState
	{
		public uint R0;

		public uint R1;

		public uint R2;

		public ushort MainElements;

		public bool HeaderRead;

		public uint BlockType;

		public uint BlockLength;

		public uint BlockRemaining;

		public uint FramesRead;

		public int IntelFilesize;

		public int IntelCurpos;

		public int IntelStarted;

		public ushort[] PreTreeTable;

		public byte[] PreTreeLen;

		public ushort[] MainTreeTable;

		public byte[] MainTreeLen;

		public ushort[] LengthTable;

		public byte[] LengthLen;

		public ushort[] AlignedTable;

		public byte[] AlignedLen;

		public uint ActualSize;

		public byte[] Window;

		public uint WindowSize;

		public uint WindowPos;
	}

	private const ushort MinMatch = 2;

	private const ushort MaxMatch = 257;

	private const ushort NumChars = 256;

	private const uint Invalid = 0u;

	private const uint Verbatim = 1u;

	private const uint Aligned = 2u;

	private const uint Uncompressed = 3u;

	private const ushort PreTreeNumElements = 20;

	private const ushort AlignedNumElements = 8;

	private const ushort NumPrimaryLengths = 7;

	private const ushort NumSecondaryLengths = 249;

	private const ushort PreTreeMaxSymbols = 20;

	private const ushort PreTreeTableBits = 6;

	private const ushort MainTreeMaxSymbols = 656;

	private const ushort MainTreeTableBits = 12;

	private const ushort LengthMaxSymbols = 250;

	private const ushort LengthTableBits = 12;

	private const ushort AlignedMaxSymbols = 8;

	private const ushort AlignedTableBits = 7;

	private const ushort LenTableSafety = 64;

	private static readonly uint[] position_base;

	private static readonly byte[] extra_bits;

	private LzxState m_state;

	static LZX()
	{
		extra_bits = new byte[52];
		int i = 0;
		int num = 0;
		for (; i < 52; i += 2)
		{
			extra_bits[i] = (extra_bits[i + 1] = (byte)num);
			if (i != 0 && num < 17)
			{
				num++;
			}
		}
		position_base = new uint[51];
		int j = 0;
		int num2 = 0;
		for (; j < 51; j++)
		{
			position_base[j] = (uint)num2;
			num2 += 1 << (int)extra_bits[j];
		}
		Logger.Debug(Error.LZX_13, extra_bits.ToJoinStr());
		Logger.Debug(Error.LZX_14, position_base.ToJoinStr());
	}

	public void Dispose()
	{
		Pool.Return(m_state.Window);
	}

	private LZX(int window)
	{
		if (window < 15 || window > 21)
		{
			throw new XnbError(Error.LZX_1);
		}
		m_state.WindowSize = (uint)(1 << window);
		int num = window switch
		{
			21 => 50, 
			20 => 42, 
			_ => window << 1, 
		};
		m_state.R0 = (m_state.R1 = (m_state.R2 = 1u));
		m_state.MainElements = (ushort)(256 + (num << 3));
		m_state.HeaderRead = false;
		m_state.BlockRemaining = 0u;
		m_state.BlockType = 0u;
		m_state.WindowPos = 0u;
		m_state.PreTreeLen = new byte[20];
		m_state.AlignedLen = new byte[8];
		m_state.LengthLen = new byte[249];
		m_state.MainTreeLen = new byte[656];
		m_state.Window = Pool.RentByte((int)m_state.WindowSize);
	}

	private void Decompress(BufferReader bufferReader, int frameSize, int blockSize, byte[] decompressed, int decompressedIndex)
	{
		if (!m_state.HeaderRead)
		{
			uint num = bufferReader.ReadLzxBits(1);
			Logger.Debug(Error.LZX_15, num.B(), num);
			if (num != 0)
			{
				throw new XnbError(Error.LZX_2);
			}
			m_state.HeaderRead = true;
		}
		int num2 = frameSize;
		while (num2 > 0)
		{
			if (m_state.BlockRemaining == 0)
			{
				m_state.BlockType = bufferReader.ReadLzxBits(3);
				Logger.Debug(Error.LZX_16, m_state.BlockType.B(), m_state.BlockType);
				uint num3 = bufferReader.ReadLzxBits(16);
				uint num4 = bufferReader.ReadLzxBits(8);
				m_state.BlockRemaining = (num3 << 8) | num4;
				Logger.Debug(Error.LZX_17, m_state.BlockRemaining);
				switch (m_state.BlockType)
				{
				case 2u:
				{
					for (int i = 0; i < 8; i++)
					{
						m_state.AlignedLen[i] = (byte)bufferReader.ReadLzxBits(3);
					}
					m_state.AlignedTable = DecodeTable(8u, 7u, m_state.AlignedLen, 512);
					goto case 1u;
				}
				case 1u:
					ReadLengths(bufferReader, m_state.MainTreeLen, 0, 256u);
					ReadLengths(bufferReader, m_state.MainTreeLen, 256, m_state.MainElements);
					m_state.MainTreeTable = DecodeTable(656u, 12u, m_state.MainTreeLen, 8192);
					ReadLengths(bufferReader, m_state.LengthLen, 0, 249u);
					m_state.LengthTable = DecodeTable(250u, 12u, m_state.LengthLen, 8192);
					break;
				case 3u:
					bufferReader.Align();
					m_state.R0 = bufferReader.ReadUInt32();
					m_state.R1 = bufferReader.ReadUInt32();
					m_state.R2 = bufferReader.ReadUInt32();
					break;
				default:
					throw new XnbError(Error.LZX_3, m_state.BlockType);
				}
			}
			int num5;
			while ((num5 = (int)m_state.BlockRemaining) > 0 && num2 > 0)
			{
				if (num5 > num2)
				{
					num5 = num2;
				}
				num2 -= num5;
				m_state.BlockRemaining -= (uint)num5;
				m_state.WindowPos &= m_state.WindowSize - 1;
				if (m_state.WindowPos + num5 > m_state.WindowSize)
				{
					throw new XnbError(Error.LZX_4);
				}
				switch (m_state.BlockType)
				{
				case 2u:
					while (num5 > 0)
					{
						int num6 = (int)ReadHuffSymbol(bufferReader, m_state.MainTreeTable, m_state.MainTreeLen, 656u, 12u);
						if (num6 < 256)
						{
							m_state.Window[m_state.WindowPos++] = (byte)num6;
							num5--;
							continue;
						}
						num6 -= 256;
						int num7 = num6 & 7;
						if (num7 == 7)
						{
							int num8 = (int)ReadHuffSymbol(bufferReader, m_state.LengthTable, m_state.LengthLen, 250u, 12u);
							num7 += num8;
						}
						num7 += 2;
						int num9 = num6 >> 3;
						if (num9 > 2)
						{
							int num10 = extra_bits[num9];
							num9 = (int)(position_base[num9] - 2);
							if (num10 <= 3)
							{
								num9 = ((num10 == 3) ? (num9 + (int)ReadHuffSymbol(bufferReader, m_state.AlignedTable, m_state.AlignedLen, 8u, 7u)) : ((num10 <= 0) ? 1 : (num9 + (int)bufferReader.ReadLzxBits((byte)num10))));
							}
							else
							{
								num10 -= 3;
								int num11 = (int)bufferReader.ReadLzxBits((byte)num10);
								num9 += num11 << 3;
								int num14 = (int)ReadHuffSymbol(bufferReader, m_state.AlignedTable, m_state.AlignedLen, 8u, 7u);
								num9 += num14;
							}
							m_state.R2 = m_state.R1;
							m_state.R1 = m_state.R0;
							m_state.R0 = (uint)num9;
						}
						else
						{
							switch (num9)
							{
							case 0:
								num9 = (int)m_state.R0;
								break;
							case 1:
								num9 = (int)m_state.R1;
								m_state.R1 = m_state.R0;
								m_state.R0 = (uint)num9;
								break;
							default:
								num9 = (int)m_state.R2;
								m_state.R2 = m_state.R0;
								m_state.R0 = (uint)num9;
								break;
							}
						}
						int windowPos = (int)m_state.WindowPos;
						num5 -= num7;
						int num12;
						if (m_state.WindowPos >= num9)
						{
							num12 = windowPos - num9;
						}
						else
						{
							num12 = windowPos + ((int)m_state.WindowSize - num9);
							int num13 = num9 - (int)m_state.WindowPos;
							if (num13 < num7)
							{
								num7 -= num13;
								m_state.WindowPos += (uint)num13;
								while (num13-- > 0)
								{
									m_state.Window[windowPos++] = m_state.Window[num12++];
								}
								num12 = 0;
							}
						}
						m_state.WindowPos += (uint)num7;
						while (num7-- > 0)
						{
							m_state.Window[windowPos++] = m_state.Window[num12++];
						}
					}
					break;
				case 1u:
					while (num5 > 0)
					{
						int num6 = (int)ReadHuffSymbol(bufferReader, m_state.MainTreeTable, m_state.MainTreeLen, 656u, 12u);
						if (num6 < 256)
						{
							m_state.Window[m_state.WindowPos++] = (byte)num6;
							num5--;
							continue;
						}
						num6 -= 256;
						int num7 = num6 & 7;
						if (num7 == 7)
						{
							int num8 = (int)ReadHuffSymbol(bufferReader, m_state.LengthTable, m_state.LengthLen, 250u, 12u);
							num7 += num8;
						}
						num7 += 2;
						int num9 = num6 >> 3;
						if (num9 > 2)
						{
							if (num9 != 3)
							{
								int num10 = extra_bits[num9];
								int num11 = (int)bufferReader.ReadLzxBits((byte)num10);
								num9 = (int)(position_base[num9] - 2) + num11;
							}
							else
							{
								num9 = 1;
							}
							m_state.R2 = m_state.R1;
							m_state.R1 = m_state.R0;
							m_state.R0 = (uint)num9;
						}
						else
						{
							switch (num9)
							{
							case 0:
								num9 = (int)m_state.R0;
								break;
							case 1:
								num9 = (int)m_state.R1;
								m_state.R1 = m_state.R0;
								m_state.R0 = (uint)num9;
								break;
							default:
								num9 = (int)m_state.R2;
								m_state.R2 = m_state.R0;
								m_state.R0 = (uint)num9;
								break;
							}
						}
						int windowPos = (int)m_state.WindowPos;
						num5 -= num7;
						int num12;
						if (m_state.WindowPos >= num9)
						{
							num12 = windowPos - num9;
						}
						else
						{
							num12 = windowPos + ((int)m_state.WindowSize - num9);
							int num13 = num9 - (int)m_state.WindowPos;
							if (num13 < num7)
							{
								num7 -= num13;
								m_state.WindowPos += (uint)num13;
								while (num13-- > 0)
								{
									m_state.Window[windowPos++] = m_state.Window[num12++];
								}
								num12 = 0;
							}
						}
						m_state.WindowPos += (uint)num7;
						while (num7-- > 0)
						{
							m_state.Window[windowPos++] = m_state.Window[num12++];
						}
					}
					break;
				case 3u:
					if (bufferReader.BytePosition + num5 > blockSize)
					{
						throw new XnbError(Error.LZX_5, blockSize, bufferReader.BytePosition, num5);
					}
					bufferReader.Buffer.AsSpan(bufferReader.BytePosition, num5).CopyTo(m_state.Window.AsSpan((int)m_state.WindowPos, num5));
					bufferReader.BytePosition += num5;
					m_state.WindowPos += (uint)num5;
					break;
				default:
					throw new XnbError(Error.LZX_6);
				}
			}
		}
		if (num2 != 0)
		{
			throw new XnbError(Error.LZX_7);
		}
		bufferReader.Align();
		int num15 = (int)((m_state.WindowPos == 0) ? m_state.WindowSize : m_state.WindowPos);
		num15 -= frameSize;
		m_state.Window.AsSpan(num15, frameSize).CopyTo(decompressed.AsSpan(decompressedIndex, frameSize));
	}

	private void ReadLengths(BufferReader buffer, Span<byte> table, int first, uint last)
	{
		int i;
		for (i = 0; i < 20; i++)
		{
			m_state.PreTreeLen[i] = (byte)buffer.ReadLzxBits(4);
		}
		m_state.PreTreeTable = DecodeTable(20u, 6u, m_state.PreTreeLen, 128);
		i = first;
		while (i < last)
		{
			int num = (int)ReadHuffSymbol(buffer, m_state.PreTreeTable, m_state.PreTreeLen, 20u, 6u);
			switch (num)
			{
			case 17:
			{
				uint num2 = buffer.ReadLzxBits(4) + 4;
				table.Slice(i, (int)num2).Fill(0);
				i += (int)num2;
				break;
			}
			case 18:
			{
				uint num2 = buffer.ReadLzxBits(5) + 20;
				table.Slice(i, (int)num2).Fill(0);
				i += (int)num2;
				break;
			}
			case 19:
			{
				uint num3 = buffer.ReadLzxBits(1) + 4;
				num = (int)ReadHuffSymbol(buffer, m_state.PreTreeTable, m_state.PreTreeLen, 20u, 6u);
				num = table[i] - num;
				if (num < 0)
				{
					num += 17;
				}
				table.Slice(i, (int)num3).Fill((byte)num);
				i += (int)num3;
				break;
			}
			default:
				num = table[i] - num;
				if (num < 0)
				{
					num += 17;
				}
				table[i++] = (byte)num;
				break;
			}
		}
	}

	private ushort[] DecodeTable(uint symbols, uint bits, byte[] length, int tableMaxLen)
	{
		ushort[] array = Pool.RentUShort(tableMaxLen);
		int num = 0;
		uint num2 = 0u;
		uint num3 = (uint)(1 << (int)bits);
		uint num4 = num3 >> 1;
		for (int i = 1; i <= bits; i++)
		{
			for (int j = 0; j < symbols; j++)
			{
				if (length.Length != j && length[j] == i)
				{
					uint num5 = num2;
					num2 += num4;
					if (num2 > num3)
					{
						Logger.Debug(length[j].ToString());
						Logger.Debug(Error.LZX_18, num2, num4, num3);
						Logger.Debug(Error.LZX_19, i, bits);
						Logger.Debug(Error.LZX_20, j, symbols);
						throw new XnbError(Error.LZX_8);
					}
					uint num6 = num4;
					num += (int)num6;
					while (num6-- != 0)
					{
						array[num5] = (ushort)j;
						num5++;
					}
				}
			}
			num4 >>= 1;
		}
		if (num2 == num3)
		{
			ushort[] subArray = array[..num];
			Pool.Return(array);
			return subArray;
		}
		num = Math.Max(num, (int)num3);
		for (uint num7 = num2; num7 < num3; num7++)
		{
			array[num7] = ushort.MaxValue;
		}
		uint num8 = Math.Max(num3 >> 1, symbols);
		num2 <<= 16;
		num3 <<= 16;
		num4 = 32768u;
		symbols = Math.Min((uint)length.Length, symbols);
		for (uint num9 = bits + 1; num9 <= 16; num9++)
		{
			for (int k = 0; k < symbols; k++)
			{
				if (length[k] != num9)
				{
					continue;
				}
				int num10 = (int)(num2 >> 16);
				for (int l = 0; l < num9 - bits; l++)
				{
					if (array[num10] == ushort.MaxValue)
					{
						int num11 = (int)(num8 << 1);
						num = Math.Max(num, num11 + 2);
						ushort num12;
						array[num11 + 1] = (num12 = ushort.MaxValue);
						array[num11] = num12;
						array[num10] = (ushort)num8++;
					}
					num10 = array[num10] << 1;
					if (((num2 >> 15 - l) & (true ? 1u : 0u)) != 0)
					{
						num10++;
					}
				}
				array[num10] = (ushort)k;
				if ((num2 += num4) > num3)
				{
					throw new XnbError(Error.LZX_9);
				}
			}
			num4 >>= 1;
		}
		if (num2 != num3)
		{
			throw new XnbError(Error.LZX_10);
		}
		ushort[] subArray2 = array[..num];
		Pool.Return(array);
		return subArray2;
	}

	private uint ReadHuffSymbol(BufferReader bufferReader, ushort[] table, byte[] length, uint symbols, uint bits)
	{
		uint num = bufferReader.PeekLzxBits(32);
		uint num2 = table[bufferReader.PeekLzxBits((int)bits)];
		if (num2 >= symbols)
		{
			uint num3 = (uint)(1 << (int)(32 - bits));
			do
			{
				num3 >>= 1;
				num2 <<= 1;
				num2 |= (((num & num3) != 0) ? 1u : 0u);
				if (num3 == 0)
				{
					return 0u;
				}
			}
			while ((num2 = table[num2]) >= symbols);
		}
		bufferReader.BitPosition += length[num2];
		return num2;
	}

	public static void Decompress(BufferReader bufferReader, int compressedTodo, int decompressedTodo)
	{
		int num = 0;
		LZX lZX = new LZX(16);
		byte[] array = Pool.RentByte(decompressedTodo);
		int num2 = 0;
		while (num < compressedTodo)
		{
			int num3;
			int num4;
			if (bufferReader.ReadByte() == byte.MaxValue)
			{
				num3 = bufferReader.ReadLzxInt16();
				num4 = bufferReader.ReadLzxInt16();
				num += 5;
			}
			else
			{
				bufferReader.Skip(-1);
				num4 = bufferReader.ReadLzxInt16();
				num3 = 32768;
				num += 2;
			}
			if (num4 == 0 || num3 == 0)
			{
				break;
			}
			if (num4 > 65536 || num3 > 65536)
			{
				throw new XnbError(Error.LZX_11);
			}
			Logger.Debug(Error.LZX_21, num4, num3);
			lZX.Decompress(bufferReader, num3, num4, array, num2);
			num2 += num3;
			num += num4;
		}
		Logger.Info(Error.LZX_12);
		array.AsSpan(0, decompressedTodo).CopyTo(bufferReader.Buffer.AsSpan(14, decompressedTodo));
		Pool.Return(array);
		lZX.Dispose();
	}
}
