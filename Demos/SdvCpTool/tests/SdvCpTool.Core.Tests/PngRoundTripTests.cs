using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// PNG 编解码的**往返**钉子：写出去、马上读回来，必须逐字节相同。
///
/// <para>
/// 它是被一次**偶发失败**逼出来的：`ImageCache.TryGetPixels` 报
/// <c>PNG 读取失败：Invalid PNG data: 调色板索引越界</c> —— 读的是同一条线程刚写下的那张图
/// （2×1、单色）。单色图的索引必然全是 0，本来不该越界。
/// </para>
///
/// <para>
/// 真因不在编解码器，而在**夹具自己**：那张图是用
/// <c>Pack(rel, Px(0,0,0,255), width: 2, height: 1)</c> 写出来的 ——
/// 只给了一个像素（4 字节）却声明 2×1。`Png.Write` 从池里借的缓冲比用到的长，
/// 多出来那一位读到的是**上次用剩的残留**：碰巧是 0 就过、是 1 就报「索引越界」，
/// 于是它时灵时不灵。夹具的长度断言（见 `EditImageApplierTests.Tree/Pack`）才是真正的补丁。
/// </para>
///
/// <para>这条测试留着：它钉的是「写出去的东西读得回来」，与上面那个夹具 bug 无关。</para>
/// </summary>
public class PngRoundTripTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvprt_" + Guid.NewGuid().ToString("N"));

    public PngRoundTripTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Solid(int w, int h, int r, int g, int b, int a = 255)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = (byte)r;
            px[i * 4 + 1] = (byte)g;
            px[i * 4 + 2] = (byte)b;
            px[i * 4 + 3] = (byte)a;
        }
        return px;
    }

    /// <summary>出事的那张：2×1、**单色**（调色板只有一项，索引必然全 0）。</summary>
    [Fact]
    public void 单色小图反复写读都还原()
    {
        byte[] expected = Solid(2, 1, 0, 0, 0);

        for (int i = 0; i < 500; i++)
        {
            string path = Path.Combine(_dir, "Base.png");   // 刻意**复用同一个路径**：覆盖写
            Png.Write(path, expected, 2, 1);

            byte[] back = Png.Read(path, out int w, out int h);
            Assert.Equal(2, w);
            Assert.Equal(1, h);
            Assert.Equal(expected, back);
        }
    }

    /// <summary>两色 2×1（调色板两项 + 1 位深），与上面那条交替写。</summary>
    [Fact]
    public void 两色小图反复写读都还原()
    {
        byte[] expected = { 10, 20, 30, 255, 0, 0, 0, 255 };

        for (int i = 0; i < 500; i++)
        {
            string path = Path.Combine(_dir, "Overlay.png");
            Png.Write(path, expected, 2, 1);

            byte[] back = Png.Read(path, out int w, out int h);
            Assert.Equal(2, w);
            Assert.Equal(1, h);
            Assert.Equal(expected, back);
        }
    }
}
