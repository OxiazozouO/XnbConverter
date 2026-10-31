using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// MistyCore 的 <c>append &lt;fragmentId&gt;</c> → 转换期静态内联（<see cref="EventCommandFixes"/>）。
/// <para>
/// 实测来源（2026-10-18）：GiEX 的 `GiEX_Javi_10h` 在 quickQuestion 分支后写
/// <c>append GiEX_Javi_10h_badEnding</c>；片段是同资产里 key 恰为片段 id 的条目。
/// 纯 XNB 没有这个命令 ⇒ 整段分支剧情被跳过。
/// </para>
/// </summary>
public class EventCommandFixesTests
{
    [Fact]
    public void append片段被静态内联()
    {
        var data = JObject.Parse("""
        {
          "GiEX_Javi_10h": "continue/17 10/farmer 16 14 0/speak X \"hi\"/append GiEX_Javi_10h_badEnding/end",
          "GiEX_Javi_10h_badEnding": "speak X \"bad\"/mailReceived GiEX_bad/end"
        }
        """);

        Assert.Equal(1, EventCommandFixes.InlineAppend("Data/Events/IslandSouth", data));
        Assert.Equal(
            "continue/17 10/farmer 16 14 0/speak X \"hi\"/speak X \"bad\"/mailReceived GiEX_bad/end/end",
            data["GiEX_Javi_10h"]!.Value<string>());
    }

    [Fact]
    public void 递归内联且找不到片段时保持原样()
    {
        var data = JObject.Parse("""
        {
          "evt": "a/append frag1/b/append missingId/c",
          "frag1": "x/append frag2/y",
          "frag2": "z"
        }
        """);

        // 3 = evt 里的 append frag1 + 递归的 append frag2 + 片段条目 frag1 自己也处理了一次
        Assert.Equal(3, EventCommandFixes.InlineAppend("Data/Events/Loc", data));
        Assert.Equal("a/x/z/y/b/append missingId/c", data["evt"]!.Value<string>());
    }

    /// <summary>引号里的 `/` 不是命令分隔符（否则会把 `speak "a/b"` 切成两条）。</summary>
    [Fact]
    public void 引号里的斜杠不切()
    {
        var data = JObject.Parse("""
        {
          "evt": "speak X \"a/b\"/append frag/end",
          "frag": "speak Y \"c/d\""
        }
        """);

        Assert.Equal(1, EventCommandFixes.InlineAppend("Data/Events/Loc", data));
        Assert.Equal("speak X \"a/b\"/speak Y \"c/d\"/end", data["evt"]!.Value<string>());
    }

    /// <summary>
    /// `quickQuestion` 的分支命令用 `(break)` 分隔、分支内多条命令用 `\` 分隔
    /// （`Event.cs:12820`）；分支里的 `append` 也要展开。
    /// </summary>
    [Fact]
    public void quickQuestion分支里的append也展开()
    {
        var data = JObject.Parse("""
        {
          "evt": "quickQuestion q#a1#a2(break)append bad(break)append good",
          "bad": "speak X \"bad\"/mailReceived B",
          "good": "speak X \"good\""
        }
        """);

        Assert.Equal(2, EventCommandFixes.InlineAppend("Data/Events/Loc", data));
        Assert.Equal(
            "quickQuestion q#a1#a2(break)speak X \"bad\"\\mailReceived B(break)speak X \"good\"",
            data["evt"]!.Value<string>());
    }

    /// <summary>真实 `GiEX_Javi_10h_furtherBranching` 的形态：if 分支 append 的片段里还有 quickQuestion + append。</summary>
    [Fact]
    public void 嵌套片段里的quickQuestion也展开()
    {
        var data = JObject.Parse("""
        {
          "evt": "a/if PLAYER_HAS_MAIL Current M##append areYouSure##append good/end",
          "areYouSure": "speak X \"...\"/quickQuestion q#a#b(break)append bad(break)append good",
          "bad": "stopMusic/speak X \"bad\"",
          "good": "speak X \"good\""
        }
        """);

        EventCommandFixes.RewriteMailIfs("Data/Events/Loc", data);
        foreach (JProperty p in data.Properties())
            if (p.Value.Type == JTokenType.String)
                Assert.DoesNotContain("append ", p.Value.Value<string>());
    }

    /// <summary>
    /// 实测 `GiEX_Javi_10h_areYouSure` 的 `speak` **少了收尾引号**；MistyCore 的 `append`
    /// 本来就是朴素 `Split('/')`，所以引号没闭合时要退回朴素切分，否则 `/quickQuestion` 会被吞掉。
    /// </summary>
    [Fact]
    public void 引号没闭合时退回朴素切分()
    {
        var data = JObject.Parse("""
        {
          "evt": "speak X \"unclosed/quickQuestion q#a#b(break)append bad(break)append good",
          "bad": "speak Y \"bad\"",
          "good": "speak Y \"good\""
        }
        """);

        Assert.Equal(2, EventCommandFixes.InlineAppend("Data/Events/Loc", data));
        Assert.DoesNotContain("append ", data["evt"]!.Value<string>());
        Assert.Contains("quickQuestion", data["evt"]!.Value<string>());
    }

