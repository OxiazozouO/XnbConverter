using XnbConverter.Configurations;
using XnbConverter.Readers;
using XnbConverter.Utilities;

/// <summary>
/// 拿一个 LZX 压缩的 xnb：拆壳 -> 解压 -> 用 LzxCompressor 重压 -> 与原始载荷逐字节比对，
/// 用来验证压缩器产出的流能被 MonoGame 的解码器原样解出。
/// 用法: XnbDump lzxrt &lt;sample.xnb&gt; [--trace]
/// </summary>
public static class LzxRoundTrip
{
    public static void Run(string[] args)
    {

// 用法: LzxRoundTrip <sample.xnb>
// 流程: 拆壳 -> Lzx 解压 -> LzxCompressor 压缩 -> 逐字节比对 -> 往返自检
string path = args.Length > 0 ? args[0] : "sample.xnb";

// 用 UTF-8 输出，脚本里才能 grep 中文标签
Console.OutputEncoding = System.Text.Encoding.UTF8;

byte[] xnb = File.ReadAllBytes(path);
int unpacked = BitConverter.ToInt32(xnb, 10);
byte[] payload = xnb[14..];

Console.WriteLine($"xnb        = {xnb.Length}");
Console.WriteLine($"payload    = {payload.Length}");
Console.WriteLine($"unpacked   = {unpacked}");

// ---- 0) 解剖原始载荷的块结构 ----
if (args.Contains("--trace"))
{
    Console.WriteLine("---- 原始块结构 ----");
    _ = new TraceLog();
    Decompress(payload, unpacked);
    Logger.Instance = null;
}

// ---- 1) 解压 ----
byte[] plain = Decompress(payload, unpacked);
Console.WriteLine($"解压完成   = {plain.Length}");

// ---- 2) 压缩 ----
byte[] repacked = LzxCompressor.Compress(plain, 0, plain.Length);
Console.WriteLine($"压缩完成   = {repacked.Length}");

// ---- 3) 与原始载荷逐字节比对 ----
DumpFrames(payload, "原始帧");
DumpFrames(repacked, "重压帧");
Console.WriteLine($"长度一致   = {repacked.Length == payload.Length}");
int diff = 0;
int tokDiff = -1;
int min = Math.Min(repacked.Length, payload.Length);
for (int i = 0; i < min; i++)
{
    if (repacked[i] != payload[i])
    {
        diff++;
        if (tokDiff < 0)
        {
            tokDiff = i;
        }
    }
}

diff += Math.Abs(repacked.Length - payload.Length);
Console.WriteLine($"差异字节   = {diff} / {Math.Max(repacked.Length, payload.Length)}");
if (tokDiff >= 0)
{
    Console.WriteLine($"首个差异   = {tokDiff}");
    Console.WriteLine($"  原始: {Hex(payload, tokDiff)}");
    Console.WriteLine($"  重压: {Hex(repacked, tokDiff)}");
}
else if (repacked.Length == payload.Length)
{
    Console.WriteLine(">>> 完全一致 <<<");
}

File.WriteAllBytes("plain.bin", plain);
File.WriteAllBytes("repacked.bin", repacked);

// ---- 3.5) 比对两边的词法单元序列 ----
if (args.Contains("--tokens"))
{
    List<(bool Lit, int Len, int Dist)> origTokens = DumpTokens(payload, unpacked);
    List<(bool Lit, int Len, int Dist)> myTokens = DumpTokens(repacked, unpacked);
    Console.WriteLine($"原始 token = {origTokens.Count}, 重压 token = {myTokens.Count}");

    if (args.Contains("--reemit"))
    {
        byte[] reemitted = LzxCompressor.CompressWithTokens(plain, 0, plain.Length, origTokens);
        File.WriteAllBytes("reemitted.bin", reemitted);

        List<(int First, int Index, int Sym, int Count)> origLens = DumpLengths(payload, unpacked);
        List<(int First, int Index, int Sym, int Count)> mineLens = DumpLengths(reemitted, unpacked);
        int lm = Math.Min(origLens.Count, mineLens.Count);
        int ld = -1;
        for (int i = 0; i < lm; i++)
        {
            if (!origLens[i].Equals(mineLens[i]))
            {
                ld = i;
                break;
            }
        }

        Console.WriteLine($"长度符号: 原始 {origLens.Count} / 重编 {mineLens.Count}，首个差异 #{ld}");

        List<int> allDiff = new List<int>();
        for (int i = 0; i < lm; i++)
        {
            if (!origLens[i].Equals(mineLens[i]))
            {
                allDiff.Add(i);
            }
        }

        int[] oLen = RebuildMainLengths(origLens);
        int[] mLen = RebuildMainLengths(mineLens);
        List<int> lenDiff = new List<int>();
        for (int i = 0; i < 512; i++)
        {
            if (oLen[i] != mLen[i])
            {
                lenDiff.Add(i);
            }
        }

        long kOrig = 0L;
        long kMine = 0L;
        for (int i = 0; i < 512; i++)
        {
            if (oLen[i] > 0)
            {
                kOrig += 1L << (16 - oLen[i]);
            }

            if (mLen[i] > 0)
            {
                kMine += 1L << (16 - mLen[i]);
            }
        }

        Console.WriteLine($"主树码长差异点 = {lenDiff.Count}");
        Console.WriteLine($"  Kraft(2^16 为单位) 原始={kOrig} 重编={kMine}（完全码=65536）");
        int[] freqOf = Analyser.MainFreq(origTokens);
        foreach (int i in lenDiff.Take(24))
        {
            Console.WriteLine($"    idx {i,4}: 码长 原始={oLen[i],2} 重编={mLen[i],2}  频次={freqOf[i]}");
        }

        Console.WriteLine($"长度符号差异总数 = {allDiff.Count}");
        foreach (int i in allDiff.Take(30))
        {
            Console.WriteLine($"  #{i,6}  原始 {Lfmt(origLens[i]),-22} 重编 {Lfmt(mineLens[i])}");
        }

        if (false)
        {
            for (int i = Math.Max(0, ld - 4); i < Math.Min(Math.Max(origLens.Count, mineLens.Count), ld + 44); i++)
            {
                string a = i < origLens.Count ? Lfmt(origLens[i]) : "-";
                string b = i < mineLens.Count ? Lfmt(mineLens[i]) : "-";
                Console.WriteLine($"  #{i,6}  原始 {a,-22} 重编 {b}");
            }
        }

        bool same = reemitted.AsSpan().SequenceEqual(payload);
        Console.WriteLine($"按原始 token 重编码 = {reemitted.Length}（原始 {payload.Length}）\\n逐字节相同 = {same}");
        if (!same)
        {
            int m = Math.Min(reemitted.Length, payload.Length);
            int d = -1;
            for (int i = 0; i < m; i++)
            {
                if (reemitted[i] != payload[i])
                {
                    d = i;
                    break;
                }
            }

            if (d >= 0)
            {
                Console.WriteLine($"首个字节差异 = {d}");
                Console.WriteLine($"  原始: {Hex(payload, d)}");
                Console.WriteLine($"  重编: {Hex(reemitted, d)}");
            }
        }
    }

    int limit = Math.Min(origTokens.Count, myTokens.Count);
    int tDiff = -1;
    for (int i = 0; i < limit; i++)
    {
        if (!origTokens[i].Equals(myTokens[i]))
        {
            tDiff = i;
            break;
        }
    }

    if (tDiff < 0 && origTokens.Count == myTokens.Count)
    {
        Console.WriteLine(">>> token 序列完全一致 <<<");
    }
    else
    {
        if (tDiff < 0)
        {
            tDiff = limit;
        }

        Console.WriteLine($"首个 token 差异 = #{tDiff}（输出位置 {OutPos(origTokens, tDiff)}）");
        int from = Math.Max(0, tDiff - 6);
        int to = Math.Min(Math.Max(origTokens.Count, myTokens.Count), tDiff + 8);
        for (int i = from; i < to; i++)
        {
            string a = i < origTokens.Count ? Fmt(origTokens[i]) : "-";
            string b = i < myTokens.Count ? Fmt(myTokens[i]) : "-";
            Console.WriteLine($"  #{i,7}  原始 {a,-28} 重压 {b}");
        }
    }

    Analyser.Report(origTokens, "原始");
    Analyser.Report(myTokens, "重压");
    Analyser.Audit(plain, origTokens);
    Analyser.AuditDepth(plain, origTokens);
}

// ---- 4) 往返自检：解压(压缩(x)) == x ----
byte[] back = Decompress(repacked, unpacked);
Console.WriteLine($"往返一致   = {back.AsSpan().SequenceEqual(plain)}");

return;

static byte[] Decompress(byte[] payload, int unpacked)
{
    // 输出固定落在缓冲区 offset 14 处，与 Lzx.Decompress 的约定一致
    byte[] buf = new byte[14 + unpacked + 64];
    Array.Copy(payload, 0, buf, 14, payload.Length);
    BufferReader reader = new BufferReader(buf);
    reader.BytePosition = 14;
    Lzx.Decompress(reader, payload.Length, unpacked);
    return buf[14..(14 + unpacked)];
}

static List<(bool Lit, int Len, int Dist)> DumpTokens(byte[] payload, int unpacked)
{
    List<(bool, int, int)> list = new List<(bool, int, int)>();
    Lzx.TokenHook = (isLit, length, dist) => list.Add((isLit, length, dist));
    try
    {
        Decompress(payload, unpacked);
    }
    finally
    {
        Lzx.TokenHook = null;
    }

    return list;
}

static List<(int First, int Index, int Sym, int Count)> DumpLengths(byte[] payload, int unpacked)
{
    List<(int, int, int, int)> list = new List<(int, int, int, int)>();
    Lzx.LengthHook = (first, index, sym, count) => list.Add((first, index, sym, count));
    try
    {
        Decompress(payload, unpacked);
    }
    finally
    {
        Lzx.LengthHook = null;
    }

    return list;
}

/// <summary>从长度钩子流重建主树（符号 0..511）的码长。仅适用首块（增量基准全 0）。</summary>
static int[] RebuildMainLengths(List<(int First, int Index, int Sym, int Count)> hooks)
{
    int[] lengths = new int[512];
    int seg = 0;
    int prevFirst = 0;
    foreach ((int First, int Index, int Sym, int Count) h in hooks)
    {
        if (h.First != prevFirst)
        {
            seg++;
        }

        prevFirst = h.First;
        if (seg > 1)
        {
            break;
        }

        for (int k = 0; k < h.Count; k++)
        {
            int i = h.Index + k;
            if (i >= 512)
            {
                break;
            }

            lengths[i] = h.Sym >= 17 ? 0 : (17 - h.Sym) % 17;
        }
    }

    return lengths;
}

static string Lfmt((int First, int Index, int Sym, int Count) t)
{
    return $"first={t.First} i={t.Index} sym={t.Sym} n={t.Count}";
}

static int OutPos(List<(bool Lit, int Len, int Dist)> tokens, int index)
{
    int pos = 0;
    for (int i = 0; i < index && i < tokens.Count; i++)
    {
        pos += tokens[i].Lit ? 1 : tokens[i].Len;
    }

    return pos;
}

static string Fmt((bool Lit, int Len, int Dist) t)
{
    return t.Lit ? $"lit 0x{t.Dist:X2}" : $"match len={t.Len} dist={t.Dist}";
}

static void DumpFrames(byte[] payload, string tag)
{
    int pos = 0;
    List<string> list = new List<string>();
    while (pos + 2 <= payload.Length)
    {
        int frameSize;
        int compSize;
        if (payload[pos] == 0xFF)
        {
            if (pos + 5 > payload.Length) break;
            frameSize = (payload[pos + 1] << 8) | payload[pos + 2];
            compSize = (payload[pos + 3] << 8) | payload[pos + 4];
            pos += 5;
        }
        else
        {
            frameSize = 32768;
            compSize = (payload[pos] << 8) | payload[pos + 1];
            pos += 2;
        }

        if (frameSize == 0 || compSize == 0) break;
        list.Add($"{frameSize}->{compSize}");
        pos += compSize;
    }

    Console.WriteLine($"{tag}     = {string.Join(", ", list)}  (尾={pos}/{payload.Length})");
}

static string Hex(byte[] data, int at)
{
    int from = Math.Max(0, at - 8);
    int to = Math.Min(data.Length, at + 8);
    return string.Join(' ', data[from..to].Select(b => b.ToString("X2")));
}
    }
}


