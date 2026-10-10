using System;

namespace NVorbis.Ogg;

internal class Packet : DataPacket
{
	private long _offset;

	private int _length;

	private int _curOfs;

	private Packet _mergedPacket;

	private Packet _next;

	private Packet _prev;

	private ContainerReader _containerReader;

	internal Packet Next
	{
		get
		{
			return _next;
		}
		set
		{
			_next = value;
		}
	}

	internal Packet Prev
	{
		get
		{
			return _prev;
		}
		set
		{
			_prev = value;
		}
	}

	internal bool IsContinued
	{
		get
		{
			return GetFlag(PacketFlags.User1);
		}
		set
		{
			SetFlag(PacketFlags.User1, value);
		}
	}

	internal bool IsContinuation
	{
		get
		{
			return GetFlag(PacketFlags.User2);
		}
		set
		{
			SetFlag(PacketFlags.User2, value);
		}
	}

	internal Packet(ContainerReader containerReader, long streamOffset, int length)
		: base(length)
	{
		_containerReader = containerReader;
		_offset = streamOffset;
		_length = length;
		_curOfs = 0;
	}

	internal void MergeWith(DataPacket continuation)
	{
		if (!(continuation is Packet mergedPacket))
		{
			throw new ArgumentException("Incorrect packet type!");
		}
		base.Length += continuation.Length;
		if (_mergedPacket == null)
		{
			_mergedPacket = mergedPacket;
		}
		else
		{
			_mergedPacket.MergeWith(continuation);
		}
		base.PageGranulePosition = continuation.PageGranulePosition;
		base.PageSequenceNumber = continuation.PageSequenceNumber;
	}

	internal void Reset()
	{
		_curOfs = 0;
		ResetBitReader();
		if (_mergedPacket != null)
		{
			_mergedPacket.Reset();
		}
	}

	protected override int ReadNextByte()
	{
		if (_curOfs == _length)
		{
			if (_mergedPacket == null)
			{
				return -1;
			}
			return _mergedPacket.ReadNextByte();
		}
		int num = _containerReader.PacketReadByte(_offset + _curOfs);
		if (num != -1)
		{
			_curOfs++;
		}
		return num;
	}

	public override void Done()
	{
		if (_mergedPacket != null)
		{
			_mergedPacket.Done();
		}
		else
		{
			_containerReader.PacketDiscardThrough(_offset + _length);
		}
	}
}
