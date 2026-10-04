// 直接照抄 MonoGame 的 Lz4DecoderStream 算法，作为 LZ4PCL 的对照实现。
// 来源: D:\MonoGame-develop\MonoGame.Framework\Utilities\Lz4Stream\Lz4DecoderStream.cs
public static class Lz4Reference
{
    /// <summary>从 src[srcOffset..] 解压 dstLength 字节到 dst[dstOffset..]。返回实际写入长度。</summary>
    public static int Decode(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
    {
        int srcPos = srcOffset;
        int srcEnd = srcOffset + srcLength;
        int dstPos = dstOffset;
        int dstEnd = dstOffset + dstLength;

        while (true)
        {
            if (srcPos >= srcEnd) break;
            int token = src[srcPos++];

            // 字面量
            int litLen = token >> 4;
            if (litLen == 15)
            {
                int b;
                do { b = src[srcPos++]; litLen += b; } while (b == 255);
            }

            for (int i = 0; i < litLen; i++)
            {
                if (dstPos >= dstEnd) return dstPos - dstOffset;
                dst[dstPos++] = src[srcPos++];
            }

            if (dstPos >= dstEnd) break;

            // 偏移
            int offset = src[srcPos] | (src[srcPos + 1] << 8);
            srcPos += 2;

            int matLen = (token & 0xF) + 4;
            if ((token & 0xF) == 15)
            {
                int b;
                do { b = src[srcPos++]; matLen += b; } while (b == 255);
            }

            int from = dstPos - offset;
            for (int i = 0; i < matLen; i++)
            {
                if (dstPos >= dstEnd) return dstPos - dstOffset;
                dst[dstPos++] = dst[from++];
            }
        }

        return dstPos - dstOffset;
    }
}
