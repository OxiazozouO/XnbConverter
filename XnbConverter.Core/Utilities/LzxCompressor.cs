namespace XnbConverter.Utilities;

/// <summary>
/// LZX 压缩器，逐位镜像 <see cref="Lzx"/> 的解码流程：
/// 相同的 16 位小端字位序（字内 MSB 优先）、相同的容器帧头、
/// 相同的 aligned 块结构、相同的预树增量编码、相同的 R0/R1/R2 与槽位规则。
///
/// 结构上与原始编码器一致：整份数据只发一个块（超长时按帧边界分块，块长上限 24 位），
/// 容器再把该块按 32768 字节的帧切开。因此匹配与字面量都不能跨越帧边界。
/// </summary>
public static class LzxCompressor
{
	private const int WindowBits = 16;

	/// <summary>窗口大小 64KB，与解码器 Lzx(16) 对应。</summary>
	private const int WindowSize = 1 << WindowBits;

	/// <summary>整帧未压缩字节数，对应解码器 else 分支硬编码的 32768。</summary>
	private const int FrameSize = 32768;

	private const int MaxMatch = 257;

	private const uint Verbatim = 1u;

	private const uint Aligned = 2u;

	private const int NumChars = 256;

	/// <summary>槽位数量 = window &lt;&lt; 1 = 32，仅槽位 0..31 可用。</summary>
	private const int NumSlots = WindowBits << 1;

	/// <summary>主树符号数 = 256 + (32 &lt;&lt; 3) = 512，与解码器 MainElements 一致。</summary>
	private const int MainElements = NumChars + (NumSlots << 3);

	/// <summary>解码器为长度树符号数传 250，但长度数组仅 249，故有效符号数为 249。</summary>
	private const int LengthSymbols = 249;

	private const int AlignedSymbols = 8;

	private const int PreTreeSymbols = 20;

	private const int MainBits = 12;

	private const int LengthBits = 12;

	private const int AlignedBits = 7;

	private const int PreTreeBits = 6;

	/// <summary>
	/// 每块最多容纳的整帧数。原始编码器约每 28 万未压缩字节换一块（3.2MB 的文件分成 12 块），
	/// 换成 8 帧（262144 字节）最接近；块边界必须落在帧边界上，否则解码器的按帧记账会错位。
	/// 块长为 24 位，8 帧（262144 字节）远低于上限。
	/// </summary>
	private const int MaxFramesPerBlock = 8;

	private const int HashSize = 1 << 16;

	private const int HashMask = HashSize - 1;

	private const int WindowMask = WindowSize - 1;

	private const int MaxChain = 96;

	private static readonly byte[] ExtraBits = new byte[52];

	private static readonly uint[] PositionBase = new uint[51];

	/// <summary>距离 -&gt; 槽位（仅槽位 3..31）。</summary>
	private static readonly ushort[] SlotTable = new ushort[WindowSize + 1];

	/// <summary>可表示的最大匹配距离：槽位 31 的上界，再远就没有对应的主树符号。</summary>
	private static readonly int MaxDistance;

	static LzxCompressor()
	{
		int i = 0;
		int num = 0;
		for (; i < 52; i += 2)
		{
			ExtraBits[i] = ExtraBits[i + 1] = (byte)num;
			if (i != 0 && num < 17)
			{
				num++;
			}
		}

		int j = 0;
		num = 0;
		for (; j < 51; j++)
		{
			PositionBase[j] = (uint)num;
			num += 1 << ExtraBits[j];
		}

		for (int slot = 3; slot < NumSlots; slot++)
		{
			uint b = PositionBase[slot] - 2;
			uint cnt = 1u << ExtraBits[slot];
			for (uint k = 0; k < cnt; k++)
			{
				uint d = b + k;
				if (d >= 1 && d <= WindowSize)
				{
					SlotTable[d] = (ushort)slot;
				}
			}
		}

		MaxDistance = (int)(PositionBase[NumSlots - 1] - 2 + (1u << ExtraBits[NumSlots - 1]) - 1);
	}

	/// <summary>
	/// 压缩 <paramref name="data"/> 的 [index, index+count) 区间，
	/// 返回可直接写入 XNB 第 14 字节之后的载荷。
	/// </summary>
	public static byte[] Compress(byte[] data, int index, int count)
	{
		if (count <= 0)
		{
			return Array.Empty<byte>();
		}

		return new Encoder(data, index, count).Run(null);
	}

	/// <summary>
	/// 用给定的词法单元序列压缩，跳过匹配查找。
	/// 单元为 (是否字面量, 长度, 距离/字节值)。
	/// 用于隔离验证编码阶段（哈夫曼/预树/容器）是否与原始编码器逐位一致。
	/// </summary>
	public static byte[] CompressWithTokens(byte[] data, int index, int count,
		IReadOnlyList<(bool Lit, int Length, int Distance)> tokens)
	{
		if (count <= 0)
		{
			return Array.Empty<byte>();
		}

		return new Encoder(data, index, count).Run(tokens);
	}

	private sealed class Encoder
	{
		private readonly byte[] _data;

		private readonly int _start;

		private readonly int _end;

		/// <summary>主树长度表，跨块保持，作为增量编码的基准。</summary>
		private readonly byte[] _mainTreeLen = new byte[656];

		private readonly byte[] _lengthLen = new byte[LengthSymbols];

		private readonly byte[] _alignedLen = new byte[AlignedSymbols];

		private readonly int[] _head = new int[HashSize];

		private readonly int[] _prev = new int[WindowSize];

		public Encoder(byte[] data, int index, int count)
		{
			_data = data;
			_start = index;
			_end = index + count;
		}

