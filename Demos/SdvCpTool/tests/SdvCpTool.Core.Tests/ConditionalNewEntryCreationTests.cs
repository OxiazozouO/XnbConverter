using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 带条件的 <c>Data/Characters</c>「创建新 NPC」：拆出一条无条件补丁。
/// 实测 Lavril / CharlieChicken / GoblinZic —— 不拆的话这些 NPC 在纯 XNB 里根本不存在。
/// </summary>
public class ConditionalNewEntryCreationTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "ccc_" + Guid.NewGuid().ToString("N"));

    public ConditionalNewEntryCreationTests() => Directory.CreateDirectory(_pack);

    public void Dispose() => Directory.Delete(_pack, recursive: true);

    private DynamicTokenResolver Tokens(string json, Func<JObject, DynamicTokenResolver.WhenResult>? eval = null)
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"),
            $$"""{ "Format": "2.9.0", "DynamicTokens": {{json}}, "Changes": [] }""");
        return DynamicTokenResolver.Load(_pack, eval);
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
            SourceFile = "content.json",
        };
    }

    [Fact]
    public void 新条目拆成无条件补丁_已有条目保持原判()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""
                { "Action": "EditData", "Target": "Data/Characters",
                  "When": { "HasSeenEvent: hostPlayer |contains=123": true },
                  "Entries": {
                    "NewNpc": { "DisplayName": "New", "SpawnIfMissing": true },
                    "OldNpc": { "DisplayName": "Changed" } } }
                """),
        };
        var existing = JObject.Parse("""{ "OldNpc": { "DisplayName": "Old" } }""");

        var notes = new List<string>();
        int added = ConditionalNewEntryCreation.Apply(patches, Tokens("[]"), _ => existing, notes);

        Assert.Equal(1, added);
        Assert.Equal(2, patches.Count);
        ChangePatch unconditional = Assert.Single(patches, p => p.When == null);
        Assert.NotNull(unconditional.Raw["Entries"]!["NewNpc"]);
        Assert.Null(unconditional.Raw["Entries"]!["OldNpc"]);
        ChangePatch conditional = Assert.Single(patches, p => p.When != null);
        Assert.Null(conditional.Raw["Entries"]!["NewNpc"]);
        Assert.NotNull(conditional.Raw["Entries"]!["OldNpc"]);
        Assert.Contains(notes, n => n.Contains("NewNpc"));
    }

    /// <summary>
    /// **带 `TargetField` 的补丁不是"创建条目"**：`Entries` 的键是目标条目的**字段名**
    /// （`BirthSeason` / `CanBeRomanced` / `Price`…）。当成新条目拆开，会把
    /// "条件成立时改字段"变成"无条件改字段"（实测 East Scarp）。
    /// </summary>
    [Fact]
    public void 带TargetField的字段补丁不拆()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""
                { "Action": "EditData", "Target": "Data/Characters",
                  "TargetField": [ "Aideen" ],
                  "When": { "HasSeenEvent": "123" },
                  "Entries": { "BirthSeason": "spring", "CanBeRomanced": true } }
                """),
        };
        var existing = JObject.Parse("""{ "Aideen": { "BirthSeason": "summer" } }""");

        int added = ConditionalNewEntryCreation.Apply(patches, Tokens("[]"), _ => existing, null);

        Assert.Equal(0, added);
        Assert.Single(patches);
        Assert.NotNull(patches[0].When);
    }

    /// <summary>
    /// **空日程资产**是特例：SVE 先用 `Load Assets/Blank.json` 建 `{}`，再用条件 `EditData`
    /// 加条目。不补的话 Charlie 永远没有日程。已有日程的资产**不动**。
    /// </summary>
    [Fact]
    public void 空日程资产才补条目()
    {
        ChangePatch empty = Patch("""
            { "Action": "EditData", "Target": "Characters/schedules/Charlie",
              "When": { "HasSeenEvent": "3900074" },
              "Entries": { "11": "0 Town 1 1 2" } }
            """);
        ChangePatch filled = Patch("""
            { "Action": "EditData", "Target": "Characters/schedules/Other",
              "When": { "HasSeenEvent": "1" },
              "Entries": { "11": "0 Town 1 1 2" } }
            """);
        var patches = new List<ChangePatch> { empty, filled };

        int added = ConditionalNewEntryCreation.Apply(patches, Tokens("[]"),
            t => t.EndsWith("Charlie") ? new JObject() : JObject.Parse("""{ "9": "x" }"""), null);

        Assert.Equal(1, added);
        ChangePatch unconditional = Assert.Single(patches,
            p => p.Target!.EndsWith("Charlie") && p.When == null);
        Assert.NotNull(unconditional.Raw["Entries"]!["11"]);
        Assert.DoesNotContain(patches, p => p.Target!.EndsWith("Charlie") && p.When != null);
        Assert.Contains(patches, p => p.Target!.EndsWith("Other") && p.When != null);
    }

    /// <summary>
    /// **键大小写不敏感**：`wizard_sleep` 与已有的 `Wizard_Sleep` 是同一个键，不能再无条件写一条。
    /// 实测 SVE 的婚姻动画补丁。
    /// </summary>
    [Fact]
    public void 大小写不同的已有键不算新条目()
    {
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Data/animationDescriptions",
              "When": { "Spouse": "Wizard" },
              "Entries": { "wizard_sleep": "29/29/29" } }
            """);
        var existing = JObject.Parse("""{ "Wizard_Sleep": "29/29/29" }""");

        int added = ConditionalNewEntryCreation.Apply(
            new List<ChangePatch> { p }, Tokens("[]"), _ => existing, null);

        Assert.Equal(0, added);
        Assert.NotNull(p.When);
    }

    /// <summary>拆出的补丁必须**保持源码顺序**（CP 后者胜）。</summary>
    [Fact]
    public void 拆分补丁保持源码顺序()
    {
        ChangePatch first = Patch("""
            { "Action": "EditData", "Target": "Data/Objects",
              "When": { "HasSeenEvent": "1" },
              "Entries": { "A": { "Name": "A" } } }
            """);
        ChangePatch second = Patch("""
            { "Action": "EditData", "Target": "Data/Objects",
              "When": { "HasSeenEvent": "2" },
              "Entries": { "B": { "Name": "B" } } }
            """);
        var patches = new List<ChangePatch> { first, second };

        ConditionalNewEntryCreation.Apply(patches, Tokens("[]"), _ => new JObject(), null);

        Assert.Equal(new[] { "A", "B" },
            patches.Where(p => p.When == null)
                   .Select(p => ((JObject)p.Raw["Entries"]!).Properties().First().Name).ToArray());
    }

    /// <summary>混合条件里只要有一个已知静态子句为假，就不能无条件化。</summary>
    [Fact]
    public void 静态假与运行时未知混合时不拆()
    {
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Data/Characters",
              "When": { "HasMod": "Not.Installed", "HasSeenEvent": "1" },
              "Entries": { "X": { "DisplayName": "X" } } }
            """);
        var tokens = Tokens("[]", when =>
        {
            JToken? v = when["HasMod"];
            return new DynamicTokenResolver.WhenResult(v == null);
        });
        var patches = new List<ChangePatch> { p };

        Assert.Equal(0, ConditionalNewEntryCreation.Apply(patches, tokens, _ => null, null));
        Assert.NotNull(p.When);
    }

    /// <summary>多个条件变体写**完全同名**的键（Lavril）可以拆；仅大小写不同的写法不行。</summary>
    [Fact]
    public void 同名变体可以拆_仅大小写不同不行()
    {
        ChangePatch same1 = Patch("""
            { "Action": "EditData", "Target": "Data/Characters",
              "When": { "HasSeenEvent": "1" },
              "Entries": { "Lavril": { "DisplayName": "L1" } } }
            """);
        ChangePatch same2 = Patch("""
            { "Action": "EditData", "Target": "Data/Characters",
              "When": { "HasSeenEvent": "2" },
              "Entries": { "Lavril": { "DisplayName": "L2" } } }
            """);
        var same = new List<ChangePatch> { same1, same2 };
        Assert.Equal(2, ConditionalNewEntryCreation.Apply(same, Tokens("[]"), _ => new JObject(), null));

        ChangePatch lower = Patch("""
            { "Action": "EditData", "Target": "Data/animationDescriptions",
              "When": { "Spouse": "Wizard" },
              "Entries": { "wizard_sleep": "29" } }
            """);
        ChangePatch upper = Patch("""
            { "Action": "EditData", "Target": "Data/animationDescriptions",
              "Entries": { "Wizard_Sleep": "29" } }
            """);
        var caseOnly = new List<ChangePatch> { lower, upper };
        Assert.Equal(0, ConditionalNewEntryCreation.Apply(caseOnly, Tokens("[]"), _ => new JObject(), null));
    }

    /// <summary>`Strings/*` 是纯文本：新键无条件写安全（只在被引用时显示）。</summary>
    [Fact]
    public void Strings新键无条件写()
    {
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Strings/StringsFromCSFiles",
              "When": { "Year": "1" },
              "Entries": { "festival_y1": "Year one line" } }
            """);

        var patches = new List<ChangePatch> { p };
        int added = ConditionalNewEntryCreation.Apply(
            patches, Tokens("[]"), _ => new JObject(), null);

        Assert.Equal(1, added);
        Assert.Single(patches, x => x.When == null && x.Raw["Entries"]!["festival_y1"] != null);
    }

    /// <summary>
    /// **运行时状态**（`HasSeenEvent`）被解析器冻结成 `false`，不等于静态为假 ⇒ 仍按无条件写。
    /// 实测 SVE 的 `Apples`：`When: { ApplesHere: true }`，而 `ApplesHere = {{HasSeenEvent…}}`。
    /// </summary>
    [Fact]
    public void 运行时状态冻结为假时仍写()
    {
        var tokens = Tokens("""
            [ { "Name": "ApplesHere", "Value": "{{HasSeenEvent: hostPlayer |contains=7775927}}" } ]
            """);
        var patches = new List<ChangePatch>
        {
            Patch("""
                { "Action": "EditData", "Target": "Data/Characters",
                  "When": { "ApplesHere": "true" },
                  "Entries": { "Apples": { "DisplayName": "Apples" } } }
                """),
        };

        int added = ConditionalNewEntryCreation.Apply(patches, tokens, _ => null, null);

        Assert.Equal(1, added);
        Assert.Contains(patches, p => p.When == null && p.Raw["Entries"]!["Apples"] != null);
    }

    /// <summary>条件静态为假（`HasMod` 不成立）⇒ 这块本就不生效，**不许**无条件化。</summary>
    [Fact]
    public void 静态为假的条件不拆()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""
                { "Action": "EditData", "Target": "Data/Characters",
                  "When": { "HasMod": "Not.Installed" },
                  "Entries": { "NewNpc": { "DisplayName": "New" } } }
                """),
        };
        var tokens = Tokens("[]", when =>
        {
            JToken? value = when["HasMod"];
            return new DynamicTokenResolver.WhenResult(
                value != null && value.Value<string>() == "Installed");
        });

        int added = ConditionalNewEntryCreation.Apply(patches, tokens, _ => null, null);

        Assert.Equal(0, added);
        Assert.Single(patches);
        Assert.NotNull(patches[0].When);
    }
}
