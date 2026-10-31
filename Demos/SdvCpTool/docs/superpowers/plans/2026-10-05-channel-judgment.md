# 通道判定实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把判定从「能不能翻成 GSQ」改成「有没有任何一条原生通道能表达」，并产出一份口径正确的对账报告。

**背景：** spec §5.1 已确立五条通道（A / B1 / B2 / B3 / C）。当前 `Verdict` 是**通道 A 导向**的措辞，把"通道 A 不行"误报成了"不可转换"。实测 Ridgeside 有 **226 条**因此被误判。

**本计划的范围：** 只做**判定**，不写资源树（合并是后续里程碑）。通道 B2/B3/C 的**实现**也留到后续 —— 本计划只让判定**如实说出"该走哪条通道"**。

**Tech Stack:** C# net8.0、Newtonsoft.Json、xUnit

---

## 关键设计：烘焙与语言后缀会把补丁"降级为无条件"

这两条通道有个共同性质，让判定变得简单：

| 通道 | 做什么 | 结果 |
|---|---|---|
| **C 烘焙** | 读 `Mods/` 与包内文件，把条件求值成真/假 | 真 → 变成**无条件**补丁；假 → **整个补丁不适用**（跳过，不是"阻塞"） |
| **B1 语言后缀** | 内容只写进 `<name>.<长码>.xnb` | 在**该语言下**变成无条件的 |

所以走这两条通道的补丁，**不需要目标资产有条件落点** —— 比通道 A 更强。

**未提供 `Mods/` 目录时无法烘焙** → 该补丁判 `NeedsReview`，理由说明"需 `--mods` 才能烘焙"。

---

## 文件结构

```
src/SdvCpTool.Core/Conditions/
└─ TokenKind.cs                    (改) EnvironmentDependent → Bakeable + Locale
src/SdvCpTool.Core/Conditions/
└─ ConditionVocabulary.cs          (改) 相应调整
src/SdvCpTool.Core/Judging/
├─ Channel.cs                      (新) 通道枚举
├─ PatchVerdict.cs                 (改) 加 Channel 字段
└─ PatchJudge.cs                   (改) 接入通道判定
src/SdvCpTool.Cli/
├─ CommandLine.cs                  (改) 加 --mods
└─ Program.cs                      (改)
tests/SdvCpTool.Core.Tests/
├─ ConditionVocabularyTests.cs     (改)
├─ ConditionVocabularyCoverageTests.cs (改)
└─ PatchJudgeTests.cs              (改)
```

---

## Task 1: `TokenKind` 与分类表改造

**Files:**
- Modify: `src/SdvCpTool.Core/Conditions/TokenKind.cs`
- Modify: `src/SdvCpTool.Core/Conditions/ConditionVocabulary.cs`
- Test: `tests/SdvCpTool.Core.Tests/ConditionVocabularyTests.cs`
- Test: `tests/SdvCpTool.Core.Tests/ConditionVocabularyCoverageTests.cs`

- [ ] **Step 1: 改 TokenKind**

把 `EnvironmentDependent` 拆成两个值。修改 `TokenKind.cs`：

```csharp
namespace SdvCpTool.Core.Conditions;

public enum TokenKind
{
    /// <summary>有 GSQ 等价关键字，可机械翻译。</summary>
    GsqTranslatable,

    /// <summary>能生成 GSQ，但语义与 CP 有偏差，须人工确认。</summary>
    GsqApproximate,

    /// <summary>GSQ 原理上表达不了（需要返回集合或遍历实体）。</summary>
    NoGsqEquivalent,

    /// <summary>依赖已装模组或包内文件 —— 读对应目录即可离线求值（通道 C 烘焙）。</summary>
    Bakeable,

    /// <summary>当前游戏语言 —— 把内容写进对应语言后缀的资产即可表达（通道 B1）。</summary>
    Locale,

    /// <summary>纯计算或取值，字面量参数可离线求值。</summary>
    PureComputation,

    /// <summary>CP 内容包之外的 mod 通过 C# 注册的令牌。</summary>
    CustomModToken,

    /// <summary>不认识的键名。</summary>
    Unknown
}
```

