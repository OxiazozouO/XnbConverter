using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NVorbis;

internal abstract class VorbisFloor
{
	internal abstract class PacketData
	{
		internal int BlockSize;

		protected abstract bool HasEnergy { get; }

		internal bool ForceEnergy { get; set; }

		internal bool ForceNoEnergy { get; set; }

		internal bool ExecuteChannel => (ForceEnergy | HasEnergy) & !ForceNoEnergy;
	}

	private class Floor0 : VorbisFloor
	{
		private class PacketData0 : PacketData
		{
			internal float[] Coeff;

			internal float Amp;

			protected override bool HasEnergy => Amp > 0f;
		}

		private int _order;

		private int _rate;

		private int _bark_map_size;

		private int _ampBits;

		private int _ampOfs;

		private int _ampDiv;

		private VorbisCodebook[] _books;

		private int _bookBits;

		private Dictionary<int, float[]> _wMap;

		private Dictionary<int, int[]> _barkMaps;

		private PacketData0[] _reusablePacketData;

		internal Floor0(VorbisStreamDecoder vorbis)
			: base(vorbis)
		{
		}

		protected override void Init(DataPacket packet)
		{
			_order = (int)packet.ReadBits(8);
			_rate = (int)packet.ReadBits(16);
			_bark_map_size = (int)packet.ReadBits(16);
			_ampBits = (int)packet.ReadBits(6);
			_ampOfs = (int)packet.ReadBits(8);
			_books = new VorbisCodebook[(int)packet.ReadBits(4) + 1];
			if (_order < 1 || _rate < 1 || _bark_map_size < 1 || _books.Length == 0)
			{
				throw new InvalidDataException();
			}
			_ampDiv = (1 << _ampBits) - 1;
			for (int i = 0; i < _books.Length; i++)
			{
				int num = (int)packet.ReadBits(8);
				if (num < 0 || num >= _vorbis.Books.Length)
				{
					throw new InvalidDataException();
				}
				VorbisCodebook vorbisCodebook = _vorbis.Books[num];
				if (vorbisCodebook.MapType == 0 || vorbisCodebook.Dimensions < 1)
				{
					throw new InvalidDataException();
				}
				_books[i] = vorbisCodebook;
			}
			_bookBits = Utils.ilog(_books.Length);
			_barkMaps = new Dictionary<int, int[]>();
			_barkMaps[_vorbis.Block0Size] = SynthesizeBarkCurve(_vorbis.Block0Size / 2);
			_barkMaps[_vorbis.Block1Size] = SynthesizeBarkCurve(_vorbis.Block1Size / 2);
			_wMap = new Dictionary<int, float[]>();
			_wMap[_vorbis.Block0Size] = SynthesizeWDelMap(_vorbis.Block0Size / 2);
			_wMap[_vorbis.Block1Size] = SynthesizeWDelMap(_vorbis.Block1Size / 2);
			_reusablePacketData = new PacketData0[_vorbis._channels];
			for (int j = 0; j < _reusablePacketData.Length; j++)
			{
				_reusablePacketData[j] = new PacketData0
				{
					Coeff = new float[_order + 1]
				};
			}
		}

		private int[] SynthesizeBarkCurve(int n)
		{
			float num = (float)_bark_map_size / toBARK(_rate / 2);
			int[] array = new int[n + 1];
			for (int i = 0; i < n - 1; i++)
			{
				array[i] = Math.Min(_bark_map_size - 1, (int)Math.Floor(toBARK((float)_rate / 2f / (float)n * (float)i) * num));
			}
			array[n] = -1;
			return array;
		}

		private static float toBARK(double lsp)
		{
			return (float)(13.1 * Math.Atan(0.00074 * lsp) + 2.24 * Math.Atan(1.85E-08 * lsp * lsp) + 0.0001 * lsp);
		}

		private float[] SynthesizeWDelMap(int n)
		{
			float num = (float)(Math.PI / (double)_bark_map_size);
			float[] array = new float[n];
			for (int i = 0; i < n; i++)
			{
				array[i] = 2f * (float)Math.Cos(num * (float)i);
			}
			return array;
		}

