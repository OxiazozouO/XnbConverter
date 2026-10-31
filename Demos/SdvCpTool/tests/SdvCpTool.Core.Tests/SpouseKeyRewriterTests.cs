using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// Task 10f：节日配偶对话改用原生 <c>&lt;NPC&gt;_spouse</c> 键。
/// <para>
/// 前提四条严格满足才改写；键名与条件里的 NPC 不一致时<b>绝不</b>改写 ——
/// 那是唯一会静默改错别人对话的方向。
/// </para>
/// </summary>
public class SpouseKeyRewriterTests : IDisposable
{
    private readonly string _tree = Path.Combine(Path.GetTempPath(), "sdvspk_t_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvspk_p_" + Guid.NewGuid().ToString("N"));
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvspk_s_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvspk_o_" + Guid.NewGuid().ToString("N"));

    public SpouseKeyRewriterTests()
    {
        Directory.CreateDirectory(_tree);
        Directory.CreateDirectory(_pack);
        Directory.CreateDirectory(_src);
    }

    private OutputTree? _outTree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush() => _outTree?.Flush();

    public void Dispose()
    {
        Directory.Delete(_tree, recursive: true);
        Directory.Delete(_pack, recursive: true);
        Directory.Delete(_src, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
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
            SourceFile = "content.json"
        };
    }

    /// <summary>真实形态：FreeLove.json 里的 244 条补丁之一。</summary>
    private const string Valid = """
    {
      "Action": "EditData",
      "Target": "Data/Festivals/spring13",
      "Entries": { "Ian": "{{i18n:Festivals.EggFest.Ian_spouse}}" },
      "When": { "Relationship:Ian |contains= married": true }
    }
    """;

    // ------------------------------------------------------------------ 改写本身

