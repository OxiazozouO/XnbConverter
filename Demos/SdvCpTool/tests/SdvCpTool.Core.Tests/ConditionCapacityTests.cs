using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// **条件容量封闭**的那几族（日程 / 节日）：补丁里只要有**一个合取项**落在容量外，
/// 判定就该**确证**"无处安放"（<see cref="Verdict.NoLandingPoint"/>），而不是含糊地
/// "需人工确认落点" —— 那会让 385 条排进人工队列，而它们本来就不可能有落点。
///
/// <para>
/// ⚠ 反过来的边界同样要钉住：**全在容量内**的条件（比如日程只带 `Season`）只能说"不知道"，
/// 因为那可能是"有落点但没实现"（日程的 `MAIL` / `marriage_` / `rain` 那几种写法）。
/// 一口咬定"没有落点"就是**说错**。
/// </para>
/// </summary>
public class ConditionCapacityTests : IDisposable
{
    private readonly string _packDir = Path.Combine(Path.GetTempPath(), "sdvcapk_" + Guid.NewGuid().ToString("N"));
    private readonly string _treeDir = Path.Combine(Path.GetTempPath(), "sdvcaptr_" + Guid.NewGuid().ToString("N"));

    public ConditionCapacityTests()
    {
        Directory.CreateDirectory(_packDir);
        Directory.CreateDirectory(_treeDir);
    }

    public void Dispose()
    {
        Directory.Delete(_packDir, recursive: true);
        Directory.Delete(_treeDir, recursive: true);
    }

    private void Tree(string relative, string content)
    {
        string full = Path.Combine(_treeDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private PatchVerdict Judge(string patchJson)
    {
        var raw = JObject.Parse(patchJson);
        var patch = new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            FromFile = raw["FromFile"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = Path.Combine(_packDir, "content.json"),
        };
        return new PatchJudge(new ResourceTree(_treeDir), _packDir, null, null).Judge(patch);
    }

    // ------------------------------------------------------------------ 直接测口径

    [Theory]
    [InlineData("Characters/schedules/Kent", """{ "HasSeenEvent": "1" }""", true)]
    [InlineData("Characters/schedules/Kent", """{ "Weather": "Rain" }""", true)]
    [InlineData("Characters/schedules/Kent", """{ "Year |contains=1": false }""", true)]
    [InlineData("Characters/schedules/Kent", """{ "Season": "Spring" }""", false)]      // 容量内：可能只是没实现
    [InlineData("Characters/schedules/Kent", """{ "HasMail": "ccVault" }""", false)]     // 值里的 MAIL 槽
    [InlineData("Characters/schedules/Kent", """{ "Spouse": "Kent" }""", false)]         // marriage_ 前缀
    [InlineData("Strings/schedules/Kent", """{ "Hearts:Kent": "5, 6" }""", true)]
    [InlineData("Data/Festivals/fall27", """{ "HasSeenEvent": "1" }""", true)]
    [InlineData("Data/Festivals/fall27", """{ "Year": "3, 4" }""", false)]               // `_yN` 循环
    [InlineData("Data/Objects", """{ "HasSeenEvent": "1" }""", false)]                   // 不在那几族里
    public void 容量口径(string asset, string whenJson, bool expected)
    {
        var when = JObject.Parse(whenJson);
        Assert.Equal(expected, ConditionCapacity.ProvablyUnlandable(asset, when, out _));
    }

    /// <summary>没有条件时什么都不说（那是"无条件"，走另一条路）。</summary>
    [Fact]
    public void 没有条件时不下结论()
    {
        Assert.False(ConditionCapacity.ProvablyUnlandable("Characters/schedules/Kent", null, out _));
        Assert.False(ConditionCapacity.ProvablyUnlandable("Characters/schedules/Kent", new JObject(), out _));
    }

    // ------------------------------------------------------------------ 判定接线

    /// <summary>
    /// ★ 日程带 `HasSeenEvent`（键空间与值槽都表达不了）⇒ **确证无处安放**，
    /// 理由里要把那串候选键/值槽列出来（读报告的人要能自己判断）。
    /// </summary>
    [Fact]
    public void 日程带事件条件判无落点()
    {
        Tree("Characters/schedules/Kent.json", """{ "spring_1": "630 Town 1 1 2", "Mon": "630 Town 1 1 2" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/schedules/Kent",
          "Entries": { "Mon": "630 Town 1 1 2" },
          "When": { "HasSeenEvent": "502261" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("NPC.checkSchedule", v.Reason);
        Assert.Contains("确证无处安放", v.Reason);
    }

    /// <summary>只带容量内的季节条件 ⇒ **仍然交人工**（可能是"有落点但没实现"）。</summary>
    [Fact]
    public void 日程只带季节条件仍交人工()
    {
        Tree("Characters/schedules/Kent.json", """{ "spring_1": "630 Town 1 1 2", "Mon": "630 Town 1 1 2" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/schedules/Kent",
          "Entries": { "Mon": "630 Town 1 1 2" },
          "When": { "Season": "Summer" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }

    /// <summary>节日数据同理：`HasSeenEvent` 无处安放，`Year`（`_yN` 循环）则留人工。</summary>
    [Fact]
    public void 节日带事件条件判无落点()
    {
        Tree("Data/Festivals/fall27.json", """{ "set-up": "x", "set-up_y2": "y" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Festivals/fall27",
          "Entries": { "set-up": "z" },
          "When": { "HasSeenEvent": "5553210" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("_y<年>", v.Reason);
    }

    /// <summary>不在这几族里的资产**不受影响**（口径要收得干净，别外溢）。</summary>
    [Fact]
    public void 别的资产不受影响()
    {
        Tree("Data/mail.json", """{ "spring_13": "hi", "fall_27": "yo" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/mail",
          "Entries": { "spring_13": "新文本" },
          "When": { "HasSeenEvent": "502261" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }


    /// <summary>
    /// ★ **新建**资产同样适用：日程这类"资产还不存在、条件也表达不了"的，
    /// 该判**确证无处安放**，而不是"落点 Unknown（需人工）"——
    /// 后者会让读报告的人去找一个并不存在的机制。
    /// </summary>
    [Fact]
    public void 新建日程资产带事件条件判无落点()
    {
        // 树里**没有**这个资产（新建）
        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/schedules/Martin",
          "Entries": { "Mon": "630 Town 1 1 2" },
          "When": { "HasSeenEvent": "6951319" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("确证无处安放", v.Reason);
    }

    /// <summary>新建资产的条件若**在容量内**，仍交人工（不能替它下结论）。</summary>
    [Fact]
    public void 新建日程资产带季节条件仍交人工()
    {
        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/schedules/Martin",
          "Entries": { "Mon": "630 Town 1 1 2" },
          "When": { "Season": "Spring" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }
}
