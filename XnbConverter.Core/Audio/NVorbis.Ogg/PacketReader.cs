using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace NVorbis.Ogg;

[DebuggerTypeProxy(typeof(DebugView))]
internal class PacketReader : IPacketProvider, IDisposable
{
	private class DebugView
	{
		private PacketReader _reader;

		private Packet _last;

		private Packet _first;

		private Packet[] _packetList = new Packet[0];

		public ContainerReader Container => _reader._container;

		public int StreamSerial => _reader._streamSerial;

		public bool EndOfStreamFound => _reader._eosFound;

		public int CurrentPacketIndex
		{
			get
			{
				if (_reader._current == null)
				{
					return -1;
				}
				return Array.IndexOf(Packets, _reader._current);
			}
		}

		public Packet[] Packets
		{
			get
			{
				if (_reader._last == _last && _reader._first == _first)
				{
					return _packetList;
				}
				_last = _reader._last;
				_first = _reader._first;
				List<Packet> list = new List<Packet>();
				for (Packet packet = _first; packet != null; packet = packet.Next)
				{
					list.Add(packet);
				}
				_packetList = list.ToArray();
				return _packetList;
			}
		}

		public DebugView(PacketReader reader)
		{
			if (reader == null)
			{
				throw new ArgumentNullException("reader");
			}
			_reader = reader;
		}
	}

	private ContainerReader _container;

	private int _streamSerial;

	private bool _eosFound;

	private Packet _first;

	private Packet _current;

	private Packet _last;

	private object _packetLock = new object();

	internal bool HasEndOfStream => _eosFound;

	public int StreamSerial => _streamSerial;

	public long ContainerBits { get; set; }

	public bool CanSeek => _container.CanSeek;

	public event EventHandler<ParameterChangeEventArgs> ParameterChange;

	internal PacketReader(ContainerReader container, int streamSerial)
	{
		_container = container;
		_streamSerial = streamSerial;
	}

	public void Dispose()
	{
		_eosFound = true;
		if (_container != null)
		{
			_container.DisposePacketReader(this);
		}
		_container = null;
		_current = null;
		if (_first != null)
		{
			Packet packet = _first;
			_first = null;
			while (packet.Next != null)
			{
				Packet next = packet.Next;
				packet.Next = null;
				packet = next;
				packet.Prev = null;
			}
			packet = null;
		}
		_last = null;
	}

	internal void AddPacket(Packet packet)
	{
		lock (_packetLock)
		{
			if (_eosFound)
			{
				return;
			}
			if (packet.IsResync)
			{
				packet.IsContinuation = false;
				if (_last != null)
				{
					_last.IsContinued = false;
				}
			}
			if (packet.IsContinuation)
			{
				if (_last == null)
				{
					throw new InvalidDataException();
				}
				if (!_last.IsContinued)
				{
					throw new InvalidDataException();
				}
				_last.MergeWith(packet);
				_last.IsContinued = packet.IsContinued;
			}
			else
			{
				if (packet == null)
				{
					throw new ArgumentException("Wrong packet datatype", "packet");
				}
				if (_first == null)
				{
					_first = packet;
					_last = packet;
				}
				else
				{
					Packet packet2 = (packet.Prev = _last);
					Packet last2 = (packet2.Next = packet);
					_last = last2;
				}
			}
			if (packet.IsEndOfStream)
			{
				SetEndOfStream();
			}
		}
	}

	internal void SetEndOfStream()
	{
		lock (_packetLock)
		{
			_eosFound = true;
			if (_last.IsContinued)
			{
				_last = _last.Prev;
				_last.Next.Prev = null;
				_last.Next = null;
			}
		}
	}

	public DataPacket GetNextPacket()
	{
		return _current = PeekNextPacketInternal();
	}

	public DataPacket PeekNextPacket()
	{
		return PeekNextPacketInternal();
	}

	private Packet PeekNextPacketInternal()
	{
		Packet packet;
		if (_current == null)
		{
			packet = _first;
		}
		else
		{
			while (true)
			{
				lock (_packetLock)
				{
					packet = _current.Next;
					if ((packet != null && !packet.IsContinued) || _eosFound)
					{
						break;
					}
					goto IL_004f;
				}
				IL_004f:
				_container.GatherNextPage(_streamSerial);
			}
		}
		if (packet != null)
		{
			if (packet.IsContinued)
			{
				throw new InvalidDataException("Packet is incomplete!");
			}
			packet.Reset();
		}
		return packet;
	}

	internal void ReadAllPages()
	{
		if (!CanSeek)
		{
			throw new InvalidOperationException();
		}
		while (!_eosFound)
		{
			_container.GatherNextPage(_streamSerial);
		}
	}

	internal DataPacket GetLastPacket()
	{
		ReadAllPages();
		return _last;
	}

	public int GetTotalPageCount()
	{
		ReadAllPages();
		int num = 0;
		int num2 = 0;
		for (Packet packet = _first; packet != null; packet = packet.Next)
		{
			if (packet.PageSequenceNumber != num2)
			{
				num++;
				num2 = packet.PageSequenceNumber;
			}
		}
		return num;
	}