		internal override PacketData UnpackPacket(DataPacket packet, int blockSize, int channel)
		{
			PacketData0 packetData = _reusablePacketData[channel];
			packetData.BlockSize = blockSize;
			packetData.ForceEnergy = false;
			packetData.ForceNoEnergy = false;
			packetData.Amp = packet.ReadBits(_ampBits);
			if (packetData.Amp > 0f)
			{
				Array.Clear(packetData.Coeff, 0, packetData.Coeff.Length);
				packetData.Amp = packetData.Amp / (float)_ampDiv * (float)_ampOfs;
				uint num = (uint)packet.ReadBits(_bookBits);
				if (num >= _books.Length)
				{
					packetData.Amp = 0f;
					return packetData;
				}
				VorbisCodebook vorbisCodebook = _books[num];
				int i = 0;
				while (i < _order)
				{
					int num2 = vorbisCodebook.DecodeScalar(packet);
					if (num2 == -1)
					{
						packetData.Amp = 0f;
						return packetData;
					}
					int num3 = 0;
					for (; i < _order; i++)
					{
						if (num3 >= vorbisCodebook.Dimensions)
						{
							break;
						}
						packetData.Coeff[i] = vorbisCodebook[num2, num3];
						num3++;
					}
				}
				float num4 = 0f;
				int num5 = 0;
				while (num5 < _order)
				{
					int num6 = 0;
					while (num5 < _order && num6 < vorbisCodebook.Dimensions)
					{
						packetData.Coeff[num5] += num4;
						num5++;
						num6++;
					}
					num4 = packetData.Coeff[num5 - 1];
				}
			}
			return packetData;
		}

		internal override void Apply(PacketData packetData, float[] residue)
		{
			if (!(packetData is PacketData0 packetData2))
			{
				throw new ArgumentException("Incorrect packet data!");
			}
			int num = packetData2.BlockSize / 2;
			if (packetData2.Amp > 0f)
			{
				int[] array = _barkMaps[packetData2.BlockSize];
				float[] array2 = _wMap[packetData2.BlockSize];
				int num2 = 0;
				for (num2 = 0; num2 < _order; num2++)
				{
					packetData2.Coeff[num2] = 2f * (float)Math.Cos(packetData2.Coeff[num2]);
				}
				num2 = 0;
				while (num2 < num)
				{
					int num3 = array[num2];
					float num4 = 0.5f;
					float num5 = 0.5f;
					float num6 = array2[num3];
					int i;
					for (i = 1; i < _order; i += 2)
					{
						num5 *= num6 - packetData2.Coeff[i - 1];
						num4 *= num6 - packetData2.Coeff[i];
					}
					if (i == _order)
					{
						num5 *= num6 - packetData2.Coeff[i - 1];
						num4 *= num4 * (4f - num6 * num6);
						num5 *= num5;
					}
					else
					{
						num4 *= num4 * (2f - num6);
						num5 *= num5 * (2f + num6);
					}
					num5 = packetData2.Amp / (float)Math.Sqrt(num4 + num5) - (float)_ampOfs;
					num5 = (float)Math.Exp(num5 * 0.11512925f);
					residue[num2] *= num5;
					while (array[++num2] == num3)
					{
						residue[num2] *= num5;
					}
				}
			}
			else
			{
				Array.Clear(residue, 0, num);
			}
		}
	}

	private class Floor1 : VorbisFloor
	{
		private class PacketData1 : PacketData
		{
			public int[] Posts = new int[64];

			public int PostCount;

			protected override bool HasEnergy => PostCount > 0;
		}

		private int[] _partitionClass;

		private int[] _classDimensions;

		private int[] _classSubclasses;

		private int[] _xList;

		private int[] _classMasterBookIndex;

		private int[] _hNeigh;

		private int[] _lNeigh;

		private int[] _sortIdx;

		private int _multiplier;

		private int _range;

		private int _yBits;

		private VorbisCodebook[] _classMasterbooks;

		private VorbisCodebook[][] _subclassBooks;

		private int[][] _subclassBookIndex;

		private static int[] _rangeLookup = new int[4] { 256, 128, 86, 64 };