		public byte[] Run(IReadOnlyList<(bool Lit, int Length, int Distance)>? source)
		{
			Array.Fill(_head, -1);
			int num = _end - _start;
			int num2 = (num + FrameSize - 1) / FrameSize;

			List<Token> list = new List<Token>();
			if (source == null)
			{
				// 第一遍：贪心解析，只为估出各符号的码长，作为代价模型的输入
				List<Token> seed = new List<Token>();
				BuildTokens(seed);
				AssignSlots(seed);
				byte[] estMain = new byte[656];
				byte[] estLen = new byte[LengthSymbols];
				byte[] estAlign = new byte[AlignedSymbols];
				BuildEstimate(seed, estMain, estLen, estAlign);

				// seed 到这里已经死了：把它整块容量交给近最优解析复用。
				// 两个列表的规模是同一量级（都是几十万个 40 字节的结构体），
				// 重新 new 一个等于再走一遍 4→N 的翻倍链，每个文件白扔几十 MB。
				// 近最优解析：内部反复「按码长解析 → 由解析结果重建码长」，匹配查找只做一次。
				// 实测（SmallFont.zh-CN，2 MB 载荷）这一步占整个编码的 98% 以上 —— 13 s 里
				// 贪心那遍只花 0.2 s。轮数是唯一现成的旋钮：6 轮 → 3 轮省 ~30% 时间、
				// 体积只涨 0.02%；降到 1 轮省 44%、但体积涨 1.9%。再要提速就得改 DP 的松弛策略。
				Array.Fill(_head, -1);
				Array.Clear(_prev);
				list = ParseOptimal(estMain, estLen, estAlign, 6, seed);
			}
			else
			{
				foreach ((bool Lit, int Length, int Distance) item in source)
				{
					list.Add(item.Lit ? Token.NewLiteral(item.Distance) : Token.NewMatch(item.Length, item.Distance));
				}
			}

			AssignSlots(list);

			using BitWriter bitWriter = new BitWriter();
			bitWriter.WriteBits(0u, 1);
			List<int> list2 = new List<int>(num2);

			int num3 = 0;
			int num4 = 0;
			while (num3 < num2)
			{
				int num5 = Math.Min(num2 - num3, MaxFramesPerBlock);
				int num6 = num3 * FrameSize + _start;
				int num7 = Math.Min(num6 + num5 * FrameSize, _end);
				int num8 = num4;
				int num9 = num6;
				while (num4 < list.Count && num9 < num7)
				{
					num9 += list[num4].OutLength;
					num4++;
				}

				EncodeBlock(bitWriter, list, num8, num4, num7 - num6, num6, list2, _end);
				num3 += num5;
			}

			bitWriter.Align();
			list2.Add(bitWriter.ByteCount);
			return BuildContainer(bitWriter.Bytes, list2);
		}

		/// <summary>
		/// 按帧长把块位流切成容器帧，每帧前置大端 16 位压缩长度。
		///
		/// 先按同一套判据算准总长再一次性精确分配 —— 取代原来
		/// 「List&lt;byte&gt; 逐个 Add + ToArray()」的两次整块拷贝。
		/// </summary>
		private byte[] BuildContainer(ReadOnlySpan<byte> stream, List<int> frameEnds)
		{
			int total = 0;
			int prev = 0;
			for (int i = 0; i < frameEnds.Count; i++)
			{
				int size = Math.Min(FrameSize, _end - _start - i * FrameSize);
				total += size == FrameSize ? 2 : 5;
				int chunk = frameEnds[i] - prev;
				if (chunk > 0)
				{
					total += chunk;
				}

				prev = frameEnds[i];
			}

			byte[] result = new byte[total];
			int at = 0;
			int num = 0;
			for (int i = 0; i < frameEnds.Count; i++)
			{
				int num2 = frameEnds[i] - num;
				int num3 = Math.Min(FrameSize, _end - _start - i * FrameSize);
				if (num3 == FrameSize)
				{
					result[at++] = (byte)(num2 >> 8);
					result[at++] = (byte)num2;
				}
				else
				{
					result[at++] = byte.MaxValue;
					result[at++] = (byte)(num3 >> 8);
					result[at++] = (byte)num3;
					result[at++] = (byte)(num2 >> 8);
					result[at++] = (byte)num2;
				}

				int len = frameEnds[i] - num;
				if (len > 0)
				{
					stream.Slice(num, len).CopyTo(result.AsSpan(at));
					at += len;
				}

				num = frameEnds[i];
			}

			return result;
		}

		/// <summary>输出 list[begin, end) 这些词法单元，构成一个 aligned 块。</summary>
		private void EncodeBlock(BitWriter bitWriter, List<Token> tokens, int begin, int end, int blockLength,
			int startOut, List<int> frameEnds, int streamEnd)
		{
			int[] array = new int[MainElements];
			int[] array2 = new int[LengthSymbols];
			int[] array3 = new int[AlignedSymbols];
			for (int i = begin; i < end; i++)
			{
				Token token = tokens[i];
				if (token.IsLiteral)
				{
					array[token.Literal]++;
					continue;
				}

				array[token.MainSymbol]++;
				if (token.LengthSymbol >= 0)
				{
					array2[token.LengthSymbol]++;
				}

				if (token.AlignSymbol >= 0)
				{
					array3[token.AlignSymbol]++;
				}
			}

			byte[] array4 = new byte[656];
			uint[] codes = new uint[656];
			BuildHuffman(array, MainElements, 12, array4, codes);
			byte[] array5 = new byte[LengthSymbols];
			uint[] codes2 = new uint[LengthSymbols];
			BuildHuffman(array2, LengthSymbols, 12, array5, codes2);
			uint[] codes3 = new uint[AlignedSymbols];
			BuildHuffman(array3, AlignedSymbols, 7, _alignedLen, codes3);

			bitWriter.WriteBits(Aligned, 3);
			bitWriter.WriteBits((uint)(blockLength >> 8) & 0xFFFFu, 16);
			bitWriter.WriteBits((uint)(blockLength & 0xFF), 8);
			for (int j = 0; j < AlignedSymbols; j++)
			{
				bitWriter.WriteBits(_alignedLen[j], 3);
			}

			WriteLengths(bitWriter, _mainTreeLen, array4, 0, NumChars);
			WriteLengths(bitWriter, _mainTreeLen, array4, NumChars, MainElements);
			WriteLengths(bitWriter, _lengthLen, array5, 0, LengthSymbols);

			int num = startOut;
			for (int k = begin; k < end; k++)
			{
				Token token2 = tokens[k];
				if (token2.IsLiteral)
				{
					bitWriter.WriteBits(codes[token2.Literal], array4[token2.Literal]);
				}
				else
				{
					bitWriter.WriteBits(codes[token2.MainSymbol], array4[token2.MainSymbol]);
					if (token2.LengthSymbol >= 0)
					{
						bitWriter.WriteBits(codes2[token2.LengthSymbol], array5[token2.LengthSymbol]);
					}

					if (token2.ExtraBitCount > 0)
					{
						bitWriter.WriteBits(token2.Extra, token2.ExtraBitCount);
					}

					if (token2.AlignSymbol >= 0)
					{
						bitWriter.WriteBits(codes3[token2.AlignSymbol], _alignedLen[token2.AlignSymbol]);
					}
				}

				num += token2.OutLength;
				// num 是绝对位置，帧边界要按载荷内的相对位置判断（_start 不一定为 0）
				if (num < streamEnd && (num - _start) % FrameSize == 0)
				{
					// 帧边界：解码器在每帧结束处 Align()，压缩流必须在此补满 16 位字
					bitWriter.Align();
					frameEnds.Add(bitWriter.ByteCount);
				}
			}

		}

