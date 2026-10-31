using SdvCpTool.Core.Assets;

namespace SdvCpTool.Core.Tests;

public class ConditionLandingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvland_" + Guid.NewGuid().ToString("N"));

    public ConditionLandingTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string relative, string json)
    {
        string full = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
        return full;
    }

    [Fact]
    public void entry带Condition字段()
    {
        string f = Write("Data/Locations.json", """
        {
          "Town": {
            "Forage": [
              { "ItemId": "1", "Condition": "SEASON spring" },
              { "ItemId": "2" }
            ]
          }
        }
        """);

        Assert.Equal(LandingKind.EntryConditionField, ConditionLanding.Probe(f, "Data/Locations"));
    }

    [Fact]
    public void 嵌套的Condition字段也能识别()
    {
        // 真实结构：MachineData.OutputRules[].Triggers[].Condition
        string f = Write("Data/Machines.json", """
        {
          "Furnace": {
            "OutputRules": [
              { "Id": "a", "Triggers": [ { "Trigger": "DayUpdate", "Condition": "PLAYER_HAS_MAIL x" } ] }
            ]
          }
        }
        """);

        Assert.Equal(LandingKind.EntryConditionField, ConditionLanding.Probe(f, "Data/Machines"));
    }

    // ── 同义条件字段名（原版有 20+ 个不同的名字，不是只有 "Condition"）────────────

    /// <summary>
    /// `Data/IncomingPhoneCalls` 用 `TriggerCondition` / `RingCondition`，`Data/Powers` 用 `UnlockedCondition`。
    /// 只认字面 `"Condition"` 会把它们误判成 Unknown —— 而它们**是货真价实的 GSQ 条件位**。
    /// <para>
    /// <b>⚠ 后 8 个名字里没有 <c>Condition</c> 字样</b>（`CanSocialize` / `CanVisitIsland` /
    /// `ItemDeliveryQuests` / `WinterStarParticipant` / `SpouseGiftJealousy` /
    /// `SpouseWantsChildren` / `SpouseAdopts` / `PerItemCondition`）——
    /// 按「名字带 Condition」筛会**全漏**。依据
    /// <c>docs/research/2026-10-06-condition-consumers.md</c>（逐点附源码行号）。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Data/IncomingPhoneCalls", "TriggerCondition")]
    [InlineData("Data/IncomingPhoneCalls", "RingCondition")]
    [InlineData("Data/Powers", "UnlockedCondition")]
    [InlineData("Data/Powers", "UnlockCondition")]
    [InlineData("Data/Buildings", "BuildCondition")]
    [InlineData("Data/FishPondData", "SkipPermanentlyCondition")]
    // ↓ 名字里没有 Condition 字样，但确实是 GSQ
    [InlineData("Data/Characters", "CanSocialize")]
    [InlineData("Data/Characters", "CanVisitIsland")]
    [InlineData("Data/Characters", "ItemDeliveryQuests")]
    [InlineData("Data/Characters", "WinterStarParticipant")]
    [InlineData("Data/Characters", "SpouseGiftJealousy")]
    [InlineData("Data/Characters", "SpouseWantsChildren")]
    [InlineData("Data/Characters", "SpouseAdopts")]
    [InlineData("Data/Shops", "PerItemCondition")]
    public void 同义条件字段名也能识别(string asset, string field)
    {
        string f = Write("Data/x.json", $$"""
        { "a": { "Id": "a", "{{field}}": "SEASON spring" } }
        """);

        Assert.Equal(LandingKind.EntryConditionField, ConditionLanding.Probe(f, asset));
    }

    /// <summary>
    /// `WeatherConditions` 是<b>容器</b>不是条件串（GSQ 在它元素里的 <c>Condition</c>）——
    /// 台账里核实过，仍然不收。
    /// </summary>
    [Fact]
    public void 是容器不是条件的字段名不收()
    {
        string f = Write("Data/x.json", """
        { "a": { "Id": "a", "WeatherConditions": "Sunny" } }
        """);

        Assert.NotEqual(LandingKind.EntryConditionField, ConditionLanding.Probe(f, "Data/LocationContexts"));
    }

    /// <summary>
    /// **关键反例**：原版里就有名字带 `Condition` 却**不是** GSQ 的 ——
    /// `IgnoreUnlockConditions` 是布尔旁路开关。收进来会写出**语义错误**的产物。
    /// </summary>
    [Fact]
    public void 名字带Condition但不是GSQ的不收()
    {
        string f = Write("Data/x.json", """
        { "a": { "Id": "a", "IgnoreUnlockConditions": true } }
        """);

        Assert.NotEqual(LandingKind.EntryConditionField, ConditionLanding.Probe(f, "Data/x"));
    }

    /// <summary>普查里出现过但**未能确证**是 GSQ 的，宁少勿多不收。</summary>
    [Fact]
    public void 未确证的字段名不收()
    {
        string f = Write("Data/x.json", """
        { "a": { "Id": "a", "WeatherConditions": "Sunny" } }
        """);

        Assert.NotEqual(LandingKind.EntryConditionField, ConditionLanding.Probe(f, "Data/x"));
    }

    [Fact]
    public void 值为字符串且key形如id斜杠条件()
    {
        string f = Write("Data/Events/Town.json", """
        {
          "100/t 600 1200": "continue/64 15/farmer 1 1 1",
          "200/e 123": "continue/64 15/farmer 2 2 2"
        }
        """);

        Assert.Equal(LandingKind.EntryKeyConditions, ConditionLanding.Probe(f, "Data/Events/Town"));
    }

    [Fact]
    public void 字符串字典但键维度认不出时是Unknown不是None()
    {
        // 这是个字符串字典，但键既不是事件键、也不含任何已确证的维度。
        // 我们**不知道**它有没有键维度 —— 必须返回 Unknown（上层 → NeedsReview），
        // 绝不能返回 None（上层映射为 NoLandingPoint，等于断言"无处承载条件"）。
        string f = Write("Data/Festivals/spring13.json", """
        {
          "name": "Egg Festival",
          "conditions": "Town/900 1400",
          "set-up": "changeToTemporaryMap Town-EggFestival"
        }
        """);

        Assert.Equal(LandingKind.Unknown, ConditionLanding.Probe(f, "Data/Festivals/spring13"));
    }

    [Fact]
    public void 对话资产走路径规则()
    {
        string f = Write("Characters/Dialogue/Aguar.json", """
        { "aguar1_1": "你好", "aguar1_2": "再见" }
        """);

        Assert.Equal(LandingKind.DialogueKey, ConditionLanding.Probe(f, "Characters/Dialogue/Aguar"));
    }

    [Fact]
    public void tbin没有落点()
    {
        string f = Write("Maps/Town.tbin", "tBIN10");

        Assert.Equal(LandingKind.None, ConditionLanding.Probe(f, "Maps/Town"));
    }

    [Fact]
    public void png没有落点()
    {
        string f = Write("Characters/Abigail.png", "PNG");

        Assert.Equal(LandingKind.None, ConditionLanding.Probe(f, "Characters/Abigail"));
    }

    [Fact]
    public void 空JSON是None()
    {
        string f = Write("Data/Empty.json", "{}");

        Assert.Equal(LandingKind.None, ConditionLanding.Probe(f, "Data/Empty"));
    }

    [Fact]
    public void 无法解析的JSON是Unknown()
    {
        string f = Write("Data/Broken.json", "{ this is not json");

        Assert.Equal(LandingKind.Unknown, ConditionLanding.Probe(f, "Data/Broken"));
    }

    [Fact]
    public void 键不带斜杠且无已知维度的字符串字典是Unknown()
    {
        // "spring13" 没有下划线，不是 `<season>_` 前缀维度 —— 认不出来。
        // 收紧 None 的核心：不知道就说不知道（Unknown → NeedsReview），不再伪装成"确定没有落点"。
        string f = Write("Data/Festivals/FestivalDates.json", """
        { "spring13": "Egg Festival", "spring24": "Flower Dance" }
        """);

        Assert.Equal(LandingKind.Unknown, ConditionLanding.Probe(f, "Data/Festivals/FestivalDates"));
    }

    [Fact]
    public void 年份后缀键命中KeyEncodedVariant()
    {
        // Data/Festivals 的 `_yN` 年份变体（Event.cs:4417-4448 TryGetFestivalDataForYear）
        string f = Write("Data/Festivals/fall16.json", """
        { "set-up": "x", "set-up_y2": "y", "Abigail": "z" }
        """);

        Assert.Equal(LandingKind.KeyEncodedVariant, ConditionLanding.Probe(f, "Data/Festivals/fall16"));
    }

    [Fact]
    public void 配偶后缀键命中KeyEncodedVariant()
    {
        // `<NPC>_spouse`（Event.cs:12609-12665）
        string f = Write("Data/Festivals/spring13.json", """
        { "Abigail_spouse": "hi", "Emily_spouse": "yo" }
        """);

        Assert.Equal(LandingKind.KeyEncodedVariant, ConditionLanding.Probe(f, "Data/Festivals/spring13"));
    }

    [Fact]
    public void 性别后缀键命中KeyEncodedVariant()
    {
        // `_Male`/`_Female`（FarmerTeam.cs:1315-1317 genderedKey）
        string f = Write("Data/Mail.json", """
        { "passedOut1_Billed_Male": "a", "passedOut1_Billed_Female": "b" }
        """);

        Assert.Equal(LandingKind.KeyEncodedVariant, ConditionLanding.Probe(f, "Data/mail"));
    }

    [Fact]
    public void 季节前缀键命中KeyEncodedVariant()
    {
        // `<season>_<day>[_<year>]`（Game1.cs:8296-8302）
        string f = Write("Data/mail.json", """
        { "spring_13": "a", "spring_13_2": "b" }
        """);

        Assert.Equal(LandingKind.KeyEncodedVariant, ConditionLanding.Probe(f, "Data/mail"));
    }

    [Fact]
    public void 顶层数组资产也能识别Condition字段()
    {
        // Data/TriggerActions 是 List<TriggerActionData>，顶层是 JSON 数组，
        // 条目带 Condition 字段（GSQ）。这类资产不能被判成"没有落点"。
        string f = Write("Data/TriggerActions.json", """
        [
          { "Id": "a", "Trigger": "DayStarted", "Condition": "SEASON spring", "Action": "AddMail Current x" }
        ]
        """);

        Assert.Equal(LandingKind.EntryConditionField, ConditionLanding.Probe(f, "Data/TriggerActions"));
    }

    [Fact]
    public void 认不出形状的对象是Unknown而不是None()
    {
        // 既没有 Condition 字段、值也不是全字符串 —— 结构无法归类。
        // 必须返回 Unknown（上层映射为 NeedsReview），不能伪装成 None（映射为 NoLandingPoint）。
        string f = Write("Data/Weird.json", """
        { "a": { "nested": 1 }, "b": 2 }
        """);

        Assert.Equal(LandingKind.Unknown, ConditionLanding.Probe(f, "Data/Weird"));
    }

    [Fact]
    public void 对话资产的斜杠键不会误判成事件键()
    {
        // 路径识别必须排在事件键形状判断之前，否则这里会返回 EntryKeyConditions（危险方向）
        string f = Write("Characters/Dialogue/Aguar.json", """
        { "spring_Mon/x": "你好" }
        """);

        Assert.Equal(LandingKind.DialogueKey, ConditionLanding.Probe(f, "Characters/Dialogue/Aguar"));
    }
}
