using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Reporting;

namespace SdvCpTool.Core.Tests;

public class ReportWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvrep_" + Guid.NewGuid().ToString("N"));

    public ReportWriterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static PatchVerdict V(Verdict verdict, string target = "Data/X")
    {
        var patch = new ChangePatch { Action = "EditData", Target = target, SourceFile = "content.json", Index = 1 };
        return new PatchVerdict { Patch = patch, Verdict = verdict, Reason = "因为" + verdict };
    }

    /// <summary>
    /// **"哪些内容拿不到"要进报告**：非 CP 框架包（FTM）的导入说明与内容包解析警告，
    /// 原先只打 stdout —— 回头只看 `report.json` 的人就漏掉了（实测：FTM 的刷怪 246 个 area 落不了，
    /// 那句话只在终端里出现过）。没话说时写 `null`，不写空数组。
    /// </summary>
    [Fact]
    public void 导入说明与解析警告进报告()
    {
        string path = Path.Combine(_dir, "r.json");
        AnalysisReport report = AnalysisReport.Build("pack", "tree", new[] { V(Verdict.Converted) });
        report.Notes.Add("FTM 刷怪 246 个 area 没写");
        ReportWriter.WriteJson(report, path);

        JObject root = JObject.Parse(File.ReadAllText(path));
        Assert.Equal("FTM 刷怪 246 个 area 没写", ((JArray)root["notes"]!)[0]!.Value<string>());

        string empty = Path.Combine(_dir, "empty.json");
        ReportWriter.WriteJson(AnalysisReport.Build("pack", "tree", new[] { V(Verdict.Converted) }), empty);
        Assert.Equal(JTokenType.Null, JObject.Parse(File.ReadAllText(empty))["notes"]!.Type);
    }

    [Fact]
    public void 统计按判定分组计数()
    {
        var report = AnalysisReport.Build("pack", "tree", new[]
        {
            V(Verdict.Converted),
            V(Verdict.Converted),
            V(Verdict.NoLandingPoint),
        });

        Assert.Equal(2, report.Summary[Verdict.Converted]);
        Assert.Equal(1, report.Summary[Verdict.NoLandingPoint]);
        Assert.Equal(3, report.Total);
    }

    [Fact]
    public void 统计包含所有判定类别且零值也在()
    {
        var report = AnalysisReport.Build("pack", "tree", new[] { V(Verdict.Converted) });

        Assert.All(Enum.GetValues<Verdict>(), v => Assert.True(report.Summary.ContainsKey(v)));
    }

    /// <summary>
    /// 「同一目的有几条路、各要改多少」要靠这三个字段：落点（这条路具体怎么走）、
    /// 代价（碰多少条目）、原始条件。它们本来就存在 PatchVerdict 里，只是没导出。
    /// </summary>
    [Fact]
    public void 逐条记录导出落点代价与原始条件()
    {
        var patch = new ChangePatch
        {
            Action = "EditData", Target = "Data/Objects", SourceFile = "content.json", Index = 7,
            Raw = JObject.Parse(
                """{ "When": { "Season": "spring" }, "Entries": { "a": "1", "b": "2", "c": "3" } }"""),
        };
        var pv = new PatchVerdict
        {
            Patch = patch, Verdict = Verdict.Converted, Channel = Channel.Gsq,
            Landing = LandingKind.EntryConditionField,
        };

        string path = Path.Combine(_dir, "r.json");
        ReportWriter.WriteJson(AnalysisReport.Build("pack", "tree", new[] { pv }), path);
        JObject one = (JObject)((JArray)JObject.Parse(File.ReadAllText(path))["patches"]!)[0]!;

        Assert.Equal("EntryConditionField", one["landing"]!.Value<string>());
        Assert.Equal(3, one["entryCount"]!.Value<int>());        // Entries 三个键
        Assert.Equal("spring", one["when"]!["Season"]!.Value<string>());
        Assert.Equal("Gsq", one["channel"]!.Value<string>());
    }

    [Fact]
    public void 没有条目列表的补丁代价记一()
    {
        var patch = new ChangePatch
        {
            Action = "Load", Target = "Maps/X", SourceFile = "content.json", Index = 0,
            Raw = JObject.Parse("""{ "FromFile": "assets/Maps/X.tbin" }"""),
        };
        var pv = new PatchVerdict { Patch = patch, Verdict = Verdict.Converted };

        string path = Path.Combine(_dir, "r2.json");
        ReportWriter.WriteJson(AnalysisReport.Build("pack", "tree", new[] { pv }), path);
        JObject one = (JObject)((JArray)JObject.Parse(File.ReadAllText(path))["patches"]!)[0]!;

        Assert.Equal(1, one["entryCount"]!.Value<int>());
        // ⚠ Newtonsoft 把 null 写成 `JValue.CreateNull()` —— 那是个**非 null 的 JToken**，
        //   所以断言 `Assert.Null(one["landing"])` 会挂，要断言**类型**才对。
        Assert.Equal(JTokenType.Null, one["landing"]!.Type);
        Assert.Equal(JTokenType.Null, one["when"]!.Type);
    }

    [Fact]
    public void 可合并数量包含三种已转换判定()
    {
        var report = AnalysisReport.Build("pack", "tree", new[]
        {
            V(Verdict.Converted),
            V(Verdict.ConvertedNoCondition),
            V(Verdict.NeedsReview),
            V(Verdict.NoLandingPoint),
        });

        Assert.Equal(3, report.ConvertibleCount);
        Assert.Equal(1, report.BlockedCount);
    }

    /// <summary>
    /// 被集合发射覆盖的补丁**不算 blocked** —— 自己没有落点，但意图已由组合产物产出。
    /// 算进去就是少报自己的进度（实测 SVE 有 208 条这种）。
    /// </summary>
    [Fact]
    public void 集合发射覆盖的补丁不计入无法合并()
    {
        PatchVerdict emitted = V(Verdict.NoLandingPoint);
        emitted.GroupEmitted = true;
        var report = AnalysisReport.Build("pack", "tree", new[]
        {
            V(Verdict.Converted),
            emitted,
            V(Verdict.NoLandingPoint),
        });

        Assert.Equal(1, report.GroupEmittedCount);
        Assert.Equal(1, report.BlockedCount);   // 只剩没被覆盖的那一条
    }

    [Fact]
    public void 写出的JSON可被重新读取()
    {
        var report = AnalysisReport.Build("pack", "tree", new[] { V(Verdict.Converted) });
        string path = Path.Combine(_dir, "report.json");

        ReportWriter.WriteJson(report, path);

        JObject back = JObject.Parse(File.ReadAllText(path));
        Assert.Equal("pack", back["pack"]!.Value<string>());
        Assert.Equal(1, back["total"]!.Value<int>());
        Assert.Single(back["patches"]!);
        Assert.Equal("Converted", back["patches"]![0]!["verdict"]!.Value<string>());
    }

    [Fact]
    public void JSON里包含原因与建议通道()
    {
        var v = V(Verdict.NoLandingPoint);
        v.SuggestedChannel = "None";
        var report = AnalysisReport.Build("p", "t", new[] { v });
        string path = Path.Combine(_dir, "report.json");

        ReportWriter.WriteJson(report, path);

        JObject back = JObject.Parse(File.ReadAllText(path));
        Assert.Equal("因为NoLandingPoint", back["patches"]![0]!["reason"]!.Value<string>());
        Assert.Equal("None", back["patches"]![0]!["suggestedChannel"]!.Value<string>());
    }

    [Fact]
    public void JSON与摘要区分判定与写入()
    {
        var written = V(Verdict.ConvertedNoCondition);
        written.Written = true;
        var failed = V(Verdict.CreatesNewAsset);
        failed.Written = false;
        failed.WriteNote = "找不到 config";
        var notWritten = V(Verdict.Converted);

        var report = AnalysisReport.Build("p", "t", new[] { written, failed, notWritten });

        Assert.Equal(2, report.AttemptedWriteCount);
        Assert.Equal(1, report.WrittenCount);
        Assert.Equal(1, report.WriteFailedCount);

        string path = Path.Combine(_dir, "report.json");
        ReportWriter.WriteJson(report, path);
        JObject back = JObject.Parse(File.ReadAllText(path));

        Assert.Equal(2, back["attemptedWrite"]!.Value<int>());
        Assert.Equal(1, back["written"]!.Value<int>());
        Assert.Equal(1, back["writeFailed"]!.Value<int>());

        var patches = (JArray)back["patches"]!;
        Assert.True(patches[0]!["written"]!.Value<bool>());
        Assert.False(patches[1]!["written"]!.Value<bool>());
        Assert.Equal("找不到 config", patches[1]!["writeNote"]!.Value<string>());
        // 判定为可转换但本任务未写入 → null，不得伪装成已写入
        Assert.Equal(JTokenType.Null, patches[2]!["written"]!.Type);

        string text = ReportWriter.RenderSummary(report);
        Assert.Contains("实际写入：  1", text);
        Assert.Contains("写入失败：  1", text);
        Assert.Contains("找不到 config", text);
    }

    [Fact]
    public void 未尝试写入那行列出判定构成而不是说范围外()
    {
        // `ConvertibleCount - AttemptedWriteCount` 这个差值**不是**「本任务范围外」的同义词：
        // 它由 `NeedsReview`（需人工确认 —— **待解决**）与 `NotApplicable`（条件折叠后恒假 ——
        // **本就不适用**）构成。原先那句把「还没解决」说成了「不归本任务管」。
        var tried = V(Verdict.Converted);
        tried.Written = true;
        var report = AnalysisReport.Build("p", "t", new[]
        {
            tried,
            V(Verdict.NeedsReview),
            V(Verdict.NeedsReview),
            V(Verdict.NotApplicable),
        });

        string text = ReportWriter.RenderSummary(report);

        Assert.Contains("未尝试写入：3", text);
        Assert.Contains("NeedsReview 2", text);
        Assert.Contains("NotApplicable 1", text);
        Assert.DoesNotContain("本任务范围外", text);
    }

    [Fact]
    public void 未尝试写入的构成里不含集合发射覆盖的补丁()
    {
        // 那些补丁没有逐条落点（意图已并进一次组合发射），不属于「没试过写」。
        var emitted = V(Verdict.NoLandingPoint);
        emitted.GroupEmitted = true;
        var report = AnalysisReport.Build("p", "t", new[]
        {
            V(Verdict.NeedsReview),
            emitted,
        });

        string text = ReportWriter.RenderSummary(report);

        Assert.Contains("NeedsReview 1", text);
        Assert.DoesNotContain("NoLandingPoint 1", text);
    }

    [Fact]
    public void 终端摘要包含总数与不可合并原因()
    {
        var report = AnalysisReport.Build("pack", "tree", new[]
        {
            V(Verdict.Converted),
            V(Verdict.NoLandingPoint, "Data/Festivals/spring13"),
        });

        string text = ReportWriter.RenderSummary(report);

        Assert.Contains("补丁总数：2", text);
        Assert.Contains("Data/Festivals/spring13", text);
    }

    [Fact]
    public void JSON与摘要都带上通道()
    {
        var gsq = V(Verdict.Converted);
        gsq.Channel = Channel.Gsq;
        var bake = V(Verdict.NeedsReview);
        bake.Channel = Channel.Bake;
        var report = AnalysisReport.Build("p", "t", new[] { gsq, bake });
        string path = Path.Combine(_dir, "report.json");

        ReportWriter.WriteJson(report, path);

        JObject back = JObject.Parse(File.ReadAllText(path));
        Assert.Equal("Gsq", back["patches"]![0]!["channel"]!.Value<string>());
        Assert.Equal("Bake", back["patches"]![1]!["channel"]!.Value<string>());

        string text = ReportWriter.RenderSummary(report);
        Assert.Contains("按通道分类：", text);
        Assert.Contains("Gsq", text);
        Assert.Contains("Bake", text);
    }

    /// <summary>
    /// 阻塞条目要**按理由归并**给一张汇总表。只有流水账时读者看不出
    /// 「卡住的是同一件事的若干条」还是「若干件不同的事」—— 而处置方式完全不同。
    /// </summary>
    [Fact]
    public void 阻塞条目按理由归并且掩码掉具体值()
    {
        // 三条同因（只有反引号里的资产名不同）、一条另一个因。
        var a = V(Verdict.NoLandingPoint, "Mods/P/A"); a.Reason = "资产 `Mods/P/A` 没有可承载条件的位置";
        var b = V(Verdict.NoLandingPoint, "Mods/P/B"); b.Reason = "资产 `Mods/P/B` 没有可承载条件的位置";
        var c = V(Verdict.NoLandingPoint, "Mods/P/C"); c.Reason = "资产 `Mods/P/C` 没有可承载条件的位置";
        var d = V(Verdict.LocalEdit, "Maps/Town"); d.Reason = "条件化的地图改动没有通道";

        string text = ReportWriter.RenderSummary(AnalysisReport.Build("p", "t", new[] { a, b, c, d }));

        Assert.Contains("按理由归并", text);
        Assert.Contains("3  [NoLandingPoint] 资产 `X` 没有可承载条件的位置", text);
        Assert.Contains("1  [LocalEdit] 条件化的地图改动没有通道", text);
    }

    /// <summary>反引号只掩码一层，不吞掉后面的正文。</summary>
    [Fact]
    public void 掩码只替换反引号里的内容()
    {
        var a = V(Verdict.LocalEdit, "Maps/A"); a.Reason = "`Maps/A` 这个位置不接受 Condition（原版不读）";
        var b = V(Verdict.LocalEdit, "Maps/B"); b.Reason = "`Maps/B` 这个位置不接受 Condition（原版不读）";

        string text = ReportWriter.RenderSummary(AnalysisReport.Build("p", "t", new[] { a, b }));

        Assert.Contains("2  [LocalEdit] `X` 这个位置不接受 Condition（原版不读）", text);
    }

    /// <summary>
    /// 裸的路径形态（<b>没加反引号</b>）也要掩掉 —— `TargetMissing` 的理由就是这种，
    /// 少这一步每个缺失资产各成一组，把汇总表前十挤满（实测 SVE 11 组本该是 1 类）。
    /// </summary>
    [Fact]
    public void 掩码也认裸的路径形态()
    {
        var a = V(Verdict.TargetMissing, "Maps/X"); a.Reason = "资源树中找不到资产 Maps/X（该资产不在提供的资源树中）";
        var b = V(Verdict.TargetMissing, "Maps/Y"); b.Reason = "资源树中找不到资产 Maps/Y（该资产不在提供的资源树中）";

        string text = ReportWriter.RenderSummary(AnalysisReport.Build("p", "t", new[] { a, b }));

        Assert.Contains("2  [TargetMissing] 资源树中找不到资产 X（该资产不在提供的资源树中）", text);
    }

    /// <summary>
    /// 「待查」必须**列出是哪几格** —— 只给个数的话读者没法据此去啃骨头，
    /// 而这份清单本来就是「还有什么没查」的答案。
    /// </summary>
    [Fact]
    public void 待查要列出是哪几格()
    {
        // `(AssetName, Hearts, Mods/*)` 表里刻意没收录 ⇒ 走 Unknown。
        var v = V(Verdict.NeedsReview);
        v.Patch = new ChangePatch
        {
            Action = "Load", Target = "Mods/P/Thing", SourceFile = "content.json",
            When = new JObject { ["Hearts"] = "Linus 4" },
        };

        string text = ReportWriter.RenderSummary(AnalysisReport.Build("p", "t", new[] { v }));

        Assert.Contains("未定（待查）：", text);
        Assert.Contains("(AssetName, Hearts, Mods/*)", text);
    }

    /// <summary>
    /// 「无法合并」的**列表**必须与汇总行的 `blocked` **同一个口径** ——
    /// 已由集合发射覆盖的（<c>GroupEmitted</c>）要扣掉。
    /// <para>
    /// 回归护栏：实测 SVE 汇总行说 658、下面的列表却列 866，差 208 正好是四季组发射的数。
    /// 同一次报告里两个数对不上，读者只会信错的那个。
    /// </para>
    /// </summary>
    [Fact]
    public void 无法合并的列表扣掉已集合发射的()
    {
        var emitted = V(Verdict.NoLandingPoint, "Mods/P/Shrub");
        emitted.Reason = "资产 `Mods/P/Shrub` 是图片";
        emitted.GroupEmitted = true;
        var stillBlocked = V(Verdict.NoLandingPoint, "LooseSprites/Cursors");
        stillBlocked.Reason = "资产 `LooseSprites/Cursors` 是图片";

        var report = AnalysisReport.Build("p", "t", new[] { emitted, stillBlocked });
        string text = ReportWriter.RenderSummary(report);

        Assert.Equal(1, report.BlockedCount);
        Assert.Contains("无法合并的条目（共 1）", text);   // 不是 2
        Assert.Contains("Cursors", text);
        Assert.DoesNotContain("Shrub", text);
    }
}
