using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Normalizing;
using SdvCpTool.Core.Workspace;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 第三套集合发射：条件 <c>EditImage</c> → <c>Data/Characters/&lt;id&gt;/Appearance[]</c>。
/// <para>
/// 机制见 <c>NPC.ChooseAppearance</c>（<c>NPC.cs:957-1066</c>）。两条**最容易写错**的：
/// 原版自己的外观条目 Precedence 是 <b>−100/−1000</b>（越小越优先），我们新增的必须**大于**它们；
/// 且原版 48 条目里 <b>31 个已有 Appearance</b>，只能**追加**不能覆盖。
/// </para>
/// </summary>
public class AppearanceTests : IDisposable
{
    private readonly string _in = Path.Combine(Path.GetTempPath(), "sdvap_in_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvap_out_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvap_pack_" + Guid.NewGuid().ToString("N"));

    private const int W = 16, H = 32;
    private const string ImageConfig =
        """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0" } ] }""";

    public AppearanceTests()
    {
        Directory.CreateDirectory(_in);
        Directory.CreateDirectory(_pack);
        Directory.CreateDirectory(Path.Combine(_in, "Characters"));
        Directory.CreateDirectory(Path.Combine(_in, "Portraits"));
        Directory.CreateDirectory(Path.Combine(_in, "Data"));

        Sheet(Path.Combine(_in, "Characters", "Clerk.png"), 9, 9, 9);
        Sheet(Path.Combine(_in, "Portraits", "Clerk.png"), 9, 9, 9);
        File.WriteAllText(Path.Combine(_in, "Characters", "Clerk.config"), ImageConfig);
        File.WriteAllText(Path.Combine(_in, "Portraits", "Clerk.config"), ImageConfig);

        // `Clerk` 是原版 NPC，且**已有** Appearance（模拟原版冬季外观）。
        File.WriteAllText(Path.Combine(_in, "Data", "Characters.json"), """
        { "Clerk": { "DisplayName": "Clerk",
                     "Appearance": [ { "Id": "Winter", "Season": "Winter", "Precedence": -100,
                                       "Sprite": "Characters/Clerk_Winter" } ] } }
        """);
        File.WriteAllText(Path.Combine(_in, "Data", "Characters.config"), "{ }");
    }

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路，图片的落盘权又在 `ImageCache` 手里。</summary>
    private void Flush()
    {
        _images?.Flush();
        _tree?.Flush();
    }

    public void Dispose()
    {
        foreach (string d in new[] { _in, _pack, _out })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private static void Sheet(string path, byte r, byte g, byte b) =>
        Png.Write(path, Solid(r, g, b), W, H);

    private static byte[] Solid(byte r, byte g, byte b)
    {
        var px = new byte[W * H * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255; }
        return px;
    }

    private void PackSource(string rel, byte r, byte g, byte b)
    {
        string full = Path.Combine(_pack, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Sheet(full, r, g, b);
    }

    private static PatchVerdict Image(string target, string fromFile, string whenJson)
    {
        var when = JObject.Parse(whenJson);
        var raw = new JObject
        {
            ["Action"] = "EditImage", ["Target"] = target, ["FromFile"] = fromFile, ["When"] = when,
        };
        return new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Raw = raw, Action = "EditImage", Target = target, FromFile = fromFile,
                When = when, SourceFile = "content.json",
            },
            Verdict = Verdict.NoLandingPoint,
        };
    }

    private static readonly string[] Known = { "Clerk" };

    private OutputTree? _tree;
    private OutputTree Tree() => _tree ??= _tree = new(_in, _out);

    /// <summary>树 + 它的图片内存模型。图片只在 `Images.Flush()` 时才落盘。</summary>
    private ImageCache? _images;
    private ImageCache Images => _images ??= new ImageCache(Tree());

    // ── 规划 ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Characters/Clerk", "Clerk", true)]
    [InlineData("Portraits/Clerk", "Clerk", false)]
    [InlineData("Characters/Monsters/Slime", null, false)]   // 多级路径不是立绘/行走图
    [InlineData("Data/Objects", null, false)]
    public void NpcOf只认一级的立绘与行走图(string asset, string? npc, bool isSprite)
    {
        Assert.Equal(npc, AppearancePlanner.NpcOf(asset, out bool s));
        if (npc != null) Assert.Equal(isSprite, s);
    }

    [Fact]
    public void 同一When的行走图与立绘合成一个变体()
    {
        PackSource("sp.png", 1, 1, 1);
        PackSource("po.png", 2, 2, 2);

        var verdicts = new List<PatchVerdict>
        {
            Image("Characters/Clerk", "sp.png", """{ "LocationName": "JojaMart" }"""),
            Image("Portraits/Clerk", "po.png", """{ "LocationName": "JojaMart" }"""),
        };

        AppearanceGroup g = Assert.Single(AppearancePlanner.Plan(verdicts, Known.ToHashSet()));

        Assert.Null(g.Reject);
        AppearanceVariant v = Assert.Single(g.Variants);
        Assert.Equal("sp.png", v.SpriteFrom);
        Assert.Equal("po.png", v.PortraitFrom);
        Assert.Contains("LOCATION_NAME", v.Gsq);
    }