		/// <summary>贪心 + 哈希链最长匹配；匹配不得跨越帧边界。</summary>
		private void BuildTokens(List<Token> tokens)
		{
			int num = _start;
			while (num < _end)
			{
				int num2 = (num - _start) / FrameSize + 1;
				int num3 = Math.Min(MaxMatch, Math.Min(_start + num2 * FrameSize, _end) - num);
				int bestLen = 0;
				int bestDist = 0;
				if (num3 >= 2)
				{
					int num4 = Math.Max(_start, num - MaxDistance);
					int num5 = _head[HashAt(num)];
					int num6 = MaxChain;
					while (num5 >= num4 && num6-- > 0)
					{
						if (_data[num5 + bestLen] == _data[num + bestLen])
						{
							int num7 = 0;
							while (num7 < num3 && _data[num5 + num7] == _data[num + num7])
							{
								num7++;
							}

							if (num7 > bestLen)
							{
								bestLen = num7;
								bestDist = num - num5;
								if (num7 >= num3)
								{
									break;
								}
							}
						}

						int num8 = _prev[num5 & WindowMask];
						if (num8 >= num5)
						{
							break;
						}

						num5 = num8;
					}
				}

				// 长度 2 的匹配只有在比两个字面量更省时才划算：
				// 主符号约 9 位 + 额外位，两个字面量约 16 位，故额外位 <= 6（距离 <= 253）
				if (bestLen > 2 || (bestLen == 2 && bestDist <= 253))
				{
					tokens.Add(Token.NewMatch(bestLen, bestDist));
					for (int i = 0; i < bestLen - 1; i++)
					{
						Insert(num + i);
					}

					num += bestLen;
				}
				else
				{
					tokens.Add(Token.NewLiteral(_data[num]));
					Insert(num);
					num++;
				}
			}
		}

		private int HashAt(int p)
		{
			// 2 字节键，才能找到长度为 2 的匹配（原始编码器会取长度 2 的匹配）
			return ((_data[p] << 8) | _data[p + 1]) & HashMask;
		}

		private void Insert(int p)
		{
			if (p + 2 > _end)
			{
				return;
			}

			int num = HashAt(p);
			_prev[p & WindowMask] = _head[num];
			_head[num] = p;
		}

		/// <summary>按词法单元统计三棵树的符号频次并建表，供代价模型估算用。</summary>
		private static void BuildEstimate(List<Token> tokens, byte[] mainLen, byte[] lenLen, byte[] alignLen)
		{
			Array.Clear(mainLen);
			Array.Clear(lenLen);
			Array.Clear(alignLen);
			int[] mainFreq = new int[MainElements];
			int[] lenFreq = new int[LengthSymbols];
			int[] alignFreq = new int[AlignedSymbols];
			foreach (Token token in tokens)
			{
				if (token.IsLiteral)
				{
					mainFreq[token.Literal]++;
					continue;
				}

				mainFreq[token.MainSymbol]++;
				if (token.LengthSymbol >= 0)
				{
					lenFreq[token.LengthSymbol]++;
				}

				if (token.AlignSymbol >= 0)
				{
					alignFreq[token.AlignSymbol]++;
				}
			}

			BuildHuffman(mainFreq, MainElements, MainBits, mainLen, new uint[656]);
			BuildHuffman(lenFreq, LengthSymbols, LengthBits, lenLen, new uint[LengthSymbols]);
			BuildHuffman(alignFreq, AlignedSymbols, AlignedBits, alignLen, new uint[AlignedSymbols]);
		}

		/// <summary>
		/// 近最优解析：把整块建成一张有向图 —— 节点是输出位置，边是「字面量」或「匹配」，
		/// 用 DP 求块首到块尾的最小代价路径。代价单位是「位」，直接取自估出的码长。
		/// 候选边含 R0/R1/R2 复用匹配与显式偏移匹配，且每个长度只保留最小偏移（剪枝启发式）。
		/// </summary>
		/// <remarks>
		/// 匹配查找在每个窗口只做一次并缓存（对应 liblzx 的 match_cache），多轮迭代复用；
		/// 每轮只需重算依赖 R0/R1/R2 状态的复用匹配，因此迭代几乎不增加耗时。
		/// </remarks>