		private static int[] _yBitsLookup = new int[4] { 8, 7, 7, 6 };

		private PacketData1[] _reusablePacketData;

		private bool[] _stepFlags = new bool[64];

		private int[] _finalY = new int[64];

		private static readonly float[] inverse_dB_table = new float[256]
		{
			1.0649863E-07f, 1.1341951E-07f, 1.2079015E-07f, 1.2863978E-07f, 1.369995E-07f, 1.459025E-07f, 1.5538409E-07f, 1.6548181E-07f, 1.7623574E-07f, 1.8768856E-07f,
			1.998856E-07f, 2.128753E-07f, 2.2670913E-07f, 2.4144197E-07f, 2.5713223E-07f, 2.7384212E-07f, 2.9163792E-07f, 3.1059022E-07f, 3.307741E-07f, 3.5226967E-07f,
			3.7516213E-07f, 3.995423E-07f, 4.255068E-07f, 4.5315863E-07f, 4.8260745E-07f, 5.1397E-07f, 5.4737063E-07f, 5.829419E-07f, 6.208247E-07f, 6.611694E-07f,
			7.041359E-07f, 7.4989464E-07f, 7.98627E-07f, 8.505263E-07f, 9.057983E-07f, 9.646621E-07f, 1.0273513E-06f, 1.0941144E-06f, 1.1652161E-06f, 1.2409384E-06f,
			1.3215816E-06f, 1.4074654E-06f, 1.4989305E-06f, 1.5963394E-06f, 1.7000785E-06f, 1.8105592E-06f, 1.9282195E-06f, 2.053526E-06f, 2.1869757E-06f, 2.3290977E-06f,
			2.4804558E-06f, 2.6416496E-06f, 2.813319E-06f, 2.9961443E-06f, 3.1908505E-06f, 3.39821E-06f, 3.619045E-06f, 3.8542307E-06f, 4.1047006E-06f, 4.371447E-06f,
			4.6555283E-06f, 4.958071E-06f, 5.280274E-06f, 5.623416E-06f, 5.988857E-06f, 6.3780467E-06f, 6.7925284E-06f, 7.2339453E-06f, 7.704048E-06f, 8.2047E-06f,
			8.737888E-06f, 9.305725E-06f, 9.910464E-06f, 1.0554501E-05f, 1.1240392E-05f, 1.1970856E-05f, 1.2748789E-05f, 1.3577278E-05f, 1.4459606E-05f, 1.5399271E-05f,
			1.6400005E-05f, 1.7465769E-05f, 1.8600793E-05f, 1.9809577E-05f, 2.1096914E-05f, 2.2467912E-05f, 2.3928002E-05f, 2.5482977E-05f, 2.7139005E-05f, 2.890265E-05f,
			3.078091E-05f, 3.2781227E-05f, 3.4911533E-05f, 3.718028E-05f, 3.9596467E-05f, 4.2169668E-05f, 4.491009E-05f, 4.7828602E-05f, 5.0936775E-05f, 5.424693E-05f,
			5.7772202E-05f, 6.152657E-05f, 6.552491E-05f, 6.9783084E-05f, 7.4317984E-05f, 7.914758E-05f, 8.429104E-05f, 8.976875E-05f, 9.560242E-05f, 0.00010181521f,
			0.00010843174f, 0.00011547824f, 0.00012298267f, 0.00013097477f, 0.00013948625f, 0.00014855085f, 0.00015820454f, 0.00016848555f, 0.00017943469f, 0.00019109536f,
			0.00020351382f, 0.0002167393f, 0.00023082423f, 0.00024582449f, 0.00026179955f, 0.00027881275f, 0.00029693157f, 0.00031622787f, 0.00033677815f, 0.00035866388f,
			0.00038197188f, 0.00040679457f, 0.00043323037f, 0.0004613841f, 0.0004913675f, 0.00052329927f, 0.0005573062f, 0.0005935231f, 0.0006320936f, 0.0006731706f,
			0.000716917f, 0.0007635063f, 0.00081312325f, 0.00086596457f, 0.00092223985f, 0.0009821722f, 0.0010459992f, 0.0011139743f, 0.0011863665f, 0.0012634633f,
			0.0013455702f, 0.0014330129f, 0.0015261382f, 0.0016253153f, 0.0017309374f, 0.0018434235f, 0.0019632196f, 0.0020908006f, 0.0022266726f, 0.0023713743f,
			0.0025254795f, 0.0026895993f, 0.0028643848f, 0.0030505287f, 0.003248769f, 0.0034598925f, 0.0036847359f, 0.0039241905f, 0.0041792067f, 0.004450795f,
			0.004740033f, 0.005048067f, 0.0053761187f, 0.005725489f, 0.0060975635f, 0.0064938175f, 0.0069158226f, 0.0073652514f, 0.007843887f, 0.008353627f,
			0.008896492f, 0.009474637f, 0.010090352f, 0.01074608f, 0.011444421f, 0.012188144f, 0.012980198f, 0.013823725f, 0.014722068f, 0.015678791f,
			0.016697686f, 0.017782796f, 0.018938422f, 0.020169148f, 0.021479854f, 0.022875736f, 0.02436233f, 0.025945531f, 0.027631618f, 0.029427277f,
			0.031339627f, 0.03337625f, 0.035545226f, 0.037855156f, 0.0403152f, 0.042935107f, 0.045725275f, 0.048696756f, 0.05186135f, 0.05523159f,
			0.05882085f, 0.062643364f, 0.06671428f, 0.07104975f, 0.075666964f, 0.08058423f, 0.08582105f, 0.09139818f, 0.097337745f, 0.1036633f,
			0.11039993f, 0.11757434f, 0.12521498f, 0.13335215f, 0.14201812f, 0.15124726f, 0.16107617f, 0.1715438f, 0.18269168f, 0.19456401f,
			0.20720787f, 0.22067343f, 0.23501402f, 0.25028655f, 0.26655158f, 0.28387362f, 0.3023213f, 0.32196787f, 0.34289113f, 0.36517414f,
			0.3889052f, 0.41417846f, 0.44109413f, 0.4697589f, 0.50028646f, 0.53279793f, 0.5674221f, 0.6042964f, 0.64356697f, 0.6853896f,
			0.72993004f, 0.777365f, 0.8278826f, 0.88168305f, 0.9389798f, 1f
		};

