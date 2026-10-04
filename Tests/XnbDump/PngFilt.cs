using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using XnbConverter.Utilities.Png;

/// <summary>
/// PNG 编码方案对比：同一批真实贴图，比较
/// <list type="bullet">
///   <item>RGBA（颜色类型 6，4 字节/像素）；</item>
///   <item>灰度+Alpha（颜色类型 4，2 字节/像素）；</item>
///   <item>调色板（颜色类型 3，1 字节/像素 + 调色板）。</item>
/// </list>
/// 字体图集这类「灰阶 + 颜色数很少」的图，后两种能少喂给 DEFLATE 一半到四分之三的数据，
/// 时间和体积应当同时下降。每张图都会**读回来验证逐字节无损**。
/// </summary>
public static class PngFilt
{
    private const int Bpp = 4;

    /// <summary>数据画像：这些贴图到底能不能用更省的颜色类型。</summary>
    public static void Stat(string dir, int top)
    {
        List<(string Path, long Len)> files = Files(dir, top);
        foreach ((string path, long _) in files)
        {
            byte[] rgba = Png.Read(path, out int w, out int h);
            bool opaque = true;
            bool grey = true;
            HashSet<uint> colors = new();
            for (int i = 0; i < rgba.Length; i += 4)
            {
                if (rgba[i + 3] != 255)
                {
                    opaque = false;
                }

                if (rgba[i] != rgba[i + 1] || rgba[i + 1] != rgba[i + 2])
                {
                    grey = false;
                }

                if (colors.Count < 300)
                {
                    colors.Add((uint)(rgba[i] << 24 | rgba[i + 1] << 16 | rgba[i + 2] << 8 | rgba[i + 3]));
                }
            }

            string tag = opaque ? "全不透明→可 RGB" : (grey ? "全灰阶→可灰度/调色板" : "需 RGBA");
            Console.WriteLine($"  {Path.GetFileName(path),-28} {w,5}x{h,-5} 颜色数{(colors.Count >= 300 ? "300+" : colors.Count.ToString()),-5} {tag}");
        }
    }

    /// <summary>两个目录里同名 PNG 解码成 RGBA 后逐字节比对。</summary>
    public static void Cmp(string dirA, string dirB)
    {
        Dictionary<string, string> a = Directory.EnumerateFiles(dirA, "*.png", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(dirA, p), p => p);
        int total = 0, same = 0, diff = 0, missing = 0;
        foreach (KeyValuePair<string, string> kv in a.OrderBy(k => k.Key))
        {
            string bp = Path.Combine(dirB, kv.Key);
            if (!File.Exists(bp))
            {
                missing++;
                continue;
            }

            total++;
            byte[] ra = Png.Read(kv.Value, out int wa, out int ha);
            byte[] rb = Png.Read(bp, out int wb, out int hb);
            if (wa == wb && ha == hb && ra.AsSpan().SequenceEqual(rb))
            {
                same++;
            }
            else
            {
                diff++;
                if (diff <= 5)
                {
                    Console.WriteLine($"    不同: {kv.Key} {wa}x{ha} vs {wb}x{hb}");
                }
            }
        }

        Console.WriteLine($"  解码后相同 {same}/{total}   不同 {diff}   缺失 {missing}");
    }