		/// <summary>
		/// 把 <see cref="MatchCost"/> 拆成「只跟长度有关」和「只跟距离有关」两块：
		/// <c>cost(slot, dist, len) = lenPart[slot][len] + ExtraPart(slot, dist)</c>。
		///
		/// 这是精确分解（原式里长度只影响主符号与长度符号，距离只影响额外位与对齐符号），
		/// 但能把它从最内层循环里挪出去：这个 DP 每轮要松弛上亿次，原来每次都走一遍
		/// 函数调用 + 多次查表 + 分支，现在只剩一次加法加一次比较。
		/// </summary>
		private int[]? _lenPart;

		/// <summary>按当前轮的码长重建 lenPart 表（码长每个窗口、每轮都会变）。</summary>
		private void RebuildLenPart(byte[] mainLen, byte[] lenLen)
		{
			if (_lenPart == null)
			{
				_lenPart = new int[NumSlots * (MaxMatch + 1)];
			}

			for (int slot = 0; slot < NumSlots; slot++)
			{
				int b = slot * (MaxMatch + 1);
				int mainBase = NumChars + (slot << 3);
				for (int len = 2; len <= MaxMatch; len++)
				{
					int lenHead = len - 2;
					int c = SymbolCost(mainLen, mainBase + (lenHead < 7 ? lenHead : 7), MainBits);
					if (lenHead >= 7)
					{
						c += SymbolCost(lenLen, lenHead - 7, LengthBits);
					}

					_lenPart[b + len] = c;
				}
			}
		}

		/// <summary>MatchCost 里只跟槽位与距离有关的那一半（额外位 / 对齐符号）。</summary>
		private static int ExtraPart(byte[] alignLen, int slot, int dist)
		{
			int bits = ExtraBits[slot];
			if (bits <= 0)
			{
				return 0;
			}

			int value = dist - (int)(PositionBase[slot] - 2);
			if (bits <= 2)
			{
				return bits;
			}

			return bits == 3
				? SymbolCost(alignLen, value, AlignedBits)
				: bits - 3 + SymbolCost(alignLen, value & 7, AlignedBits);
		}

