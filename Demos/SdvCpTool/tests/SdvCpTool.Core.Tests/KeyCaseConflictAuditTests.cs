using SdvCpTool.Core.Validating;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 键名大小写自检：同一**模型对象**里仅大小写不同的键 —— 反序列化大小写不敏感，
/// 两键撞同一属性 ⇒ 该资产**打不出 XNB**（实测 SVE 的 Data/WorldMap 回包时 4 个资产失败）。
/// </summary>
public class KeyCaseConflictAuditTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvkcc_" + Guid.NewGuid().ToString("N"));

    public KeyCaseConflictAuditTests()
    {
        Directory.CreateDirectory(Path.Combine(_out, "Data", "Events"));
        Directory.CreateDirectory(Path.Combine(_out, "Data", "FishPondData"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void Write(string rel, string json)
    {
        string p = Path.Combine(_out, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, json);
    }

    [Fact]
    public void 模型对象里仅大小写不同的键要报出()
    {
        Write("Data/WorldMap.json",
            """{ "Valley": { "MapAreas": [ { "Id": "Mountain", "Tooltips": [ ], "ToolTips": { "Mines": { } } } ] } }""");

        var audit = KeyCaseConflictAudit.Run(_out);

        Assert.Equal(1, audit.Conflicts);
        Assert.Equal(1, audit.FatalConflicts);        // 数组 vs 对象 ⇒ 致命
        Assert.True(audit.Findings[0].Fatal);
        Assert.Equal("Valley.MapAreas[0]", audit.Findings[0].Path);
        Assert.Equal("Tooltips", audit.Findings[0].Key1);
        Assert.Equal("ToolTips", audit.Findings[0].Key2);
    }

    /// <summary>
    /// **类型相同**时反序列化**不报错** —— 两个键撞到同一属性、**后者覆盖前者**（静默改数据）。
    /// 实测依据：SVE 产出树 72 处冲突里只有 4 处致命（回包失败数正好是 4）——
    /// 不分档就会把「静默覆盖」也说成「打不出包」，那是对不上的数。
    /// </summary>
    [Fact]
    public void 类型相同时不致命但静默覆盖()
    {
        Write("Data/FishPondData.json",
            """[ { "ID": "FlashShifter.X", "Id": "FlashShifter.Y" } ]""");

        var audit = KeyCaseConflictAudit.Run(_out);

        Assert.Equal(1, audit.Conflicts);
        Assert.Equal(0, audit.FatalConflicts);
        Assert.False(audit.Findings[0].Fatal);
    }

    /// <summary>
    /// **不误报「键即数据」的资产**：`Data/Events` 是 <c>Map&lt;string,string&gt;</c>，
    /// 两个只差大小写的**事件键**是合法的两个条目。
    /// </summary>
    [Fact]
    public void 事件字典的两个大小写不同键不算冲突()
    {
        Write("Data/Events/Farm.json",
            """{ "992559/n clothfound": "a", "992559/n clothFound": "b" }""");

        Assert.Equal(0, KeyCaseConflictAudit.Run(_out).Conflicts);
    }

    /// <summary>`Strings/*` 整片都是键即数据，同样不报。</summary>
    [Fact]
    public void 字符串资产不报()
    {
        Write("Data/../Strings/StringsFromCSFiles.json", """{ "a.1": "x", "A.1": "y" }""");

        Assert.Equal(0, KeyCaseConflictAudit.Run(_out).Conflicts);
    }

    /// <summary>本地化变体（`X.de-DE.json`）与主资产同形，跳过不重复计。</summary>
    [Fact]
    public void 本地化变体跳过()
    {
        Write("Data/WorldMap.de-DE.json",
            """{ "A": { "Tooltips": [ ], "ToolTips": { } } }""");

        var audit = KeyCaseConflictAudit.Run(_out);
        Assert.Equal(0, audit.CheckedAssets);
    }

    [Fact]
    public void 正常资产不报()
    {
        Write("Data/Objects.json", """{ "16": { "Name": "x", "Price": 1 } }""");

        var audit = KeyCaseConflictAudit.Run(_out);
        Assert.Equal(1, audit.CheckedAssets);
        Assert.Equal(0, audit.Conflicts);
    }
}
