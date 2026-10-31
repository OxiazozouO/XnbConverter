using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// Task 12a：<c>EditMap</c> 的**属性类**改动（<c>MapProperties</c> / <c>AddWarps</c> / <c>AddNpcWarps</c>）。
/// <para>
/// 夹具是原版真实地图 <c>Maps/Island_House_Bin.tbin</c> —— tbin 是二进制格式，
/// 合成的假夹具证明不了「真文件能读能写」。
/// </para>
/// </summary>
public class MapApplierTests : IDisposable
{
    private readonly string _in = Path.Combine(Path.GetTempPath(), "sdvmap_in_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvmap_out_" + Guid.NewGuid().ToString("N"));

    public MapApplierTests()
    {
        Directory.CreateDirectory(_in);
        Directory.CreateDirectory(Path.Combine(_in, "Maps"));
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin");
        File.Copy(fixture, Path.Combine(_in, "Maps", "Island_House_Bin.tbin"), overwrite: true);
    }

    private OutputTree? _tree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路，图片的落盘权又在 `ImageCache` 手里。</summary>
    private void Flush()
    {
        _images?.Flush();
        _tree?.Flush();
    }

    public void Dispose()
    {
        Directory.Delete(_in, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    /// <summary>整条测试共用一个产出树 —— <see cref="OutputTree"/> 的构造要求输出目录为空。</summary>
    private OutputTree? _output;
    private OutputTree Output() => _output ??= new OutputTree(_in, _out);

    /// <summary>图片内存模型（`EditMap` 的 `FromFile` 搬包内同级图集时走它）。</summary>
    private ImageCache? _images;
    private ImageCache Images => _images ??= new ImageCache(Output());

    private MergeOutcome Run(string patchJson) =>
        MapApplier.Apply(Output(), "Maps/Island_House_Bin", JObject.Parse(patchJson), images: Images);

    /// <summary>
    /// <c>EditMap</c> 带 <c>FromFile</c> 时，补丁块自己的图集也可能来自**包内同级**
    /// （SMAPI 按模组目录找图集，见 <see cref="LocalMapTilesheets"/>）。合并后
    /// <c>PatchMap</c> 会把它们作为 <c>z_&lt;id&gt;</c> 图集挂到目标地图上、名字照抄块里的 ——
    /// 那些图片不搬进产出树，**目标地图整个建不出来**。
    /// </summary>
    [Fact]
    public void FromFile补丁块的包内同级图集也搬进产出树()
    {
        // 图片资产的 .config 模板（根 Reader 是 Texture2DReader）——没有它就写不出图片资产
        WriteInput("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        WriteInputBytes("Maps/spring_outdoorsTileSheet.png", new byte[] { 1 });

        string pack = Path.Combine(_in, "..", "pack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(pack, "assets", "Maps"));
        byte[] chunk = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        TBin10 model = (TBin10)TBin10Reader.Create(chunk).Read();
        model.TileSheets[0].Image = "ChunkTiles.png";
        TBin10Reader.Create(Array.Empty<byte>()).Save(model, Path.Combine(pack, "assets", "Maps", "Chunk.tbin"));
        File.WriteAllBytes(Path.Combine(pack, "assets", "Maps", "ChunkTiles.png"), new byte[] { 5, 5 });

        MergeOutcome r = MapApplier.Apply(Output(), "Maps/Island_House_Bin", JObject.Parse("""
        { "FromFile": "assets/Maps/Chunk.tbin",
          "ToArea": { "X": 0, "Y": 0, "Width": 2, "Height": 2 } }
        """), pack, images: Images);

        Assert.True(r.Success, r.FailureReason);
        Flush();
        Assert.True(File.Exists(Path.Combine(_out, "Maps", "ChunkTiles.png")));
        Directory.Delete(pack, recursive: true);
    }

    /// <summary>
    /// ★ **同一份源地图被多条补丁共用时，图集引用不能被反复重定基**。
    /// <para>
    /// 实机那条：`Spiral Mine Shortcuts` 用同一份 `assets/19-edit.tmx` 打 4 条 `EditMap` 到
    /// `Maps/Mines/19`，产出里那条引用变成 `../../mine` ⇒ 游戏去**内容根**找 `mine.xnb` ⇒ 那张地图建不起来
    /// （逐字节比：我们的 tbin 里多出几个 `z_` 图集、引用逐次加层）。
    /// </para>
    /// <para>
    /// 成因：源地图对象由 <see cref="MapCache.TryGetSource"/> 共用 ⇒ `LocalMapTilesheets` 写进去的形态
    /// 必须**幂等**。补丁的**源**保持「相对 `Maps/`」（由 `PatchMap` 按目标目录定基一次），
    /// 只有**要写进产出树的目标地图**才写成「相对它自己那层目录」。
    /// </para>
    /// </summary>
    [Fact]
    public void 同一份源地图被反复用时图集引用不被反复重定基()
    {
        // 目标放在子目录里（实机那张是 Maps/Mines/19）：引用只有写成「相对它自己那层目录」才不会错位。
        WriteInputBytes("Maps/Mines/19.tbin", File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin")));

        string pack = Path.Combine(_in, "..", "pack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(pack, "assets"));
        TBin10 src = (TBin10)TBin10Reader
            .Create(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin")))
            .Read();
        src.TileSheets[0].Id = "src";                 // Id 与目标不同 ⇒ 会作为 `z_<id>` 图集挂上去，那条引用才看得见
        src.TileSheets[0].Image = "Mines/mine.png";   // 包内**没有**这张图 ⇒ 引用的是游戏资产（实机就是这种）
        TBin10Reader.Create(Array.Empty<byte>()).Save(src, Path.Combine(pack, "assets", "19-edit.tbin"));

        var maps = new MapCache(Output());
        var patch = JObject.Parse("""
        { "FromFile": "assets/19-edit.tbin",
          "ToArea": { "X": 0, "Y": 0, "Width": 2, "Height": 2 } }
        """);
        for (int i = 0; i < 4; i++)
        {
            MergeOutcome r = MapApplier.Apply(Output(), "Maps/Mines/19", patch, pack, maps, Images);
            Assert.True(r.Success, r.FailureReason);
        }
        Assert.Equal(3, maps.SourceHits);   // 后三条真的走了共用缓存 —— 不然这条用例测不到点子上
        maps.Flush();
        Flush();

        TBin10 map = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Mines", "19.tbin"));
        List<TileSheet> fromSource = map.TileSheets.Where(s => s.Id.StartsWith("z_")).ToList();
        Assert.Equal(4, fromSource.Count);                            // 4 条补丁各挂一个
        Assert.All(fromSource, s => Assert.Equal("mine", s.Image));   // 相对 `Maps/Mines/` ⇒ `Maps/Mines/mine` ✓
        Assert.DoesNotContain(fromSource, s => s.Image.Contains(".."));

        Directory.Delete(pack, recursive: true);
    }

    private void WriteInput(string rel, string content)
    {
        string full = Path.Combine(_in, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void WriteInputBytes(string rel, byte[] bytes)
    {
        string full = Path.Combine(_in, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    /// <summary>回读产出树里的地图，取属性字典。</summary>
    private Dictionary<string, string> ReadProps() => ReadMap().Properties
        .ToDictionary(p => p.Key, p => p.Value?.ToString() ?? "");

    /// <summary>回读产出树里的地图（要看 <see cref="Propertie.Type"/> 时用它）。</summary>
    private TBin10 ReadMap() =>
        TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));

    /// <summary>
    /// 往**输入树**的那张地图上直接加一个属性 —— 用来造「非字符串类型」的夹具。
    /// <c>MapApplier</c> 自己只会写字符串属性，所以只能这样造。
    /// </summary>
    private void AddInputProperty(Propertie property)
    {
        string path = Path.Combine(_in, "Maps", "Island_House_Bin.tbin");
        TBin10Reader reader = TBin10Reader.Create(File.ReadAllBytes(path));
        TBin10 map = (TBin10)reader.Read();
        map.Properties ??= new List<Propertie>();
        map.Properties.Add(property);
        reader.Save(map, path);
    }

    private string[] Warps() =>
        ReadProps().TryGetValue("Warp", out string? w)
            ? w.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

    // ── MapProperties ────────────────────────────────────────────────────

    [Fact]
    public void 新增地图属性()
    {
        Assert.True(Run("""{ "MapProperties": { "MyKey": "MyValue" } }""").Success);

        Assert.Equal("MyValue", ReadProps()["MyKey"]);
    }

    /// <summary>CP 语义（EditMapPatch.cs:105-118）：值为 <c>null</c> 即**删除**该键。</summary>
    [Fact]
    public void 值为null时删除地图属性()
    {
        Assert.True(Run("""{ "MapProperties": { "K": "V" } }""").Success);
        Assert.True(Run("""{ "MapProperties": { "K": null } }""").Success);

        Assert.False(ReadProps().ContainsKey("K"));
    }

    // ── AddWarps ─────────────────────────────────────────────────────────

    /// <summary>
    /// CP 把新 warp **前置**到已有值（<c>(新 + " " + 旧).Trim()</c>，EditMapPatch.cs:300-320），
    /// 且构造时先 <c>Reverse</c>（:59-60）。
    /// </summary>
    [Fact]
    public void 新增warp前置到已有值之前()
    {
        Assert.True(Run("""{ "MapProperties": { "Warp": "1 2 Town 3 4" } }""").Success);
        Assert.True(Run("""{ "AddWarps": [ "5 6 Beach 7 8" ] }""").Success);

        Assert.Equal(new[] { "5", "6", "Beach", "7", "8", "1", "2", "Town", "3", "4" }, Warps());
    }

    [Fact]
    public void 多条warp按CP的逆序落位()
    {
        Assert.True(Run("""{ "AddWarps": [ "1 1 Town 1 1", "2 2 Beach 2 2" ] }""").Success);

        // CP 先 Reverse([a,b]) → [b,a]，再拼串 → "b a"
        Assert.Equal(new[] { "2", "2", "Beach", "2", "2", "1", "1", "Town", "1", "1" }, Warps());
    }

    [Fact]
    public void NPCWarp与Warp是两条独立的属性()
    {
        Assert.True(Run("""{ "AddWarps": [ "1 1 Town 1 1" ], "AddNpcWarps": [ "2 2 Beach 2 2" ] }""").Success);

        Dictionary<string, string> props = ReadProps();
        Assert.StartsWith("1 1 Town", props["Warp"]);
        Assert.StartsWith("2 2 Beach", props["NPCWarp"]);
    }

    /// <summary>CP 校验 5 段、第 0/1/3/4 段是整数（EditMapPatch.cs:322+）。</summary>
    [Theory]
    [InlineData("1 2 Town 3")]              // 段数不足
    [InlineData("a 2 Town 3 4")]            // 首段不是整数
    [InlineData("1 2 Town 3 4 5")]          // 段数过多
    public void 非法warp只跳过那一条(string warp)
    {
        // CP `ApplyWarps` 把校验不过的收进 errors、调用处 `WarnForPatch`，
        // 而**合法的那些照样写进 Warp 属性**。原先一条非法就整条 Fail，
        // 把同一补丁里合法的也一起丢了。
        MergeOutcome r = Run($$"""{ "AddWarps": [ "{{warp}}", "9 9 Town 9 9" ] }""");

        Assert.True(r.Success);
        Assert.Contains("格式非法", string.Join("；", r.Notes));
        Assert.StartsWith("9 9 Town", ReadProps()["Warp"]);
    }

    /// <summary>全是非法 warp 时**一个字段都不动**（CP 也是 `if (list.Any())` 才写）。</summary>
    [Fact]
    public void 全是非法warp时不写属性()
    {
        MergeOutcome r = Run("""{ "AddWarps": [ "1 2 Town 3" ] }""");

        Assert.True(r.Success);
        Assert.False(ReadProps().ContainsKey("Warp"));
    }

    // ── 瓦片几何：MapTiles 四个字段都支持了，整图替换仍拒绝 ────────────────

    /// <summary>
    /// <c>MapTiles</c> 的 <c>SetIndex</c> 现在**支持**（语义照抄 CP `EditMapPatch.cs:235-312`），
    /// 不再是拒绝理由。
    /// </summary>
    [Fact]
    public void MapTiles的SetIndex判定为可写()
    {
        var patch = new SdvCpTool.Core.Cp.ChangePatch
        {
            Action = "EditMap",
            Raw = JObject.Parse("""{ "MapTiles": [ { "Layer": "Back", "Position": "0,0", "SetIndex": "1" } ] }"""),
        };

        Assert.True(WritePolicy.IsWritableMapEdit(patch));
    }

    /// <summary>
    /// **整图替换 / 合并**（<c>FromFile</c>）现在也写 —— 合并语义已从
    /// <c>StardewModdingAPI.dll</c> 读出（<see cref="SdvCpTool.Core.Maps.MapPatcher"/>），
    /// 「未确证，不猜」的前提消失。
    /// </summary>
    [Theory]
    [InlineData("""{ "FromFile": "assets/x.tbin" }""")]
    [InlineData("""{ "PatchMode": "Overlay" }""")]
    public void 整图替换判定为可写(string extra)
    {
        var patch = new SdvCpTool.Core.Cp.ChangePatch
        {
            Action = "EditMap",
            Raw = JObject.Parse($$"""{ "MapProperties": { "K": "V" }, {{extra[1..^1]}} }"""),
        };

        Assert.True(WritePolicy.IsWritableMapEdit(patch));
    }

    [Fact]
    public void 纯属性改动判定为可写()
    {
        var patch = new SdvCpTool.Core.Cp.ChangePatch
        {
            Action = "EditMap",
            Raw = JObject.Parse("""{ "MapProperties": { "K": "V" } }"""),
        };

        Assert.True(WritePolicy.IsWritableMapEdit(patch));
    }

    [Fact]
    public void 地图不在树里时失败()
    {
        MergeOutcome r = MapApplier.Apply(Output(), "Maps/NotThere",
            JObject.Parse("""{ "MapProperties": { "K": "V" } }"""), images: Images);

        Assert.False(r.Success);
        Assert.Contains("不在资源树里", r.FailureReason);
    }

    /// <summary>改动必须**落盘**且能被重新读出来（真二进制往返，不是内存里的对象）。</summary>
    [Fact]
    public void 改动能被重新读出()
    {
        Assert.True(Run("""{ "MapProperties": { "RoundTrip": "ok" } }""").Success);

        string path = Path.Combine(_out, "Maps", "Island_House_Bin.tbin");
        Assert.True(new FileInfo(path).Length > 0);
        Assert.Equal("ok", TBin10Reader.FormFile(path).Properties
            .First(p => p.Key == "RoundTrip").Value);
    }

    // ── TextOperations 指向 MapProperties ─────────────────────────────────

    /// <summary>
    /// CP 的 <c>TextOperationTargetRoot</c> 枚举里有 <c>MapProperties</c>，
    /// 与 <c>Entries</c> 同形（字符串键 → 字符串值）。
    /// </summary>
    [Fact]
    public void 文本操作可以作用在地图属性上()
    {
        Assert.True(Run("""{ "MapProperties": { "K": "旧" } }""").Success);
        Assert.True(Run("""
        { "TextOperations": [
            { "Target": [ "MapProperties", "K" ], "Operation": "Append", "Value": "新" }
        ] }
        """).Success);

        Assert.Equal("旧新", ReadProps()["K"]);
    }

    /// <summary>
    /// 文本操作**只改它点名的那一个属性**，别的属性原样不动（值一样、类型一样）。
    /// <para>
    /// 依据 CP <c>EditMapPatch.TryApplyTextOperation(Map, …)</c>（反编译 :14136）：
    /// 它只做 `Properties[名字] = PropertyValue.op_Implicit(op.Apply(旧值))` ——
    /// **不重写整张属性表**。我们原先走的是「把属性镜成 JObject → 交给通用文本操作器 →
    /// 整表写回」，那会把**别的属性也按字符串类型重写**（`WriteBackAll`）。
    /// </para>
    /// </summary>
    [Fact]
    public void 文本操作只改点名的属性其余原样()
    {
        // 造一个 int 类型的属性：只有类型保住了，才说明没被整表按字符串重写。
        AddInputProperty(new Propertie { Key = "IntProp", Type = 1, Value = 7 });

        Assert.True(Run("""{ "MapProperties": { "K": "旧" } }""").Success);
        Assert.True(Run("""
        { "TextOperations": [
            { "Target": [ "MapProperties", "K" ], "Operation": "Append", "Value": "新" }
        ] }
        """).Success);

        TBin10 map = ReadMap();
        Assert.Equal("旧新", map.Properties.First(p => p.Key == "K").Value?.ToString());

        Propertie kept = map.Properties.First(p => p.Key == "IntProp");
        Assert.Equal((byte)1, kept.Type);
        Assert.Equal("7", kept.Value?.ToString());
    }

    /// <summary>点名的属性不存在时按空串追加（CP 的 `TryGetValue(..., ref "")`）。</summary>
    [Fact]
    public void 文本操作点名不存在的属性时按空串处理()
    {
        Assert.True(Run("""
        { "TextOperations": [
            { "Target": [ "MapProperties", "Fresh" ], "Operation": "Append", "Value": "v" }
        ] }
        """).Success);

        Assert.Equal("v", ReadProps()["Fresh"]);
    }

    /// <summary>
    /// 一条补丁里同时有 <c>MapProperties</c> 与 <c>TextOperations</c> 时，**顺序照 CP**：
    /// <c>EditMapPatch.Edit</c>（反编译 :13860-13930）是
    /// <c>FromFile → MapTiles → MapProperties → AddWarps → TextOperations</c>。
    /// <para>
    /// 即 <c>MapProperties</c> 先落值，<c>TextOperations</c> 再看**落过之后的**值。
    /// 我们原来把 TextOperations 排在 MapProperties 之前 —— 这条就会写出 `base` 而不是 `base+op`。
    /// </para>
    /// </summary>
    [Fact]
    public void MapProperties先于TextOperations生效()
    {
        Assert.True(Run("""
        {
            "MapProperties": { "K": "base" },
            "TextOperations": [
                { "Target": [ "MapProperties", "K" ], "Operation": "Append", "Value": "+op" }
            ]
        }
        """).Success);

        Assert.Equal("base+op", ReadProps()["K"]);
    }

    /// <summary>根不是 <c>MapProperties</c> 的文本操作会碰到瓦片几何，一律拒绝。</summary>
    [Fact]
    public void 其它根的文本操作被拒绝()
    {
        MergeOutcome r = Run("""
        { "TextOperations": [
            { "Target": [ "MapTiles", "x" ], "Operation": "Append", "Value": "y" }
        ] }
        """);

        Assert.False(r.Success);
        Assert.Contains("MapProperties", r.FailureReason);
    }

    // ── MapTiles.SetProperties（Task 12c）────────────────────────────────

    /// <summary>夹具里找一个真有图块的格子 —— tbin 是稀疏的，不能假定 (0,0) 一定有。</summary>
    private (string Layer, int X, int Y) FindTile()
    {
        _ = Output();   // 产出树是构造时才把输入树拷过来的 —— 先建出来再读
        TBin10 m = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        foreach (Layer l in m.Layers)
            for (int i = 0; i < l.Tiles.Count; i++)
                if (l.Tiles[i] is BaseTile)
                    return (l.Id, i % l.LayerSize.X, i / l.LayerSize.X);
        throw new InvalidOperationException("夹具里找不到有图块的格子");
    }

    private (string Layer, int X, int Y) FindEmptyCell()
    {
        _ = Output();
        TBin10 m = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        foreach (Layer l in m.Layers)
            for (int i = 0; i < l.Tiles.Count; i++)
                if (l.Tiles[i] is not BaseTile)
                    return (l.Id, i % l.LayerSize.X, i / l.LayerSize.X);
        throw new InvalidOperationException("夹具里找不到空格子");
    }

    /// <summary>找一个**有图块**的格子（几何改动要在它身上做）。</summary>
    private (string Layer, int X, int Y) FindFirstTileCell()
    {
        _ = Output();
        TBin10 m = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        foreach (Layer l in m.Layers)
            for (int i = 0; i < l.Tiles.Count; i++)
                if (l.Tiles[i] is BaseTile)
                    return (l.Id, i % l.LayerSize.X, i / l.LayerSize.X);
        throw new InvalidOperationException("夹具里找不到有图块的格子");
    }

    private static string SheetOf(BaseTile? tile) =>
        tile is StaticTile st ? st.TileSheet
        : tile is AnimatedTile at && at.Frames.Count > 0 ? at.Frames[0].TileSheet
        : "";

    private static int IndexOf(BaseTile? tile) =>
        tile is StaticTile st ? st.TileIndex
        : tile is AnimatedTile at && at.Frames.Count > 0 ? at.Frames[0].TileIndex
        : 0;

    /// <summary>夹具地图里第一个图集的 Id —— 新建图块时要按 Id 指名。</summary>
    private string FirstSheetId()
    {
        _ = Output();
        TBin10 m = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        return m.TileSheets[0].Id;
    }

    /// <summary>回读产出树里某图层某格的模型，用来断言"这一格现在画的是什么"。</summary>
    private BaseTile TileAt(string layerId, int x, int y)
    {
        TBin10 m = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        Layer l = m.Layers.First(v => v.Id == layerId);
        return l.Tiles[y * l.LayerSize.X + x];
    }

    /// <summary>
    /// 改图块属性后**必须重建 RLE** —— <c>Layer.Write</c> 回放 <c>Index</c>/<c>_sizeArr</c> 而不推导，
    /// 不同步就会写出与模型对不上的二进制。这里用「回读后属性确实在」来验证。
    /// </summary>
    [Fact]
    public void 改图块属性且回读一致()
    {
        (string layer, int x, int y) = FindTile();

        Assert.True(Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "{{x}},{{y}}",
            "SetProperties": { "Action": "Message \"hi\"" } } ] }
        """).Success);

        TBin10 back = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        var tile = (StaticTile)back.Layers.First(l => l.Id == layer).Tiles[y * back.Layers.First(l => l.Id == layer).LayerSize.X + x]!;
        Assert.Equal("Message \"hi\"", tile.Properties.First(p => p.Key == "Action").Value);
    }

    /// <summary>
    /// 地图/图块属性里的 <c>{{i18n:key}}</c> 按译文展开（地图单资产，按中文/默认冻结）。
    /// 实测 SVE 的 DarkShrine / Townhouse / TreasureCave 对话文字。
    /// </summary>
    [Fact]
    public void 地图与图块属性里的i18n按译文展开()
    {
        Directory.CreateDirectory(Path.Combine(_in, "i18n"));
        File.WriteAllText(Path.Combine(_in, "i18n", "default.json"), """{ "greet": "Hello" }""");
        File.WriteAllText(Path.Combine(_in, "i18n", "zh.json"), """{ "greet": "你好" }""");
        PackTranslations translations = PackTranslations.Load(_in);

        (string layer, int x, int y) = FindTile();
        string json = """
            { "MapProperties": { "Message": "{{i18n:greet}}" },
              "MapTiles": [ { "Layer": "LAYERNAME", "Position": "TILEX,TILEY",
                              "SetProperties": { "Action": "Message \"{{i18n:greet}}\"" } } ] }
            """
            .Replace("LAYERNAME", layer)
            .Replace("TILEX", x.ToString())
            .Replace("TILEY", y.ToString());
        MergeOutcome r = MapApplier.Apply(Output(), "Maps/Island_House_Bin", JObject.Parse(json),
            images: Images, translations: translations);

        Assert.True(r.Success, r.FailureReason);
        Flush();

        TBin10 back = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        Assert.Equal("你好", back.Properties.First(p => p.Key == "Message").Value);
        var tile = (StaticTile)back.Layers.First(l => l.Id == layer)
            .Tiles[y * back.Layers.First(l => l.Id == layer).LayerSize.X + x]!;
        Assert.Equal("Message \"你好\"", tile.Properties.First(p => p.Key == "Action").Value);
    }

    /// <summary>CP 语义：空位报错不创建（除非同时给 SetTilesheet 与 SetIndex，那不在本任务范围）。</summary>
    [Fact]
    public void 图块属性打在空格子时失败()
    {
        (string layer, int x, int y) = FindEmptyCell();

        MergeOutcome r = Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "{{x}},{{y}}",
            "SetProperties": { "Action": "Message" } } ] }
        """);

        // CP EditMapPatch.cs:258-262：空位**可以**新建，但前提是同时给了 SetTilesheet 与 SetIndex。
        // 只给 SetProperties 时 CP 是 `WarnForPatch` 后**继续**（不作废补丁），我们照做 ——
        // 凭空造一个"半成品图块"仍然不做，只是不再连坐同一补丁里别的改动。
        Assert.True(r.Success);
        Assert.Contains("要新建必须同时给 SetTilesheet 与 SetIndex", string.Join("；", r.Notes));
    }

    // ── 瓦片几何（语义照抄 CP EditMapPatch.cs:235-312）────────────────────

    /// <summary>给了 <c>SetTilesheet</c> + <c>SetIndex</c> 时，空格子**可以新建**图块（同 CP）。</summary>
    [Fact]
    public void 空位同时给图集与索引时可以新建图块()
    {
        (string layer, int x, int y) = FindEmptyCell();
        string sheet = FirstSheetId();

        MergeOutcome r = Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "{{x}},{{y}}",
            "SetTilesheet": "{{sheet}}", "SetIndex": 5 } ] }
        """);

        Assert.True(r.Success, r.FailureReason);
        var tile = Assert.IsType<StaticTile>(TileAt(layer, x, y));
        Assert.Equal(5, tile.TileIndex);
        Assert.Equal(sheet, tile.TileSheet);
    }

    [Fact]
    public void SetIndex改图块索引并保留图集与属性()
    {
        (string layer, int x, int y) = FindFirstTileCell();
        (string sheet, int index) = (SheetOf(TileAt(layer, x, y)), IndexOf(TileAt(layer, x, y)));

        Assert.True(Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "{{x}},{{y}}", "SetIndex": 42 } ] }
        """).Success);

        var tile = Assert.IsType<StaticTile>(TileAt(layer, x, y));
        Assert.Equal(42, tile.TileIndex);
        Assert.Equal(sheet, tile.TileSheet);       // 没给 SetTilesheet ⇒ 图集不动
        Assert.NotEqual(index, tile.TileIndex);
    }

    /// <summary>`SetTilesheet` 按图集 **Id** 查；给了不存在的 Id 要**直接失败**（同 CP），不是静默忽略。</summary>
    [Fact]
    public void SetTilesheet指定不存在的图集时失败()
    {
        (string layer, int x, int y) = FindFirstTileCell();

        MergeOutcome r = Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "{{x}},{{y}}",
            "SetTilesheet": "根本没有这个图集" } ] }
        """);

        // 图集不存在是**运行期**失败 ⇒ CP `WarnForPatch` 后继续，不作废补丁。
        Assert.True(r.Success);
        Assert.Contains("不在该地图的图集里", string.Join("；", r.Notes));
    }

    /// <summary><c>Remove</c> 单独用时把那格清空。</summary>
    [Fact]
    public void Remove单独用时清空该格()
    {
        (string layer, int x, int y) = FindFirstTileCell();

        Assert.True(Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "{{x}},{{y}}", "Remove": true } ] }
        """).Success);

        Assert.Null(TileAt(layer, x, y));
    }

    /// <summary>
    /// <c>Remove</c> + <c>SetIndex</c> 同时给时，是<b>替换</b>而不是清空 ——
    /// CP 先置 null 再建新块（`EditMapPatch.cs:269-270`）。顺序错就成"删了不建"。
    /// </summary>
    [Fact]
    public void Remove与SetIndex同时给时是替换()
    {
        (string layer, int x, int y) = FindFirstTileCell();

        Assert.True(Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "{{x}},{{y}}",
            "Remove": true, "SetIndex": 7, "SetTilesheet": "{{FirstSheetId()}}" } ] }
        """).Success);

        var tile = Assert.IsType<StaticTile>(TileAt(layer, x, y));
        Assert.Equal(7, tile.TileIndex);
    }

    /// <summary><c>Remove</c> 必须是布尔值；写成别的要失败，不能猜。</summary>
    [Fact]
    public void Remove不是布尔时失败()
    {
        (string layer, int x, int y) = FindFirstTileCell();

        MergeOutcome r = Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "{{x}},{{y}}", "Remove": "yes" } ] }
        """);

        Assert.False(r.Success);
        Assert.Contains("不是布尔", r.FailureReason);
    }

    /// <summary>什么都没指定既不改图块也不改属性 —— 拒绝，不当作空操作放过去。</summary>
    [Fact]
    public void 什么都没指定时失败()
    {
        (string layer, int x, int y) = FindFirstTileCell();

        MergeOutcome r = Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "{{x}},{{y}}" } ] }
        """);

        // CP 那边这一档其实**什么也不做**（`TryApplyTile` 一路 fall through），
        // 连警告都没有；我们记一条 note 让人看得见，同样不作废补丁。
        Assert.True(r.Success);
        Assert.Contains("什么都没指定", string.Join("；", r.Notes));
    }

    /// <summary>`SetIndex`/`SetTilesheet`/`Remove` 现在都支持 —— 一并进写入路径。</summary>
    [Theory]
    [InlineData("SetIndex")]
    [InlineData("SetTilesheet")]
    [InlineData("Remove")]
    public void 改图块本身而不是属性时也判定为可写(string field)
    {
        var patch = new SdvCpTool.Core.Cp.ChangePatch
        {
            Action = "EditMap",
            Raw = JObject.Parse($$"""
            { "MapTiles": [ { "Layer": "Back", "Position": "0,0", "{{field}}": "x", "SetProperties": { "K": "V" } } ] }
            """),
        };

        Assert.True(WritePolicy.IsWritableMapEdit(patch));
    }

    [Fact]
    public void 纯属性改动的MapTiles判定为可写()
    {
        var patch = new SdvCpTool.Core.Cp.ChangePatch
        {
            Action = "EditMap",
            Raw = JObject.Parse("""
            { "MapTiles": [ { "Layer": "Back", "Position": "0,0", "SetProperties": { "K": "V" } } ] }
            """),
        };

        Assert.True(WritePolicy.IsWritableMapEdit(patch));
    }

    /// <summary>CP 的 Position **两种写法都吃** —— 实测 RSV 用对象形式，只认字符串会抛异常。</summary>
    [Fact]
    public void Position的对象形式也能用()
    {
        (string layer, int x, int y) = FindTile();

        MergeOutcome r = Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": { "X": {{x}}, "Y": {{y}} },
            "SetProperties": { "ObjPos": "ok" } } ] }
        """);

        Assert.True(r.Success, r.FailureReason);
    }

    [Fact]
    public void Position格式不对时干净失败而不是抛异常()
    {
        (string layer, int x, int y) = FindTile();

        MergeOutcome r = Run($$"""
        { "MapTiles": [ { "Layer": "{{layer}}", "Position": "abc",
            "SetProperties": { "K": "V" } } ] }
        """);

        Assert.False(r.Success);
        Assert.Contains("Position 非法", r.FailureReason);
    }

    /// <summary>
    /// ★ 门禁写的是**补丁条件本身**，不是它的取反（2026-10-18 订正）。
    /// <para>
    /// `GameLocation.performTouchAction` 的 `ConditionalDoor` 分支（`:3907`）在 GSQ 为**真**时
    /// `break`（放行）、为**假**时才弹回。第一版写了 `Negate(gsq)`，结果 Standard 农场
    /// （`FARM_TYPE Standard` 为真）反而被自己的门禁弹回 —— Grandpa's Farm 的 Backwoods 楼梯上不去。
    /// </para>
    /// </summary>
    [Fact]
    public void 门禁用条件本身而不是取反()
    {
        // 夹具的 Back 图层没有实心图块 ⇒ 用一张合成的 1×1 地图（门禁只往 Back 层写）。
        var layer = new Layer
        {
            Id = "Back",
            Visible = 1,
            Description = "",
            LayerSize = new XnbConverter.Entity.Mono.IntVector2 { X = 1, Y = 1 },
            TileSize = new XnbConverter.Entity.Mono.IntVector2 { X = 16, Y = 16 },
            Properties = new List<Propertie>(),
            Tiles = new List<BaseTile>
            {
                new StaticTile
                {
                    TileSheet = "",
                    TileIndex = 0,
                    Properties = new List<Propertie>(),
                },
            },
            Index = new List<char> { 'S' },
            _sizeArr = new List<int>(),
            _currTileSheet = new List<string> { "" },
        };
        var map = new TBin10
        {
            Format = "tBIN10",
            Id = "",
            Description = "",
            Properties = new List<Propertie>(),
            TileSheets = new List<TileSheet>(),
            Layers = new List<Layer> { layer },
        };
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, Path.Combine(_in, "Maps", "Gate.tbin"));

        MergeOutcome r = MapApplier.Apply(Output(), "Maps/Gate",
            JObject.Parse("""
            { "MapTiles": [ { "Layer": "Back", "Position": "0,0" } ] }
            """),
            images: Images, doorGateGsq: "FARM_TYPE Standard");

        Assert.True(r.Success, r.FailureReason);
        Flush();

        TBin10 after = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Gate.tbin"));
        var tile = (BaseTile)after.Layers[0].Tiles[0]!;
        Assert.Contains(tile.Properties,
            p => p.Key == "TouchAction" && (string)p.Value! == "ConditionalDoor FARM_TYPE Standard");
    }
}