# i18n 实参里的令牌漏进产出（以及它们到底能不能被原版行为定义）

日期：2026-10-07　　代码：`Task 49`

上一轮（Task 48）把「补丁**值**里带代不掉的 CP 令牌」整条拦住之后，语料里还剩 **13 处**残渣
（谢恩 8 + SVE 5），当时标的是**未确证**。这一轮查清了，根因**不是**「来自预编译 `.xnb`」——
那是我当时的猜测，**猜错了**。

---

## 1. 残渣长什么样

产出里的原文（`Data/Events/Farm.json`、`Data/Events/AnimalShop.json`）：

```
speak Jas "早上好，@{{JasAuncleToken}} ！$h"
textAboveHead Shane "Hey, {{PlayerName}}! Dinner's on me!"
question fork1 "{{PlayerName}}... Can we talk? …"
```

游戏**不认识** `{{…}}`（它只认 `@`、`%farm`、`[Token]`、`${a^b}$` —— 见第 4 节），
所以玩家看到的是字面花括号。

## 2. 根因：**两处扫描器不一致**

漏点在 `{{i18n:…}}` 的**具名参数**里。源文件（谢恩 `assets/Events.json`）：

```
"speak Jas \"{{i18n:ICS.Shane13Heart.Part1.event59443124.0 |JasAuncleToken={{JasAuncleToken}}}}\""
```

`{{i18n:…}}` 的结尾要**配对**找：`|JasAuncleToken={{JasAuncleToken}}` 里的 `}}` 是**内层**令牌的结尾。
两处实现对此不一致：

| 位置 | 扫描方式 | 结果 |
|---|---|---|
| `TranslationApplier.Replace`（写入侧） | 配对（`FindTokenEnd`，带深度计数） | **对** |
| `ValueTokenSubstitutor.HasTokenText`（判定侧） | `text.IndexOf("}}")` 找第一个 | **错** |

于是判定侧把整段看成一个「纯 i18n 令牌」放行（`name.StartsWith("i18n")` ⇒ 跳过），
而写入侧把实参值原样代进了译文的占位符。

**修法**：把配对扫描抽成唯一实现 `Cp/TokenScan.cs`，判定侧与写入侧共用
（`TranslationApplier` 里那份私有 `FindTokenEnd` 删掉）。
另外写入侧补一道：**具名参数的「值」**里带未展开令牌 ⇒ 整条不写。

> ⚠ **只查实参的值，不查代入后的整串**。译文里**没有对应实参**的 `{{unknown}}`，
> CP 自己也原样保留（SMAPI `Translation.FormatText` 认不出就留着），那一条我们要**照抄**才对 ——
> 多拦一步就成了「比 CP 还不忠实」。用例：`TranslationApplierTests.占位符大小写不敏感且认不出的原样保留`。

## 3. CP 那边到底做了什么

`ContentPatcher.Framework.Tokens.ValueProviders/TranslationValueProvider.cs:44-73`：

```csharp
object obj = input.HasNamedArgs
    ? input.NamedArgs.ToDictionary(p => p.Key, p => Stringify(p.Value))   // ← 实参**先解析令牌**
    : null;
Translation val = TranslationHelper.Get(firstPositionalArg, obj).ApplyGenderSwitchBlocks(false);
```

`Stringify` = `string.Join(", ", input.Parsed)` —— **`Parsed` 是解析后的值**。
也就是说 CP 把 `|JasAuncleToken={{JasAuncleToken}}` 里的嵌套令牌**先代成真值**再交给 SMAPI，
SMAPI 再把译文里的 `{{JasAuncleToken}}` 占位符换成它。**CP 的产物里没有花括号，我们的有** ——
这就是「与 CP 不一致」。

## 4. 这些令牌能不能被**原版行为**定义？（实地考察）

这是本轮的第二个问题：是不是所有「不可解析」其实都有原版等价物。逐个查（依据都在树的源码里）：

### 4.1 `{{PlayerName}}` → `@`（**有等价物，但只在部分通道**）

- CP 定义：`TokenManager.cs:189` `PerPlayerValueProvider(ConditionType.PlayerName, player => ((Character)player).Name)` —— **原始名字**。
- 原版：`Dialogue.ReplacePlayerEnteredStrings`（`Dialogue.cs:1097-1109`）
  `str = str.Replace("@", Utility.FilterUserName(farmer.Name))`；
  本版 `Utility.FilterUserName`（`Utility.cs:7775`）的体就是 `return name;` ⇒ **与 CP 等价**。
  （CP 自己的常量表也写着 `InternalConstants.PlayerNamePrefix = "@"`。）