    [Fact]
    public void NPC不在DataCharacters时不成组()
    {
        var verdicts = new List<PatchVerdict>
        {
            Image("Characters/Nobody", "x.png", """{ "Season": "spring" }"""),
        };

        Assert.Contains("不在 `Data/Characters` 里",
            AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0].Reject);
    }

    [Fact]
    public void 同一When下重复改同一张图时不成组()
    {
        var verdicts = new List<PatchVerdict>
        {
            Image("Characters/Clerk", "a.png", """{ "Season": "spring" }"""),
            Image("Characters/Clerk", "b.png", """{ "Season": "spring" }"""),
        };

        Assert.Contains("说不清用哪个",
            AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0].Reject);
    }

    /// <summary>
    /// 条件只剩 `IsOutdoors`（摘掉后**没有任何条件**）时不成组 ——
    /// 无条件 Appearance 条目的 Precedence 比原版高，会压住该 NPC 的其它条目，
    /// 多半不是作者本意，拒绝而不是猜。
    /// </summary>
    [Fact]
    public void 条件只剩IsOutdoors时不成组()
    {
        var verdicts = new List<PatchVerdict>
        {
            Image("Characters/Clerk", "a.png", """{ "IsOutdoors": true }"""),
        };

        Assert.Contains("只剩 `IsOutdoors`", AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0].Reject);
    }

    // ── 发射 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 发射变体资产并把原版外观前置回来()
    {
        PackSource("joja.png", 200, 0, 0);
        var verdicts = new List<PatchVerdict>
        {
            Image("Characters/Clerk", "joja.png", """{ "LocationName": "JojaMart" }"""),
        };
        AppearanceGroup g = AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0];

        Assert.True(AppearanceEmitter.TryEmit(Tree(), Images, _pack, verdicts, g, out var res, out string? err), err);

        Assert.Single(res!.Assets);
        Assert.Equal("Characters/Clerk_Appearance0", res.Assets[0]);
        Assert.NotNull(res.DirectPatchJson);   // 原版 NPC ⇒ 合成改写

        JObject patch = JObject.Parse(res.DirectPatchJson!);
        JArray arr = (JArray)patch["Fields"]!["Clerk"]!["Appearance"]!;

        // ★ 原版那条**必须还在**（前置），否则冬季外观被冲掉。
        Assert.Equal(2, arr.Count);
        Assert.Equal("Winter", arr[0]!["Id"]!.Value<string>());
        Assert.Equal(-100, arr[0]!["Precedence"]!.Value<int>());
    }

    /// <summary>
    /// 同名的 `_Appearance&lt;n&gt;` 生成资产已存在时**复用**，而不是让整组失败。
    /// 实测 Eyvinder：SCC 先发射了行走图组，East Scarp 的组要同时写行走图 + 立绘，
    /// 卡在行走图上 ⇒ 3 张肖像一份都没写。
    /// </summary>
    [Fact]
    public void 已生成同名外观资产时复用()
    {
        PackSource("sp.png", 1, 1, 1);
        PackSource("po.png", 2, 2, 2);
        Images.SetPixels("Characters/Clerk_Appearance0.png", Solid(5, 5, 5), W, H);

        var verdicts = new List<PatchVerdict>
        {
            Image("Characters/Clerk", "sp.png", """{ "LocationName": "JojaMart" }"""),
            Image("Portraits/Clerk", "po.png", """{ "LocationName": "JojaMart" }"""),
        };
        AppearanceGroup g = AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0];

        Assert.True(AppearanceEmitter.TryEmit(Tree(), Images, _pack, verdicts, g, out var res, out string? err), err);

        Assert.Contains("Characters/Clerk_Appearance0", res!.Assets);
        Assert.Contains("Portraits/Clerk_Appearance0", res.Assets);
    }

    /// <summary>
    /// 原版用 <c>−100/−1000</c>（越小越优先），新增的必须**大于**它们；
    /// 组内又必须**互不相同**（并列会走 <c>Weight</c> 加权随机 ⇒ 不确定），
    /// 且**越靠后越小**（CP 是「后写的赢」）。
    /// </summary>
    [Fact]
    public void Precedence大于原版且组内互不相同()
    {
        var verdicts = new List<PatchVerdict>();
        string[] days = { "Mon", "Tue", "Wed" };
        for (int i = 0; i < 3; i++)
        {
            PackSource($"v{i}.png", (byte)(i + 1), 0, 0);
            verdicts.Add(Image("Characters/Clerk", $"v{i}.png",
                new JObject { ["DayOfWeek"] = days[i] }.ToString(Newtonsoft.Json.Formatting.None)));
        }
        AppearanceGroup g = AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0];
        Assert.True(AppearanceEmitter.TryEmit(Tree(), Images, _pack, verdicts, g, out var res, out string? err), err);

        JArray arr = (JArray)JObject.Parse(res!.DirectPatchJson!)["Fields"]!["Clerk"]!["Appearance"]!;
        var ours = arr.Skip(1).Select(a => a["Precedence"]!.Value<int>()).ToList();

        Assert.All(ours, p => Assert.True(p > -100, $"Precedence {p} 不大于原版的 -100，会抢掉原版外观"));
        Assert.Equal(ours.Count, ours.Distinct().Count());       // 互不相同 ⇒ 不会加权随机
        Assert.True(ours[0] > ours[^1], "越靠后的补丁 Precedence 应越小（后写赢）");
    }

    /// <summary>
    /// `When` 里说「仅室外」时，必须显式写 <c>Outdoors: true, Indoors: false</c> ——
    /// 模型里这两个字段的 C# 初值**都是 true**，不写就是室内外都适用。
    /// </summary>
    [Fact]
    public void 仅室外条件写成Outdoors真Indoors假()
    {
        PackSource("out.png", 5, 5, 5);
        var verdicts = new List<PatchVerdict>
        {
            Image("Characters/Clerk", "out.png", """{ "IsOutdoors": true, "Season": "fall" }"""),
        };
        AppearanceGroup g = AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0];
        Assert.True(AppearanceEmitter.TryEmit(Tree(), Images, _pack, verdicts, g, out var res, out string? err), err);

        JToken entry = ((JArray)JObject.Parse(res!.DirectPatchJson!)["Fields"]!["Clerk"]!["Appearance"]!)[1]!;
        Assert.True(entry["Outdoors"]!.Value<bool>());
        Assert.False(entry["Indoors"]!.Value<bool>());
    }

    /// <summary>不写 `IsOutdoors` 时不碰这两个字段（交给原版初值 true/true）。</summary>
    [Fact]
    public void 没提室内外时不写这两个字段()
    {
        PackSource("any.png", 5, 5, 5);
        var verdicts = new List<PatchVerdict> { Image("Characters/Clerk", "any.png", """{ "Season": "spring" }""") };
        AppearanceGroup g = AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0];
        Assert.True(AppearanceEmitter.TryEmit(Tree(), Images, _pack, verdicts, g, out var res, out string? err), err);

        JToken entry = ((JArray)JObject.Parse(res!.DirectPatchJson!)["Fields"]!["Clerk"]!["Appearance"]!)[1]!;
        Assert.Null(entry["Outdoors"]);
        Assert.Null(entry["Indoors"]);
    }

    [Fact]
    public void 变体资产的像素等于替换源图()
    {
        PackSource("joja.png", 200, 0, 0);
        var verdicts = new List<PatchVerdict> { Image("Characters/Clerk", "joja.png", """{ "Season": "summer" }""") };
        AppearanceGroup g = AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0];
        Assert.True(AppearanceEmitter.TryEmit(Tree(), Images, _pack, verdicts, g, out _, out string? err), err);
        Images.Flush();

        byte[] d = Png.Read(Path.Combine(_out, "Characters", "Clerk_Appearance0.png"), out int w, out int h);
        Assert.Equal(W, w); Assert.Equal(H, h);
        Assert.Equal((200, 0, 0), (d[0], d[1], d[2]));
    }

    /// <summary>本包那条建条目的补丁存在时**就地追加**，不合成新改写（一条补丁一次写入）。</summary>
    [Fact]
    public void 本包建条目的补丁存在时就地追加()
    {
        PackSource("joja.png", 1, 2, 3);
        var verdicts = new List<PatchVerdict> { Image("Characters/Clerk", "joja.png", """{ "Season": "spring" }""") };
        var create = new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Action = "EditData", Target = "Data/Characters", SourceFile = "content.json",
                Raw = new JObject
                {
                    ["Action"] = "EditData",
                    ["Target"] = "Data/Characters",
                    ["Entries"] = new JObject { ["Clerk"] = new JObject { ["DisplayName"] = "Clerk" } },
                },
            },
            Verdict = Verdict.CreatesNewAsset,
        };
        verdicts.Add(create);

        AppearanceGroup g = AppearancePlanner.Plan(verdicts, Known.ToHashSet())[0];
        Assert.True(AppearanceEmitter.TryEmit(Tree(), Images, _pack, verdicts, g, out var res, out string? err), err);

        Assert.Null(res!.DirectPatchJson);      // 就地追加 ⇒ 没有单独改写
        var entries = (JObject)create.Patch.Raw["Entries"]!;
        JArray app = (JArray)entries["Clerk"]!["Appearance"]!;
        // 本包的新条目没写 Appearance ⇒ 把产出树里**已有的**（测试夹具的 Winter）也带上，
        // 否则整条替换会把它冲掉（Eyvinder 的 12 条显式外观就是这么丢的）。
        Assert.Equal(2, app.Count);
        Assert.Equal("Winter", app[0]!["Id"]!.Value<string>());
        Assert.Equal("Clerk_Appearance0", app[1]!["Id"]!.Value<string>());
    }
}
