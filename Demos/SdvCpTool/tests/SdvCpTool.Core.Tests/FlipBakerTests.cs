using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Workspace;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// TMX 的翻转/旋转**烘进图集**（<see cref="FlipBaker"/>）。
/// <para>
/// 为什么必须烘：xTile 只按 <c>TileIndex</c> 取格子、不认任何翻转；SMAPI 的
/// <c>SDisplayDevice</c> 才会读 TMXTile 的 <c>@Rotation</c>/<c>@Flip</c> 并画出来。
/// 纯 XNB 没有 SMAPI ⇒ 只能把变换后的像素作为**新格**追加进图集，再改地图的索引。
/// </para>
/// </summary>
public class FlipBakerTests : IDisposable
{
    private const int Size = 16;

    private readonly string _in = Path.Combine(Path.GetTempPath(), "sdvflip_in_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvflip_out_" + Guid.NewGuid().ToString("N"));

    public FlipBakerTests()
    {
        Directory.CreateDirectory(Path.Combine(_in, "Maps"));
    }

    public void Dispose()
    {
        foreach (string d in new[] { _in, _out })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    /// <summary>两格图：格 0 左上角是 4 个不同颜色的像素，格 1 全灰。</summary>
    private void WriteSource(string name, byte r = 200)
    {
        var px = new byte[Size * 2 * Size * 4];
        void Set(int cell, int x, int y, byte cr, byte cg, byte cb)
        {
            int i = ((y * (Size * 2)) + cell * Size + x) * 4;
            px[i] = cr; px[i + 1] = cg; px[i + 2] = cb; px[i + 3] = 255;
        }
        // 格 0 铺一层底色（四季各不相同），再把 4 个标记像素压在左上角。
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                Set(0, x, y, r, r, r);
                Set(1, x, y, r, r, r);
            }
        Set(0, 0, 0, 255, 0, 0);       // 红（左上）
        Set(0, 1, 0, 0, 255, 0);       // 绿（右上）
        Set(0, 0, 1, 0, 0, 255);       // 蓝（左下）
        Set(0, 1, 1, 255, 255, 255);   // 白（右下）

        Png.Write(Path.Combine(_in, "Maps", name + ".png"), px, Size * 2, Size);
    }

    /// <summary>只有一格宽的图（用来模拟比基础图更短的语言变体）。</summary>
    private void WriteSingleCell(string name, byte r)
    {
        var px = new byte[Size * Size * 4];
        for (int i = 0; i < Size * Size; i++)
        {
            px[i * 4] = r; px[i * 4 + 1] = r; px[i * 4 + 2] = r; px[i * 4 + 3] = 255;
        }
        Png.Write(Path.Combine(_in, "Maps", name + ".png"), px, Size, Size);
    }

    private static TBin10 MapWithMarker(string image, string marker, int sourceIndex = 0)
    {
        var sheet = new TileSheet
        {
            Id = "sheet",
            Image = image,
            TileSize = new IntVector2 { X = Size, Y = Size },
            Properties = new List<Propertie>
            {
                new() { Key = "@TileIndex@0@Passable", Type = 3, Value = "T" },
            },
        };
        return new TBin10
        {
            TileSheets = new List<TileSheet> { sheet },
            Layers = new List<Layer>
            {
                new()
                {
                    Id = "Back",
                    LayerSize = new IntVector2 { X = 1, Y = 1 },
                    TileSize = new IntVector2 { X = Size, Y = Size },
                    Tiles = new List<BaseTile>
                    {
                        new StaticTile
                        {
                            TileSheet = "sheet",
                            TileIndex = sourceIndex,
                            Properties = new List<Propertie>
                            {
                                new() { Key = TmxToTbin.FlipProperty, Type = 3, Value = marker },
                            },
                        },
                    },
                },
            },
        };
    }

    private ImageCache Images() => new(new OutputTree(_in, _out));

    private static (byte R, byte G, byte B) Pixel(string path, int cell, int x, int y)
    {
        byte[] px = Png.Read(path, out int w, out _);
        int columns = w / Size;
        int cx = (cell % columns) * Size;
        int cy = (cell / columns) * Size;
        int i = ((cy + y) * w + cx + x) * 4;
        return (px[i], px[i + 1], px[i + 2]);
    }

    [Fact]
    public void 横翻后新格是镜像且地图指向新索引()
    {
        WriteSource("Test");
        var images = Images();
        TBin10 map = MapWithMarker("Test", "0,1");   // H 翻

        int baked = new FlipBaker(images).Bake(map);
        images.Flush();

        Assert.Equal(1, baked);
        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.Equal(2, tile.TileIndex);                                // 两格图后面追加的新格
        Assert.DoesNotContain(tile.Properties, p => p.Key == TmxToTbin.FlipProperty);

        string png = Path.Combine(_out, "Maps", "Test.png");
        Assert.Equal((255, 0, 0), Pixel(png, 2, 15, 0));                // 左上红 → 右上
        Assert.Equal((0, 255, 0), Pixel(png, 2, 14, 0));                // 右上绿 → 左上
        Assert.Equal((0, 0, 255), Pixel(png, 2, 15, 1));
        Assert.Equal((255, 255, 255), Pixel(png, 2, 14, 1));

        // 原格不动
        Assert.Equal((255, 0, 0), Pixel(png, 0, 0, 0));

        // 逐块属性复制到新索引（否则翻转后的格子失去通行性）
        Assert.Equal("T", map.TileSheets[0].Properties.First(p => p.Key == "@TileIndex@2@Passable").Value);
    }

