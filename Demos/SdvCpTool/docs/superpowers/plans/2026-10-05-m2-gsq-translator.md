# M2 实施计划：GSQ 翻译器

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 CP 的 `When` 条件树翻译成游戏原生的 GameStateQuery 字符串，并用金标准 + 实跑双重验证。

**Architecture:** 三层 —— ①`WhenNode` 补上「值」字段（现在缺）；②`ConditionVocabulary` 按权威对照表订正分类；③`GsqTranslator` 纯函数式翻译（条件树 → GSQ 字符串）。翻译器不抛异常，翻译不了时返回结构化失败原因。

**Tech Stack:** C# net8.0、Newtonsoft.Json、xUnit

**权威依据：** `docs/research/2026-10-05-cp-to-gsq-token-mapping.md`。**实现前必须通读它**，尤其是 §一 的三条"静默出错规则"。

**范围外**：本计划**不做合并**（不写资源树），也不做"原生地图机制"通道（那是 M3）。

---

## 文件结构

```
src/SdvCpTool.Core/Conditions/
├─ WhenNode.cs                    (改) 加 Value 字段
├─ WhenParser.cs                  (改) 捕获值
├─ TokenKind.cs                   (改) 加 GsqApproximate / NoGsqEquivalent
├─ ConditionVocabulary.cs         (改) 按对照表订正
└─ GsqTranslator.cs               (新) 翻译器主体
tests/SdvCpTool.Core.Tests/
├─ WhenParserTests.cs             (改) 加值的断言
├─ ConditionVocabularyTests.cs    (改) 按订正后的分类重写
├─ ConditionVocabularyCoverageTests.cs (新) 与 CP 令牌清单做差集
└─ GsqTranslatorTests.cs          (新) 金标准
```

---

## Task 1: `WhenNode` 携带值

现在 `WhenParser.ParseEntry(name, value)` 收到值却只把 `name` 传给 `SplitKey`，**值被丢弃**。翻译器需要它（`"Season": "spring"` 里的 `"spring"`）。

**Files:**
- Modify: `src/SdvCpTool.Core/Conditions/WhenNode.cs`
- Modify: `src/SdvCpTool.Core/Conditions/WhenParser.cs`
- Test: `tests/SdvCpTool.Core.Tests/WhenParserTests.cs`

- [ ] **Step 1: 追加失败的测试**

在 `WhenParserTests.cs` 末尾（类内）追加：

```csharp
    [Fact]
    public void 键节点携带原始值()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"Season\": \"spring\"}"))!;
        Assert.Equal("spring", Assert.Single(root.Children).Value);
    }

    [Fact]
    public void 带参数时值与参数分开()
    {
        var raw = WhenParser.Parse(JObject.Parse("{\"HasSeenEvent: hostPlayer\": \"123\"}"))!;
        var key = Assert.Single(raw.Children);
        Assert.Equal("HasSeenEvent", key.KeyName);
        Assert.Equal("hostPlayer", key.Arguments);
        Assert.Equal("123", key.Value);
    }

    [Fact]
    public void 组合节点的值不参与()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"AND\": [{\"Season\": \"spring\"}]}"))!;
        Assert.Null(root.Children[0].Value);
    }

    [Fact]
    public void 非字符串值转成字符串()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"HasSeenEvent\": 75160255}"))!;
        Assert.Equal("75160255", Assert.Single(root.Children).Value);
    }
```

- [ ] **Step 2: 运行确认失败**

```bash
cd /d/SdvCpTool && dotnet test --filter WhenParserTests
```

预期：编译失败，`WhenNode` 没有 `Value`。

- [ ] **Step 3: 给 WhenNode 加字段**

修改 `src/SdvCpTool.Core/Conditions/WhenNode.cs`，在 `Arguments` 之后加：

```csharp
    /// <summary>Key 节点的原始值（CP 值侧）。组合节点（All/Any/Not）为 null。</summary>
    public string? Value;
```

并把工厂方法改为：

```csharp
    public static WhenNode Key(string name, string? arguments = null, string? value = null) =>
        new() { Kind = WhenNodeKind.Key, KeyName = name, Arguments = arguments, Value = value };
```

- [ ] **Step 4: 让 WhenParser 捕获值**

修改 `src/SdvCpTool.Core/Conditions/WhenParser.cs`：

`ParseEntry` 的 fallback 分支改为把值传下去：

```csharp
        return SplitKey(name, value);
```

`SplitKey` 签名与实现改为：

```csharp
    /// <summary>
    /// 把条件键拆成名字与参数。规则来自 CP 的词法器（`Lexer.cs:23-26`）：
    /// `:`（位置参数）与 `|`（具名参数）都是分隔符，取**先出现**的那个；
    /// `|` 本身计入参数串（原文注释：`{{Token |key=value}}` 中 `:` 是隐含的，`|` 属于参数）。
    /// </summary>
    private static WhenNode SplitKey(string raw, JToken value)
    {
        string? text = value.Type == JTokenType.String
            ? value.Value<string>()
            : value.ToString();

        int colon = raw.IndexOf(':');
        int pipe = raw.IndexOf('|');

        int idx = colon < 0 ? pipe
                : pipe < 0 ? colon
                : Math.Min(colon, pipe);

        if (idx < 0) return WhenNode.Key(raw.Trim(), null, text);

        string name = raw[..idx].Trim();
        string args = raw[idx] == '|' ? raw[idx..].Trim() : raw[(idx + 1)..].Trim();
        return args.Length == 0
            ? WhenNode.Key(name, null, text)
            : WhenNode.Key(name, args, text);
    }
```

`ParseSingle` 里对**非对象**值的处理保持不变（`WhenNode.Key(value.ToString())`）—— 那是"裸值当键名"的退化情形，没有值可言。

- [ ] **Step 5: 运行确认通过**

```bash
cd /d/SdvCpTool && dotnet test
```

预期：`已通过! - 失败: 0，通过: 146`（142 + 4）

