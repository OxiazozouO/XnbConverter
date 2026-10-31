# 全语料 8 条「写入失败」逐条定性

日期：2026-10-09

> **订正（2026-10-11 晚按当前产出重数）**：现在是 **9 条**。逐条对过 —— **下面这 8 条一条不少、
> 定性不变**，第 9 条是后来才进视野的：**鸟类观察协会**（`[CP]`）的
> `Characters/Dialogue/OrinothlogistsGuild_…`，`writeNote` = 「对话值内联 GSQ 失败：条目键
> `Custom_Ivy_OrnithologistsGuild_Mon` … 新值或旧值含 `|`，会撑破 `$query` 的分支切分」——
> 与第 1 条**同一类**（`$query` 内联不了）。
> 数法：`report.json` 里 `patches[].written == false`（「写过了但失败」，与 `written = null`
> 「压根没尝试」是两回事；`AnalysisReport.WriteFailedCount` 就是这么算的）。
> ⚠ 别拿「`writeNote` 含『失败』」去数 —— 那会把**三套发射器**（四季/图集/立绘）的「发射失败」
> 也算进来，当前全语料那样数是 **26** 条；它们记在 `normalization.rejectedPatches`，
> 不是 `writeFailed`（两处别混）。

**为什么要逐条看**：`writeFailed` 是报告里最容易被当成「内容丢了」的数字。
8 条听上去像 8 处损失，实际逐条查下来**没有一条是能修的内容丢失** ——
3 条是**假失败**（产出本就正确），4 条是**能力边界**，1 条是**CP 自己也做不了**。

## 分类

| # | 包 | 来源 | 目标 | 定性 |
|---|---|---|---|---|
| 1 | 礼物对话 | `GiftDialogue/Haley.json` | `Characters/Dialogue/Haley` | 能力边界：mod 自带原生 `$query` |
| 2 | 身临其境 谢恩 | `content.json`（EditMap） | `Maps/spouseRooms` | 能力边界：`FromFile` 含自定义 token |
| 3 | 聚宝盆 制作 | `machines-extendedcrops/extruder.json` | `Data/Machines` | 落点模型缺口（`includes` 数组） |
| 4 | 聚宝盆 制作 | `machines-extendedcrops/preservesjar.json` | `Data/Machines` | 同上 |
| 5 | SVE | `code/npcs/Claire.json` | `Characters/Dialogue/Claire` | **假失败**：被后继无条件写入覆盖 |
| 6 | SVE | `code/npcs/Adventurers.json` | `Characters/Dialogue/Edmund` | **假失败**：同上 |
| 7 | SVE | `code/npcs/Martin.json` | `Characters/Dialogue/Martin` | **假失败**：同上 |
| 8 | SVE 季节性服装 | `assets/Code/ExtraNPCs.json` | `Maps/Stadium` | CP 同样作用不了：目标不存在 |

## 1 — mod 自带原生 `$query`（能力边界）

「礼物对话」的 `AcceptGift_(O)221` 有两条补丁：

```json
{ "Action": "EditData", "Target": "Characters/Dialogue/Haley",
  "Entries": { "AcceptGift_(O)221": "{{i18n:…early}}" } }        // 无条件

{ "Action": "EditData", "Target": "Characters/Dialogue/Haley",
  "When": { "DaysPlayed": "161" },
  "Entries": { "AcceptGift_(O)221":
    "$query !PLAYER_HAS_COOKING_RECIPE Current \"Pink Cake\" {{i18n:…}}#$action MarkCookingRecipeKnown Current \"Pink Cake\"|{{i18n:…}}" } }
```

第二条的**值本身就是 mod 写的原生 `$query`**（作者直接在对话里内联了 GSQ）。

我们要再加一层条件就得到 `$query <我们的GSQ>#<那个 $query>|<旧值>`。
但原版 `Dialogue.parseDialogueString` 用 `Split('#', 2)` / `Split('|')` 切分支，
值里已有的 `|` 会把它撑破 ⇒ **`Compose` 拒绝**。

CP 的语义是三分支（天数为 161 且没有配方 / 天数为 161 且有配方 / 其余用旧值），
而 `$query` 只有两分支、且嵌套会被按原始串重切 ⇒ **本质上表达不了**。

⚠ 原先的失败文案说「取不到可用的原有值（缺失/空/与新值相同/本身已是 `$query`）」——
**4 项都不对**，真实原因是「新值含 `|`」。照着那句话去查旧值只会查错方向。
已改成让 `Compose` 输出**具体**缘由（见 `ComposeDetailed`）。

## 2 — `FromFile` 含自定义 token（能力边界）

