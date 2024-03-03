using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using XnbConverter.Entity;
using XnbConverter.Utilities;
using static XnbConverter.Entity.XnbObject.CompressedMasks;
using static XnbConverter.XNB;

namespace XnbConverter.Readers;

/**
 * 创建Reader类的实例。
 * @constructor
 * @param {String} filename 要读取的文件名。
 */
public class BufferReader : IDisposable
{
    private const int LITTLE_ENDIAN = 0;
    private const int BIG_ENDIAN = 1;
    private readonly bool _isPool = true;
    private int _bitOffset;
    private int _byteOrder = LITTLE_ENDIAN;
    private int _lastDebugLoc = 0;

    //返回缓冲区。
    public byte[] Buffer;

    /**
     * 获取缓冲区的查找索引。
     * @public
     * @property bytePosition 字节位置
     * @return {Number} Reurns 重新获得缓冲区查找索引。
     */
    public int BytePosition;

    private BufferReader()
    {
    }

    public BufferReader(byte[] buffer)
    {
        Buffer = buffer;
        Size = Buffer.Length;
        _isPool = false;
    }

    public int BitPosition
    {
        //获取当前的位读取位置。
        get => _bitOffset;
        //设置位位置，限制在16位帧内。
        set
        {
            // 当倒回时，将其重置为
            if (value < 0)
                value = 16 - value;
            // 设置偏移量，并将其限制在16位帧内
            _bitOffset = value % 16;
            // 获取超过16位帧的位范围的字节寻找
            var byteSeek = (value - Math.Abs(value) % 16) / 16 * 2;
            // 寻找溢出的16位帧
            Skip(byteSeek);
        }
    }

    //缓冲区的大小。
    public int Size { get; private set; }

    /**
     * 关闭和归还相关资源
     */
    public void Dispose()
    {
        if (_isPool) Pool.Return(Buffer);
    }

    public static BufferReader FormXnbFile(string filename)
    {
        // 确保文件存在
        if (!File.Exists(filename))
            throw new XnbError(Helpers.I18N["BufferReader.1"], filename);
        var result = new BufferReader();
        //设置缓冲区流的字节顺序。
        result._byteOrder = LITTLE_ENDIAN;

        // 按照xnb提供的解压缩后大小初始化缓冲区
        using var fs = new FileStream(filename, FileMode.Open);
        result.Size = (int)fs.Length;
        fs.Position = XnbConstants.FILE_SIZE_INDEX - 1;
        var flags = (XnbObject.CompressedMasks)fs.ReadByte();
        var b = (flags & (Lzx | Lz4)) != 0;
        var size = 0u;
        if (b)
        {
            fs.Position = XnbConstants.CONTENT_ORIGINAL_SIZE_INDEX;
            size = XnbConstants.XNB_COMPRESSED_PROLOGUE_SIZE;
        }

        var t = new byte[sizeof(uint)];
        fs.Read(t, 0, sizeof(uint));
        size += BitConverter.ToUInt32(t);

        result.Buffer = Pool.RentByte((int)size);

        var span = result.Buffer.AsSpan(0, result.Size);
        fs.Position = 0;
        fs.Read(span);
        return result;
    }

    public static BufferReader FormFile(string filename)
    {
        return new BufferReader(File.ReadAllBytes(filename));
    }

    /**
     * 在缓冲区中跳过特定长度。
     * @public
     * @param {Number} index 设置缓冲区的寻找索引。
     * @param {Number} origin 寻找的起始位置
     * @returns {Number} 返回偏移量的变化值
     */
    public void Skip(int index)
    {
        BytePosition = Math.Max(BytePosition + index, 0);
        if (BytePosition < 0 || BytePosition > Buffer.Length)
            throw new XnbError(Helpers.I18N["BufferReader.2"], BytePosition, Buffer.Length);
    }

    public byte[] GetBuffer()
    {
        return Buffer[..BytePosition];
    }

    /**
     * 读取指定数量的字节。
     * @public
     * @method Read
     * @param {Number} count 要读取的字节数。
     * @returns {Buffer} 缓冲区的内容。
     */
    public byte[] Read(int count)
    {
        // 从缓冲区读取
        var buffer = Buffer.AsSpan(BytePosition, count).ToArray();
        // 前进偏移量
        Skip(count);
        // 调试此读取
        //if (this._debug_mode) this.debug();
        // 返回读取的缓冲区
        return buffer;
    }