- [ ] **Step 2: 改分类表**

`ConditionVocabulary.cs` 里把 `Environment` 集合改名拆开：

```csharp
    // 可烘焙：读 Mods/*/manifest.json 与包内文件即可离线求值（通道 C）
    // 依据：CP 把它们注册成 ImmutableValueProvider（TokenManager.cs:268,286），
    // 对给定的 mod 集与内容包，值在一次会话内不可变 —— 所以固化在语义上正确。
    private static readonly HashSet<string> Bakeable = new(StringComparer.OrdinalIgnoreCase)
    {
        "HasMod", "ModId", "HasFile", "FirstValidFile"
    };

    // 语言：写进对应语言后缀的资产即可（通道 B1）
    private static readonly HashSet<string> Locale = new(StringComparer.OrdinalIgnoreCase)
    {
        "I18n"
    };
```

`Classify` 的分支相应替换：

```csharp
        if (Bakeable.Contains(name)) return TokenKind.Bakeable;
        if (Locale.Contains(name)) return TokenKind.Locale;
```

（放在 `Translatable` 之后、`Computation` 之前，与原 `Environment` 的位置一致。）

- [ ] **Step 3: 改测试**

`ConditionVocabularyTests.cs`：把原 `依赖mod环境的键` 那个 Theory 拆成两个：

```csharp
    [Theory]
    [InlineData("HasMod")]
    [InlineData("ModId")]
    [InlineData("HasFile")]
    [InlineData("FirstValidFile")]
    public void 可烘焙的键(string key)
    {
        Assert.Equal(TokenKind.Bakeable, ConditionVocabulary.Classify(key));
    }

    [Theory]
    [InlineData("I18n")]
    public void 语言维度的键(string key)
    {
        Assert.Equal(TokenKind.Locale, ConditionVocabulary.Classify(key));
    }
```

`ConditionVocabularyCoverageTests.cs`：`EnvironmentDependent` 的引用要跟着改（若有）。

- [ ] **Step 4: 运行**

```bash
cd /d/SdvCpTool && dotnet test --filter ConditionVocabulary
```
预期：`ConditionVocabularyTests` **61 条**（原 60 + 1：`依赖mod环境的键` 的 4 个用例拆成 `可烘焙的键` 4 个 + `语言维度的键` 1 个）

```bash
cd /d/SdvCpTool && dotnet test && dotnet build
```
预期：全绿；`0 个警告 0 个错误`

> ⚠️ `PatchJudge` 引用了 `TokenKind.EnvironmentDependent`，Step 1 之后它会编译不过。**Task 2 会修它** —— 若你想让每个提交都能编译，就把 Task 1 与 Task 2 的改动合成一个提交。