    [Fact]
    public void 四条前提全满足时改写键并删掉条件()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch(Valid));

        Assert.NotNull(r);
        Assert.Equal("{{i18n:Festivals.EggFest.Ian_spouse}}",
            r!["Entries"]!["Ian_spouse"]!.Value<string>());
        Assert.Null(r["Entries"]!["Ian"]);      // 旧键必须消失，否则会变成"所有人都是这个对话"
        Assert.Null(r["When"]);                 // 条件整个删掉
        Assert.Equal("Data/Festivals/spring13", r["Target"]!.Value<string>());
    }

    [Fact]
    public void 改写是纯函数_原补丁不被改动()
    {
        ChangePatch patch = Patch(Valid);

        _ = SpouseKeyRewriter.TryRewrite(patch);

        Assert.NotNull(patch.Raw["Entries"]!["Ian"]);
        Assert.NotNull(patch.Raw["When"]);
    }

    [Fact]
    public void 条件里的NPC与条目键不一致时不改写()
    {
        // 关键安全用例：把 Ian 的对话写进 Jeric 的 _spouse 键会静默改错别人的对话。
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Jeric": "{{i18n:Festivals.EggFest.Ian_spouse}}" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 键是NPC名的前缀但不是同一个人时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian2": "x" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void When有两个叶子时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "x" },
          "When": {
            "Relationship:Ian |contains= married": true,
            "HasMod": "|contains=Some.Mod"
          }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void Entries有两个条目时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "x", "Jeric": "y" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void Target不是节日资产时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Locations",
          "Entries": { "Ian": "x" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void Target只是节日路径的前缀时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/FestivalsExtra/spring13",
          "Entries": { "Ian": "x" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 条件值为false时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "x" },
          "When": { "Relationship:Ian |contains= married": false }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 关系值不是married时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "x" },
          "When": { "Relationship:Ian |contains= dating": true }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void married大小写不敏感()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "x" },
          "When": { "Relationship:Ian |contains= Married": true }
        }
        """));

        Assert.NotNull(r);
        Assert.NotNull(r!["Entries"]!["Ian_spouse"]);
    }

    [Fact]
    public void 键带y2后缀时后缀保留在名字之后()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring24",
          "Entries": { "Ian_y2": "x" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """));

        Assert.NotNull(r);
        Assert.NotNull(r!["Entries"]!["Ian_spouse_y2"]);
        Assert.Null(r["Entries"]!["Ian_y2"]);
    }

    // ------------------------------------------------- 三种等价拼写（NPC 的位置不同）

    /// <summary>
    /// `Spouse`/`Roommate` 是**值令牌**：NPC 名在**值侧**（CP 的 `TokenManager.cs:200-201`
    /// 证实其值就是玩家配偶名）。此前只认 `Relationship`（NPC 在键侧）会漏掉语料里大量真实补丁。
    /// </summary>
    [Theory]
    [InlineData("""{"Spouse": "Ian"}""", "Ian_spouse")]
    [InlineData("""{"Spouse |contains=Ian": true}""", "Ian_spouse")]
    [InlineData("""{"Roommate": "Krobus"}""", "Krobus_roommate")]
    [InlineData("""{"Roommate |contains=Krobus": true}""", "Krobus_roommate")]
    public void 值令牌拼写也能改写成原生后缀键(string when, string expectedKey)
    {
        string npc = expectedKey.Split('_')[0];
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch($$"""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "{{npc}}": "结婚后才用的新对话" },
          "When": {{when}}
        }
        """));

        Assert.NotNull(r);
        Assert.Equal("结婚后才用的新对话", r!["Entries"]![expectedKey]!.Value<string>());
        Assert.Null(r["Entries"]![npc]);
        Assert.Null(r["When"]);
    }

    [Fact]
    public void 配偶键也保留y2后缀()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring24",
          "Entries": { "Ian_y2": "x" },
          "When": { "Spouse": "Ian" }
        }
        """));

        Assert.NotNull(r);
        Assert.NotNull(r!["Entries"]!["Ian_spouse_y2"]);
    }

    [Fact]
    public void 值令牌的条件值为false时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "x" },
          "When": { "Spouse |contains=Ian": false }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 配偶是多值时不改写()
    {
        // 多值要拆成多条条目，而前提 4 要求 Entries 恰好一条 —— 先挡掉，避免误改。
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "x" },
          "When": { "Spouse": "Ian, Jeric" }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 值令牌的条件NPC与条目键不一致时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Jeric": "x" },
          "When": { "Spouse": "Ian" }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 带TargetField时不改写()
    {
        // 落点被改变时 _spouse 约定不再适用（它只认节日资产的顶层键）。
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "TargetField": [ "Entries" ],
          "Entries": { "Ian": "x" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """));

        Assert.Null(r);
    }

    [Fact]
    public void 非EditData动作时不改写()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "Load",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "x" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """));

        Assert.Null(r);
    }

    // ------------------------------------------------------------------ 判定接线

    private PatchVerdict Judge(string patchJson)
    {
        var patch = Patch(patchJson);
        return new PatchJudge(new ResourceTree(_tree), _pack).Judge(patch);
    }

    private void TreeFile(string rel, string content)
    {
        string full = Path.Combine(_tree, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void 判定_已存在的节日资产走原生键通道且可写入()
    {
        TreeFile("Data/Festivals/spring13.json", """{ "Ian": "原版", "Abigail": "x" }""");

        PatchVerdict v = Judge(Valid);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.NativeKey, v.Channel);
        Assert.True(WritePolicy.ShouldWrite(v));
    }

    [Fact]
    public void 判定_模组自建的节日资产也走原生键通道()
    {
        // fall20 / winter28 是 RSV 自建的节日，资源树里没有 —— 以前被判 NeedsReview。
        PatchVerdict v = Judge("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/winter28",
          "Entries": { "Ian": "{{i18n:Festivals.Ian_spouse}}" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.NativeKey, v.Channel);
        Assert.True(WritePolicy.ShouldWrite(v));
    }

    [Fact]
    public void 配偶条件的竖线后带空格也能识别()
    {
        JObject? r = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "x" },
          "When": { "Relationship:Ian| contains= married": true }
        }
        """));

        Assert.NotNull(r);
        Assert.NotNull(r!["Entries"]!["Ian_spouse"]);
    }

    /// <summary>判定与写入必须走同一个 `TryRewrite`（Task 10b 的教训）—— 值令牌拼写也不例外。</summary>
    [Fact]
    public void 判定_值令牌拼写同样走原生键通道()
    {
        TreeFile("Data/Festivals/spring13.json", """{ "Ian": "原版", "Abigail": "x" }""");

        PatchVerdict v = Judge("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "结婚后才用的新对话" },
          "When": { "Spouse": "Ian" }
        }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.NativeKey, v.Channel);
        Assert.True(WritePolicy.ShouldWrite(v));
    }

    [Fact]
    public void 判定_键名与NPC不一致时保持原判定不变()
    {
        TreeFile("Data/Festivals/spring13.json", """{ "Ian": "原版" }""");

        PatchVerdict v = Judge("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Jeric": "y" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """);

        Assert.NotEqual(Channel.NativeKey, v.Channel);
        Assert.NotEqual(Verdict.Converted, v.Verdict);
    }

    // ------------------------------------------------------------------ 写入产物（逐字核对的核心）

    [Fact]
    public void 写入_Ian_spouse出现且Ian原值一字未动()
    {
        string srcFile = Path.Combine(_src, "Data", "Festivals", "spring13.json");
        Directory.CreateDirectory(Path.GetDirectoryName(srcFile)!);
        File.WriteAllText(srcFile, """{ "Ian": "已婚以外也用的原版对话", "Abigail": "x" }""");

        var applier = new PatchApplier(_outTree = new OutputTree(_src, _out));
        // 用字面量对话（i18n 展开由 TranslationApplier 负责，另有测试覆盖）。
        JObject rewritten = SpouseKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian": "结婚后才用的新对话" },
          "When": { "Relationship:Ian |contains= married": true }
        }
        """))!;
        var outcome = applier.ApplyEditData(
            "Data/Festivals/spring13", rewritten.ToString(Newtonsoft.Json.Formatting.None));

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();
        JObject written = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Festivals", "spring13.json")));
        Assert.Equal("结婚后才用的新对话", written["Ian_spouse"]!.Value<string>());
        Assert.Equal("已婚以外也用的原版对话", written["Ian"]!.Value<string>());   // 逐字未动
        Assert.Equal("x", written["Abigail"]!.Value<string>());
    }
}