    /// <summary>只比过滤策略（RGBA 输出），用于多色贴图。</summary>
    public static void Filters(string dir, int top)
    {
        List<(string Path, long Len)> files = Files(dir, top);
        List<(byte[] Rgba, int W, int H)> images = new();
        long pixels = 0;
        foreach ((string path, long _) in files)
        {
            byte[] rgba = Png.Read(path, out int w, out int h);
            images.Add((rgba, w, h));
            pixels += (long)w * h;
        }

        Console.WriteLine($"{images.Count} 张图，{pixels * 4 / 1048576.0:F1} MB 像素");
        string[] names = { "None", "Sub", "Up", "Average", "Paeth", "Adaptive" };
        long[] size = new long[names.Length];
        double[] sec = new double[names.Length];

        foreach ((byte[] rgba, int w, int h) in images.Take(1))
        {
            for (int f = 0; f < names.Length; f++)
            {
                _ = EncodeWithFilter(rgba, w, h, f);
            }
        }

        for (int f = 0; f < names.Length; f++)
        {
            foreach ((byte[] rgba, int w, int h) in images)
            {
                byte[] png = EncodeWithFilter(rgba, w, h, f);
                size[f] += png.Length;
            }

            double t0 = Stopwatch.GetTimestamp();
            foreach ((byte[] rgba, int w, int h) in images)
            {
                _ = EncodeWithFilter(rgba, w, h, f);
            }

            sec[f] = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
        }

        for (int f = 0; f < names.Length; f++)
        {
            Console.WriteLine($"  {names[f],-9} {size[f] / 1048576.0,7:F2} MB  {sec[f],7:F3} s");
        }
    }

    private static byte[] EncodeWithFilter(byte[] rgba, int w, int h, int filter)
    {
        int stride = w * 4;
        byte[] raw = new byte[h * (1 + stride)];
        byte[] prev = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            ReadOnlySpan<byte> row = rgba.AsSpan(y * stride, stride);
            int dst = y * (1 + stride);
            int f = filter == 5 ? ChooseFilter(row, prev) : filter;
            raw[dst] = (byte)f;
            ApplyFilter(f, row, prev, raw.AsSpan(dst + 1, stride));
            row.CopyTo(prev);
        }

