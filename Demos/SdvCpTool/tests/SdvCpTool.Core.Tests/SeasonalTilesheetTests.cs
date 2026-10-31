using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Normalizing;
using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 四季<b>图集</b>：文件名季节前缀（<c>GameLocation.cs:16439-16447</c>）+ 地图
/// <c>TileSheet.ImageSource</c> 改指春天那份。
/// <para>
/// 与 <see cref="SeasonalSpritePlanner"/> 是**两套**机制，判据与产物都不同，不能混用。
/// </para>
/// </summary>
public class SeasonalTilesheetTests : IDisposable
{
    private const string Target = "Tilesheets/ChairTiles";
    private const int Cell = 16;

    private readonly string _in = Path.Combine(Path.GetTempPath(), "sdvst_in_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvst_out_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvst_pack_" + Guid.NewGuid().ToString("N"));

    private static readonly (string Season, byte R, byte G, byte B)[] Seasons =
    {
        ("Spring", 10, 0, 0), ("Summer", 0, 20, 0), ("Fall", 0, 0, 30), ("Winter", 40, 40, 40),
    };

    private const string ImageConfig =
        """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0" } ] }""";

    public SeasonalTilesheetTests()
    {
        Directory.CreateDirectory(_in);
        Directory.CreateDirectory(_pack);
        Directory.CreateDirectory(Path.Combine(_in, "Tilesheets"));

        // 底图：一格 16x16 的纯色（季节变体会整块盖掉它）。
        Png.Write(Path.Combine(_in, "Tilesheets", "ChairTiles.png"), Solid(200, 200, 200), Cell, Cell);
        File.WriteAllText(Path.Combine(_in, "Tilesheets", "ChairTiles.config"), ImageConfig);

        // 地图：图集 Image 指向目标资产（用真实 tbin 改图集名，保证"真文件能读能写"）。
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin");
        Directory.CreateDirectory(Path.Combine(_in, "Maps"));
        TBin10 map = TBin10Reader.FormFile(fixture);
        map.TileSheets[0].Image = Target;
        Directory.CreateDirectory(Path.Combine(_in, "Maps"));
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, Path.Combine(_in, "Maps", "Test.tbin"));
    }

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路，图片的落盘权又在 `ImageCache` 手里。</summary>
    private void Flush()
    {
        _images?.Flush();
        _maps?.Flush();
        _tree?.Flush();
    }

    public void Dispose()
    {
        foreach (string d in new[] { _in, _pack, _out })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private static byte[] Solid(byte r, byte g, byte b)
    {
        var px = new byte[Cell * Cell * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255; }
        return px;
    }

    private void PackSource(string name, byte r, byte g, byte b) =>
        Png.Write(Path.Combine(_pack, name), Solid(r, g, b), Cell, Cell);

    /// <summary>一条条件 <c>EditImage</c>：把整张源图铺到目标图集的 (0,0)。</summary>
    private static PatchVerdict Edit(string season, string fromFile, params (string Key, JToken Value)[] extra)
    {
        var when = new JObject { ["Season"] = season };
        foreach ((string k, JToken v) in extra) when[k] = v;

        var raw = new JObject
        {
            ["Action"] = "EditImage",
            ["Target"] = Target,
            ["FromFile"] = fromFile,
            ["When"] = when,
            ["ToArea"] = new JObject { ["X"] = 0, ["Y"] = 0, ["Width"] = Cell, ["Height"] = Cell },
        };
        return new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Raw = raw, Action = "EditImage", Target = Target,
                When = when, FromFile = fromFile, SourceFile = "content.json",
            },
            Verdict = Verdict.NoLandingPoint,
        };
    }

    private List<PatchVerdict> FourSeasons(params (string Key, JToken Value)[] extra)
    {
        var list = new List<PatchVerdict>();
        foreach ((string s, byte r, byte g, byte b) in Seasons)
        {
            string file = s + ".png";
            PackSource(file, r, g, b);
            list.Add(Edit(s, file, extra));
        }
        return list;
    }

    private OutputTree? _tree;
    private OutputTree Tree() => _tree ??= new(_in, _out);

    /// <summary>树 + 它的图片内存模型。图片只在 `Images.Flush()` 时才落盘。</summary>
    private ImageCache? _images;
    private ImageCache Images => _images ??= new ImageCache(Tree());

    /// <summary>地图内存模型 —— 季节图集发射器要靠它找「谁引用了这张图集」。</summary>
    private MapCache? _maps;
    private MapCache Maps => _maps ??= new MapCache(Tree());

    private (byte R, byte G, byte B) PixelAt(string rel)
    {
        Flush();
        byte[] d = Png.Read(Path.Combine(_out, rel.Replace('/', Path.DirectorySeparatorChar)), out int w, out _);
        Assert.True(w >= Cell, $"{rel} 宽 {w} 不够");
        return (d[0], d[1], d[2]);
    }

    // ── 规划 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 四季齐备时成组()
    {
        SeasonalTilesheetGroup g = Assert.Single(SeasonalTilesheetPlanner.Plan(FourSeasons()));

        Assert.Null(g.Reject);
        Assert.Equal(4, g.BySeason.Count);
        Assert.Empty(g.LostConditions);
    }

    /// <summary>
    /// 缺季不拒绝：发射器会用底图补那一季。四季图集机制要求四份文件都存在，
    /// 缺季复制底图 = CP 下“该季没有补丁”的效果。
    /// </summary>
    [Fact]
    public void 缺一季时用底图补齐()
    {
        var verdicts = FourSeasons();
        verdicts.RemoveAt(3);

        SeasonalTilesheetGroup g = Assert.Single(SeasonalTilesheetPlanner.Plan(verdicts));

        Assert.Null(g.Reject);
        Assert.Equal(3, g.BySeason.Count);
    }

    /// <summary>
    /// 容忍表外的条件：能给**静态求值**就按值算（真 → 计入该季，假 → 跳过该条）。
    /// 实测语料里唯一的四季图集组就带 <c>SeasonalEdits</c>（CP 配置令牌，默认 true）——
    /// 不求值这个功能就是个零。
    /// </summary>
    [Fact]
    public void 配置令牌求值为真时该条计入()
    {
        var verdicts = FourSeasons(("SeasonalEdits", true));

        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(verdicts, evaluate: (_, _) => true)[0];

        Assert.Null(g.Reject);
        Assert.All(g.BySeason.Values, list => Assert.Single(list));
    }

    /// <summary>
    /// 求值为假 ⇒ 该条不适用，**只跳过这一条**，季里还有别的补丁就照常成组。
    /// </summary>
    [Fact]
    public void 配置令牌求值为假时只跳过该条()
    {
        var verdicts = FourSeasons();
        PackSource("Extra.png", 1, 2, 3);
        verdicts.Add(Edit("Spring", "Extra.png", ("OnlyWhenEnabled", true)));   // 春季第二条

        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(verdicts,
            evaluate: (name, _) => name == "OnlyWhenEnabled" ? false : true)[0];

        Assert.Null(g.Reject);
        Assert.Single(g.BySeason["Spring"]);                       // 被跳过的那条不算
        Assert.Equal("Spring.png", g.BySeason["Spring"][0].Patch.FromFile);
    }

    /// <summary>
    /// 某一季的补丁**被跳过到一条不剩** ⇒ 那一季就没有变体可写，整组不成 ——
    /// 少一季的四季图集比不成组更糟（那季会显示成别的季节的图）。
    /// </summary>
    [Fact]
    public void 所有季都被跳空时不成组()
    {
        var verdicts = FourSeasons(("OnlyWhenEnabled", true));

        Assert.Contains("没有任何一季",
            SeasonalTilesheetPlanner.Plan(verdicts, evaluate: (_, _) => false)[0].Reject);
    }

    [Fact]
    public void 求不了值的额外条件时不成组()
    {
        var verdicts = FourSeasons(("HasSeenEvent", "12345"));

        Assert.Contains("无法静态求值", SeasonalTilesheetPlanner.Plan(verdicts)[0].Reject);
    }

    [Fact]
    public void 仅室外条件成组但标近似()
    {
        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(FourSeasons(("IsOutdoors", true)))[0];

        Assert.Null(g.Reject);
        Assert.Equal(new[] { "IsOutdoors" }, g.LostConditions);
    }

    // ── 发射 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 发射四份带季节前缀的图集且像素正确()
    {
        var verdicts = FourSeasons();
        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(verdicts)[0];

        Assert.True(SeasonalTilesheetEmitter.TryEmit(Tree(), Images, Maps, _pack, g, out var res, out string? err), err);

        Assert.Equal(4, res!.Assets.Count);
        for (int i = 0; i < 4; i++)
        {
            string rel = $"Tilesheets/{Seasons[i].Season.ToLowerInvariant()}_ChairTiles.png";
            Assert.Contains(rel, res.Assets);
            Assert.Equal((Seasons[i].R, Seasons[i].G, Seasons[i].B), PixelAt(rel));
        }
    }

    /// <summary>缺一季时用底图补齐那一季；四份文件仍必须齐全。</summary>
    [Fact]
    public void 缺一季时用底图补齐该季()
    {
        var verdicts = FourSeasons();
        verdicts.RemoveAt(3);                       // 去掉 Winter
        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(verdicts)[0];
        Assert.Null(g.Reject);

        Assert.True(SeasonalTilesheetEmitter.TryEmit(Tree(), Images, Maps, _pack, g, out var res, out string? err), err);

        Assert.Equal(4, res!.Assets.Count);
        Assert.Equal((200, 200, 200), PixelAt("Tilesheets/winter_ChairTiles.png"));   // 底图纯色
        Assert.Equal((0, 20, 0), PixelAt("Tilesheets/summer_ChairTiles.png"));
    }

    /// <summary>地图的图集指向必须改成<b>春天</b>那份 —— 游戏从它出发按季替换前缀。</summary>
    [Fact]
    public void 地图图集指向改为春天那份()
    {
        var verdicts = FourSeasons();
        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(verdicts)[0];
        Assert.True(SeasonalTilesheetEmitter.TryEmit(Tree(), Images, Maps, _pack, g, out var res, out _));
        Maps.Flush();                 // 地图由内存模型统一落盘

        Assert.Single(res!.Maps);
        TBin10 after = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Test.tbin"));
        Assert.Equal("Tilesheets/spring_ChairTiles", after.TileSheets[0].Image);
    }

    [Fact]
    public void 四份产物都配了图片config()
    {
        var verdicts = FourSeasons();
        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(verdicts)[0];
        Assert.True(SeasonalTilesheetEmitter.TryEmit(Tree(), Images, Maps, _pack, g, out _, out _));
Flush();

        foreach (string s in new[] { "spring", "summer", "fall", "winter" })
            Assert.True(File.Exists(Path.Combine(_out, "Tilesheets", $"{s}_ChairTiles.config")), s);
    }

    /// <summary>没有地图引用它时**不发射** —— 季节前缀靠地图的 ImageSource 生效，写了也是死资产。</summary>
    [Fact]
    public void 没有地图引用时不发射()
    {
        // 把地图的图集指到别处，目标就没人引用了。
        TBin10 map = TBin10Reader.FormFile(Path.Combine(_in, "Maps", "Test.tbin"));
        map.TileSheets[0].Image = "Tilesheets/其他";
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, Path.Combine(_in, "Maps", "Test.tbin"));

        var verdicts = FourSeasons();
        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(verdicts)[0];

        Assert.False(SeasonalTilesheetEmitter.TryEmit(Tree(), Images, Maps, _pack, g, out _, out string? err));
        Assert.Contains("没有任何地图", err);
        Assert.False(File.Exists(Path.Combine(_out, "Tilesheets", "spring_ChairTiles.png")));
    }

    /// <summary>
    /// 图集名匹配用 SMAPI 的同一套归一：去 <c>Maps/</c> 前缀、去 <c>.png</c>。
    /// 地图写 <c>.png</c> 结尾也要匹配上。
    /// </summary>
    [Fact]
    public void 地图图集名带png后缀时也能匹配()
    {
        TBin10 map = TBin10Reader.FormFile(Path.Combine(_in, "Maps", "Test.tbin"));
        map.TileSheets[0].Image = Target + ".png";
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, Path.Combine(_in, "Maps", "Test.tbin"));

        var verdicts = FourSeasons();
        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(verdicts)[0];

        Assert.True(SeasonalTilesheetEmitter.TryEmit(Tree(), Images, Maps, _pack, g, out var res, out string? err), err);
        Maps.Flush();                 // 地图由内存模型统一落盘
        TBin10 after = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Test.tbin"));
        // 前缀加在**文件名**上；落盘前图集引用归一成原版写法（去掉 `.png`、解析 `..`）。
        Assert.Equal("Tilesheets/spring_ChairTiles", after.TileSheets[0].Image);
        Assert.Single(res!.Maps);
    }

    [Fact]
    public void 底图不存在时不发射()
    {
        File.Delete(Path.Combine(_in, "Tilesheets", "ChairTiles.png"));
        var verdicts = FourSeasons();
        SeasonalTilesheetGroup g = SeasonalTilesheetPlanner.Plan(verdicts)[0];

        Assert.False(SeasonalTilesheetEmitter.TryEmit(Tree(), Images, Maps, _pack, g, out _, out string? err));
        Assert.Contains("找不到底图", err);
    }
}
