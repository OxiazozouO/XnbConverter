using System;
using System.Collections.Generic;

namespace NVorbis;

/// <summary>
/// A single data packet from a logical Vorbis stream.
/// </summary>
public abstract class DataPacket
{
	/// <summary>
	/// Defines flags to apply to the current packet
	/// </summary>
	[Flags]
	protected enum PacketFlags : byte
	{
		/// <summary>
		/// Packet is first since reader had to resync with stream.
		/// </summary>
		IsResync = 1,
		/// <summary>
		/// Packet is the last in the logical stream.
		/// </summary>
		IsEndOfStream = 2,
		/// <summary>
		/// Packet does not have all its data available.
		/// </summary>
		IsShort = 4,
		/// <summary>
		/// Packet has a granule count defined.
		/// </summary>
		HasGranuleCount = 8,
		/// <summary>
		/// Flag for use by inheritors.
		/// </summary>
		User1 = 0x10,
		/// <summary>
		/// Flag for use by inheritors.
		/// </summary>
		User2 = 0x20,
		/// <summary>
		/// Flag for use by inheritors.
		/// </summary>
		User3 = 0x40,
		/// <summary>
		/// Flag for use by inheritors.
		/// </summary>
		User4 = 0x80
	}

	private ulong _bitBucket;

	private int _bitCount;

	private int _readBits;

	private byte _overflowBits;

	private PacketFlags _packetFlags;

	private long _granulePosition;

	private long _pageGranulePosition;

	private int _length;

	private int _granuleCount;

	private int _pageSequenceNumber;

	/// <summary>
	/// Gets whether the packet was found after a stream resync.
	/// </summary>
	public bool IsResync
	{
		get
		{
			return GetFlag(PacketFlags.IsResync);
		}
		internal set
		{
			SetFlag(PacketFlags.IsResync, value);
		}
	}

	/// <summary>
	/// Gets the position of the last granule in the packet.
	/// </summary>
	public long GranulePosition
	{
		get
		{
			return _granulePosition;
		}
		set
		{
			_granulePosition = value;
		}
	}

	/// <summary>
	/// Gets the position of the last granule in the page the packet is in.
	/// </summary>
	public long PageGranulePosition
	{
		get
		{
			return _pageGranulePosition;
		}
		internal set
		{
			_pageGranulePosition = value;
		}
	}

	/// <summary>
	/// Gets the length of the packet.
	/// </summary>
	public int Length
	{
		get
		{
			return _length;
		}
		protected set
		{
			_length = value;
		}
	}

	/// <summary>
	/// Gets whether the packet is the last one in the logical stream.
	/// </summary>
	public bool IsEndOfStream
	{
		get
		{
			return GetFlag(PacketFlags.IsEndOfStream);
		}
		internal set
		{
			SetFlag(PacketFlags.IsEndOfStream, value);
		}
	}

	/// <summary>
	/// Gets the number of bits read from the packet.
	/// </summary>
	public long BitsRead => _readBits;

	/// <summary>
	/// Gets the number of granules in the packet.  If <c>null</c>, the packet has not been decoded yet.
	/// </summary>
	public int? GranuleCount
	{
		get
		{
			if (GetFlag(PacketFlags.HasGranuleCount))
			{
				return _granuleCount;
			}
			return null;
		}
		set
		{
			if (value.HasValue)
			{
				_granuleCount = value.Value;
				SetFlag(PacketFlags.HasGranuleCount, value: true);
			}
			else
			{
				SetFlag(PacketFlags.HasGranuleCount, value: false);
			}
		}
	}

	internal int PageSequenceNumber
	{
		get
		{
			return _pageSequenceNumber;
		}
		set
		{
			_pageSequenceNumber = value;
		}
	}

	internal bool IsShort
	{
		get
		{
			return GetFlag(PacketFlags.IsShort);
		}
		private set
		{
			SetFlag(PacketFlags.IsShort, value);
		}
	}

	/// <summary>
	/// Gets the value of the specified flag.
	/// </summary>
	protected bool GetFlag(PacketFlags flag)
	{
		return (_packetFlags & flag) == flag;
	}

	/// <summary>
	/// Sets the value of the specified flag.
	/// </summary>
	protected void SetFlag(PacketFlags flag, bool value)
	{
		if (value)
		{
			_packetFlags |= flag;
		}
		else
		{
			_packetFlags &= (PacketFlags)(byte)(~(int)flag);
		}
	}

	/// <summary>
	/// Creates a new instance with the specified length.
	/// </summary>
	/// <param name="length">The length of the packet.</param>
	protected DataPacket(int length)
	{
		Length = length;
	}

