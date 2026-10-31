# 补丁字段守卫：它一上线就抓到两个真东西（**字段名大小写不敏感**）

日期：2026-10-11

## 起因：FTM 那次的教训

FTM 通道漏读了 `<段>SpawnEnabled` 与 `File_Conditions` 两个字段（
`2026-10-11-ftm-can-land-forage-buried.md` §6）——**静默按"它不存在"处理**，语料里恰好不触发才没出事。
同一类风险在 CP 那边也存在（字段是会长的，`TargetLocale` 就是后加的），所以加了一道**守卫**：
一条补丁里出现我们不认识的键，就写进 `pack.Warnings`（→ 报告的 `notes` 段）。

白名单来自 **CP 自己的解析器**（反编译 `PatchLoader.cs` 里出现过的键全集），外加 CP 文档里的 `LogName`。

## 守卫第一次跑全语料：25 条命中，三个键

```
MigrateLegacyNames 13      Patchmode 10      when 2
```

**后两个是真的踩到东西了。**

### ★ 发现一（真 bug）：CP 的字段名**大小写不敏感**，而我们按大小写直取

CP 是把补丁对象**反序列化进 `PatchConfig`** 的（`ConfigModels/PatchConfig.cs`），
JSON.NET 的属性匹配**不区分大小写** ⇒ `"when"` / `"Patchmode"` 在 CP 那边**照常生效**。
我们这边是 `raw["When"]` 直取 ⇒ **当成"没写"**：

| 写法 | 我们会怎么处理（修之前） | 后果 |
|---|---|---|
| `"when": {…}`（SVE 2 处）| 条件被丢掉 | 该门控的对话变成**无条件**，事件之后还会出现 |
| `"Patchmode": "Overlay"`（SVE 10 处）| 当成默认的 `Replace` | 语义反了（该叠加的变成了覆盖）|

**修法**：加载时把**字段名**统一成 CP 的规范拼写（`PatchFieldGuard.Normalize`，只动名字、不碰值，
只认白名单里的名字）。这样**下游 45 处 `Raw["X"]` 一处都不用改**。

⚠ 有意思的是：**嵌套那层早就知道这件事** —— `EditDataMerger.Field` 的注释写着
「按**不区分大小写**的字段名取值 —— CP 的补丁字段名就是这么认的（语料里同一种写法有
`Id`/`ID`、`AfterId`/`AfterID`、`BeforeId`/`BeforeID` 并存）」。漏的只是**补丁顶层**那几处直取。
⇒ 这也解释了为什么 `MoveEntries`（17 条 `Id` + 13 条 `BeforeId`）一直是对的，
而 `"when"`/`"Patchmode"` 一直被当成"没写"。

### 发现二（不是 bug，是模组写错了地方）：`MigrateLegacyNames`

它属于 **`CustomLocations`**（`ConfigModels/CustomLocationConfig.cs:11`），
而 SVE 把它写在了 `Load Maps/…` 补丁上（13 处）—— **CP 自己也不会读它**。
⇒ 报告里如实写成"CP 自己也不会读它，这不是我们的漏读"，别让人去"修"。

## 同参数受控对比（36 包）

```
被报未知字段：  改前 {MigrateLegacyNames 13, Patchmode 10, when 2}
               改后 {MigrateLegacyNames 13}          ← 大小写那两个不再报（已按 CP 的口径处理）
判定：          ConvertedNoCondition 2137 → 2135，NeedsReview 1077 → 1079
产出树：        只在改前 0 / 只在改后 0 / 内容不同 1 个文件 = `Characters/Dialogue/Olivia.json`
```

那 1 个文件正是那两条 `"when"` 补丁：**修之前它们被无条件写进去了**（事件之后还能看见不该看见的对话），
修之后条件被认出来、判 `NeedsReview`（对话键那套落点接不住）⇒ **不写**。
⇒ 按「宁可少写，不可写错」，用 2 条**写错的**对话换 2 条**不写的**，这是对的。

⚠ 另外 10 条 `"Patchmode": "Overlay"` 修完**产出树没变** —— 因为我们的地图应用路径对
`FromArea`/`ToArea` 本来就是"贴上去"（不先清空）⇒ 模式读没读**在这条路径上等价**。
`PatchMode: "Mask"` 之类仍然值得留意（CP 2.9 已禁用 `Mask`）。

单测 **1313 通过 / 0 失败**（新增 4 条：未知字段报警 / 大小写不敏感 / 认识但没实现单报 /
属于别处的字段标明"CP 也不读"）。

