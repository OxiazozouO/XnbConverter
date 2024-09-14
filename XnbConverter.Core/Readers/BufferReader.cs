using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using XnbConverter.Exceptions;
using XnbConverter.Utilities;

namespace XnbConverter.Readers;

public class BufferReader : IDisposable
{
	private const int LITTLE_ENDIAN = 0;

	private const int BIG_ENDIAN = 1;

	private readonly bool _isPool = true;

	private int _bitOffset;

	private int _byteOrder;

	private int _lastDebugLoc;

	public byte[] Buffer;

	public int BytePosition;

	public int BitPosition
	{
		get
		{
			return _bitOffset;
		}
		set
		{
			if (value < 0)
			{
				value = 16 - value;
			}
			_bitOffset = value % 16;
			int index = (value - Math.Abs(value) % 16) / 16 * 2;
			Skip(index);
		}
	}

	public int Size { get; private set; }

	private BufferReader()
	{
	}

	public BufferReader(byte[] buffer)
	{
		Buffer = buffer;
		Size = Buffer.Length;
		_isPool = false;
	}

	public void Dispose()
	{
		if (_isPool)
		{
			Pool.Return(Buffer);
		}
	}

	public static BufferReader FormXnbFile(string filename)
	{
		if (!File.Exists(filename))
		{
			throw new XnbError(Error.BufferReader_1, filename);
		}
		BufferReader bufferReader = new BufferReader();
		bufferReader._byteOrder = 0;
		using FileStream fileStream = new FileStream(filename, FileMode.Open);
		bufferReader.Size = (int)fileStream.Length;
		fileStream.Position = 5L;
		bool num = ((byte)fileStream.ReadByte() & 0xC0) != 0;
		uint num2 = 0u;
		if (num)
		{
			fileStream.Position = 10L;
			num2 = 14u;
		}
		byte[] array = new byte[4];
		fileStream.Read(array, 0, 4);
		num2 += BitConverter.ToUInt32(array);
		bufferReader.Buffer = Pool.RentByte((int)num2);
		Span<byte> buffer = bufferReader.Buffer.AsSpan(0, bufferReader.Size);
		fileStream.Position = 0L;
		fileStream.Read(buffer);
		return bufferReader;
	}

	public static BufferReader FormFile(string filename)
	{
		return new BufferReader(File.ReadAllBytes(filename));
	}

	public void Skip(int index)
	{
		BytePosition = Math.Max(BytePosition + index, 0);
		if (BytePosition < 0 || BytePosition > Buffer.Length)
		{
			throw new XnbError(Error.BufferReader_2, BytePosition, Buffer.Length);
		}
	}

	public byte[] GetBuffer()
	{
		return Buffer[..BytePosition];
	}

	public byte[] Read(int count)
	{
		byte[] result = Buffer.AsSpan(BytePosition, count).ToArray();
		Skip(count);
		return result;
	}

	public ReadOnlySpan<byte> ReadOnly(int count)
	{
		ReadOnlySpan<byte> result = Buffer.AsSpan(BytePosition, count);
		Skip(count);
		return result;
	}

	public byte ReadByte()
	{
		byte result = Buffer[BytePosition];
		Skip(1);
		return result;
	}

	public bool ReadBoolean()
	{
		return ReadByte() != 0;
	}

	public sbyte ReadInt8()
	{
		return (sbyte)ReadByte();
	}

	public byte ReadUInt8()
	{
		return ReadByte();
	}

	public ushort ReadUInt16()
	{
		byte[] array = Read(2);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToUInt16(array, 0);
	}

	public uint ReadUInt32()
	{
		byte[] array = Read(4);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToUInt32(array, 0);
	}

	public short ReadInt16()
	{
		byte[] array = Read(2);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToInt16(array, 0);
	}

	public int ReadInt32()
	{
		byte[] array = Read(4);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToInt32(array, 0);
	}

	public float ReadSingle()
	{
		byte[] array = Read(4);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToSingle(array, 0);
	}

	public double ReadDouble()
	{
		byte[] array = Read(8);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToDouble(array, 0);
	}

	public string ReadString(int count = 0)
	{
		if (count != 0)
		{
			return Encoding.Default.GetString(ReadOnly(count));
		}
		StringBuilder stringBuilder = new StringBuilder();
		while (PeekByte() != 0)
		{
			stringBuilder.Append((char)ReadByte());
		}
		Skip(1);
		return stringBuilder.ToString();
	}

	public byte[] Peek(int count)
	{
		return Buffer.AsSpan(BytePosition, count).ToArray();
	}

	public byte PeekByte()
	{
		return Buffer[BytePosition];
	}

	public ushort PeekUInt16()
	{
		byte[] array = Peek(2);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToUInt16(array, 0);
	}

	public uint PeekUInt32()
	{
		byte[] array = Peek(4);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToUInt32(array, 0);
	}

	public short PeekInt16()
	{
		byte[] array = Peek(2);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToInt16(array, 0);
	}

	public int PeekInt32()
	{
		byte[] array = Peek(4);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToInt32(array, 0);
	}

	public float PeekSingle()
	{
		byte[] array = Peek(4);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToSingle(array, 0);
	}

	public double PeekDouble()
	{
		byte[] array = Peek(8);
		if (_byteOrder == 1)
		{
			Array.Reverse(array);
		}
		return BitConverter.ToDouble(array, 0);
	}

	public string PeekString(int count = 0)
	{
		if (count != 0)
		{
			return Encoding.Default.GetString(Peek(count));
		}
		int bytePosition = BytePosition;
		StringBuilder stringBuilder = new StringBuilder();
		while (PeekByte() != 0)
		{
			stringBuilder.Append(ReadString(1));
		}
		BytePosition = bytePosition;
		return stringBuilder.ToString();
	}

	public int Read7BitNumber()
	{
		int num = 0;
		int num2 = 0;
		int num3;
		do
		{
			num3 = ReadByte();
			num |= (num3 & 0x7F) << num2;
			num2 += 7;
		}
		while (((uint)num3 & 0x80u) != 0);
		return num;
	}

	public uint ReadLzxBits(int bits)
	{
		int num = bits;
		uint num2 = 0u;
		while (num > 0)
		{
			ushort num3 = MemoryMarshal.Cast<byte, ushort>(Buffer.AsSpan(BytePosition, 2))[0];
			int num4 = Math.Min(Math.Max(num, 0), 16 - BitPosition);
			int num5 = 16 - BitPosition - num4;
			int num6 = (num3 & ((1 << num4) - 1 << num5)) >> num5;
			num -= num4;
			BitPosition += num4;
			num2 |= (uint)(num6 << num);
		}
		return num2;
	}

	public uint PeekLzxBits(int bits)
	{
		int bitPosition = BitPosition;
		int bytePosition = BytePosition;
		uint result = ReadLzxBits(bits);
		BitPosition = bitPosition;
		BytePosition = bytePosition;
		return result;
	}

	public int ReadLzxInt16(bool seek = true)
	{
		byte num = ReadByte();
		int num2 = ReadByte();
		if (!seek)
		{
			Skip(-2);
		}
		return (num << 8) | num2;
	}

	public void Align()
	{
		if (BitPosition > 0)
		{
			BitPosition += 16 - BitPosition;
		}
	}
}
