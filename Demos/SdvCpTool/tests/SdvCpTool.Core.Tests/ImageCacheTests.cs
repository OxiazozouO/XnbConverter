using SdvCpTool.Core.Workspace;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// <see cref="ImageCache"/> —— 「同一张图只解码一次、改动累积在内存、最后统一写回」。
/// <para>
/// 它同时是**正确性**设施：延迟写模式下，本包刚 <c>Load</c> 进来的图磁盘上还没有，
/// 后面的 <c>EditImage</c> 若直接读盘就会误判成「目标不存在」。
/// </para>
/// </summary>
public class ImageCacheTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvimg_s_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvimg_o_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvimg_p_" + Guid.NewGuid().ToString("N"));

    public ImageCacheTests()
    {
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_pack);
    }

    private OutputTree? _tree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush()
    {
        _tree?.Flush();
    }

    public void Dispose()
    {
        foreach (string d in new[] { _src, _out, _pack })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private string OutPath(string rel) => Path.Combine(_out, rel.Replace('/', Path.DirectorySeparatorChar));

    private void WritePng(string path, int w, int h, int r, int g, int b, int a = 255)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = (byte)r;
            px[i * 4 + 1] = (byte)g;
            px[i * 4 + 2] = (byte)b;
            px[i * 4 + 3] = (byte)a;
        }
        Png.Write(path, px, w, h);
    }

    private void TreePng(string rel, int w, int h, int r, int g, int b, int a = 255) =>
        WritePng(Path.Combine(_src, rel.Replace('/', Path.DirectorySeparatorChar)), w, h, r, g, b, a);

    private void PackPng(string rel, int w, int h, int r, int g, int b, int a = 255) =>
        WritePng(Path.Combine(_pack, rel.Replace('/', Path.DirectorySeparatorChar)), w, h, r, g, b, a);

    private (OutputTree Tree, ImageCache Images) Build()
    {
        var tree = _tree = new OutputTree(_src, _out);
        return (tree, new ImageCache(tree));
    }

    private (byte R, byte G, byte B) PixelAt(string rel, int x, int y)
    {
        byte[] d = Png.Read(OutPath(rel), out int w, out _);
        int i = (y * w + x) * 4;
        return (d[i], d[i + 1], d[i + 2]);
    }

    // ------------------------------------------------------------------ 攒批

    [Fact]
    public void 同一张图被改多次只在落盘时写一次()
    {
        TreePng("Characters/A.png", 4, 1, 0, 0, 0);
        (OutputTree tree, ImageCache images) = Build();

        Assert.True(images.TryGetPixels("Characters/A.png", out byte[] px, out int w, out int h, out _));

        // 三条补丁各改一格
        for (int x = 0; x < 3; x++)
        {
            byte[] next = (byte[])px.Clone();
            next[x * 4] = 100;                     // 只动 R
            images.SetPixels("Characters/A.png", next, w, h);
            px = next;
        }

        // 落盘前磁盘上还是原样（延迟写）
        Assert.Equal((byte)0, PixelAt("Characters/A.png", 0, 0).R);

        List<string> written = images.Flush();
        Assert.Single(written);
        Assert.Equal("Characters/A.png", written[0]);

        Assert.Equal((byte)100, PixelAt("Characters/A.png", 0, 0).R);
        Assert.Equal((byte)100, PixelAt("Characters/A.png", 1, 0).R);
        Assert.Equal((byte)100, PixelAt("Characters/A.png", 2, 0).R);
    }

    [Fact]
    public void 只解码不算改动不落盘()
    {
        TreePng("Characters/A.png", 2, 1, 7, 7, 7);
        (OutputTree _, ImageCache images) = Build();

        Assert.True(images.TryGetPixels("Characters/A.png", out _, out _, out _, out _));

        Assert.Empty(images.Flush());   // 没人改过 ⇒ 一次都不写
    }

    [Fact]
    public void 延迟写下刚Load进来的图后续补丁能读到()
    {
        // 目标不是原版资产 —— 全靠本次运行里那条 Load 造出来，磁盘上现在没有它
        PackPng("assets/Icon.png", 2, 1, 9, 9, 9);
        (OutputTree tree, ImageCache images) = Build();

        Assert.False(File.Exists(OutPath("LooseSprites/Icon.png")));   // 前提：盘上没有

        images.NoteWholeFile("LooseSprites/Icon.png", Path.Combine(_pack, "assets", "Icon.png"));

        Assert.True(images.Exists("LooseSprites/Icon.png"));
        Assert.True(images.TryGetPixels("LooseSprites/Icon.png", out byte[] px, out int w, out int h,
            out string? err), err);
        Assert.Equal(9, px[0]);

        px[1] = 200;                                   // EditImage 改一格
        images.SetPixels("LooseSprites/Icon.png", px, w, h);

        images.Flush();
        Assert.Equal((byte)9, PixelAt("LooseSprites/Icon.png", 0, 0).R);
        Assert.Equal((byte)200, PixelAt("LooseSprites/Icon.png", 0, 0).G);
    }

    [Fact]
    public void Load整份覆盖会丢掉先前累积的改动()
    {
        TreePng("Characters/A.png", 2, 1, 0, 0, 0);
        PackPng("assets/New.png", 2, 1, 50, 0, 0);
        (OutputTree _, ImageCache images) = Build();

        Assert.True(images.TryGetPixels("Characters/A.png", out byte[] px, out int w, out int h, out _));
        px[0] = 99;
        images.SetPixels("Characters/A.png", px, w, h);

        // 后写的 Load 是**整份替换**（CP 的语义），该盖掉上面的改动
        images.NoteWholeFile("Characters/A.png", Path.Combine(_pack, "assets", "New.png"));

        Assert.True(images.TryGetPixels("Characters/A.png", out byte[] after, out _, out _, out _));
        Assert.Equal(50, after[0]);

        images.Flush();
        Assert.Equal((byte)50, PixelAt("Characters/A.png", 0, 0).R);
    }

    /// <summary>
    /// 地图图集被后面的包换成**更矮**的同名副本时，保留原图下半部分。
    /// 链式转换里不同包的地图共用一个 `Maps/&lt;图集&gt;` 键，截断会直接让前面的地图索引越界。
    /// </summary>
    [Fact]
    public void 地图图集被更矮的版本覆盖时保留下半部分()
    {
        TreePng("Maps/sheet.png", 16, 32, 0, 200, 0);     // 旧图：2 行绿
        PackPng("assets/short.png", 16, 16, 200, 0, 0);   // 新图：1 行红
        (OutputTree _, ImageCache images) = Build();

        images.NoteWholeFile("Maps/sheet.png", Path.Combine(_pack, "assets", "short.png"));
        Assert.True(images.TryGetPixels("Maps/sheet.png", out _, out int w, out int h, out _));
        Assert.Equal(16, w);
        Assert.Equal(32, h);                              // 没被截成 16

        images.Flush();
        Assert.Equal((byte)200, PixelAt("Maps/sheet.png", 0, 0).R);   // 顶部用新图
        Assert.Equal(0, PixelAt("Maps/sheet.png", 0, 0).G);
        Assert.Equal((byte)200, PixelAt("Maps/sheet.png", 0, 20).G);  // 下半部分还是旧图
    }

    [Fact]
    public void 同一资产Load两次只拷最后那个来源()
    {
        PackPng("assets/One.png", 1, 1, 1, 1, 1);
        PackPng("assets/Two.png", 1, 1, 2, 2, 2);
        (OutputTree _, ImageCache images) = Build();

        images.NoteWholeFile("LooseSprites/Icon.png", Path.Combine(_pack, "assets", "One.png"));
        images.NoteWholeFile("LooseSprites/Icon.png", Path.Combine(_pack, "assets", "Two.png"));

        List<string> written = images.Flush();
        Assert.Single(written);
        Assert.Equal((byte)2, PixelAt("LooseSprites/Icon.png", 0, 0).R);
    }

    // ------------------------------------------------------------------ 源图解码缓存

    [Fact]
    public void 同一张源图只解码一次()
    {
        PackPng("assets/Overlay.png", 2, 1, 3, 3, 3);
        (OutputTree _, ImageCache images) = Build();
        string src = Path.Combine(_pack, "assets", "Overlay.png");

        for (int i = 0; i < 5; i++)
            Assert.True(images.TryReadSource(src, out byte[] px, out _, out _, out _), "源图读失败");
        Assert.True(images.TryReadSource(src, out byte[] first, out _, out _, out _));

        Assert.Equal(1, images.SourceDecodes);   // 只解码一次
        Assert.Equal(5, images.SourceHits);      // 其余全命中
        Assert.Equal((byte)3, first[0]);
    }

    [Fact]
    public void 源图读不出来时报错而不是抛异常()
    {
        (OutputTree _, ImageCache images) = Build();

        bool ok = images.TryReadSource(Path.Combine(_pack, "没有这个.png"),
            out _, out _, out _, out string? error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void 大小写不同的路径算同一张图()
    {
        // Windows 上 `LooseSprites/Cursors` 与 `loosesprites/cursors` 是同一个文件；
        // 用序号比较会记成两个条目 ⇒ 内存里分叉、盘上写两次、谁后写谁赢。
        TreePng("LooseSprites/Cursors.png", 2, 1, 0, 0, 0);
        (OutputTree _, ImageCache images) = Build();

        Assert.True(images.TryGetPixels("LooseSprites/Cursors.png", out byte[] px, out int w, out int h, out _));
        px[0] = 5;
        images.SetPixels("LooseSprites/Cursors.png", px, w, h);

        Assert.True(images.TryGetPixels("loosesprites/cursors.png", out byte[] again, out _, out _, out _));
        Assert.Equal((byte)5, again[0]);          // 看到的是同一块内存
        Assert.Single(images.Flush());
    }
}