## 顺带：同一套"读漏字段"的办法，把**别的几个方向**也过了一遍

| 方向 | 查法 | 结论 |
|---|---|---|
| CP 的 `content.json` **顶层键** | 枚举语料里全部 CP 包的顶层键 | 只有 `Format / Changes / ConfigSchema / DynamicTokens / CustomLocations` —— **五个全都在处理**（`CustomLocations` 走 `ContentPack.ExpandCustomLocations` ⇒ `Load Maps/<Name>` + `Data/Locations` 登记）✓ |
| 我们自己写出去的 `Appearance[]` | 对着游戏消费它的代码核（`NPC.cs:957-1010` 会看 `Precedence`/`Season`/`Outdoors`/`Indoors`/`IsIslandAttire`/`Weight`/`Condition`）| 我们只写 `Id/Condition/Sprite/Portrait/Precedence`，其余取模型默认值 —— 而默认值恰好是我们要的语义：`Indoors = true`、`Outdoors = true`（**室内外都生效**，与 CP 的 `EditImage` 没有室内外概念一致）、`IsIslandAttire = false`、`Weight = 1`（`StardewValley.GameData.Characters/CharacterAppearanceData.cs:20/24/37/46`）✓ **不用改** |
| FTM 段**内**的键 | 同 §6 的做法 | 又找出一批"原版没这个槽"的旋钮，改成**如实报出**（见 `2026-10-11-ftm-can-land-forage-buried.md` §6.1）|
| 只打 stdout 的转换说明 | 逐条对报告 | `CustomLocations 展开：N 个` 原先只在终端 —— 与 FTM 那次同一个毛病，已并进报告的 `notes` |

⇒ 这条经验值得记：**"我们不认识的字段"要能自动冒出来**（要么报出来、要么在语料上量出来），
别等出了事才回头找 —— 守卫这次上线第一次跑就抓到了大小写那个真 bug。

## 收口：本轮改动过了一遍**独立代码审查**，逮到两个真 bug（都复现了）

同一次提交让审查 agent 逐行看了一遍（上一轮这么做也逮到过 critical），结果是**两个真缺陷 + 两处加固**：

| # | 严重度 | 缺陷 | 修法 |
|---|---|---|---|
| 1 | **Critical（崩溃）** | `TokenEnumerationExpander`：令牌有 **≥2 个条件块、却没有无条件默认块**、且没占满四季时，`CandidateShape` 返回的 `DefaultValue` 是 `null`，`Build` 里 `DefaultValue.Length` 直接 **NRE** —— 而且类注释第③条本来就说"无条件块**至多**一块"（含零块）。**我的测试恰好每条都带了默认块，漏掉了这个形状** | 未占满四季时**必须**有一个非空默认值，否则整条不拆（`if (string.IsNullOrEmpty(defaultValue)) return null;`）；补两条反例测试 |
| 2 | Important（会写错） | `PatchFieldGuard.Normalize`：一条补丁里 `when` 与 `When` **并存且值不同**时，先处理的 `when` 会**覆盖**已存在的 `When` ⇒ 我们取到**先**出现的值，而 CP（JSON.NET 反序列化，重名键取最后一个）取**后**出现的 ⇒ 静默写错 | 按**文档顺序"最后一个赢"**收集再统一写回；补一条测试 |
| 3 | 加固（未确证可达） | `FileWhen` 对 `File_Conditions` 的**形状**没核过：若某项不是数组/对象，会被静默当"没有" ⇒ 把"只在某玩家/某农场类型下生效"的整份文件写成**无条件** | 形状与 FTM 模板（`data/default.json`：三个数组 + `OtherMods` 对象）不符就**整份不转**并报出；补两条测试 |
| 4 | Minor（少报） | 报警去重键用的是**文件名**，不同目录下的同名 `content.json` 会互相吞掉 | 去重键换全路径，标签打"上一层目录/文件名"（全路径太长） |

⚠ 第 1 条尤其值得记：**"我每条测试都恰好带了默认块"** —— 测试的**形状覆盖面**和"逻辑覆盖面"不是一回事。

**受控对比**（同参数、四处一起改完再跑）：判定**一条没变**、产出树 **13562 个文件逐字节零差异**
（这几处形状这份语料都没触发），notes 条数不变（第 4 条只换了标签写法）。
单测 **1319 通过 / 0 失败**。
