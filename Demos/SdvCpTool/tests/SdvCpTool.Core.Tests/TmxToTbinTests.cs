using System.IO.Compression;
using SdvCpTool.Core.Maps;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// TMX（Tiled 的 XML）→ tbin。
/// <para>
/// 夹具是**合成**的，但每条路径都覆盖到：CSV 图层、base64+zlib 图层、tileset 的逐块属性、
/// 动画帧、翻转、以及 <b>objectgroup 折算图块属性</b>（Stardew 用 Tiled 对象承载图块属性）。
/// </para>
/// </summary>
public class TmxToTbinTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvtmx_" + Guid.NewGuid().ToString("N"));

    public TmxToTbinTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private TBin10 Convert(string tmx) => Convert(tmx, out _);

    private TBin10 Convert(string tmx, out int droppedFlips)
    {
        string path = Path.Combine(_dir, "map.tmx");
        File.WriteAllText(path, tmx);
        Assert.True(TmxToTbin.TryConvert(path, out TBin10? map, out string? error, out droppedFlips), error);
        return map!;
    }

    private void Reject(string tmx, string expect)
    {
        string path = Path.Combine(_dir, "map.tmx");
        File.WriteAllText(path, tmx);
        Assert.False(TmxToTbin.TryConvert(path, out _, out string? error, out _));
        Assert.Contains(expect, error);
    }

    /// <summary>base64 + zlib 的图层数据（与真实 tmx 一致）。</summary>
    private static string ZlibBase64(params uint[] gids)
    {
        byte[] raw = new byte[gids.Length * 4];
        for (int i = 0; i < gids.Length; i++) BitConverter.TryWriteBytes(raw.AsSpan(i * 4), gids[i]);
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionMode.Compress, leaveOpen: true)) z.Write(raw);
        return System.Convert.ToBase64String(output.ToArray());
    }

    /// <summary>4×4 图、两个图层（CSV 与 base64+zlib）、tileset 带逐块属性与动画、objectgroup 折算属性。</summary>
    private static string Map(string layers, string extra = "") => $$"""
    <?xml version="1.0" encoding="UTF-8"?>
    <map version="1.5" orientation="orthogonal" renderorder="right-down"
         width="4" height="4" tilewidth="16" tileheight="16" infinite="0">
     <properties>
      <property name="Outdoors" value="T"/>
     </properties>
     <tileset firstgid="1" name="tiles" tilewidth="16" tileheight="16" tilecount="4" columns="2">
      <image source="spring_tiles" width="32" height="32"/>
      <tile id="1"><properties><property name="Type" value="Grass"/></properties></tile>
      <tile id="3"><animation><frame tileid="0" duration="100"/><frame tileid="1" duration="100"/></animation></tile>
     </tileset>
     {{layers}}
     {{extra}}
    </map>
    """;

    private const string CsvBack = """
    <layer id="1" name="Back" width="4" height="4">
     <data encoding="csv">2,4,2147483649,0, 0,0,0,0, 0,0,0,0, 0,0,0,0</data>
    </layer>
    """;

    private static string ZlibLayer(string name, params uint[] gids) => $$"""
    <layer id="2" name="{{name}}" width="4" height="4">
     <data encoding="base64" compression="zlib">{{ZlibBase64(gids)}}</data>
    </layer>
    """;

    private const string TileData = """
    <objectgroup id="3" name="Back">
     <object id="1" name="TileData" x="0" y="0" width="16" height="16">
      <properties><property name="NPCBarrier" value="T"/></properties>
     </object>
     <object id="2" name="Other" x="32" y="0"><properties><property name="X" value="1"/></properties></object>
    </objectgroup>
    """;

    private static uint[] Empty16() => new uint[16];

    // ────────────────────────── 基本结构 ──────────────────────────

    [Fact]
    public void 地图与图集的基本字段()
    {
        uint[] g = Empty16();
        TBin10 map = Convert(Map(CsvBack + ZlibLayer("Buildings", g)));

        Assert.Equal("tBIN10", map.Format);
        Assert.Equal("T", map.Properties.First(p => p.Key == "Outdoors").Value);
        Assert.Single(map.TileSheets);

        TileSheet sheet = map.TileSheets[0];
        // 实测真实 tbin：Id 是 tileset 的 name，Image 是 image 的 source
        Assert.Equal("tiles", sheet.Id);
        Assert.Equal("spring_tiles", sheet.Image);
        Assert.Equal(2, sheet.SheetSize!.Value.X);
        Assert.Equal(2, sheet.SheetSize.Value.Y);
        Assert.Equal(16, sheet.TileSize.X);
    }

    /// <summary>
    /// Tiled 允许两个 tileset 同名（实测 `Cove_exit.tmx` 两个都叫 `untitled tile sheet`）。
    /// tbin 的图块只靠名字找图集，重名会让索引落到错误的那张图上。
    /// </summary>
    [Fact]
    public void 同名图集自动改成唯一Id()
    {
        string tmx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.5" orientation="orthogonal" renderorder="right-down"
             width="1" height="1" tilewidth="16" tileheight="16" infinite="0">
         <tileset firstgid="1" name="same" tilewidth="16" tileheight="16" tilecount="4" columns="2">
          <image source="a" width="32" height="32"/>
         </tileset>
         <tileset firstgid="5" name="same" tilewidth="16" tileheight="16" tilecount="4" columns="2">
          <image source="b" width="32" height="32"/>
         </tileset>
         <layer id="1" name="Back" width="1" height="1">
          <data encoding="csv">5</data>
         </layer>
        </map>
        """;
        TBin10 map = Convert(tmx);

        Assert.Equal(new[] { "same", "same_2" }, map.TileSheets.Select(s => s.Id));
        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.Equal("same_2", tile.TileSheet);
        Assert.Equal(0, tile.TileIndex);
    }

    [Fact]
    public void 图层尺寸与图块数()
    {
        uint[] g = Empty16();
        TBin10 map = Convert(Map(CsvBack + ZlibLayer("Buildings", g)));

        Assert.Equal(2, map.Layers.Count);
        Assert.All(map.Layers, l =>
        {
            Assert.Equal(4, l.LayerSize.X);
            Assert.Equal(4, l.LayerSize.Y);
            Assert.Equal(16, l.Tiles.Count);   // 含 null 占位，与 Read 的语义一致
        });
        Assert.Equal("Back", map.Layers[0].Id);
    }

    /// <summary>
    /// ★ Tiled 的 **XML 层数据**写法（没有 <c>encoding</c> 属性）：每个格子一个
    /// <c>&lt;tile gid="…"/&gt;</c>，空格子写成没有 gid 的 <c>&lt;tile/&gt;</c>。
    ///
    /// 实测 `[CP] East Scarp` 的 `JacobBarn.tmx` 就是这个写法，缺了这一档那张地图**产不出来**
    /// （实机日志：`EastScarp_JacobBarn` 一直报 `Couldn't create the … location`）。
    /// </summary>
    [Fact]
    public void XML写法的图层数据也认()
    {
        string xml = """
        <layer id="1" name="Back" width="4" height="4">
         <data>
          <tile gid="2"/><tile gid="3"/><tile gid="2147483649"/><tile/>
          <tile/><tile/><tile/><tile/>
          <tile/><tile/><tile/><tile/>
          <tile gid="1"/><tile/><tile/><tile/>
         </data>
        </layer>
        """;

        TBin10 map = Convert(Map(xml));

        Assert.Single(map.Layers);
        Assert.Equal(16, map.Layers[0].Tiles.Count);
        // 落进 tbin 的是**局部**下标（gid − firstgid），与 csv 那条路一致
        Assert.Equal(1, ((StaticTile)map.Layers[0].Tiles[0]!).TileIndex);    // gid 2
        Assert.Equal(2, ((StaticTile)map.Layers[0].Tiles[1]!).TileIndex);    // gid 3
        Assert.Equal(0, ((StaticTile)map.Layers[0].Tiles[2]!).TileIndex);    // gid 2147483649（带翻转位）→ 局部 0
        Assert.Null(map.Layers[0].Tiles[3]);                                 // 没有 gid = 空格子
        Assert.Equal(0, ((StaticTile)map.Layers[0].Tiles[12]!).TileIndex);   // gid 1；顺序按格子走
    }

    /// <summary>XML 写法的格子数与地图尺寸不符（多半是无限地图）⇒ 明确拒绝，不猜。</summary>
    [Fact]
    public void XML写法格子数不符时拒绝()
    {
        Reject(Map("""
        <layer id="1" name="Back" width="4" height="4">
         <data><tile gid="1"/><tile gid="2"/></data>
        </layer>
        """), "XML 图块数 2 与地图尺寸 16 不符");
    }

    /// <summary>空段用 <c>N</c>+计数压缩 —— 这是 tbin 的 RLE，不是随便写。</summary>
    [Fact]
    public void 空图块用N段压缩()
    {
        uint[] g = Empty16();
        TBin10 map = Convert(Map(ZlibLayer("Back", g)));

        Layer l = map.Layers[0];
        Assert.Equal("N", l.Index[0].ToString());
        Assert.Equal(4, l._sizeArr[0]);          // 每行 4 格全空 → 4 个 N 段
        Assert.Equal(4, l.Index.Count);           // 4 行
        Assert.All(l.Tiles, t => Assert.Null(t));
    }

    // ────────────────────────── 图块属性 / 动画 / 翻转 ──────────────────────────

    /// <summary>
    /// tileset 的逐块属性挂到**图集**上，键是 <c>@TileIndex@&lt;id&gt;@&lt;名&gt;</c> ——
    /// 这是真实实现（<c>TMXTile.LoadTileSets</c>）与原版（<c>Maps/Town.tbin</c> 里 460 条这种键）
    /// 共同的写法。游戏先查 <c>tile.TileIndexProperties</c>、没有再落 <c>tile.Properties</c>
    /// （<c>GameLocation.cs:2784-2787</c>）—— 挂到图块上会让优先级反过来。
    /// </summary>
    [Fact]
    public void tileset的逐块属性落到图集上()
    {
        TBin10 map = Convert(Map(CsvBack + ZlibLayer("Buildings", Empty16())));

        // gid 2 → local 1 → tileset 里 <tile id="1"> 有 Type=Grass
        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.Equal(1, tile.TileIndex);
        Assert.Empty(tile.Properties);                                   // 图块上**不**挂 tileset 那套
        Assert.Equal("Grass",
            map.TileSheets[0].Properties.First(p => p.Key == "@TileIndex@1@Type").Value);
    }

    [Fact]
    public void 动画块变成AnimatedTile()
    {
        TBin10 map = Convert(Map(CsvBack + ZlibLayer("Buildings", Empty16())));

        // gid 4 → local 3 → <tile id="3"> 是动画
        var anim = Assert.IsType<AnimatedTile>(map.Layers[0].Tiles[1]);
        Assert.Equal(2, anim._frameCount);
        Assert.Equal(2, anim.Frames.Count);
        Assert.Equal(100, anim.FrameInterval);
        Assert.Equal(new[] { 0, 1 }, anim.Frames.Select(f => f.TileIndex).ToArray());
    }

    /// <summary>
    /// 翻转位**记成标记**（不再丢弃）：xTile 不认翻转、纯 XNB 也没有 SMAPI 的
    /// <c>SDisplayDevice</c> ⇒ 落盘前 <c>FlipBaker</c> 把变换烘进图集。
    /// 标记值 = TMXTile 的 <c>(@Rotation, @Flip)</c>，H 翻转是 <c>"0,1"</c>。
    /// </summary>
    [Fact]
    public void 翻转位记成标记等待烘焙()
    {
        TBin10 map = Convert(Map(CsvBack + ZlibLayer("Buildings", Empty16())), out int flipped);

        // 2147483649 = 0x80000001 → local 0，带 H 翻转
        var tile = (StaticTile)map.Layers[0].Tiles[2]!;
        Assert.Equal(0, tile.TileIndex);
        Assert.Equal("0,1", tile.Properties.First(p => p.Key == TmxToTbin.FlipProperty).Value);
        Assert.Equal(1, flipped);
    }

    /// <summary>对角翻转（D）也是同一个通道：TMXTile 给的是 rotation=90、effect=2（垂直翻）。</summary>
    [Fact]
    public void 对角翻转记成旋转90加垂直翻()
    {
        TBin10 map = Convert(Map(ZlibLayer("Back", 536870913, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)),
            out int flipped);

        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.Equal(0, tile.TileIndex);
        Assert.Equal("90,2", tile.Properties.First(p => p.Key == TmxToTbin.FlipProperty).Value);
        Assert.Equal(1, flipped);
    }

    /// <summary>H+V ⇒ 180°（effect=0）；D+H+V ⇒ -90°+垂直翻 —— 三个标志都覆盖到。</summary>
    [Theory]
    [InlineData(0xC0000001u, "180,0")]
    [InlineData(0xE0000001u, "-90,2")]
    [InlineData(0x60000001u, "-90,0")]
    public void 其余翻转组合的标记值(uint gid, string expected)
    {
        TBin10 map = Convert(Map(ZlibLayer("Back", gid, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)), out _);

        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.Equal(expected, tile.Properties.First(p => p.Key == TmxToTbin.FlipProperty).Value);
    }

    /// <summary>
    /// **Stardew 的 tmx 约定**：objectgroup 与图层同名，<c>name="TileData"</c> 的对象
    /// 按像素坐标画在格子上，属性要折算到那一格的 tile 上。当成无关数据丢掉就是丢属性。
    /// </summary>
    [Fact]
    public void objectgroup的TileData折算到图块属性()
    {
        TBin10 map = Convert(Map(CsvBack + ZlibLayer("Buildings", Empty16()), TileData));

        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.Equal("T", tile.Properties.First(p => p.Key == "NPCBarrier").Value);
        // tileset 那套仍在**图集**上，不因 objectgroup 折算而跑到图块上。
        Assert.DoesNotContain(tile.Properties, p => p.Key == "Type");
        Assert.Equal("Grass",
            map.TileSheets[0].Properties.First(p => p.Key == "@TileIndex@1@Type").Value);
    }

    [Fact]
    public void 非TileData的对象被忽略()
    {
        TBin10 map = Convert(Map(CsvBack + ZlibLayer("Buildings", Empty16()), TileData));

        // 夹具里还有一个 name="Other" 的对象，它不该折算成任何图块属性。
        Assert.DoesNotContain(
            map.Layers[0].Tiles.OfType<BaseTile>(),
            t => t.Properties != null && t.Properties.Any(p => p.Key == "X"));
    }

    // ────────────────────────── 只读回来才算数 ──────────────────────────

    /// <summary>序列化再读回来，结构必须一致 —— 只比「对象有字段」证明不了写出的二进制是对的。</summary>
    [Fact]
    public void 序列化后回读结构一致()
    {
        TBin10 map = Convert(Map(CsvBack + ZlibLayer("Buildings", Empty16()), TileData));

        string path = Path.Combine(_dir, "out.tbin");
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, path);
        TBin10 back = TBin10Reader.FormFile(path);

        Assert.Equal(map.Layers.Count, back.Layers.Count);
        Assert.Equal(map.TileSheets.Count, back.TileSheets.Count);
        for (int i = 0; i < map.Layers.Count; i++)
        {
            Assert.Equal(map.Layers[i].Tiles.Count, back.Layers[i].Tiles.Count);
            Assert.Equal(map.Layers[i].LayerSize.X, back.Layers[i].LayerSize.X);
        }
        Assert.Equal("Grass",
            back.TileSheets[0].Properties.First(p => p.Key == "@TileIndex@1@Type").Value);
        Assert.IsType<AnimatedTile>(back.Layers[0].Tiles[1]);
        var flipped = (StaticTile)back.Layers[0].Tiles[2]!;
        Assert.Equal(0, flipped.TileIndex);
        Assert.Equal("0,1", flipped.Properties.First(p => p.Key == TmxToTbin.FlipProperty).Value);
    }

    // ────────────────────────── 不支持的形态要拒绝 ──────────────────────────

    [Fact]
    public void 非正交地图被拒绝()
    {
        Reject("""<map orientation="isometric" width="4" height="4" tilewidth="16" tileheight="16"/>""",
            "只支持正交");
    }

    [Fact]
    public void 无限地图被拒绝()
    {
        Reject("""<map orientation="orthogonal" infinite="1" width="4" height="4" tilewidth="16" tileheight="16"/>""",
            "不支持无限地图");
    }

    [Fact]
    public void 未知图层编码被拒绝()
    {
        Reject(Map("""<layer name="Back" width="4" height="4"><data encoding="base32">AAAA</data></layer>"""),
            "不支持的图层数据编码");
    }

    [Fact]
    public void 图块数与地图尺寸不符时拒绝()
    {
        Reject(Map("""<layer name="Back" width="4" height="4"><data encoding="csv">1,2,3</data></layer>"""),
            "与地图尺寸");
    }

    /// <summary>
    /// objectgroup 名字对不上图层时 **跳过**，不是拒绝整张图 —— 照抄真实实现
    /// （<c>TMXTile.dll</c> 的 <c>LoadObjects</c>：<c>if (layer == null) continue;</c>）。
    /// 那组属性在游戏里本来就不生效，我们拒绝整张图反而比游戏更严。
    /// </summary>
    [Fact]
    public void objectgroup找不到同名图层时跳过()
    {
        TBin10 map = Convert(Map(
            """<layer name="Back" width="4" height="4"><data encoding="csv">1,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0</data></layer>""",
            """
            <objectgroup name="Nope">
             <object id="1" name="TileData" x="0" y="0" width="16" height="16">
              <properties><property name="K" value="V"/></properties>
             </object>
            </objectgroup>
            """));

        Assert.Single(map.Layers);
        Assert.Empty(map.Layers[0].Tiles[0]!.Properties);
    }

    // ── 两条从真实语料补上的缺口 ──────────────────────────────────────────

    /// <summary>
    /// ⚠ <b>没有 `<图层>Spouse` 这种回退</b> —— 曾经照 RSV 语料加过，现已撤掉。
    /// <para>
    /// 真实实现只做<b>精确同名</b>匹配（<c>TMXTile.LoadObjects</c> 就一句
    /// <c>map.GetLayer(objectgroup.Name)</c>），名字对不上就 <c>continue</c>；
    /// 游戏侧（<c>sdv_src</c>）也**搜不到**任何针对 <c>Spouse</c> 后缀的图层/对象组处理。
    /// </para>
    /// <para>
    /// 加回退的代价不是"多做了点" —— 是**应用了游戏不会应用的属性**，产物与游戏行为不一致。
    /// 实测 RSV 的 <c>RidgesideSpouseRooms.tmx</c> 里 <c>FrontSpouse</c>/<c>AlwaysFrontSpouse</c>
    /// 两组属性在游戏里就是**不生效的死数据**。
    /// </para>
    /// </summary>
    [Fact]
    public void objectgroup的Spouse后缀不回退到同名图层()
    {
        TBin10 map = Convert(Map(
            """<layer name="Front" width="4" height="4"><data encoding="csv">1,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0</data></layer>""",
            """
            <objectgroup name="FrontSpouse">
             <object id="1" name="TileData" x="0" y="0" width="16" height="16">
              <properties><property name="NPCBarrier" value="T"/></properties>
             </object>
            </objectgroup>
            """));

        var tile = (StaticTile)map.Layers[0].Tiles[0]!;
        Assert.DoesNotContain(tile.Properties, p => p.Key == "NPCBarrier");
    }

    /// <summary>
    /// <c>TileData</c> 对象按 <c>Width</c>/<c>Height</c> 覆盖**一个矩形**，不是只落左上那一格
    /// —— 照抄 <c>TMXTile.LoadObjects</c> 的双层循环。只落一格会让宽对象的属性漏掉大半。
    /// </summary>
    [Fact]
    public void TileData对象按宽高覆盖矩形()
    {
        TBin10 map = Convert(Map(
            """<layer name="Back" width="4" height="4"><data encoding="csv">1,1,1,1, 1,1,1,1, 1,1,1,1, 1,1,1,1</data></layer>""",
            """
            <objectgroup name="Back">
             <object id="1" name="TileData" x="16" y="16" width="32" height="32">
              <properties><property name="K" value="V"/></properties>
             </object>
            </objectgroup>
            """));

        // (1,1)、(2,1)、(1,2)、(2,2) 四格都要有；外面的格子不能有。
        for (int x = 1; x <= 2; x++)
            for (int y = 1; y <= 2; y++)
                Assert.Contains(map.Layers[0].Tiles[y * 4 + x]!.Properties, p => p.Key == "K");
        Assert.Empty(map.Layers[0].Tiles[0]!.Properties);
        Assert.Empty(map.Layers[0].Tiles[3 * 4 + 3]!.Properties);
    }

    /// <summary>**没有 tileset 是合法的** —— 只有对象、没有图块的地图（RSV 实测有）。</summary>
    [Fact]
    public void 没有tileset的地图可以转换()
    {
        string tmx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.5" orientation="orthogonal" width="4" height="4" tilewidth="16" tileheight="16" infinite="0">
         <layer name="Back" width="4" height="4">
          <data encoding="csv">0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0</data>
         </layer>
        </map>
        """;
        string path = Path.Combine(_dir, "map.tmx");
        File.WriteAllText(path, tmx);

        Assert.True(TmxToTbin.TryConvert(path, out TBin10? map, out string? error, out _), error);
        Assert.Empty(map!.TileSheets);
        Assert.Single(map.Layers);
    }

    /// <summary>真有图块却找不到 tileset 时仍要报错（别把"没 tileset"变成静默放过）。</summary>
    [Fact]
    public void 有图块但没有tileset时仍报错()
    {
        Reject("""
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.5" orientation="orthogonal" width="4" height="4" tilewidth="16" tileheight="16" infinite="0">
         <layer name="Back" width="4" height="4">
          <data encoding="csv">1,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0</data>
         </layer>
        </map>
        """, "gid");
    }

    /// <summary>
    /// ★ **外部 `.tsx`**：`&lt;tileset firstgid="1" source="foo.tsx"/&gt;` —— 定义在另一个文件里（**相对 tmx 所在目录** 解析）。
    /// 实测语料 `Crystal Island` 等 16 条地图就是这么写的，以前一律拒 ⇒ **整张地图不写**。
    /// </summary>
    [Fact]
    public void 外部tsx也能转()
    {
        File.WriteAllText(Path.Combine(_dir, "sheet.tsx"), """
        <?xml version="1.0" encoding="UTF-8"?>
        <tileset version="1.10" tiledversion="1.10.2" name="outdoor" tilewidth="16" tileheight="16" tilecount="4" columns="2">
          <image source="outdoor.png" width="32" height="32"/>
          <tile id="1"><properties><property name="Type" value="Dirt"/></properties></tile>
        </tileset>
        """);

        TBin10 map = Convert("""
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.10" orientation="orthogonal" width="2" height="1" tilewidth="16" tileheight="16">
          <tileset firstgid="1" source="sheet.tsx"/>
          <layer name="Back" width="2" height="1">
            <data encoding="csv">1,2</data>
          </layer>
        </map>
        """);

        TileSheet sheet = Assert.Single(map.TileSheets!);
        Assert.Equal("outdoor", sheet.Id);                       // Id 取 tsx 的 name
        Assert.Equal("outdoor.png", sheet.Image);                // Image 取 tsx 里 image 的 source
        Assert.Equal(2, sheet.SheetSize!.Value.X);              // 32/16
    }

    /// <summary>反向边界：**图集集合**（每格一张 &lt;image&gt;）本质上表示不了 ⇒ 该拒，且理由要说出是“集合”。</summary>
    [Fact]
    public void 图集集合该拒()
    {
        Reject("""
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.10" orientation="orthogonal" width="1" height="1" tilewidth="16" tileheight="16">
          <tileset firstgid="1" name="coll" tilewidth="16" tileheight="16" tilecount="1" columns="0">
            <tile id="0"><image source="a.png" width="16" height="16"/></tile>
          </tileset>
          <layer name="Back" width="1" height="1"><data encoding="csv">1</data></layer>
        </map>
        """, "图集集合");
    }
}
