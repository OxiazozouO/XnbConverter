using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class ConditionWriterTests
{
    [Fact]
    public void 写进Condition字段()
    {
        var data = JObject.Parse("""{ "Town": { "Forage": [ { "Id": "a", "Condition": null } ] } }""");

        var r = ConditionWriter.Apply(data, "Town.Forage.0", "SEASON spring",
            LandingKind.EntryConditionField);

        Assert.True(r.Success);
        Assert.Equal("SEASON spring",
            data["Town"]!["Forage"]![0]!["Condition"]!.Value<string>());
    }

    /// <summary>
    /// ★ 落点路径的**数组段是 `Id`** 时也要认（不只认数字下标）。
    /// 实测聚宝盆制作 2 条：`TargetField` 指到 `…OutputRules.<Id>.Triggers`，
    /// 合并器认 `Id`、这里原先只认下标 ⇒ 「合并成功、写条件却报定位不到」。
    /// </summary>
    [Fact]
    public void 落点路径的数组段可以是Id()
    {
        var data = JObject.Parse("""
        { "M": { "OutputRules": [ { "Id": "Noodles", "Triggers": [ { "RequiredItemId": "x", "Condition": null } ] } ] } }
        """);

        var r = ConditionWriter.Apply(data, "M.OutputRules.Noodles.Triggers.0", "SEASON spring",
            LandingKind.EntryConditionField);

        Assert.True(r.Success);
        Assert.Equal("SEASON spring",
            data["M"]!["OutputRules"]![0]!["Triggers"]![0]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 事件键追加G段()
    {
        var data = JObject.Parse("""{ "100/t 600 1200": "continue/64 15/x" }""");

        var r = ConditionWriter.Apply(data, "100/t 600 1200", "SEASON spring",
            LandingKind.EntryKeyConditions);

        Assert.True(r.Success);
        Assert.NotNull(data["100/t 600 1200/G SEASON spring"]);
        Assert.Null(data["100/t 600 1200"]);
    }

    [Fact]
    public void 无条件字段写空串会清掉()
    {
        var data = JObject.Parse("""{ "A": { "Condition": "OLD" } }""");

        ConditionWriter.Apply(data, "A", "", LandingKind.EntryConditionField);

        Assert.Null(data["A"]!["Condition"]!.Value<string>());
    }

    // ---- 从补丁结构推断落点条目 ----

    [Fact]
    public void TargetField指到的那条是条目()
    {
        // Data/Characters：TargetField ["June"] → 条目就是 June
        var patch = JObject.Parse("""{ "TargetField": ["June"], "Entries": { "Calendar": "AlwaysShown" } }""");

        var paths = ConditionWriter.EntryPaths(patch, LandingKind.EntryConditionField);

        Assert.Equal(new[] { "June" }, paths);
    }

    [Fact]
    public void 顶层Entries的每个key是条目()
    {
        var patch = JObject.Parse("""{ "Entries": { "New": { "Y": 2 }, "Other": { "Y": 3 } } }""");

        var paths = ConditionWriter.EntryPaths(patch, LandingKind.EntryConditionField);

        Assert.Equal(new[] { "New", "Other" }, paths);
    }

    [Fact]
    public void Fields的叶子对象是条目()
    {
        // Data/Weddings：Fields.Attendees.<name> 下是 ID/Setup 标量 → 条目路径 Attendees.<name>
        var patch = JObject.Parse(
            """{ "Fields": { "Attendees": { "Olga": { "ID": "Olga", "Setup": "Olga 29 69 0" } } } }""");

        var paths = ConditionWriter.EntryPaths(patch, LandingKind.EntryConditionField);

        Assert.Equal(new[] { "Attendees.Olga" }, paths);
    }

    [Fact]
    public void 事件键落点返回Entries的key()
    {
        var patch = JObject.Parse("""{ "Entries": { "100/t 600 1200": "x", "101/e 5": "y" } }""");

        var paths = ConditionWriter.EntryPaths(patch, LandingKind.EntryKeyConditions);

        Assert.Equal(new[] { "100/t 600 1200", "101/e 5" }, paths);
    }

    [Fact]
    public void 落点条目定位不到时失败()
    {
        var data = JObject.Parse("""{ "A": { "B": 1 } }""");

        var r = ConditionWriter.Apply(data, "A.C", "SEASON spring", LandingKind.EntryConditionField);

        Assert.False(r.Success);
        Assert.Contains("定位不到", r.FailureReason);
    }

    // ---- 跨补丁的同落点条件合并（Task 6b）----

    [Fact]
    public void 同一落点多条补丁条件做OR合并()
    {
        var data = JObject.Parse("""{ "A": { "Condition": null } }""");
        var state = new LandingState();

        ConditionWriter.Apply(data, "A", "q1", LandingKind.EntryConditionField, state);
        ConditionWriter.Apply(data, "A", "q2", LandingKind.EntryConditionField, state);

        Assert.Equal("ANY \"q1\" \"q2\"", data["A"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 三条补丁条件全部并入ANY()
    {
        var data = JObject.Parse("""{ "A": { "Condition": null } }""");
        var state = new LandingState();

        ConditionWriter.Apply(data, "A", "q1", LandingKind.EntryConditionField, state);
        ConditionWriter.Apply(data, "A", "q2", LandingKind.EntryConditionField, state);
        ConditionWriter.Apply(data, "A", "q3", LandingKind.EntryConditionField, state);

        Assert.Equal("ANY \"q1\" \"q2\" \"q3\"", data["A"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 资产自带Condition与新条件做AND()
    {
        // 条目在基础树里已有 Condition → 新条件是追加限定，用顶层逗号（AND）
        var data = JObject.Parse("""{ "A": { "Condition": "PLAYER_HAS_MET Target Dwarf" } }""");

        ConditionWriter.Apply(data, "A", "q1", LandingKind.EntryConditionField, new LandingState());

        Assert.Equal("PLAYER_HAS_MET Target Dwarf, q1", data["A"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 资产自带Condition与多条补丁条件做AND加OR()
    {
        // 资产自带条件 AND (补丁条件 OR) —— GSQ 顶层逗号 + ANY 是合法写法
        var data = JObject.Parse("""{ "A": { "Condition": "base" } }""");
        var state = new LandingState();

        ConditionWriter.Apply(data, "A", "q1", LandingKind.EntryConditionField, state);
        ConditionWriter.Apply(data, "A", "q2", LandingKind.EntryConditionField, state);

        Assert.Equal("base, ANY \"q1\" \"q2\"", data["A"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 前序条件是组合条件时拒绝OR合并()
    {
        // 补丁自身的 When 是"与" → GSQ 含顶层逗号，ANY 包不住它。不许硬凑，判失败。
        var data = JObject.Parse("""{ "A": { "Condition": null } }""");
        var state = new LandingState();

        ConditionWriter.Apply(data, "A", "SEASON spring, HAS_MOD X", LandingKind.EntryConditionField, state);
        var r = ConditionWriter.Apply(data, "A", "SEASON fall", LandingKind.EntryConditionField, state);

        Assert.False(r.Success);
        Assert.Contains("ANY", r.FailureReason);
        // 失败时数据保持在上一次成功合并的结果
        Assert.Equal("SEASON spring, HAS_MOD X", data["A"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 相同补丁条件不重复累积()
    {
        var data = JObject.Parse("""{ "A": { "Condition": null } }""");
        var state = new LandingState();

        ConditionWriter.Apply(data, "A", "q1", LandingKind.EntryConditionField, state);
        ConditionWriter.Apply(data, "A", "q1", LandingKind.EntryConditionField, state);

        Assert.Equal("q1", data["A"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 事件键落点不做OR合并()
    {
        // 事件条目不是"单槽"：一个 key 只承接它自己的那一个 When，不与前序补丁的条件合并
        var data = JObject.Parse("""{ "100/t 600 1200": "continue/64 15/x" }""");

        ConditionWriter.Apply(data, "100/t 600 1200", "q1", LandingKind.EntryKeyConditions, new LandingState());

        Assert.NotNull(data["100/t 600 1200/G q1"]);
    }

    [Fact]
    public void 状态副本独立互不影响()
    {
        var state = new LandingState { BaseCondition = "base", Touched = true };
        state.PatchConditions.Add("q1");

        var clone = state.Clone();
        clone.PatchConditions.Add("q2");

        Assert.Equal(new[] { "q1" }, state.PatchConditions);
        Assert.Equal(new[] { "q1", "q2" }, clone.PatchConditions);
    }
}

/// <summary>跨补丁写入的追踪要靠 <see cref="PatchApplier"/> 的运行内状态，这里走真实文件路径验证。</summary>
public class PatchApplierConditionTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvcw_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvcwo_" + Guid.NewGuid().ToString("N"));

    public PatchApplierConditionTests() => Directory.CreateDirectory(_src);

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

    private JObject ReadOut(string rel)
    {
        Flush();
        return JObject.Parse(File.ReadAllText(Path.Combine(_out, rel.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void 跨补丁同落点写入做OR合并()
    {
        // Ridgeside 的 Data/Weddings.Attendees.Lenny 场景：多条补丁按不同关系声明同一个 attendee
        Touch("Data/Weddings.json",
            """{ "Attendees": { "Lenny": { "ID": "Lenny", "Condition": null } } }""");
        var applier = Build();
        const string patch = """{ "Fields": { "Attendees": { "Lenny": { "ID": "Lenny" } } } }""";

        Assert.True(applier.ApplyEditData("Data/Weddings", patch, false,
            "PLAYER_NPC_RELATIONSHIP Current Alissa Engaged", LandingKind.EntryConditionField).Success);
        Assert.True(applier.ApplyEditData("Data/Weddings", patch, false,
            "PLAYER_NPC_RELATIONSHIP Current Shiro Engaged", LandingKind.EntryConditionField).Success);

        JObject written = ReadOut("Data/Weddings.json");
        Assert.Equal(
            "ANY \"PLAYER_NPC_RELATIONSHIP Current Alissa Engaged\" \"PLAYER_NPC_RELATIONSHIP Current Shiro Engaged\"",
            written["Attendees"]!["Lenny"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 资产自带Condition跨补丁保持AND()
    {
        // 基础树里 Dwarf 自带条件；多条补丁各加一个条件 → 自带条件 AND (补丁条件 OR)
        Touch("Data/Weddings.json",
            """{ "Attendees": { "Dwarf": { "ID": "Dwarf", "Condition": "PLAYER_HAS_MET Target Dwarf" } } }""");
        var applier = Build();
        const string patch = """{ "Fields": { "Attendees": { "Dwarf": { "ID": "Dwarf" } } } }""";

        Assert.True(applier.ApplyEditData("Data/Weddings", patch, false, "q1", LandingKind.EntryConditionField).Success);
        Assert.True(applier.ApplyEditData("Data/Weddings", patch, false, "q2", LandingKind.EntryConditionField).Success);

        JObject written = ReadOut("Data/Weddings.json");
        Assert.Equal("PLAYER_HAS_MET Target Dwarf, ANY \"q1\" \"q2\"",
            written["Attendees"]!["Dwarf"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 事件键多条补丁各自成键()
    {
        // 同一事件 ID 的两条补丁条件不同 → 各成一个 key，不能被合并掉（脚本可能不同）
        Touch("Data/Events/Town.json", """{ "100/t 600 1200": "base" }""");
        var applier = Build();
        const string patch = """{ "Entries": { "100/t 600 1200": "continue/64 15/x" } }""";

        Assert.True(applier.ApplyEditData("Data/Events/Town", patch, false, "q1", LandingKind.EntryKeyConditions).Success);
        Assert.True(applier.ApplyEditData("Data/Events/Town", patch, false, "q2", LandingKind.EntryKeyConditions).Success);

        JObject written = ReadOut("Data/Events/Town.json");
        Assert.NotNull(written["100/t 600 1200/G q1"]);
        Assert.NotNull(written["100/t 600 1200/G q2"]);
    }

    // ── 落点校验：Condition 只有在游戏真的会读它的位置才有意义 ──────────────

    /// <summary>
    /// `Data/Objects` **只在 `GeodeDrops[]` 下有** `Condition`（全库 11 处），条目级没有。
    /// 未加校验时，条件会被写到物件条目上而游戏不读 —— 补丁却记成「写入成功」，静默错。
    /// 实测 SVE 产物里出现过 25 个这种条目。
    /// </summary>
    [Fact]
    public void 同级都没有Condition时拒写()
    {
        // 形状与真实 Data/Objects 一致：某个条目下有嵌套的 GeodeDrops[].Condition，
        // 但**条目级**没有 —— 资产级递归扫描会误判成"支持条目级条件"。
        var data = JObject.Parse("""
        { "791": { "GeodeDrops": [ { "Condition": "SEASON winter" } ] },
          "107": { "Name": "X", "Price": 10 } }
        """);

        var r = ConditionWriter.Apply(data, "107", "PLAYER_HAS_SEEN_EVENT Any 1",
            LandingKind.EntryConditionField);

        Assert.False(r.Success);
        Assert.Contains("不接受 Condition", r.FailureReason);
        Assert.Null(data["107"]!["Condition"]);   // 绝不落盘
    }

    [Fact]
    public void Weddings字典条目条件落到子对象()
    {
        // `Data/Weddings.Attendees` 是 Dictionary<string, WeddingAttendeeData>，
        // CP 用 TargetField:["Attendees"] + Entries:{<id>:{...}}。条件应落到每个 attendee。
        var data = JObject.Parse(
            """{"Attendees":{"A":{"Id":"A","Condition":null},"B":{"Id":"B"}}}""");
        var patch = JObject.Parse(
            """{"TargetField":["Attendees"],"Entries":{"A":{"Id":"A"},"B":{"Id":"B"}}}""");

        Assert.Null(ConditionWriter.RefusalFor(data, patch,
            LandingKind.EntryConditionField, "Data/Weddings"));

        var r = ConditionWriter.Apply(data, "Attendees", "PLAYER_HAS_SEEN_EVENT Current 1",
            LandingKind.EntryConditionField, assetName: "Data/Weddings", patch: patch);
        Assert.True(r.Success, r.FailureReason);
        Assert.Equal("PLAYER_HAS_SEEN_EVENT Current 1",
            data["Attendees"]!["A"]!["Condition"]!.Value<string>());
        Assert.Equal("PLAYER_HAS_SEEN_EVENT Current 1",
            data["Attendees"]!["B"]!["Condition"]!.Value<string>());
    }

    [Fact]
    public void 商店Items数组的条件落到具体商品()
    {
        var data = JObject.Parse(
            """{"Shop":{"Items":[{"Id":"a","Condition":null},{"Id":"b"}]}}""");
        var patch = JObject.Parse(
            """{"TargetField":["Shop","Items"],"Entries":{"a":{"Id":"a"}}}""");

        Assert.Null(ConditionWriter.RefusalFor(data, patch,
            LandingKind.EntryConditionField, "Data/Shops"));

        var r = ConditionWriter.Apply(data, "Shop.Items", "PLAYER_HAS_SEEN_EVENT Current 1",
            LandingKind.EntryConditionField, assetName: "Data/Shops", patch: patch);
        Assert.True(r.Success, r.FailureReason);
        Assert.Equal("PLAYER_HAS_SEEN_EVENT Current 1",
            data["Shop"]!["Items"]![0]!["Condition"]!.Value<string>());
        Assert.Null(data["Shop"]!["Items"]![1]!["Condition"]);
    }

    [Fact]
    public void Weddings字典同一子条目多条补丁做OR()
    {
        var data = JObject.Parse(
            """{"Attendees":{"A":{"Id":"A","Condition":null}}}""");
        var patch = JObject.Parse(
            """{"TargetField":["Attendees"],"Entries":{"A":{"Id":"A"}}}""");
        var state = new LandingState();

        Assert.True(ConditionWriter.Apply(data, "Attendees", "PLAYER_HAS_SEEN_EVENT Current 1",
            LandingKind.EntryConditionField, state, "Data/Weddings", patch).Success);
        Assert.True(ConditionWriter.Apply(data, "Attendees", "PLAYER_HAS_SEEN_EVENT Current 2",
            LandingKind.EntryConditionField, state, "Data/Weddings", patch).Success);

        Assert.Equal("ANY \"PLAYER_HAS_SEEN_EVENT Current 1\" \"PLAYER_HAS_SEEN_EVENT Current 2\"",
            data["Attendees"]!["A"]!["Condition"]!.Value<string>());
    }

    /// <summary>同级已有 Condition ⇒ 这个位置的条件字段是原版机制的一部分，照写。</summary>
    [Fact]
    public void 同级已有Condition时照写()
    {
        var data = JObject.Parse("""
        { "Town": { "Forage": [ { "Id": "a", "Condition": "SEASON spring" }, { "Id": "b" } ] } }
        """);

        var r = ConditionWriter.Apply(data, "Town.Forage.1", "SEASON summer",
            LandingKind.EntryConditionField);

        Assert.True(r.Success);
        Assert.Equal("SEASON summer", data["Town"]!["Forage"]![1]!["Condition"]!.Value<string>());
    }

    /// <summary>条目自己已有 Condition ⇒ 直接放行（不必看同级）。</summary>
    [Fact]
    public void 条目自身已有Condition时照写()
    {
        var data = JObject.Parse("""{ "a": { "Condition": "SEASON spring" } }""");

        Assert.True(ConditionWriter.Apply(data, "a", "SEASON summer",
            LandingKind.EntryConditionField).Success);
    }

    /// <summary>
    /// **对象成员**的落点：同级已有 `Condition` ⇒ 该位置的条件字段是原版机制的一部分，照写。
    /// <para>
    /// 这条是回归用例：`JToken.Parent` 对对象成员返回的是 **`JProperty`** 而不是 `JObject`，
    /// 少了往上再取一层，`AcceptsCondition` 就会落进 `_ => false` **一律拒绝** ——
    /// 实测 RSV 的 `Data/Weddings` 的 `Attendees.Lenny/Lorenzo/Yuuma` 就是这么被误杀的。
    /// </para>
    /// </summary>
    [Fact]
    public void 对象成员的同级已有Condition时照写()
    {
        var data = JObject.Parse("""
        { "Attendees": {
            "Marnie": { "ID": "Marnie", "Condition": "!SEASON winter" },
            "Lenny":  { "ID": "Lenny", "Setup": "Lenny 32 69 0" } } }
        """);

        var r = ConditionWriter.Apply(data, "Attendees.Lenny", "PLAYER_NPC_RELATIONSHIP Current Ian Engaged",
            LandingKind.EntryConditionField);

        Assert.True(r.Success, r.FailureReason);
        Assert.Equal("PLAYER_NPC_RELATIONSHIP Current Ian Engaged",
            data["Attendees"]!["Lenny"]!["Condition"]!.Value<string>());
    }

    /// <summary>同级**都**没有 Condition 时仍然拒写（别把校验整个放空）。</summary>
    [Fact]
    public void 对象成员的同级都没有Condition时拒写()
    {
        var data = JObject.Parse("""
        { "Attendees": { "Marnie": { "ID": "Marnie" }, "Lenny": { "ID": "Lenny" } } }
        """);

        var r = ConditionWriter.Apply(data, "Attendees.Lenny", "SEASON spring",
            LandingKind.EntryConditionField);

        Assert.False(r.Success);
        Assert.Contains("不接受 Condition", r.FailureReason);
    }

    /// <summary>
    /// ★ **模型兵底**：数据里一个先例都没有，但模型说条目元素自带 `Condition`
    /// ⇒ 该接受（实测 SVE 的 2 个被动节日 + 聚宝盆的 1 条特殊订单就是这么被误拒的）。
    /// </summary>
    [Fact]
    public void 模型有条件槽时接受顶层条目()
    {
        var data = JObject.Parse("""{ "SomeFestival": { "Id": "SomeFestival" } }""");

        // 没给 assetName⇒ 不认（不猜）
        Assert.False(ConditionWriter.Apply(data, "SomeFestival", "HAS_MAIL ccVault",
            LandingKind.EntryConditionField).Success);

        var r = ConditionWriter.Apply(data, "SomeFestival", "HAS_MAIL ccVault",
            LandingKind.EntryConditionField, assetName: "Data/PassiveFestivals");

        Assert.True(r.Success);
        Assert.Equal("HAS_MAIL ccVault", data["SomeFestival"]!["Condition"]!.Value<string>());
    }

    /// <summary>反向边界：模型里**没有**条件槽的资产（`ObjectData`）仍然该拒（语料 33 条）。</summary>
    [Fact]
    public void 模型没槽的资产仍该拒()
    {
        var data = JObject.Parse("""{ "24": { "Name": "X", "Price": 10 } }""");

        var r = ConditionWriter.Apply(data, "24", "SEASON spring",
            LandingKind.EntryConditionField, assetName: "Data/Objects");

        Assert.False(r.Success);
        Assert.Contains("不接受 Condition", r.FailureReason);
    }

    /// <summary>
    /// ★ **条目名里带点**也要定位得到（实测：SVE 的 2 个被动节日与聚宝盆的
    /// 1 条特殊订单就是这么被"定位不到"拒掉的）。合并器用的是**字面键**
    /// （`EditDataMerger.ApplyEntries` 就是 `obj[prop.Name]`），所以定位也得先整串找。
    /// </summary>
    [Fact]
    public void 条目名带点也能定位()
    {
        var data = JObject.Parse("""{ "Author.SomeFestival": { "Id": "Author.SomeFestival", "Condition": "SEASON spring" } }""");

        var r = ConditionWriter.Apply(data, "Author.SomeFestival", "HAS_MAIL ccVault",
            LandingKind.EntryConditionField);

        Assert.True(r.Success);
        Assert.Contains("SEASON spring", data["Author.SomeFestival"]!["Condition"]!.Value<string>());
    }

    /// <summary>
    /// ★ “定位不到”要说清是**合并把条目丢了**（值里含代不掉的令牌），
    /// 而不是让人去找一个不存在的落点问题。实测：SVE 的 2 个被动节日
    /// （`{{Season}}`）与聚宝盆的 1 条特殊订单（`{{GeorgeSpecialOrderRoses}}`）。
    /// </summary>
    [Fact]
    public void 定位不到时要带上合并的说明()
    {
        var data = JObject.Parse("""{ "Existing": { "Condition": "" } }""");
        JObject patch = JObject.Parse("""
        { "Entries": { "NewOne": { "Name": "{{Season}}", "Condition": "" } } }
        """);

        string? refusal = ConditionWriter.RefusalFor(data, patch, LandingKind.EntryConditionField,
            "Data/PassiveFestivals");

        Assert.NotNull(refusal);
        Assert.Contains("定位不到条件落点条目", refusal);
        Assert.Contains("合并把条目丢了", refusal);
    }

    /// <summary>
    /// ★ **数组落点**：`TargetField` 指到"数组字段"时（实测 `[CP] Integrated Minecarts`：
    /// `Data/Minecarts` 的 `["Default","Destinations"]`），条件要落到**本条补丁 `Entries` 那几个 Id**
    /// 的条目自己的 `Condition` 上 —— 游戏就是这么逐条查的（`GameLocation.cs:10246` 对每个
    /// `MinecartDestinationData` 查 `destination.Condition`）。
    /// ⚠ 只写那几个条目：铺给全数组会把**原版条目**也变成有条件的。
    /// </summary>
    [Fact]
    public void 数组落点只写本条补丁那几个条目()
    {
        var data = JObject.Parse("""
            { "Default": { "Destinations": [
                { "Id": "old", "Condition": null },
                { "Id": "jibb.Cart1", "TargetLocation": "X" } ] } }
            """);
        var patch = JObject.Parse("""
            { "Action": "EditData", "Target": "Data/Minecarts",
              "TargetField": [ "Default", "Destinations" ],
              "Entries": { "jibb.Cart1": { "Id": "jibb.Cart1", "TargetLocation": "X" } },
              "When": { "Enablejibb.Cart1": true } }
            """);

        string? refused = ConditionWriter.RefusalFor(data, patch, LandingKind.EntryConditionField, "Data/Minecarts");
        Assert.True(refused == null, refused);

        var r = ConditionWriter.Apply(data, "Default.Destinations",
            "PLAYER_VISITED_LOCATION Any Desert", LandingKind.EntryConditionField,
            assetName: "Data/Minecarts", patch: patch);

        Assert.True(r.Success);
        // ⚠ 按下标断言会踩合并器的插入位置（新条目不一定在后面）⇒ **按 Id 查**。
        JArray carts = (JArray)data["Default"]!["Destinations"]!;
        JObject ById(string id) => carts.OfType<JObject>().First(o => (string?)o["Id"] == id);
        Assert.Equal("PLAYER_VISITED_LOCATION Any Desert", ById("jibb.Cart1")["Condition"]!.Value<string>());
        // ⚠ `JValue.CreateNull()` 是个**非 null 的 JToken** ⇒ 断言要看**值**，不能 `Assert.Null(token)`
        Assert.Null(ById("old")["Condition"]?.Value<string>());             // 原版那条**不动**
    }

    /// <summary>模型没背书过的数组位置**不开这个口子** —— 仍判「无处安放」（要加得先核模型）。</summary>
    [Fact]
    public void 数组落点没有模型背书的仍拒绝()
    {
        var data = JObject.Parse("""{ "A": { "Items": [ { "Id": "x" } ] } }""");
        var patch = JObject.Parse("""
            { "Action": "EditData", "Target": "Data/Somewhere",
              "TargetField": [ "A", "Items" ], "Entries": { "x": { "Id": "x" } } }
            """);

        string? refusal = ConditionWriter.RefusalFor(data, patch, LandingKind.EntryConditionField,
            "Data/Somewhere");

        Assert.NotNull(refusal);
        Assert.Contains("无处安放", refusal);
    }
}