- **通道约束**（这一条最容易想当然，所以实测）：
  - `speak`：`Event.DefaultCommands.Speak` → `new Dialogue(...)` → `prepareDialogueForDisplay` → `ReplacePlayerEnteredStrings` ⇒ **支持**。
  - `end`：同理走对话 ⇒ **支持**。
  - `textAboveHead`：`Event.DefaultCommands.TextAboveHead` → `NPC.showTextAboveHead`（`NPC.cs:1372-1383`）
    只调 `Dialogue.applyGenderSwitchBlocks` —— **没有** `@` 替换；画的时候走
    `SpriteText.drawStringWithScrollCenteredAt`，那里面也没有（`SpriteText.cs` 全文搜不到 `'@'`）⇒ **不支持**。
  - `message`：`Game1.drawDialogueNoTyping(Game1.parseText(...))` → `DialogueBox(string)` ⇒ **不支持**。
- 实测计数（`tools/event-text-channels.py`，原版 258 条事件、按 `/` 切命令后逐条取文本实参）：

  | 命令 | 命令数 | 文本含 `@` |
  |---|---:|---:|
  | `speak` | 1573 | **248** |
  | `end` | 111 | **12** |
  | `splitSpeak` | 4 | 1 |
  | `textAboveHead` | 153 | 0 |
  | `message` | 124 | 0 |
  | `question` | 46 | 0 |
  | `quickQuestion` | 30 | 0 |
  | `spriteText` | 3 | 0 |

  ⇒ `@` 是**对话类通道**的原版写法；非对话通道原版作者一次都没这么写。
  （**这张表我改过两版**：第一版把 `@` 记到了 `quickQuestion` 头上 —— 扫描器没先切
  `(break)` 就开始找引号，于是 `(break)` 之后那些内嵌的 `\speak Sam "…@…"` 被算进了
  `quickQuestion`。修好之后 0/30，与代码一致。**先怀疑自己的量具**。）

### 4.2 `{{FarmName}}` → `%farm` 或 `[FarmName]`

- CP 定义：`TokenSaveReader.cs:477` `GetFarmName() => GetCurrentPlayer()?.farmName.Value`。
- 原版两条路，值都等于 `farmer.farmName.Value`：
  - `%farm`（`Dialogue.cs:1125`）—— 只在对话通道。
  - `[FarmName]`（`TokenParser.cs` 的 `FarmName` 解析器：`replacement = player.farmName.Value`）——
    **不受对话通道限制**：`Event.ParseCommands`（`Event.cs:11727-11733`）把**整段脚本**过一遍
    `TokenParser.ParseText`；`Dialogue.parseDialogueString`（`Dialogue.cs:590`）也过一遍。
- ⇒ **`[FarmName]` 是更宽的等价物**，事件脚本里任何位置都能用。

### 4.3 `{{ObjPro}}`（按玩家性别的 him/her）→ `${him^her}$`

`Dialogue.applyGenderSwitchBlocks`（文档注释原文：`Replace gender-switch blocks like ${male^female}$ or ${male¦female}$`），
而 `Event.ParseCommands` 的**第一步**就是它 ⇒ 事件脚本**全通道**可用。
但谢恩的 `ObjPro` 是**两块带 `When` 的 DynamicToken**（`PlayerGender` male/female），
把它翻成 gender block 要**按令牌定义改写表达式**，不是简单代换。**本轮没做**（见第 5 节）。

### 4.4 `{{Random: 1,2}}` → **没有原版等价物**

原版没有任何「文本里随机取一支」的机制（`ParseCommands` 只做 gender block + token，
`Dialogue` 只做 `@`/`%…`）。只能不写。

### 4.5 `{{JasAuncleToken}}` → **代不了，而且不该代**

它的值本身是 `{{i18n:ICS.JasAuncle.{{JasAuncle}}}}`（`content.json` 的 DynamicTokens），
跨「i18n × 配置字段」两层，**且随语言不同而不同**。
补丁层的令牌展开发生在**选语言之前**，代了就等于把一种语言写死进所有语言版本 ⇒ **必须不代**。

## 5. 为什么这一轮**没有**顺手把 `@` / `[FarmName]` 代上

- **要判「这段字符串属于哪条命令」，代换器现在没有这个上下文**（它在通用 JSON 上走）。
  通道判错的代价是写出**可见的字面 `@`** —— 比不写更糟。
- **规模**：本轮实测这类残渣全语料只有 **13 处**（Task 48 已拦掉 418/431）。
- 记成「**已考察、可做、暂缓**」。要做的话前提是：
  在 `Data/Events/*` 的值上先做一层**命令切分**（照搬 `Event.ParseCommands` 的 `/` 切分），
  再按命令名白名单决定 `{{PlayerName}}` 换 `@` 还是拒绝。`{{FarmName}}` 则无脑换 `[FarmName]` 即可。

## 6. 受控对比（32 包，同参数，只有 exe 不同）

改前 exe 由 `git worktree` 从 HEAD 单独编译（`D:\tmp\sdvcp_base`），改后 = 工作区。
两轮都用 `CORPUS_SHARED_TREE=1`，输出 `D:\tmp\c8`（前）/ `D:\tmp\c9`（后），
逐项比照 `tools/corpus-diff.py`。**改前那轮复现了上一轮记下的数（3562/42），基线可信。**