	public DataPacket GetPacket(int packetIndex)
	{
		if (!CanSeek)
		{
			throw new InvalidOperationException();
		}
		if (packetIndex < 0)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		if (_first == null)
		{
			throw new InvalidOperationException("Packet reader has no packets!");
		}
		Packet packet = _first;
		while (--packetIndex >= 0)
		{
			while (packet.Next == null)
			{
				if (_eosFound)
				{
					throw new ArgumentOutOfRangeException("index");
				}
				_container.GatherNextPage(_streamSerial);
			}
			packet = packet.Next;
		}
		packet.Reset();
		return packet;
	}

	private Packet GetLastPacketInPage(Packet packet)
	{
		if (packet != null)
		{
			int pageSequenceNumber = packet.PageSequenceNumber;
			while (packet.Next != null && packet.Next.PageSequenceNumber == pageSequenceNumber)
			{
				packet = packet.Next;
			}
			if (packet != null && packet.IsContinued)
			{
				packet = packet.Prev;
			}
		}
		return packet;
	}

	private Packet FindPacketInPage(Packet pagePacket, long targetGranulePos, Func<DataPacket, DataPacket, int> packetGranuleCountCallback)
	{
		Packet lastPacketInPage = GetLastPacketInPage(pagePacket);
		if (lastPacketInPage == null)
		{
			return null;
		}
		Packet packet = lastPacketInPage;
		do
		{
			if (!packet.GranuleCount.HasValue)
			{
				if (packet == lastPacketInPage)
				{
					packet.GranulePosition = packet.PageGranulePosition;
				}
				else
				{
					packet.GranulePosition = packet.Next.GranulePosition - packet.Next.GranuleCount.Value;
				}
				if (packet == _last && _eosFound && packet.Prev.PageSequenceNumber < packet.PageSequenceNumber)
				{
					packet.GranuleCount = (int)(packet.GranulePosition - packet.Prev.PageGranulePosition);
				}
				else if (packet.Prev != null)
				{
					packet.Prev.Reset();
					packet.Reset();
					packet.GranuleCount = packetGranuleCountCallback(packet, packet.Prev);
				}
				else
				{
					if (packet.GranulePosition > packet.Next.GranulePosition - packet.Next.GranuleCount)
					{
						throw new InvalidOperationException("First data packet size mismatch");
					}
					packet.GranuleCount = (int)packet.GranulePosition;
				}
			}
			if (targetGranulePos <= packet.GranulePosition && targetGranulePos > packet.GranulePosition - packet.GranuleCount)
			{
				if (packet.Prev != null && !packet.Prev.GranuleCount.HasValue)
				{
					packet.Prev.GranulePosition = packet.GranulePosition - packet.GranuleCount.Value;
				}
				return packet;
			}
			packet = packet.Prev;
		}
		while (packet != null && packet.PageSequenceNumber == lastPacketInPage.PageSequenceNumber);
		if (packet != null && packet.PageGranulePosition < targetGranulePos)
		{
			packet.GranulePosition = packet.PageGranulePosition;
			return packet.Next;
		}
		return null;
	}

	public DataPacket FindPacket(long granulePos, Func<DataPacket, DataPacket, int> packetGranuleCountCallback)
	{
		if (granulePos < 0)
		{
			throw new ArgumentOutOfRangeException("granulePos");
		}
		Packet packet = null;
		Packet packet2 = _current ?? _first;
		if (granulePos > packet2.PageGranulePosition)
		{
			while (granulePos > packet2.PageGranulePosition)
			{
				if ((packet2.Next == null || packet2.IsContinued) && !_eosFound)
				{
					_container.GatherNextPage(_streamSerial);
					if (_eosFound)
					{
						packet2 = null;
						break;
					}
				}
				packet2 = packet2.Next;
			}
			return FindPacketInPage(packet2, granulePos, packetGranuleCountCallback);
		}
		while (packet2.Prev != null && (granulePos <= packet2.Prev.PageGranulePosition || packet2.Prev.PageGranulePosition == -1))
		{
			packet2 = packet2.Prev;
		}
		return FindPacketInPage(packet2, granulePos, packetGranuleCountCallback);
	}

	public void SeekToPacket(DataPacket packet, int preRoll)
	{
		if (preRoll < 0)
		{
			throw new ArgumentOutOfRangeException("preRoll");
		}
		if (packet == null)
		{
			throw new ArgumentNullException("granulePos");
		}
		Packet packet2 = packet as Packet;
		if (packet2 == null)
		{
			throw new ArgumentException("Incorrect packet type!", "packet");
		}
		while (--preRoll >= 0)
		{
			packet2 = packet2.Prev;
			if (packet2 == null)
			{
				throw new ArgumentOutOfRangeException("preRoll");
			}
		}
		_current = packet2.Prev;
	}

	public long GetGranuleCount()
	{
		return GetLastPacket().PageGranulePosition;
	}
}