- [ ] **Step 6: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "WhenNode 携带条件值"
```

---

## Task 2: 订正条件分类表

按 `docs/research/2026-10-05-cp-to-gsq-token-mapping.md` §五 订正。**先通读那份文档再动手。**

**Files:**
- Modify: `src/SdvCpTool.Core/Conditions/TokenKind.cs`
- Modify: `src/SdvCpTool.Core/Conditions/ConditionVocabulary.cs`
- Test: `tests/SdvCpTool.Core.Tests/ConditionVocabularyTests.cs`（重写）
- Test: `tests/SdvCpTool.Core.Tests/ConditionVocabularyCoverageTests.cs`（新建）

- [ ] **Step 1: 写覆盖度回归测试（先写它，它会立刻暴露分类表的错）**

创建 `tests/SdvCpTool.Core.Tests/ConditionVocabularyCoverageTests.cs`：

```csharp
using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 与 CP 的真实令牌清单做差集，防止分类表随时间漂移。
/// 清单来源：CP 源码 Framework/Conditions/ConditionType.cs（版本 2.9.1）。
/// </summary>
public class ConditionVocabularyCoverageTests
{
    /// <summary>CP 2.9.1 的全部条件令牌。改动它必须说明版本来源。</summary>
    private static readonly string[] CpConditionTypes =
    {
        "AbsoluteFilePath", "ChildGenders", "ChildNames", "Count", "DailyLuck", "Day", "DayEvent",
        "DayOfWeek", "DaysPlayed", "FarmCave", "FarmMapAsset", "FarmName", "FarmType",
        "FarmhouseUpgrade", "FirstValidFile", "FormatAssetName", "FromFile", "HasActiveQuest",
        "HasCaughtFish", "HasConversationTopic", "HasCookingRecipe", "HasCraftingRecipe",
        "HasDialogueAnswer", "HasFile", "HasFlag", "HasMod", "HasProfession", "HasReadLetter",
        "HasSeenEvent", "HasValue", "HasVisitedLocation", "HasWalletItem", "HavingChild", "Hearts",
        "InternalAssetKey", "IsCommunityCenterComplete", "IsJojaMartComplete", "IsMainPlayer",
        "IsOutdoors", "Language", "LocationContext", "LocationName", "LocationOwnerId",
        "LocationUniqueName", "Lowercase", "Merge", "ModId", "PathPart", "PlayerGender",
        "PlayerName", "PreferredPet", "Pregnant", "Query", "Random", "Range", "Relationship",
        "Render", "Roommate", "Round", "Season", "SkillLevel", "Spouse", "Target",
        "TargetPathOnly", "TargetWithoutPath", "Time", "Uppercase", "Weather", "Year"
    };

    [Fact]
    public void 每个CP令牌都应有明确分类()
    {
        // 断言：CP 真实存在的令牌不能被归为 Unknown —— 存在却"不认识"是分类表的疏漏
        var unknown = CpConditionTypes
            .Where(t => ConditionVocabulary.Classify(t) == TokenKind.Unknown)
            .ToArray();

        Assert.True(unknown.Length == 0,
            "以下 CP 真实令牌未被分类：" + string.Join(", ", unknown));
    }

    [Fact]
    public void 不应把非CP令牌当成可翻译()
    {
        // RandomNumber 是 Ridgeside 用 DynamicTokens 自建的，不是 CP 内置
        Assert.NotEqual(TokenKind.GsqTranslatable, ConditionVocabulary.Classify("RandomNumber"));
        // 拼写错误的版本也绝不能是可翻译
        Assert.NotEqual(TokenKind.GsqTranslatable, ConditionVocabulary.Classify("IsJoMartComplete"));
    }

    [Fact]
    public void 令牌清单与CP版本一致()
    {
        Assert.Equal(69, CpConditionTypes.Length);
    }
}
```

- [ ] **Step 2: 运行，看它暴露什么**

```bash
cd /d/SdvCpTool && dotnet test --filter ConditionVocabularyCoverageTests
```

预期：**失败**。`不应把非CP令牌当成可翻译` 会因 `RandomNumber` 仍在 `Translatable` 而失败。这正是要修的。

- [ ] **Step 3: 扩展 TokenKind**

修改 `src/SdvCpTool.Core/Conditions/TokenKind.cs`：

```csharp
namespace SdvCpTool.Core.Conditions;

public enum TokenKind
{
    /// <summary>有 GSQ 等价关键字，可机械翻译。</summary>
    GsqTranslatable,

    /// <summary>能生成 GSQ，但语义与 CP 有偏差，须人工确认。</summary>
    GsqApproximate,

    /// <summary>确认 GSQ 原理上表达不了（需要返回集合或遍历实体）。</summary>
    NoGsqEquivalent,

    /// <summary>依赖 mod 运行环境（装了哪些 mod、翻译文件），无法离线翻译。</summary>
    EnvironmentDependent,

    /// <summary>纯计算或取值，字面量参数可离线求值。</summary>
    PureComputation,

    /// <summary>CP 内容包之外的 mod 通过 C# 注册的令牌。</summary>
    CustomModToken,

    /// <summary>不认识的键名。</summary>
    Unknown
}
```

- [ ] **Step 4: 订正 ConditionVocabulary**

修改 `src/SdvCpTool.Core/Conditions/ConditionVocabulary.cs` 的三个集合，并新增一个：

```csharp
    // 有 GSQ 等价关键字，可机械翻译（依据 docs/research/2026-10-05-cp-to-gsq-token-mapping.md）
    private static readonly HashSet<string> Translatable = new(StringComparer.OrdinalIgnoreCase)
    {
        // 日期与天气
        "Day", "DayOfWeek", "DaysPlayed", "Season", "Time", "Weather", "Year",
        // 玩家
        "FarmhouseUpgrade", "HasCaughtFish", "HasConversationTopic", "HasCookingRecipe",
        "HasCraftingRecipe", "HasDialogueAnswer", "HasFlag", "HasProfession", "HasReadLetter",
        "HasSeenEvent", "HasVisitedLocation", "LocationContext", "LocationName",
        "LocationUniqueName", "PlayerGender", "PreferredPet", "SkillLevel",
        // 关系
        "Hearts", "Relationship", "Roommate", "Spouse",
        // 世界
        "FarmCave", "FarmName", "FarmType", "IsCommunityCenterComplete", "IsJojaMartComplete"
    };

    // 能生成 GSQ 但语义有偏差
    private static readonly HashSet<string> Approximate = new(StringComparer.OrdinalIgnoreCase)
    {
        "DayEvent",     // GSQ 只有 IS_FESTIVAL_DAY 布尔，匹配不了节日名
        "IsMainPlayer", // GSQ IS_HOST 是"本机是否主机"，无玩家参数
        "IsOutdoors"    // GSQ LOCATION_IS_OUTDOORS 按地点不按玩家
    };

    // GSQ 原理上表达不了 —— 需要返回集合或遍历实体
    private static readonly HashSet<string> NoEquivalent = new(StringComparer.OrdinalIgnoreCase)
    {
        "DailyLuck",       // 小数日运，GSQ 无比较关键字
        "HasActiveQuest",  // GSQ 完全没有任务关键字
        "PlayerName",      // GSQ 无玩家名关键字
        "ChildNames",      // GSQ 只有 PLAYER_HAS_CHILDREN 计数
        "ChildGenders",
        "HavingChild",     // 需遍历所有玩家读 NextBirthingDate
        "Pregnant",
        "HasWalletItem"    // 无通用钱包关键字；仅 KeyToTheTown 例外
    };