    public ReadOnlySpan<byte> ReadOnly(int count)
    {
        // 从缓冲区读取
        ReadOnlySpan<byte> buffer = Buffer.AsSpan(BytePosition, count);
        // 前进偏移量
        Skip(count);
        // 调试此读取
        //if (this._debug_mode) this.debug();
        // 返回读取的缓冲区
        return buffer;
    }

    /**
     * 读取单个字节。
     * @public
     * @returns {Number}
     */
    public byte ReadByte()
    {
        // 从缓冲区读取
        var buffer = Buffer[BytePosition];
        // 前进偏移量
        Skip(1);
        // 返回读取的缓冲区
        return buffer;
    }

    /**
     * 读取布尔值
     * @public
     * @returns {Number}
     */
    public bool ReadBoolean()
    {
        return ReadByte() != 0;
    }

    /**
     * 读取int8。
     * @public
     * @returns {Number}
     */
    public sbyte ReadInt8()
    {
        return (sbyte)ReadByte();
    }

    /**
     * 读取uint8。
     * @public
     * @returns {Number}
     */
    public byte ReadUInt8()
    {
        return ReadByte();
    }

    /**
     * 读取uint16。
     * @public
     * @returns {Number}
     */
    public ushort ReadUInt16()
    {
        var read = Read(sizeof(ushort)); //2
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(read);
        return BitConverter.ToUInt16(read, 0);
    }

    /**
     * 读取uint32。
     * @public
     * @returns {Number}
     */
    public uint ReadUInt32()
    {
        var read = Read(sizeof(uint)); //4
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(read);
        return BitConverter.ToUInt32(read, 0);
    }

    /**
     * 读取int16。
     * @public
     * @returns {Number}
     */
    public short ReadInt16()
    {
        var read = Read(sizeof(short)); //2
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(read);
        return BitConverter.ToInt16(read, 0);
    }

    /**
     * 读取int32。
     * @public
     * @returns {Number}
     */
    public int ReadInt32()
    {
        var read = Read(sizeof(int)); //4
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(read);
        return BitConverter.ToInt32(read, 0);
    }

    /**
     * 读取float。
     * @public
     * @returns {Number}
     */
    public float ReadSingle()
    {
        var read = Read(sizeof(float)); //4
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(read);
        return BitConverter.ToSingle(read, 0);
    }

    /**
     * 读取double。
     * @public
     * @returns {Number}
     */
    public double ReadDouble()
    {
        var read = Read(sizeof(double)); //8
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(read);
        return BitConverter.ToDouble(read, 0);
    }

    /**
     * 读取字符串。
     * @public
     * @param {Number} [count]
     * @returns {String}
     */
    public string ReadString(int count = 0)
    {
        if (count != 0) return Encoding.Default.GetString(ReadOnly(count));
        var chars = new StringBuilder();
        while (PeekByte() != 0x0) chars.Append((char)ReadByte());
        Skip(1);
        return chars.ToString();
    }

    /**
     * 预览缓冲区内容，而不实际前进。
     * @public
     * @param {Number} count 预览的字节数。
     * @returns {byte[]} 缓冲区内容。
     */
    public byte[] Peek(int count)
    {
        var bytes = Buffer.AsSpan(BytePosition, count).ToArray();
        // 返回缓冲区内容
        return bytes;
    }

    /**
     * 预览一个字节。
     * @public
     * @returns {Number}
     */
    public byte PeekByte()
    {
        return Buffer[BytePosition];
    }

    /**
     * 预览一个uint16。
     * @public
     * @returns {Number}
     */
    public ushort PeekUInt16()
    {
        var buffer = Peek(sizeof(ushort)); //2
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(buffer);
        return BitConverter.ToUInt16(buffer, 0);
    }

    /**
     * 预览一个uint32。
     * @public
     * @returns {Number}
     */
    public uint PeekUInt32()
    {
        var buffer = Peek(sizeof(uint)); //4
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(buffer);
        return BitConverter.ToUInt32(buffer, 0);
    }

    /**
     * 预览一个int16。
     * @public
     * @returns {Number}
     */
    public short PeekInt16()
    {
        var buffer = Peek(sizeof(short)); //2
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(buffer);
        return BitConverter.ToInt16(buffer, 0);
    }

    /**
     * 预览一个int32。
     * @public
     * @returns {Number}
     */
    public int PeekInt32()
    {
        var buffer = Peek(sizeof(int)); //24
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(buffer);
        return BitConverter.ToInt32(buffer, 0);
    }

