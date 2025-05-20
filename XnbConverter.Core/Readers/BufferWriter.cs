using System.Text;
using XnbConverter.Utilities;

namespace XnbConverter.Readers;

public class BufferWriter : IDisposable
{
	private readonly bool _isPool = true;

	public byte[] Buffer;

	public int BytePosition;

	public BufferWriter(int size = Pool.LongSize)
	{
		Buffer = Pool.RentByte(size);
	}

	public BufferWriter(byte[] buffer)
	{
		_isPool = false;
		Buffer = buffer;
	}

	public void Dispose()
	{
		if (_isPool)
		{
			Pool.Return(Buffer);
		}
	}

	public void SaveBufferToFile(string path)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Create);
		fileStream.Write(Buffer, 0, BytePosition);
	}

	public void Skip(int off)
	{
		BytePosition += off;
	}

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

	public void WriteAsciiString(string str)
	{
		byte[] bytes = Encoding.ASCII.GetBytes(str);
		Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
		BytePosition += bytes.Length;
	}

	public void WriteByte(byte b)
	{
		Buffer[BytePosition++] = b;
	}

	public void WriteInt8(sbyte number)
	{
		Buffer[BytePosition++] = (byte)number;
	}

	public void WriteUInt8(byte number)
	{
		Buffer[BytePosition++] = number;
	}

	public void WriteInt16(short number)
	{
		byte[] bytes = BitConverter.GetBytes(number);
		Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
		BytePosition += bytes.Length;
	}

	public void WriteUInt16(ushort number)
	{
		byte[] bytes = BitConverter.GetBytes(number);
		Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
		BytePosition += bytes.Length;
	}

	public void WriteInt32(int number)
	{
		byte[] bytes = BitConverter.GetBytes(number);
		Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
		BytePosition += bytes.Length;
	}

	public void WriteUInt32(uint number)
	{
		byte[] bytes = BitConverter.GetBytes(number);
		Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
		BytePosition += bytes.Length;
	}

	public void WriteUInt32(uint number, int offset)
	{
		byte[] bytes = BitConverter.GetBytes(number);
		Array.Copy(bytes, 0, Buffer, offset, bytes.Length);
	}

	public void WriteSingle(float number)
	{
		byte[] bytes = BitConverter.GetBytes(number);
		Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
		BytePosition += bytes.Length;
	}

	public void WriteDouble(double number)
	{
		byte[] bytes = BitConverter.GetBytes(number);
		Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
		BytePosition += bytes.Length;
	}

	public void WriteBoolean(bool b)
	{
		byte[] bytes = BitConverter.GetBytes(b);
		Array.Copy(bytes, 0, Buffer, BytePosition, bytes.Length);
		BytePosition += bytes.Length;
	}

	public void Write7BitNumber(int number)
	{
		do
		{
			int num = number & 0x7F;
			number >>= 7;
			if (number > 0)
			{
				num |= 0x80;
			}
			Buffer[BytePosition++] = (byte)(sbyte)num;
		}
		while (number > 0);
	}
}
