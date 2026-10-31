namespace SdvCpTool.Core.Tracing;

/// <summary>
/// 16×16 图块的抗轻微变化指纹：4×4 个 4×4 像素块，每块 RGB 各量化到 4 bit。
/// 不用精确像素哈希 —— 截图里的角色/动画/1px 相位差会把精确哈希全部打散。
/// </summary>
public static class TileFingerprint
{
    public const int Tile = 16;

    public static ulong Compute(ReadOnlySpan<byte> rgba, int width, int height, int x0, int y0)
    {
        ulong hash = 1469598103934665603UL;
        for (int by = 0; by < 4; by++)
        {
            for (int bx = 0; bx < 4; bx++)
            {
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (int y = 0; y < 4; y++)
                {
                    int py = y0 + by * 4 + y;
                    if (py < 0 || py >= height) continue;
                    for (int x = 0; x < 4; x++)
                    {
                        int px = x0 + bx * 4 + x;
                        if (px < 0 || px >= width) continue;
                        int i = (py * width + px) * 4;
                        r += rgba[i];
                        g += rgba[i + 1];
                        b += rgba[i + 2];
                        a += rgba[i + 3];
                        n++;
                    }
                }
                if (n == 0) n = 1;
                int qr = (r / n) >> 4;      // 0..15
                int qg = (g / n) >> 4;
                int qb = (b / n) >> 4;
                int qa = ((a / n) >= 128) ? 1 : 0;
                uint packed = (uint)((qr << 9) | (qg << 5) | (qb << 1) | qa);
                hash ^= packed;
                hash *= 1099511628211UL;
            }
        }
        return hash;
    }
}
