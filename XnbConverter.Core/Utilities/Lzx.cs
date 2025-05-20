using XnbConverter.Configurations;
using XnbConverter.Exceptions;
using XnbConverter.Readers;

namespace XnbConverter.Utilities;

public class Lzx : IDisposable
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

    private static readonly uint[] PositionBase;

    private static readonly byte[] ExtraBits;

    private LzxState _state;

    static Lzx()
    {
        ExtraBits = new byte[52];
        int i = 0;
        int num = 0;
        for (; i < 52; i += 2)
        {
            ExtraBits[i] = ExtraBits[i + 1] = (byte)num;
            if (i != 0 && num < 17)
            {
                num++;
            }
        }

        PositionBase = new uint[51];
        int j = 0;
        int num2 = 0;
        for (; j < 51; j++)
        {
            PositionBase[j] = (uint)num2;
            num2 += 1 << (int)ExtraBits[j];
        }

        Logger.Debug(Error.LZX_13, ExtraBits.ToJoinStr());
        Logger.Debug(Error.LZX_14, PositionBase.ToJoinStr());
    }

    public void Dispose()
    {
        Pool.Return(_state.Window);
    }

    private Lzx(int window)
    {
        if (window < 15 || window > 21)
        {
            throw new XnbError(Error.LZX_1);
        }

        _state.WindowSize = (uint)(1 << window);
        int num = window switch
        {
            21 => 50,
            20 => 42,
            _ => window << 1,
        };
        _state.R0 = _state.R1 = _state.R2 = 1u;
        _state.MainElements = (ushort)(256 + (num << 3));
        _state.HeaderRead = false;
        _state.BlockRemaining = 0u;
        _state.BlockType = 0u;
        _state.WindowPos = 0u;
        _state.PreTreeLen = new byte[20];
        _state.AlignedLen = new byte[8];
        _state.LengthLen = new byte[249];
        _state.MainTreeLen = new byte[656];
        _state.Window = Pool.RentByte((int)_state.WindowSize);
    }

    private void Decompress(BufferReader bufferReader, int frameSize, int blockSize, byte[] decompressed,
        int decompressedIndex)
    {
        if (!_state.HeaderRead)
        {
            uint num = bufferReader.ReadLzxBits(1);
            Logger.Debug(Error.LZX_15, num.B(), num);
            if (num != 0)
            {
                throw new XnbError(Error.LZX_2);
            }

            _state.HeaderRead = true;
        }

        int num2 = frameSize;
        while (num2 > 0)
        {
            if (_state.BlockRemaining == 0)
            {
                _state.BlockType = bufferReader.ReadLzxBits(3);
                Logger.Debug(Error.LZX_16, _state.BlockType.B(), _state.BlockType);
                uint num3 = bufferReader.ReadLzxBits(16);
                uint num4 = bufferReader.ReadLzxBits(8);
                _state.BlockRemaining = (num3 << 8) | num4;
                Logger.Debug(Error.LZX_17, _state.BlockRemaining);
                switch (_state.BlockType)
                {
                    case 2u:
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            _state.AlignedLen[i] = (byte)bufferReader.ReadLzxBits(3);
                        }

                        _state.AlignedTable = DecodeTable(8u, 7u, _state.AlignedLen, 512);
                        goto case 1u;
                    }
                    case 1u:
                        ReadLengths(bufferReader, _state.MainTreeLen, 0, 256u);
                        ReadLengths(bufferReader, _state.MainTreeLen, 256, _state.MainElements);
                        _state.MainTreeTable = DecodeTable(656u, 12u, _state.MainTreeLen, 8192);
                        ReadLengths(bufferReader, _state.LengthLen, 0, 249u);
                        _state.LengthTable = DecodeTable(250u, 12u, _state.LengthLen, 8192);
                        break;
                    case 3u:
                        bufferReader.Align();
                        _state.R0 = bufferReader.ReadUInt32();
                        _state.R1 = bufferReader.ReadUInt32();
                        _state.R2 = bufferReader.ReadUInt32();
                        break;
                    default:
                        throw new XnbError(Error.LZX_3, _state.BlockType);
                }
            }

            int num5;
            while ((num5 = (int)_state.BlockRemaining) > 0 && num2 > 0)
            {
                if (num5 > num2)
                {
                    num5 = num2;
                }

                num2 -= num5;
                _state.BlockRemaining -= (uint)num5;
                _state.WindowPos &= _state.WindowSize - 1;
                if (_state.WindowPos + num5 > _state.WindowSize)
                {
                    throw new XnbError(Error.LZX_4);
                }

                switch (_state.BlockType)
                {
                    case 2u:
                        while (num5 > 0)
                        {
                            int num6 = (int)ReadHuffSymbol(bufferReader, _state.MainTreeTable, _state.MainTreeLen,
                                656u, 12u);
                            if (num6 < 256)
                            {
                                _state.Window[_state.WindowPos++] = (byte)num6;
                                num5--;
                                continue;
                            }

                            num6 -= 256;
                            int num7 = num6 & 7;
                            if (num7 == 7)
                            {
                                int num8 = (int)ReadHuffSymbol(bufferReader, _state.LengthTable, _state.LengthLen,
                                    250u, 12u);
                                num7 += num8;
                            }

                            num7 += 2;
                            int num9 = num6 >> 3;
                            if (num9 > 2)
                            {
                                int num10 = ExtraBits[num9];
                                num9 = (int)(PositionBase[num9] - 2);
                                if (num10 <= 3)
                                {
                                    num9 = num10 == 3
                                        ? num9 + (int)ReadHuffSymbol(bufferReader, _state.AlignedTable,
                                            _state.AlignedLen, 8u, 7u)
                                        : num10 <= 0 ? 1 : num9 + (int)bufferReader.ReadLzxBits((byte)num10);
                                }
                                else
                                {
                                    num10 -= 3;
                                    int num11 = (int)bufferReader.ReadLzxBits((byte)num10);
                                    num9 += num11 << 3;
                                    int num14 = (int)ReadHuffSymbol(bufferReader, _state.AlignedTable,
                                        _state.AlignedLen, 8u, 7u);
                                    num9 += num14;
                                }

                                _state.R2 = _state.R1;
                                _state.R1 = _state.R0;
                                _state.R0 = (uint)num9;
                            }
                            else
                            {
                                switch (num9)
                                {
                                    case 0:
                                        num9 = (int)_state.R0;
                                        break;
                                    case 1:
                                        num9 = (int)_state.R1;
                                        _state.R1 = _state.R0;
                                        _state.R0 = (uint)num9;
                                        break;
                                    default:
                                        num9 = (int)_state.R2;
                                        _state.R2 = _state.R0;
                                        _state.R0 = (uint)num9;
                                        break;
                                }
                            }

                            int windowPos = (int)_state.WindowPos;
                            num5 -= num7;
                            int num12;
                            if (_state.WindowPos >= num9)
                            {
                                num12 = windowPos - num9;
                            }
                            else
                            {
                                num12 = windowPos + ((int)_state.WindowSize - num9);
                                int num13 = num9 - (int)_state.WindowPos;
                                if (num13 < num7)
                                {
                                    num7 -= num13;
                                    _state.WindowPos += (uint)num13;
                                    while (num13-- > 0)
                                    {
                                        _state.Window[windowPos++] = _state.Window[num12++];
                                    }

                                    num12 = 0;
                                }
                            }

                            _state.WindowPos += (uint)num7;
                            while (num7-- > 0)
                            {
                                _state.Window[windowPos++] = _state.Window[num12++];
                            }
                        }

                        break;
                    case 1u:
                        while (num5 > 0)
                        {
                            int num6 = (int)ReadHuffSymbol(bufferReader, _state.MainTreeTable, _state.MainTreeLen,
                                656u, 12u);
                            if (num6 < 256)
                            {
                                _state.Window[_state.WindowPos++] = (byte)num6;
                                num5--;
                                continue;
                            }

                            num6 -= 256;
                            int num7 = num6 & 7;
                            if (num7 == 7)
                            {
                                int num8 = (int)ReadHuffSymbol(bufferReader, _state.LengthTable, _state.LengthLen,
                                    250u, 12u);
                                num7 += num8;
                            }

                            num7 += 2;
                            int num9 = num6 >> 3;
                            if (num9 > 2)
                            {
                                if (num9 != 3)
                                {
                                    int num10 = ExtraBits[num9];
                                    int num11 = (int)bufferReader.ReadLzxBits((byte)num10);
                                    num9 = (int)(PositionBase[num9] - 2) + num11;
                                }
                                else
                                {
                                    num9 = 1;
                                }

                                _state.R2 = _state.R1;
                                _state.R1 = _state.R0;
                                _state.R0 = (uint)num9;
                            }
                            else
                            {
                                switch (num9)
                                {
                                    case 0:
                                        num9 = (int)_state.R0;
                                        break;
                                    case 1:
                                        num9 = (int)_state.R1;
                                        _state.R1 = _state.R0;
                                        _state.R0 = (uint)num9;
                                        break;
                                    default:
                                        num9 = (int)_state.R2;
                                        _state.R2 = _state.R0;
                                        _state.R0 = (uint)num9;
                                        break;
                                }
                            }

                            int windowPos = (int)_state.WindowPos;
                            num5 -= num7;
                            int num12;
                            if (_state.WindowPos >= num9)
                            {
                                num12 = windowPos - num9;
                            }
                            else
                            {
                                num12 = windowPos + ((int)_state.WindowSize - num9);
                                int num13 = num9 - (int)_state.WindowPos;
                                if (num13 < num7)
                                {
                                    num7 -= num13;
                                    _state.WindowPos += (uint)num13;
                                    while (num13-- > 0)
                                    {
                                        _state.Window[windowPos++] = _state.Window[num12++];
                                    }

                                    num12 = 0;
                                }
                            }

                            _state.WindowPos += (uint)num7;
                            while (num7-- > 0)
                            {
                                _state.Window[windowPos++] = _state.Window[num12++];
                            }
                        }

                        break;
                    case 3u:
                        if (bufferReader.BytePosition + num5 > blockSize)
                        {
                            throw new XnbError(Error.LZX_5, blockSize, bufferReader.BytePosition, num5);
                        }

                        bufferReader.Buffer.AsSpan(bufferReader.BytePosition, num5)
                            .CopyTo(_state.Window.AsSpan((int)_state.WindowPos, num5));
                        bufferReader.BytePosition += num5;
                        _state.WindowPos += (uint)num5;
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
        int num15 = (int)(_state.WindowPos == 0 ? _state.WindowSize : _state.WindowPos);
        num15 -= frameSize;
        _state.Window
            .AsSpan(num15, frameSize)
            .CopyTo(decompressed.AsSpan(decompressedIndex, frameSize));
    }

    private void ReadLengths(BufferReader buffer, Span<byte> table, int first, uint last)
    {
        int i;
        for (i = 0; i < 20; i++)
        {
            _state.PreTreeLen[i] = (byte)buffer.ReadLzxBits(4);
        }

        _state.PreTreeTable = DecodeTable(20u, 6u, _state.PreTreeLen, 128);
        i = first;
        while (i < last)
        {
            int num = (int)ReadHuffSymbol(buffer, _state.PreTreeTable, _state.PreTreeLen, 20u, 6u);
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
                    num = (int)ReadHuffSymbol(buffer, _state.PreTreeTable, _state.PreTreeLen, 20u, 6u);
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
                        array[num11 + 1] = num12 = ushort.MaxValue;
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
                num2 |= (num & num3) != 0 ? 1u : 0u;
                if (num3 == 0)
                {
                    return 0u;
                }
            } while ((num2 = table[num2]) >= symbols);
        }

        bufferReader.BitPosition += length[num2];
        return num2;
    }

    public static void Decompress(BufferReader bufferReader, int compressedTodo, int decompressedTodo)
    {
        int num = 0;
        Lzx lzx = new Lzx(16);
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
            lzx.Decompress(bufferReader, num3, num4, array, num2);
            num2 += num3;
            num += num4;
        }

        Logger.Info(Error.LZX_12);
        array.AsSpan(0, decompressedTodo).CopyTo(bufferReader.Buffer.AsSpan(14, decompressedTodo));
        Pool.Return(array);
        lzx.Dispose();
    }
}