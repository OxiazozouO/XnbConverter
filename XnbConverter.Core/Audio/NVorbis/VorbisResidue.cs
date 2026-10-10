using System;
using System.IO;
using System.Linq;

namespace NVorbis;

internal abstract class VorbisResidue
{
	private class Residue0 : VorbisResidue
	{
		private int _begin;

		private int _end;

		private int _partitionSize;

		private int _classifications;

		private int _maxStages;

		private VorbisCodebook[][] _books;

		private VorbisCodebook _classBook;

		private int[] _cascade;

		private int[] _entryCache;

		private int[][] _decodeMap;

		private int[][][] _partWordCache;

		internal Residue0(VorbisStreamDecoder vorbis)
			: base(vorbis)
		{
		}

		protected override void Init(DataPacket packet)
		{
			_begin = (int)packet.ReadBits(24);
			_end = (int)packet.ReadBits(24);
			_partitionSize = (int)packet.ReadBits(24) + 1;
			_classifications = (int)packet.ReadBits(6) + 1;
			_classBook = _vorbis.Books[(uint)packet.ReadBits(8)];
			_cascade = new int[_classifications];
			int num = 0;
			for (int i = 0; i < _classifications; i++)
			{
				int num2 = (int)packet.ReadBits(3);
				if (packet.ReadBit())
				{
					_cascade[i] = ((int)packet.ReadBits(5) << 3) | num2;
				}
				else
				{
					_cascade[i] = num2;
				}
				num += icount(_cascade[i]);
			}
			int[] array = new int[num];
			for (int j = 0; j < num; j++)
			{
				array[j] = (int)packet.ReadBits(8);
				if (_vorbis.Books[array[j]].MapType == 0)
				{
					throw new InvalidDataException();
				}
			}
			int entries = _classBook.Entries;
			int num3 = _classBook.Dimensions;
			int num4 = 1;
			while (num3 > 0)
			{
				num4 *= _classifications;
				if (num4 > entries)
				{
					throw new InvalidDataException();
				}
				num3--;
			}
			num3 = _classBook.Dimensions;
			_books = new VorbisCodebook[_classifications][];
			num = 0;
			int num5 = 0;
			for (int k = 0; k < _classifications; k++)
			{
				int num6 = Utils.ilog(_cascade[k]);
				_books[k] = new VorbisCodebook[num6];
				if (num6 <= 0)
				{
					continue;
				}
				num5 = Math.Max(num5, num6);
				for (int l = 0; l < num6; l++)
				{
					if ((_cascade[k] & (1 << l)) > 0)
					{
						_books[k][l] = _vorbis.Books[array[num++]];
					}
				}
			}
			_maxStages = num5;
			_decodeMap = new int[num4][];
			for (int m = 0; m < num4; m++)
			{
				int num7 = m;
				int num8 = num4 / _classifications;
				_decodeMap[m] = new int[_classBook.Dimensions];
				for (int n = 0; n < _classBook.Dimensions; n++)
				{
					int num9 = num7 / num8;
					num7 -= num9 * num8;
					num8 /= _classifications;
					_decodeMap[m][n] = num9;
				}
			}
			_entryCache = new int[_partitionSize];
			_partWordCache = new int[_vorbis._channels][][];
			int num10 = ((_end - _begin) / _partitionSize + _classBook.Dimensions - 1) / _classBook.Dimensions;
			for (int num11 = 0; num11 < _vorbis._channels; num11++)
			{
				_partWordCache[num11] = new int[num10][];
			}
		}

		internal override float[][] Decode(DataPacket packet, bool[] doNotDecode, int channels, int blockSize)
		{
			float[][] residueBuffer = GetResidueBuffer(doNotDecode.Length);
			int num = ((_end < blockSize / 2) ? _end : (blockSize / 2)) - _begin;
			if (num > 0 && Enumerable.Contains(doNotDecode, value: false))
			{
				int num2 = num / _partitionSize;
				int length = (num2 + _classBook.Dimensions - 1) / _classBook.Dimensions;
				for (int i = 0; i < channels; i++)
				{
					Array.Clear(_partWordCache[i], 0, length);
				}
				for (int j = 0; j < _maxStages; j++)
				{
					int k = 0;
					int num3 = 0;
					while (k < num2)
					{
						if (j == 0)
						{
							for (int l = 0; l < channels; l++)
							{
								int num4 = _classBook.DecodeScalar(packet);
								if (num4 >= 0 && num4 < _decodeMap.Length)
								{
									_partWordCache[l][num3] = _decodeMap[num4];
									continue;
								}
								k = num2;
								j = _maxStages;
								break;
							}
						}
						int num5 = 0;
						for (; k < num2; k++)
						{
							if (num5 >= _classBook.Dimensions)
							{
								break;
							}
							int offset = _begin + k * _partitionSize;
							for (int m = 0; m < channels; m++)
							{
								int num6 = _partWordCache[m][num3][num5];
								if ((_cascade[num6] & (1 << j)) != 0)
								{
									VorbisCodebook vorbisCodebook = _books[num6][j];
									if (vorbisCodebook != null && WriteVectors(vorbisCodebook, packet, residueBuffer, m, offset, _partitionSize))
									{
										k = num2;
										j = _maxStages;
										break;
									}
								}
							}
							num5++;
						}
						num3++;
					}
				}
			}
			return residueBuffer;
		}

