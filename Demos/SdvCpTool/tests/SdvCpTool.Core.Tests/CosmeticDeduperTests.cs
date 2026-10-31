using SdvCpTool.Core.Frameworks;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 纯变色判据的边界：整图换色算重复，但小面积局部变化（换窗帘/书脊/花色）不算。
/// </summary>
public class CosmeticDeduperTests
{
    private const int W = 32, H = 32;

    [Fact]
    public void 整图换色算纯变色()
    {
        var white = Solid(255, 255, 255);
        var gray = Solid(230, 230, 230);
        var a = CosmeticDeduper.Fingerprint.FromPixels(white, W, H, 0, 0, W, H);
        var b = CosmeticDeduper.Fingerprint.FromPixels(gray, W, H, 0, 0, W, H);

        Assert.True(a.PureRecolor(b, tolerance: 18, consistency: 2.0));
    }

    [Fact]
    public void 小面积深色补丁不算纯变色()
    {
        var white = Solid(255, 255, 255);
        var patched = Solid(230, 230, 230);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                int i = (y * W + x) * 4;
                patched[i] = patched[i + 1] = patched[i + 2] = 0;
            }

        var a = CosmeticDeduper.Fingerprint.FromPixels(white, W, H, 0, 0, W, H);
        var b = CosmeticDeduper.Fingerprint.FromPixels(patched, W, H, 0, 0, W, H);

        // 平均一致性只有约 1.6%，但单格偏移远超上限：不能当成整图换色。
        Assert.False(a.PureRecolor(b, tolerance: 18, consistency: 2.0));
    }

    private static byte[] Solid(byte r, byte g, byte b)
    {
        var data = new byte[W * H * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = r;
            data[i + 1] = g;
            data[i + 2] = b;
            data[i + 3] = 255;
        }
        return data;
    }
}
