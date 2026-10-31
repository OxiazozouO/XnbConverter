using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Normalizing;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// Task 29 的规划器：4 条条件 <c>EditImage</c> 合成一个「四季 BigCraftable」意图。
/// <para>
/// 最要命的一条：<b>前提不成立就必须整组保持原判</b> —— 半套发射会把条件静默丢掉。
/// </para>
/// </summary>
public class SeasonalSpritePlannerTests
{
    private const string Tex = "Mods/TestPack/Shrub";
    private const string Entry = "TestPack_Shrub";

    /// <summary>造一条 <c>EditImage</c>：把某季的 16x32 图块写进 (0,0,16,32)。</summary>
    private static PatchVerdict Edit(string target, string? season, int fromX = 0,
        params (string Key, JToken Value)[] extraWhen)
    {
        var when = new JObject();
        if (season != null) when["Season"] = season;
        foreach ((string key, JToken value) in extraWhen) when[key] = value;

        var raw = new JObject
        {
            ["Action"] = "EditImage",
            ["Target"] = target,
            ["FromFile"] = "assets/SeasonalJA.png",
            ["When"] = when,
            ["FromArea"] = new JObject { ["X"] = fromX, ["Y"] = 0, ["Width"] = 16, ["Height"] = 32 },
            ["ToArea"] = new JObject { ["X"] = 0, ["Y"] = 0, ["Width"] = 16, ["Height"] = 32 },
        };
        return new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Raw = raw, Action = "EditImage", Target = target,
                When = when, FromFile = "assets/SeasonalJA.png",
                SourceFile = "content.json",
            },
            Verdict = Verdict.NoLandingPoint,
        };
    }

    /// <summary>造创建 BigCraftables 条目的 <c>EditData</c> 补丁（消费方来源）。</summary>
    private static PatchVerdict Consumer(string key, string texture, int spriteIndex, string name = "Shrub")
    {
        var raw = new JObject
        {
            ["Action"] = "EditData",
            ["Target"] = "Data/BigCraftables",
            ["Entries"] = new JObject
            {
                [key] = new JObject { ["Name"] = name, ["Texture"] = texture, ["SpriteIndex"] = spriteIndex },
            },
        };
        return new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Raw = raw, Action = "EditData", Target = "Data/BigCraftables", SourceFile = "content.json",
            },
            Verdict = Verdict.ConvertedNoCondition,
        };
    }

    private static List<PatchVerdict> FourSeasons(string target = Tex,
        params (string Key, JToken Value)[] extraWhen)
    {
        var list = new List<PatchVerdict>();
        string[] seasons = { "Spring", "Summer", "Fall", "Winter" };
        for (int i = 0; i < 4; i++)
            list.Add(Edit(target, seasons[i], i * 16, extraWhen));
        return list;
    }

    // ── 成组 ──────────────────────────────────────────────────────────────

    [Fact]
    public void 四季齐备且消费方唯一时成组()
    {
        var verdicts = FourSeasons();
        verdicts.Add(Consumer(Entry, Tex, 0));

        var groups = SeasonalSpritePlanner.Plan(verdicts);

        Assert.Single(groups);
        SeasonalSpriteGroup g = groups[0];
        Assert.Null(g.Reject);
        Assert.Equal(Tex, g.Target);
        Assert.Equal(Entry, g.BigCraftableEntry);
        Assert.Empty(g.LostConditions);
        Assert.Equal(new[] { "Spring", "Summer", "Fall", "Winter" }, g.Variants.Select(v => v.Season));
    }

    /// <summary>
    /// 变体顺序由 <c>(int)Season</c> 定，<b>不按补丁出现顺序</b>。
    /// 写成「夏在前」也必须还原成春夏秋冬 —— 索引错位会让四季整体错位。
    /// </summary>
    [Fact]
    public void 变体按春夏秋冬排序而非补丁顺序()
    {
        var verdicts = new List<PatchVerdict>
        {
            Edit(Tex, "Winter", 48), Edit(Tex, "Fall", 32),
            Edit(Tex, "Spring", 0), Edit(Tex, "Summer", 16),
            Consumer(Entry, Tex, 0),
        };

        var groups = SeasonalSpritePlanner.Plan(verdicts);

        Assert.Equal(new[] { "Spring", "Summer", "Fall", "Winter" }, groups[0].Variants.Select(v => v.Season));
        Assert.Equal(new[] { 0, 16, 32, 48 }, groups[0].Variants.Select(v => v.FromArea.X));
    }

    /// <summary>
    /// <c>IsOutdoors</c> 原生机制装不下（<c>Object.cs:1940</c> 不看室内外），
    /// 但**仍然成组** —— 只是必须标「近似」，不得当成无损。
    /// </summary>
    [Fact]
    public void 仅室外条件成组但标近似()
    {
        var verdicts = FourSeasons(extraWhen: [("IsOutdoors", true)]);
        verdicts.Add(Consumer(Entry, Tex, 0));

        var groups = SeasonalSpritePlanner.Plan(verdicts);

        Assert.Null(groups[0].Reject);
        Assert.Equal(new[] { "IsOutdoors" }, groups[0].LostConditions);
    }

    // ── 拒绝（整组保持原判）──────────────────────────────────────────────

    /// <summary>缺一季时用底图补那一格；四季 BigCraftable 必须正好四格。</summary>
    [Fact]
    public void 缺一季时用底图补齐()
    {
        var verdicts = new List<PatchVerdict>
        {
            Edit(Tex, "Spring"), Edit(Tex, "Summer"), Edit(Tex, "Fall"),
            Consumer(Entry, Tex, 0),
        };

        SeasonalSpriteGroup g = SeasonalSpritePlanner.Plan(verdicts)[0];

        Assert.Null(g.Reject);
        Assert.Equal(4, g.Variants.Count);
        Assert.Equal(3, g.Variants.Count(v => v.FromFile.Length > 0));
        Assert.Contains(g.Variants, v => v.Season == "Winter" && v.FromFile.Length == 0);
    }

    [Fact]
    public void 同一季重复时不成组()
    {
        var verdicts = new List<PatchVerdict>
        {
            Edit(Tex, "Spring", 0), Edit(Tex, "Spring", 16),
            Edit(Tex, "Fall"), Edit(Tex, "Winter"),
            Consumer(Entry, Tex, 0),
        };

        Assert.Contains("出现两条", SeasonalSpritePlanner.Plan(verdicts)[0].Reject);
    }

    /// <summary>
    /// 除季节外的**其它**条件（这里是 <c>HasSeenEvent</c>）原生装不下 ——
    /// 硬发射会把它静默丢掉，所以整组不发射。
    /// </summary>
    [Fact]
    public void 有装不下的其它条件时不成组()
    {
        var verdicts = FourSeasons(extraWhen: [("HasSeenEvent", "12345")]);
        verdicts.Add(Consumer(Entry, Tex, 0));

        Assert.Contains("HasSeenEvent", SeasonalSpritePlanner.Plan(verdicts)[0].Reject);
    }

    [Fact]
    public void 找不到消费方时不成组()
    {
        var groups = SeasonalSpritePlanner.Plan(FourSeasons());

        Assert.Contains("找不到", groups[0].Reject);
    }

    /// <summary>两个条目共用一张贴图时说不清改哪一个 —— 不发射。</summary>
    [Fact]
    public void 消费方不唯一时不成组()
    {
        var verdicts = FourSeasons();
        verdicts.Add(Consumer(Entry, Tex, 0));
        verdicts.Add(Consumer("TestPack_Other", Tex, 0));

        Assert.Contains("无法确定改哪一个", SeasonalSpritePlanner.Plan(verdicts)[0].Reject);
    }

    /// <summary>
    /// <c>SpriteIndex</c> 非 0 时索引会指到贴图外（本发射把整张贴图换成 4 格）—— 不发射。
    /// </summary>
    [Fact]
    public void 消费方SpriteIndex非零时不成组()
    {
        var verdicts = FourSeasons();
        verdicts.Add(Consumer(Entry, Tex, 8));

        Assert.Contains("SpriteIndex", SeasonalSpritePlanner.Plan(verdicts)[0].Reject);
    }

    [Fact]
    public void ToArea不一致时不成组()
    {
        var verdicts = FourSeasons();
        ((JObject)verdicts[3].Patch.Raw["ToArea"]!)["X"] = 16;   // 第 4 条写去别处
        verdicts.Add(Consumer(Entry, Tex, 0));

        Assert.Contains("ToArea 不一致", SeasonalSpritePlanner.Plan(verdicts)[0].Reject);
    }

    [Fact]
    public void 带PatchMode时不成组()
    {
        var verdicts = FourSeasons();
        foreach (PatchVerdict v in verdicts) v.Patch.Raw["PatchMode"] = "Overlay";
        verdicts.Add(Consumer(Entry, Tex, 0));

        Assert.Contains("PatchMode", SeasonalSpritePlanner.Plan(verdicts)[0].Reject);
    }

    /// <summary>
    /// **Target 含令牌的一律不参与分组** —— 静态确定不了它是哪个资产。
    /// <para>
    /// 实测代价：SVE 的 <c>Maps/{{season}}_z_extras</c> 4 条（条件其实是
    /// <c>HasMod: &lt;重着色 mod&gt;</c>，<c>{{season}}</c> 只在运行期展开）被按 target 字符串
    /// 分成一组、当成「四季变体」，还**覆盖写**了「四季组未发射」——
    /// 而那 4 条本已被正确判为 <c>NotApplicable</c>（没装那些 mod ⇒ 恒假）。
    /// </para>
    /// </summary>
    [Fact]
    public void Target含令牌时不参与分组()
    {
        var verdicts = new List<PatchVerdict>();
        foreach (string s in new[] { "Spring", "Summer", "Fall", "Winter" })
            verdicts.Add(Edit("Maps/{{season}}_z_extras", s, 0));
        verdicts.Add(Consumer(Entry, Tex, 0));

        Assert.Empty(SeasonalSpritePlanner.Plan(verdicts));
    }

    /// <summary>
    /// <c>{"Season |contains=Spring": true}</c> 与 <c>{"Season": "Spring"}</c> **严格等价** ——
    /// <c>Season</c> 令牌的值集只有单元素（当前季节），「与 {Spring} 有交集」就是「当前季节 = Spring」。
    /// 实测依据：SVE 的 <c>Maps/Spring_z_extras</c> 四季变体就是这么写的。
    /// </summary>
    [Fact]
    public void 季节用contains写法且声明值为真时成组()
    {
        var verdicts = new List<PatchVerdict>();
        int i = 0;
        foreach (string s in new[] { "Spring", "Summer", "Fall", "Winter" })
        {
            PatchVerdict v = Edit(Tex, null, i * 16);
            v.Patch.When = JObject.Parse($$"""{ "Season |contains={{s}}": true }""");
            v.Patch.Raw["When"] = v.Patch.When;
            verdicts.Add(v);
            i++;
        }
        verdicts.Add(Consumer(Entry, Tex, 0));

        SeasonalSpriteGroup g = SeasonalSpritePlanner.Plan(verdicts)[0];
        Assert.Null(g.Reject);
        Assert.Equal(new[] { "Spring", "Summer", "Fall", "Winter" }, g.Variants.Select(v => v.Season));
    }

    /// <summary>
    /// 四季**各自**写 <c>|contains=X: false</c> 时，每条覆盖另外三季，同一季会命中多条 ——
    /// 无法确定用哪张图，仍拒绝（单条补集覆盖多季是支持的）。
    /// </summary>
    [Fact]
    public void 四季各自取反时同一季重复不成组()
    {
        var verdicts = new List<PatchVerdict>();
        foreach (string s in new[] { "Spring", "Summer", "Fall", "Winter" })
        {
            PatchVerdict v = Edit(Tex, null);
            v.Patch.When = JObject.Parse($$"""{ "Season |contains={{s}}": false }""");
            v.Patch.Raw["When"] = v.Patch.When;
            verdicts.Add(v);
        }
        verdicts.Add(Consumer(Entry, Tex, 0));

        Assert.Contains("出现两条", SeasonalSpritePlanner.Plan(verdicts)[0].Reject);
    }

    /// <summary>
    /// 会被逐条写入的补丁不参与集合发射 —— 否则同一条补丁被两条路径各写一遍。
    /// </summary>
    [Fact]
    public void 已会被逐条写入的补丁不参与成组()
    {
        var verdicts = FourSeasons();
        verdicts.Add(Consumer(Entry, Tex, 0));
        // 把四条都改成「无条件可写」—— 规划器应把它们全部排除。
        foreach (PatchVerdict v in verdicts)
        {
            v.Verdict = Verdict.ConvertedNoCondition;
            if (v.Patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase))
                v.Patch.When = null;
        }

        Assert.Empty(SeasonalSpritePlanner.Plan(verdicts));
    }

    /// <summary>贴图路径里的反斜杠与正斜杠必须归一 —— 否则消费方查不到。</summary>
    [Fact]
    public void 反斜杠贴图路径能与消费方对上()
    {
        var verdicts = FourSeasons();
        verdicts.Add(Consumer(Entry, @"Mods\TestPack\Shrub", 0));

        Assert.Null(SeasonalSpritePlanner.Plan(verdicts)[0].Reject);
    }

    /// <summary>
    /// 带参数的容忍条件按**基名**比白名单。实测 SVE 那 52 组全带
    /// <c>"LocationContext |contains=Island, Desert"</c> —— 拿原始键去查会把它们**全误杀**。
    /// </summary>
    [Fact]
    public void 带参数的容忍条件按基名识别()
    {
        var verdicts = FourSeasons(extraWhen: [("LocationContext |contains=Island, Desert", false)]);
        verdicts.Add(Consumer(Entry, Tex, 0));

        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];

        Assert.Null(group.Reject);
        Assert.Equal(new[] { "LocationContext" }, group.LostConditions);
    }

    /// <summary>
    /// 四季必须写进 BigCraftable 的精灵格 (0,0,16,32)。
    /// 写到别处，合出来的贴图就不是游戏按 <c>SpriteIndex</c> 取的那张。
    /// </summary>
    [Fact]
    public void ToArea不是精灵格时不成组()
    {
        var verdicts = FourSeasons();
        foreach (PatchVerdict v in verdicts)
            ((JObject)v.Patch.Raw["ToArea"]!)["Y"] = 8;
        verdicts.Add(Consumer(Entry, Tex, 0));

        Assert.Contains("精灵格", SeasonalSpritePlanner.Plan(verdicts)[0].Reject);
    }

    /// <summary>区域坐标写成数字字符串（真实语料的写法）也要能规划。</summary>
    [Fact]
    public void 区域坐标是字符串时仍能成组()
    {
        var verdicts = FourSeasons();
        // 把 FromArea/ToArea 的每个值都换成字符串形态。
        foreach (PatchVerdict v in verdicts)
            foreach (string field in new[] { "FromArea", "ToArea" })
            {
                var area = (JObject)v.Patch.Raw[field]!;
                foreach (string key in new[] { "X", "Y", "Width", "Height" })
                    area[key] = area[key]!.Value<int>().ToString();
            }
        verdicts.Add(Consumer(Entry, Tex, 0));

        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];

        Assert.Null(group.Reject);
        Assert.Equal(new ImageArea(0, 0, 16, 32), group.ToArea);
    }
}
