using System;
using System.Collections.Generic;
using System.IO;

namespace NVorbis;

internal class StreamReadBuffer : IDisposable
{
	private class StreamWrapper
	{
		internal Stream Source;

		internal object LockObject = new object();

		internal long EofOffset = long.MaxValue;

		internal int RefCount = 1;
	}

	private class SavedBuffer
	{
		public byte[] Buffer;

		public long BaseOffset;

		public int End;

		public int DiscardCount;

		public long VersionSaved;
	}

	private static Dictionary<Stream, StreamWrapper> _lockObjects = new Dictionary<Stream, StreamWrapper>();

	private StreamWrapper _wrapper;

	private int _maxSize;

	private byte[] _data;

	private long _baseOffset;

	private int _end;

	private int _discardCount;

	private bool _minimalRead;

	private long _versionCounter;

	private List<SavedBuffer> _savedBuffers;

	/// <summary>
	/// Gets or Sets whether to limit reads to the smallest size possible.
	/// </summary>
	public bool MinimalRead
	{
		get
		{
			return _minimalRead;
		}
		set
		{
			_minimalRead = value;
		}
	}

	/// <summary>
	/// Gets or Sets the maximum size of the buffer.  This is not a hard limit.
	/// </summary>
	public int MaxSize
	{
		get
		{
			return _maxSize;
		}
		set
		{
			if (value < 1)
			{
				throw new ArgumentOutOfRangeException("Must be greater than zero.");
			}
			int num = 1 << (int)Math.Ceiling(Math.Log(value, 2.0));
			if (num < _end)
			{
				if (num < _end - _discardCount)
				{
					throw new ArgumentOutOfRangeException("Must be greater than or equal to the number of bytes currently buffered.");
				}
				CommitDiscard();
				byte[] array = new byte[num];
				Buffer.BlockCopy(_data, 0, array, 0, _end);
				_data = array;
			}
			_maxSize = num;
		}
	}

	/// <summary>
	/// Gets the offset of the start of the buffered data.  Reads to offsets before this are likely to require a seek.
	/// </summary>
	public long BaseOffset => _baseOffset + _discardCount;

	/// <summary>
	/// Gets the number of bytes currently buffered.
	/// </summary>
	public int BytesFilled => _end - _discardCount;

	/// <summary>
	/// Gets the number of bytes the buffer can hold.
	/// </summary>
	public int Length => _data.Length;

	internal long BufferEndOffset
	{
		get
		{
			if (_end - _discardCount > 0)
			{
				return _baseOffset + _discardCount + _maxSize;
			}
			return _wrapper.Source.Length;
		}
	}

	internal StreamReadBuffer(Stream source, int initialSize, int maxSize, bool minimalRead)
	{
		StreamWrapper value;
		lock (_lockObjects)
		{
			if (!_lockObjects.TryGetValue(source, out value))
			{
				_lockObjects.Add(source, new StreamWrapper
				{
					Source = source
				});
				value = _lockObjects[source];
				if (source.CanSeek)
				{
					value.EofOffset = source.Length;
				}
			}
			else
			{
				value.RefCount++;
			}
		}
		initialSize = 2 << (int)Math.Log(initialSize - 1, 2.0);
		maxSize = 1 << (int)Math.Log(maxSize, 2.0);
		_wrapper = value;
		_data = new byte[initialSize];
		_maxSize = maxSize;
		_minimalRead = minimalRead;
		_savedBuffers = new List<SavedBuffer>();
	}

	public void Dispose()
	{
		lock (_lockObjects)
		{
			if (--_wrapper.RefCount == 0)
			{
				_lockObjects.Remove(_wrapper.Source);
			}
		}
	}

	/// <summary>
	/// Reads the number of bytes specified into the buffer given, starting with the offset indicated.
	/// </summary>
	/// <param name="offset">The offset into the stream to start reading.</param>
	/// <param name="buffer">The buffer to read to.</param>
	/// <param name="index">The index into the buffer to start writing to.</param>
	/// <param name="count">The number of bytes to read.</param>
	/// <returns>The number of bytes read.</returns>
	public int Read(long offset, byte[] buffer, int index, int count)
	{
		if (offset < 0)
		{
			throw new ArgumentOutOfRangeException("offset");
		}
		if (buffer == null)
		{
			throw new ArgumentNullException("buffer");
		}
		if (index < 0 || index + count > buffer.Length)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		if (count < 0)
		{
			throw new ArgumentOutOfRangeException("count");
		}
		if (offset >= _wrapper.EofOffset)
		{
			return 0;
		}
		int srcOffset = EnsureAvailable(offset, ref count, isRecursion: false);
		Buffer.BlockCopy(_data, srcOffset, buffer, index, count);
		return count;
	}

