using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// <c>Png.Write</c> / <c>Png.Read</c> 在**多线程**下必须各自正确。
///
/// <para>
/// **为什么要有这条**：产出阶段是并行落盘的（<c>OutputTree.WriteBatch</c> /
/// <c>ImageCache.Flush</c> 都用 <c>Parallel.ForEach</c>），同一时刻有好几张图在编解码，
/// 而编码器内部从 <c>XnbConverter</c> 的静态 <c>Pool</c>（建在 <c>ArrayPool&lt;T&gt;</c> 上）借缓冲。
/// 「并行落盘」这个前提就靠它撑着。
/// </para>
///
/// <para>
/// 结论（2026-10-10 实测，256 并发 × 6 种形状）：**并发下编解码没问题**。
/// 当时那条偶发的 <c>调色板索引越界</c> 最后查明是**测试夹具**的长度写错了（见
/// <see cref="PngRoundTripTests"/> 的说明），与并发无关。
/// </para>
/// </summary>
public class PngConcurrencyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvpc_" + Guid.NewGuid().ToString("N"));

    public PngConcurrencyTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>造一张 w×h、颜色数可控的图（颜色少 ⇒ 走调色板路径）。</summary>
    private static byte[] Make(int w, int h, int colors)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int c = i % colors;
            px[i * 4] = (byte)(c * 37 % 256);
            px[i * 4 + 1] = (byte)(c * 91 % 256);
            px[i * 4 + 2] = (byte)(c * 13 % 256);
            px[i * 4 + 3] = 255;
        }
        return px;
    }

    [Theory]
    [InlineData(2, 1, 1)]      // **单色**：调色板只有一项、索引必然全 0（实测出过问题的那张）
    [InlineData(2, 1, 2)]      // 最小、两色 ⇒ 1 位深调色板
    [InlineData(5, 3, 2)]      // 宽度不是 8 的倍数 ⇒ 行末要补位
    [InlineData(17, 4, 3)]     // 三色 ⇒ 2 位深
    [InlineData(9, 9, 40)]     // 40 色 ⇒ 8 位深调色板
    [InlineData(8, 8, 300)]    // 超过 256 色 ⇒ 退回 RGBA 路径
    public void 并发编解码每张图都逐字节还原(int w, int h, int colors)
    {
        byte[] expected = Make(w, h, colors);
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 256, i =>
        {
            string path = Path.Combine(_dir, $"t{i}_{w}x{h}_{colors}.png");
            try
            {
                Png.Write(path, expected, w, h);
                byte[] back = Png.Read(path, out int bw, out int bh);
                if (bw != w || bh != h) { failures.Add($"尺寸 {bw}x{bh} ≠ {w}x{h}"); return; }
                if (!back.AsSpan().SequenceEqual(expected)) failures.Add("像素不一致");
            }
            catch (Exception ex) { failures.Add(ex.Message); }
        });

        Assert.Empty(failures);
    }
}