	/// <summary>
	/// Reads the next byte of the packet.
	/// </summary>
	/// <returns>The next byte if available, otherwise -1.</returns>
	protected abstract int ReadNextByte();

	/// <summary>
	/// Indicates that the packet has been read and its data is no longer needed.
	/// </summary>
	public virtual void Done()
	{
	}

	/// <summary>
	/// Attempts to read the specified number of bits from the packet, but may return fewer.  Does not advance the position counter.
	/// </summary>
	/// <param name="count">The number of bits to attempt to read.</param>
	/// <param name="bitsRead">The number of bits actually read.</param>
	/// <returns>The value of the bits read.</returns>
	/// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="count" /> is not between 0 and 64.</exception>
	public ulong TryPeekBits(int count, out int bitsRead)
	{
		ulong num = 0uL;
		switch (count)
		{
		default:
			throw new ArgumentOutOfRangeException("count");
		case 0:
			bitsRead = 0;
			return 0uL;
		case 1:
		case 2:
		case 3:
		case 4:
		case 5:
		case 6:
		case 7:
		case 8:
		case 9:
		case 10:
		case 11:
		case 12:
		case 13:
		case 14:
		case 15:
		case 16:
		case 17:
		case 18:
		case 19:
		case 20:
		case 21:
		case 22:
		case 23:
		case 24:
		case 25:
		case 26:
		case 27:
		case 28:
		case 29:
		case 30:
		case 31:
		case 32:
		case 33:
		case 34:
		case 35:
		case 36:
		case 37:
		case 38:
		case 39:
		case 40:
		case 41:
		case 42:
		case 43:
		case 44:
		case 45:
		case 46:
		case 47:
		case 48:
		case 49:
		case 50:
		case 51:
		case 52:
		case 53:
		case 54:
		case 55:
		case 56:
		case 57:
		case 58:
		case 59:
		case 60:
		case 61:
		case 62:
		case 63:
		case 64:
			break;
		}
		while (_bitCount < count)
		{
			int num2 = ReadNextByte();
			if (num2 == -1)
			{
				bitsRead = _bitCount;
				num = _bitBucket;
				_bitBucket = 0uL;
				_bitCount = 0;
				IsShort = true;
				return num;
			}
			_bitBucket = (ulong)((long)(num2 & 0xFF) << _bitCount) | _bitBucket;
			_bitCount += 8;
			if (_bitCount > 64)
			{
				_overflowBits = (byte)(num2 >> 72 - _bitCount);
			}
		}
		num = _bitBucket;
		if (count < 64)
		{
			num &= (ulong)((1L << count) - 1);
		}
		bitsRead = count;
		return num;
	}

	/// <summary>
	/// Advances the position counter by the specified number of bits.
	/// </summary>
	/// <param name="count">The number of bits to advance.</param>
	public void SkipBits(int count)
	{
		if (count == 0)
		{
			return;
		}
		if (_bitCount > count)
		{
			if (count > 63)
			{
				_bitBucket = 0uL;
			}
			else
			{
				_bitBucket >>= count;
			}
			if (_bitCount > 64)
			{
				int num = _bitCount - 64;
				_bitBucket |= (ulong)_overflowBits << _bitCount - count - num;
				if (num > count)
				{
					_overflowBits = (byte)(_overflowBits >> count);
				}
			}
			_bitCount -= count;
			_readBits += count;
			return;
		}
		if (_bitCount == count)
		{
			_bitBucket = 0uL;
			_bitCount = 0;
			_readBits += count;
			return;
		}
		count -= _bitCount;
		_readBits += _bitCount;
		_bitCount = 0;
		_bitBucket = 0uL;
		while (count > 8)
		{
			if (ReadNextByte() == -1)
			{
				count = 0;
				IsShort = true;
				break;
			}
			count -= 8;
			_readBits += 8;
		}
		if (count > 0)
		{
			int num2 = ReadNextByte();
			if (num2 == -1)
			{
				IsShort = true;
				return;
			}
			_bitBucket = (ulong)(num2 >> count);
			_bitCount = 8 - count;
			_readBits += count;
		}
	}

	/// <summary>
	/// Resets the bit reader.
	/// </summary>
	protected void ResetBitReader()
	{
		_bitBucket = 0uL;
		_bitCount = 0;
		_readBits = 0;
		IsShort = false;
	}