    /// <summary>`addColouredSlime` → 带 color 的原版 `temporaryAnimatedSprite`；`SVE_BadlandsDeath` → `HospitalDeath` + 固定扣款。</summary>
    [Fact]
    public void 视觉与死亡惩罚命令近似()
    {
        var data = JObject.Parse("""
        {
          "slime": "a/addColouredSlime 5 6 0 247 13/b",
          "death": "SVE_BadlandsDeath/faceDirection farmer 2 true/end"
        }
        """);

        Assert.Equal(2, EventCommandFixes.RewriteMismatchedCommands("Data/Events/Loc", data));
        Assert.Equal(
            "a/temporaryAnimatedSprite Characters\\Monsters\\Green Slime 0 0 16 24 200 4 999 5 6 false false 1 0 1 0 0 0 color #00F70D/b",
            data["slime"]!.Value<string>());
        Assert.Equal("HospitalDeath/Money -65666/faceDirection farmer 2 true/end",
            data["death"]!.Value<string>());
    }

    [Fact]
    public void 非事件资产不动()
    {
        var data = JObject.Parse("""{ "evt": "append frag", "frag": "x" }""");
        Assert.Equal(0, EventCommandFixes.InlineAppend("Data/Objects", data));
        Assert.Equal("append frag", data["evt"]!.Value<string>());
    }

    /// <summary>
    /// MistyCore `if` → 原版 `fork`：两封邮件 AND、分支里带 `append`。
    /// 形态照抄 `GiEX_Javi_10h`（`Data/Events/IslandSouth`）。
    /// </summary>
    [Fact]
    public void 邮件if改写成fork且分支append被展开()
    {
        var data = JObject.Parse("""
        {
          "evt/pre": "a/if PLAYER_HAS_MAIL Current M1, PLAYER_HAS_MAIL Current M2##append bad##append good/b/end",
          "bad": "speak X \"bad\"/mailReceived B",
          "good": "speak X \"good\""
        }
        """);

        Assert.True(EventCommandFixes.RewriteMailIfs("Data/Events/Loc", data) >= 1);
        string script = data["evt/pre"]!.Value<string>()!;
        Assert.DoesNotContain("append", script);
        Assert.DoesNotContain("/if ", script);
        Assert.Contains("fork M1 evt__sdvcp_if0", script);
        Assert.Contains("speak X \"good\"", script);           // 假分支 + 尾部
        Assert.Contains("b/end", script);

        string wrapper0 = data["evt__sdvcp_if0"]!.Value<string>()!;
        Assert.Contains("fork M2 evt__sdvcp_if1", wrapper0);
        Assert.Contains("speak X \"good\"", wrapper0);          // M1 真、M2 假

        string wrapper1 = data["evt__sdvcp_if1"]!.Value<string>()!;
        Assert.Contains("speak X \"bad\"", wrapper1);           // M1 真、M2 真
        Assert.Contains("mailReceived B", wrapper1);
    }

    [Fact]
    public void 单邮件if与取反()
    {
        var data = JObject.Parse("""
        {
          "evt": "if PLAYER_HAS_MAIL Current M##say true##say false/end",
          "neg": "if !PLAYER_HAS_MAIL Current M##say yes##say no/end"
        }
        """);

        Assert.Equal(2, EventCommandFixes.RewriteMailIfs("Data/Events/Loc", data));
        Assert.Equal("fork M evt__sdvcp_if0/say false/end", data["evt"]!.Value<string>());
        Assert.Equal("say true/end", data["evt__sdvcp_if0"]!.Value<string>());

        // 取反：真/假分支对调，wrapper 是「没邮件时」的分支
        Assert.Equal("fork M neg__sdvcp_if0/say yes/end", data["neg"]!.Value<string>());
        Assert.Equal("say no/end", data["neg__sdvcp_if0"]!.Value<string>());
    }

    /// <summary>
    /// SpaceCore 的 <c>setDating</c>/<c>setEngaged</c> 在纯 XNB 下是未知命令 ⇒ 删掉整段；
    /// `quickQuestion` 的答案分支（`\` 分隔）里也要删。
    /// </summary>
    [Fact]
    public void 删除SpaceCore恋爱状态命令()
    {
        var data = JObject.Parse("""
        {
          "evt": "speak Mateo \"x\"/setDating Mateo true/pause 500/end",
          "branch": "quickQuestion \"问\"#\"答\"(break)speak Mateo \"a\"\\setEngaged Hector false\\pause 500(break)setDating Cirrus\\speak Cirrus \"b\""
        }
        """);

        Assert.Equal(3, EventCommandFixes.RewriteMismatchedCommands("Data/Events/Loc", data));
        Assert.Equal("speak Mateo \"x\"/pause 500/end", data["evt"]!.Value<string>());
        string branch = data["branch"]!.Value<string>()!;
        Assert.DoesNotContain("setEngaged", branch);
        Assert.DoesNotContain("setDating", branch);
        Assert.Contains("speak Mateo \"a\"\\pause 500", branch);
        Assert.Contains("speak Cirrus \"b\"", branch);
    }

    [Fact]
    public void 非邮件的if不动()
    {
        var data = JObject.Parse("""
        { "evt": "if PLAYER_HEARTS Abigail 8##say a##say b/end" }
        """);
        Assert.Equal(0, EventCommandFixes.RewriteMailIfs("Data/Events/Loc", data));
        Assert.Contains("if PLAYER_HEARTS", data["evt"]!.Value<string>());
    }
}