- [ ] **Step 5: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "条件分类：EnvironmentDependent 拆为 Bakeable 与 Locale"
```

---

## Task 2: 通道判定

**Files:**
- Create: `src/SdvCpTool.Core/Judging/Channel.cs`
- Modify: `src/SdvCpTool.Core/Judging/PatchVerdict.cs`
- Modify: `src/SdvCpTool.Core/Judging/PatchJudge.cs`
- Modify: `src/SdvCpTool.Core/Conditions/TokenTranslator.cs`
- Test: `tests/SdvCpTool.Core.Tests/PatchJudgeTests.cs`
- Test: `tests/SdvCpTool.Core.Tests/RealSampleTests.cs`

### Task 1 留下的两处连带影响（Task 1 的实施者报告，已核实）

1. **`PatchJudgeTests.含HasMod时是NonTranslatableCondition` 必然会失败**。`HasMod` 现在是 `Bakeable`，不再判 `NonTranslatableCondition`。把它改成：

```csharp
    [Fact]
    public void 含HasMod时走烘焙通道()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "HasMod": "|contains=Foo.Bar" } }
        """);

        Assert.Equal(Channel.Bake, v.Channel);
        Assert.NotEqual(Verdict.NonTranslatableCondition, v.Verdict);
    }
```

2. **`TokenTranslator` 里 `Bakeable` 分支的文案过时了**。现在它逐字保留着 "条件 `X` 依赖 mod 环境，GSQ 没有对应关键字" —— 但 `Bakeable` 令牌不是"翻不了"，而是"**该走烘焙通道**"。同期改 `RealSampleTests.cs:74` 的期望文案（它断言 `HasMod` 的原因含"依赖 mod 环境"）。

把 `TokenTranslator` 的该分支文案改成：

```csharp
            case TokenKind.Bakeable:
                return GsqTranslation.Fail(
                    $"条件 `{name}` 依赖已装模组或包内文件，应走离线烘焙通道（通道 C），GSQ 翻译器不处理");
```

`RealSampleTests` 里那条的期望改为 `"应走离线烘焙通道"`。**注意**：那条样本本身仍然"翻译失败"是对的 —— `GsqTranslator` 只做通道 A，烘焙是别的模块的事。改的只是**理由文案**。

- [ ] **Step 1: 建 Channel 枚举**

创建 `src/SdvCpTool.Core/Judging/Channel.cs`：

```csharp
namespace SdvCpTool.Core.Judging;

/// <summary>把（条件, 改动）组合落到原生形式的通道。</summary>
public enum Channel
{
    /// <summary>没有可用通道。</summary>
    None,

    /// <summary>GSQ 条件 + 数据资产的条件落点。</summary>
    Gsq,

    /// <summary>语言后缀资产（&lt;name&gt;.&lt;长码&gt;.xnb）。</summary>
    LocaleSuffix,

    /// <summary>季节 tilesheet 命名（spring_ 等）。</summary>
    SeasonalTilesheet,

    /// <summary>昼夜瓦片（DayTiles / NightTiles）。</summary>
    DayNightTiles,

    /// <summary>读 Mods/ 与包内文件离线烘焙。</summary>
    Bake
}
```

- [ ] **Step 2: PatchVerdict 加字段**

`PatchVerdict.cs` 增加：

```csharp
    /// <summary>走哪条通道达成转换。未转换时为 <see cref="Channel.None"/>。</summary>
    public Channel Channel = Channel.None;
```

- [ ] **Step 3: 写失败的测试**

在 `PatchJudgeTests.cs` 追加：

```csharp
    [Fact]
    public void 可烘焙的条件走通道C()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "HasFile": "config.json" } }
        """);

        // 未提供 mods/pack 上下文时无法求值，但通道已确定
        Assert.NotEqual(Verdict.NonTranslatableCondition, v.Verdict);
        Assert.Equal(Channel.Bake, v.Channel);
    }

    [Fact]
    public void 语言条件走通道B1()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "I18n": "zh" } }
        """);

        Assert.Equal(Channel.LocaleSuffix, v.Channel);
        Assert.Equal(Verdict.NeedsReview, v.Verdict);   // 内容要写进 .zh-CN 资产，需人工确认
    }

    [Fact]
    public void GSQ通道的记录通道为Gsq()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Channel.Gsq, v.Channel);
    }

    [Fact]
    public void 无条件补丁的通道是None()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""{ "Action": "EditData", "Target": "Data/Locations" }""");

        Assert.Equal(Channel.None, v.Channel);
    }
