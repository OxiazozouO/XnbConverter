using System;

namespace NVorbis;

internal class RingBuffer
{
	private float[] _buffer;

	private int _start;

	private int _end;

	private int _bufLen;

	internal int Channels;

	internal int Length
	{
		get
		{
			int num = _end - _start;
			if (num < 0)
			{
				num += _bufLen;
			}
			return num;
		}
	}

	internal RingBuffer(int size)
	{
		_buffer = new float[size];
		_start = (_end = 0);
		_bufLen = size;
	}

	internal void EnsureSize(int size)
	{
		size += Channels;
		if (_bufLen < size)
		{
			float[] array = new float[size];
			Array.Copy(_buffer, _start, array, 0, _bufLen - _start);
			if (_end < _start)
			{
				Array.Copy(_buffer, 0, array, _bufLen - _start, _end);
			}
			int length = Length;
			_start = 0;
			_end = length;
			_buffer = array;
			_bufLen = size;
		}
	}

	internal void CopyTo(float[] buffer, int index, int count)
	{
		if (index < 0 || index + count > buffer.Length)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		int start = _start;
		RemoveItems(count);
		int num = (_end - start + _bufLen) % _bufLen;
		if (count > num)
		{
			throw new ArgumentOutOfRangeException("count");
		}
		int num2 = Math.Min(count, _bufLen - start);
		Buffer.BlockCopy(_buffer, start * 4, buffer, index * 4, num2 * 4);
		if (num2 < count)
		{
			Buffer.BlockCopy(_buffer, 0, buffer, (index + num2) * 4, (count - num2) * 4);
		}
	}

	internal void RemoveItems(int count)
	{
		int num = (count + _start) % _bufLen;
		if (_end > _start)
		{
			if (num > _end || num < _start)
			{
				throw new ArgumentOutOfRangeException();
			}
		}
		else if (num < _start && num > _end)
		{
			throw new ArgumentOutOfRangeException();
		}
		_start = num;
	}

	internal void Clear()
	{
		_start = (_end = 0);
	}

	internal void Write(int channel, int index, int start, int switchPoint, int end, float[] pcm, float[] window)
	{
		int num;
		for (num = (index + start) * Channels + channel + _start; num >= _bufLen; num -= _bufLen)
		{
		}
		if (num < 0)
		{
			start -= index;
			num = channel;
		}
		while (num < _bufLen && start < switchPoint)
		{
			_buffer[num] += pcm[start] * window[start];
			num += Channels;
			start++;
		}
		if (num >= _bufLen)
		{
			num -= _bufLen;
			while (start < switchPoint)
			{
				_buffer[num] += pcm[start] * window[start];
				num += Channels;
				start++;
			}
		}
		while (num < _bufLen && start < end)
		{
			_buffer[num] = pcm[start] * window[start];
			num += Channels;
			start++;
		}
		if (num >= _bufLen)
		{
			num -= _bufLen;
			while (start < end)
			{
				_buffer[num] = pcm[start] * window[start];
				num += Channels;
				start++;
			}
		}
		_end = num;
	}
}
