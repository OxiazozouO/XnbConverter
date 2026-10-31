using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 带条件的 <c>Load</c>：运行时条件 + 目标不存在 ⇒ 去掉条件按无条件加载。
/// 实测 SVE 的 `Characters/Dialogue/CharlieChicken`（看过事件后才有对话）。
/// </summary>
public class ConditionalLoadDeterminizationTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "cld_" + Guid.NewGuid().ToString("N"));

    public ConditionalLoadDeterminizationTests() => Directory.CreateDirectory(_pack);

    public void Dispose() => Directory.Delete(_pack, recursive: true);

    private DynamicTokenResolver Tokens(Func<JObject, DynamicTokenResolver.WhenResult>? eval = null)
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"),
            """{ "Format": "2.9.0", "DynamicTokens": [], "Changes": [] }""");
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
            FromFile = raw["FromFile"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = "content.json",
        };
    }

    [Fact]
    public void 运行时条件的新对话资产去掉条件()
    {
        var patches = new List<ChangePatch>
        {
            Patch("""
                { "Action": "Load", "Target": "Characters/Dialogue/CharlieChicken",
                  "FromFile": "assets/charlie.json",
                  "When": { "HasSeenEvent |contains=3900074": true } }
                """),
        };

        int n = ConditionalLoadDeterminization.Apply(patches, Tokens(), _ => false, null);

        Assert.Equal(1, n);
        Assert.Null(patches[0].When);
        Assert.Null(patches[0].Raw["When"]);
    }

    /// <summary>
    /// 多目标 `Load Characters/X, Portraits/X`：目标**全部**不存在时整条去条件；
    /// 只要有一个已存在就不动（否则会连带覆盖已有资产）。
    /// 实测 Always Raining in the Valley 的 `Characters/Sterling, Portraits/Sterling`。
    /// </summary>
    [Fact]
    public void 多目标Load全部不存在才去条件()
    {
        var allMissing = Patch("""
            { "Action": "Load", "Target": "Characters/Sterling, Portraits/Sterling",
              "FromFile": "assets/sterling.json",
              "When": { "SeasonalsLite": "false" } }
            """);
        var oneExists = Patch("""
            { "Action": "Load", "Target": "Characters/Mia, Portraits/Mia",
              "FromFile": "assets/mia.json",
              "When": { "SeasonalsLite": "false" } }
            """);
        var patches = new List<ChangePatch> { allMissing, oneExists };

        int n = ConditionalLoadDeterminization.Apply(
            patches, Tokens(), t => t == "Portraits/Mia", null);

        Assert.Equal(1, n);
        Assert.Null(allMissing.When);
        Assert.NotNull(oneExists.When);
    }

    [Fact]
    public void 静态为假与已存在与日程都不动()
    {
        var staticFalse = Patch("""
            { "Action": "Load", "Target": "Characters/Dialogue/X", "FromFile": "a.json",
              "When": { "HasMod": "Not.Installed" } }
            """);
        var existing = Patch("""
            { "Action": "Load", "Target": "Characters/Dialogue/Y", "FromFile": "a.json",
              "When": { "HasSeenEvent": "1" } }
            """);
        var schedule = Patch("""
            { "Action": "Load", "Target": "Characters/schedules/Z", "FromFile": "a.json",
              "When": { "HasSeenEvent": "1" } }
            """);
        var patches = new List<ChangePatch> { staticFalse, existing, schedule };
        var tokens = Tokens(when =>
        {
            JToken? v = when["HasMod"];
            return new DynamicTokenResolver.WhenResult(v == null);   // HasMod 永远假
        });

        int n = ConditionalLoadDeterminization.Apply(
            patches, tokens, t => t == "Characters/Dialogue/Y", null);

        Assert.Equal(0, n);
        Assert.All(patches, p => Assert.NotNull(p.When));
    }
}