```

- [ ] **Step 4: 运行确认失败**

```bash
cd /d/SdvCpTool && dotnet test --filter PatchJudgeTests
```

预期：编译失败（`Channel` 不存在 / 编译不过的 `EnvironmentDependent`）。

- [ ] **Step 5: 改 PatchJudge**

在 `Judge` 里，把原来的 `EnvironmentDependent or CustomModToken` 分支改为按新类别分流：

```csharp
        foreach (WhenNode key in keys)
        {
            TokenKind kind = ConditionVocabulary.Classify(key.KeyName ?? "");
            if (kind == TokenKind.CustomModToken)
            {
                return Fail(result, Verdict.NonTranslatableCondition,
                    $"条件 `{WhenParser.Render(key)}` 是 mod 用 C# 注册的令牌，值在另一个 mod 的代码里算，无法离线求值");
            }
            if (kind == TokenKind.NoGsqEquivalent)
            {
                return Fail(result, Verdict.NonTranslatableCondition,
                    $"条件 `{WhenParser.Render(key)}` 在 GSQ 里没有等价关键字，也没有对应的名字维度（GSQ 无法返回集合或遍历实体）");
            }
        }

        // 通道 C：可烘焙。读 Mods/ 与包内文件求值成真/假 —— 真则降级为无条件，假则整个补丁不适用。
        // 本计划只判定通道，不实现求值；因此未提供 --mods 时判 NeedsReview。
        if (keys.Any(k => ConditionVocabulary.Classify(k.KeyName ?? "") == TokenKind.Bakeable))
        {
            result.Channel = Channel.Bake;
            return Ok(result, Verdict.NeedsReview,
                "条件可由已装模组/包内文件离线烘焙（通道 C）；需提供 --mods 才能求值");
        }

        // 通道 B1：语言。内容写进对应语言后缀的资产即可表达。
        if (keys.Any(k => ConditionVocabulary.Classify(k.KeyName ?? "") == TokenKind.Locale))
        {
            result.Channel = Channel.LocaleSuffix;
            return Ok(result, Verdict.NeedsReview,
                "条件是游戏语言，可写进对应语言后缀的资产（通道 B1），需人工确认要产出哪些语言的资产");
        }
```

再在**落点探测成功的那两处**（`LandingKind.EntryConditionField` / `EntryKeyConditions` 且 `blocker == null`）设置 `result.Channel = Channel.Gsq;`。

- [ ] **Step 6: 运行确认通过**

```bash
cd /d/SdvCpTool && dotnet test
```
预期：全绿

```bash
cd /d/SdvCpTool && dotnet build
```
预期：`0 个警告 0 个错误`

- [ ] **Step 7: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "判定接入通道模型"
```

---

## Task 3: 重跑对账

**Files:**
- Modify: `docs/verification/2026-10-05-ridgeside-reconciliation.md`
- Modify: `src/SdvCpTool.Cli/CommandLine.cs` 与 `Program.cs`（把 `Channel` 写进报告）

- [ ] **Step 1: 报告里带上通道**

`ReportWriter.WriteJson` 的每条 patch 增加 `"channel": pv.Channel.ToString()`。终端摘要里按通道统计一次。

- [ ] **Step 2: 重跑**

```bash
cd /d/SdvCpTool && mkdir -p /d/sdv_out5
dotnet run --project src/SdvCpTool.Cli -- convert \
  --pack "D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods\Ridgeside Village 2.5.17-7286-2-5-17-1731446698\Ridgeside Village\[CP] Ridgeside Village" \
  --tree "D:\sdv_tree" \
  --content-hashes "D:\Stardew.Valley.v1.6.15\Stardew Valley\Content\ContentHashes.json" \
  --out "D:\sdv_out5" --dry-run
```

- [ ] **Step 3: 把结果追加到对账记录**

在 `docs/verification/2026-10-05-ridgeside-reconciliation.md` 追加一节，含：

- 改前 vs 改后的分类对比表
- 新增的通道分布
- **诚实说明**：走通道 B1/C 的补丁目前只是"判定为可转换"，**本工具尚未实现该通道的产出**，所以暂时不能真正写出来

- [ ] **Step 4: 提交**

```bash
cd /d/SdvCpTool
git add -A
git commit -m "按通道模型重跑对账"
```

---

## 完成标志

- [ ] `dotnet test` 全绿，`dotnet build` 零警告
- [ ] `Verdict` 不再把"通道 A 不行"报成"不可转换"
- [ ] 每条已转换的补丁都记录了 `Channel`
- [ ] 对账记录里明确区分「**判定为可转换**」与「**本工具已能产出**」—— 不许把前者说成后者