	/// <summary>
	/// Reads the specified number of bits from the packet and advances the position counter.
	/// </summary>
	/// <param name="count">The number of bits to read.</param>
	/// <returns>The value of the bits read.</returns>
	/// <exception cref="T:System.ArgumentOutOfRangeException">The number of bits specified is not between 0 and 64.</exception>
	public ulong ReadBits(int count)
	{
		if (count == 0)
		{
			return 0uL;
		}
		int bitsRead;
		ulong result = TryPeekBits(count, out bitsRead);
		SkipBits(count);
		return result;
	}

	/// <summary>
	/// Reads the next byte from the packet.  Does not advance the position counter.
	/// </summary>
	/// <returns>The byte read from the packet.</returns>
	public byte PeekByte()
	{
		int bitsRead;
		return (byte)TryPeekBits(8, out bitsRead);
	}

	/// <summary>
	/// Reads the next byte from the packet and advances the position counter.
	/// </summary>
	/// <returns>The byte read from the packet.</returns>
	public byte ReadByte()
	{
		return (byte)ReadBits(8);
	}

	/// <summary>
	/// Reads the specified number of bytes from the packet and advances the position counter.
	/// </summary>
	/// <param name="count">The number of bytes to read.</param>
	/// <returns>A byte array holding the data read.</returns>
	public byte[] ReadBytes(int count)
	{
		List<byte> list = new List<byte>(count);
		while (list.Count < count)
		{
			list.Add(ReadByte());
		}
		return list.ToArray();
	}

	/// <summary>
	/// Reads the specified number of bytes from the packet into the buffer specified and advances the position counter.
	/// </summary>
	/// <param name="buffer">The buffer to read into.</param>
	/// <param name="index">The index into the buffer to start placing the read data.</param>
	/// <param name="count">The number of bytes to read.</param>
	/// <returns>The number of bytes read.</returns>
	/// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="index" /> is less than 0 or <paramref name="index" /> + <paramref name="count" /> is past the end of <paramref name="buffer" />.</exception>
	public int Read(byte[] buffer, int index, int count)
	{
		if (index < 0 || index + count > buffer.Length)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		for (int i = 0; i < count; i++)
		{
			int bitsRead;
			byte b = (byte)TryPeekBits(8, out bitsRead);
			if (bitsRead == 0)
			{
				return i;
			}
			buffer[index++] = b;
			SkipBits(8);
		}
		return count;
	}

	/// <summary>
	/// Reads the next bit from the packet and advances the position counter.
	/// </summary>
	/// <returns>The value of the bit read.</returns>
	public bool ReadBit()
	{
		return ReadBits(1) == 1;
	}

	/// <summary>
	/// Retrieves the next 16 bits from the packet as a <see cref="T:System.Int16" /> and advances the position counter.
	/// </summary>
	/// <returns>The value of the next 16 bits.</returns>
	public short ReadInt16()
	{
		return (short)ReadBits(16);
	}

	/// <summary>
	/// Retrieves the next 32 bits from the packet as a <see cref="T:System.Int32" /> and advances the position counter.
	/// </summary>
	/// <returns>The value of the next 32 bits.</returns>
	public int ReadInt32()
	{
		return (int)ReadBits(32);
	}

	/// <summary>
	/// Retrieves the next 64 bits from the packet as a <see cref="T:System.Int64" /> and advances the position counter.
	/// </summary>
	/// <returns>The value of the next 64 bits.</returns>
	public long ReadInt64()
	{
		return (long)ReadBits(64);
	}

	/// <summary>
	/// Retrieves the next 16 bits from the packet as a <see cref="T:System.UInt16" /> and advances the position counter.
	/// </summary>
	/// <returns>The value of the next 16 bits.</returns>
	public ushort ReadUInt16()
	{
		return (ushort)ReadBits(16);
	}

	/// <summary>
	/// Retrieves the next 32 bits from the packet as a <see cref="T:System.UInt32" /> and advances the position counter.
	/// </summary>
	/// <returns>The value of the next 32 bits.</returns>
	public uint ReadUInt32()
	{
		return (uint)ReadBits(32);
	}

	/// <summary>
	/// Retrieves the next 64 bits from the packet as a <see cref="T:System.UInt64" /> and advances the position counter.
	/// </summary>
	/// <returns>The value of the next 64 bits.</returns>
	public ulong ReadUInt64()
	{
		return ReadBits(64);
	}

	/// <summary>
	/// Advances the position counter by the specified number of bytes.
	/// </summary>
	/// <param name="count">The number of bytes to advance.</param>
	public void SkipBytes(int count)
	{
		SkipBits(count * 8);
	}
}