		protected virtual bool WriteVectors(VorbisCodebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
		{
			float[] array = residue[channel];
			int num = partitionSize / codebook.Dimensions;
			for (int i = 0; i < num; i++)
			{
				if ((_entryCache[i] = codebook.DecodeScalar(packet)) == -1)
				{
					return true;
				}
			}
			for (int j = 0; j < codebook.Dimensions; j++)
			{
				int num2 = 0;
				while (num2 < num)
				{
					array[offset] += codebook[_entryCache[num2], j];
					num2++;
					offset++;
				}
			}
			return false;
		}
	}

	private class Residue1 : Residue0
	{
		internal Residue1(VorbisStreamDecoder vorbis)
			: base(vorbis)
		{
		}

		protected override bool WriteVectors(VorbisCodebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
		{
			float[] array = residue[channel];
			int num = 0;
			while (num < partitionSize)
			{
				int num2 = codebook.DecodeScalar(packet);
				if (num2 == -1)
				{
					return true;
				}
				for (int i = 0; i < codebook.Dimensions; i++)
				{
					array[offset + num] += codebook[num2, i];
					num++;
				}
			}
			return false;
		}
	}

	private class Residue2 : Residue0
	{
		private int _channels;

		internal Residue2(VorbisStreamDecoder vorbis)
			: base(vorbis)
		{
		}

		internal override float[][] Decode(DataPacket packet, bool[] doNotDecode, int channels, int blockSize)
		{
			_channels = channels;
			return base.Decode(packet, doNotDecode, 1, blockSize * channels);
		}

		protected override bool WriteVectors(VorbisCodebook codebook, DataPacket packet, float[][] residue, int channel, int offset, int partitionSize)
		{
			int num = 0;
			offset /= _channels;
			int num2 = 0;
			while (num2 < partitionSize)
			{
				int num3 = codebook.DecodeScalar(packet);
				if (num3 == -1)
				{
					return true;
				}
				int num4 = 0;
				while (num4 < codebook.Dimensions)
				{
					residue[num][offset] += codebook[num3, num4];
					if (++num == _channels)
					{
						num = 0;
						offset++;
					}
					num4++;
					num2++;
				}
			}
			return false;
		}
	}

	private VorbisStreamDecoder _vorbis;

	private float[][] _residue;

	internal static VorbisResidue Init(VorbisStreamDecoder vorbis, DataPacket packet)
	{
		int num = (int)packet.ReadBits(16);
		VorbisResidue vorbisResidue = null;
		switch (num)
		{
		case 0:
			vorbisResidue = new Residue0(vorbis);
			break;
		case 1:
			vorbisResidue = new Residue1(vorbis);
			break;
		case 2:
			vorbisResidue = new Residue2(vorbis);
			break;
		}
		if (vorbisResidue == null)
		{
			throw new InvalidDataException();
		}
		vorbisResidue.Init(packet);
		return vorbisResidue;
	}

	private static int icount(int v)
	{
		int num = 0;
		while (v != 0)
		{
			num += v & 1;
			v >>= 1;
		}
		return num;
	}

	protected VorbisResidue(VorbisStreamDecoder vorbis)
	{
		_vorbis = vorbis;
		_residue = new float[_vorbis._channels][];
		for (int i = 0; i < _vorbis._channels; i++)
		{
			_residue[i] = new float[_vorbis.Block1Size];
		}
	}

	protected float[][] GetResidueBuffer(int channels)
	{
		float[][] array = _residue;
		if (channels < _vorbis._channels)
		{
			array = new float[channels][];
			Array.Copy(_residue, array, channels);
		}
		for (int i = 0; i < channels; i++)
		{
			Array.Clear(array[i], 0, array[i].Length);
		}
		return array;
	}

	internal abstract float[][] Decode(DataPacket packet, bool[] doNotDecode, int channels, int blockSize);

	protected abstract void Init(DataPacket packet);
}
