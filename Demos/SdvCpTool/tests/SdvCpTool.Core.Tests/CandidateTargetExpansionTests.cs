using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// **按候选值展开 Target**：`Target` 里的令牌代不掉、但取值可枚举时，按候选资产名各写一份
/// （<see cref="CandidateTargetExpansion"/>）。
/// <para>
/// 实测来源（2026-10-15）：`Lnh's Farms Convenient Road` 按**农场类型**定义同一个令牌 <c>Farm</c>，
/// 补丁写 <c>"Target": "Maps/{{Farm}}"</c> —— 想在"玩家实际在玩的那张农场图"上盖一条便捷通道。
/// 展开之前整条判 `TargetContainsTokens` 不写 ⇒ 实机任何农场图上都没有那条传送。
/// </para>
/// <para>
/// ⚠ 这里钉的是**安全前提**：只在形状能被证明互斥（单键、各块键名相同、取值两两不同且都是字面量）
/// 且候选数不超上限时才展开；否则整条不动、保持原判。
/// </para>
/// </summary>
public class CandidateTargetExpansionTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvcand_" + Guid.NewGuid().ToString("N"));

    public CandidateTargetExpansionTests() => Directory.CreateDirectory(_pack);

    public void Dispose()
    {
        if (Directory.Exists(_pack)) Directory.Delete(_pack, recursive: true);
    }

    private void Write(string relative, string content)
    {
        string full = Path.Combine(_pack, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private List<ChangePatch> Expand(string dynamicTokens, string changes, out int added,
        out List<string> notes)
    {
        Write("content.json",
            $$"""{ "Format": "2.9.0", "DynamicTokens": [ {{dynamicTokens}} ], "Changes": [ {{changes}} ] }""");

        ContentPack pack = ContentPack.Load(_pack);
        DynamicTokenResolver tokens = DynamicTokenResolver.Load(_pack, null);
        notes = new List<string>();
        added = CandidateTargetExpansion.Apply(pack.Patches, tokens, notes);
        return pack.Patches;
    }

    /// <summary>实测那个形状：八种农场类型各一块（缺一个默认块 —— 这正是"代不掉"的来源）。</summary>
    private const string FarmToken = """
        { "Name": "Farm", "Value": "Farm",             "When": { "Farmtype": "Standard" } },
        { "Name": "Farm", "Value": "Farm_Foraging",    "When": { "Farmtype": "Forest" } },
        { "Name": "Farm", "Value": "Farm_Fishing",     "When": { "Farmtype": "Riverland" } },
        { "Name": "Farm", "Value": "Farm_Mining",      "When": { "Farmtype": "Hilltop" } },
        { "Name": "Farm", "Value": "Farm_Combat",      "When": { "Farmtype": "Wilderness" } },
        { "Name": "Farm", "Value": "Farm_Island",      "When": { "Farmtype": "Beach" } },
        { "Name": "Farm", "Value": "Farm_FourCorners", "When": { "Farmtype": "FourCorners" } },
        { "Name": "Farm", "Value": "Farm_Ranching",    "When": { "Farmtype": "MeadowlandsFarm" } }
        """;

    private const string RoadPatch =
        """{ "Action": "EditMap", "Target": "Maps/{{Farm}}", "FromFile": "assets/Farm_Enter.tmx", "PatchMode": "Overlay" }""";

    // ------------------------------------------------------------------ 会展开

    [Fact]
    public void 按农场类型的令牌在Target里代不掉时按候选各写一份()
    {
        List<ChangePatch> patches = Expand(FarmToken, RoadPatch, out int added, out List<string> notes);

        Assert.Equal(7, added);                       // 8 个候选 = 1 条换 8 条
        Assert.Equal(8, patches.Count);
        Assert.Equal(
            new[]
            {
                "Maps/Farm", "Maps/Farm_Foraging", "Maps/Farm_Fishing", "Maps/Farm_Mining",
                "Maps/Farm_Combat", "Maps/Farm_Island", "Maps/Farm_FourCorners", "Maps/Farm_Ranching",
            }.OrderBy(t => t, StringComparer.Ordinal).ToArray(),
            patches.Select(p => p.Target).OrderBy(t => t, StringComparer.Ordinal).ToArray());
        Assert.All(patches, p => Assert.Equal("assets/Farm_Enter.tmx", p.FromFile));
        Assert.Single(notes);
        Assert.Contains("`{{Farm}}` 代不掉但取值可枚举（8 个）", notes[0]);
        Assert.Contains("Maps/Farm_Ranching", notes[0]);   // 说明里带上了每个候选落点
    }

    [Fact]
    public void 补丁里其它字段的同令牌也一起代掉()
    {
        List<ChangePatch> patches = Expand(FarmToken,
            """{ "Action": "EditMap", "Target": "Maps/{{Farm}}", "FromFile": "assets/{{Farm}}_extra.tmx" }""",
            out _, out _);

        Assert.Contains(patches, p => p.FromFile == "assets/Farm_Fishing_extra.tmx");
        Assert.All(patches, p => Assert.DoesNotContain("{{", p.FromFile));
    }

    [Fact]
    public void When里的令牌不动()
    {
        List<ChangePatch> patches = Expand(FarmToken,
            """{ "Action": "EditMap", "Target": "Maps/{{Farm}}", "When": { "HasMod |contains=Other": "{{Farm}}" } }""",
            out _, out _);

        Assert.All(patches, p => Assert.Equal("{{Farm}}", (string?)p.When!["HasMod |contains=Other"]));
    }

    [Fact]
    public void 多目标里只有一项含令牌时不带令牌那项只写一份()
    {
        // 实测来源（`[CP] Ultimate Farm`）：`Maps/UltimateFarm, Maps/{{file}}`，
        // 而且那个补丁用的是 `TextOperations: Append`（往 MapProperties **追加**）——
        // 不按项拆的话 `Maps/UltimateFarm` 会被追加 N 遍（CP 只追加一遍）。
        List<ChangePatch> patches = Expand(FarmToken,
            """{ "Action": "EditMap", "Target": "Maps/UltimateFarm, Maps/{{Farm}}", "FromFile": "assets/Cave.tmx" }""",
            out int added, out List<string> notes);

        Assert.Equal(8, added);                       // 1 份"不含令牌"的 + 8 份候选
        Assert.Single(patches.Where(p => p.Target == "Maps/UltimateFarm"));
        Assert.Equal(8, patches.Count(p => p.Target!.StartsWith("Maps/Farm", StringComparison.Ordinal)));
        Assert.DoesNotContain(patches, p => p.Target!.Contains("UltimateFarm,"));
        Assert.Contains("只写一次", notes[0]);
    }

    [Fact]
    public void 多目标但内容里也有该令牌时不拆出单份()
    {
        // 内容里也有令牌 ⇒ "不含令牌"的那一份代不掉（会永远不生效）⇒ 不出那一份
        List<ChangePatch> patches = Expand(FarmToken,
            """{ "Action": "EditMap", "Target": "Maps/UltimateFarm, Maps/{{Farm}}", "FromFile": "assets/{{Farm}}.tmx" }""",
            out _, out _);

        Assert.DoesNotContain(patches, p => p.Target == "Maps/UltimateFarm");
    }

    // ------------------------------------------------------------------ 不展开（安全前提）

    [Fact]
    public void 取值里还含令牌时不展开()
    {
        // 实测就有这样的块：别的维度代成 {{FarmType}} / CustomFarmMapName，枚举不出来
        List<ChangePatch> patches = Expand("""
            { "Name": "Farm", "Value": "{{FarmType}}",          "When": { "Farmtype": "X" } },
            { "Name": "Farm", "Value": "{{CustomFarmMapName}}", "When": { "Farmtype": "Y" } }
            """, RoadPatch, out int added, out _);

        Assert.Equal(0, added);
        Assert.Single(patches);
        Assert.Equal("Maps/{{Farm}}", patches[0].Target);
    }

    [Fact]
    public void 只取一个维度其余块记档()
    {
        // 实测那个令牌的真实形状：8 块 Farmtype 维度（要的）+ 2 块自定义农场
        //（键是 `Query: …` 两个、取值本身是令牌）—— 只取前一组，后一组**不否定整体**
        List<ChangePatch> patches = Expand("""
            { "Name": "Farm", "Value": "Farm",          "When": { "Farmtype": "Standard" } },
            { "Name": "Farm", "Value": "Farm_Fishing",  "When": { "Farmtype": "Riverland" } },
            { "Name": "Farm", "Value": "Farm_Island",   "When": { "Farmtype": "Beach" } },
            { "Name": "Farm", "Value": "{{FarmType}}",  "When": { "Query: 'a'='b'": true, "Query: 'c'=''": true } }
            """, RoadPatch, out int added, out List<string> notes);

        Assert.Equal(2, added);
        Assert.Equal(
            new[] { "Maps/Farm", "Maps/Farm_Fishing", "Maps/Farm_Island" },
            patches.Select(p => p.Target).ToArray());
        Assert.Contains("另有 1 个块不属于这个维度", notes[0]);
    }

    [Fact]
    public void 代得掉的令牌不动()
    {
        // 有一个无条件块 ⇒ 解析器代得掉（CP 语义：最后一个匹配块胜出）⇒ 不在这里碰
        List<ChangePatch> patches = Expand("""
            { "Name": "Farm", "Value": "Farm_Fishing", "When": { "Farmtype": "Riverland" } },
            { "Name": "Farm", "Value": "Farm" }
            """, RoadPatch, out int added, out _);

        Assert.Equal(0, added);
        Assert.Single(patches);
    }

    /// <summary>
    /// ★ 所有块的条件都**静态为假**（没有一块是"不知道"）⇒ CP 里这个令牌**没有值**，整条补丁不会落，
    /// 不能按候选枚举。实测（2026-10-19）`[CP] Ultimate Farm` 的 `{{file}}`：8 块全是
    /// `ReplaceVanillaFarm: X`，而配置里该项是空（`AllowBlank`）—— 枚举出来的 `Maps/Farm` 那份
    /// 把 Ultimate Farm 的洞穴传送追加到了 Grandpa's Farm 上（`FarmCave 37 13` 在 25×20 图外）。
    /// </summary>
    [Fact]
    public void 所有块条件都静态为假时不展开()
    {
        Write("content.json",
            $$"""{ "Format": "2.9.0", "DynamicTokens": [ {{FarmToken}} ], "Changes": [ {{RoadPatch}} ] }""");

        ContentPack pack = ContentPack.Load(_pack);
        DynamicTokenResolver tokens = DynamicTokenResolver.Load(_pack,
            _ => new DynamicTokenResolver.WhenResult(false));
        var notes = new List<string>();

        int added = CandidateTargetExpansion.Apply(pack.Patches, tokens, notes);

        Assert.Equal(0, added);
        Assert.Single(pack.Patches);
        Assert.Equal("Maps/{{Farm}}", pack.Patches[0].Target);
        Assert.Empty(notes);
    }

    [Fact]
    public void 各块条件键名不同时不展开()
    {
        // 不是"一个维度的取值" ⇒ 互斥证明不了，不猜
        List<ChangePatch> patches = Expand("""
            { "Name": "Farm", "Value": "Farm_A", "When": { "Farmtype": "Standard" } },
            { "Name": "Farm", "Value": "Farm_B", "When": { "Season": "Fall" } }
            """, RoadPatch, out int added, out _);

        Assert.Equal(0, added);
        Assert.Single(patches);
    }

    [Fact]
    public void 条件里有两个键时不展开()
    {
        List<ChangePatch> patches = Expand("""
            { "Name": "Farm", "Value": "Farm_A", "When": { "Farmtype": "Standard", "Season": "Fall" } },
            { "Name": "Farm", "Value": "Farm_B", "When": { "Farmtype": "Forest", "Season": "Spring" } }
            """, RoadPatch, out int added, out _);

        Assert.Equal(0, added);
        Assert.Single(patches);
    }

    [Fact]
    public void 候选数超过上限时不展开()
    {
        // 13 块（> MaxCandidates=12）：形状不像"一个维度的取值"
        var blocks = string.Join(", ",
            Enumerable.Range(0, 13).Select(i =>
                $$"""{ "Name": "Farm", "Value": "Farm_{{i}}", "When": { "Farmtype": "T{{i}}" } }"""));
        List<ChangePatch> patches = Expand(blocks, RoadPatch, out int added, out _);

        Assert.Equal(0, added);
        Assert.Single(patches);
    }
}
