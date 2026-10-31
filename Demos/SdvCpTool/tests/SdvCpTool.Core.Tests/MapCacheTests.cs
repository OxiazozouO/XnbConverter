using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Workspace;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// <see cref="MapCache"/> —— 地图的「解析一次、改在内存、最后序列化一次」，
/// 外加两处**只读**缓存：源地图解码、全树图集引用扫描。
/// </summary>
public class MapCacheTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvmc_s_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvmc_o_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvmc_p_" + Guid.NewGuid().ToString("N"));

    public MapCacheTests()
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

    /// <summary>拿 fixture 里的真地图，把首图集指到一张好认的图上再落盘。</summary>
    private static void SaveMap(string fullPath, string sheetImage = "Maps/spring_town")
    {
        byte[] chunk = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        TBin10 map = (TBin10)TBin10Reader.Create(chunk).Read();
        map.TileSheets[0].Image = sheetImage;
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, fullPath);
    }

    private void TreeMap(string rel) => SaveMap(Path.Combine(_src, rel.Replace('/', Path.DirectorySeparatorChar)));

    private void PackMap(string rel) => SaveMap(Path.Combine(_pack, rel.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>序列化时确实会写出的静态图块位置（Index 里是 'S' 而不是 'N'）。</summary>
    private static (Layer? Layer, int Pos, string Sheet) FirstStaticTile(TBin10 map)
    {
        foreach (Layer layer in map.Layers)
        {
            int pos = layer.Tiles.FindIndex(t => t is StaticTile);
            if (pos >= 0)
                return (layer, pos, ((StaticTile)layer.Tiles[pos]!).TileSheet);
        }
        return (null, -1, "");
    }

    private (OutputTree Tree, MapCache Maps) Build()
    {
        var tree = _tree = new OutputTree(_src, _out);
        return (tree, new MapCache(tree));
    }

    // ------------------------------------------------------------------ 只读不写

    [Fact]
    public void 只读过没改过的地图不写回()
    {
        TreeMap("Maps/A.tbin");
        (OutputTree _, MapCache maps) = Build();          // 建树时整树拷过去
        byte[] before = File.ReadAllBytes(OutPath("Maps/A.tbin"));

        Assert.NotNull(maps.Get("Maps/A.tbin", before, out _));

        // 没标脏 ⇒ 一次都不写：把没改过的 tbin 重新序列化会写出与原文件不逐字节相同的内容
        Assert.Empty(maps.Flush());
        Assert.Equal(before, File.ReadAllBytes(OutPath("Maps/A.tbin")));
    }

    [Fact]
    public void 标脏之后才写回()
    {
        TreeMap("Maps/A.tbin");
        (OutputTree _, MapCache maps) = Build();

        (TBin10Reader reader, TBin10 map) = maps.Get(
            "Maps/A.tbin", File.ReadAllBytes(OutPath("Maps/A.tbin")), out _)!.Value;
        map.Properties ??= new List<Propertie>();
        map.Properties.Add(new Propertie { Key = "K", Type = 3, Value = "V" });   // Type 3 = 字符串
        maps.MarkModified("Maps/A.tbin");

        List<string> written = maps.Flush();
        Assert.Single(written);
        Assert.Equal("Maps/A.tbin", written[0]);

        TBin10 after = TBin10Reader.FormFile(OutPath("Maps/A.tbin"));
        Assert.Contains(after.Properties, p => p.Key == "K" && (string?)p.Value == "V");
        _ = reader;
    }

    /// <summary>
    /// 回归：图集引用按地图目录解析后落到不存在的路径时，落盘前必须改成**真实存在**的图。
    /// 实测 `Maps/ArchaeologyHouse` 里写着 `TileSheets/furniture`，游戏去找
    /// `Maps/TileSheets/furniture.xnb` ⇒ 整张博物馆建不起来（实机 SMAPI 日志）。
    /// </summary>
    [Fact]
    public void 落盘前修正指向不存在路径的图集引用()
    {
        SaveMap(Path.Combine(_src, "Maps", "ArchaeologyHouse.tbin"), sheetImage: "TileSheets/furniture");
        (OutputTree tree, MapCache maps) = Build();
        // 图集在产出树里（真实转换时由 ImageApplier 落进来），不是只在输入树
        tree.StageBytes("TileSheets/furniture.png", new byte[] { 1, 2, 3 });

        (TBin10Reader _, TBin10 map) = maps.Get(
            "Maps/ArchaeologyHouse.tbin",
            File.ReadAllBytes(OutPath("Maps/ArchaeologyHouse.tbin")), out _)!.Value;
        maps.MarkModified("Maps/ArchaeologyHouse.tbin");

        Assert.Single(maps.Flush());

        TBin10 after = TBin10Reader.FormFile(OutPath("Maps/ArchaeologyHouse.tbin"));
        Assert.Contains(after.TileSheets, s => s.Image == "../TileSheets/furniture");
    }

    /// <summary>
    /// 缓存键是大小写不敏感的，但**落盘用的名字跟随「第一次创建它的那次写」** ——
    /// Windows 上往已存在的文件里写**不会**改名字，所以后面那条大写 <c>EditMap</c> 改不了目录项。
    /// <para>
    /// 实测 SVE：先 <c>Load Maps/Beach-Nightmarket</c>、后 <c>EditMap Maps/Beach-NightMarket</c>，
    /// 产出树里的目录项是前者（小写 m）。文件还没落盘时，那条 <c>Load</c> 在产出树暂存里 ——
    /// 名字要沿用它的写法。
    /// </para>
    /// </summary>
    [Fact]
    public void 落盘名字沿用第一次创建它的那次写()
    {
        TreeMap("Maps/A.tbin");
        (OutputTree tree, MapCache maps) = Build();

        // 先 Load（暂存里写的是小写），再 EditMap（大写）
        tree.StageBytes("Maps/beach.tbin", File.ReadAllBytes(OutPath("Maps/A.tbin")));
        Assert.True(maps.TryGetModel("Maps/Beach.tbin", () => tree.ReadBytesOrNull("Maps/Beach.tbin"),
            out _, out _, out string? err), err);
        maps.MarkModified("Maps/Beach.tbin");

        Assert.Single(maps.Flush());
        Assert.Contains("beach.tbin",
            Directory.GetFiles(Path.Combine(_out, "Maps")).Select(Path.GetFileName));
    }

    /// <summary>
    /// 回归：翻转烘焙把新格追加到**内存**图集之后，磁盘 PNG 还是旧尺寸。
    /// 此时若按磁盘算 SheetSize，游戏会把新索引判成越界 ⇒ 实机整块黑洞。
    /// </summary>
    [Fact]
    public void 翻转追加后按内存里的新尺寸写回SheetSize()
    {
        Directory.CreateDirectory(Path.Combine(_src, "Maps"));
        var px = new byte[32 * 16 * 4];
        for (int i = 0; i < 32 * 16; i++) px[i * 4 + 3] = 255;
        Png.Write(Path.Combine(_src, "Maps", "sheet.png"), px, 32, 16);

        // 用真 tbin 当骨架（手搓的 TBin10 缺少序列化必需字段）。
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"),
            Path.Combine(_src, "Maps", "Flip.tbin"));

        var tree = _tree = new OutputTree(_src, _out);
        var images = new ImageCache(tree);
        var maps = new MapCache(tree) { Images = images };
        (TBin10Reader _, TBin10 map) = maps.Get(
            "Maps/Flip.tbin", File.ReadAllBytes(OutPath("Maps/Flip.tbin")), out _)!.Value;

        // 骨架里的一个静态图块所属图集指向一张两格图，再给它加翻转标记。
        (Layer? tileLayer, int tilePos, string sheetId) = FirstStaticTile(map);
        Assert.True(tileLayer != null && tilePos >= 0, "夹具里没有静态图块");
        TileSheet sheet = map.TileSheets.First(s => s.Id == sheetId);
        sheet.Image = "sheet";
        sheet.TileSize = new IntVector2 { X = 16, Y = 16 };
        sheet.SheetSize = new IntVector2 { X = 2, Y = 1 };
        tileLayer!.Tiles[tilePos] = new StaticTile
        {
            TileSheet = sheetId,
            TileIndex = 0,
            Properties = new List<Propertie>
            {
                new() { Key = TmxToTbin.FlipProperty, Type = 3, Value = "0,1" },
            },
        };
        maps.MarkModified("Maps/Flip.tbin");

        Assert.Single(maps.Flush());          // 翻转烘焙在这里做
        Assert.True(maps.FlipBaked > 0, $"FlipBaked={maps.FlipBaked}");
        Assert.True(images.TryGetSize("Maps/sheet.png", out int memW, out int memH), "内存图集读不到");
        Assert.Equal(32, memW);                 // 两列不变
        Assert.Equal(32, memH);                 // 追加第 3 格 ⇒ 多一行
        images.Flush();                        // 新格落盘

        TBin10 after = TBin10Reader.FormFile(OutPath("Maps/Flip.tbin"));
        TileSheet afterSheet = after.TileSheets.First(s => s.Id == sheetId);
        Assert.Equal(2, afterSheet.SheetSize!.Value.X);   // 2 列
        Assert.Equal(2, afterSheet.SheetSize!.Value.Y);   // 原 1 行 + 追加 1 行

        byte[] png = Png.Read(OutPath("Maps/sheet.png"), out int w, out _);
        Assert.Equal(32, w);
        Assert.NotEqual(0, png[(16 * 32 + 0) * 4 + 3]);             // 新格有像素
    }

    /// <summary>
    /// 链式转换：单包落盘时只保留标记，等所有包跑完后 `FlipBakePass` 统一烘一次。
    /// </summary>
    [Fact]
    public void 延迟烘焙保留标记并由全树烘焙统一处理()
    {
        Directory.CreateDirectory(Path.Combine(_src, "Maps"));
        var px = new byte[32 * 16 * 4];
        for (int i = 0; i < 32 * 16; i++) px[i * 4 + 3] = 255;
        Png.Write(Path.Combine(_src, "Maps", "sheet.png"), px, 32, 16);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"),
            Path.Combine(_src, "Maps", "Flip.tbin"));

        var tree = _tree = new OutputTree(_src, _out);
        var images = new ImageCache(tree);
        var maps = new MapCache(tree) { Images = images, DeferFlips = true };
        (TBin10Reader _, TBin10 map) = maps.Get(
            "Maps/Flip.tbin", File.ReadAllBytes(OutPath("Maps/Flip.tbin")), out _)!.Value;
        (Layer? tileLayer, int tilePos, string sheetId) = FirstStaticTile(map);
        Assert.True(tileLayer != null && tilePos >= 0, "夹具里没有静态图块");
        TileSheet sheet = map.TileSheets.First(s => s.Id == sheetId);
        sheet.Image = "sheet";
        sheet.TileSize = new IntVector2 { X = 16, Y = 16 };
        sheet.SheetSize = new IntVector2 { X = 2, Y = 1 };
        tileLayer!.Tiles[tilePos] = new StaticTile
        {
            TileSheet = sheetId,
            TileIndex = 0,
            Properties = new List<Propertie>
            {
                new() { Key = TmxToTbin.FlipProperty, Type = 3, Value = "0,1" },
            },
        };
        maps.MarkModified("Maps/Flip.tbin");

        Assert.Single(maps.Flush());
        TBin10 deferred = TBin10Reader.FormFile(OutPath("Maps/Flip.tbin"));
        Layer deferredLayer = deferred.Layers.First(l => l.Id == tileLayer.Id);
        var deferredTile = (StaticTile)deferredLayer.Tiles[tilePos]!;
        Assert.Contains(deferredTile.Properties, p => p.Key == TmxToTbin.FlipProperty);
        Assert.Equal(0, deferredTile.TileIndex);

        (int mapCount, int tileCount) = FlipBakePass.Run(_out);
        Assert.Equal(1, mapCount);
        Assert.Equal(1, tileCount);

        TBin10 after = TBin10Reader.FormFile(OutPath("Maps/Flip.tbin"));
        Layer afterLayer = after.Layers.First(l => l.Id == tileLayer.Id);
        var tile = (StaticTile)afterLayer.Tiles[tilePos]!;
        Assert.DoesNotContain(tile.Properties, p => p.Key == TmxToTbin.FlipProperty);
        Assert.Equal(2, tile.TileIndex);
        Assert.Equal(2, after.TileSheets.First(s => s.Id == sheetId).SheetSize!.Value.Y);
    }

    [Fact]
    public void 同一张地图取多次拿到同一个对象()
    {
        TreeMap("Maps/A.tbin");
        (OutputTree _, MapCache maps) = Build();
        byte[] bytes = File.ReadAllBytes(OutPath("Maps/A.tbin"));

        (TBin10Reader _, TBin10 first) = maps.Get("Maps/A.tbin", bytes, out _)!.Value;
        (TBin10Reader _, TBin10 second) = maps.Get("Maps/A.tbin", bytes, out _)!.Value;

        Assert.Same(first, second);
    }

    // ------------------------------------------------------------------ 源地图解码缓存

    [Fact]
    public void 同一张源地图只解码一次()
    {
        PackMap("assets/Source.tbin");
        (OutputTree _, MapCache maps) = Build();
        string src = Path.Combine(_pack, "assets", "Source.tbin");

        for (int i = 0; i < 4; i++)
            Assert.True(maps.TryGetSource(src, () => TBin10Reader.FormFile(src), out _, out _));

        Assert.Equal(1, maps.SourceDecodes);
        Assert.Equal(3, maps.SourceHits);
    }

    [Fact]
    public void 源地图读失败时报出原因而不是抛异常()
    {
        (OutputTree _, MapCache maps) = Build();

        bool ok = maps.TryGetSource("Z:/不存在.tbin",
            () => throw new IOException("读不了"), out _, out string? error);

        Assert.False(ok);
        Assert.Contains("读不了", error);
    }

    // ------------------------------------------------------------------ 图集引用索引

    [Fact]
    public void 图集引用索引一次扫出全部并在第二次直接复用()
    {
        TreeMap("Maps/A.tbin");
        TreeMap("Maps/B.tbin");
        (OutputTree tree, MapCache maps) = Build();

        var index = maps.TileSheetRefIndex(tree, s => s?.Trim() ?? "");

        Assert.True(index.ContainsKey("Maps/spring_town"));
        Assert.Equal(2, index["Maps/spring_town"].Count);

        // 再问一次：不重新扫（把磁盘上的地图删了也照样给出来）
        Directory.Delete(Path.Combine(_out, "Maps"), recursive: true);
        Assert.Same(index, maps.TileSheetRefIndex(tree, s => s?.Trim() ?? ""));
    }
}