    /**
     * 预览一个float。
     * @public
     * @returns {Number}
     */
    public float PeekSingle()
    {
        var buffer = Peek(sizeof(float)); //4
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(buffer);
        return BitConverter.ToSingle(buffer, 0);
    }

    /**
     * 预览一个double。
     * @public
     * @returns {Number}
     */
    public double PeekDouble()
    {
        var buffer = Peek(sizeof(double)); //8
        if (_byteOrder == BIG_ENDIAN)
            Array.Reverse(buffer);
        return BitConverter.ToDouble(buffer, 0);
    }

    /**
     * 预览一个字符串。
     * @public
     * @param {Number} [count]
     * @returns {String}
     */
    public string PeekString(int count = 0)
    {
        if (count != 0)
            return Encoding.Default.GetString(Peek(count));
        var bytePosition = BytePosition;
        var sb = new StringBuilder();
        while (PeekByte() != 0x0)
            sb.Append(ReadString(1));
        BytePosition = bytePosition;
        return sb.ToString();
    }


    /**
     * 读取一个7位数。
     * @public
     * @method read7BitNumber
     * @returns {Number} 返回读取的数。
     */
    public int Read7BitNumber()
    {
        var result = 0;
        var bitsRead = 0;
        int value;
        // 循环读取位
        do
        {
            value = ReadByte();
            result |= (value & 0x7F) << bitsRead;
            bitsRead += 7;
        } while ((value & 0x80) != 0);

        return result;
    }

    /**
     * 读取用于LZX压缩的位。
     * @public
     * @method readLZXBits
     * @param {Number} bits
     * @returns {Number}
     */
    public uint ReadLzxBits(int bits)
    {
        // 初始化循环的值
        int bitsLeft = bits, bitsInFrame, offset, value;
        uint read = 0;
        ushort peek;
        // 以16位为单位读取位
        while (bitsLeft > 0)
        {
            // 预览一个16位值

            peek = MemoryMarshal.Cast<byte, ushort>(Buffer.AsSpan(BytePosition, sizeof(ushort)))[0];
            // 将位限制在剩余的16位框架中，只读取剩余的位数
            bitsInFrame = Math.Min(Math.Max(bitsLeft, 0), 16 - BitPosition);
            // 根据当前位置和位数设置偏移量
            offset = 16 - BitPosition - bitsInFrame;

            // 创建掩码，并将掩码左移偏移量 <<
            // 然后将返回值右移回掩码空间 >>
            value = (peek & (((1 << bitsInFrame) - 1) << offset)) >> offset;

            // 从剩余的位数中移除已读取的位
            bitsLeft -= bitsInFrame;
            // 将已读取的位添加到位位置
            BitPosition += bitsInFrame;

            // 将值左移以便在循环中读取
            read |= (uint)(value << bitsLeft);
        }

        // 返回已读取的位
        return read;
    }

    /**
     * 用于预览位。
     * @public
     * @method peekLZXBits
     * @param {Number} bits
     * @returns {Number}
     */
    public uint PeekLzxBits(int bits)
    {
        // 存储当前位位置
        var bitPosition = BitPosition;
        // 存储当前字节位置
        var bytePosition = BytePosition;

        // 像正常情况下一样读取位
        var read = ReadLzxBits(bits);

        // 只需倒回位位置，这也会在需要时倒回字节位置
        BitPosition = bitPosition;
        // 恢复字节位置
        BytePosition = bytePosition;

        // 返回预览值
        return read;
    }

    /**
     * 从LZX位流中读取一个16位整数
     * 
     * 字节顺序与位流相反，16位整数存储为LSB -> MSB (字节)的顺序
     * abc[...]xyzABCDEF 的位表示为：
     * [ijklmnop][abcdefgh][yzABCDEF][qrstuvwx]
     * 
     * @public
     * @method readLZXInt16
     * @param {Boolean} seek
     * @returns {Number}
     */
    public int ReadLzxInt16(bool seek = true)
    {
        // 读取下两个字节的数据
        int lsB = ReadByte();
        int msB = ReadByte();

        // 倒回寻找头
        if (!seek) Skip(-2);

        // 设置值
        return (lsB << 8) | msB;
    }

    /**
     * 对齐到16位偏移量。
     * @public
     * @method align
     */
    public void Align()
    {
        if (BitPosition > 0)
            BitPosition += 16 - BitPosition;
    }
}