		private List<Token> ParseOptimal(byte[] mainLen, byte[] lenLen, byte[] alignLen, int passes,
			List<Token> reuse)
		{
			const int DpWindow = 1 << 16;
			const int MaxRecords = 8;
			// 复用调用方给出的容量（那是上一遍贪心解析留下的、已经没人用的列表）
			List<Token> tokens = reuse;
			tokens.Clear();
			uint r0 = 1u;
			uint r1 = 1u;
			uint r2 = 1u;

			// 这批 DP 工作数组合计 6MB 出头，而每次压缩（每个文件）都要一整批。
			// 走池复用：单个字体文件压缩本来要分配三百多 MB，其中这 6MB 每次都白扔。
			int[] recStart = Pool.RentBigInt(DpWindow + 1);
			int[] recLen = Pool.RentBigInt(DpWindow * MaxRecords);
			int[] recDist = Pool.RentBigInt(DpWindow * MaxRecords);
			int[] cost = Pool.RentBigInt(DpWindow + 1);
			int[] edgeRep = Pool.RentBigInt(DpWindow + 1);
			int[] edgeLen = Pool.RentBigInt(DpWindow + 1);
			int[] edgeDist = Pool.RentBigInt(DpWindow + 1);
			uint[] q0 = Pool.RentUInt(DpWindow + 1);
			uint[] q1 = Pool.RentUInt(DpWindow + 1);
			uint[] q2 = Pool.RentUInt(DpWindow + 1);
			int[] smallestOffset = Pool.RentBigInt(MaxMatch + 1);
			try
			{
			List<Token> local = new List<Token>();

			// 复用的「码长估计样本」缓冲，见下面 estimate.Clear() 处的说明
			List<Token> estimate = new List<Token>();

			// 上一轮的词法单元，用于判断本轮是否已收敛
			List<Token> prevLocal = new List<Token>();

			int blockStart = _start;
			while (blockStart < _end)
			{
				int blockEnd = Math.Min(blockStart + DpWindow, _end);
				int size = blockEnd - blockStart;

				// ---- 1) 匹配查找只做一次：记录每个位置的「记录长度」匹配（长度递增、距离递增）----
				int total = 0;
				for (int i = 0; i < size; i++)
				{
					recStart[i] = total;
					int p = blockStart + i;
					int limit = Math.Min(Math.Min(FrameEndOf(p), blockEnd), _end);
					int maxLen = Math.Min(MaxMatch, limit - p);
					if (maxLen >= 2)
					{
						int best = 1;
						int cand = _head[HashAt(p)];
						int guard = 1024;
						int chainLimit = Math.Max(_start, p - MaxDistance);
						while (cand >= chainLimit && guard-- > 0)
						{
							if (_data[cand + best] == _data[p + best])
							{
								// 一次比 8 字节：异或后取尾零数直接得到首个不同字节的下标。
								// 这是整个编码里调用最密的循环（每个位置沿哈希链最多 1024 个候选，
								// 每个候选都要比一次），逐字节版本占了匹配查找的主要时间。
								// 边界：p + maxLen 不超过 _end，而 cand < p，所以读 8 字节不会越界。
								int n = 0;
								while (n + 8 <= maxLen)
								{
									ulong x = System.Buffers.Binary.BinaryPrimitives
										.ReadUInt64LittleEndian(_data.AsSpan(cand + n));
									ulong y = System.Buffers.Binary.BinaryPrimitives
										.ReadUInt64LittleEndian(_data.AsSpan(p + n));
									ulong diff = x ^ y;
									if (diff != 0)
									{
										n += System.Numerics.BitOperations.TrailingZeroCount(diff) >> 3;
										break;
									}

									n += 8;
								}

								while (n < maxLen && _data[cand + n] == _data[p + n])
								{
									n++;
								}

								if (n > best)
								{
									recLen[total] = n;
									recDist[total] = p - cand;
									total++;
									best = n;
									if (best >= maxLen || total >= recLen.Length)
									{
										break;
									}
								}
							}

							int nx = _prev[cand & WindowMask];
							if (nx >= cand)
							{
								break;
							}

							cand = nx;
						}
					}

					Insert(p);
				}

				recStart[size] = total;

				// ---- 2) 多轮 DP，复用上面的匹配缓存 ----
				bool converged = false;
				for (int pass = 0; pass < passes && !converged; pass++)
				{
						Array.Fill(cost, int.MaxValue, 0, size + 1);
					RebuildLenPart(mainLen, lenLen);
					cost[0] = 0;
					q0[0] = r0;
					q1[0] = r1;
					q2[0] = r2;

					for (int i = 0; i < size; i++)
					{
						if (cost[i] == int.MaxValue)
						{
							continue;
						}

								int p = blockStart + i;
						uint cr0 = q0[i];
						uint cr1 = q1[i];
						uint cr2 = q2[i];
						// 匹配不得跨越帧边界：解码器按帧记账，跨越会让输出错位
						int limit = Math.Min(Math.Min(FrameEndOf(p), blockEnd), _end);
						int maxLen = Math.Min(MaxMatch, limit - p);

						// 1) 字面量
						int litCost = cost[i] + LiteralCost(mainLen, _data[p]);
						if (litCost < cost[i + 1])
						{
							cost[i + 1] = litCost;
							edgeRep[i + 1] = -1;
							edgeLen[i + 1] = 1;
							edgeDist[i + 1] = _data[p];
							q0[i + 1] = cr0;
							q1[i + 1] = cr1;
							q2[i + 1] = cr2;
						}

						if (maxLen < 2)
						{
							continue;
						}

						// 2) R0/R1/R2 复用匹配（槽位 0/1/2，无额外位，代价最低）
						for (int rep = 0; rep < 3; rep++)
						{
							uint rd = rep == 0 ? cr0 : (rep == 1 ? cr1 : cr2);
							int repLen = MatchLenAt(p, (int)rd, maxLen);
										// 复用匹配的槽位就是 rep 本身，距离也固定，所以代价只剩「长度」那一半
							int repExtra = ExtraPart(alignLen, rep, (int)rd);
							int repBase = rep * (MaxMatch + 1);
							int baseCost = cost[i];
							for (int len = 2; len <= repLen; len++)
							{
								int c = baseCost + _lenPart![repBase + len] + repExtra;
								int t = i + len;
								if (c < cost[t])
								{
									cost[t] = c;
									edgeRep[t] = rep;
									edgeLen[t] = len;
									edgeDist[t] = (int)rd;
									UpdateReps(rep, rd, cr0, cr1, cr2, out q0[t], out q1[t], out q2[t]);
								}
							}
						}

						// 3) 显式偏移：把缓存展开成「每个长度可用的最小偏移」
						int from = recStart[i];
						int to = recStart[i + 1];
						int covered = 1;
						Array.Clear(smallestOffset);
						for (int r = from; r < to; r++)
						{
							int n = Math.Min(recLen[r], maxLen);
							for (int len = covered + 1; len <= n; len++)
							{
								smallestOffset[len] = recDist[r];
							}

							if (n > covered)
							{
								covered = n;
							}

							if (covered >= maxLen)
							{
								break;
							}
						}

						// smallestOffset 是阶梯函数：同一段距离相同，槽位、额外位也就不变，
						// 只在距离换段时重算一次
						int lastDist = -1;
						int expBase = 0;
						int expExtra = 0;
						for (int len = 2; len <= covered; len++)
						{
							int d = smallestOffset[len];
							if (d != lastDist)
							{
								lastDist = d;
								int slot = SlotTable[d];
								expBase = slot * (MaxMatch + 1);
								expExtra = ExtraPart(alignLen, slot, d);
							}

							int c = cost[i] + _lenPart![expBase + len] + expExtra;
							int t = i + len;
							if (c < cost[t])
							{
								cost[t] = c;
								edgeRep[t] = 3;
								edgeLen[t] = len;
								edgeDist[t] = d;
								UpdateReps(3, (uint)d, cr0, cr1, cr2, out q0[t], out q1[t], out q2[t]);
							}
						}
					}

					// 回溯出本轮的词法单元
					local.Clear();
					int node = size;
					while (node > 0)
					{
						int len = edgeLen[node];
						if (len <= 0)
						{
							break;
						}

						local.Add(edgeRep[node] < 0
							? Token.NewLiteral(_data[blockStart + node - 1])
							: Token.NewMatch(len, edgeDist[node]));
						node -= len;
					}

					local.Reverse();

					// 收敛即停：本轮的词法单元若与上一轮逐项相同，码长估计就没变，
					// 再跑下去结果也不会变（DP 是确定性的）。实测这个词法在 3 轮左右就稳定，
					// 原来固定跑满 6 轮等于白烧一半时间。
					if (pass > 0 && local.Count == prevLocal.Count)
					{
						bool same = true;
						for (int t = 0; t < local.Count; t++)
						{
							if (local[t].Literal != prevLocal[t].Literal || local[t].Length != prevLocal[t].Length
								|| local[t].Distance != prevLocal[t].Distance)
							{
								same = false;
								break;
							}
						}

						if (same)
						{
							converged = true;
							break;
						}
					}

					prevLocal.Clear();
					prevLocal.AddRange(local);

					if (pass + 1 < passes)
					{
						// 用「先前窗口的定稿 + 本窗口本轮结果」重建码长估计，样本比单个窗口大得多。
						//
						// 必须复用同一个 estimate：它处在「窗口 × 轮次」双层循环里，要被调用上百次，
						// 而 Token 是 40 字节的结构体、容量还随已产出单元数一路增长 ——
						// 每次新建等于把前面所有词法单元整份复制一遍，单文件能烧掉几个 GB。
						// Clear() 不释放容量，AddRange 沿用同一块底层数组，只有第一次真正分配。
						estimate.Clear();
						estimate.AddRange(tokens);
						estimate.AddRange(local);
						AssignSlots(estimate);
						BuildEstimate(estimate, mainLen, lenLen, alignLen);
					}
				}

				tokens.AddRange(local);
				r0 = q0[size];
				r1 = q1[size];
				r2 = q2[size];
				blockStart = blockEnd;
			}

				return tokens;
			}
			finally
			{
				Pool.ReturnBig(recStart);
				Pool.ReturnBig(recLen);
				Pool.ReturnBig(recDist);
				Pool.ReturnBig(cost);
				Pool.ReturnBig(edgeRep);
				Pool.ReturnBig(edgeLen);
				Pool.ReturnBig(edgeDist);
				Pool.ReturnBig(smallestOffset);
				Pool.Return(q0);
				Pool.Return(q1);
				Pool.Return(q2);
			}
		}

