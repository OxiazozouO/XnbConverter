using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace XnbConverter.Utilities.Png;

/// <summary>
/// RGBA8 → PNG 的写出端。
///
/// 输入就是 <see cref="Entity.Mono.Texture2D.Data"/> 那种 R,G,B,A 逐像素、逐行排布的内存，
/// 所以过滤可以**直接在源数组上做**，不需要先拷进图像对象 —— 这是相比 ImageSharp 最直接的一处收益。
///
/// 性能优化点都留了口子：
/// <list type="bullet">
///   <item>过滤方式（<see cref="Filter"/>.None 现在是零拷贝直通，改成 Paeth/自适应只动一处）；</item>
///   <item>压缩档（<c>CompressionLevel</c>）；</item>
///   <item>DEFLATE 本身（<see cref="Compress"/> 是唯一和 zlib 打交道的地方，将来要换自研的只改这里）。</item>
/// </list>
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// 每行的过滤类型，固定 None。
    ///
    /// 实测过 Paeth：在这类大片平坦/透明的贴图上反而更大（15.8 MB → 17.6 MB），
    /// 因为 None 保留的长行程对 DEFLATE 更友好。所以这里不做自适应。
    /// </summary>
    private const byte FilterNone = 0;

    /// <summary>
    /// DEFLATE 档位，来自 .config/config.json 的 PngCompression。
    /// 默认 optimal（体积优先）；设成 fastest 可以把编码耗时砍掉约 3.4 倍，代价是 PNG 大 ~40%。
    /// </summary>
    private static CompressionLevel Level =>
        Configurations.ConfigHelper.PngFastest ? CompressionLevel.Fastest : CompressionLevel.Optimal;

    public static void Write(Stream output, ReadOnlySpan<byte> rgba, int width, int height)
    {
        // 颜色数 ≤256 时改写成调色板 PNG：每像素只喂给 DEFLATE 1 字节而不是 4 字节。
        // 实测（Content/Fonts，16 张大图集，188 MB 像素）：
        //   体积 14.40 → 10.40 MB（−28%），编码 4.04 → 2.24 s（−45%），读回逐字节无损。
        // 落点是字体图集那类「灰阶 + 极少数颜色」的贴图 —— 它们正是解包里最慢的一批；
        // 颜色多的图会在统计阶段早早放弃，退回下面的 RGBA 路径，代价只有几百次查表。
        if (TryBuildPalette(rgba, out byte[] plte, out byte[] trns, out byte[] indices, out int bitDepth))
        {
            // 下标数组是从池里借的（4096² 就是 16MB，直接进 LOH），写完立刻还
            try
            {
                WritePalette(output, indices, width, height, plte, trns, bitDepth);
            }
            finally
            {
                Pool.Return(indices);
            }

            return;
        }

        int stride = width * 4;

        // 压缩落在池化缓冲上：MemoryStream 每张图都要新开一块 64KB，涨不动了就翻倍并丢掉旧块，
        // 而每张图的输出平均只有几十 KB —— 这一进一出上千次就是几百 MB 的垃圾，还全落在 LOH 上。
        // PooledStream 直接把数组从池里借，用完还回去，同一个块在整批文件之间循环。
        using PooledStream deflated = new PooledStream(1 << 16);
        using (ZLibStream deflate = new ZLibStream(deflated, Level, leaveOpen: true))
        {
            // 过滤固定 None，所以 deflate 的输入就是「每行 1 字节过滤类型 + 整行像素」。
            // 这里按行直接喂进去，不再另开一块和整张图同样大的扫描线缓冲
            //（旧写法要先把整张图拷进 raw 再一次性压缩，等于白多占一份图像大小的内存）。
            Span<byte> filterNone = stackalloc byte[1];
            for (int y = 0; y < height; y++)
            {
                deflate.Write(filterNone);
                deflate.Write(rgba.Slice(y * stride, stride));
            }
        }

        ReadOnlySpan<byte> compressed = deflated.Written;

        output.Write(Signature);
        WriteIhdr(output, width, height, 6);
        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    /// <summary>调色板最多 256 项（位深 8）。</summary>
    private const int MaxPalette = 256;

    /// <summary>颜色去重表的槽数：最多 256 色，负载 ≤50%，线性探测足够。</summary>
    private const int ColorSlots = 512;

    /// <summary>
    /// 用开放寻址表统计独立 RGBA 值，按 <paramref name="stepPixels"/> 步长取样，
    /// 一旦超过 <see cref="MaxPalette"/> 立即返回（不必把整图走完）。
    /// </summary>
    private static int CountColors(ReadOnlySpan<byte> rgba, int stepPixels, Span<uint> keys, Span<int> vals)
    {
        vals.Fill(-1);
        int mask = ColorSlots - 1;
        int count = 0;
        for (int p = 0; p * 4 < rgba.Length; p += stepPixels)
        {
            int i = p * 4;
            uint c = (uint)(rgba[i] << 24 | rgba[i + 1] << 16 | rgba[i + 2] << 8 | rgba[i + 3]);
            int h = (int)((c * 2654435761u) >> 23) & mask;
            while (vals[h] >= 0 && keys[h] != c)
            {
                h = (h + 1) & mask;
            }

            if (vals[h] < 0)
            {
                if (count == MaxPalette)
                {
                    return count + 1;
                }

                keys[h] = c;
                vals[h] = count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 统计整图的独立 RGBA 值。超过 <see cref="MaxPalette"/> 就立刻放弃 ——
    /// 真彩色贴图通常几千个像素内就凑满 256 色，这一步几乎不花时间。
    /// </summary>
    private static bool TryBuildPalette(ReadOnlySpan<byte> rgba, out byte[] plte, out byte[] trns,
        out byte[] indices, out int bitDepth)
    {
        plte = System.Array.Empty<byte>();
        trns = System.Array.Empty<byte>();
        indices = System.Array.Empty<byte>();
        bitDepth = 8;

        int pixels = rgba.Length / 4;
        if (pixels == 0)
        {
            return false;
        }

        // 颜色表用栈上的开放寻址小表，不用 Dictionary<uint,byte>：
        // 后者每像素一次装箱式哈希，全图扫下来是秒级开销。
        // 另外先做一遍**采样预检** —— 大图（1024x4096 这种对照表）常有几百种颜色，
        // 却先铺一大片同色（透明）才出现新颜色，逐像素数要白扫几百万次。
        // 只抽 4096 个点均匀撒在全图上，样本里就超过 256 色的话整图必然更多，直接放弃。
        Span<uint> keys = stackalloc uint[ColorSlots];
        Span<int> vals = stackalloc int[ColorSlots];
        int sampleStep = Math.Max(1, pixels / 4096);
        if (CountColors(rgba, sampleStep, keys, vals) > MaxPalette)
        {
            return false;
        }

        // 全量统计：只有真的 ≤256 色（值得写调色板）才会走到这里
        vals.Fill(-1);
        byte[] colors = new byte[MaxPalette * 4];
        int count = 0;
        int mask = ColorSlots - 1;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            uint c = (uint)(rgba[i] << 24 | rgba[i + 1] << 16 | rgba[i + 2] << 8 | rgba[i + 3]);
            int h = (int)((c * 2654435761u) >> 23) & mask;
            while (vals[h] >= 0 && keys[h] != c)
            {
                h = (h + 1) & mask;
            }

            if (vals[h] < 0)
            {
                if (count == MaxPalette)
                {
                    return false;
                }

                keys[h] = c;
                vals[h] = count;
                colors[count * 4] = rgba[i];
                colors[count * 4 + 1] = rgba[i + 1];
                colors[count * 4 + 2] = rgba[i + 2];
                colors[count * 4 + 3] = rgba[i + 3];
                count++;
            }
        }

        // 第二遍才建下标（确定要写调色板了）
        byte[] idx = Pool.RentByte(pixels);
        for (int i = 0, p = 0; p < pixels; i += 4, p++)
        {
            uint c = (uint)(rgba[i] << 24 | rgba[i + 1] << 16 | rgba[i + 2] << 8 | rgba[i + 3]);
            int h = (int)((c * 2654435761u) >> 23) & mask;
            while (keys[h] != c)
            {
                h = (h + 1) & mask;
            }

            idx[p] = (byte)vals[h];
        }

        byte[] palette = new byte[count * 3];
        byte[] alpha = new byte[count];
        bool anyAlpha = false;
        for (int i = 0; i < count; i++)
        {
            palette[i * 3] = colors[i * 4];
            palette[i * 3 + 1] = colors[i * 4 + 1];
            palette[i * 3 + 2] = colors[i * 4 + 2];
            alpha[i] = colors[i * 4 + 3];
            if (alpha[i] != 255)
            {
                anyAlpha = true;
            }
        }

        plte = palette;
        // 全不透明就不写 tRNS，能再省一点（也少一个块）
        trns = anyAlpha ? alpha : System.Array.Empty<byte>();
        indices = idx;
        // 能塞进更低的位深就用更低的：调色板下标本身很小，位深一降，
        // 交给 DEFLATE 的原始数据就同比减少（2 色→1 位、4 色→2 位、16 色→4 位）。
        bitDepth = count <= 2 ? 1 : count <= 4 ? 2 : count <= 16 ? 4 : 8;
        return true;
    }

    private static void WritePalette(Stream output, byte[] indices, int width, int height,
        byte[] plte, byte[] trns, int bitDepth)
    {
        int rowBytes = (width * bitDepth + 7) / 8;
        byte[] packed = bitDepth == 8 ? System.Array.Empty<byte>() : new byte[rowBytes];

        using PooledStream deflated = new PooledStream(1 << 16);
        using (ZLibStream deflate = new ZLibStream(deflated, Level, leaveOpen: true))
        {
            // 同样固定 None：每行 1 字节过滤类型 + 该行的调色板下标
            Span<byte> filterNone = stackalloc byte[1];
            for (int y = 0; y < height; y++)
            {
                deflate.Write(filterNone);
                ReadOnlySpan<byte> row = indices.AsSpan(y * width, width);
                if (bitDepth == 8)
                {
                    deflate.Write(row);
                }
                else
                {
                    PackRow(row, packed, bitDepth);
                    deflate.Write(packed);
                }
            }
        }

        output.Write(Signature);
        WriteIhdr(output, width, height, 3, (byte)bitDepth);
        WriteChunk(output, "PLTE", plte);
        if (trns.Length > 0)
        {
            WriteChunk(output, "tRNS", trns);
        }

        WriteChunk(output, "IDAT", deflated.Written);
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    /// <summary>把一行调色板下标压进 &lt;8 位的行缓冲；PNG 规定同一字节内高位在前。</summary>
    private static void PackRow(ReadOnlySpan<byte> indices, Span<byte> dst, int bitDepth)
    {
        int perByte = 8 / bitDepth;
        int mask = (1 << bitDepth) - 1;
        dst.Clear();
        for (int i = 0; i < indices.Length; i++)
        {
            int shift = 8 - bitDepth * (i % perByte + 1);
            dst[i / perByte] |= (byte)((indices[i] & mask) << shift);
        }
    }

    private static void WriteIhdr(Stream output, int width, int height, byte colorType, byte bitDepth = 8)
    {
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = bitDepth;  // 位深
        ihdr[9] = colorType; // 颜色类型（6 = RGBA，3 = 调色板）
        ihdr[10] = 0;        // 压缩方法
        ihdr[11] = 0;        // 过滤方法
        ihdr[12] = 0;        // 非交错
        WriteChunk(output, "IHDR", ihdr);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)data.Length);
        header[4] = (byte)type[0];
        header[5] = (byte)type[1];
        header[6] = (byte)type[2];
        header[7] = (byte)type[3];
        output.Write(header);

        output.Write(data);

        uint crc = Crc32.Update(0, header[4..]);
        crc = Crc32.Update(crc, data);
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        output.Write(tail);
    }

    /// <summary>
    /// 只写不读、可增长、底层数组走池的缓冲，用来接 DEFLATE 的输出。
    ///
    /// 替代 MemoryStream 的原因：后者每次构造都要新开一块初始缓冲，涨不动时按倍增涨并丢掉旧块。
    /// 一批文件下来（实测 Content 有 1019 张 PNG）会累积几百 MB 垃圾，而且这些块大多 ≥85KB，
    /// 全落在 LOH 上，进的是昂贵的 gen2 回收。这里借池里的数组，用完还回去循环复用。
    /// </summary>
    private sealed class PooledStream : Stream
    {
        private byte[] _buffer;

        private int _length;

        public PooledStream(int initialCapacity)
        {
            _buffer = Pool.RentByte(initialCapacity);
        }

        /// <summary>已写入的内容（不含未使用的容量）。</summary>
        public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _length;

        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_length + buffer.Length > _buffer.Length)
            {
                Grow(_length + buffer.Length);
            }

            buffer.CopyTo(_buffer.AsSpan(_length));
            _length += buffer.Length;
        }

        private void Grow(int needed)
        {
            int size = _buffer.Length * 2;
            while (size < needed)
            {
                size *= 2;
            }

            byte[] bigger = Pool.RentByte(size);
            _buffer.AsSpan(0, _length).CopyTo(bigger);
            Pool.Return(_buffer);
            _buffer = bigger;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Pool.Return(_buffer);
                _buffer = System.Array.Empty<byte>();
                _length = 0;
            }

            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