    [Fact]
    public void 四季图集同一位次各追加自己那一格的变换()
    {
        WriteSource("spring_Test", 10);
        WriteSource("summer_Test", 20);
        var images = Images();
        TBin10 map = MapWithMarker("spring_Test", "0,2");   // 竖翻

        Assert.Equal(1, new FlipBaker(images).Bake(map));
        images.Flush();

        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.Equal(2, tile.TileIndex);

        // 春天：竖翻把 (0,0) 的红送到 (0,15)，(0,1) 的蓝送到 (0,14)
        string spring = Path.Combine(_out, "Maps", "spring_Test.png");
        Assert.Equal((255, 0, 0), Pixel(spring, 2, 0, 15));
        Assert.Equal((0, 0, 255), Pixel(spring, 2, 0, 14));

        // 夏天用**它自己**那张图的内容（灰）变换，而不是春天的颜色
        string summer = Path.Combine(_out, "Maps", "summer_Test.png");
        Assert.Equal((20, 20, 20), Pixel(summer, 2, 0, 0));
        Assert.Equal((20, 20, 20), Pixel(summer, 2, 15, 15));
    }

    /// <summary>
    /// ★ 语言变体也要追加**同一个索引**：中文存档加载 `spring_X.zh-CN`，
    /// 不追加的话地图指向的新索引在那张图里不存在（图块直接不画）。
    /// </summary>
    [Fact]
    public void 中文语言变体也追加同一位次()
    {
        WriteSource("spring_Test", 10);
        WriteSource("spring_Test.zh-CN", 30);
        var images = Images();
        TBin10 map = MapWithMarker("spring_Test", "0,1");   // H 翻

        Assert.Equal(1, new FlipBaker(images).Bake(map));
        images.Flush();

        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.Equal(2, tile.TileIndex);

        string zh = Path.Combine(_out, "Maps", "spring_Test.zh-CN.png");
        Assert.Equal((255, 0, 0), Pixel(zh, 2, 15, 0));       // 中文那张自己的红块翻到右上
        Assert.Equal((30, 30, 30), Pixel(zh, 2, 15, 15));     // 底色来自中文那张，不是春天那张
    }

    /// <summary>
    /// 语言变体比基础图短、源格在它里面越界时，仍要把**同一新索引**补出来。
    /// 以前的实现只要有一个变体写成功就算成功：中文图缺格，游戏加载后就是整块黑洞。
    /// </summary>
    [Fact]
    public void 语言变体缺这一格时用基础图像素补齐而不是留黑洞()
    {
        WriteSource("spring_Test", 10);              // 两格
        WriteSingleCell("spring_Test.zh-CN", 30);    // 只有一格
        var images = Images();
        TBin10 map = MapWithMarker("spring_Test", "0,1", sourceIndex: 1);

        Assert.Equal(1, new FlipBaker(images).Bake(map));
        images.Flush();

        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.Equal(2, tile.TileIndex);

        // 中文图原本只有 1 格 × 1 行，新索引 2 在它自己的列数下是第 3 行。
        string zh = Path.Combine(_out, "Maps", "spring_Test.zh-CN.png");
        Assert.Equal((10, 10, 10), Pixel(zh, 2, 0, 0));   // 用的是基础图（春天）那一格的像素，不是透明/黑色
        Assert.Equal((10, 10, 10), Pixel(zh, 2, 15, 15));
    }

    [Fact]
    public void 没有标记时不动图()
    {
        WriteSource("Test");
        var images = Images();
        TBin10 map = MapWithMarker("Test", "0,0");
        map.Layers[0].Tiles[0]!.Properties.Clear();     // 去掉标记
        map.Layers[0].Tiles[0] = new StaticTile { TileSheet = "sheet", TileIndex = 0, Properties = new() };

        Assert.False(FlipBaker.HasMarkers(map));
        Assert.Equal(0, new FlipBaker(images).Bake(map));
        Assert.Empty(images.Flush());
    }

    [Fact]
    public void 图片不在树里时保留原索引并留说明()
    {
        var images = Images();
        TBin10 map = MapWithMarker("Missing", "0,1");
        var baker = new FlipBaker(images);

        Assert.Equal(0, baker.Bake(map));
        Assert.Equal(0, ((StaticTile)map.Layers[0].Tiles[0]!).TileIndex);
        Assert.DoesNotContain(((StaticTile)map.Layers[0].Tiles[0]!).Properties,
            p => p.Key == TmxToTbin.FlipProperty);       // 标记仍要清掉
        Assert.NotEmpty(baker.Notes);
    }
}