		/// <summary>绝对位置 p 所在帧的结束位置（按载荷内相对位置切帧）。</summary>
		private int FrameEndOf(int p)
		{
			return _start + ((p - _start) / FrameSize + 1) * FrameSize;
		}

		/// <summary>位置 at 处、距离 dist 的匹配长度，上限 maxLen。</summary>
		private int MatchLenAt(int at, int dist, int maxLen)
		{
			if (dist <= 0 || maxLen < 2 || at - dist < _start)
			{
				return 0;
			}

			int n = 0;
			while (n < maxLen && _data[at + n] == _data[at - dist + n])
			{
				n++;
			}

			return n;
		}

		/// <summary>字面量的编码代价（位）。第一遍没用到的符号按上限估。</summary>
		private static int LiteralCost(byte[] mainLen, int value)
		{
			int v = mainLen[value];
			return v > 0 ? v : MainBits;
		}

		/// <summary>匹配 (rep, dist, len) 的编码代价（位）：主符号 + 长度符号 + 额外位 + aligned 符号。</summary>
		private static int MatchCost(byte[] mainLen, byte[] lenLen, byte[] alignLen, int rep, int dist, int len)
		{
			int slot = rep < 3 ? rep : SlotTable[dist];
			int lenHead = len - 2;
			int mainSymbol = NumChars + (slot << 3) + (lenHead < 7 ? lenHead : 7);
			int num = SymbolCost(mainLen, mainSymbol, MainBits);
			if (lenHead >= 7)
			{
				num += SymbolCost(lenLen, lenHead - 7, LengthBits);
			}

			int num2 = ExtraBits[slot];
			if (num2 > 0)
			{
				int num3 = dist - (int)(PositionBase[slot] - 2);
				if (num2 <= 2)
				{
					num += num2;
				}
				else if (num2 == 3)
				{
					num += SymbolCost(alignLen, num3, AlignedBits);
				}
				else
				{
					num += num2 - 3 + SymbolCost(alignLen, num3 & 7, AlignedBits);
				}
			}

			return num;
		}

		private static int SymbolCost(byte[] lengths, int symbol, int fallback)
		{
			int v = lengths[symbol];
			return v > 0 ? v : fallback;
		}

		/// <summary>按解码器规则更新 R0/R1/R2。</summary>
		private static void UpdateReps(int rep, uint dist, uint r0, uint r1, uint r2,
			out uint n0, out uint n1, out uint n2)
		{
			n0 = dist;
			switch (rep)
			{
				case 0:
					n1 = r1;
					n2 = r2;
					break;
				case 1:
					n1 = r0;
					n2 = r2;
					break;
				case 2:
					n1 = r1;
					n2 = r0;
					break;
				default:
					n1 = r0;
					n2 = r1;
					break;
			}
		}

		/// <summary>分配槽位、额外位与 aligned 符号，同时按解码器规则推进 R0/R1/R2。</summary>
		private static void AssignSlots(List<Token> tokens)
		{
			AssignSlots(tokens, 1u, 1u, 1u);
		}

		/// <summary>同上，但指定 R0/R1/R2 的初值（窗口内重建符号频次时需要）。</summary>
		private static void AssignSlots(List<Token> tokens, uint r0, uint r1, uint r2)
		{
			uint num = r0;
			uint num2 = r1;
			uint num3 = r2;
			for (int i = 0; i < tokens.Count; i++)
			{
				Token value = tokens[i];
				if (value.IsLiteral)
				{
					continue;
				}

				uint num4 = (uint)value.Distance;
				int num5;
				if (num4 == num)
				{
					num5 = 0;
				}
				else if (num4 == num2)
				{
					num5 = 1;
				}
				else if (num4 == num3)
				{
					num5 = 2;
				}
				else
				{
					num5 = SlotTable[num4];
				}

				value.Slot = num5;
				int num6 = value.Length - 2;
				if (num6 < 7)
				{
					value.MainSymbol = NumChars + (num5 << 3) + num6;
					value.LengthSymbol = -1;
				}
				else
				{
					value.MainSymbol = NumChars + (num5 << 3) + 7;
					value.LengthSymbol = num6 - 7;
				}

				if (num5 >= 3)
				{
					int num7 = ExtraBits[num5];
					uint num8 = num4 - (PositionBase[num5] - 2);
					if (num7 == 0)
					{
						// 槽位 3：偏移恒为 1，不写任何位
					}
					else if (num7 <= 3)
					{
						// 额外位 1/2 直接写；等于 3 时低 3 位走 aligned 树
						if (num7 == 3)
						{
							value.AlignSymbol = (int)num8;
						}
						else
						{
							value.Extra = num8;
							value.ExtraBitCount = num7;
						}
					}
					else
					{
						// 高位原样写，低 3 位走 aligned 树
						value.Extra = num8 >> 3;
						value.ExtraBitCount = num7 - 3;
						value.AlignSymbol = (int)(num8 & 7);
					}
				}

				tokens[i] = value;
				switch (num5)
				{
					case 1:
						num2 = num;
						num = num4;
						break;
					case 2:
						num3 = num;
						num = num4;
						break;
					default:
						num3 = num2;
						num2 = num;
						num = num4;
						break;
					case 0:
						break;
				}
			}
		}