```

`Environment` 保持不变。`Computation` **补入 4 个令牌**：`Random`、`Language`、`LocationOwnerId`、`FarmMapAsset`。

**为什么补这 4 个**：CP 2.9.1 的令牌清单里有它们，但 research 文档没给映射（归为"待定"），而 Step 1 的覆盖度测试要求"每个 CP 令牌都不能是 `Unknown`"。二者不能同时满足时，选覆盖度测试 —— 因为这 4 个都是"取值"类令牌，`Computation` 的自述语义正是"纯计算**或取值**"，且 `PatchJudge` 对 `PureComputation` 只降级为 `NeedsReview`（**安全方向，绝不会产出 `Converted`**）。

**代价**：分类语义偏粗（`Language` 严格说是环境相关而非计算）。若日后要精确，可另开一个 `ValueLookup` 类别，但那不影响正确性 —— 这三类都走 `NeedsReview`。

`Classify` 改为（**顺序要紧**：Approximate 与 NoEquivalent 都要在 Translatable 之前判断，因为它们互不重叠但顺序能防手误）：

```csharp
    public static TokenKind Classify(string key)
    {
        string name = key.Trim();
        if (name.Length == 0) return TokenKind.Unknown;

        // 自定义 mod 令牌形如 `作者.Mod名/令牌名`
        if (name.Contains('/') && name.Contains('.')) return TokenKind.CustomModToken;

        if (NoEquivalent.Contains(name)) return TokenKind.NoGsqEquivalent;
        if (Approximate.Contains(name)) return TokenKind.GsqApproximate;
        if (Translatable.Contains(name)) return TokenKind.GsqTranslatable;
        if (Environment.Contains(name)) return TokenKind.EnvironmentDependent;
        if (Computation.Contains(name)) return TokenKind.PureComputation;
        return TokenKind.Unknown;
    }
```

- [ ] **Step 5: 重写 ConditionVocabularyTests**

把原来的理论用例按新分类改写。**要点**：原来放在「可翻译」里的 8 个令牌（`DailyLuck`/`HasActiveQuest`/`PlayerName`/`ChildNames`/`ChildGenders`/`HavingChild`/`Pregnant`/`HasWalletItem`）要移到「无等价物」，`RandomNumber` 从所有集合里删掉。

新增三个理论：

```csharp
    [Theory]
    [InlineData("DailyLuck")]
    [InlineData("HasActiveQuest")]
    [InlineData("PlayerName")]
    [InlineData("ChildNames")]
    [InlineData("ChildGenders")]
    [InlineData("HavingChild")]
    [InlineData("Pregnant")]
    [InlineData("HasWalletItem")]
    public void 无GSQ等价物的键(string key)
    {
        Assert.Equal(TokenKind.NoGsqEquivalent, ConditionVocabulary.Classify(key));
    }

    [Theory]
    [InlineData("DayEvent")]
    [InlineData("IsMainPlayer")]
    [InlineData("IsOutdoors")]
    public void 只能近似的键(string key)
    {
        Assert.Equal(TokenKind.GsqApproximate, ConditionVocabulary.Classify(key));
    }

    [Fact]
    public void 拼写错误的名字不是可翻译()
    {
        Assert.Equal(TokenKind.Unknown, ConditionVocabulary.Classify("IsJoMartComplete"));
        Assert.Equal(TokenKind.GsqTranslatable, ConditionVocabulary.Classify("IsJojaMartComplete"));
    }
```

- [ ] **Step 6: 运行确认通过**

```bash
cd /d/SdvCpTool && dotnet test --filter ConditionVocabulary
```

预期：全绿。

- [ ] **Step 7: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "订正条件分类表：区分可翻译/近似/无等价物"
```

---

## Task 3: `PatchJudge` 适配新类别

**Files:**
- Modify: `src/SdvCpTool.Core/Judging/PatchJudge.cs`
- Test: `tests/SdvCpTool.Core.Tests/PatchJudgeTests.cs`

- [ ] **Step 1: 追加失败的测试**

```csharp
    [Fact]
    public void 无GSQ等价物的条件降级为NonTranslatableCondition()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "DailyLuck": "0.02" } }
        """);

        Assert.Equal(Verdict.NonTranslatableCondition, v.Verdict);
    }

    [Fact]
    public void 只能近似的条件降级为NeedsReview()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "DayEvent": "Egg Festival" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }
```

- [ ] **Step 2: 运行确认失败**

```bash
cd /d/SdvCpTool && dotnet test --filter PatchJudgeTests
```

预期：两个新测试失败（现在 `DailyLuck` 走的是 Translatable 路径 → `Converted`；`DayEvent` 同理）。

- [ ] **Step 3: 改 PatchJudge**

在 `Judge` 里，原有的"依赖 mod 运行环境"循环里加上无等价物：

```csharp
        foreach (WhenNode key in keys)
        {
            TokenKind kind = ConditionVocabulary.Classify(key.KeyName ?? "");
            if (kind is TokenKind.EnvironmentDependent or TokenKind.CustomModToken)
            {
                return Fail(result, Verdict.NonTranslatableCondition,
                    $"条件 `{WhenParser.Render(key)}` 依赖 mod 运行环境，原版无法表达");
            }
            if (kind == TokenKind.NoGsqEquivalent)
            {
                return Fail(result, Verdict.NonTranslatableCondition,
                    $"条件 `{WhenParser.Render(key)}` 在 GSQ 里没有等价关键字（GSQ 无法返回集合或遍历实体）");
            }
        }
```

`blocker` 的 switch 里补上近似类：

```csharp
                TokenKind.GsqApproximate =>
                    $"条件 `{WhenParser.Render(key)}` 只能近似翻译成 GSQ，语义可能有偏差",
```

- [ ] **Step 4: 运行确认通过**

```bash
cd /d/SdvCpTool && dotnet test
```

预期：全绿。

- [ ] **Step 5: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "判定表适配新的条件分类"
```

---

## Task 4: 翻译器骨架与组合子

**Files:**
- Create: `src/SdvCpTool.Core/Conditions/GsqTranslator.cs`
- Test: `tests/SdvCpTool.Core.Tests/GsqTranslatorTests.cs`

- [ ] **Step 1: 写失败的测试**

创建 `tests/SdvCpTool.Core.Tests/GsqTranslatorTests.cs`：

```csharp
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

public class GsqTranslatorTests
{
    private static GsqTranslation T(string whenJson) =>
        GsqTranslator.Translate(WhenParser.Parse(JObject.Parse(whenJson)));

    [Fact]
    public void 无条件返回空串()
    {
        var r = GsqTranslator.Translate(null);
        Assert.True(r.Success);
        Assert.Equal("", r.Gsq);
    }

    [Fact]
    public void 单键()
    {
        Assert.Equal("SEASON spring", T("""{"Season": "spring"}""").Gsq);
    }

    [Fact]
    public void 同对象多键是与()
    {
        var r = T("""{"Season": "spring", "Year": "1"}""");
        Assert.Equal("SEASON spring, YEAR 1 1", r.Gsq);
    }

    [Fact]
    public void AND数组也是与()
    {
        var r = T("""{"AND": [{"Season": "spring"}, {"Year": "1"}]}""");
        Assert.Equal("SEASON spring, YEAR 1 1", r.Gsq);
    }

    [Fact]
    public void OR数组用ANY()
    {
        var r = T("""{"OR": [{"Season": "spring"}, {"Season": "fall"}]}""");
        Assert.True(r.Success);
        Assert.Equal("""ANY "SEASON spring" "SEASON fall" """.Trim(), r.Gsq);
    }

