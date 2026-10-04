using System.Text;

public static class Lz4Trace
{
    /// <summary>逐 token 解码，打印每个 token 覆盖的输出区间，用来定位某些字节究竟来自字面量还是匹配。</summary>
    public static void Run(byte[] src, int srcOffset, int srcLength, int outLength, int fromOut, int toOut)
    {
        int srcPos = srcOffset;
        int srcEnd = srcOffset + srcLength;
        int dstPos = 0;
        byte[] dst = new byte[outLength];

        Console.WriteLine($"    {"输出区间",-16}{"来源",-8}{"字面量@源",-14}匹配(偏移,长度)");
        while (srcPos < srcEnd && dstPos < outLength)
        {
            int tokenAt = srcPos;
            int token = src[srcPos++];

            int litLen = token >> 4;
            if (litLen == 15)
            {
                int b;
                do { b = src[srcPos++]; litLen += b; } while (b == 255);
            }

            int litStart = srcPos;
            int litOutStart = dstPos;
            for (int i = 0; i < litLen && dstPos < outLength; i++)
            {
                dst[dstPos++] = src[srcPos++];
            }

            if (dstPos >= outLength) { Report(litOutStart, dstPos, "字面量", tokenAt, litStart, 0, 0, fromOut, toOut); break; }

            int offset = src[srcPos] | (src[srcPos + 1] << 8);
            srcPos += 2;

            int matLen = (token & 0xF) + 4;
            if ((token & 0xF) == 15)
            {
                int b;
                do { b = src[srcPos++]; matLen += b; } while (b == 255);
            }

            int matOutStart = dstPos;
            int from = dstPos - offset;
            for (int i = 0; i < matLen && dstPos < outLength; i++)
            {
                dst[dstPos++] = dst[from++];
            }

            Report(litOutStart, litOutStart + litLen, "字面量", tokenAt, litStart, 0, 0, fromOut, toOut);
            Report(matOutStart, matOutStart + matLen, "匹配", tokenAt, 0, offset, matLen, fromOut, toOut);
        }
    }

    private static void Report(int outStart, int outEnd, string kind, int tokenAt, int litStart, int offset, int matLen,
        int fromOut, int toOut)
    {
        if (outEnd <= fromOut || outStart >= toOut)
        {
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append($"    [0x{outStart:X4},0x{outEnd:X4})  {kind,-8}");
        if (kind == "字面量")
        {
            sb.Append($"源 0x{litStart:X4}        ");
        }
        else
        {
            sb.Append($"{"",-14}偏移={offset,6} 长度={matLen}");
        }

        sb.Append($"   (token@源 0x{tokenAt:X4})");
        Console.WriteLine(sb.ToString());
    }
}
