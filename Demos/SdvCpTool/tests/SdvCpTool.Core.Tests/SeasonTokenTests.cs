using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Normalizing;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 从 <c>When</c> 里读「哪一季」—— 四季 BigCraftable 与四季图集**两套规划器共用这一处判定**。
/// <para>
/// 关键：<c>Season</c> 令牌的值集只有单元素（当前季节），所以
/// <c>|contains=Spring</c> 与 <c>= Spring</c> **严格等价**；而 <c>|contains=Spring: false</c>
/// 是**补集**（「不是春」），拆不出是哪一季。
/// </para>
/// </summary>
public class SeasonTokenTests
{
    private static string? Of(string json, out string? reason) =>
        SeasonToken.Of(JObject.Parse(json), out reason);

    [Fact]
    public void 裸写法()
    {
        Assert.Equal("Spring", Of("""{ "Season": "Spring" }""", out _));
    }

    /// <summary>大小写不敏感，且回**规范名**（否则下游按四季表查会落空）。</summary>
    [Theory]
    [InlineData("spring", "Spring")]
    [InlineData("SUMMER", "Summer")]
    [InlineData("fall", "Fall")]
    [InlineData("Winter", "Winter")]
    public void 回规范名(string raw, string canonical)
    {
        Assert.Equal(canonical, Of($$"""{ "Season": "{{raw}}" }""", out _));
    }

    [Fact]
    public void contains写法声明值为真时等价于裸写法()
    {
        Assert.Equal("Spring", Of("""{ "Season |contains=Spring": true }""", out _));
    }

    /// <summary><c>|contains=X: false</c> 是补集（「不是 X」）—— 拆不出是哪一季，必须拒绝。</summary>
    [Fact]
    public void contains取反是补集()
    {
        Assert.Null(Of("""{ "Season |contains=Spring": false }""", out string? reason));
        Assert.Contains("补集", reason);
    }

    [Fact]
    public void contains里的季节名不合法时报错()
    {
        Assert.Null(Of("""{ "Season |contains=Monsoon": true }""", out string? reason));
        Assert.Contains("不是四季之一", reason);
    }

    /// <summary><c>|contains=</c> 的声明值必须是布尔 —— 写成别的就说不清是正向还是补集。</summary>
    [Fact]
    public void contains的声明值不是布尔时报错()
    {
        Assert.Null(Of("""{ "Season |contains=Spring": "yes" }""", out string? reason));
        Assert.Contains("true/false", reason);
    }

    /// <summary>一条补丁覆盖多季时，发射器要把同一份图复制到每一季。</summary>
    [Fact]
    public void 多季节写法返回全部季节()
    {
        Assert.Equal(new[] { "Spring", "Summer", "Fall" },
            SeasonToken.OfAll(JObject.Parse("""{ "Season": "Spring, Summer, Fall" }"""), out _));
        Assert.Equal(new[] { "Summer", "Winter" },
            SeasonToken.OfAll(JObject.Parse("""{ "Season |contains=Summer, Winter": true }"""), out _));
        Assert.Equal(new[] { "Spring", "Fall" },
            SeasonToken.OfAll(JObject.Parse("""{ "Season |contains=Summer, Winter": false }"""), out _));
    }

    [Fact]
    public void 多个Season时报错()
    {
        Assert.Null(Of("""{ "Season": "Spring", "Season |contains=Summer": true }""", out string? reason));
        Assert.Contains("多个 Season", reason);
    }

    [Fact]
    public void 没有Season时报错()
    {
        Assert.Null(Of("""{ "HasSeenEvent": "123" }""", out string? reason));
        Assert.Contains("没给 Season", reason);
    }

    [Fact]
    public void 不是四季之一时报错()
    {
        Assert.Null(Of("""{ "Season": "Monsoon" }""", out string? reason));
        Assert.Contains("不是四季之一", reason);
    }

    [Fact]
    public void 值是布尔时报错()
    {
        Assert.Null(Of("""{ "Season": true }""", out string? reason));
        Assert.Contains("布尔", reason);
    }

    /// <summary>其它键不干扰（<c>IsOutdoors</c> 这类由别的机制承载）。</summary>
    [Fact]
    public void 其它键不干扰()
    {
        Assert.Equal("Fall", Of("""{ "Season": "Fall", "IsOutdoors": true }""", out _));
    }

    // ============================================================ Target 里的 {{Season}} 展开

    private static ChangePatch Patch(string json)
    {
        JObject raw = JObject.Parse(json);
        return new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            FromFile = raw["FromFile"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = "content.json",
        };
    }