        using MemoryStream ms = new MemoryStream(1 << 16);
        using (ZLibStream z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw);
        }

        return BuildPng(ms.GetBuffer().AsSpan(0, (int)ms.Length), w, h, 6, null, null, 8);
    }

    public static void Run(string dir, int top)
    {
        List<(string Path, long Len)> files = Files(dir, top);
        List<(byte[] Rgba, int W, int H)> images = new();
        long pixels = 0;
        foreach ((string path, long _) in files)
        {
            byte[] rgba = Png.Read(path, out int w, out int h);
            images.Add((rgba, w, h));
            pixels += (long)w * h;
        }

        Console.WriteLine($"{images.Count} 张图，合计 {pixels * 4 / 1048576.0:F1} MB 像素");

        (string Name, Func<byte[], int, int, byte[]> Enc)[] schemes =
        {
            ("RGBA(6)", (r, w, h) => EncodeRgba(r, w, h)),
            ("灰度A(4)", (r, w, h) => EncodeGreyAlpha(r, w, h)),
            ("调色板(3)", (r, w, h) => EncodePalette(r, w, h)),
        };

        // 预热
        foreach ((byte[] r, int w, int h) in images.Take(2))
        {
            foreach ((string _, Func<byte[], int, int, byte[]> e) in schemes)
            {
                _ = e(r, w, h);
            }
        }

        string tmp = Path.Combine(Path.GetTempPath(), "pngfilt_check.png");
        foreach ((string name, Func<byte[], int, int, byte[]> enc) in schemes)
        {
            long size = 0;
            double sec = 0;
            int lossless = 0;
            int failed = 0;
            foreach ((byte[] rgba, int w, int h) in images)
            {
                long t0 = Stopwatch.GetTimestamp();
                byte[] png = enc(rgba, w, h);
                sec += (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
                size += png.Length;

                // 读回来验证无损
                File.WriteAllBytes(tmp, png);
                try
                {
                    byte[] back = Png.Read(tmp, out int w2, out int h2);
                    if (w2 == w && h2 == h && back.AsSpan().SequenceEqual(rgba))
                    {
                        lossless++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch
                {
                    failed++;
                }
            }

            Console.WriteLine($"  {name,-11} 体积 {size / 1048576.0,7:F2} MB   编码 {sec,7:F3} s   " +
                              $"无损 {lossless}/{images.Count}   失败 {failed}");
            File.Delete(tmp);
        }
    }


    /// <summary>逐行自适应：选「该行差分后绝对值之和」最小的过滤类型。</summary>
    private static int ChooseFilter(ReadOnlySpan<byte> row, ReadOnlySpan<byte> prev)
    {
        int best = 0;
        long bestScore = long.MaxValue;
        Span<byte> tmp = new byte[row.Length];
        for (int f = 0; f <= 4; f++)
        {
            ApplyFilter(f, row, prev, tmp);
            long score = 0;
            foreach (byte b in tmp)
            {
                score += b < 128 ? b : 256 - b;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = f;
            }
        }

        return best;
    }

    private static void ApplyFilter(int f, ReadOnlySpan<byte> row, ReadOnlySpan<byte> prev, Span<byte> dst)
    {
        int n = row.Length;
        switch (f)
        {
            case 0:
                row.CopyTo(dst);
                break;
            case 1:
                for (int i = 0; i < n; i++)
                {
                    dst[i] = (byte)(row[i] - (i >= Bpp ? row[i - Bpp] : 0));
                }

                break;
            case 2:
                for (int i = 0; i < n; i++)
                {
                    dst[i] = (byte)(row[i] - prev[i]);
                }

                break;
            case 3:
                for (int i = 0; i < n; i++)
                {
                    int a = i >= Bpp ? row[i - Bpp] : 0;
                    dst[i] = (byte)(row[i] - ((a + prev[i]) >> 1));
                }

                break;
            default:
                for (int i = 0; i < n; i++)
                {
                    int a = i >= Bpp ? row[i - Bpp] : 0;
                    int c = i >= Bpp ? prev[i - Bpp] : 0;
                    dst[i] = (byte)(row[i] - Paeth(a, prev[i], c));
                }

                break;
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }


    /// <summary>deflate 的喂法与档位对比。</summary>
    public static void Levels(string dir, int top)
    {
        List<(string Path, long Len)> files = Files(dir, top);
        List<(byte[] Rgba, int W, int H)> images = new();
        long pixels = 0;
        foreach ((string path, long _) in files)
        {
            byte[] rgba = Png.Read(path, out int w, out int h);
            images.Add((rgba, w, h));
            pixels += (long)w * h;
        }

        Console.WriteLine($"{images.Count} 张图，{pixels * 4 / 1048576.0:F1} MB 像素");
        (string Name, CompressionLevel Level, bool PerRow)[] modes =
        {
            ("Optimal 整块", CompressionLevel.Optimal, false),
            ("Optimal 按行", CompressionLevel.Optimal, true),
            ("Fastest 整块", CompressionLevel.Fastest, false),
            ("Fastest 按行", CompressionLevel.Fastest, true),
            ("Smallest 整块", CompressionLevel.SmallestSize, false),
        };

        foreach ((byte[] r, int w, int h) in images.Take(1))
        {
            foreach ((string _, CompressionLevel lv, bool pr) in modes)
            {
                _ = DeflateTo(r, w, h, lv, pr);
            }
        }

        foreach ((string name, CompressionLevel lv, bool pr) in modes)
        {
            long size = 0;
            long t0 = Stopwatch.GetTimestamp();
            foreach ((byte[] r, int w, int h) in images)
            {
                size += DeflateTo(r, w, h, lv, pr);
            }

            double sec = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
            Console.WriteLine($"  {name,-14} {size / 1048576.0,7:F2} MB  {sec,7:F3} s");
        }
    }

    /// <summary>把整图的过滤后扫描线喂给 deflate，返回压缩后字节数。</summary>
    private static int DeflateTo(byte[] rgba, int w, int h, CompressionLevel level, bool perRow)
    {
        int stride = w * 4;
        using MemoryStream ms = new MemoryStream(1 << 16);
        using (ZLibStream z = new ZLibStream(ms, level, leaveOpen: true))
        {
            if (perRow)
            {
                Span<byte> filterNone = stackalloc byte[1];
                for (int y = 0; y < h; y++)
                {
                    z.Write(filterNone);
                    z.Write(rgba.AsSpan(y * stride, stride));
                }
            }
            else
            {
                byte[] raw = new byte[h * (1 + stride)];
                for (int y = 0; y < h; y++)
                {
                    raw[y * (1 + stride)] = 0;
                    Array.Copy(rgba, y * stride, raw, y * (1 + stride) + 1, stride);
                }

                z.Write(raw);
            }
        }

        return (int)ms.Length;
    }


    /// <summary>全语料画像：按「能用的最省颜色类型」聚合像素量，估算收益上限。</summary>
    public static void Profile(string dir)
    {
        var files = Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories).OrderBy(p => p).ToList();
        var byKind = new Dictionary<string, long[]>();
        foreach (string f in files)
        {
            byte[] rgba = Png.Read(f, out int w, out int h);
            bool opaque = true, grey = true;
            var colors = new HashSet<uint>();
            bool many = false;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                if (rgba[i + 3] != 255) opaque = false;
                if (rgba[i] != rgba[i + 1] || rgba[i + 1] != rgba[i + 2]) grey = false;
                if (!many)
                {
                    colors.Add((uint)(rgba[i] << 24 | rgba[i + 1] << 16 | rgba[i + 2] << 8 | rgba[i + 3]));
                    if (colors.Count > 256) many = true;
                }
            }

            string kind = !many
                ? (opaque ? "调色板/RGB" : "调色板/灰度A")
                : (opaque ? (grey ? "灰度-不透明(可type0, 4x省)" : "RGB不透明(可type2, 1.33x省)")
                          : (grey ? "灰度+Alpha(可type4, 2x省)" : "必须RGBA"));
            long px = (long)w * h;
            if (!byKind.TryGetValue(kind, out long[]? acc))
            {
                byKind[kind] = acc = new long[3];
            }

            acc[0]++;
            acc[1] += px;
            acc[2] += new FileInfo(f).Length;
        }

        long totalPx = byKind.Values.Sum(v => v[1]);
        Console.WriteLine($"  {files.Count} 个 PNG，合计 {totalPx * 4 / 1048576.0:F0} MB 像素");
        foreach (var kv in byKind.OrderByDescending(k => k.Value[1]))
        {
            long[] v = kv.Value;
            Console.WriteLine($"  {kv.Key,-32} {v[0],5} 个  {v[1] * 4 / 1048576.0,8:F0} MB 像素  " +
                              $"({100.0 * v[1] / Math.Max(totalPx, 1),5:F1}%)  现输出 {v[2] / 1048576.0:F1} MB");
        }
    }


    /// <summary>统计「二值 Alpha + 透明像素 RGB 统一」的贴图占比（可用 RGB+tRNS 无损表达）。</summary>
    public static void Trns(string dir)
    {
        var files = Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories).OrderBy(p => p).ToList();
        int hit = 0, miss = 0, gain = 0;
        long hitPx = 0, missPx = 0, hitBytes = 0, missBytes = 0, gainPx = 0, gainBytes = 0;
        foreach (string f in files)
        {
            byte[] rgba = Png.Read(f, out int w, out int h);
            bool ok = true;
            int transRgb = -2;
            var distinct = new HashSet<uint>();
            bool manyColors = false;
            for (int i = 0; i < rgba.Length && ok; i += 4)
            {
                int a = rgba[i + 3];
                int rgb = rgba[i] << 16 | rgba[i + 1] << 8 | rgba[i + 2];
                if (!manyColors)
                {
                    distinct.Add((uint)(rgb << 8 | a));
                    if (distinct.Count > 256) manyColors = true;
                }
                if (a == 0)
                {
                    if (transRgb == -2) transRgb = rgb;
                    else if (rgb != transRgb) ok = false;
                }
                else if (a == 255)
                {
                    if (rgb == transRgb) ok = false;
                }
                else
                {
                    ok = false;   // 有半透明像素，tRNS 表达不了
                }
            }

            if (transRgb == -2)
            {
                ok = false;       // 全不透明，本就走不到这条分支
            }

            long px = (long)w * h;
            long sz = new FileInfo(f).Length;
            if (ok)
            {
                hit++; hitPx += px; hitBytes += sz;
                if (manyColors)
                {
                    gain++; gainPx += px; gainBytes += sz;
                }
            }
            else
            {
                miss++; missPx += px; missBytes += sz;
            }
        }

        Console.WriteLine($"  适用 RGB+tRNS: {hit,5} 个  {hitPx * 4 / 1048576.0,8:F0} MB 像素  现输出 {hitBytes / 1048576.0:F1} MB");
        Console.WriteLine($"  不适用        : {miss,5} 个  {missPx * 4 / 1048576.0,8:F0} MB 像素  现输出 {missBytes / 1048576.0:F1} MB");
        // 其中「已经能用调色板」的那部分，RGB+tRNS 是退步，不算收益
        Console.WriteLine($"  ★ 真正增量（适用 ∧ 颜色数>256）: {gain,5} 个  {gainPx * 4 / 1048576.0,8:F0} MB 像素  现输出 {gainBytes / 1048576.0:F1} MB");
    }


    /// <summary>量 zlib 包装（头 + adler32）本身的开销：ZLibStream vs 裸 DeflateStream。</summary>
    public static void Zlib(string dir, int top)
    {
        List<(string Path, long Len)> files = Files(dir, top);
        List<(byte[] Rgba, int W, int H)> images = new();
        long pixels = 0;
        foreach ((string path, long _) in files)
        {
            byte[] rgba = Png.Read(path, out int w, out int h);
            images.Add((rgba, w, h));
            pixels += (long)w * h;
        }

        Console.WriteLine($"{images.Count} 张图，{pixels * 4 / 1048576.0:F1} MB 像素");

        foreach (bool zlib in new[] { true, false })
        {
            foreach ((byte[] r, int w, int h) in images.Take(1))
            {
                _ = Raw(r, w, h, zlib);
            }

            long size = 0;
            long t0 = Stopwatch.GetTimestamp();
            foreach ((byte[] r, int w, int h) in images)
            {
                size += Raw(r, w, h, zlib);
            }

            double sec = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
            Console.WriteLine($"  {(zlib ? "ZLibStream  " : "裸 Deflate  ")} 输出 {size / 1048576.0,7:F2} MB  耗时 {sec,7:F3} s");
        }
    }

    private static int Raw(byte[] rgba, int w, int h, bool zlib)
    {
        int stride = w * 4;
        using MemoryStream ms = new MemoryStream(1 << 16);
        Stream s = zlib ? new ZLibStream(ms, CompressionLevel.Optimal, true)
                        : new DeflateStream(ms, CompressionLevel.Optimal, true);
        using (s)
        {
            Span<byte> filterNone = stackalloc byte[1];
            for (int y = 0; y < h; y++)
            {
                s.Write(filterNone);
                s.Write(rgba.AsSpan(y * stride, stride));
            }
        }

        return (int)ms.Length;
    }

    private static List<(string Path, long Len)> Files(string dir, int top)
    {
        return Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories)
            .Select(p => (Path: p, Len: new FileInfo(p).Length))
            .OrderByDescending(x => x.Len)
            .Take(top)
            .ToList();
    }

    // ---------- 三种编码 ----------

    private static byte[] EncodeRgba(byte[] rgba, int w, int h)
    {
        byte[] raw = PackRows(rgba, w, h, 4, null);
        return BuildPng(raw, w, h, 6, null, null, 8);
    }

    private static byte[] EncodeGreyAlpha(byte[] rgba, int w, int h)
    {
        int n = w * h;
        byte[] px = new byte[n * 2];
        for (int i = 0, j = 0; i < n; i++, j += 2)
        {
            px[j] = rgba[i * 4];      // R==G==B 时取 R 即可
            px[j + 1] = rgba[i * 4 + 3];
        }

        byte[] raw = PackRows(px, w, h, 2, null);
        return BuildPng(raw, w, h, 4, null, null, 8);
    }

    private static byte[] EncodePalette(byte[] rgba, int w, int h)
    {
        Dictionary<uint, byte> map = new();
        List<uint> palette = new();
        int n = w * h;
        byte[] idx = new byte[n];
        for (int i = 0; i < n; i++)
        {
            uint c = (uint)(rgba[i * 4] << 24 | rgba[i * 4 + 1] << 16 | rgba[i * 4 + 2] << 8 | rgba[i * 4 + 3]);
            if (!map.TryGetValue(c, out byte v))
            {
                if (palette.Count >= 256)
                {
                    throw new InvalidOperationException(">256 色");
                }

                v = (byte)palette.Count;
                map[c] = v;
                palette.Add(c);
            }

            idx[i] = v;
        }

        byte[] plte = new byte[palette.Count * 3];
        byte[] trns = new byte[palette.Count];
        bool anyAlpha = false;
        for (int i = 0; i < palette.Count; i++)
        {
            plte[i * 3] = (byte)(palette[i] >> 24);
            plte[i * 3 + 1] = (byte)(palette[i] >> 16);
            plte[i * 3 + 2] = (byte)(palette[i] >> 8);
            trns[i] = (byte)palette[i];
            if (trns[i] != 255)
            {
                anyAlpha = true;
            }
        }

        byte[] raw = PackRows(idx, w, h, 1, null);
        return BuildPng(raw, w, h, 3, plte, anyAlpha ? trns : null, 8);
    }

    /// <summary>每行加 1 字节过滤类型（固定 None），行间不动。</summary>
    private static byte[] PackRows(byte[] px, int w, int h, int bytesPerPixel, byte[] _)
    {
        int stride = w * bytesPerPixel;
        byte[] raw = new byte[h * (1 + stride)];
        for (int y = 0; y < h; y++)
        {
            int dst = y * (1 + stride);
            raw[dst] = 0;
            Array.Copy(px, y * stride, raw, dst + 1, stride);
        }

        return raw;
    }

    // ---------- 容器 ----------

    private static byte[] BuildPng(ReadOnlySpan<byte> raw, int w, int h, int colorType, byte[] plte, byte[] trns, int depth)
    {
        using MemoryStream ms = new MemoryStream(1 << 16);
        using (ZLibStream z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw);
        }

        using MemoryStream o = new MemoryStream();
        o.Write(Sig);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)h);
        ihdr[8] = (byte)depth;
        ihdr[9] = (byte)colorType;
        WriteChunk(o, "IHDR", ihdr);
        if (plte != null)
        {
            WriteChunk(o, "PLTE", plte);
        }

        if (trns != null)
        {
            WriteChunk(o, "tRNS", trns);
        }

        WriteChunk(o, "IDAT", ms.GetBuffer().AsSpan(0, (int)ms.Length));
        WriteChunk(o, "IEND", ReadOnlySpan<byte>.Empty);
        return o.ToArray();
    }

    private static readonly byte[] Sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static void WriteChunk(Stream o, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> head = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(head[..4], (uint)data.Length);
        head[4] = (byte)type[0];
        head[5] = (byte)type[1];
        head[6] = (byte)type[2];
        head[7] = (byte)type[3];
        o.Write(head);
        o.Write(data);
        uint crc = Update(0xFFFFFFFFu, head[4..]);
        crc = Update(crc, data) ^ 0xFFFFFFFFu;
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        o.Write(tail);
    }

    private static uint[] BuildTable()
    {
        uint[] t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            t[n] = c;
        }

        return t;
    }

    private static readonly uint[] Table = BuildTable();

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }
}