		/// <summary>镜像 ReadLengths：用 20 符号预树对长度表做增量编码。</summary>
		private static void WriteLengths(BitWriter bitWriter, byte[] table, byte[] target, int first, int last)
		{
			int num = last - first;
			byte[] array = new byte[num];
			for (int i = 0; i < num; i++)
			{
				int num2 = table[first + i] - target[first + i];
				num2 %= 17;
				if (num2 < 0)
				{
					num2 += 17;
				}

				array[i] = (byte)num2;
			}

			List<int> list = new List<int>(num);
			List<int> list2 = new List<int>(num);
			List<int> list3 = new List<int>(num);
			int j = 0;
			while (j < num)
			{
				// 游程符号 17/18 会把目标长度直接写成 0，因此只能用于 target 为 0 的位置；
				// 增量为 0 只代表“长度不变”，不能走游程。
				if (target[first + j] != 0)
				{
					list.Add(array[j]);
					list2.Add(0);
					list3.Add(0);
					j++;
					continue;
				}

				int num3 = 0;
				while (j + num3 < num && target[first + j + num3] == 0)
				{
					num3++;
				}

				if (num3 >= 20)
				{
					int num4 = Math.Min(num3, 51);
					list.Add(18);
					list2.Add(num4 - 20);
					list3.Add(5);
					j += num4;
				}
				else if (num3 >= 4)
				{
					list.Add(17);
					list2.Add(num3 - 4);
					list3.Add(4);
					j += num3;
				}
				else
				{
					for (int k = 0; k < num3; k++)
					{
						list.Add(array[j + k]);
						list2.Add(0);
						list3.Add(0);
					}

					j += num3;
				}
			}

			int[] array2 = new int[PreTreeSymbols];
			foreach (int item in list)
			{
				array2[item]++;
			}

			byte[] array3 = new byte[PreTreeSymbols];
			uint[] codes = new uint[PreTreeSymbols];
			BuildHuffman(array2, PreTreeSymbols, PreTreeBits, array3, codes);
			for (int l = 0; l < PreTreeSymbols; l++)
			{
				bitWriter.WriteBits(array3[l], 4);
			}

			for (int m = 0; m < list.Count; m++)
			{
				int num5 = list[m];
				bitWriter.WriteBits(codes[num5], array3[num5]);
				if (list3[m] > 0)
				{
					bitWriter.WriteBits((uint)list2[m], list3[m]);
				}
			}

			for (int n = 0; n < num; n++)
			{
				table[first + n] = target[first + n];
			}
		}
	}

	private struct Token
	{
		public int Literal;

		public int Length;

		public int Distance;

		public int MainSymbol;

		public int LengthSymbol;

		public int Slot;

		public uint Extra;

		public int ExtraBitCount;

		public int AlignSymbol;

		public bool IsLiteral => Literal >= 0;

		public int OutLength => (Literal >= 0) ? 1 : Length;

		private Token(int literal, int length, int distance)
		{
			Literal = literal;
			Length = length;
			Distance = distance;
			MainSymbol = 0;
			LengthSymbol = -1;
			Slot = 0;
			Extra = 0u;
			ExtraBitCount = 0;
			AlignSymbol = -1;
		}

		public static Token NewLiteral(int b)
		{
			return new Token(b, 0, 0);
		}

		public static Token NewMatch(int length, int distance)
		{
			return new Token(-1, length, distance);
		}
	}

	private sealed class BitWriter : IDisposable
	{
		/// <summary>
		/// 底层用池化的 byte[] 而不是 List&lt;byte&gt;：
		/// 位流长度和载荷同量级，List 的按倍增涨会一路丢弃旧数组，收尾还要 ToArray() 再拷一份，
		/// 峰值等于同时存在三份输出。这里自己管容量，并直接暴露 span 给调用方零拷贝取用。
		/// </summary>
		private byte[] _buffer = Pool.RentByte(1 << 16);

		private int _count;

		private ushort _word;

		private int _bits;

		public int ByteCount => _count;

		/// <summary>已写出的位流（只读，不含未满的当前字）。</summary>
		public ReadOnlySpan<byte> Bytes => _buffer.AsSpan(0, _count);

		public void Dispose()
		{
			Pool.Return(_buffer);
			_buffer = System.Array.Empty<byte>();
		}

		/// <summary>MSB 优先写入 count 位，满 16 位按小端落盘。</summary>
		public void WriteBits(uint value, int count)
		{
			for (int num = count - 1; num >= 0; num--)
			{
				_word |= (ushort)(((value >> num) & 1) << 15 - _bits);
				if (++_bits == 16)
				{
					FlushWord();
				}
			}
		}

		private void FlushWord()
		{
			if (_count + 2 > _buffer.Length)
			{
				Grow(_count + 2);
			}

			_buffer[_count++] = (byte)(_word & 0xFF);
			_buffer[_count++] = (byte)(_word >> 8);
			_word = 0;
			_bits = 0;
		}

		/// <summary>容量翻倍重借一块池缓冲，把已有内容搬过去。</summary>
		private void Grow(int needed)
		{
			int size = _buffer.Length * 2;
			while (size < needed)
			{
				size *= 2;
			}

			byte[] bigger = Pool.RentByte(size);
			_buffer.AsSpan(0, _count).CopyTo(bigger);
			Pool.Return(_buffer);
			_buffer = bigger;
		}

		/// <summary>镜像 BufferReader.Align()：补齐当前 16 位字。</summary>
		public void Align()
		{
			if (_bits > 0)
			{
				FlushWord();
			}
		}
	}

	private sealed class Package
	{
		public long Weight;

		public int[] Symbols;
	}

