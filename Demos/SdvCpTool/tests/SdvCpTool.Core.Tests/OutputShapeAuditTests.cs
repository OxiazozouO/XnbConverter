using SdvCpTool.Core.Reporting;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 产出树自检：**原版是数组的字段，我们有没有写成对象**。
/// <para>
/// 判据收窄到这一条是**试出来的**：更宽的「形状对不上就报」在 2267 份资产上报 116 处、
/// 排除 null 后反而涨到 9145 处（<c>null</c>/<c>[]</c>/<c>{}</c> 都是空但类型不同）。
/// 收窄后同一语料 **0 处**误报，且确实覆盖真问题。
/// </para>
/// </summary>
public class OutputShapeAuditTests : IDisposable
{
    private readonly string _in = Path.Combine(Path.GetTempPath(), "sdvshape_in_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvshape_out_" + Guid.NewGuid().ToString("N"));

    public OutputShapeAuditTests()
    {
        Directory.CreateDirectory(_in);
        Directory.CreateDirectory(_out);
    }

    public void Dispose()
    {
        Directory.Delete(_in, recursive: true);
        Directory.Delete(_out, recursive: true);
    }

    private void Write(string root, string rel, string json)
    {
        string full = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    private OutputShapeAudit.Audit Run() => OutputShapeAudit.Run(_out, _in);

    /// <summary>
    /// 实测踩过的那一类：`Fish` 在模型里是 `List&lt;SpawnFishData&gt;`，却被合成了字典
    /// —— 形状位置没变，容器类型变了，游戏按 List 反序列化会失败。
    /// </summary>
    [Fact]
    public void 数组被写成对象时报出来()
    {
        Write(_in, "Data/Locations.json", """
        { "Town": { "Fish": [ { "Id": "a" } ] } }
        """);
        Write(_out, "Data/Locations.json", """
        { "Town": { "Fish": { "a": { "Id": "a" } } } }
        """);

        OutputShapeAudit.Finding f = Assert.Single(Run().Findings);
        Assert.Equal("Data/Locations.json", f.Asset);
        Assert.Equal("/*/Fish", f.Path);
        Assert.Equal("Fish", f.Field);
    }

    /// <summary>仍然是数组就不报。</summary>
    [Fact]
    public void 仍是数组时不报()
    {
        Write(_in, "Data/Locations.json", """{ "Town": { "Fish": [ { "Id": "a" } ] } }""");
        Write(_out, "Data/Locations.json", """{ "Town": { "Fish": [ { "Id": "a" }, { "Id": "b" } ] } }""");

        Assert.Empty(Run().Findings);
    }

    /// <summary>原版在该位置是**对象**、我们是对象 —— 不报（这条只管数组退化成对象）。</summary>
    [Fact]
    public void 原版本来就是对象时不报()
    {
        Write(_in, "Data/X.json", """{ "a": { "Cfg": { "K": 1 } } }""");
        Write(_out, "Data/X.json", """{ "a": { "Cfg": { "K": 2 } } }""");

        Assert.Empty(Run().Findings);
    }

    /// <summary>mod 自建的新字段不报 —— 否则每加一个字段就刷一条噪音。</summary>
    [Fact]
    public void 原版没见过的字段名不报()
    {
        Write(_in, "Data/X.json", """{ "a": { "Name": "x" } }""");
        Write(_out, "Data/X.json", """{ "a": { "Name": "x", "BrandNew": { "nested": 1 } } }""");

        Assert.Empty(Run().Findings);
    }

    /// <summary>原版没有这份资产时跳过，不妄下结论。</summary>
    [Fact]
    public void 原版无此资产时跳过()
    {
        Write(_out, "Data/OnlyMine.json", """{ "a": { "Fish": { "b": 1 } } }""");

        OutputShapeAudit.Audit a = Run();

        Assert.Empty(a.Findings);
        Assert.Equal(1, a.SkippedNoVanilla);
    }

    /// <summary>同上：同一棵树 ⇒ 报跳过，而不是报 0 条。</summary>
    [Fact]
    public void 输入树与产出树同一棵时报跳过()
    {
        OutputShapeAudit.Audit audit = OutputShapeAudit.Run(_in, _in);

        Assert.True(audit.SkippedSameTree);
        Assert.Equal(0, audit.CheckedAssets);
        Assert.Empty(audit.Findings);
    }

    /// <summary>
    /// ★ **资产类型自检**：产出与原版的 `.config` 声明的扩展名对不上 ⇒ 报出来。
    /// 实测代价：`Maps/Coop` 树里帖图与地图同时存在、描述符说 `.png`
    /// ⇒ 打包后游戏拿帖图当地图用 ⇒ **进鸡舍必炸**。
    /// </summary>
    [Fact]
    public void 资产类型与原版不一致要报出来()
    {
        // 原版：Coop 是地图；产出：描述符说 .png
        Directory.CreateDirectory(Path.Combine(_in, "Maps"));
        Directory.CreateDirectory(Path.Combine(_out, "Maps"));
        File.WriteAllText(Path.Combine(_in, "Maps", "Coop.config"),
            """{ "Content": { "Extension": ".tbin" }, "Readers": [] }""");
        File.WriteAllText(Path.Combine(_out, "Maps", "Coop.config"),
            """{ "Content": { "Extension": ".png" }, "Readers": [] }""");

        AssetTypeAudit.Audit audit = AssetTypeAudit.Run(new OutputTree(_in, _out));

        AssetTypeAudit.Finding f = Assert.Single(audit.Findings);
        Assert.Equal("Maps/Coop", f.Asset);
        Assert.Equal(".png", f.ProducedExtension);
        Assert.Equal(".tbin", f.VanillaExtension);
    }
}
