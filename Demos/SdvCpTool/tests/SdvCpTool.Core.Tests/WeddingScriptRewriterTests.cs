using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// Task 10k-2：婚礼台词覆写改写为 <c>Data/Weddings</c> 的 <c>EventScript[&lt;配偶名&gt;]</c>。
/// <para>
/// 前提任一不满足就<b>不转</b>（保持原判定）：尤其「母本里没有这个键」时必须整条放弃 ——
/// 转换出来的脚本会少半句台词，而婚礼是全存档一次性的，写错无法补救。
/// </para>
/// </summary>
public class WeddingScriptRewriterTests : IDisposable
{
    private readonly string _tree = Path.Combine(Path.GetTempPath(), "sdvwed_t_" + Guid.NewGuid().ToString("N"));

    public WeddingScriptRewriterTests()
    {
        Directory.CreateDirectory(_tree);
    }

    public void Dispose()
    {
        Directory.Delete(_tree, recursive: true);
    }

    // ------------------------------------------------------------------ 母本与补丁

    /// <summary>
    /// 母本按原版 <c>Data/Weddings.EventScript["default"]</c> 的形态裁剪：
    /// 5367/5369 带 <c>[DayOfMonth] [Season]</c> 两个位置参数，5371/5377 不带。
    /// <para>
    /// 注意反斜杠个数：这里是**已解码的脚本字符串**（不是 JSON 原文），
    /// 真实数据里引用长这样 <c>Strings\StringsFromCSFiles:Utility.cs.5367</c>（**一个**反斜杠），
    /// 所以 C# 字面量写 <c>\\</c>。写成 <c>\\\\</c> 会得到两个反斜杠，
    /// 而 <c>WeddingScriptRewriter.IsReferenceTo</c> 把 <c>\</c> 归一成 <c>/</c> 后按
    /// <c>Strings/StringsFromCSFiles:</c> 前缀匹配 —— 两个反斜杠会变成 <c>Strings//…</c> 而失配。
    /// </para>
    /// </summary>
    private const string DefaultScript =
        "sweet/-1000 -100/farmer 27 63 2/speak Lewis \"[GenderedText " +
        "[LocalizedText Strings\\StringsFromCSFiles:Utility.cs.5367 [DayOfMonth] [Season]] " +
        "[LocalizedText Strings\\StringsFromCSFiles:Utility.cs.5369 [DayOfMonth] [Season]]]\"/" +
        "speak Lewis \"[LocalizedText Strings\\StringsFromCSFiles:Utility.cs.5371]\"/" +
        "speak Lewis \"[LocalizedText Strings\\StringsFromCSFiles:Utility.cs.5377]\"";

    private void WriteDefaultWeddingScript()
    {
        string dir = Path.Combine(_tree, "Data");
        Directory.CreateDirectory(dir);
        var asset = new JObject
        {
            ["EventScript"] = new JObject { ["default"] = DefaultScript },
            ["Attendees"] = new JObject(),
        };
        File.WriteAllText(Path.Combine(dir, "Weddings.json"), asset.ToString());
    }

