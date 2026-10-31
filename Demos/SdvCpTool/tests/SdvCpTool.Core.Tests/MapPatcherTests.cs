using SdvCpTool.Core.Maps;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// SMAPI <c>PatchMap</c> / <c>ExtendMap</c> 的移植。
/// <para>
/// 夹具是**真实** tbin（<c>Maps/Island_House_Bin.tbin</c>）—— 合成的小地图证明不了
/// 「真文件能读、能改、能写回」。
/// </para>
/// </summary>
public class MapPatcherTests
{
    private static TBin10 Fixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin");
        return TBin10Reader.FormFile(path);
    }

    private static TBin10 Clone(TBin10 m) => TBin10Reader.FormFile(Write(m));

    private static string Write(TBin10 m)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "sdvmp_" + Guid.NewGuid().ToString("N") + ".tbin");
        TBin10Reader.Create(Array.Empty<byte>()).Save(m, tmp);
        return tmp;
    }

    private static Layer FirstLayer(TBin10 m) => m.Layers[0];

    /// <summary>把整层填成指定图集 + 索引，用来构造"源地图"。</summary>
    private static void Fill(Layer layer, string sheet, int index)
    {
        for (int i = 0; i < layer.Tiles.Count; i++)
            layer.Tiles[i] = new StaticTile
            {
                TileSheet = sheet, TileIndex = index, BlendMode = 0, Properties = new List<Propertie>(),
            };
        LayerRle.Rebuild(layer);
    }

    private static string SheetOf(BaseTile? t) => t is StaticTile s ? s.TileSheet : "";

    /// <summary>把某层铺满指定图集/索引（副本上做，不影响夹具）。</summary>
    private static void FillNative(Layer layer, string sheet, int index) => Fill(layer, sheet, index);

    // ── 默认 PatchMode ───────────────────────────────────────────────────

    /// <summary>
    /// CP 的 <c>EditMap</c> 缺省 <c>PatchMode</c> 是 <b>ReplaceByLayer</b>
    /// （<c>PatchLoader.cs:594</c> 的 <c>(PatchMapMode)1</c>，而枚举序是 Overlay=0/ReplaceByLayer=1/Replace=2），
    /// <b>不是</b> SMAPI 签名上的 <c>Overlay</c>。差这一步就会静默写出错地图。
    /// </summary>
    [Fact]
    public void 默认PatchMode是ReplaceByLayer()
    {
        Assert.Equal(1, (int)MapPatchMode.ReplaceByLayer);
        Assert.Equal(0, (int)MapPatchMode.Overlay);
        Assert.Equal(2, (int)MapPatchMode.Replace);
    }

    // ── 三种模式的区别 ───────────────────────────────────────────────────

    /// <summary>Overlay：源里**为空**的格子保留目标原样。</summary>
    [Fact]
    public void Overlay保留源里为空的目标格子()
    {
        TBin10 target = Fixture(), source = Clone(target);
        Layer sl = FirstLayer(source), tl = FirstLayer(target);
        FillNative(tl, target.TileSheets[0].Id, 5);       // 目标先铺满索引 5
        Fill(sl, source.TileSheets[0].Id, 99);
        sl.Tiles[0] = null!;                              // 源里挖空一格
        LayerRle.Rebuild(sl);
        string before = SheetOf(tl.Tiles[0]) + "#" + ((StaticTile)tl.Tiles[0]).TileIndex;

        Assert.True(MapPatcher.PatchMap(target, source, null, null, MapPatchMode.Overlay, out string? e), e);

        Assert.Equal(before, SheetOf(FirstLayer(target).Tiles[0]) + "#" + ((StaticTile)FirstLayer(target).Tiles[0]).TileIndex);
    }

    /// <summary>ReplaceByLayer：源图层存在的层，源里为空也要盖成空。</summary>
    [Fact]
    public void ReplaceByLayer会把源里的空格也盖过去()
    {
        TBin10 target = Fixture(), source = Clone(target);
        Layer tl = FirstLayer(target), sl = FirstLayer(source);
        FillNative(tl, target.TileSheets[0].Id, 5);
        Fill(sl, source.TileSheets[0].Id, 99);
        sl.Tiles[0] = null!;
        LayerRle.Rebuild(sl);

        Assert.True(MapPatcher.PatchMap(target, source, null, null, MapPatchMode.ReplaceByLayer, out string? e), e);

        Assert.Null(FirstLayer(target).Tiles[0]);
    }

    /// <summary>Replace：源里**没有的图层**就地清空。</summary>
    [Fact]
    public void Replace会清空源里没有的图层()
    {
        TBin10 target = Fixture(), source = Fixture();
        // 源只留第一层，其余全删 → 目标里那些层应被清空。
        Layer keep = source.Layers[0];
        source.Layers = new List<Layer> { keep };
        Assert.True(target.Layers.Count > 1, "夹具地图只有一层，这条用例失去意义");

        Assert.True(MapPatcher.PatchMap(target, source, null, null, MapPatchMode.Replace, out string? e), e);

        Assert.All(target.Layers.Skip(1).SelectMany(l => l.Tiles), t => Assert.Null(t));
    }

    // ── 图集映射 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 源图集与目标图集 <b>Id 相同但 Image 不同</b> ⇒ 新建 <c>z_&lt;id&gt;</c>，
    /// 瓦片指向新图集（照抄 SMAPI）。不这么做就会把目标的图集**改掉指向**。
    /// </summary>
    [Fact]
    public void 同名图集但图不同时新建z前缀图集()
    {
        TBin10 target = Fixture(), source = Clone(target);
        source.TileSheets[0].Image = "Maps/完全不同的图";
        Layer sl = FirstLayer(source);
        Fill(sl, source.TileSheets[0].Id, 7);

        Assert.True(MapPatcher.PatchMap(target, source, null, null, MapPatchMode.ReplaceByLayer, out string? e), e);

        TileSheet created = Assert.Single(target.TileSheets, s => s.Id.StartsWith("z_"));
        Assert.Equal("Maps/完全不同的图", created.Image);
        Assert.Contains(target.Layers.SelectMany(l => l.Tiles).OfType<StaticTile>(),
            t => t.TileSheet == created.Id);
        // 原图集**没被动过**。
        Assert.Equal(Fixture().TileSheets[0].Image, target.TileSheets.First(s => s.Id == created.Id[2..]).Image);
    }

    /// <summary>
    /// ★ **拷图集时按目标地图的目录重新定基**：源地图（包里的 tbin/tmx）的引用是相对 `Maps/` 的，
    /// 而目标地图的引用是相对**它自己那层目录**解析的 —— 照抄会让游戏把 `Mines/mine` 读成
    /// `Maps/Mines/Mines/mine`（实机异常链里正是这个，`Maps/Mines/19` 建不起来）。
    /// </summary>
    [Fact]
    public void 拷图集时按目标地图目录重新定基()
    {
        // 目标在 Maps/Mines/ ⇒ 同一层直接写裸名
        TBin10 target = Fixture(), source = Clone(target);
        source.TileSheets[0].Image = "Mines/mine";
        Fill(FirstLayer(source), source.TileSheets[0].Id, 7);

        Assert.True(MapPatcher.PatchMap(target, source, null, null, MapPatchMode.ReplaceByLayer,
            out string? e1, "Maps/Mines/19"), e1);
        Assert.Equal("mine", Assert.Single(target.TileSheets, s => s.Id.StartsWith("z_")).Image);

        // 目标在 Maps/ 这一层 ⇒ 要带目录
        TBin10 target2 = Fixture(), source2 = Clone(target2);
        source2.TileSheets[0].Image = "Mines/mine";
        Fill(FirstLayer(source2), source2.TileSheets[0].Id, 7);

        Assert.True(MapPatcher.PatchMap(target2, source2, null, null, MapPatchMode.ReplaceByLayer,
            out string? e2, "Maps/Town"), e2);
        Assert.Equal("Mines/mine", Assert.Single(target2.TileSheets, s => s.Id.StartsWith("z_")).Image);

        // 源引用已经带 `Maps/` 前缀时不能再补一个（实测 `[CP] East Scarp` 写 `Maps/Mines/mine.png`）
        TBin10 target4 = Fixture(), source4 = Clone(target4);
        source4.TileSheets[0].Image = "Maps/Mines/mine.png";
        Fill(FirstLayer(source4), source4.TileSheets[0].Id, 7);

        Assert.True(MapPatcher.PatchMap(target4, source4, null, null, MapPatchMode.ReplaceByLayer,
            out string? e4, "Maps/Town"), e4);
        Assert.Equal("Mines/mine.png", Assert.Single(target4.TileSheets, s => s.Id.StartsWith("z_")).Image);

        // 不带目标资产名时不重定基（旧行为）
        TBin10 target3 = Fixture(), source3 = Clone(target3);
        source3.TileSheets[0].Image = "Mines/mine";
        Fill(FirstLayer(source3), source3.TileSheets[0].Id, 7);

        Assert.True(MapPatcher.PatchMap(target3, source3, null, null, MapPatchMode.ReplaceByLayer,
            out string? e3), e3);
        Assert.Equal("Mines/mine", Assert.Single(target3.TileSheets, s => s.Id.StartsWith("z_")).Image);
    }

    /// <summary>图集完全相同 ⇒ 复用，不新建。</summary>
    [Fact]
    public void 图集完全相同时复用()
    {
        TBin10 target = Fixture(), source = Clone(target);
        int before = target.TileSheets.Count;

        Assert.True(MapPatcher.PatchMap(target, source, null, null, MapPatchMode.ReplaceByLayer, out string? e), e);

        Assert.Equal(before, target.TileSheets.Count);
    }

    /// <summary>源里有、目标里没有的图层 ⇒ 新建。</summary>
    [Fact]
    public void 源里有目标没有的图层时新建()
    {
        TBin10 target = Fixture(), source = Clone(target);
        Layer extra = source.Layers[0];
        extra.Id = "源独有层";
        source.Layers = new List<Layer> { extra };

        Assert.True(MapPatcher.PatchMap(target, source, null, null, MapPatchMode.Overlay, out string? e), e);

        Assert.Contains(target.Layers, l => l.Id == "源独有层");
    }

    // ── ExtendMap ────────────────────────────────────────────────────────

    [Fact]
    public void ExtendMap撑大图层并补空格()
    {
        TBin10 map = Fixture();
        Layer l = FirstLayer(map);
        int w = l.LayerSize.X, h = l.LayerSize.Y;

        MapPatcher.ExtendMap(map, w + 5, h + 3);

        Assert.Equal(w + 5, l.LayerSize.X);
        Assert.Equal(h + 3, l.LayerSize.Y);
        Assert.Equal((w + 5) * (h + 3), l.Tiles.Count);
        Assert.Null(l.Tiles[(h + 2) * (w + 5) + w + 4]);   // 新格子是空的
    }

    [Fact]
    public void ExtendMap已经够大时不动()
    {
        TBin10 map = Fixture();
        Layer l = FirstLayer(map);
        var size = new IntVector2 { X = l.LayerSize.X, Y = l.LayerSize.Y };

        MapPatcher.ExtendMap(map, 1, 1);

        Assert.Equal(size.X, l.LayerSize.X);
        Assert.Equal(size.Y, l.LayerSize.Y);
    }

    // ── 区域与校验 ───────────────────────────────────────────────────────

    [Fact]
    public void 区域尺寸不等时失败()
    {
        TBin10 target = Fixture(), source = Clone(target);

        // 夹具地图是 2x2 —— 区域必须落在界内，否则先撞上越界检查，测不到尺寸那一条。
        Assert.False(MapPatcher.PatchMap(target, source, (0, 0, 2, 2), (0, 0, 1, 1),
            MapPatchMode.Overlay, out string? error));
        Assert.Contains("尺寸不等", error);
    }

    [Fact]
    public void 区域越界时失败()
    {
        TBin10 target = Fixture(), source = Clone(target);

        Assert.False(MapPatcher.PatchMap(target, source, (0, 0, 9999, 9999), null,
            MapPatchMode.Overlay, out string? error));
        Assert.Contains("超出源地图", error);
    }

    /// <summary>
    /// 搬运**动画块**时，新块自己的 <c>Index</c>/<c>_currTileSheet</c> 必须一并建出来
    /// （<c>AnimatedTilerWriter</c> 回放的就是它）。忘了建就写出帧数为 0 的动画块，回读越界 ——
    /// 实测代价：SVE/RSV 各多出 4/12 条「写回地图 tbin 失败：Index was out of range」。
    /// </summary>
    [Fact]
    public void 搬运动画块时自带RLE段()
    {
        TBin10 target = Fixture(), source = Clone(target);
        string sheet = source.TileSheets[0].Id;
        Layer sl = FirstLayer(source);

        var frames = new List<StaticTile>
        {
            new() { TileSheet = sheet, TileIndex = 1, BlendMode = 0, Properties = new List<Propertie>() },
            new() { TileSheet = sheet, TileIndex = 2, BlendMode = 0, Properties = new List<Propertie>() },
        };
        for (int i = 0; i < sl.Tiles.Count; i++)
            sl.Tiles[i] = LayerRle.BuildAnimatedTile(120, frames);
        LayerRle.Rebuild(sl);

        Assert.True(MapPatcher.PatchMap(target, source, null, null, MapPatchMode.ReplaceByLayer, out string? e), e);

        // 关键：**写得出、读得回**。RLE 段没建就会在这一步炸。
        TBin10 back = TBin10Reader.FormFile(Write(target));
        var animated = Assert.IsType<AnimatedTile>(FirstLayer(back).Tiles[0]);
        Assert.Equal(2, animated.Frames.Count);
        Assert.Equal(120, animated.FrameInterval);
        Assert.Equal(2, animated._frameCount);
    }

    /// <summary>改完必须**写得出、读得回** —— 模型改了但 RLE 没重建，二进制就会与模型对不上。</summary>
    [Fact]
    public void 改完能写回并原样读回()
    {
        TBin10 target = Fixture(), source = Clone(target);
        Layer sl = FirstLayer(source);
        Fill(sl, source.TileSheets[0].Id, 11);

        Assert.True(MapPatcher.PatchMap(target, source, (0, 0, 2, 2), (0, 0, 2, 2),
            MapPatchMode.ReplaceByLayer, out string? e), e);

        TBin10 back = TBin10Reader.FormFile(Write(target));
        var t0 = Assert.IsType<StaticTile>(FirstLayer(back).Tiles[0]);
        Assert.Equal(11, t0.TileIndex);
    }
}
