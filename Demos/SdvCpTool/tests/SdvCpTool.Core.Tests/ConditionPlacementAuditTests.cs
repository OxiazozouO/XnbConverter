using SdvCpTool.Core.Reporting;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 产出树自检：写出去的 <c>Condition</c> 有没有落在**游戏不读**的位置。
/// <para>
/// 判据是「与原版树同一份资产的**路径形状**对照」—— 形状把条目键抽象成 <c>*</c>、
/// 数组下标抽象成 <c>[]</c>。落在原版从未出现过的形状上 ⇒ 游戏不会读它。
/// </para>
/// </summary>
public class ConditionPlacementAuditTests : IDisposable
{
    private readonly string _in = Path.Combine(Path.GetTempPath(), "sdvaud_in_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvaud_out_" + Guid.NewGuid().ToString("N"));

    public ConditionPlacementAuditTests()
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

    /// <summary>
    /// 先写**原版**（输入树），建出产出树（构造时会把输入树拷过去），**再**写产物。
    /// 顺序反了的话产物会被构造时的拷贝覆盖掉。
    /// </summary>
    private ConditionPlacementAudit.Audit Run()
    {
        var output = new OutputTree(_in, _out);
        foreach ((string rel, string json) in _produced)
            Write(_out, rel, json);
        return ConditionPlacementAudit.Run(output, SdvCpTool.Core.Assets.ConditionLanding.ConditionFieldNames);
    }

    private readonly List<(string Rel, string Json)> _produced = new();

    /// <summary>记一份「产物」内容，等产出树建好后再落盘。</summary>
    private void Produced(string rel, string json) => _produced.Add((rel, json));

    /// <summary>
    /// 原版只在嵌套的 <c>GeodeDrops[]</c> 下有 <c>Condition</c>，产出却写在**条目级**
    /// —— 这正是实测踩过的那个静默错（SVE 的 `Data/Objects`，25 个条目）。
    /// </summary>
    [Fact]
    public void 条目级条件对不上原版形状时报出来()
    {
        const string vanilla = """
        { "791": { "GeodeDrops": [ { "Condition": "SEASON winter" } ] }, "107": { "Name": "X" } }
        """;
        Write(_in, "Data/Objects.json", vanilla);
        Produced("Data/Objects.json", """
        { "791": { "GeodeDrops": [ { "Condition": "SEASON winter" } ] },
          "107": { "Name": "X", "Condition": "PLAYER_HAS_SEEN_EVENT Any 1" } }
        """);

        ConditionPlacementAudit.Audit a = Run();

        ConditionPlacementAudit.Finding f = Assert.Single(a.Findings);
        Assert.Equal("Data/Objects.json", f.Asset);
        Assert.Equal("/*/Condition", f.Path);
    }

    /// <summary>形状对得上就不报 —— 别把正当位置误伤成可疑。</summary>
    [Fact]
    public void 形状对得上时不报()
    {
        Write(_in, "Data/Locations.json", """
        { "Beach": { "Fish": [ { "Condition": "SEASON spring" } ] } }
        """);
        Produced("Data/Locations.json", """
        { "Beach": { "Fish": [ { "Condition": "SEASON summer" } ] } }
        """);

        Assert.Empty(Run().Findings);
    }

    /// <summary>原版没有这份资产时**不妄下结论** —— 无从对照就跳过并计数。</summary>
    [Fact]
    public void 原版没有该资产时跳过()
    {
        Produced("Data/NewThing.json", """{ "a": { "Condition": "SEASON spring" } }""");

        ConditionPlacementAudit.Audit a = Run();

        Assert.Empty(a.Findings);
        Assert.Equal(1, a.SkippedNoVanilla);
    }