    private static ChangePatch Patch(string json)
    {
        JObject raw = JObject.Parse(json);
        return new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = "Data/NPCData/Romance.json"
        };
    }

    /// <summary>真实形态：RSV <c>Data/NPCData/Romance.json</c> 的 11 条「Female Wedding Fixes」之一。</summary>
    private const string Valid = """
    {
      "Action": "EditData",
      "Target": "strings/StringsFromCSFiles",
      "Entries": {
        "Utility.cs.5367": "{{i18n:Romance.FemaleWeddingFix.MaleFarmer1.Alissa}}",
        "Utility.cs.5369": "{{i18n:Romance.FemaleWeddingFix.FemaleFarmer1.Alissa}}",
        "Utility.cs.5371": "{{i18n:Romance.FemaleWeddingFix.MaleFarmer2.Alissa}}",
        "Utility.cs.5377": "{{i18n:Romance.FemaleWeddingFix.FemaleFarmer2.Alissa}}"
      },
      "When": { "Relationship:Alissa": "Engaged" }
    }
    """;

    private static string Script(JObject rewritten) =>
        rewritten["Entries"]!["Alissa"]!.Value<string>()!;

    // ------------------------------------------------------------------ 前提全满足

    [Fact]
    public void 前提全满足时改写为Data_Weddings的EventScript()
    {
        JObject? r = WeddingScriptRewriter.TryRewrite(Patch(Valid), DefaultScript);

        Assert.NotNull(r);
        Assert.Equal("Data/Weddings", r!["Target"]!.Value<string>());
        Assert.Null(r!["When"]);                                 // 条件整个删掉（键即配偶名）
        Assert.Equal("EventScript", r!["TargetField"]![0]!.Value<string>());
        // 只有 Alissa 这一个键 —— 键即配偶名，游戏只在与该 NPC 结婚时取它
        Assert.Single(r["Entries"]!.Children());
        Assert.Contains("{{i18n:Romance.FemaleWeddingFix.MaleFarmer2.Alissa}}", Script(r));
    }

    [Fact]
    public void 改写后无参数的引用被替换_带参数的引用原样保留()
    {
        JObject r = WeddingScriptRewriter.TryRewrite(Patch(Valid), DefaultScript)!;
        string script = Script(r);

        // 带位置参数的 5367/5369（值含 {{i18n:…}}，改写阶段代换不了 {0}/{1}）→ 保留原标签。
        Assert.Contains("Utility.cs.5367", script);
        Assert.Contains("Utility.cs.5369", script);
        Assert.Contains(
            "[LocalizedText Strings\\StringsFromCSFiles:Utility.cs.5367 [DayOfMonth] [Season]]", script);
        // 不带参数的 5371/5377（性别 bug 修复）→ 照常就地替换。
        Assert.DoesNotContain("Utility.cs.5371", script);
        Assert.DoesNotContain("Utility.cs.5377", script);
        Assert.Contains("{{i18n:Romance.FemaleWeddingFix.MaleFarmer2.Alissa}}", script);
        Assert.Contains("{{i18n:Romance.FemaleWeddingFix.FemaleFarmer2.Alissa}}", script);
        // 回归钉子：产物里绝不能漏出字面占位符。
        Assert.DoesNotContain("{0}", script);
        Assert.DoesNotContain("{1}", script);
        // 脚本骨架（sweet 起点、三次 speak、GenderedText 结构）必须留着，否则婚礼事件跑不起来
        Assert.StartsWith("sweet/-1000 -100/farmer 27 63 2/", script);
        Assert.Contains("[GenderedText", script);
        Assert.Equal(3, script.Split("/speak").Length - 1);
    }

    [Fact]
    public void 位置参数被还原成母本里的令牌()
    {
        // 5367 在母本里是 `[LocalizedText …:5367 [DayOfMonth] [Season]]`，
        // 丢掉 LocalizedText 包裹后 `{0}`/`{1}` 必须还原成同样的令牌，否则婚礼台词会显示 "{0} of {1}"。
        // 值不含 {{（纯文本）→ {0}/{1} 改写阶段就看得见，这条代换路径是好的、必须保住。
        var patch = Patch("""
        {
          "Action": "EditData",
          "Target": "strings/StringsFromCSFiles",
          "Entries": { "Utility.cs.5367": "on this day {0} of {1}, @ and Alissa" },
          "When": { "Relationship:Alissa": "Engaged" }
        }
        """);

        JObject r = WeddingScriptRewriter.TryRewrite(patch, DefaultScript)!;

        Assert.Contains("on this day [DayOfMonth] of [Season], @ and Alissa", Script(r));
        Assert.DoesNotContain("{0}", Script(r));
        Assert.DoesNotContain("{1}", Script(r));
    }

    [Fact]
    public void 带参数的标签遇i18n令牌时跳过_一个都替换不了则整条不转()
    {
        // 只覆写 5367（带位置参数），值又是 {{i18n:…}} → 改写阶段代换不了 {0}/{1}，
        // 跳过它；一个条目都没替换 → 产出的脚本与母本一模一样，写它没意义，整条不转。
        var patch = Patch("""
        {
          "Action": "EditData",
          "Target": "strings/StringsFromCSFiles",
          "Entries": { "Utility.cs.5367": "{{i18n:Romance.FemaleWeddingFix.MaleFarmer1.Alissa}}" },
          "When": { "Relationship:Alissa": "Engaged" }
        }
        """);

        Assert.Null(WeddingScriptRewriter.TryRewrite(patch, DefaultScript));
    }

    [Fact]
    public void Married条件同样接受()
    {
        string json = Valid.Replace("\"Engaged\"", "\"Married\"");
        Assert.NotNull(WeddingScriptRewriter.TryRewrite(Patch(json), DefaultScript));
    }

    [Fact]
    public void 改写是纯函数_原补丁不被改动()
    {
        ChangePatch patch = Patch(Valid);

        _ = WeddingScriptRewriter.TryRewrite(patch, DefaultScript);

        Assert.NotNull(patch.Raw["Entries"]!["Utility.cs.5367"]);
        Assert.NotNull(patch.Raw["When"]);
        Assert.Equal("strings/StringsFromCSFiles", patch.Raw["Target"]!.Value<string>());
    }

    // ------------------------------------------------------------------ 前提不满足 → 不转

    [Fact]
    public void 目标不是StringsFromCSFiles时不转()
    {
        string json = Valid.Replace("\"strings/StringsFromCSFiles\"", "\"Strings/Characters\"");
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(json), DefaultScript));
    }

    [Fact]
    public void 键里有非Utility_cs_53xx时不转()
    {
        // 54xx 不在婚礼台词族里
        string other = Valid.Replace("\"Utility.cs.5377\"", "\"Utility.cs.5477\"");
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(other), DefaultScript));

        // 段落键（Engagement Dialogue 那批）也不是婚礼脚本
        string engagement = Valid.Replace("\"Utility.cs.5377\"", "\"Alissa_Engaged\"");
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(engagement), DefaultScript));
    }

    [Fact]
    public void 值不是字符串时不转()
    {
        string json = Valid.Replace(
            "\"Utility.cs.5377\": \"{{i18n:Romance.FemaleWeddingFix.FemaleFarmer2.Alissa}}\"",
            "\"Utility.cs.5377\": 42");
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(json), DefaultScript));
    }

    [Fact]
    public void 条件不是Relationship或值不是EngagedMarried时不转()
    {
        Assert.Null(WeddingScriptRewriter.TryRewrite(
            Patch(Valid.Replace("\"Relationship:Alissa\"", "\"Season\"")), DefaultScript));
        Assert.Null(WeddingScriptRewriter.TryRewrite(
            Patch(Valid.Replace("\"Engaged\"", "\"Dating\"")), DefaultScript));
    }

    [Fact]
    public void 条件不止一个叶子时不转()
    {
        string json = Valid.Replace(
            "\"When\": { \"Relationship:Alissa\": \"Engaged\" }",
            "\"When\": { \"Relationship:Alissa\": \"Engaged\", \"Season\": \"spring\" }");
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(json), DefaultScript));
    }

    [Fact]
    public void 母本里没有该键引用时不转()
    {
        // 母本缺 Utility.cs.5377 的引用 → 转换会少半句台词，宁可整条不转
        // 反斜杠个数要与 DefaultScript 一致（已解码的脚本里是一个）
        string truncated = DefaultScript.Replace(
            "/speak Lewis \"[LocalizedText Strings\\StringsFromCSFiles:Utility.cs.5377]\"", "");
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(Valid), truncated));
    }

    [Fact]
    public void 取不到母本时不转()
    {
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(Valid), null));
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(Valid), ""));
    }

    [Fact]
    public void 用了TargetField或Fields时不转()
    {
        string json = Valid.Replace("\"Entries\": {", "\"TargetField\": [\"X\"], \"Entries\": {");
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(json), DefaultScript));
    }

    [Fact]
    public void 多目标时不转()
    {
        string json = Valid.Replace(
            "\"strings/StringsFromCSFiles\"",
            "\"strings/StringsFromCSFiles, Strings/Characters\"");
        Assert.Null(WeddingScriptRewriter.TryRewrite(Patch(json), DefaultScript));
    }

    // ------------------------------------------------------------------ 母本解析

    [Fact]
    public void 从资源树解析母本()
    {
        WriteDefaultWeddingScript();

        string? script = WeddingScriptRewriter.ResolveDefaultEventScript(new ResourceTree(_tree));

        Assert.NotNull(script);
        Assert.Contains("Utility.cs.5367", script);
    }

    [Fact]
    public void 资源树里没有Data_Weddings时母本为空()
    {
        Assert.Null(WeddingScriptRewriter.ResolveDefaultEventScript(new ResourceTree(_tree)));
    }

    // ------------------------------------------------------------------ 接入判定

    [Fact]
    public void 判定走NativeKey通道并判可转换()
    {
        WriteDefaultWeddingScript();
        var judge = new PatchJudge(new ResourceTree(_tree), _tree);

        PatchVerdict v = judge.Judge(Patch(Valid));

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.NativeKey, v.Channel);
    }

    [Fact]
    public void 母本缺失时判定不把这类补丁判成可转换()
    {
        // 资源树里没有 Data/Weddings → 改写作废，必须落回原有判定（这里是 NeedsReview），
        // 绝不能因为"识别出了形态"就判 Converted 却在写入阶段失败。
        var judge = new PatchJudge(new ResourceTree(_tree), _tree);

        PatchVerdict v = judge.Judge(Patch(Valid));

        Assert.NotEqual(Verdict.Converted, v.Verdict);
    }
}