    [Fact]
    public void NOT加感叹号()
    {
        var r = T("""{"NOT": {"Season": "winter"}}""");
        Assert.Equal("!SEASON winter", r.Gsq);
    }

    [Fact]
    public void 值的逗号是或()
    {
        // 关键规则：不能写成 `SEASON spring, SEASON summer`（那是"与"，恒假）
        Assert.Equal("SEASON spring summer", T("""{"Season": "spring, summer"}""").Gsq);
    }

    [Fact]
    public void 嵌套令牌的值翻译不了()
    {
        var r = T("""{"Season": "{{Day}}"}""");
        Assert.False(r.Success);
        Assert.Contains("令牌", r.FailureReason);
    }

    [Fact]
    public void 认不出的键翻译不了()
    {
        var r = T("""{"NoSuchToken": "x"}""");
        Assert.False(r.Success);
    }

    [Fact]
    public void OR里出现多键对象时翻译不了()
    {
        // ANY 只能包单个 query，包不住"与"
        var r = T("""{"OR": [{"Season": "spring", "Year": "1"}, {"Season": "fall"}]}""");
        Assert.False(r.Success);
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
cd /d/SdvCpTool && dotnet test --filter GsqTranslatorTests
```

预期：编译失败，`GsqTranslator` 不存在。

- [ ] **Step 3: 实现骨架**

创建 `src/SdvCpTool.Core/Conditions/GsqTranslator.cs`：

```csharp
namespace SdvCpTool.Core.Conditions;

/// <summary>翻译结果。失败时带可读原因，不抛异常。</summary>
public sealed record GsqTranslation
{
    public bool Success { get; init; }
    public string? Gsq { get; init; }
    public string? FailureReason { get; init; }

    public static GsqTranslation Ok(string gsq) => new() { Success = true, Gsq = gsq };
    public static GsqTranslation Fail(string reason) => new() { Success = false, FailureReason = reason };
}

/// <summary>
/// 把 CP 的 <c>When</c> 条件树翻译成 GameStateQuery 字符串。
/// 权威依据：docs/research/2026-10-05-cp-to-gsq-token-mapping.md
/// </summary>
public static class GsqTranslator
{
    public static GsqTranslation Translate(WhenNode? when)
    {
        if (when == null) return GsqTranslation.Ok("");
        return Render(when);
    }

    private static GsqTranslation Render(WhenNode node) => node.Kind switch
    {
        WhenNodeKind.All => RenderAll(node),
        WhenNodeKind.Any => RenderAny(node),
        WhenNodeKind.Not => RenderNot(node),
        WhenNodeKind.Key => TokenTranslator.Translate(node),
        _ => GsqTranslation.Fail($"未知的节点类型 {node.Kind}")
    };

    /// <summary>`{"a":..,"b":..}` 与 `AND` 都是与 —— GSQ 用顶层逗号连接。</summary>
    private static GsqTranslation RenderAll(WhenNode node)
    {
        var parts = new List<string>();
        foreach (WhenNode child in node.Children)
        {
            GsqTranslation r = Render(child);
            if (!r.Success) return r;
            if (!string.IsNullOrEmpty(r.Gsq)) parts.Add(r.Gsq!);
        }
        return GsqTranslation.Ok(string.Join(", ", parts));
    }

    /// <summary>`OR` → `ANY "q1" "q2"`。ANY 只能包单个 query，所以子节点必须各是一个 query。</summary>
    private static GsqTranslation RenderAny(WhenNode node)
    {
        var parts = new List<string>();
        foreach (WhenNode child in node.Children)
        {
            GsqTranslation r = Render(child);
            if (!r.Success) return r;
            if (string.IsNullOrEmpty(r.Gsq)) continue;
            if (r.Gsq!.Contains(", "))
            {
                return GsqTranslation.Fail(
                    "OR 分支里出现了多条件（GSQ 的 ANY 只能包单个 query，包不住\"与\"）");
            }
            parts.Add(r.Gsq!);
        }
        if (parts.Count == 0) return GsqTranslation.Fail("OR 没有任何分支");
        if (parts.Count == 1) return GsqTranslation.Ok(parts[0]);
        return GsqTranslation.Ok("ANY " + string.Join(" ", parts.Select(p => $"\"{p}\"")));
    }

    private static GsqTranslation RenderNot(WhenNode node)
    {
        WhenNode child = node.Children[0];
        if (child.Kind != WhenNodeKind.Key)
        {
            return GsqTranslation.Fail("NOT 只能作用于单个条件，不能包住多条件（GSQ 的 `!` 不接受组合）");
        }
        GsqTranslation r = Render(child);
        return r.Success ? GsqTranslation.Ok("!" + r.Gsq) : r;
    }
}
```

- [ ] **Step 4: 加一个临时的 TokenTranslator 让骨架能编译**

创建 `src/SdvCpTool.Core/Conditions/TokenTranslator.cs`（Task 5 会填满）：

```csharp
namespace SdvCpTool.Core.Conditions;

/// <summary>单个条件令牌 → GSQ 片段。逐令牌规则见 research 文档的对照表。</summary>
internal static class TokenTranslator
{
    public static GsqTranslation Translate(WhenNode key)
    {
        return GsqTranslation.Fail($"尚未实现：{key.KeyName}");
    }
}
```

- [ ] **Step 5: 运行**

```bash
cd /d/SdvCpTool && dotnet test --filter GsqTranslatorTests
```

预期：**部分通过，这是刻意的中间状态**。准确地说：

**通过 3 条**（不依赖令牌翻译）：
- `无条件返回空串`
- `认不出的键翻译不了`（只断言 `Success == false`，桩恰好也失败）
- `OR里出现多键对象时翻译不了`（只断言失败，多键分支先经桩失败）

**失败 7 条**（全部因为 `TokenTranslator` 是桩，`Gsq` 为 null）：
`单键`、`同对象多键是与`、`AND数组也是与`、`OR数组用ANY`、`NOT加感叹号`、`值的逗号是或`、`嵌套令牌的值翻译不了`

> 注意最后一条：它断言 `FailureReason` 含"令牌"二字，而桩返回的是"尚未实现：Season"。Task 5 给 `TokenTranslator` 补上 `{{` 检查后它才会转绿。

**因此本任务提交后 `dotnet test` 是全红的（7 个失败）—— 这是有意的**，Task 5 会收口。不要为了让它变绿而提前实现 Task 5。

- [ ] **Step 6: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "GSQ 翻译器骨架：组合子与失败传播"
```

---

## Task 5: 逐令牌翻译

**Files:**
- Modify: `src/SdvCpTool.Core/Conditions/TokenTranslator.cs`
- Test: `tests/SdvCpTool.Core.Tests/GsqTranslatorTests.cs`

**实现前必读**：`docs/research/2026-10-05-cp-to-gsq-token-mapping.md` §一 的三条规则与 §二 的对照表。

**关于 `HasProfession` 的两层语义**（别混淆）：
- `ConditionVocabulary` 把它归为 `GsqTranslatable` —— 这是对的，**GSQ 确实有 `PLAYER_HAS_PROFESSION` 这个关键字**。
- 但 `TokenTranslator` 对它**一律失败** —— 因为 CP 用职业**名字**、GSQ 要**整数 ID**，而完整映射表没调研出来（只知 3 处改名陷阱 + `Blacksmith`=20）。

这两层不矛盾：分类表回答"有没有等价物"，翻译器回答"现在能不能产出"。实现时**不要为了"一致"而把 `HasProfession` 从 `Translatable` 挪走**。

- [ ] **Step 1: 写逐令牌的金标准测试**

在 `GsqTranslatorTests.cs` 追加：

```csharp
    // 规则 2：PLAYER_* 必须显式带玩家键
    [Theory]
    [InlineData("""{"HasSeenEvent": "123"}""", "PLAYER_HAS_SEEN_EVENT Current 123")]
    [InlineData("""{"HasSeenEvent: hostPlayer": "123"}""", "PLAYER_HAS_SEEN_EVENT Host 123")]
    [InlineData("""{"HasSeenEvent: anyPlayer |contains=1, 2"}""", "PLAYER_HAS_SEEN_EVENT Any 1 2")]
    [InlineData("""{"HasFlag": "flagA"}""", "PLAYER_HAS_MAIL Current flagA Any")]
    [InlineData("""{"HasReadLetter": "x"}""", "PLAYER_HAS_MAIL Current x Received")]
    [InlineData("""{"HasVisitedLocation": "Town"}""", "PLAYER_VISITED_LOCATION Current Town")]
    [InlineData("""{"PlayerGender": "Female"}""", "PLAYER_GENDER Current Female")]
    [InlineData("""{"PreferredPet": "Cat"}""", "PLAYER_PREFERRED_PET Current Cat")]
    [InlineData("""{"LocationName": "Town"}""", "PLAYER_LOCATION_NAME Current Town")]
    [InlineData("""{"LocationContext": "Island"}""", "PLAYER_LOCATION_CONTEXT Current Island")]
    public void 玩家类令牌(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    // 规则 3：数值型是闭区间，单值须补第二个参数
    [Theory]
    [InlineData("""{"Year": "1"}""", "YEAR 1 1")]
    [InlineData("""{"DaysPlayed": "28"}""", "DAYS_PLAYED 28 28")]
    [InlineData("""{"FarmhouseUpgrade": "2"}""", "PLAYER_FARMHOUSE_UPGRADE Current 2 2")]
    [InlineData("""{"Time": "600 1200"}""", "TIME 600 1200")]
    public void 数值型令牌(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    // 无玩家键的关键字
    [Theory]
    [InlineData("""{"Season": "spring"}""", "SEASON spring")]
    [InlineData("""{"Day": "1, 2"}""", "DAY_OF_MONTH 1 2")]
    [InlineData("""{"DayOfWeek": "Friday, Saturday"}""", "DAY_OF_WEEK Friday Saturday")]
    [InlineData("""{"FarmCave": "Bats"}""", "FARM_CAVE Bats")]
    [InlineData("""{"FarmType": "Beach"}""", "FARM_TYPE Beach")]
    [InlineData("""{"IsCommunityCenterComplete": "true"}""", "IS_COMMUNITY_CENTER_COMPLETE")]
    [InlineData("""{"IsJojaMartComplete": "true"}""", "IS_JOJA_MART_COMPLETE")]
    public void 无玩家键的令牌(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    // 带地点参数的
    [Fact]
    public void 天气要插地点参数()
    {
        Assert.Equal("WEATHER Here Rain Storm", T("""{"Weather": "Rain, Storm"}""").Gsq);
    }

    // 单值整串
    [Fact]
    public void 农场名是单个整串()
    {
        Assert.Equal("""FARM_NAME "My Farm" """.Trim(), T("""{"FarmName": "My Farm"}""").Gsq);
    }

    // NPC 名在键侧
    [Fact]
    public void 好感度()
    {
        Assert.Equal("PLAYER_HEARTS Current Abigail 10 10",
            T("""{"Hearts:Abigail": "10"}""").Gsq);
    }

    [Fact]
    public void 关系()
    {
        Assert.Equal("PLAYER_NPC_RELATIONSHIP Current June Married Roommate",
            T("""{"Relationship:June |contains=Married"}""").Gsq);
    }

    [Fact]
    public void 技能等级决定关键字()
    {
        Assert.Equal("PLAYER_COMBAT_LEVEL Current 5 5",
            T("""{"SkillLevel:Combat": "5"}""").Gsq);
    }

    // 无等价物的令牌
    [Theory]
    [InlineData("""{"DailyLuck": "0.02"}""")]
    [InlineData("""{"PlayerName": "Alex"}""")]
    [InlineData("""{"ChildNames": "x"}""")]
    public void 无等价物的令牌翻译失败(string when)
    {
        Assert.False(T(when).Success);
    }
```

- [ ] **Step 2: 运行，看失败清单**

```bash
cd /d/SdvCpTool && dotnet test --filter GsqTranslatorTests
```

预期：大部分失败（`TokenTranslator` 还是桩）。

- [ ] **Step 3: 实现 TokenTranslator**

创建/替换 `src/SdvCpTool.Core/Conditions/TokenTranslator.cs`：

```csharp
namespace SdvCpTool.Core.Conditions;

/// <summary>单个条件令牌 → GSQ 片段。</summary>
/// <remarks>
/// 三条必须遵守的规则（见 research 文档 §一）：
/// ① CP 值侧逗号是"或" → GSQ 同关键字多实参；绝不写成顶层逗号（那是"与"）
/// ② PLAYER_* 的玩家键是必填位置参数
/// ③ 数值型关键字是闭区间，单值须补第二个 max
/// </remarks>
internal static class TokenTranslator
{
    public static GsqTranslation Translate(WhenNode key)
    {
        string name = key.KeyName ?? "";
        string? args = key.Arguments;
        string? rawValue = key.Value;

        if (string.IsNullOrWhiteSpace(rawValue))
            return GsqTranslation.Fail($"条件 `{name}` 没有值");

        if (rawValue.Contains("{{"))
            return GsqTranslation.Fail($"条件 `{name}` 的值里嵌套了令牌，无法静态翻译");

        string[] values = rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
            return GsqTranslation.Fail($"条件 `{name}` 的值为空");

        // 玩家键：CP 用 currentPlayer/hostPlayer/anyPlayer，GSQ 用 Current/Host/Any（规则 2）
        string player = MapPlayer(args, out bool playerOk);
        if (!playerOk)
            return GsqTranslation.Fail($"条件 `{name}` 的玩家参数 `{args}` 无法映射到 GSQ");

        return name switch
        {
            // —— 无玩家键，纯或列表 ——
            "Season" => Ok("SEASON", values),
            "Day" => Ok("DAY_OF_MONTH", values),
            "DayOfWeek" => Ok("DAY_OF_WEEK", values),
            "FarmCave" => Ok("FARM_CAVE", values),
            "FarmType" => Ok("FARM_TYPE", values),

            // —— 带地点参数 ——
            "Weather" => Ok("WEATHER", Prepend("Here", values)),

            // —— 布尔（无参）——
            "IsCommunityCenterComplete" => Ok("IS_COMMUNITY_CENTER_COMPLETE"),
            "IsJojaMartComplete" => Ok("IS_JOJA_MART_COMPLETE"),

            // —— 数值区间（规则 3）——
            "Year" => Interval("YEAR", values),
            "DaysPlayed" => Interval("DAYS_PLAYED", values),
            "Time" => Interval("TIME", values),
            "FarmhouseUpgrade" => Interval("PLAYER_FARMHOUSE_UPGRADE", values, player),

            // —— 玩家 + 或列表 ——
            "HasSeenEvent" => Ok("PLAYER_HAS_SEEN_EVENT", Prepend(player, values)),
            "HasCaughtFish" => Ok("PLAYER_HAS_CAUGHT_FISH", Prepend(player, values)),
            "HasConversationTopic" => Ok("PLAYER_HAS_CONVERSATION_TOPIC", Prepend(player, values)),
            "HasDialogueAnswer" => Ok("PLAYER_HAS_DIALOGUE_ANSWER", Prepend(player, values)),
            "HasVisitedLocation" => Ok("PLAYER_VISITED_LOCATION", Prepend(player, values)),
            "LocationContext" => Ok("PLAYER_LOCATION_CONTEXT", Prepend(player, values)),
            "LocationName" => Ok("PLAYER_LOCATION_NAME", Prepend(player, values)),
            "LocationUniqueName" => Ok("PLAYER_LOCATION_UNIQUE_NAME", Prepend(player, values)),
            "PlayerGender" => Ok("PLAYER_GENDER", Prepend(player, values)),
            "PreferredPet" => Ok("PLAYER_PREFERRED_PET", Prepend(player, values)),

            // —— 玩家 + 或列表 + 固定后缀 ——
            "HasFlag" => Ok("PLAYER_HAS_MAIL", Prepend(player, values).Append("Any").ToArray()),
            "HasReadLetter" => Ok("PLAYER_HAS_MAIL", Prepend(player, values).Append("Received").ToArray()),

            // —— 玩家 + 单个整串（多值须 ANY）——
            "HasCookingRecipe" => SingleString("PLAYER_HAS_COOKING_RECIPE", player, values),
            "HasCraftingRecipe" => SingleString("PLAYER_HAS_CRAFTING_RECIPE", player, values),
            "FarmName" => SingleStringNoPlayer("FARM_NAME", values),

            // —— 特殊 ——
            "Hearts" => Hearts(args, values, player),
            "Relationship" => Relationship(args, values, player),
            "Spouse" => Relationship(args, values, player),
            "Roommate" => Relationship(args, values, player),
            "SkillLevel" => SkillLevel(args, values, player),
            "HasProfession" => HasProfession(player, values),

            _ => GsqTranslation.Fail($"条件 `{name}` 没有已实现的 GSQ 翻译规则"),
        };
    }

    // ----- 辅助方法 -----

    private const string SkillCombat = "PLAYER_COMBAT_LEVEL";
    private const string SkillFarming = "PLAYER_FARMING_LEVEL";
    private const string SkillFishing = "PLAYER_FISHING_LEVEL";
    private const string SkillForaging = "PLAYER_FORAGING_LEVEL";
    private const string SkillMining = "PLAYER_MINING_LEVEL";
    private const string SkillLuck = "PLAYER_LUCK_LEVEL";

    /// <summary>CP 技能名 → GSQ 关键字。CP 取的是含 buff 的等级，所以用**非 BASE** 版。</summary>
    private static readonly Dictionary<string, string> SkillKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Combat"] = SkillCombat,
        ["Farming"] = SkillFarming,
        ["Fishing"] = SkillFishing,
        ["Foraging"] = SkillForaging,
        ["Mining"] = SkillMining,
        ["Luck"] = SkillLuck,
    };

    /// <summary>
    /// CP 玩家键 → GSQ 玩家实参。规则 2：PLAYER_* 的玩家键是**必填位置参数**。
    /// CP 用 currentPlayer/hostPlayer/anyPlayer，GSQ 用 Current/Host/Any。
    /// </summary>
    private static string MapPlayer(string? args, out bool ok)
    {
        ok = true;
        if (string.IsNullOrWhiteSpace(args)) return "Current";

        return args.Trim().ToLowerInvariant() switch
        {
            "currentplayer" => "Current",
            "hostplayer" => "Host",
            "anyplayer" => "Any",
            _ => int.TryParse(args.Trim(), out int id) ? id.ToString() : Fail()
        };

        string Fail() { ok = false; return ""; }
    }

    private static GsqTranslation Ok(string keyword) => GsqTranslation.Ok(keyword);

    private static GsqTranslation Ok(string keyword, params string[] args) =>
        GsqTranslation.Ok(keyword + " " + string.Join(' ', args));

    private static string[] Prepend(string head, string[] rest) =>
        new[] { head }.Concat(rest).ToArray();

    /// <summary>规则 3：数值型关键字是**闭区间**，单值必须补第二个 max。</summary>
    private static GsqTranslation Interval(string keyword, string[] values) =>
        Interval(keyword, values, null);

    private static GsqTranslation Interval(string keyword, string[] values, string? player)
    {
        if (values.Length > 2)
        {
            return GsqTranslation.Fail(
                $"`{keyword}` 是区间关键字，表达不了 {values.Length} 个离散值");
        }

        string[] args = values.Length == 1
            ? new[] { values[0], values[0] }   // 单值 → min max 相同
            : values;

        return player == null
            ? Ok(keyword, args)
            : Ok(keyword, Prepend(player, args));
    }

    /// <summary>单值整串关键字（可含空格，须加引号）。多值表达不了 → 失败。</summary>
    private static GsqTranslation SingleString(string keyword, string player, string[] values)
    {
        if (values.Length > 1)
        {
            return GsqTranslation.Fail(
                $"`{keyword}` 只接受单个整串，{values.Length} 个值须改用 ANY 包装，请人工处理");
        }
        return Ok(keyword, player, $"\"{values[0]}\"");
    }

    private static GsqTranslation Hearts(string? npc, string[] values, string player)
    {
        if (string.IsNullOrWhiteSpace(npc))
            return GsqTranslation.Fail("`Hearts` 需要 NPC 名，须写在键侧（如 `\"Hearts:Abigail\"`）");

        // NPC 名是位置参数，不是键侧 —— 重排后玩家键在最前
        string[] args = values.Length == 1 ? new[] { values[0], values[0] } : values;
        if (args.Length > 2)
            return GsqTranslation.Fail("`Hearts` 是区间关键字，表达不了多个离散值");

        return Ok("PLAYER_HEARTS", Prepend(player, Prepend(npc.Trim(), args)));
    }

    private static GsqTranslation Relationship(string? npc, string[] values, string player)
    {
        if (string.IsNullOrWhiteSpace(npc))
            return GsqTranslation.Fail("关系类条件需要 NPC 名，须写在键侧（如 `\"Relationship:June\"`）");

        var types = new List<string>();
        foreach (string v in values)
        {
            switch (v.Trim().ToLowerInvariant())
            {
                case "married":
                    // research §二 陷阱②：CP 的 Married **包含**室友婚，GSQ 的 Married **排除**，
                    // 所以两个类型都要列上（GSQ 同关键字多实参 = 或）
                    types.Add("Married");
                    types.Add("Roommate");
                    break;
                case "dating":
                    types.Add("Dating");
                    break;
                case "engaged":
                    types.Add("Engaged");
                    break;
                case "divorced":
                    types.Add("Divorced");
                    break;
                case "roommate":
                    types.Add("Roommate");
                    break;
                case "unmet":
                    return GsqTranslation.Fail(
                        "`Unmet` 在 GSQ 里没有对应类型，须改写为 `!PLAYER_HAS_MET`，请人工处理");
                default:
                    return GsqTranslation.Fail($"关系类型 `{v}` 没有 GSQ 对应值");
            }
        }

        return Ok("PLAYER_NPC_RELATIONSHIP", Prepend(player, Prepend(npc.Trim(), types.ToArray())));
    }

    private static GsqTranslation SkillLevel(string? skill, string[] values, string player)
    {
        if (string.IsNullOrWhiteSpace(skill))
            return GsqTranslation.Fail("`SkillLevel` 需要技能名，须写在键侧（如 `\"SkillLevel:Combat\"`）");

        string skillName = skill.Trim();
        if (!SkillKeywords.TryGetValue(skillName, out string? keyword))
            return GsqTranslation.Fail($"技能 `{skillName}` 没有对应的 GSQ 关键字");

        string[] args = values.Length == 1 ? new[] { values[0], values[0] } : values;
        if (args.Length > 2)
            return GsqTranslation.Fail("`SkillLevel` 是区间关键字，表达不了多个离散值");

        return Ok(keyword, Prepend(player, args));
    }

    /// <summary>
    /// CP 职业名要转成游戏整数 ID 才能喂给 GSQ。
    /// research 文档只给出了 3 处改名陷阱与一个例子（Blacksmith → 20），**没有完整映射表**。
    /// 因此这里**一律失败**，宁可不可翻译也不要写错 ID。
    /// </summary>
    private static GsqTranslation HasProfession(string? player, string[] values) =>
        GsqTranslation.Fail(
            "`HasProfession` 需要 CP 职业名到游戏整数 ID 的完整映射表；调研只给出部分，" +
            "为避免写错 ID，本翻译器暂不翻译它");
}
```

- [ ] **Step 4: 运行确认通过**

```bash
cd /d/SdvCpTool && dotnet test
```

预期：全绿。若某些金标准用例与你的实现不符，**先把差异查清楚**：是实现错了、还是我写的期望值错了（对照 research 文档）。任一方有错都要修正，不要为了让测试过而改期望值。

- [ ] **Step 5: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "实现逐令牌 GSQ 翻译"
```

---

## Task 5b: 处理 `|contains=` 具名参数

**为什么必须有这一步**：实测 `|contains=` 在 Ridgeside 真实数据里出现 **636 次**。不处理它，绝大多数真实补丁翻译不了。

CP 的语义（`docs/author-guide/tokens.md` §contains 1538-1583）：

```json
"HasSeenEvent: |contains=75160120": "true"
```

这里 **ID 在 `|contains=` 里，JSON 值 `"true"` 只是占位**。等价于 `HasSeenEvent == 75160120`（对多值令牌是"包含任一"）。

真实形态有三种：

| 原文 | args 串 | 玩家键 | 值列表 |
|---|---|---|---|
| `"HasSeenEvent: \|contains=75160120"` | `\|contains=75160120` | 无 → `Current` | `75160120` |
| `"HasDialogueAnswer\|contains=751600641"` | `\|contains=751600641` | 无 → `Current` | `751600641` |
| `"HasFlag: hostPlayer \|contains=GH1letter"` | `hostPlayer \|contains=GH1letter` | `hostPlayer` → `Host` | `GH1letter` |
| `"Relationship:June \|contains=Married"` | `June \|contains=Married` | 名字键 → `Current` + NPC=`June` | `Married` |
| `"HasSeenEvent: anyPlayer \|contains=1, 2"` | `anyPlayer \|contains=1, 2` | `anyPlayer` → `Any` | `1`, `2` |

**Files:**
- Modify: `src/SdvCpTool.Core/Conditions/TokenTranslator.cs`
- Test: `tests/SdvCpTool.Core.Tests/GsqTranslatorTests.cs`

- [ ] **Step 1: 写失败的测试**

在 `GsqTranslatorTests.cs` 追加：

```csharp
    [Theory]
    [InlineData("""{"HasSeenEvent: |contains=75160120": "true"}""",
                "PLAYER_HAS_SEEN_EVENT Current 75160120")]
    [InlineData("""{"HasDialogueAnswer|contains=751600641": "true"}""",
                "PLAYER_HAS_DIALOGUE_ANSWER Current 751600641")]
    [InlineData("""{"HasFlag: hostPlayer |contains=GH1letter": "true"}""",
                "PLAYER_HAS_MAIL Host GH1letter Any")]
    [InlineData("""{"HasSeenEvent: anyPlayer |contains=1, 2": "true"}""",
                "PLAYER_HAS_SEEN_EVENT Any 1 2")]
    [InlineData("""{"Relationship:June |contains=Married": "true"}""",
                "PLAYER_NPC_RELATIONSHIP Current June Married Roommate")]
    public void 具名参数contains(string when, string expected)
    {
        Assert.Equal(expected, T(when).Gsq);
    }

    [Fact]
    public void contains时忽略JSON值()
    {
        // 值为占位，真正要匹配的是 |contains= 里的内容
        var r = T("""{"HasSeenEvent: |contains=123": "随便什么占位"}""");
        Assert.Equal("PLAYER_HAS_SEEN_EVENT Current 123", r.Gsq);
    }

    [Fact]
    public void 无contains时仍从JSON值取值()
    {
        Assert.Equal("PLAYER_HAS_SEEN_EVENT Current 123",
            T("""{"HasSeenEvent": "123"}""").Gsq);
    }
```

- [ ] **Step 2: 运行确认失败**

```bash
cd /d/SdvCpTool && dotnet test --filter GsqTranslatorTests
```

预期：新增的 7 条失败（现在 `hostPlayer |contains=GH1letter` 这类整串被当玩家键解析 → `MapPlayer` 拒绝 → 翻译失败）。

- [ ] **Step 3: 实现**

在 `TokenTranslator.Translate` 里，**解析 args 与取值之前**插入 `|contains=` 的处理：

```csharp
        // CP 的具名参数：`|contains=a, b` 表示"包含任一"，此时 JSON 值只是占位。
        // 实测出现 636 次，不处理则绝大多数真实补丁翻译不了。
        // args 形如 `hostPlayer |contains=GH1letter`，`|contains=` 之前是键槽（玩家键或名字），之后才是值列表。
        if (args != null && args.Contains("|contains=", StringComparison.OrdinalIgnoreCase))
        {
            int at = args.IndexOf("|contains=", StringComparison.OrdinalIgnoreCase);
            string head = args[..at].Trim();
            string tail = args[(at + "|contains=".Length)..].Trim();

            if (tail.Length == 0)
                return GsqTranslation.Fail($"条件 `{name}` 的 |contains= 后面没有值");

            nameSlot = head;                 // 键槽：玩家键 或 名字
            rawValue = tail;                 // 值来自 |contains=，忽略 JSON 值
        }
```

（把 `nameSlot`/`rawValue` 作为后续逻辑的输入 —— 具体接法按你现有的实现结构调整，**不要重写整个方法**。）

- [ ] **Step 4: 运行确认通过**

```bash
cd /d/SdvCpTool && dotnet test --filter GsqTranslatorTests
```
预期：全绿

```bash
cd /d/SdvCpTool && dotnet test && dotnet build
```
预期：全绿；`0 个警告 0 个错误`

- [ ] **Step 5: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "翻译器支持 |contains= 具名参数"
```

---

## Task 6: 真实样本对账

前四个任务的金标准样本都是**我手写的**。这一步用**真实 mod 数据**验证翻译器。

**前置问题（已核实）**：`GsqTranslator` 目前**没有被任何生产代码调用**（`src/` 里除自身外零引用），报告里也没有翻译结果。所以"跑 `convert` 挑样本"不可行 —— 必须先给它一个出口。

**Files:**
- Modify: `src/SdvCpTool.Cli/CommandLine.cs`（加 `translate` 子命令）
- Modify: `src/SdvCpTool.Cli/Program.cs`
- Create: `tests/SdvCpTool.Core.Tests/RealSampleTests.cs`（把真实样本固化成 CI 用例）
- Create: `docs/verification/2026-10-05-translator-real-samples.md`

- [ ] **Step 1: 给 CLI 加 `translate` 子命令**

```
SdvCpTool.Cli translate --pack <CP内容包目录> [--report <输出.json>]
```

行为：展开内容包，对**每条带 `When` 的补丁**调用 `GsqTranslator.Translate`，输出一份清单：

```json
{
  "total": 1234, "ok": 800, "failed": 434,
  "entries": [
    { "source": "Data/Events/Events.json", "when": "{\"Season\":\"spring\"}",
      "gsq": "SEASON spring", "ok": true },
    { "source": "…", "when": "…", "reason": "条件 `DailyLuck` 在 GSQ 里没有等价关键字", "ok": false }
  ]
}
```

终端打印成功/失败计数与失败原因的 top 10 分组。

**这是 M2 唯一新增的 CLI 表面**，M3 会把翻译器接进 `convert` 主流程。

- [ ] **Step 2: 跑真实数据，看分布**

```bash
cd /d/SdvCpTool
dotnet run --project src/SdvCpTool.Cli -- translate \
  --pack "D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods\Ridgeside Village 2.5.17-7286-2-5-17-1731446698\Ridgeside Village\[CP] Ridgeside Village" \
  --report "D:\sdv_translate.json"
```

- [ ] **Step 3: 分析失败原因，分类**

把所有失败按 `reason` 分组，逐组判断属于哪一类：

| 类别 | 处理 |
|---|---|
| **该失败的**（`NoGsqEquivalent`、`HasMod`、自定义令牌、嵌套 `{{`） | 正确行为，记录即可 |
| **实现缺陷**（本该能翻译却失败了） | 必须修 |
| **文档不足**（research 没覆盖的真实写法） | 补 research 文档 |

**特别留意** `HasProfession` 的失败 —— 那是刻意的（缺 ID 表），确认它属于"该失败"。

- [ ] **Step 4: 抽 20 条代表性样本固化成 CI 用例**

创建 `tests/SdvCpTool.Core.Tests/RealSampleTests.cs`。样本**必须来自真实数据**（从 Step 2 的输出里挑），覆盖：

- 全部成功的形态（每种至少一条）
- 每类"该失败"的形态（至少各一条）
- 任何在 Step 3 里判定为"实现缺陷"的（修完后作为回归护栏）

每条写成：

```csharp
    [Theory]
    [InlineData("""{"Season": "spring"}""", "SEASON spring")]
    // … 其余 19 条
    public void 真实样本(string whenJson, string expectedGsq)
    {
        Assert.Equal(expectedGsq, GsqTranslator.Translate(WhenParser.Parse(JObject.Parse(whenJson))).Gsq);
    }
```

**每条都要在注释里标明它的来源**（哪个 mod、哪个文件），这样将来出问题能回溯。

- [ ] **Step 5: 修 Step 3 发现的实现缺陷**

若发现翻译器错，改代码 + 补金标准测试；若发现 research 文档不足，补文档。

- [ ] **Step 6: 写对账记录**

创建 `docs/verification/2026-10-05-translator-real-samples.md`，内容：

- 实际命令与统计（总数 / 成功 / 失败）
- 失败按原因分组的分布表
- 每组的类别归属（该失败 / 实现缺陷 / 文档不足）
- 修了什么
- **诚实记录修不了的部分**及影响面

- [ ] **Step 7: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "翻译器真实样本对账"
```

---

## Task 7: 实跑验证（可选，需人工触发）

用游戏自己的 GSQ 解析器验证生成的字符串能被真实解析。**不放进 CI**（要加载游戏程序集）。

**Files:**
- Create: `Tests/GsqSyntaxCheck/` （独立小工具，不进主 sln 的默认构建）

- [ ] **Step 1: 建一个独立控制台工程**

```bash
cd /d/SdvCpTool
dotnet new console -n GsqSyntaxCheck -o Tests/GsqSyntaxCheck -f net8.0
```

**不加入 `SdvCpTool.sln`** —— 它要引用游戏程序集，不能进 CI。

- [ ] **Step 2: 引用游戏程序集并调用 `GameStateQuery.Parse`**

在 `GsqSyntaxCheck.csproj` 里加对 `D:\Stardew.Valley.v1.6.15\Stardew Valley\Stardew Valley.dll` 的 `Reference`（HintPath）。程序读一个 GSQ 字符串文件，逐行调 `GameStateQuery.Parse`，报告哪些解析失败。

**若依赖加载不通**（MonoGame/xTile 等），把遇到的错误如实记录下来，不为跑通而做 hack。

- [ ] **Step 3: 用翻译器的输出跑一遍**

把 Task 6 里那 20 条实际输出喂给它。

- [ ] **Step 4: 把结果并入对账文档并提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "GSQ 语法实跑验证"
```

---

## 完成标志

- [ ] `dotnet test` 全绿
- [ ] `ConditionVocabularyCoverageTests` 与 CP 令牌清单零差集
- [ ] 翻译器对 ~33 个可翻译令牌有金标准覆盖，对无等价物的令牌明确失败
- [ ] 20 条真实样本对账完成，每处不一致都有归属
- [ ] 实跑验证跑过或如实记录为何跑不通
- [ ] 本计划**不修改资源树**（合并是 M3）