| 指标 | 改前 | 改后 | 差 |
|---|---:|---:|---:|
| 补丁合计 | 6743 | 6743 | 0 |
| 可合并 | 5509 | 5509 | 0 |
| **已写入** | 3562 | **3535** | **−27** |
| 写入失败 | 42 | 39 | −3 |
| `NeedsReview` | 1101 | 1131 | +30 |
| 其余判定 | — | — | **逐项相同** |
| **未展开令牌（影响行为）** | **13** | **0** | **−13** |

那 13 处的最后一处也不剩：`Strings/StringsFromMaps / FarmName` 5、`Data/Events/Farm` 4、
`Data/Events/Forest` 3、`Data/Events/AnimalShop` 1 —— **全部清零**。

### ⚠ 代价比「13 处」大：30 条补丁，且**大头不是它**

新拦下的 30 条 = 谢恩 11 + 孩子剧情 17 + SVE 1 + 鸟类观察协会 1。**两类成因**：

- **A 档**：i18n **具名参数**里有代不掉的令牌
  （`|PlayerName={{PlayerName}}`、`|FarmName={{FarmName}}`、
  `|PlayerName={{Ivy.OrnithologistsGuild/PowerUpPlayerName}}` —— 最后这个是**别的 mod 的 C# 令牌**，
  根本不在我们能查的范围内）。**必须拦**，否则玩家看到字面花括号。
- **B 档**：**带 `When` 的令牌嵌在 i18n 的键里**，例如孩子剧情的
  `{{i18n:TwoKidsEat01_George{{WhenSpouseIsAlex}}}}`，而
  `WhenSpouseIsAlex` = 两块（`""` 无条件 + `"_Alex"` when spouse contains Alex）。
  我们判不了 `spouse |contains=Alex` ⇒ 落到无条件默认块 ⇒ 标 `Lossy`。
  - 旧代码**照样写**（`TranslationApplier` 对键上的 `Lossy` 只记一条 note，
    `tokens.Expand` 的结果照用）⇒ 产出的是**猜出来的** `TwoKidsEat01_George`
    （对所有「配偶不是 Alex」的玩家恰好对，对 Alex 玩家是错的）。
  - 新代码**拦住**。这不是新政策，是把已有政策**落到实处**：
    `ValueTokenSubstitutor.Expand` 早就写着「**`Lossy` 一律拒绝** —— 那是猜出来的值」，
    只是**同一个配对 bug** 让判定侧看不见 i18n 里的它。

⇒ **这一档是行为改变，如实记账**：这部分对话**不再写**（其中本来写出去的是**猜**出来的值）。
若认为「宁可给通用文案也别缺」 —— 那要改的是**政策**（`Lossy` 是否放行），
不是这个 bug 修复；两件事不混。

> **未确证**：30 条里 **A 档 / B 档各占几条，我没逐条分类**。
> 已知：谢恩那 11 条里有 A 档源（`|JasAuncleToken={{JasAuncleToken}}`）也在产出里留下 8 处残渣
> ⇒ 至少 4 条是 A 档；孩子剧情那 17 条**抽样**到的是 B 档（`{{WhenSpouseIsAlex}}` 在键里）。
> 要精确分档得让闸把「残留的令牌名」也记进原因，再跑一轮 —— **本轮没做**。

## 7. 已知不一致（本轮**没**动，记在这里）

`TranslationApplier` 对**键**上的 `Lossy` 是「记 note、照写」，
而判定侧（`Program` 的令牌闸）现在是「拦住」。**同一个情形两种政策**。
CLI 路径上闸先跑，写入侧那段实际上到不了；`LoadApplier` 那条路也另有闸。
⇒ 现状是**够用但不对称**。要真正统一，应把写入侧也改成拒绝（那时
`TranslationOutcome.Notes` 会失去唯一生产者，要一并处理）—— 本轮**没做**，
因为语料上零差别，而改它要动一处**没被测到**的路径。

## 8. 未确证清单（不得当成结论用）

1. `@` 与 `{{PlayerName}}` 在**分屏**下的差异：CP 是 per-player（`PerPlayerValueProvider`），
   `@` 取的是当前 `farmer` —— 单机下相同，分屏**未验**。
2. `splitSpeak`（4 条命令、1 条带 `@`）的通道判定**未单独核实**（`Event.cs:2267`），
   只是计数上不像「非对话通道」。
3. Task 49 那 30 条新拦的补丁里 **A 档（i18n 实参）/ B 档（键上带 `When` 的令牌）各占几条**，
   **未逐条分类**（见第 6 节）。
4. `question` / `quickQuestion` **已收敛**：代码路径 `DialogueBox(string, Response[])`
   （`DialogueBox.cs:115-138`）没有 `@` 替换；修正扫描器的 `(break)` 切分后原版计数为 **0/46、0/30**。
   两边一致 ⇒ 判为**不支持 `@`**。