		internal Floor1(VorbisStreamDecoder vorbis)
			: base(vorbis)
		{
		}

		protected override void Init(DataPacket packet)
		{
			_partitionClass = new int[(uint)packet.ReadBits(5)];
			for (int i = 0; i < _partitionClass.Length; i++)
			{
				_partitionClass[i] = (int)packet.ReadBits(4);
			}
			int num = _partitionClass.Max();
			_classDimensions = new int[num + 1];
			_classSubclasses = new int[num + 1];
			_classMasterbooks = new VorbisCodebook[num + 1];
			_classMasterBookIndex = new int[num + 1];
			_subclassBooks = new VorbisCodebook[num + 1][];
			_subclassBookIndex = new int[num + 1][];
			for (int j = 0; j <= num; j++)
			{
				_classDimensions[j] = (int)packet.ReadBits(3) + 1;
				_classSubclasses[j] = (int)packet.ReadBits(2);
				if (_classSubclasses[j] > 0)
				{
					_classMasterBookIndex[j] = (int)packet.ReadBits(8);
					_classMasterbooks[j] = _vorbis.Books[_classMasterBookIndex[j]];
				}
				_subclassBooks[j] = new VorbisCodebook[1 << _classSubclasses[j]];
				_subclassBookIndex[j] = new int[_subclassBooks[j].Length];
				for (int k = 0; k < _subclassBooks[j].Length; k++)
				{
					int num2 = (int)packet.ReadBits(8) - 1;
					if (num2 >= 0)
					{
						_subclassBooks[j][k] = _vorbis.Books[num2];
					}
					_subclassBookIndex[j][k] = num2;
				}
			}
			_multiplier = (int)packet.ReadBits(2);
			_range = _rangeLookup[_multiplier];
			_yBits = _yBitsLookup[_multiplier];
			_multiplier++;
			int num3 = (int)packet.ReadBits(4);
			List<int> list = new List<int>();
			list.Add(0);
			list.Add(1 << num3);
			for (int l = 0; l < _partitionClass.Length; l++)
			{
				int num4 = _partitionClass[l];
				for (int m = 0; m < _classDimensions[num4]; m++)
				{
					list.Add((int)packet.ReadBits(num3));
				}
			}
			_xList = list.ToArray();
			_lNeigh = new int[list.Count];
			_hNeigh = new int[list.Count];
			_sortIdx = new int[list.Count];
			_sortIdx[0] = 0;
			_sortIdx[1] = 1;
			for (int n = 2; n < _lNeigh.Length; n++)
			{
				_lNeigh[n] = 0;
				_hNeigh[n] = 1;
				_sortIdx[n] = n;
				for (int num5 = 2; num5 < n; num5++)
				{
					int num6 = _xList[num5];
					if (num6 < _xList[n])
					{
						if (num6 > _xList[_lNeigh[n]])
						{
							_lNeigh[n] = num5;
						}
					}
					else if (num6 < _xList[_hNeigh[n]])
					{
						_hNeigh[n] = num5;
					}
				}
			}
			for (int num7 = 0; num7 < _sortIdx.Length - 1; num7++)
			{
				for (int num8 = num7 + 1; num8 < _sortIdx.Length; num8++)
				{
					if (_xList[num7] == _xList[num8])
					{
						throw new InvalidDataException();
					}
					if (_xList[_sortIdx[num7]] > _xList[_sortIdx[num8]])
					{
						int num9 = _sortIdx[num7];
						_sortIdx[num7] = _sortIdx[num8];
						_sortIdx[num8] = num9;
					}
				}
			}
			_reusablePacketData = new PacketData1[_vorbis._channels];
			for (int num10 = 0; num10 < _reusablePacketData.Length; num10++)
			{
				_reusablePacketData[num10] = new PacketData1();
			}
		}