```
EditMap  Target=Maps/spouseRooms
FromFile=assets/Maps/ShaneSpouseRoom_{{MessyCleanToken}}.tmx
```

`{{MessyCleanToken}}` 是那个 mod 用 C# 自己注册的 token。离线烘焙没有它的求值实现，
`SpouseRoom` 的整洁/凌乱两版都在包里（`…_Messy.tmx` / `…_Clean.tmx`）——
选哪一版是运行时才知道的。如实拒绝。

## 3/4 — 落点落在数组本身（模型缺口，已记待办）

```
TargetField: ["(BC)Cornucopia_Extruder","OutputRules","Cornucopia_GlassNoodles","Triggers"]
```

`Triggers` 是**数组**，数组没有 `Condition` 可写；新增的元素又没有 `Id`（按 CP 去掉了「追加时补 Id」）
⇒ 按 `Id` 寻址不到，按 `#<下标>` 寻址要在**合并之后**才知道下标，而 `EntryPaths` 现在在合并**前**算。
详见 `docs/research/2026-10-09-include-gap-is-not-a-gap.md` 第 4.3 节。

## 5/6/7 — 假失败：条件补丁被后继的无条件写入覆盖

**这三条产出本来就对**，只是报告成了「写入失败」。

SVE 的 `content.json` 里 Include 顺序（第 79 条 / 第 82 条）：

```
79  Include  code/npcs/Claire.json                          ← 条件补丁
82  Include  Assets/CharacterFiles/Dialogue/Claire/Claire.json   ← 无条件补丁
```

`code/NPCs/Claire.json`：

```json
{ "Action": "EditData", "Target": "Characters/Dialogue/Claire",
  "When": { "HasSeenEvent |contains=191393": false },
  "Entries": { "Mon": "{{i18n:…1}}", "summer_Sun": "{{i18n:…2}}", "winter_Wed": "{{i18n:…3}}" } }
```

后面那条**无条件**补丁（141 个键）里 `Mon` / `summer_Sun` / `winter_Wed` **全都有**。

CP 是**顺序覆盖**（逐键 last-writer-wins）：

| | 条件成立 | 条件不成立 |
|---|---|---|
| 第 79 步写上 | 条件补丁的值 | （跳过，什么都不写）|
| 第 82 步写上 | **无条件值**（覆盖）| **无条件值** |
| 最终 | **无条件值** | **无条件值** |

⇒ 这条条件补丁对最终产出**没有任何影响**。

我们的处理恰好也是这个结果：`priorLookup` 取「本条合并**前**」的值，
而那时无条件补丁还没合并 ⇒ 取不到旧值 ⇒ `$query` 无法合成 ⇒ 拒绝 ⇒
条件补丁不写、无条件补丁照写 ⇒ **最终值正确**。

实测核对产出树（`Characters/Dialogue/Claire.json`，150 键）：

```
Mon        = "Let me know if I can do anything for you.$2"   ← Claire.CharacterDialogue.018
summer_Sun = "I want to find something worth working toward…" ← Claire.CharacterDialogue.053
winter_Wed = "Do you ever feel like next year will be different?$1" ← Claire.CharacterDialogue.085
```

三个键的值**都是无条件补丁的**（`Claire.CharacterDialogue.0xx`），与 CP 语义一致。

⚠ 但报告把它记成 **写入失败** ⇒ `writeFailed` 这个指标**虚高 3 条**。
顺带说明：`priorLookup` 取不到旧值这个**检测**本身是对的 ——
「条件补丁之前没有该键、之后有」正是「无法内联成 `$query`」的判据。

诊断文案改准之后，这三条现在报的是：

```
对话值内联 GSQ 失败：条目键 `Mon` 无法内联成 `$query`：取不到旧值（合并前的目标里没有这个键）
```

⚠ 但**文案改准不等于指标改对**：它现在说清了「为什么拒」，
可它仍然是**假失败**（产出没丢东西）。要把它记成「无操作」是另一件事（见下）。

**待办**：要把它报成「无操作（被后继无条件写入覆盖）」而不是失败，
得在**判定与写入两处同时**引入「后续无条件写入索引」—— 那是个模型级改动
（项目铁律：判定与写入必须同源，不能只改一处），先记着。

## 8 — 目标资产不存在（CP 同样作用不了）

`SVE 季节性服装` 想 `EditImage` 到 `Maps/Stadium` —— 那是 **SVE 本体**提供的资产。
只装季节性服装、不装 SVE 时，`Maps/Stadium` 根本不存在，**CP 自己也作用不了**
（CP 不能作用在不存在的目标上）。正确拒绝。
