using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// Task 10g：「键即条件」——补丁的条件被条目键名完全蕴含时，删掉条件、无条件写入该键。
/// 前提严格，任一不符即返回 <c>null</c>（上层保持原判定 → NeedsReview）。
/// </summary>
public class KeyEncodedVariantRewriterTests
{
    private static ChangePatch Patch(string json)
    {
        JObject raw = JObject.Parse(json);
        return new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = "content.json"
        };
    }

    /// <summary>计划里给出的范式：`When: {Season: spring}` 打在 `Data/mail:spring_13` 上。</summary>
    private const string Implied = """
    {
      "Action": "EditData",
      "Target": "Data/mail",
      "Entries": { "spring_13": "{{i18n:Mail.Spring13}}" },
      "When": { "Season": "spring" }
    }
    """;

    [Fact]
    public void 季节被键名蕴含时删掉条件并保留键()
    {
        JObject? r = KeyEncodedVariantRewriter.TryRewrite(Patch(Implied));

        Assert.NotNull(r);
        Assert.Null(r!["When"]);                                       // 条件整个删掉
        Assert.Equal("{{i18n:Mail.Spring13}}", r["Entries"]!["spring_13"]!.Value<string>());
        Assert.Equal("Data/mail", r["Target"]!.Value<string>());
    }

    [Fact]
    public void 改写是纯函数_原补丁不被改动()
    {
        ChangePatch patch = Patch(Implied);
        KeyEncodedVariantRewriter.TryRewrite(patch);

        Assert.NotNull(patch.Raw["When"]);
    }

    [Fact]
    public void 季节与键名不一致时不改写()
    {
        // 键是 spring_13，条件却要 fall —— 键名不蕴含该条件，绝不能改写。
        var r = KeyEncodedVariantRewriter.TryRewrite(Patch("""
        { "Action": "EditData", "Target": "Data/mail",
          "Entries": { "spring_13": "x" }, "When": { "Season": "fall" } }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 条件里含别的令牌时不改写()
    {
        // Season 只是条件之一，还有 HasSeenEvent —— 键名蕴含不了整条条件。
        var r = KeyEncodedVariantRewriter.TryRewrite(Patch("""
        { "Action": "EditData", "Target": "Data/mail",
          "Entries": { "spring_13": "x" },
          "When": { "Season": "spring", "HasSeenEvent": "123" } }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 键不含季节前缀时不改写()
    {
        var r = KeyEncodedVariantRewriter.TryRewrite(Patch("""
        { "Action": "EditData", "Target": "Data/mail",
          "Entries": { "someKey": "x" }, "When": { "Season": "spring" } }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 非DataMail目标不改写()
    {
        // 只有 Data/mail 是源码确证「季节前缀键 = 季节维度」的数据资产；别的资产不碰。
        var r = KeyEncodedVariantRewriter.TryRewrite(Patch("""
        { "Action": "EditData", "Target": "Data/Festivals/spring13",
          "Entries": { "spring_13": "x" }, "When": { "Season": "spring" } }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 非EditData不改写()
    {
        var r = KeyEncodedVariantRewriter.TryRewrite(Patch("""
        { "Action": "Load", "Target": "Data/mail",
          "Entries": { "spring_13": "x" }, "When": { "Season": "spring" } }
        """));

        Assert.Null(r);
    }
}
