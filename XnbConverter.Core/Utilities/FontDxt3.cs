namespace XnbConverter.Utilities;

/// <summary>
/// XNA / MonoGame 给**字体贴图**专用的 DXT3 编码器。
///
/// 对应管线里的 <c>GraphicsUtil.CompressFontDXT3</c>（<c>CompressDxt</c> 的 isSpriteFont
/// 分支）。它和通用 DXT3 编码器的区别是端点被写死：
/// <list type="bullet">
///   <item>c0 = 0xFFFF（白）、c1 = 0x0000（黑），整张贴图恒定不变；</item>
///   <item>每个像素的 2 bit 索引直接取灰度的高 2 位再查表，只用灰度通道；</item>
///   <item>alpha 只保留高 2 位，即量化到 0/5/10/15 四档。</item>
/// </list>
/// 用通用编码器（libsquish）压出来在视觉上等价，但**不是逐字节相同** —— 端点顺序和
/// 全透明块的处理都不一样，所以字体贴图必须走这一支才能对称。
/// </summary>
public static class FontDxt3
{
    /// <summary>
    /// 灰度档位（0..3）到 DXT3 索引的映射。
    /// 由于 c0 是白、c1 是黑：0 → c1(黑)、1 → 1/3×c0+2/3×c1、2 → 2/3×c0+1/3×c1、3 → c0(白)。
    /// （MonoGame 那份端点是反的，映射表是 XOR 1 的 {0,2,3,1}。）
    /// </summary>
    private static readonly byte[] GreyToIndex = { 1, 3, 2, 0 };

    /// <summary>
    /// 把 RGBA 像素压成 DXT3。每像素 1 字节，
    /// <paramref name="output"/> 长度应为 <c>((width+3)/4)*((height+3)/4)*16</c>。
    /// 超出边界的像素按全 0 处理，与管线一致。
    /// </summary>
    public static void Compress(ReadOnlySpan<byte> rgba, int width, int height, Span<byte> output,
        bool zeroTransparent)
    {
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        int offset = 0;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                CompressBlock(rgba, width, height, bx, by, output.Slice(offset, 16), zeroTransparent);
                offset += 16;
            }
        }
    }

    /// <summary>
    /// 判断一份 DXT3 数据属于哪种写法：全透明块是写成常量（XNA 字体管线）还是全 0
    /// （另一支编码器）。看第一个全透明的块就够了。
    /// </summary>
    public static bool DetectZeroTransparent(ReadOnlySpan<byte> dxt3)
    {
        for (int offset = 0; offset + 16 <= dxt3.Length; offset += 16)
        {
            if (!dxt3.Slice(offset, 8).SequenceEqual(stackalloc byte[8])) // alpha 半边全 0
            {
                continue;
            }

            return dxt3.Slice(offset + 8, 8).SequenceEqual(stackalloc byte[8]);
        }

        return false;
    }

    private static void CompressBlock(ReadOnlySpan<byte> rgba, int width, int height, int bx, int by,
        Span<byte> block, bool zeroTransparent)
    {
        Span<byte> alpha = stackalloc byte[16];
        Span<byte> grey = stackalloc byte[16];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int i = y * 4 + x;
                int px = bx * 4 + x;
                int py = by * 4 + y;
                byte r = 0;
                byte a = 0;
                if (px < width && py < height)
                {
                    int o = 4 * (py * width + px);
                    r = rgba[o];
                    a = rgba[o + 3];
                }

                alpha[i] = Nibble(a);
                grey[i] = Nibble(r);
            }
        }

        // 全透明块的写法有分歧：XNA 字体管线仍写常量端点，另一支编码器写全 0。
        // 两者解码结果都是 (0,0,0,0)，但字节不同，所以按原文件那一支来。
        if (zeroTransparent && !alpha.ContainsAnyExcept((byte)0))
        {
            block.Clear();
            return;
        }

        for (int i = 0; i < 8; i++)
        {
            block[i] = (byte)(alpha[2 * i] | (alpha[2 * i + 1] << 4));
        }

        block[8] = 0xFF;  // c0 = 白
        block[9] = 0xFF;
        block[10] = 0x00; // c1 = 黑
        block[11] = 0x00;

        for (int i = 0; i < 4; i++)
        {
            block[12 + i] = (byte)((GreyToIndex[grey[4 * i + 3] >> 2] << 6)
                | (GreyToIndex[grey[4 * i + 2] >> 2] << 4)
                | (GreyToIndex[grey[4 * i + 1] >> 2] << 2)
                | GreyToIndex[grey[4 * i] >> 2]);
        }
    }

    /// <summary>
    /// 先取 (int)(v/255×15)，再把高 2 位复制到低 2 位，
    /// 于是结果只会是 0000 / 0101 / 1010 / 1111 四种（0/5/10/15）。
    /// </summary>
    private static byte Nibble(byte v)
    {
        int n = (int)(v / 255f * 15.0);
        return (byte)((n & 0xC) | (n >> 2));
    }
}