/// <summary>回放 token 流并统计编码决策，用于反推原始编码器的规则。</summary>
internal static class Analyser
{
    private static readonly byte[] ExtraBits = BuildExtraBits();

    private static readonly uint[] PosBase = BuildPosBase();

    private static byte[] BuildExtraBits()
    {
        byte[] array = new byte[52];
        int num = 0;
        for (int i = 0; i < 52; i += 2)
        {
            array[i] = (array[i + 1] = (byte)num);
            if (i != 0 && num < 17)
            {
                num++;
            }
        }

        return array;
    }

    private static uint[] BuildPosBase()
    {
        uint[] array = new uint[51];
        int num = 0;
        for (int i = 0; i < 51; i++)
        {
            array[i] = (uint)num;
            num += 1 << ExtraBits[i];
        }

        return array;
    }

    private static int SlotOf(uint d)
    {
        for (int i = 3; i < 32; i++)
        {
            uint num = PosBase[i] - 2;
            if (d >= num && d <= num + (1u << ExtraBits[i]) - 1)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 参数搜索：不同（哈希字节数 × 链深）下找到的最长匹配，与原始所选长度吻合的比例。
    /// 吻合率最高的配置，最接近原始编码器的匹配器。
    /// </summary>
    public static void AuditDepth(byte[] data, List<(bool Lit, int Len, int Dist)> tokens)
    {
        int[] depths = { 2, 4, 8, 16, 32, 64, 128, 256 };
        int maxDepth = depths[depths.Length - 1];
        int[] bestAt = new int[maxDepth + 1];

        foreach (int hashBytes in new[] { 2, 3 })
        {
            int[] head = new int[65536];
            Array.Fill(head, -1);
            int[] prev = new int[65536];
            long[] eq = new long[depths.Length];
            long[] le = new long[depths.Length];
            long total = 0;
            int pos = 0;

            int Hash(int p)
            {
                if (hashBytes == 2)
                {
                    return ((data[p] << 8) | data[p + 1]) & 65535;
                }

                uint v = (uint)((data[p] << 16) | (data[p + 1] << 8) | data[p + 2]);
                return (int)(v * 2654435761u >> 16) & 65535;
            }

            void InsertAll(int from, int count)
            {
                for (int k = 0; k < count; k++)
                {
                    int q = from + k;
                    if (q + hashBytes > data.Length)
                    {
                        return;
                    }

                    int h = Hash(q);
                    prev[q & 65535] = head[h];
                    head[h] = q;
                }
            }

            foreach ((bool Item1, int Item2, int Item3) item in tokens)
            {
                if (item.Item1)
                {
                    InsertAll(pos, 1);
                    pos++;
                    continue;
                }

                int frameEnd = Math.Min(((pos / 32768) + 1) * 32768, data.Length);
                int maxLen = Math.Min(257, frameEnd - pos);
                Array.Clear(bestAt);
                if (maxLen >= 2 && pos + hashBytes <= data.Length)
                {
                    int cand = head[Hash(pos)];
                    int limit = Math.Max(0, pos - 65533);
                    int best = 0;
                    int step = 0;
                    while (cand >= limit && step < maxDepth)
                    {
                        if (maxLen > best && data[cand + best] == data[pos + best])
                        {
                            int n = 0;
                            while (n < maxLen && data[cand + n] == data[pos + n])
                            {
                                n++;
                            }

                            if (n > best)
                            {
                                best = n;
                            }
                        }

                        step++;
                        bestAt[step] = best;
                        int nx = prev[cand & 65535];
                        if (nx >= cand)
                        {
                            break;
                        }

                        cand = nx;
                    }

                    for (int s = step + 1; s <= maxDepth; s++)
                    {
                        bestAt[s] = best;
                    }
                }

                total++;
                for (int i = 0; i < depths.Length; i++)
                {
                    if (bestAt[depths[i]] == item.Item2)
                    {
                        eq[i]++;
                    }

                    if (bestAt[depths[i]] <= item.Item2)
                    {
                        le[i]++;
                    }
                }

                InsertAll(pos, item.Item2);
                pos += item.Item2;
            }

            Console.WriteLine($"-- 哈希 {hashBytes} 字节 --");
            for (int i = 0; i < depths.Length; i++)
            {
                Console.WriteLine($"   链深 {depths[i],3}: 长度完全吻合 {100.0 * eq[i] / total,5:F1}%   未找到更长 {100.0 * le[i] / total,5:F1}%");
            }
        }
    }

    /// <summary>
    /// 审计 token 流的匹配决策：原始选的匹配是不是当时最长的？
    /// 放弃更长的匹配去用 R0/R1/R2 的频率有多高？
    /// </summary>
    public static void Audit(byte[] data, List<(bool Lit, int Len, int Dist)> tokens)
    {
        const int WindowSize = 65536;
        const int MaxDist = 65533;
        const int MaxMatch = 257;
        const int FrameSize = 32768;
        int[] head = new int[65536];
        Array.Fill(head, -1);
        int[] prev = new int[WindowSize];
        uint r0 = 1u;
        uint r1 = 1u;
        uint r2 = 1u;
        int pos = 0;
        int matches = 0;
        int isMax = 0;
        int lessMax = 0;
        int repChosen = 0;
        int repIsMax = 0;
        int repLessMax = 0;
        long deficit = 0L;
        int longerAvailable = 0;
        int chosenNearer = 0;
        int chosenFarther = 0;
        SortedDictionary<int, int> deficitHist = new SortedDictionary<int, int>();
        SortedDictionary<int, int> longerSlotHist = new SortedDictionary<int, int>();

        int MatchLen(int at, int dist, int maxLen)
        {
            if (dist <= 0 || dist > at || maxLen <= 0)
            {
                return 0;
            }

            int n = 0;
            while (n < maxLen && data[at + n] == data[at - dist + n])
            {
                n++;
            }

            return n;
        }

        foreach ((bool Item1, int Item2, int Item3) item in tokens)
        {
            if (item.Item1)
            {
                if (pos + 2 <= data.Length)
                {
                    int h = ((data[pos] << 8) | data[pos + 1]) & 65535;
                    prev[pos & 65535] = head[h];
                    head[h] = pos;
                }

                pos++;
                continue;
            }

            int frameEnd = Math.Min(((pos / FrameSize) + 1) * FrameSize, data.Length);
            int maxLen = Math.Min(MaxMatch, frameEnd - pos);
            uint d = (uint)item.Item3;
            int slot = d == r0 ? 0 : d == r1 ? 1 : d == r2 ? 2 : SlotOf(d);
            matches++;
            if (slot <= 2)
            {
                repChosen++;
            }

            int lr0 = MatchLen(pos, (int)r0, maxLen);
            int lr1 = MatchLen(pos, (int)r1, maxLen);
            int lr2 = MatchLen(pos, (int)r2, maxLen);
            int repBest = Math.Max(lr0, Math.Max(lr1, lr2));

            // 全局最长（记录其距离）
            int bestLen = 0;
            int bestDist = 0;
            if (maxLen >= 2 && pos + 2 <= data.Length)
            {
                int h = ((data[pos] << 8) | data[pos + 1]) & 65535;
                int cand = head[h];
                int guard = 512;
                int limit = Math.Max(0, pos - MaxDist);
                while (cand >= limit && guard-- > 0)
                {
                    if (data[cand + bestLen] == data[pos + bestLen])
                    {
                        int n = 0;
                        while (n < maxLen && data[cand + n] == data[pos + n])
                        {
                            n++;
                        }

                        if (n > bestLen)
                        {
                            bestLen = n;
                            bestDist = pos - cand;
                        }
                    }

                    int nx = prev[cand & 65535];
                    if (nx >= cand)
                    {
                        break;
                    }

                    cand = nx;
                }
            }

            if (lr0 > bestLen)
            {
                bestLen = lr0;
                bestDist = (int)r0;
            }

            if (lr1 > bestLen)
            {
                bestLen = lr1;
                bestDist = (int)r1;
            }

            if (lr2 > bestLen)
            {
                bestLen = lr2;
                bestDist = (int)r2;
            }

            if (item.Item2 >= bestLen)
            {
                isMax++;
                if (slot <= 2)
                {
                    repIsMax++;
                }
            }
            else
            {
                lessMax++;
                deficit += bestLen - item.Item2;
                int dd = bestLen - item.Item2;
                deficitHist[dd] = deficitHist.GetValueOrDefault(dd) + 1;
                longerSlotHist[slot] = longerSlotHist.GetValueOrDefault(slot) + 1;
                if (slot <= 2)
                {
                    repLessMax++;
                }

                if (repBest >= 2 && slot > 2)
                {
                    longerAvailable++;
                }

                if (item.Item3 < bestDist)
                {
                    chosenNearer++;
                }
                else if (item.Item3 > bestDist)
                {
                    chosenFarther++;
                }
            }

            if (slot == 1)
            {
                r1 = r0;
                r0 = d;
            }
            else if (slot == 2)
            {
                r2 = r0;
                r0 = d;
            }
            else if (slot >= 3)
            {
                r2 = r1;
                r1 = r0;
                r0 = d;
            }

            for (int k = 0; k < item.Item2; k++)
            {
                int q = pos + k;
                if (q + 2 <= data.Length)
                {
                    int h = ((data[q] << 8) | data[q + 1]) & 65535;
                    prev[q & 65535] = head[h];
                    head[h] = q;
                }
            }

            pos += item.Item2;
        }

        Console.WriteLine("---- 匹配决策审计 ----");
        Console.WriteLine($"  匹配总数={matches}");
        Console.WriteLine($"  所选即最长={isMax} ({100.0 * isMax / matches:F1}%)  短于可选最长={lessMax} ({100.0 * lessMax / matches:F1}%)");
        Console.WriteLine($"  平均放弃长度={((lessMax == 0) ? 0.0 : (double)deficit / lessMax):F2}");
        Console.WriteLine($"  其中选了复用槽位(0/1/2)={repLessMax}");
        Console.WriteLine($"  选了非复用槽位但 R0/R1/R2 可用(>=2)={longerAvailable}");
        Console.WriteLine($"  用了复用槽位且确实最长={repIsMax}/{repChosen}");
        Console.WriteLine($"  放弃更长的匹配时：选了更近的={chosenNearer}  选了更远的={chosenFarther}");
        Console.WriteLine("  放弃量分布: " + string.Join(", ", deficitHist.Take(10).Select(p => $"{p.Key}:{p.Value}")));
    }

    /// <summary>按解码器规则回放 token 流，统计主树 512 个符号的频次。</summary>
    public static int[] MainFreq(List<(bool Lit, int Len, int Dist)> tokens)
    {
        int[] freq = new int[512];
        uint num = 1u;
        uint num2 = 1u;
        uint num3 = 1u;
        foreach ((bool Item1, int Item2, int Item3) item in tokens)
        {
            if (item.Item1)
            {
                freq[item.Item3]++;
                continue;
            }

            uint num4 = (uint)item.Item3;
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
                num5 = SlotOf(num4);
            }

            int num6 = item.Item2 - 2;
            freq[(num6 < 7) ? (256 + (num5 << 3) + num6) : (256 + (num5 << 3) + 7)]++;
            if (num5 == 1)
            {
                num2 = num;
                num = num4;
            }
            else if (num5 == 2)
            {
                num3 = num;
                num = num4;
            }
            else if (num5 >= 3)
            {
                num3 = num2;
                num2 = num;
                num = num4;
            }
        }

        return freq;
    }

    public static void Report(List<(bool Lit, int Len, int Dist)> tokens, string tag)
    {
        uint num = 1u;
        uint num2 = 1u;
        uint num3 = 1u;
        int num4 = 0;
        int num5 = 0;
        int num6 = 0;
        int num7 = 0;
        int num8 = 0;
        int num9 = 0;
        SortedDictionary<int, int> sortedDictionary = new SortedDictionary<int, int>();
        SortedDictionary<int, int> sortedDictionary2 = new SortedDictionary<int, int>();
        foreach ((bool Item1, int Item2, int Item3) item in tokens)
        {
            if (item.Item1)
            {
                num4++;
                num6++;
                continue;
            }

            num5++;
            num6 += item.Item2;
            sortedDictionary[item.Item2] = sortedDictionary.GetValueOrDefault(item.Item2) + 1;
            uint num10 = (uint)item.Item3;
            int num11;
            if (num10 == num)
            {
                num11 = 0;
            }
            else if (num10 == num2)
            {
                num11 = 1;
            }
            else if (num10 == num3)
            {
                num11 = 2;
            }
            else
            {
                num11 = SlotOf(num10);
            }

            sortedDictionary2[num11] = sortedDictionary2.GetValueOrDefault(num11) + 1;
            if (num11 <= 2)
            {
                num7++;
            }

            if (item.Item2 == 2)
            {
                num8++;
                if (num11 <= 2)
                {
                    num9++;
                }
            }

            if (num11 == 1)
            {
                num2 = num;
                num = num10;
            }
            else if (num11 == 2)
            {
                num3 = num;
                num = num10;
            }
            else if (num11 >= 3)
            {
                num3 = num2;
                num2 = num;
                num = num10;
            }
        }

        Console.WriteLine($"---- {tag} ----");
        Console.WriteLine($"  token={tokens.Count} 字面量={num4} 匹配={num5} 输出={num6}");
        Console.WriteLine($"  平均匹配长度={(num5 == 0 ? 0.0 : (double)(num6 - num4) / num5):F2}");
        Console.WriteLine(
            $"  复用槽位0/1/2={num7}/{num5} = {(num5 == 0 ? 0.0 : 100.0 * num7 / num5):F1}%");
        Console.WriteLine($"  长度2匹配={num8}（复用槽位 {num9} / 新距离 {num8 - num9}）");
        Console.WriteLine("  长度分布: " + string.Join(", ", sortedDictionary.Take(14).Select(p => $"{p.Key}:{p.Value}")));
        Console.WriteLine("  槽位分布: " + string.Join(", ", sortedDictionary2.Select(p => $"{p.Key}:{p.Value}")));
    }
}

/// <summary>把解码器的 Logger.Debug 输出打到控制台，用于解剖块结构。</summary>
internal sealed class TraceLog : Log
{
    public override void Message(string message = "", params object?[] format)
    {
    }

    public override void Info(string message = "", params object?[] format)
    {
    }

    public override void Debug(string message = "", params object?[] format)
    {
        Console.WriteLine(format.Length == 0 ? message : string.Format(message, format));
    }

    public override void Warn(string message = "", params object?[] format)
    {
    }

    public override void Error(string message = "", params object?[] format)
    {
    }

    protected override void Set<T>(bool isPrint, bool isSave, ConsoleColor color, string head, string message,
        params object?[] format)
    {
    }

    public override void Save()
    {
    }
}