	/// <summary>构造长度受限的规范哈夫曼码，Kraft 和恰为 1，与 DecodeTable 的快速表路径一致。</summary>
	private static void BuildHuffman(int[] freq, int symbolCount, int maxBits, byte[] lengths, uint[] codes)
	{
		// 必须清空：aligned 长度数组跨块复用，残留旧码长会让 Kraft 和不等于 1
		Array.Clear(lengths, 0, symbolCount);
		List<int> list = new List<int>();
		for (int i = 0; i < symbolCount; i++)
		{
			if (freq[i] > 0)
			{
				list.Add(i);
			}
		}

		if (list.Count == 0)
		{
			lengths[0] = 1;
			if (symbolCount > 1)
			{
				lengths[1] = 1;
			}
		}
		else if (list.Count == 1)
		{
			int num = list[0];
			lengths[num] = 1;
			for (int j = 0; j < symbolCount; j++)
			{
				if (j != num)
				{
					lengths[j] = 1;
					break;
				}
			}
		}
		else
		{
			// 优先经典哈夫曼；码长溢出上限时回退到长度受限算法
			PlainHuffman(freq, list, lengths);
			int maxLen = 0;
			foreach (int item in list)
			{
				if (lengths[item] > maxLen)
				{
					maxLen = lengths[item];
				}
			}

			if (maxLen > maxBits)
			{
				foreach (int item2 in list)
				{
					lengths[item2] = 0;
				}

				LengthLimitedHuffman(freq, list, maxBits, lengths);
			}
		}

		int[] array = new int[maxBits + 1];
		for (int k = 0; k < symbolCount; k++)
		{
			if (lengths[k] > 0)
			{
				array[lengths[k]]++;
			}
		}

		uint[] array2 = new uint[maxBits + 1];
		uint num2 = 0u;
		for (int l = 1; l <= maxBits; l++)
		{
			num2 = (num2 + (uint)array[l - 1]) << 1;
			array2[l] = num2;
		}

		for (int m = 0; m < symbolCount; m++)
		{
			int num3 = lengths[m];
			if (num3 > 0)
			{
				codes[m] = array2[num3]++;
			}
			else
			{
				codes[m] = 0u;
			}
		}
	}

	/// <summary>
	/// 经典哈夫曼：每轮合并两个最小权节点。叶子按 (权重, 符号) 升序排好，
	/// 合并出的内部节点插到第一个严格更大权重的项之前（即排在同权项之后）。
	/// </summary>
	private static void PlainHuffman(int[] freq, List<int> used, byte[] lengths)
	{
		List<Package> list = new List<Package>(used.Count);
		foreach (int item in used)
		{
			list.Add(new Package
			{
				Weight = freq[item],
				Symbols = new int[1] { item }
			});
		}

		list.Sort((Package a, Package b) => (a.Weight != b.Weight)
			? a.Weight.CompareTo(b.Weight)
			: a.Symbols[0].CompareTo(b.Symbols[0]));

		while (list.Count > 1)
		{
			Package package = list[0];
			list.RemoveAt(0);
			Package package2 = list[0];
			list.RemoveAt(0);
			int[] array = package.Symbols;
			foreach (int num in array)
			{
				lengths[num]++;
			}

			int[] array2 = package2.Symbols;
			foreach (int num2 in array2)
			{
				lengths[num2]++;
			}

			int[] array3 = new int[array.Length + array2.Length];
			Array.Copy(array, 0, array3, 0, array.Length);
			Array.Copy(array2, 0, array3, array.Length, array2.Length);
			Package package3 = new Package
			{
				Weight = package.Weight + package2.Weight,
				Symbols = array3
			};
			int num3 = list.FindIndex((Package x) => x.Weight > package3.Weight);
			if (num3 < 0)
			{
				list.Add(package3);
			}
			else
			{
				list.Insert(num3, package3);
			}
		}
	}

	/// <summary>package-merge：长度受限的最优前缀码，Kraft 和恰为 1。</summary>
	private static void LengthLimitedHuffman(int[] freq, List<int> used, int maxBits, byte[] lengths)
	{
		int count = used.Count;
		List<Package> list = new List<Package>(count);
		foreach (int item in used)
		{
			list.Add(new Package
			{
				Weight = freq[item],
				Symbols = new int[1] { item }
			});
		}

		list.Sort((Package a, Package b) => (a.Weight != b.Weight)
			? a.Weight.CompareTo(b.Weight)
			: a.Symbols[0].CompareTo(b.Symbols[0]));
		List<Package> list2 = new List<Package>(list);
		for (int i = 1; i < maxBits; i++)
		{
			List<Package> list3 = new List<Package>(list2.Count / 2);
			for (int j = 0; j + 1 < list2.Count; j += 2)
			{
				Package package = list2[j];
				Package package2 = list2[j + 1];
				int[] array = new int[package.Symbols.Length + package2.Symbols.Length];
				Array.Copy(package.Symbols, 0, array, 0, package.Symbols.Length);
				Array.Copy(package2.Symbols, 0, array, package.Symbols.Length, package2.Symbols.Length);
				list3.Add(new Package
				{
					Weight = package.Weight + package2.Weight,
					Symbols = array
				});
			}

			list2 = MergeSorted(list, list3);
		}

		int num = Math.Min(2 * count - 2, list2.Count);
		for (int k = 0; k < num; k++)
		{
			int[] symbols = list2[k].Symbols;
			foreach (int num2 in symbols)
			{
				lengths[num2]++;
			}
		}
	}

	private static List<Package> MergeSorted(List<Package> a, List<Package> b)
	{
		List<Package> list = new List<Package>(a.Count + b.Count);
		int i = 0;
		int j = 0;
		while (i < a.Count || j < b.Count)
		{
			if (j >= b.Count || (i < a.Count && a[i].Weight <= b[j].Weight))
			{
				list.Add(a[i++]);
			}
			else
			{
				list.Add(b[j++]);
			}
		}

		return list;
	}
}
