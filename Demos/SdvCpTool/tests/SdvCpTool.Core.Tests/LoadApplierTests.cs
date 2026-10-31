using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

public class LoadApplierTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvld_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvlo_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvlp_" + Guid.NewGuid().ToString("N"));

    public LoadApplierTests()
    {
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_pack);
    }

    public void Dispose()
    {
        Directory.Delete(_src, recursive: true);
        Directory.Delete(_pack, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void Tree(string rel, string content)
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void Pack(string rel, string content)
    {
        string full = Path.Combine(_pack, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private OutputTree? _tree;
    private ImageCache? _images;

    private LoadApplier Build() =>
        new(_tree = new OutputTree(_src, _out), _pack, images: _images ??= new ImageCache(_tree));

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路，
    /// 图片的落盘权又在 `ImageCache` 手里，所以两个都要刷。</summary>
    private void Flush()
    {
        _images?.Flush();
        _tree?.Flush();
    }

    [Fact]
    public void 从FromFile整份写入()
    {
        Tree("Data/Locations.json", "{}");
        Tree("Data/Locations.config", "{}");
        Pack("Assets/Base.json", """{ "name": "X", "conditions": "Town/900 1400" }""");

        var r = Build().Apply("Data/NewFestival", "Assets/Base.json", "Data/NewFestival");

        Assert.True(r.Success);
        Flush();
        Assert.Equal("X",
            JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "NewFestival.json")))["name"]!.Value<string>());
    }

    /// <summary>
    /// ★ `Load` 把**文件内容**整个拷进资产 —— 令牌可能只在**那个文件**里，补丁的值里看不到。
    /// 实测语料剩的 13 处全是这种（`Data/Events` 的事件键里带 `{{FarmName}}`/`{{PlayerName}}`）。
    /// 游戏不认识 `{{…}}`，写出去就是永远不生效的条目 ⇒ **整条不写**。
    /// </summary>
    [Fact]
    public void FromFile内容里含代不掉的令牌时不写()
    {
        Tree("Data/Locations.json", "{}");
        Pack("Assets/Ev.json", """{ "123/e 456": "speak {{FarmName}}/end" }""");

        var r = Build().Apply("Data/Events/Town", "Assets/Ev.json", "Data/Events/Town");

        Assert.False(r.Success);
        Assert.Contains("无法静态解析", r.FailureReason);
        Assert.False(File.Exists(Path.Combine(_out, "Data", "Events", "Town.json")));
    }

    // ⚠ 这里**不放**「FromFile 内容里只有 `{{i18n:…}}` 时照常写」的用例：
    //   那条路会先撞上 i18n 自身的规则（键必须在包内的 i18n 文件里找得到），
    //   测出来的失败与本次改动无关。「`i18n` 不算未展开令牌」已由
    //   `ValueTokenSubstitutorTests.逐条问得出还有没有未展开令牌` 覆盖。

    [Fact]
    public void FromFile相对内容包根()
    {
        Tree("Data/Locations.json", "{}");
        Tree("Data/Locations.config", "{}");
        Pack("Assets/Sub/Base.json", """{ "a": "b" }""");

        var r = Build().Apply("Data/New", "Assets/Sub/Base.json", "Data/New");

        Assert.True(r.Success);
    }

    [Fact]
    public void 替换Target字段引用令牌()
    {
        Tree("Data/Locations.json", "{}");
        Tree("Data/Locations.config", "{}");
        // FromFile 相对内容包根目录：{{TargetWithoutPath}} 展开成 Target 的最后一段 `Abigail`
        Pack("Abigail.json", """{ "a": "b" }""");

        var r = Build().Apply("Characters/Abigail", "{{TargetWithoutPath}}.json", "Characters/Abigail");

        Assert.True(r.Success);
    }

    [Fact]
    public void 其它令牌无法解析时失败()
    {
        Tree("Data/Locations.json", "{}");
        Tree("Data/Locations.config", "{}");

        var r = Build().Apply("Data/New", "Assets/{{Random:1,2}}.json", "Data/New");

        Assert.False(r.Success);
        Assert.Contains("令牌", r.FailureReason);
    }

    [Fact]
    public void FromFile不存在时失败()
    {
        Tree("Data/Locations.json", "{}");
        Tree("Data/Locations.config", "{}");

        var r = Build().Apply("Data/New", "Assets/nope.json", "Data/New");

        Assert.False(r.Success);
        Assert.Contains("找不到", r.FailureReason);
    }

    // ---------------------------------------------------------------- 坏路径降级（Task 16）

    [Fact]
    public void 目标资产名含星号时返回失败而不抛异常()
    {
        Tree("Data/Locations.json", "{}");
        Tree("Data/Locations.config", "{}");
        Pack("Assets/Base.json", """{ "a": "b" }""");

        var r = Build().Apply("Data/**Martin**", "Assets/Base.json", "Data/**Martin**");

        Assert.False(r.Success);
        Assert.Contains("非法字符", r.FailureReason);
    }

    [Fact]
    public void 目标资产名是保留设备名时返回失败而不抛异常()
    {
        Tree("Data/Locations.json", "{}");
        Tree("Data/Locations.config", "{}");
        Pack("Assets/Base.json", """{ "a": "b" }""");

        var r = Build().Apply("Data/NUL", "Assets/Base.json", "Data/NUL");

        Assert.False(r.Success);
        Assert.Contains("保留设备名", r.FailureReason);
    }

    /// <summary>
    /// ★ `Load` 的 FromFile 是**贴图 `.xnb`** 时落成**图片资产**（`&lt;资产&gt;.png` + 图片 `.config`），
    /// **不能**当成"数据"写成 `.json` —— 实测 `[CP] Lunna` 的
    /// `Load Characters/Lunna_Shadowfolk ← assets/Events/Sprites/Lunna_Shadowfolk.xnb`：
    /// 产出里只有 `.json` 与贴图 `.config`、**图本身没了**，整树回包报
    /// `Could not find file '…Lunna_Shadowfolk.png'`。
    /// <para>夹具 `fixtures/Texture_2x2.xnb` 是打包器把一张 2×2 的 PNG 打出来的（逐像素 RGBA）。</para>
    /// </summary>
    [Fact]
    public void FromFile是贴图XNB时落成图片资产()
    {
        Tree("Maps/Town.config",
            """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Tree("Characters/Abigail.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryPack("assets/Sprites/Tiny.xnb",
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Texture_2x2.xnb")));

        MergeOutcome r = Build().Apply("Characters/TinyPic", "assets/Sprites/Tiny.xnb");

        Assert.True(r.Success, r.FailureReason);
        Flush();
        Assert.True(File.Exists(Path.Combine(_out, "Characters", "TinyPic.png")));      // 图出来了
        Assert.False(File.Exists(Path.Combine(_out, "Characters", "TinyPic.json")));    // 没被当成数据
        Assert.Contains("Texture2DReader", File.ReadAllText(Path.Combine(_out, "Characters", "TinyPic.config")));

        // 像素逐点对得上（RGBA 顺序与解包侧同一口径）
        byte[] px = Png.Read(Path.Combine(_out, "Characters", "TinyPic.png"), out int w, out int h);
        Assert.Equal(2, w);
        Assert.Equal(2, h);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255 }, px);
    }

    // ── Load 一份 .tbin（地图）────────────────────────────────────────────

    private void BinaryTree(string rel, byte[] bytes)
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    private void BinaryPack(string rel, byte[] bytes)
    {
        string full = Path.Combine(_pack, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    /// <summary>把夹具地图读进模型、改掉第一个图集的名字、再写进包里。</summary>
    private byte[] MapWithTilesheet(string newImage)
    {
        byte[] raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        TBin10 map = (TBin10)TBin10Reader.Create(raw).Read();
        map.TileSheets[0].Image = newImage;
        string tmp = Path.Combine(_pack, "tmp.tbin");
        Directory.CreateDirectory(_pack);
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, tmp);
        byte[] bytes = File.ReadAllBytes(tmp);
        File.Delete(tmp);
        return bytes;
    }

    /// <summary>
    /// `Load` 的是**现成 tbin**（不是 TMX）时，也要展开地图属性里的 `{{i18n}}`、
    /// 归一自定义图层名 —— 这两步原来只在 `MapCache.Flush`（TMX/EditMap）那条路上。
    /// </summary>
    [Fact]
    public void Load现成tbin也展开地图i18n并归一图层名()
    {
        byte[] fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", fixture);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");

        TBin10 map = (TBin10)TBin10Reader.Create(fixture).Read();
        map.Layers[0].Id = "Always Front";
        map.Properties ??= new List<Propertie>();
        map.Properties.Add(new Propertie { Key = "Message", Type = 3, Value = "{{i18n:msg}}" });
        string tmp = Path.Combine(_pack, "tmp.tbin");
        Directory.CreateDirectory(_pack);
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, tmp);
        BinaryPack("assets/Maps/MyMap.tbin", File.ReadAllBytes(tmp));
        File.Delete(tmp);

        Directory.CreateDirectory(Path.Combine(_pack, "i18n"));
        File.WriteAllText(Path.Combine(_pack, "i18n", "default.json"), """{ "msg": "门锁着" }""");

        var output = _tree = new OutputTree(_src, _out);
        _images = new ImageCache(output);
        var loader = new LoadApplier(output, _pack, PackTranslations.Load(_pack), images: _images);

        MergeOutcome r = loader.Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");
        Assert.True(r.Success, r.FailureReason);
        Flush();

        TBin10 back = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "MyMap.tbin"));
        Assert.Equal("AlwaysFront1", back.Layers[0].Id);
        Assert.Equal("门锁着",
            back.Properties.Single(p => p.Key == "Message").Value!.ToString());
    }

    // ── 地图的「包内同级图集」──────────────────────────────────────────────

    /// <summary>
    /// ★ SMAPI `ModContentManager.FixTilesheetPaths` / `TryGetTilesheetAssetName`：
    /// **模组加载的地图**，它的图集先按「地图文件所在目录」在模组里找（试 `.png` / `.xnb`），
    /// 找不到才回落游戏内容。
    /// <para>
    /// 纯 XNB 产出里**没有"模组目录"这一层** ⇒ 那些包内同级的图集必须**一起搬进产出树**，
    /// 放到游戏会去请求的名字上（`GetContentKeyForTilesheetImageSource`：没有 `Maps` 前缀就补上、
    /// 去掉 `.png`）。不搬的话地图**建不出来** —— 实机报的就是
    /// `Could not find file '…\Content\Maps\Binoculars.xnb'`。
    /// </para>
    /// </summary>
    [Fact]
    public void Load地图时把包内同级图集一起搬进产出树()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        // 图片资产的 .config 模板（根 Reader 是 Texture2DReader）
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryTree("Maps/spring_outdoorsTileSheet.png", new byte[] { 1, 2, 3 });

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet("MyTiles.png"));
        BinaryPack("assets/Maps/MyTiles.png", new byte[] { 9, 9, 9 });

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();
        Assert.True(File.Exists(Path.Combine(_out, "Maps", "MyTiles.png")));      // 图集搬进来了
        Assert.True(File.Exists(Path.Combine(_out, "Maps", "MyTiles.config")));   // 连 .config 一起
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(Path.Combine(_out, "Maps", "MyTiles.png")));
    }

    /// <summary>
    /// 图集在包内**找不到**（指游戏内容里的，比如 `spring_outdoorsTileSheet`）时**什么都不做** ——
    /// 那种由游戏/别的包提供，不能凭空造。
    /// </summary>
    [Fact]
    public void 图集不在包内时不动它()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet("spring_outdoorsTileSheet"));

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "spring_outdoorsTileSheet.png")));
    }

    /// <summary>
    /// ★ **带前导点的引用：剥点后按名字找图**（与不带点的情形同一条规矩）——
    /// 包内同级有那份就**用包内那份（覆盖同名资产）**，没有才落到游戏资产。
    ///
    /// 不剥这个点会怎样（实机 2026-10-12）：游戏去请求 `Maps/.spring_outdoorsTileSheet.xnb` ⇒
    /// FileNotFound ⇒ `xTile.Map.LoadTileSheets` **抛异常** ⇒ **整个地点建不起来**
    /// （Town / Mountain / Forest / Woods / Desert / IslandWest 等 27 个，跨天时 `RequireLocation("Town")` 崩）。
    /// 实测两种写法都有：`[CP] Integrated Minecarts` 写 `.spring_outdoorsTileSheet.png` 而包里什么都没带
    /// （⇒ 用游戏那张）；`[CP] East Scarp` 写 `paths.png` 而包里带了那份（⇒ 用包内那份）。
    /// </summary>
    [Fact]
    public void 带前导点的引用剥点后按名字找图_包内有就用包内那份()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");

        // 游戏里先有一张（原版资产，字节 1,2,3）
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryTree("Maps/spring_outdoorsTileSheet.png", new byte[] { 1, 2, 3 });

        // 包内同级也有一份（字节 9,9,9）⇒ 按"覆盖"的规矩，用包内这份
        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet(".spring_outdoorsTileSheet.png"));
        BinaryPack("assets/Maps/spring_outdoorsTileSheet.png", new byte[] { 9, 9, 9 });

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();

        byte[] written = File.ReadAllBytes(Path.Combine(_out, "Maps", "MyMap.tbin"));
        TBin10 back = (TBin10)TBin10Reader.Create(written).Read();
        Assert.Equal("spring_outdoorsTileSheet", back.TileSheets[0].Image);          // 点被剥掉、`.png` 也没了
        Assert.Equal(new byte[] { 9, 9, 9 },
            File.ReadAllBytes(Path.Combine(_out, "Maps", "spring_outdoorsTileSheet.png")));   // 包内那份覆盖上来了
    }

    /// <summary>
    /// 带点的引用**包内没有**同名图时就落到游戏资产：引用照样被改写（剥点、去 `.png`），
    /// 但**不动**游戏那张（`[CP] Integrated Minecarts` 就是这个形态）。
    /// </summary>
    [Fact]
    public void 带点的引用包内没有时落到游戏资产且不动它()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryTree("Maps/spring_outdoorsTileSheet.png", new byte[] { 1, 2, 3 });

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet(".spring_outdoorsTileSheet.png"));

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();

        byte[] written = File.ReadAllBytes(Path.Combine(_out, "Maps", "MyMap.tbin"));
        TBin10 back = (TBin10)TBin10Reader.Create(written).Read();
        Assert.Equal("spring_outdoorsTileSheet", back.TileSheets[0].Image);
        Assert.Equal(new byte[] { 1, 2, 3 },
            File.ReadAllBytes(Path.Combine(_out, "Maps", "spring_outdoorsTileSheet.png")));   // 游戏那张没被动
    }

    /// <summary>
    /// 带点的引用指向的名字**连游戏里也没有** ⇒ 退回正常流程：包内同级那份（剥点后的名字）搬进产出树；
    /// 地图引用已经改写成剥点后的名字，所以照样指得着。
    /// </summary>
    [Fact]
    public void 带点引用在游戏里也没有时按包内同级那份补()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        // 树里得有一份**图片**资产的 .config 当模板，新建图片资产要从它抄（同别的图集用例）
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet(".MyOwnSheet.png"));
        BinaryPack("assets/Maps/MyOwnSheet.png", new byte[] { 7, 7, 7 });


        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();

        byte[] written = File.ReadAllBytes(Path.Combine(_out, "Maps", "MyMap.tbin"));
        TBin10 back = (TBin10)TBin10Reader.Create(written).Read();
        Assert.Equal("MyOwnSheet", back.TileSheets[0].Image);
        Assert.True(File.Exists(Path.Combine(_out, "Maps", "MyOwnSheet.png")), string.Join(" | ", r.Notes));
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(Path.Combine(_out, "Maps", "MyOwnSheet.png")));
    }

    /// <summary>
    /// ★ 前导点也可能出现在**路径中间那一段**的开头：实测 `[CP] Integrated Minecarts` 的 tmx 写
    /// `Mines/.mine.png`，剥点后正好是**原版资产** `Maps/Mines/mine`（原版树里有 `mine.png`）。
    /// 只剥整串开头的话这条会漏过去，游戏照样去请求 `Maps/Mines/.mine.xnb` ⇒ 地点建不起来
    /// （实机：`IslandNorthCave1`）。
    /// </summary>
    [Fact]
    public void 路径中间那段带点的引用也剥掉()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");

        // 包内没有那份图（实测这个包也没带）⇒ 剥点后落到游戏资产，一个字都不用搬
        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet("Mines/.mine.png"));

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();

        byte[] written = File.ReadAllBytes(Path.Combine(_out, "Maps", "MyMap.tbin"));
        TBin10 back = (TBin10)TBin10Reader.Create(written).Read();
        Assert.Equal("Mines/mine", back.TileSheets[0].Image);      // 剥点 + 去 .png，目录段保留
    }

    /// <summary>
    /// ★ `..` 是**上一层目录**、不是"前导点"，绝不能被剥掉：地图的图集引用是相对**地图自己那层目录**
    /// 解析的，`../TileSheets/furniture` 剥成 `TileSheets/furniture` ⇒ 游戏去 `Maps/TileSheets/…` 找
    /// ⇒ 地图建不起来（实机 15 张，异常链里正是 `Content\Maps/TileSheets/furniture.xnb`）。
    /// </summary>
    [Fact]
    public void 上一级目录记号不能被当成前导点剥掉()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet("../TileSheets/furniture"));

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();

        byte[] written = File.ReadAllBytes(Path.Combine(_out, "Maps", "MyMap.tbin"));
        TBin10 back = (TBin10)TBin10Reader.Create(written).Read();
        // 原版 exe 不解析 `..` ⇒ 归一成**内容路径**（与 SMAPI 的
        // GetContentKeyForTilesheetImageSource 同一结果）。
        Assert.Equal("TileSheets/furniture", back.TileSheets[0].Image);
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "TileSheets", "furniture.png")));
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "Mines", "mine.png")));   // 没搬任何东西
    }

    /// <summary>
    /// 包内同级图集是**季节族**时，四个季节都要搬 —— 游戏会对室外地图把图集名的季节前缀
    /// 换成当前季节（`GameLocation.GetSeasonalTilesheetName`），只搬 spring 那一份，
    /// 夏天照样找不到。
    /// </summary>
    [Fact]
    public void 季节性图集把四个季节都搬过来()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryTree("Maps/spring_outdoorsTileSheet.png", new byte[] { 1 });

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet("spring_Ext.png"));
        foreach (string season in new[] { "spring", "summer", "fall", "winter" })
            BinaryPack($"assets/Maps/{season}_Ext.png", new byte[] { 7 });

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();
        foreach (string season in new[] { "spring", "summer", "fall", "winter" })
            Assert.True(File.Exists(Path.Combine(_out, "Maps", $"{season}_Ext.png")), season);
    }

    /// <summary>
    /// ★ 图集算出来的落点**撞上另一类资产的资产名**时必须改名 —— 实测：
    /// `[CP] Always Raining in the Valley` 的地图 `Maps/CooperFarms` 引用了包内同级图集
    /// `Coop.png`（`assets/Maps/Farm/Coop.png`），按「没有 `Maps` 前缀就补上」算出来的落点是
    /// `Maps/Coop` —— 而那是**原版地图**的资产名。同一个内容键上不能同时有地图与贴图：
    /// 谁后写谁赢，游戏拿着 `Texture2D` 当 `Map` 用 ⇒ **进那个地点就炸**（`AssetTypeAudit` 报出来的）。
    /// <para>
    /// 所以撞名时把图集挪到 `Maps/&lt;名&gt;_tilesheet`，并**把地图里的引用一起改掉** ——
    /// 游戏是按 tbin 的 `ImageSource` 找图的（`gameloc.cs:1138-1143`：名字没有目录时按 `Maps/` 找）。
    /// </para>
    /// </summary>
    [Fact]
    public void 图集名撞上另一类资产时改名并把地图引用一起改()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryTree("Maps/spring_outdoorsTileSheet.png", new byte[] { 1, 2, 3 });

        // 撞车对象：另一张**地图**的资产名正好是 MyTiles
        BinaryTree("Maps/MyTiles.tbin", map);
        Tree("Maps/MyTiles.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet("MyTiles.png"));
        BinaryPack("assets/Maps/MyTiles.png", new byte[] { 9, 9, 9 });

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();

        // 图集挪到没被占用的键上，内容照搬（逐字节）
        Assert.Equal(new byte[] { 9, 9, 9 },
            File.ReadAllBytes(Path.Combine(_out, "Maps", "MyTiles_tilesheet.png")));
        Assert.True(File.Exists(Path.Combine(_out, "Maps", "MyTiles_tilesheet.config")));

        // 原地那张地图的资产**没被贴图盖掉**
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "MyTiles.png")));

        // 地图引用改到新名上
        byte[] written = File.ReadAllBytes(Path.Combine(_out, "Maps", "MyMap.tbin"));
        TBin10 back = (TBin10)TBin10Reader.Create(written).Read();
        Assert.Equal("MyTiles_tilesheet", back.TileSheets[0].Image);   // 引用写成原版那种裸名（不带 .png）

        // 报告里看得见（不静默改名）
        Assert.Contains(r.Notes, n => n.Contains("MyTiles_tilesheet"));
    }

    /// <summary>
    /// 撞名的图集**包内根本没有**（那是游戏内容里的名字）时**不改名**：没有那份图就没什么可挪的，
    /// 改名只会把「指向地图」变成「指向不存在的资产」，报告里还多一条假缺失 —— 照抄 CP：原样留着。
    /// </summary>
    [Fact]
    public void 包内没有那份图时撞名也不改名()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryTree("Maps/spring_outdoorsTileSheet.png", new byte[] { 1, 2, 3 });

        BinaryTree("Maps/MyTiles.tbin", map);
        Tree("Maps/MyTiles.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet("MyTiles.png"));   // ⚠ 包里**不放** MyTiles.png

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "MyTiles_tilesheet.png")));

        byte[] written = File.ReadAllBytes(Path.Combine(_out, "Maps", "MyMap.tbin"));
        TBin10 back = (TBin10)TBin10Reader.Create(written).Read();
        // 引用**没改名**（包内没那份图，没什么可挪的）——但写法定形了：不带 `.png`、相对地图那层目录。
        // 游戏自己会剥 `.png`（实机日志里它找的是 `.../furniture.xnb`），所以这是等价的归一。
        Assert.Equal("MyTiles", back.TileSheets[0].Image);
    }

    /// <summary>
    /// `Load` 的是**包里的 `.tbin`**（整份拷进产出树）时，撞名后的改名必须**写回那份 tbin** ——
    /// 这条路上地图是「原样字节」，改了内存对象不重新落盘就等于没改。
    /// </summary>
    [Fact]
    public void tbin地图的图集撞名时改名要写回tbin()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryTree("Maps/spring_outdoorsTileSheet.png", new byte[] { 1, 2, 3 });

        BinaryTree("Maps/MyTiles.tbin", map);
        Tree("Maps/MyTiles.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet("MyTiles.png"));
        BinaryPack("assets/Maps/MyTiles.png", new byte[] { 9, 9, 9 });

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();

        byte[] written = File.ReadAllBytes(Path.Combine(_out, "Maps", "MyMap.tbin"));
        TBin10 back = (TBin10)TBin10Reader.Create(written).Read();
        Assert.Equal("MyTiles_tilesheet", back.TileSheets[0].Image);   // 引用写成原版那种裸名（不带 .png）
        Assert.True(File.Exists(Path.Combine(_out, "Maps", "MyTiles_tilesheet.png")));
    }

    /// <summary>撞名的是季节族时，改名要**整族一起改**（四份都要在新名下），否则换季就找不到图。</summary>
    [Fact]
    public void 图集撞名时季节族跟着一起改名()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryTree("Maps/spring_outdoorsTileSheet.png", new byte[] { 1 });

        // 撞车对象：一张地图正好叫 spring_Ext
        BinaryTree("Maps/spring_Ext.tbin", map);
        Tree("Maps/spring_Ext.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");

        BinaryPack("assets/Maps/MyMap.tbin", MapWithTilesheet("spring_Ext.png"));
        foreach (string season in new[] { "spring", "summer", "fall", "winter" })
            BinaryPack($"assets/Maps/{season}_Ext.png", new byte[] { 7 });

        MergeOutcome r = Build().Apply("Maps/MyMap", "assets/Maps/MyMap.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();
        foreach (string season in new[] { "spring", "summer", "fall", "winter" })
            Assert.True(File.Exists(Path.Combine(_out, "Maps", $"{season}_Ext_tilesheet.png")), season);
    }

    /// <summary>
    /// `FromFile` 是 `.tbin` 时不再报「不是数据资产」—— tbin 本身就是地图数据，
    /// 逐字节放进产出树，再配一份地图 `.config`（`xTile.Pipeline.TideReader` 那份）。
    /// </summary>
    [Fact]
    public void tbin的地图整份写入并配上地图config()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        // 树里先有一张成对的地图，作为 .config 模板（判据是 X.tbin 与 X.config 成对）
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Directory.CreateDirectory(Path.Combine(_pack, "assets"));
        File.WriteAllBytes(Path.Combine(_pack, "assets", "New.tbin"), map);

        MergeOutcome r = Build().Apply("Maps/New", "assets/New.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();
        string written = Path.Combine(_out, "Maps", "New.tbin");
        Assert.True(File.Exists(written));
        Assert.Equal(map, File.ReadAllBytes(written));                       // 逐字节一致
        Assert.Contains("TideReader", File.ReadAllText(Path.Combine(_out, "Maps", "New.config")));
    }

    /// <summary>一张最小可用 TMX：4×4、一个图层、一个 tileset。</summary>
    private const string MinimalTmx = """
    <?xml version="1.0" encoding="UTF-8"?>
    <map version="1.5" orientation="orthogonal" renderorder="right-down"
         width="4" height="4" tilewidth="16" tileheight="16" infinite="0">
     <tileset firstgid="1" name="tiles" tilewidth="16" tileheight="16" tilecount="4" columns="2">
      <image source="spring_tiles" width="32" height="32"/>
     </tileset>
     <layer id="1" name="Back" width="4" height="4">
      <data encoding="csv">1,2,3,4, 1,2,3,4, 1,2,3,4, 1,2,3,4</data>
     </layer>
    </map>
    """;

    /// <summary>
    /// <c>Load</c> 一份 <c>.tmx</c> 时，转出来的 tbin 交给**地图内存模型**持有、由它统一落盘 ——
    /// 产出树里的 <c>.tbin</c> 不能有两个写手（延迟写模式下会和暂存打架）。
    /// </summary>
    [Fact]
    public void TMX出来的地图交给地图内存模型后仍能落盘读回()
    {
        // 树里先有一张成对的地图（.tbin + .config 成对）作为 .config 模板
        BinaryTree("Maps/Town.tbin",
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin")));
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Pack("assets/Maps/MyMap.tmx", MinimalTmx);

        var output = _tree = new OutputTree(_src, _out);
        var maps = new MapCache(output);
        _images = new ImageCache(output);
        var loader = new LoadApplier(output, _pack, maps: maps, images: _images);

        MergeOutcome r = loader.Apply("Maps/MyMap", "assets/Maps/MyMap.tmx");

        Assert.True(r.Success, r.FailureReason);
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "MyMap.tbin")));   // 由内存模型持有

        List<string> written = maps.Flush();
        Assert.Contains("Maps/MyMap.tbin", written);
Flush();

        TBin10 back = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "MyMap.tbin"));
        Assert.Single(back.Layers);
        Assert.Equal("Back", back.Layers[0].Id);
    }

    /// <summary>
    /// 配偶房间共享图：后写的**更小** TMX 不覆盖已有大图。
    /// 实测全量链：`[CP] East Scarp NPCs` 的 12×9 盖掉 `East Scarp NPCs` 的 30×27，
    /// CorwinLK/DaleWaede/JadeMalic 的矩形全部越界（婚后进农舍崩溃）。
    /// </summary>
    [Fact]
    public void spouseRoom的小TMX不覆盖已有大图()
    {
        BinaryTree("Maps/Town.tbin",
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin")));
        Tree("Maps/Town.config",
            """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Pack("assets/Maps/Big.tmx", BigTmx);       // 8×8
        Pack("assets/Maps/Small.tmx", MinimalTmx); // 4×4

        var output = _tree = new OutputTree(_src, _out);
        var maps = new MapCache(output);
        _images = new ImageCache(output);
        var loader = new LoadApplier(output, _pack, maps: maps, images: _images);

        Assert.True(loader.Apply("Maps/test_spouseRoom", "assets/Maps/Big.tmx").Success);
        maps.Flush();
        Flush();

        MergeOutcome r = loader.Apply("Maps/test_spouseRoom", "assets/Maps/Small.tmx");
        Assert.True(r.Success, r.FailureReason);
        maps.Flush();
        Flush();

        byte[] raw = File.ReadAllBytes(Path.Combine(_out, "Maps", "test_spouseRoom.tbin"));
        int headers = 0;
        for (int i = 0; i + 6 <= raw.Length; i++)
            if (raw.AsSpan(i, 6).SequenceEqual("tBIN10"u8)) headers++;
        // 两次 Flush 不能把同一张图追加成两份文档（TBin10Reader.Save 不重置 BytePosition）。
        Assert.Equal(1, headers);

        TBin10 back = TBin10Reader.Create(raw).Read() as TBin10 ?? throw new InvalidOperationException();
        Assert.Equal(8, back.Layers[0].LayerSize.X);
        Assert.Equal(8, back.Layers[0].LayerSize.Y);
    }

    private const string BigTmx = """
    <?xml version="1.0" encoding="UTF-8"?>
    <map version="1.5" orientation="orthogonal" renderorder="right-down"
         width="8" height="8" tilewidth="16" tileheight="16" infinite="0">
     <tileset firstgid="1" name="tiles" tilewidth="16" tileheight="16" tilecount="4" columns="2">
      <image source="spring_tiles" width="32" height="32"/>
     </tileset>
     <layer id="1" name="Back" width="8" height="8">
      <data encoding="csv">1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4</data>
     </layer>
    </map>
    """;

    /// <summary>
    /// ★ 撞名判据必须**看得见本次运行刚生成、还没落盘的地图**：实测 `[CP] The Last Smoluanu`
    /// 先 `Load Maps/NightBGDwarf`（地图只在内存里）、再 `Load Maps/Summit_Night`，后者的包内图集
    /// 就叫 `NightBGDwarf.png` —— 按「没有 `Maps` 前缀就补上」算出来正是那张地图的键。
    /// 看不见它就把图写到 `Maps/NightBGDwarf` 上，随后地图又写回同一键（一个键两份不同类的东西）。
    /// </summary>
    [Fact]
    public void 图集撞上本次运行刚生成的地图时也改名()
    {
        BinaryTree("Maps/Town.tbin",
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin")));
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Tree("Maps/spring_outdoorsTileSheet.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        BinaryTree("Maps/spring_outdoorsTileSheet.png", new byte[] { 1, 2, 3 });

        // ⚠ 先造的那张地图必须走 **tmx** 那条路：只有它会进地图内存模型（`.tbin` 的 Load 是
        //   「原样字节直接暂存」，不进缓存 —— 真实案例（Smoluanu）走的正是 tmx）。
        Pack("assets/Maps/First.tmx", MinimalTmx);
        BinaryPack("assets/Maps/Second.tbin", MapWithTilesheet("MyTiles.png"));
        BinaryPack("assets/Maps/MyTiles.png", new byte[] { 9, 9, 9 });   // 图集与源地图**同级**（同真实包）

        var output = _tree = new OutputTree(_src, _out);
        var maps = new MapCache(output);
        _images = new ImageCache(output);
        var loader = new LoadApplier(output, _pack, maps: maps, images: _images);

        Assert.True(loader.Apply("Maps/MyTiles", "assets/Maps/First.tmx").Success);   // 先造出那张地图
        Assert.True(loader.Apply("Maps/Second", "assets/Maps/Second.tbin").Success);  // 它的图集名跟它撞
        maps.Flush();
        Flush();

        TBin10 second = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Second.tbin"));
        Assert.Equal("MyTiles_tilesheet", second.TileSheets[0].Image);   // 同上：引用不带 .png
        Assert.True(File.Exists(Path.Combine(_out, "Maps", "MyTiles_tilesheet.png")));
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "MyTiles.png")));          // 没写到地图那个键上
    }

    /// <summary>没有成对的地图 <c>.config</c> 时不写半成品 —— 宁可失败。</summary>
    [Fact]
    public void tbin缺地图config模板时失败且不落文件()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        File.WriteAllBytes(Path.Combine(_pack, "New.tbin"), map);

        MergeOutcome r = Build().Apply("Maps/New", "New.tbin");

        Assert.False(r.Success);
        Assert.Contains(".config", r.FailureReason);
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "New.tbin")));
    }

    /// <summary>
    /// `FromFile` 是 `.xnb` 地图：解出容器里的 tbin 载荷再落盘。
    /// 断言用**逐字节对照**：原版 `Island_House_Bin.xnb` 解出来应当与解包出的
    /// `Island_House_Bin.tbin` 完全一致 —— 只比「文件非空」证明不了内容是地图而不是垃圾。
    /// </summary>
    [Fact]
    public void xnb的地图解码后与解包出的tbin逐字节一致()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "fixtures");
        byte[] expected = File.ReadAllBytes(Path.Combine(dir, "Island_House_Bin.tbin"));

        BinaryTree("Maps/Town.tbin", expected);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Directory.CreateDirectory(Path.Combine(_pack, "assets"));
        File.Copy(Path.Combine(dir, "Island_House_Bin.xnb"), Path.Combine(_pack, "assets", "New.xnb"), overwrite: true);

        MergeOutcome r = Build().Apply("Maps/New", "assets/New.xnb");

        Assert.True(r.Success, r.FailureReason);
        Flush();
        Assert.Equal(expected, File.ReadAllBytes(Path.Combine(_out, "Maps", "New.tbin")));
    }

    /// <summary>`.xnb` 解出来不是地图（如贴图）时必须失败，绝不把非地图载荷塞进 Maps 资产。</summary>
    [Fact]
    public void xnb解出来不是地图时失败()
    {
        byte[] map = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"));
        BinaryTree("Maps/Town.tbin", map);
        Tree("Maps/Town.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        Directory.CreateDirectory(Path.Combine(_pack, "assets"));
        // 一个不是合法 XNB 的文件
        File.WriteAllBytes(Path.Combine(_pack, "assets", "New.xnb"), new byte[] { 1, 2, 3, 4 });

        MergeOutcome r = Build().Apply("Maps/New", "assets/New.xnb");

        Assert.False(r.Success);
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "New.tbin")));
    }

    /// <summary>
    /// `.xnb` 的载荷**类型自己说明它是什么**，不靠 Target 名字猜 ——
    /// 实测 SVE 的 `assets/XNBs/*.xnb` 打的其实是 `Data/Events/*`（预编译好的字典），
    /// 名字看着像地图，实际不是。
    /// <para>
    /// 断言用**往返对照**：原版 `DesertFestival.xnb` 解出来的 JSON，
    /// 应当与解包出的 `Data/Events/DesertFestival.json` 语义一致。
    /// </para>
    /// </summary>
    [Fact]
    public void xnb的字典资产解码成json且与解包结果一致()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "fixtures");
        // 树里先有一个同目录的数据资产，给 .config 模板用
        Tree("Data/Events/Town.json", """{ "1/t 600 1200": "script" }""");
        Tree("Data/Events/Town.config", """{ "Readers": [] }""");
        Directory.CreateDirectory(Path.Combine(_pack, "assets", "XNBs"));
        File.Copy(Path.Combine(dir, "DesertFestival.xnb"),
                  Path.Combine(_pack, "assets", "XNBs", "New.xnb"), overwrite: true);

        MergeOutcome r = Build().Apply("Data/Events/New", "assets/XNBs/New.xnb");

        Assert.True(r.Success, r.FailureReason);
        Flush();
        JToken written = JToken.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Events", "New.json")));
        JToken expected = JToken.Parse(File.ReadAllText(Path.Combine(dir, "DesertFestival.expected.json")));
        Assert.True(JToken.DeepEquals(expected, written),
            $"解出来的 JSON 与解包结果不一致：实际 {written} / 期望 {expected}");
    }

    private void PackBytes(string rel, byte[] content)
    {
        string full = Path.Combine(_pack, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
    }

    /// <summary>
    /// ★ `Load` 进来的地图也要把 TMXL 的 <c>LoadMap</c> 改成原版 <c>Warp</c>。
    /// <para>
    /// 纯 XNB 装机没有 <c>Platonymous.TMXLoader</c>（注册这个动作的 DLL）——
    /// 不改写的话，Grandpa's Farm 的农场出口全部是"踩上去没反应"的死图块。
    /// 走的是「原样字节」那条路（没图集改名），所以这里要证明**确实重新序列化了**。
    /// </para>
    /// </summary>
    [Fact]
    public void Load地图时把TMXL的LoadMap改成Warp()
    {
        // 树里要有一份「X.tbin + X.config」成对的地图模板（FindMapConfig 的判据）。
        Tree("Maps/Template.tbin", "x");
        Tree("Maps/Template.config", """{ "Readers": [] }""");

        var layer = new Layer
        {
            Id = "Back",
            Visible = 1,
            Description = "",
            LayerSize = new IntVector2 { X = 1, Y = 1 },
            TileSize = new IntVector2 { X = 16, Y = 16 },
            Properties = new List<Propertie>(),
            Tiles = new List<BaseTile>
            {
                new StaticTile
                {
                    TileSheet = "",
                    TileIndex = 0,
                    Properties = new List<Propertie>
                    {
                        new() { Key = "TouchAction", Type = 0x03, Value = "LoadMap Forest 69 2" },
                    },
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
        string tmp = Path.Combine(Path.GetTempPath(), "sdv_loadmap_" + Guid.NewGuid().ToString("N") + ".tbin");
        try
        {
            TBin10Reader.Create(Array.Empty<byte>()).Save(map, tmp);
            PackBytes("Maps/Farm.tbin", File.ReadAllBytes(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }

        MergeOutcome r = Build().Apply("Maps/Farm", "Maps/Farm.tbin");

        Assert.True(r.Success, r.FailureReason);
        Flush();
        TBin10 written = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Farm.tbin"));
        var tile = (BaseTile)written.Layers[0].Tiles[0];
        Assert.Contains(tile.Properties,
            p => p.Key == "TouchAction" && (string)p.Value! == "Warp Forest 69 2");
    }
}