		internal override PacketData UnpackPacket(DataPacket packet, int blockSize, int channel)
		{
			PacketData1 packetData = _reusablePacketData[channel];
			packetData.BlockSize = blockSize;
			packetData.ForceEnergy = false;
			packetData.ForceNoEnergy = false;
			packetData.PostCount = 0;
			Array.Clear(packetData.Posts, 0, 64);
			if (packet.ReadBit())
			{
				int num = 2;
				packetData.Posts[0] = (int)packet.ReadBits(_yBits);
				packetData.Posts[1] = (int)packet.ReadBits(_yBits);
				for (int i = 0; i < _partitionClass.Length; i++)
				{
					int num2 = _partitionClass[i];
					int num3 = _classDimensions[num2];
					int num4 = _classSubclasses[num2];
					int num5 = (1 << num4) - 1;
					uint num6 = 0u;
					if (num4 > 0 && (num6 = (uint)_classMasterbooks[num2].DecodeScalar(packet)) == uint.MaxValue)
					{
						num = 0;
						break;
					}
					for (int j = 0; j < num3; j++)
					{
						VorbisCodebook vorbisCodebook = _subclassBooks[num2][num6 & num5];
						num6 >>= num4;
						if (vorbisCodebook != null && (packetData.Posts[num] = vorbisCodebook.DecodeScalar(packet)) == -1)
						{
							num = 0;
							i = _partitionClass.Length;
							break;
						}
						num++;
					}
				}
				packetData.PostCount = num;
			}
			return packetData;
		}

		internal override void Apply(PacketData packetData, float[] residue)
		{
			if (!(packetData is PacketData1 packetData2))
			{
				throw new ArgumentException("Incorrect packet data!", "packetData");
			}
			int num = packetData2.BlockSize / 2;
			if (packetData2.PostCount > 0)
			{
				bool[] array = UnwrapPosts(packetData2);
				int num2 = 0;
				int num3 = packetData2.Posts[0] * _multiplier;
				for (int i = 1; i < packetData2.PostCount; i++)
				{
					int num4 = _sortIdx[i];
					if (array[num4])
					{
						int num5 = _xList[num4];
						int num6 = packetData2.Posts[num4] * _multiplier;
						if (num2 < num)
						{
							RenderLineMulti(num2, num3, Math.Min(num5, num), num6, residue);
						}
						num2 = num5;
						num3 = num6;
					}
					if (num2 >= num)
					{
						break;
					}
				}
				if (num2 < num)
				{
					RenderLineMulti(num2, num3, num, num3, residue);
				}
			}
			else
			{
				Array.Clear(residue, 0, num);
			}
		}

