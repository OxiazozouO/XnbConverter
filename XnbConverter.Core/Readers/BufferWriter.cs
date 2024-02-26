using System.Text;
using XnbConverter.Utilities;

namespace XnbConverter.Readers;

public class BufferWriter : IDisposable
{
    public byte[] Buffer;

    public void SaveBufferToFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Create);
        fs.Write(Buffer, 0, BytePosition);
    }

    public int BytePosition = 0;
    private readonly bool _isPool = true;

    public BufferWriter(int size = Pool.LongSize)
    {
        Buffer = Pool.RentByte(size);
    }

    public BufferWriter(byte[] buffer)
    {
        _isPool = false;
        Buffer = buffer;
    }

    public void Skip(int off)
    {
        BytePosition += off;
    }

    /**
     * 向缓冲区写入字节
     * @param {Mixed} bytes 要写入的字节
     */
    public void Write(byte[] bytes)
    {
        Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
        BytePosition += bytes.Length;
    }

    public void Write(byte[] bytes, int sta, int len)
    {
        Array.Copy(bytes, sta, Buffer, BytePosition, len);
        BytePosition += len;
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(Buffer.AsSpan(BytePosition));
        BytePosition += bytes.Length;
    }

    /**
     * 向缓冲区写入字节
     * @param {Mixed} bytes 要写入的字节
     */
    public void WriteAsciiString(string str)
    {
        var bytes = Encoding.ASCII.GetBytes(str);
        Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
        BytePosition += bytes.Length;
    }

    /**
     * 向缓冲区写入一个字节
     * @param {Mixed} byte 要写入的字节
     */
    public void WriteByte(byte b)
    {
        Buffer[BytePosition++] = b;
    }

    /**
     * 向缓冲区写入一个int8
     * @param {Number} number
     */
    public void WriteInt8(sbyte number)
    {
        Buffer[BytePosition++] = (byte)number; //1
    }

    /**
     * 向缓冲区写入一个uint8
     * @param {Number} number
     */
    public void WriteUInt8(byte number)
    {
        Buffer[BytePosition++] = number; //1
    }

    /**
     * 向缓冲区写入一个int16
     * @param {Number} number
     */
    public void WriteInt16(short number)
    {
        var bytes = BitConverter.GetBytes(number);
        Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
        BytePosition += bytes.Length; //2
    }

    /**
     * 向缓冲区写入一个 uint16
     * @param {Number} number
     */
    public void WriteUInt16(ushort number)
    {
        var bytes = BitConverter.GetBytes(number);
        Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
        BytePosition += bytes.Length;
    }

    /**
     * 向缓冲区写入一个 int32
     * @param {Number} number
     */
    public void WriteInt32(int number)
    {
        var bytes = BitConverter.GetBytes(number);
        Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
        BytePosition += bytes.Length;
    }

    /**
     * 向缓冲区写入一个 uint32
     * @param {Number} number
     */
    public void WriteUInt32(uint number)
    {
        var bytes = BitConverter.GetBytes(number);
        Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
        BytePosition += bytes.Length;
    }

    /**
     * 向缓冲区写入一个 uint32
     * @param {Number} number
     */
    public void WriteUInt32(uint number, int offset)
    {
        var bytes = BitConverter.GetBytes(number);
        Array.Copy(bytes, 0, Buffer, offset, bytes.Length);
    }

    /**
     * 向缓冲区写入一个 float
     * @param {Number} number
     */
    public void WriteSingle(float number)
    {
        var bytes = BitConverter.GetBytes(number);
        Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
        BytePosition += bytes.Length; //4
    }

    /**
     * 向缓冲区写入一个 double
     * @param {Number} number
     */
    public void WriteDouble(double number)
    {
        var bytes = BitConverter.GetBytes(number);
        Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
        BytePosition += bytes.Length; //4
    }

    /**
     * 写入一个布尔值
     * @public
     * @returns {Number}
     */
    public void WriteBoolean(bool b)
    {
        var bytes = BitConverter.GetBytes(b);
        Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
        BytePosition += bytes.Length;
    }

    /**
     * 向缓冲区写入一个7位数
     * @param {Number} number
     */
    public void Write7BitNumber(int number)
    {
        do
        {
            var a = number & 0x7F;
            number >>= 7;
            if (number > 0)
                a |= 0x80;
            Buffer[BytePosition++] = (byte)(sbyte)a;
        } while (number > 0);
    }

    public void Dispose()
    {
        if (_isPool)
            Pool.Return(Buffer);
    }
}