	internal int ReadByte(long offset)
	{
		if (offset < 0)
		{
			throw new ArgumentOutOfRangeException("offset");
		}
		if (offset >= _wrapper.EofOffset)
		{
			return -1;
		}
		int count = 1;
		int num = EnsureAvailable(offset, ref count, isRecursion: false);
		if (count == 1)
		{
			return _data[num];
		}
		return -1;
	}

	private int EnsureAvailable(long offset, ref int count, bool isRecursion)
	{
		if (offset >= _baseOffset && offset + count < _baseOffset + _end)
		{
			return (int)(offset - _baseOffset);
		}
		if (count > _maxSize)
		{
			throw new InvalidOperationException("Not enough room in the buffer!  Increase the maximum size and try again.");
		}
		_versionCounter++;
		if (!isRecursion)
		{
			for (int i = 0; i < _savedBuffers.Count; i++)
			{
				long num = _savedBuffers[i].BaseOffset - offset;
				if ((num < 0 && _savedBuffers[i].End + num > 0) || (num > 0 && count - num > 0))
				{
					SwapBuffers(_savedBuffers[i]);
					return EnsureAvailable(offset, ref count, isRecursion: true);
				}
			}
		}
		while (_savedBuffers.Count > 0 && _savedBuffers[0].VersionSaved + 25 < _versionCounter)
		{
			_savedBuffers[0].Buffer = null;
			_savedBuffers.RemoveAt(0);
		}
		if (offset < _baseOffset && !_wrapper.Source.CanSeek)
		{
			throw new InvalidOperationException("Cannot seek before buffer on forward-only streams!");
		}
		CalcBuffer(offset, count, out var readStart, out var readEnd);
		count = FillBuffer(offset, count, readStart, readEnd);
		return (int)(offset - _baseOffset);
	}

	private void SaveBuffer()
	{
		_savedBuffers.Add(new SavedBuffer
		{
			Buffer = _data,
			BaseOffset = _baseOffset,
			End = _end,
			DiscardCount = _discardCount,
			VersionSaved = _versionCounter
		});
		_data = null;
		_end = 0;
		_discardCount = 0;
	}

	private void CreateNewBuffer(long offset, int count)
	{
		SaveBuffer();
		_data = new byte[Math.Min(2 << (int)Math.Log(count - 1, 2.0), _maxSize)];
		_baseOffset = offset;
	}

	private void SwapBuffers(SavedBuffer savedBuffer)
	{
		_savedBuffers.Remove(savedBuffer);
		SaveBuffer();
		_data = savedBuffer.Buffer;
		_baseOffset = savedBuffer.BaseOffset;
		_end = savedBuffer.End;
		_discardCount = savedBuffer.DiscardCount;
	}

	private void CalcBuffer(long offset, int count, out int readStart, out int readEnd)
	{
		readStart = 0;
		readEnd = 0;
		if (offset < _baseOffset)
		{
			if (offset + _maxSize <= _baseOffset)
			{
				if (_baseOffset - (offset + _maxSize) > _maxSize)
				{
					CreateNewBuffer(offset, count);
				}
				else
				{
					EnsureBufferSize(count, copyContents: false, 0);
				}
				_baseOffset = offset;
				readEnd = count;
			}
			else
			{
				readEnd = (int)(offset - _baseOffset);
				EnsureBufferSize(Math.Min((int)(offset + _maxSize - _baseOffset), _end) - readEnd, copyContents: true, readEnd);
				readEnd = (int)(offset - _baseOffset) - readEnd;
			}
		}
		else if (offset >= _baseOffset + _maxSize)
		{
			if (offset - (_baseOffset + _maxSize) > _maxSize)
			{
				CreateNewBuffer(offset, count);
			}
			else
			{
				EnsureBufferSize(count, copyContents: false, 0);
			}
			_baseOffset = offset;
			readEnd = count;
		}
		else
		{
			readEnd = (int)(offset + count - _baseOffset);
			int num = Math.Max(readEnd - _maxSize, 0);
			EnsureBufferSize(readEnd - num, copyContents: true, num);
			readStart = _end;
			readEnd = (int)(offset + count - _baseOffset);
		}
	}