		private bool[] UnwrapPosts(PacketData1 data)
		{
			Array.Clear(_stepFlags, 2, 62);
			_stepFlags[0] = true;
			_stepFlags[1] = true;
			Array.Clear(_finalY, 2, 62);
			_finalY[0] = data.Posts[0];
			_finalY[1] = data.Posts[1];
			for (int i = 2; i < data.PostCount; i++)
			{
				int num = _lNeigh[i];
				int num2 = _hNeigh[i];
				int num3 = RenderPoint(_xList[num], _finalY[num], _xList[num2], _finalY[num2], _xList[i]);
				int num4 = data.Posts[i];
				int num5 = _range - num3;
				int num6 = num3;
				int num7 = ((num5 >= num6) ? (num6 * 2) : (num5 * 2));
				if (num4 != 0)
				{
					_stepFlags[num] = true;
					_stepFlags[num2] = true;
					_stepFlags[i] = true;
					if (num4 >= num7)
					{
						if (num5 > num6)
						{
							_finalY[i] = num4 - num6 + num3;
						}
						else
						{
							_finalY[i] = num3 - num4 + num5 - 1;
						}
					}
					else if (num4 % 2 == 1)
					{
						_finalY[i] = num3 - (num4 + 1) / 2;
					}
					else
					{
						_finalY[i] = num3 + num4 / 2;
					}
				}
				else
				{
					_stepFlags[i] = false;
					_finalY[i] = num3;
				}
			}
			for (int j = 0; j < data.PostCount; j++)
			{
				data.Posts[j] = _finalY[j];
			}
			return _stepFlags;
		}

		private int RenderPoint(int x0, int y0, int x1, int y1, int X)
		{
			int num = y1 - y0;
			int num2 = x1 - x0;
			int num3 = Math.Abs(num) * (X - x0) / num2;
			if (num < 0)
			{
				return y0 - num3;
			}
			return y0 + num3;
		}

		private void RenderLineMulti(int x0, int y0, int x1, int y1, float[] v)
		{
			int num = y1 - y0;
			int num2 = x1 - x0;
			int num3 = Math.Abs(num);
			int num4 = 1 - ((num >> 31) & 1) * 2;
			int num5 = num / num2;
			int num6 = x0;
			int num7 = y0;
			int num8 = -num2;
			v[x0] *= inverse_dB_table[y0];
			num3 -= Math.Abs(num5) * num2;
			while (++num6 < x1)
			{
				num7 += num5;
				num8 += num3;
				if (num8 >= 0)
				{
					num8 -= num2;
					num7 += num4;
				}
				v[num6] *= inverse_dB_table[num7];
			}
		}
	}

	private VorbisStreamDecoder _vorbis;

	internal static VorbisFloor Init(VorbisStreamDecoder vorbis, DataPacket packet)
	{
		int num = (int)packet.ReadBits(16);
		VorbisFloor vorbisFloor = null;
		switch (num)
		{
		case 0:
			vorbisFloor = new Floor0(vorbis);
			break;
		case 1:
			vorbisFloor = new Floor1(vorbis);
			break;
		}
		if (vorbisFloor == null)
		{
			throw new InvalidDataException();
		}
		vorbisFloor.Init(packet);
		return vorbisFloor;
	}

	protected VorbisFloor(VorbisStreamDecoder vorbis)
	{
		_vorbis = vorbis;
	}

	protected abstract void Init(DataPacket packet);

	internal abstract PacketData UnpackPacket(DataPacket packet, int blockSize, int channel);

	internal abstract void Apply(PacketData packetData, float[] residue);
}
