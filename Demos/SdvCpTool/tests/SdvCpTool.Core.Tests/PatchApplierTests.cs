using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class PatchApplierTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvap_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvao_" + Guid.NewGuid().ToString("N"));

    public PatchApplierTests() => Directory.CreateDirectory(_src);

    public void Dispose()
    {
        Directory.Delete(_src, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void Touch(string rel, string content)
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private OutputTree? _tree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush() => _tree?.Flush();

    private PatchApplier Build() => new(_tree = new OutputTree(_src, _out));

    [Fact]
    public void 写入已有资产的Entries()
    {
        Touch("Data/Locations.json", """{ "Town": { "X": 1 } }""");
        Touch("Data/Locations.config", "{}");

        var applier = Build();
        var outcome = applier.ApplyEditData("Data/Locations", """{ "Entries": { "New": { "Y": 2 } } }""");

        Assert.True(outcome.Success);
        Flush();
        JObject written = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Locations.json")));
        Assert.Equal(2, written["New"]!["Y"]!.Value<int>());
        Assert.NotNull(written["Town"]);
    }

    [Fact]
    public void 同资产Entries补丁可合批()
    {
        Touch("Data/Locations.json", """{ "Town": {} }""");
        Touch("Data/Locations.config", "{}");

        var applier = Build();
        var outcomes = applier.TryApplyEditDataBatch("Data/Locations", new[]
        {
            JObject.Parse("""{ "Entries": { "A": { "X": 1 } } }"""),
            JObject.Parse("""{ "Entries": { "B": { "Y": 2 } } }"""),
        });

        Assert.NotNull(outcomes);
        Assert.Equal(2, outcomes!.Count);
        Flush();
        JObject written = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Locations.json")));
        Assert.Equal(1, written["A"]!["X"]!.Value<int>());
        Assert.Equal(2, written["B"]!["Y"]!.Value<int>());
        Assert.NotNull(written["Town"]);
    }

    /// <summary>
    /// FTM 翻出来的补丁用 <c>TargetField: [地点, "Forage"]</c>：改到的顶层键是**数组第一项**。
    /// 不认这个形状 ⇒ 新增地点在中文 `Data/Locations.zh-CN` 里不存在（实测 12 个 FTM 地点）。
    /// </summary>
    [Fact]
    public void FTM的TargetField数组也镜像到中文地点表()
    {
        Touch("Data/Locations.json", """{ "Town": {} }""");
        Touch("Data/Locations.zh-CN.json", """{ "Town": { "Forage": [] } }""");
        Touch("Data/Locations.config", "{}");

        var applier = Build();
        var outcome = applier.ApplyEditData("Data/Locations",
            """{ "TargetField": ["RailRoad", "Forage"], "Entries": { "Item": { "ItemId": "(O)1" } } }""");

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();

        JObject zh = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Locations.zh-CN.json")));
        Assert.NotNull(zh["RailRoad"]);
        Assert.NotNull(zh["Town"]);
    }

    [Fact]
    public void 新建资产并复制同类config()
    {
        Touch("Data/Locations.json", """{ "Town": {} }""");
        Touch("Data/Locations.config", """{"Content":{"Extension":".json"}}""");

        var applier = Build();
        var outcome = applier.ApplyEditData("Data/NewThing", """{ "Entries": { "a": "b" } }""");

        Assert.True(outcome.Success);
        Flush();
        Assert.True(File.Exists(Path.Combine(_out, "Data", "NewThing.json")));
        // 从同目录的 Locations.config 复制而来
        Assert.True(File.Exists(Path.Combine(_out, "Data", "NewThing.config")));
    }

    [Fact]
    public void 新建资产时记录到CreatedFiles()
    {
        Touch("Data/Locations.json", "{}");
        Touch("Data/Locations.config", "{}");

        var applier = Build();
        applier.ApplyEditData("Data/Brand New", """{ "Entries": { "a": "b" } }""");

        Assert.Contains("Data/Brand New.json", applier.Output.CreatedFiles);
        Assert.Contains("Data/Brand New.config", applier.Output.CreatedFiles);
    }

    [Fact]
    public void 找不到同类config时失败()
    {
        Touch("Data/Locations.json", "{}");

        var applier = Build();
        var outcome = applier.ApplyEditData("Data/NewThing", """{ "Entries": { "a": "b" } }""");

        Assert.False(outcome.Success);
        Assert.Contains("config", outcome.FailureReason);
    }

    [Fact]
    public void 已有资产不会被记进CreatedFiles()
    {
        Touch("Data/Locations.json", "{}");
        Touch("Data/Locations.config", "{}");

        var applier = Build();
        applier.ApplyEditData("Data/Locations", """{ "Entries": { "a": "b" } }""");

        Assert.DoesNotContain("Data/Locations.json", applier.Output.CreatedFiles);
    }

    [Fact]
    public void 资产名里的斜杠映射成子目录()
    {
        Touch("Data/Locations.json", "{}");
        Touch("Data/Locations.config", "{}");

        var applier = Build();
        applier.ApplyEditData("Data/Sub/Thing", """{ "Entries": { "a": "b" } }""");
Flush();

        Assert.True(File.Exists(Path.Combine(_out, "Data", "Sub", "Thing.json")));
    }

    // ---- Task 8b：新建资产的条件必须真的挂上（而不是被丢弃） ----

    [Fact]
    public void 新建资产把条件挂到条目Condition字段()
    {
        Touch("Data/Locations.json", """{ "Town": {} }""");
        Touch("Data/Locations.config", "{}");

        var applier = Build();
        var outcome = applier.ApplyEditData(
            "Data/NewLoc", """{ "Entries": { "NewTown": { "Id": "a", "Condition": null } } }""",
            isArrayAsset: false, gsq: "SEASON spring",
            conditionLanding: SdvCpTool.Core.Assets.LandingKind.EntryConditionField);

        Assert.True(outcome.Success);
        Flush();
        JObject written = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "NewLoc.json")));
        Assert.Equal("SEASON spring",
            written["NewTown"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 新建事件资产把条件追加到条目key()
    {
        Touch("Data/Events/Old.json", """{ "1/a b": "x" }""");
        Touch("Data/Events/Old.config", "{}");

        var applier = Build();
        var outcome = applier.ApplyEditData(
            "Data/Events/NewMap", """{ "Entries": { "100/t 600 1200": "continue/64 15/x" } }""",
            isArrayAsset: false, gsq: "SEASON spring",
            conditionLanding: SdvCpTool.Core.Assets.LandingKind.EntryKeyConditions);

        Assert.True(outcome.Success);
        Flush();
        JObject written = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Events", "NewMap.json")));
        Assert.NotNull(written["100/t 600 1200/G SEASON spring"]);
        Assert.Null(written["100/t 600 1200"]);
    }

    // ---------------------------------------------------------------- 坏路径降级（Task 16）

    [Fact]
    public void 资产名含星号时返回失败而不抛异常()
    {
        Touch("Data/Locations.json", "{}");
        Touch("Data/Locations.config", """{"Content":{"Extension":".json"}}""");

        var applier = Build();
        var outcome = applier.ApplyEditData("Data/**Martin**", """{ "Entries": { "a": "b" } }""");

        Assert.False(outcome.Success);
        Assert.Contains("非法字符", outcome.FailureReason);
    }

    [Fact]
    public void 资产名是保留设备名时返回失败而不抛异常()
    {
        Touch("Data/Locations.json", "{}");
        Touch("Data/Locations.config", """{"Content":{"Extension":".json"}}""");

        var applier = Build();
        var outcome = applier.ApplyEditData("Data/NUL", """{ "Entries": { "a": "b" } }""");

        Assert.False(outcome.Success);
        Assert.Contains("保留设备名", outcome.FailureReason);
    }

    [Fact]
    public void 一条坏路径不影响好资产落盘()
    {
        Touch("Data/Locations.json", "{}");
        Touch("Data/Locations.config", """{"Content":{"Extension":".json"}}""");

        var applier = Build();

        // 先写坏的那条（失败），再写好资产 —— 回归钉子：好资产必须照常落盘
        var bad = applier.ApplyEditData("Data/**Martin**", """{ "Entries": { "a": "b" } }""");
        var good = applier.ApplyEditData("Data/GoodThing", """{ "Entries": { "c": "d" } }""");

        Assert.False(bad.Success);
        Assert.True(good.Success);
        Flush();
        Assert.True(File.Exists(Path.Combine(_out, "Data", "GoodThing.json")));
        Assert.False(File.Exists(Path.Combine(_out, "Data", "**Martin**.json")));
    }
}