    /// <summary>
    /// ★ <c>Target</c> 里的 <c>{{Season}}</c> 要**展开成四季各一份**。
    /// <para>
    /// CP 的 <c>Season</c> 是**单值**令牌（<c>TokenManager.cs:148</c>），
    /// <c>GetSeason()</c> 返回小写的 <c>Game1.currentSeason</c> ⇒
    /// <c>"Target": "Maps/{{season}}_town"</c> 在 CP 里**每个季节解析成不同的资产**。
    /// 静态转换就是把四季都写一遍（那些 <c>Maps/spring_town.png</c> 原版树里真有）。
    /// </para>
    /// </summary>
    [Fact]
    public void Target里的Season展开成四季()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""
            { "Action": "EditImage", "Target": "Maps/{{season}}_town",
              "FromFile": "assets/{{Season}}/Dusty.png", "LogName": "Dusty" }
            """),
        };

        int added = SeasonTargetExpansion.Apply(patches);

        Assert.Equal(3, added);                       // 1 条变 4 条
        Assert.Equal(4, patches.Count);
        Assert.Equal(new[] { "Maps/spring_town", "Maps/summer_town", "Maps/fall_town", "Maps/winter_town" },
            patches.Select(p => p.Target).ToArray());
        Assert.Equal("assets/spring/Dusty.png", patches[0].FromFile);   // 正文里的也一并换
        Assert.Equal("assets/winter/Dusty.png", patches[3].FromFile);
    }

    /// <summary>⚠ <c>When</c> **不动** —— 条件里的 <c>{{Season}}</c> 交给 GSQ，别烤成常量。</summary>
    [Fact]
    public void When里的Season不展开()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""
            { "Action": "EditImage", "Target": "Maps/{{season}}_z_extras",
              "When": { "Season": "{{Season}}" } }
            """),
        };

        SeasonTargetExpansion.Apply(patches);

        Assert.Equal(4, patches.Count);
        Assert.Equal("{{Season}}", patches[0].When!["Season"]!.Value<string>());
        // Target 那个还是换了
        Assert.Equal("Maps/spring_z_extras", patches[0].Target);
    }

    /// <summary>Target 里没有季节令牌 ⇒ 原样不动（不做无谓的复制）。</summary>
    [Fact]
    public void Target没有季节令牌时不动()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""{ "Action": "EditImage", "Target": "Maps/Town" }"""),
        };

        Assert.Equal(0, SeasonTargetExpansion.Apply(patches));
        Assert.Single(patches);
    }

    // ============================================================ FromFile 里的 {{Season}}

    /// <summary>
    /// ★ <c>FromFile</c> 里的 <c>{{Season}}</c> 展开成四季，并**补一条 `Season` 条件**。
    /// <para>
    /// CP 的 <c>Season</c> 是单值令牌 ⇒ 「当季时用这张图」的等价物就是「四季各一条、每条带 Season 条件」。
    /// 补出来的条件正好被既有的两套发射器接住（图集季节前缀 / <c>Appearance[]</c>）。
    /// </para>
    /// </summary>
    [Fact]
    public void FromFile里的Season展开成四季并补条件()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""
            { "Action": "EditImage", "Target": "Maps/VanillaCursors",
              "FromFile": "assets/Maps/Cursors_{{season}}.png" }
            """),
        };

        int added = SeasonFromFileExpansion.Apply(patches);

        Assert.Equal(3, added);
        Assert.Equal(4, patches.Count);
        Assert.Equal("assets/Maps/Cursors_Spring.png", patches[0].FromFile);
        Assert.Equal("Spring", patches[0].When!["Season"]!.Value<string>());
        Assert.Equal("Winter", patches[3].When!["Season"]!.Value<string>());
    }

    /// <summary>
    /// 已有 <c>Season</c> 条件时仍要把 <c>FromFile</c> 的 <c>{{season}}</c> 固定到该季；
    /// 只展开允许的季节（这里 Spring 一条），不额外复制。
    /// </summary>
    [Fact]
    public void FromFile里的Season在已有Season条件时只展开允许的季节()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""
            { "Action": "EditImage", "Target": "Maps/X",
              "FromFile": "a_{{season}}.png", "When": { "Season": "Spring" } }
            """),
        };

        Assert.Equal(0, SeasonFromFileExpansion.Apply(patches));
        Assert.Single(patches);
        Assert.Equal("a_Spring.png", patches[0].FromFile);
        Assert.Equal("Spring", patches[0].When!["Season"]!.Value<string>());
    }

    /// <summary>原条件覆盖多季时只展开这些季，每份条件收窄成单季。</summary>
    [Fact]
    public void FromFile里的Season按原Season条件收窄()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""
            { "Action": "EditImage", "Target": "Maps/X",
              "FromFile": "a_{{season}}.png", "When": { "Season": "Spring, Summer" } }
            """),
        };

        Assert.Equal(1, SeasonFromFileExpansion.Apply(patches));
        Assert.Equal(2, patches.Count);
        Assert.Equal("a_Spring.png", patches[0].FromFile);
        Assert.Equal("Spring", patches[0].When!["Season"]!.Value<string>());
        Assert.Equal("a_Summer.png", patches[1].FromFile);
        Assert.Equal("Summer", patches[1].When!["Season"]!.Value<string>());
    }

    /// <summary>Target 里也有季节令牌的归 `SeasonTargetExpansion`（那条是「四个资产各写一份」）。</summary>
    [Fact]
    public void Target里有Season时不归FromFile这条管()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""{ "Action": "EditImage", "Target": "Maps/{{season}}_town", "FromFile": "a_{{season}}.png" }"""),
        };

        Assert.Equal(0, SeasonFromFileExpansion.Apply(patches));
    }
}