	private void EnsureBufferSize(int reqSize, bool copyContents, int copyOffset)
	{
		byte[] array = _data;
		if (reqSize > _data.Length)
		{
			if (reqSize > _maxSize)
			{
				if (!_wrapper.Source.CanSeek && reqSize - _discardCount > _maxSize)
				{
					throw new InvalidOperationException("Not enough room in the buffer!  Increase the maximum size and try again.");
				}
				int num = reqSize - _maxSize;
				copyOffset += num;
				reqSize = _maxSize;
			}
			else
			{
				int num2;
				for (num2 = _data.Length; num2 < reqSize; num2 *= 2)
				{
				}
				reqSize = num2;
			}
			if (reqSize > _data.Length)
			{
				array = new byte[reqSize];
			}
		}
		if (copyContents)
		{
			if ((copyOffset > 0 && copyOffset < _end) || (copyOffset == 0 && array != _data))
			{
				Buffer.BlockCopy(_data, copyOffset, array, 0, _end - copyOffset);
				if ((_discardCount -= copyOffset) < 0)
				{
					_discardCount = 0;
				}
			}
			else if (copyOffset < 0 && -copyOffset < _end)
			{
				if (array != _data || _end <= -copyOffset)
				{
					Buffer.BlockCopy(_data, 0, array, -copyOffset, Math.Max(_end, Math.Min(_end, _data.Length + copyOffset)));
				}
				else
				{
					_end = copyOffset;
				}
				_discardCount = 0;
			}
			else
			{
				_end = copyOffset;
				_discardCount = 0;
			}
			_baseOffset += copyOffset;
			_end -= copyOffset;
			if (_end > array.Length)
			{
				_end = array.Length;
			}
		}
		else
		{
			_discardCount = 0;
			_end = 0;
		}
		_data = array;
	}

	private int FillBuffer(long offset, int count, int readStart, int readEnd)
	{
		long readOffset = _baseOffset + readStart;
		int readCount = readEnd - readStart;
		lock (_wrapper.LockObject)
		{
			readCount = PrepareStreamForRead(readCount, readOffset);
			ReadStream(readStart, readCount, readOffset);
			if (_end < readStart + readCount)
			{
				count = Math.Max(0, (int)(_baseOffset + _end - offset));
			}
			else if (!_minimalRead && _end < _data.Length)
			{
				readCount = _data.Length - _end;
				readCount = PrepareStreamForRead(readCount, _baseOffset + _end);
				_end += _wrapper.Source.Read(_data, _end, readCount);
			}
		}
		return count;
	}

	private int PrepareStreamForRead(int readCount, long readOffset)
	{
		if (readCount > 0 && _wrapper.Source.Position != readOffset)
		{
			if (readOffset < _wrapper.EofOffset)
			{
				if (_wrapper.Source.CanSeek)
				{
					_wrapper.Source.Position = readOffset;
				}
				else
				{
					long num = readOffset - _wrapper.Source.Position;
					if (num < 0)
					{
						readCount = 0;
					}
					else
					{
						while (--num >= 0)
						{
							if (_wrapper.Source.ReadByte() == -1)
							{
								_wrapper.EofOffset = _wrapper.Source.Position;
								readCount = 0;
								break;
							}
						}
					}
				}
			}
			else
			{
				readCount = 0;
			}
		}
		return readCount;
	}

	private void ReadStream(int readStart, int readCount, long readOffset)
	{
		while (readCount > 0 && readOffset < _wrapper.EofOffset)
		{
			int num = _wrapper.Source.Read(_data, readStart, readCount);
			if (num == 0)
			{
				break;
			}
			readStart += num;
			readOffset += num;
			readCount -= num;
		}
		if (readStart > _end)
		{
			_end = readStart;
		}
	}

	/// <summary>
	/// Tells the buffer that it no longer needs to maintain any bytes before the indicated offset.
	/// </summary>
	/// <param name="offset">The offset to discard through.</param>
	public void DiscardThrough(long offset)
	{
		int val = (int)(offset - _baseOffset);
		_discardCount = Math.Max(val, _discardCount);
		if (_discardCount >= _data.Length)
		{
			CommitDiscard();
		}
	}

	private void CommitDiscard()
	{
		if (_discardCount >= _data.Length || _discardCount >= _end)
		{
			_baseOffset += _discardCount;
			_end = 0;
		}
		else
		{
			Buffer.BlockCopy(_data, _discardCount, _data, 0, _end - _discardCount);
			_baseOffset += _discardCount;
			_end -= _discardCount;
		}
		_discardCount = 0;
	}
}
