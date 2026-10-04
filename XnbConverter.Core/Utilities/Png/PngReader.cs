using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using XnbConverter.Exceptions;

namespace XnbConverter.Utilities.Png;

/// <summary>
/// PNG → RGBA8 的读入端。解码结果直接写进调用方给的数组，不经过任何图像对象。
///
/// 支持：颜色类型 0/2/3/4/6，位深 1/2/4/8/16，tRNS，五种过滤，Adam7 交错，多 IDAT。
/// 不校验各 chunk 的 CRC（读侧热路径），只做结构校验。
///
/// 热路径约定：**每个像素不做分支、不调方法**。反过滤按"行首 bpp 个字节"和"其余"拆成两段，
/// 展开按 (颜色类型, 位深) 特化成独立循环，交错与非交错也分开 —— 分支只发生在行级。
/// </summary>
internal static class PngReader
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly int[] Adam7XStart = { 0, 4, 0, 2, 0, 1, 0 };
    private static readonly int[] Adam7YStart = { 0, 0, 4, 0, 2, 0, 1 };
    private static readonly int[] Adam7XStep = { 8, 8, 4, 4, 2, 2, 1 };
    private static readonly int[] Adam7YStep = { 8, 8, 8, 4, 4, 2, 2 };

    public static byte[] Read(byte[] file, out int width, out int height)
    {
        if (file.Length < 8 || !file.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw new XnbError(Error.Png_1, "不是 PNG 文件");
        }

        // ---- 1. 走一遍 chunk，只取需要的几个 ----
        int bitDepth = 0, colorType = 0, interlace = 0;
        width = 0;
        height = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        int trnsGrey = -1, trnsR = -1, trnsG = -1, trnsB = -1;
        bool hasTrns = false;

        int idatFirstOffset = 0, idatFirstLength = 0, idatTotal = 0, idatCount = 0;
        List<(int Offset, int Length)>? idatExtra = null;

        int position = 8;
        while (position + 8 <= file.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(position, 4));
            if (length < 0 || position + 12 + length > file.Length)
            {
                throw new XnbError(Error.Png_1, "chunk 长度越界");
            }

            ReadOnlySpan<byte> type = file.AsSpan(position + 4, 4);
            int dataOffset = position + 8;

            if (type.SequenceEqual("IHDR"u8))
            {
                if (length < 13)
                {
                    throw new XnbError(Error.Png_1, "IHDR 过短");
                }

                width = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(dataOffset, 4));
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(dataOffset + 4, 4));
                bitDepth = file[dataOffset + 8];
                colorType = file[dataOffset + 9];
                interlace = file[dataOffset + 12];
                if (width <= 0 || height <= 0)
                {
                    throw new XnbError(Error.Png_1, "宽高非法");
                }
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = file.AsSpan(dataOffset, length).ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                // 一次性解出来，避免逐像素重复解析
                hasTrns = true;
                ReadOnlySpan<byte> trns = file.AsSpan(dataOffset, length);
                if (colorType == 0 && length >= 2)
                {
                    trnsGrey = BinaryPrimitives.ReadUInt16BigEndian(trns);
                }
                else if (colorType == 2 && length >= 6)
                {
                    trnsR = BinaryPrimitives.ReadUInt16BigEndian(trns);
                    trnsG = BinaryPrimitives.ReadUInt16BigEndian(trns.Slice(2));
                    trnsB = BinaryPrimitives.ReadUInt16BigEndian(trns.Slice(4));
                }
                else if (colorType == 3)
                {
                    // 每个调色板项一个 alpha，先存着，等 PLTE 到齐后再补
                    paletteAlpha = trns.ToArray();
                }
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (idatCount == 0)
                {
                    idatFirstOffset = dataOffset;
                    idatFirstLength = length;
                }
                else
                {
                    idatExtra ??= new List<(int, int)>(4);
                    idatExtra.Add((dataOffset, length));
                }

                idatCount++;
                idatTotal += length;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            position = dataOffset + length + 4; // 跳过 CRC
        }

        if (idatCount == 0 || width == 0 || height == 0)
        {
            throw new XnbError(Error.Png_1, "缺少 IHDR 或 IDAT");
        }

        int channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new XnbError(Error.Png_1, $"不支持的颜色类型 {colorType}")
        };
        if (colorType == 3 && palette == null)
        {
            throw new XnbError(Error.Png_1, "索引色缺少 PLTE");
        }

        if (colorType == 3 && paletteAlpha != null && palette != null)
        {
            // 补齐到调色板项数，缺的项 alpha = 255
            int entries = palette.Length / 3;
            if (paletteAlpha.Length < entries)
            {
                byte[] full = new byte[entries];
                full.AsSpan().Fill(255);
                paletteAlpha.AsSpan().CopyTo(full);
                paletteAlpha = full;
            }
        }
        else if (colorType == 3)
        {
            int entries = palette!.Length / 3;
            byte[] full = new byte[entries];
            full.AsSpan().Fill(255);
            paletteAlpha = full;
        }

        int bitsPerPixel = channels * bitDepth;
        int filterBpp = Math.Max(1, bitsPerPixel >> 3);

        // ---- 2. 算解压后该有多少字节，一次分配 ----
        int passCount = interlace == 0 ? 1 : 7;
        int expected = 0;
        Span<int> passRowBytes = stackalloc int[7];
        for (int pass = 0; pass < passCount; pass++)
        {
            PassGeometry(pass, interlace, width, height, out _, out _, out _, out _,
                out int passWidth, out int passHeight);
            int rowBytes = (passWidth * bitsPerPixel + 7) / 8;
            passRowBytes[pass] = rowBytes;
            expected += passHeight * (1 + rowBytes);
        }

        // ---- 3. 解压 ----
        byte[] raw = ArrayPool<byte>.Shared.Rent(Math.Max(expected, 1));
        try
        {
            Inflate(file, idatFirstOffset, idatFirstLength, idatExtra, idatTotal, raw.AsSpan(0, expected));

            // ---- 4. 反过滤 + 转 RGBA ----
            byte[] output = new byte[width * height * 4];
            int cursor = 0;
            bool interlaced = interlace != 0;

            for (int pass = 0; pass < passCount; pass++)
            {
                PassGeometry(pass, interlaced ? 1 : 0, width, height, out int xStart, out int yStart,
                    out int xStep, out int yStep, out int passWidth, out int passHeight);
                int rowBytes = passRowBytes[pass];
                if (passWidth == 0 || passHeight == 0)
                {
                    continue;
                }

                Span<byte> passSpan = raw.AsSpan(cursor, passHeight * (1 + rowBytes));
                UnfilterPass(passSpan, passHeight, rowBytes, filterBpp);

                for (int y = 0; y < passHeight; y++)
                {
                    ReadOnlySpan<byte> row = passSpan.Slice(y * (1 + rowBytes) + 1, rowBytes);
                    int outRow = (yStart + y * yStep) * width * 4;
                    if (interlaced)
                    {
                        ExpandStrided(row, passWidth, colorType, bitDepth, channels, palette,
                            paletteAlpha, hasTrns, trnsGrey, trnsR, trnsG, trnsB,
                            output, outRow, xStart, xStep);
                    }
                    else
                    {
                        ExpandFast(row, passWidth, colorType, bitDepth, palette,
                            paletteAlpha, hasTrns, trnsGrey, trnsR, trnsG, trnsB, output, outRow);
                    }
                }

                cursor += passHeight * (1 + rowBytes);
            }

            return output;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    private static void Inflate(byte[] file, int firstOffset, int firstLength,
        List<(int Offset, int Length)>? extra, int totalLength, Span<byte> destination)
    {
        // 单 IDAT（绝大多数情况）零拷贝套在文件数组上；多段才需要拼一次
        Stream source;
        if (extra == null)
        {
            source = new MemoryStream(file, firstOffset, firstLength, writable: false);
        }
        else
        {
            byte[] joined = new byte[totalLength];
            file.AsSpan(firstOffset, firstLength).CopyTo(joined);
            int at = firstLength;
            foreach ((int offset, int length) in extra)
            {
                file.AsSpan(offset, length).CopyTo(joined.AsSpan(at));
                at += length;
            }

            source = new MemoryStream(joined, writable: false);
        }

        using (source)
        using (ZLibStream inflate = new ZLibStream(source, CompressionMode.Decompress))
        {
            int read = 0;
            while (read < destination.Length)
            {
                int n = inflate.Read(destination[read..]);
                if (n <= 0)
                {
                    break;
                }

                read += n;
            }

            if (read != destination.Length)
            {
                throw new XnbError(Error.Png_1, "解压后长度不符");
            }
        }
    }

    private static void PassGeometry(int pass, int interlace, int width, int height,
        out int xStart, out int yStart, out int xStep, out int yStep, out int passWidth, out int passHeight)
    {
        if (interlace == 0)
        {
            xStart = 0;
            yStart = 0;
            xStep = 1;
            yStep = 1;
        }
        else
        {
            xStart = Adam7XStart[pass];
            yStart = Adam7YStart[pass];
            xStep = Adam7XStep[pass];
            yStep = Adam7YStep[pass];
        }

        passWidth = width > xStart ? (width - xStart + xStep - 1) / xStep : 0;
        passHeight = height > yStart ? (height - yStart + yStep - 1) / yStep : 0;
    }

    // ---------------------------------------------------------------- 反过滤

    private static void UnfilterPass(Span<byte> pass, int passHeight, int rowBytes, int bpp)
    {
        for (int y = 0; y < passHeight; y++)
        {
            int offset = y * (1 + rowBytes);
            Span<byte> row = pass.Slice(offset + 1, rowBytes);
            Span<byte> prev = y == 0 ? default : pass.Slice(offset - rowBytes, rowBytes);

            switch (pass[offset])
            {
                case 0:
                    break;
                case 1: // Sub
                    UnfilterSub(row, rowBytes, bpp);
                    break;
                case 2: // Up —— 第一行的"上一行"按全 0 处理，即原样不动
                    if (y > 0)
                    {
                        UnfilterUp(row, prev, rowBytes);
                    }

                    break;
                case 3: // Average
                    if (y == 0)
                    {
                        UnfilterAverageFirst(row, rowBytes, bpp);
                    }
                    else
                    {
                        UnfilterAverage(row, prev, rowBytes, bpp);
                    }

                    break;
                case 4: // Paeth —— 第一行时 Paeth 退化成 Sub（b=c=0 时预测值恒等于 a）
                    if (y == 0)
                    {
                        UnfilterSub(row, rowBytes, bpp);
                    }
                    else if (bpp == 4 && Sse2.IsSupported)
                    {
                        UnfilterPaeth4(row, prev, rowBytes);
                    }
                    else
                    {
                        UnfilterPaeth(row, prev, rowBytes, bpp);
                    }

                    break;
                default:
                    throw new XnbError(Error.Png_1, $"未知的过滤类型 {pass[offset]}");
            }
        }
    }

    // 下面几个都把"行首 bpp 个字节"单独成环，好让主循环里没有 i>=bpp 的判断。
    // 全部用 ref + Unsafe.Add 走指针算术：Span 的下标访问每次都要做边界检查，
    // 在逐字节的循环里这才是真正的开销大头。

    private static void UnfilterSub(Span<byte> row, int n, int bpp)
    {
        ref byte r = ref MemoryMarshal.GetReference(row);
        for (int i = bpp; i < n; i++)
        {
            Unsafe.Add(ref r, i) = (byte)(Unsafe.Add(ref r, i) + Unsafe.Add(ref r, i - bpp));
        }
    }

    private static void UnfilterUp(Span<byte> row, Span<byte> prev, int n)
    {
        ref byte r = ref MemoryMarshal.GetReference(row);
        ref byte p = ref MemoryMarshal.GetReference(prev);
        for (int i = 0; i < n; i++)
        {
            Unsafe.Add(ref r, i) = (byte)(Unsafe.Add(ref r, i) + Unsafe.Add(ref p, i));
        }
    }

    private static void UnfilterAverageFirst(Span<byte> row, int n, int bpp)
    {
        // y=0 时 up=0，行首 bpp 个字节的 left 也是 0，预测值恒为 0，原样不动
        ref byte r = ref MemoryMarshal.GetReference(row);
        for (int i = bpp; i < n; i++)
        {
            Unsafe.Add(ref r, i) = (byte)(Unsafe.Add(ref r, i) + (Unsafe.Add(ref r, i - bpp) >> 1));
        }
    }

    private static void UnfilterAverage(Span<byte> row, Span<byte> prev, int n, int bpp)
    {
        ref byte r = ref MemoryMarshal.GetReference(row);
        ref byte p = ref MemoryMarshal.GetReference(prev);
        int head = bpp < n ? bpp : n;
        for (int i = 0; i < head; i++)
        {
            Unsafe.Add(ref r, i) = (byte)(Unsafe.Add(ref r, i) + (Unsafe.Add(ref p, i) >> 1));
        }

        for (int i = bpp; i < n; i++)
        {
            int pred = (Unsafe.Add(ref r, i - bpp) + Unsafe.Add(ref p, i)) >> 1;
            Unsafe.Add(ref r, i) = (byte)(Unsafe.Add(ref r, i) + pred);
        }
    }

    /// <summary>
    /// bpp == 4（RGBA8）时的 Paeth，用 SSE2 一次算一个像素的 4 个通道。
    ///
    /// 能这么算的关键：a 是"本行已还原的前一个像素"，所以同一个像素的 4 个通道互相独立，
    /// 而 a/b/c 三者在算这个像素时全都已知。一个像素 4 字节正好铺满 4 条 16 位通道。
    /// 标量版每字节约 20 条指令，这里摊到每字节只要 3 条左右。
    /// </summary>
    private static void UnfilterPaeth4(Span<byte> row, Span<byte> prev, int n)
    {
        ref byte r = ref MemoryMarshal.GetReference(row);
        ref byte p = ref MemoryMarshal.GetReference(prev);

        // 行首 4 字节：a=0、c=0 时 Paeth 恒等于 b
        int head = n < 4 ? n : 4;
        for (int i = 0; i < head; i++)
        {
            Unsafe.Add(ref r, i) = (byte)(Unsafe.Add(ref r, i) + Unsafe.Add(ref p, i));
        }

        Vector128<byte> zeroB = Vector128<byte>.Zero;
        Vector128<short> zeroS = Vector128<short>.Zero;
        Vector128<short> lowByte = Vector128.Create((short)0x00FF);

        int i2 = 4;
        for (; i2 + 4 <= n; i2 += 4)
        {
            // 各取一个像素的 4 字节，展开成 4 条 16 位通道
            Vector128<short> a = Sse2.UnpackLow(
                Vector128.CreateScalar(Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref r, i2 - 4))).AsByte(), zeroB).AsInt16();
            Vector128<short> b = Sse2.UnpackLow(
                Vector128.CreateScalar(Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref p, i2))).AsByte(), zeroB).AsInt16();
            Vector128<short> c = Sse2.UnpackLow(
                Vector128.CreateScalar(Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref p, i2 - 4))).AsByte(), zeroB).AsInt16();
            Vector128<short> filtered = Sse2.UnpackLow(
                Vector128.CreateScalar(Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref r, i2))).AsByte(), zeroB).AsInt16();

            // pa=|b-c|, pb=|a-c|, pc=|a+b-2c|；16 位有符号的 abs 就是 max(x, -x)
            Vector128<short> d = Sse2.Subtract(b, c);
            Vector128<short> pa = Sse2.Max(d, Sse2.Subtract(zeroS, d));

            d = Sse2.Subtract(a, c);
            Vector128<short> pb = Sse2.Max(d, Sse2.Subtract(zeroS, d));

            d = Sse2.Subtract(Sse2.Add(a, b), Sse2.Add(c, c));
            Vector128<short> pc = Sse2.Max(d, Sse2.Subtract(zeroS, d));

            // pa > pb ? (b, pb) : (a, pa)
            Vector128<short> gt = Sse2.CompareGreaterThan(pa, pb);
            Vector128<short> near = Sse2.Or(Sse2.And(gt, b), Sse2.AndNot(gt, a));
            Vector128<short> nearDist = Sse2.Or(Sse2.And(gt, pb), Sse2.AndNot(gt, pa));

            // nearDist > pc ? c : near
            Vector128<short> gt2 = Sse2.CompareGreaterThan(nearDist, pc);
            Vector128<short> pred = Sse2.Or(Sse2.And(gt2, c), Sse2.AndNot(gt2, near));

            // 加回去。注意反过滤是**按 256 取模**（标量那边就是 (byte) 截断），
            // 不能直接饱和打包 —— 和落在 256..510 时会被饱和成 255，那就错了。
            Vector128<short> result = Sse2.And(Sse2.Add(filtered, pred), lowByte);
            Vector128<byte> packed = Sse2.PackUnsignedSaturate(result, zeroS);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref r, i2), packed.AsUInt32().ToScalar());
        }

        // 行尾不足 4 字节（RGBA8 下不会发生，保险起见）
        for (; i2 < n; i2++)
        {
            int a = Unsafe.Add(ref r, i2 - 4);
            int b = Unsafe.Add(ref p, i2);
            int c = Unsafe.Add(ref p, i2 - 4);
            int dd = b - c;
            int s = dd >> 31;
            int pa = (dd ^ s) - s;
            dd = a - c;
            s = dd >> 31;
            int pb = (dd ^ s) - s;
            dd = a + b - (c << 1);
            s = dd >> 31;
            int pc = (dd ^ s) - s;
            int m = (pa - pb - 1) >> 31;
            int near = b ^ ((a ^ b) & m);
            int nd = pb ^ ((pa ^ pb) & m);
            int m2 = (nd - pc - 1) >> 31;
            int predTail = c ^ ((near ^ c) & m2);
            Unsafe.Add(ref r, i2) = (byte)(Unsafe.Add(ref r, i2) + predTail);
        }
    }

    private static void UnfilterPaeth(Span<byte> row, Span<byte> prev, int n, int bpp)
    {
        ref byte r = ref MemoryMarshal.GetReference(row);
        ref byte p = ref MemoryMarshal.GetReference(prev);

        // 行首 bpp 字节：a=0、c=0 时 Paeth 恒等于 b
        int head = bpp < n ? bpp : n;
        for (int i = 0; i < head; i++)
        {
            Unsafe.Add(ref r, i) = (byte)(Unsafe.Add(ref r, i) + Unsafe.Add(ref p, i));
        }

        // 主体。全程无分支 —— 这里一旦编译出条件跳转，自然图像上根本无法预测，
        // 一次误判就是十几二十个周期。三处 abs 和两处三选一都用位运算写成算术。
        // 距离用 |b-c| / |a-c| / |a+b-2c|，与 |p-a| / |p-b| / |p-c| 等价且少一次加法。
        for (int i = bpp; i < n; i++)
        {
            int j = i - bpp;
            int a = Unsafe.Add(ref r, j);
            int b = Unsafe.Add(ref p, i);
            int c = Unsafe.Add(ref p, j);

            int d = b - c;
            int s = d >> 31;
            int pa = (d ^ s) - s;

            d = a - c;
            s = d >> 31;
            int pb = (d ^ s) - s;

            d = a + b - (c << 1);
            s = d >> 31;
            int pc = (d ^ s) - s;

            // pa <= pb ? (a, pa) : (b, pb)
            int m = (pa - pb - 1) >> 31;
            int near = b ^ ((a ^ b) & m);
            int nearDist = pb ^ ((pa ^ pb) & m);

            // nearDist <= pc ? near : c
            int m2 = (nearDist - pc - 1) >> 31;
            int pred = c ^ ((near ^ c) & m2);

            Unsafe.Add(ref r, i) = (byte)(Unsafe.Add(ref r, i) + pred);
        }
    }

    // ---------------------------------------------------------------- 展开成 RGBA

    /// <summary>非交错：输出连续，逐类型特化。</summary>
    private static void ExpandFast(ReadOnlySpan<byte> row, int count, int colorType, int bitDepth,
        byte[]? palette, byte[]? paletteAlpha, bool hasTrns,
        int trnsGrey, int trnsR, int trnsG, int trnsB, byte[] output, int outRow)
    {
        int at = outRow;
        switch (colorType)
        {
            case 6:
                if (bitDepth == 8)
                {
                    // 最常见的情况：源就是 RGBA8，整行直接搬
                    row.Slice(0, count * 4).CopyTo(output.AsSpan(at));
                }
                else
                {
                    ExpandSixteen(row, count, 4, output, at);
                }

                break;

            case 2:
                if (bitDepth == 8)
                {
                    ExpandRgb8(row, count, hasTrns, trnsR, trnsG, trnsB, output, at);
                }
                else
                {
                    ExpandRgb16(row, count, hasTrns, trnsR, trnsG, trnsB, output, at);
                }

                break;

            case 4:
                if (bitDepth == 8)
                {
                    for (int i = 0; i < count; i++)
                    {
                        byte g = row[i * 2];
                        output[at] = g;
                        output[at + 1] = g;
                        output[at + 2] = g;
                        output[at + 3] = row[i * 2 + 1];
                        at += 4;
                    }
                }
                else
                {
                    ExpandGreyAlpha16(row, count, output, at);
                }

                break;

            case 0:
                if (bitDepth == 8)
                {
                    ExpandGrey8(row, count, hasTrns, trnsGrey, output, at);
                }
                else if (bitDepth == 16)
                {
                    ExpandGrey16(row, count, hasTrns, trnsGrey, output, at);
                }
                else
                {
                    ExpandGreyPacked(row, count, bitDepth, hasTrns, trnsGrey, output, at);
                }

                break;

            default: // 3 = 索引色
                ExpandPalette(row, count, bitDepth, palette!, paletteAlpha!, output, at);
                break;
        }

    }

    /// <summary>Adam7：像素散点写，步长固定。</summary>
    private static void ExpandStrided(ReadOnlySpan<byte> row, int count, int colorType, int bitDepth,
        int channels, byte[]? palette, byte[]? paletteAlpha, bool hasTrns,
        int trnsGrey, int trnsR, int trnsG, int trnsB, byte[] output, int outRow, int xStart, int xStep)
    {
        if (colorType == 6 && bitDepth == 8)
        {
            int at = outRow + xStart * 4;
            for (int i = 0; i < count; i++)
            {
                int src = i * 4;
                output[at] = row[src];
                output[at + 1] = row[src + 1];
                output[at + 2] = row[src + 2];
                output[at + 3] = row[src + 3];
                at += xStep * 4;
            }

            return;
        }

        // 其余组合少见，走通用分支即可
        for (int i = 0; i < count; i++)
        {
            byte r, g, b, a = 255;
            if (bitDepth < 8)
            {
                int mask = (1 << bitDepth) - 1;
                int bit = i * bitDepth;
                int v = row[bit >> 3] >> (8 - bitDepth - (bit & 7)) & mask;
                if (colorType == 3)
                {
                    int pi = v * 3;
                    if (pi + 2 >= palette!.Length)
                    {
                        throw new XnbError(Error.Png_1, "调色板索引越界");
                    }

                    r = palette[pi];
                    g = palette[pi + 1];
                    b = palette[pi + 2];
                    a = paletteAlpha![v];
                }
                else
                {
                    r = g = b = (byte)(v * (255 / ((1 << bitDepth) - 1)));
                    if (hasTrns && v == trnsGrey)
                    {
                        a = 0;
                    }
                }
            }
            else if (bitDepth == 16)
            {
                int src = i * channels * 2;
                if (colorType == 6)
                {
                    r = row[src];
                    g = row[src + 2];
                    b = row[src + 4];
                    a = row[src + 6];
                }
                else if (colorType == 2)
                {
                    r = row[src];
                    g = row[src + 2];
                    b = row[src + 4];
                }
                else if (colorType == 4)
                {
                    r = g = b = row[src];
                    a = row[src + 2];
                }
                else
                {
                    r = g = b = row[src];
                }
            }
            else
            {
                int src = i * channels;
                if (colorType == 6)
                {
                    r = row[src];
                    g = row[src + 1];
                    b = row[src + 2];
                    a = row[src + 3];
                }
                else if (colorType == 2)
                {
                    r = row[src];
                    g = row[src + 1];
                    b = row[src + 2];
                }
                else if (colorType == 4)
                {
                    r = g = b = row[src];
                    a = row[src + 1];
                }
                else if (colorType == 3)
                {
                    int pi = row[src] * 3;
                    if (pi + 2 >= palette!.Length)
                    {
                        throw new XnbError(Error.Png_1, "调色板索引越界");
                    }

                    r = palette[pi];
                    g = palette[pi + 1];
                    b = palette[pi + 2];
                    a = paletteAlpha![row[src]];
                }
                else
                {
                    r = g = b = row[src];
                }
            }

            int at = outRow + (xStart + i * xStep) * 4;
            output[at] = r;
            output[at + 1] = g;
            output[at + 2] = b;
            output[at + 3] = a;
        }
    }

    private static void ExpandRgb8(ReadOnlySpan<byte> row, int count, bool hasTrns,
        int trnsR, int trnsG, int trnsB, byte[] output, int at)
    {
        if (!hasTrns)
        {
            for (int i = 0; i < count; i++)
            {
                int src = i * 3;
                output[at] = row[src];
                output[at + 1] = row[src + 1];
                output[at + 2] = row[src + 2];
                output[at + 3] = 255;
                at += 4;
            }

            return;
        }

        for (int i = 0; i < count; i++)
        {
            int src = i * 3;
            byte r = row[src];
            byte g = row[src + 1];
            byte b = row[src + 2];
            output[at] = r;
            output[at + 1] = g;
            output[at + 2] = b;
            output[at + 3] = r == trnsR && g == trnsG && b == trnsB ? (byte)0 : (byte)255;
            at += 4;
        }
    }

    private static void ExpandRgb16(ReadOnlySpan<byte> row, int count, bool hasTrns,
        int trnsR, int trnsG, int trnsB, byte[] output, int at)
    {
        for (int i = 0; i < count; i++)
        {
            int src = i * 6;
            byte r = row[src];
            byte g = row[src + 2];
            byte b = row[src + 4];
            output[at] = r;
            output[at + 1] = g;
            output[at + 2] = b;
            bool transparent = hasTrns
                && ((row[src] << 8) | row[src + 1]) == trnsR
                && ((row[src + 2] << 8) | row[src + 3]) == trnsG
                && ((row[src + 4] << 8) | row[src + 5]) == trnsB;
            output[at + 3] = transparent ? (byte)0 : (byte)255;
            at += 4;
        }
    }

    private static void ExpandGrey8(ReadOnlySpan<byte> row, int count, bool hasTrns, int trnsGrey,
        byte[] output, int at)
    {
        if (!hasTrns)
        {
            for (int i = 0; i < count; i++)
            {
                byte v = row[i];
                output[at] = v;
                output[at + 1] = v;
                output[at + 2] = v;
                output[at + 3] = 255;
                at += 4;
            }

            return;
        }

        for (int i = 0; i < count; i++)
        {
            byte v = row[i];
            output[at] = v;
            output[at + 1] = v;
            output[at + 2] = v;
            output[at + 3] = v == trnsGrey ? (byte)0 : (byte)255;
            at += 4;
        }
    }

    private static void ExpandGrey16(ReadOnlySpan<byte> row, int count, bool hasTrns, int trnsGrey,
        byte[] output, int at)
    {
        for (int i = 0; i < count; i++)
        {
            int src = i * 2;
            byte v = row[src];
            output[at] = v;
            output[at + 1] = v;
            output[at + 2] = v;
            output[at + 3] = hasTrns && BinaryPrimitives.ReadUInt16BigEndian(row.Slice(src, 2)) == trnsGrey
                ? (byte)0
                : (byte)255;
            at += 4;
        }
    }

    private static void ExpandGreyAlpha16(ReadOnlySpan<byte> row, int count, byte[] output, int at)
    {
        for (int i = 0; i < count; i++)
        {
            int src = i * 4;
            byte v = row[src];
            output[at] = v;
            output[at + 1] = v;
            output[at + 2] = v;
            output[at + 3] = row[src + 2];
            at += 4;
        }
    }

    private static void ExpandSixteen(ReadOnlySpan<byte> row, int count, int channels,
        byte[] output, int at)
    {
        for (int i = 0; i < count; i++)
        {
            int src = i * channels * 2;
            output[at] = row[src];
            output[at + 1] = row[src + 2];
            output[at + 2] = row[src + 4];
            output[at + 3] = channels == 4 ? row[src + 6] : (byte)255;
            at += 4;
        }
    }

    private static void ExpandGreyPacked(ReadOnlySpan<byte> row, int count, int bitDepth,
        bool hasTrns, int trnsGrey, byte[] output, int at)
    {
        int mask = (1 << bitDepth) - 1;
        int scale = 255 / mask;
        for (int i = 0; i < count; i++)
        {
            int bit = i * bitDepth;
            int v = row[bit >> 3] >> (8 - bitDepth - (bit & 7)) & mask;
            byte g = (byte)(v * scale);
            output[at] = g;
            output[at + 1] = g;
            output[at + 2] = g;
            output[at + 3] = hasTrns && v == trnsGrey ? (byte)0 : (byte)255;
            at += 4;
        }
    }

    private static void ExpandPalette(ReadOnlySpan<byte> row, int count, int bitDepth,
        byte[] palette, byte[] paletteAlpha, byte[] output, int at)
    {
        int entries = palette.Length / 3;
        if (bitDepth == 8)
        {
            for (int i = 0; i < count; i++)
            {
                int index = row[i];
                if (index >= entries)
                {
                    throw new XnbError(Error.Png_1, "调色板索引越界");
                }

                int pi = index * 3;
                output[at] = palette[pi];
                output[at + 1] = palette[pi + 1];
                output[at + 2] = palette[pi + 2];
                output[at + 3] = paletteAlpha[index];
                at += 4;
            }

            return;
        }

        int mask = (1 << bitDepth) - 1;
        for (int i = 0; i < count; i++)
        {
            int bit = i * bitDepth;
            int index = row[bit >> 3] >> (8 - bitDepth - (bit & 7)) & mask;
            if (index >= entries)
            {
                throw new XnbError(Error.Png_1, "调色板索引越界");
            }

            int pi = index * 3;
            output[at] = palette[pi];
            output[at + 1] = palette[pi + 1];
            output[at + 2] = palette[pi + 2];
            output[at + 3] = paletteAlpha[index];
            at += 4;
        }
    }
}