    /// <summary>
    /// ⚠ **这条 2026-10-13 反过来了**：原以为"同义字段名（非字面 `Condition`）写在条目级 ⇒ 形状对不上 ⇒ 报"，
    /// 但 `UnlockedCondition` **就是** powers 数据自己的字段（`StardewValley.GameData.Powers/PowersData.cs:24`，
    /// 文档里也早就写着"每个 power 自带 `UnlockedCondition`"）⇒ **游戏会反序列化它** ⇒ 不该报。
    /// <para>
    /// 那条"只看原版形状"的判据正是假阳性的来源（详见 `合法但原版没用过的条件字段不报`）。
    /// 现在判据是「**模型里没有这个字段** 且 原版同一形状也没用过」。
    /// </para>
    /// </summary>
    [Fact]
    public void 模型里有的同义字段名不报()
    {
        Write(_in, "Data/Powers.json", """{ "a": { "Inner": { "UnlockedCondition": "SEASON spring" } } }""");
        Produced("Data/Powers.json", """{ "a": { "Name": "x", "UnlockedCondition": "SEASON spring" } }""");

        Assert.Empty(Run().Findings);
    }

    /// <summary>
    /// ★ **输入树与产出树是同一棵时必须报“跳过”**（链式跑法 `--append` 就是这种）。
    /// 拿产出跟它自己比，形状当然一模一样 ⇒ **永远 0 条**；
    /// 而 `misplaced = 0` 会被读成"没问题"。实测踩过：同一份 SVE，
    /// 逐包对原版树跑报 1 条（`Data/WorldMap.json` 的 `MapAreas[]/KnownCondition`），
    /// 链式跑法里每个包都是 0。
    /// </summary>
    [Fact]
    public void 输入树与产出树同一棵时报跳过()
    {
        var output = new OutputTree(_in, _in);          // 同一棵
        ConditionPlacementAudit.Audit audit = ConditionPlacementAudit.Run(
            output, SdvCpTool.Core.Assets.ConditionLanding.ConditionFieldNames);

        Assert.True(audit.SkippedSameTree);
        Assert.Equal(0, audit.CheckedAssets);
        Assert.Empty(audit.Findings);
    }

    /// <summary>
    /// ★ **继承来的字段也算游戏的字段**（2026-10-13 修的那条假阳性）：
    /// `FruitTreeFruitData.Condition` 声明在基类 `GenericSpawnItemDataWithCondition` 上
    /// （子类注释还专门指向它）⇒ 游戏**会**反序列化 ⇒ 不报。
    /// <para>
    /// 原来判据只有"原版同一形状上有没有" ⇒ 实测 `Data/FruitTrees` 的 `Fruit[]/Condition`
    /// **每个包报 19 处**假阳性，把自检淹了。
    /// </para>
    /// </summary>
    [Fact]
    public void 合法但原版没用过的条件字段不报()
    {
        Write(_in, "Data/FruitTrees.json",
            """{ "Apple": { "Fruit": [ { "Id": "Default", "ItemId": "(O)613" } ] } }""");
        Produced("Data/FruitTrees.json", """
        { "Apple": { "Fruit": [ { "Id": "Default", "ItemId": "(O)613",
                                  "Condition": "SEASON Summer" } ] } }
        """);

        ConditionPlacementAudit.Audit a = Run();

        Assert.Empty(a.Findings);
        Assert.Equal(1, a.CheckedAssets);
    }

    /// <summary>
    /// 模型里**没有**的字段 ⇒ 游戏整段忽略 ⇒ 报（实测 SVE 的 `MapAreas[]/KnownCondition`，
    /// `WorldMapAreaData` 里没有这个字段 —— 包的 bug，CP 下同样被忽略）。
    /// </summary>
    [Fact]
    public void 模型里没有的条件字段仍要报()
    {
        Write(_in, "Data/WorldMap.json",
            """{ "Valley": { "MapAreas": [ { "Id": "Area" } ] } }""");
        Produced("Data/WorldMap.json", """
        { "Valley": { "MapAreas": [ { "Id": "Area",
                                      "KnownCondition": "LOCATION_ACCESSIBLE Railroad" } ] } }
        """);

        ConditionPlacementAudit.Finding f = Assert.Single(Run().Findings);
        Assert.Equal("KnownCondition", f.Field);
    }
}
