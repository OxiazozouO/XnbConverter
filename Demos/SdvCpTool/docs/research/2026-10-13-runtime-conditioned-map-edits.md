# 带**运行时条件**的地图改动：量清楚、再决定要不要动

日期：2026-10-13
起因：业主报「地图之间的传送又不见了」，并追问「季节怎么也丢」。要回答的是
**"丢掉的那些到底值多少、能不能烤"** —— 这是一份**决策材料**，不是已经改了行为。

## 0. 现状（政策与计数）

`PatchJudge` 对 `EditMap` 的分支（`Judging/PatchJudge.cs:380-415`）：

| 条件 | 处置 |
|---|---|
| 无条件 | 直接改 tbin（`ConvertedNoCondition`）|
| `FarmType: X` 且目标就是 `Maps/Farm_X` | 目标自证 ⇒ 按无条件改 |
| 全部条件 ∈ {`Season`} ∪ {`HasMod`/`ModId`/`HasFile`/`FirstValidFile`、包内 `ConfigSchema` 字段、包内 `DynamicTokens`、`作者.Mod/令牌`} | 走烘焙通道求值（`Channel.Bake`）|
| 其余（**运行时条件**） | `LocalEdit`：静态 XNB 树里没有通道，**只报不写** |

⚠ **`Season` 是"求值"不是"保留"**：烘焙时按项目政策**冻结到春天**（`Baker.EvalSeason`）——
`Season: Spring` 的改动会被应用、`Summer/Fall/Winter` 的被丢弃。这就是"季节也丢"里
**地图结构那一半**的由来（图集那一半没丢：四季文件都在树里，见
[`../verification/2026-10-13-map-tilesheet-reference-shape.md`](../verification/2026-10-13-map-tilesheet-reference-shape.md)）。
地图没有按季通道（游戏只有一套 tbin），只能**留一季**；四季全并进来会互相打架（秋天的路 + 冬天的冰桥）。

本轮全语料（232 包，`prof89`）实测：

- `EditMap` 共 **2795** 条：`ConvertedNoCondition` **773** 写成功 / **2** 未写、
  `NotApplicable` **762**（条件静态折叠后恒假，本就不适用）、`LocalEdit` **1013**、
  `NeedsReview` 178、`TargetMissing` 59、`TargetContainsTokens` 8。
- **1013 条 `LocalEdit` 全部是"条件里含运行时令牌"**：逐一按上面那张表重算，
  **只有 2 条**是"条件全在政策内却没走通道"的 —— 两条都是 `Query`（`{{DaysPlayed}} < 32`、
  `{{HasFlag…}} AND {{OnlyWithShortcutsUpgrade}} OR NOT …`），`Query` 本来就没有静态求值
  ⇒ **政策层面没有漏网**（不是"判定漏了"，是"确实判不了"）。

运行时令牌（按出现次数）：`HasSeenEvent` 426、`HasFlag` 241、`Weather` 215、`Time` 160、
`Day` 78、`HasWalletItem` 71、`DayOfWeek` 63、`FarmType`（改别人地图的）54、`Relationship` 49、
`Year` 42、`IsJojaMartComplete` 21、`IsCommunityCenterComplete` 12、`Spouse`/`Engaged`/`Married` 各 ~12 …

## 1. 这些改动**本来要改什么**（把补丁体读出来数）

按报告里的 `source` + `index` 回读包内 `content.json` 的补丁体（1013 条里读到 **1012** 条）：

| 要改的东西 | 条数 | 性质 |
|---|---|---|
| `MapTiles`（逐格改图块）| 358 | 地形/墙/水/物件 —— **结构性** |
| `FromFile`（`FromArea`→`ToArea` 换区域块）| 419 | 结构性（含门、建筑、通道）|
| `MapProperties` | 84 | 地图属性（可能含 `Warp`）|
| `TextOperations` | 65 | **牌子/告示的文字** —— 纯外观 |
| `AddWarps` / `AddNpcWarps` | 20 | **加传送点** —— 玩家直接感知 |
| 其它 / 组合 | 66 | |

按**目标是不是包自带的地图**分（目标不在原版树里 = mod 自己的图）：

| | 条数 |
|---|---|
| mod 自带的地图 | **约 620**（62%）|
| 原版地图 | **约 385**（38%）|

## 2. 玩家可见的损失：**原版地图的传送点，一条没少**（订正旧结论）

`tools/lost-warps.py <产出树> <原版树> --reports <产出根>`（同参数、全语料）：

```
原版有 259 张地图；产出里有 1246 张
★ 少掉传送点的地图 1 张，合计 1 条     ← 要盯的是这一栏
  （另有 33 张 / 84 条是包自带整张图（Load 替换）—— 原版那几条本就不该在，CP 在游戏里同样如此）
```

- 那 **1 条**是 `Maps/SkullCave` 的 `7 9 Desert 8 6` —— 逐字节看，我们写的是
  `7 9 Desert 44 58`：**被补丁改掉了**（mod 的意图），不是丢失 ⇒ **真缺口 0**。
- 33 张"少 warp"的（`Summit` 16/16、`Farm` 10/10、`AdventureGuild`、`JoshHouse`、
  `Beach-NightMarket`…）全是**包自带整张图**：报告里有对应的 `Load` 记录，游戏里 CP 也
  同样整张换掉 ⇒ **两边一致，不是我们的损失**。

⚠ **订正**：`PatchJudge` 里那段注释（"52 条条件化的 EditMap 被整条丢掉 ⇒ 9 张原版地图的 warp
比原版少：`Summit` 8→2、`AdventureGuild` 10→5、`JoshHouse` 15→9 …"）当时**把整张 `Load` 替换
也算成了丢失**。其中真正因"条件判不了"丢掉的部分，随着**包内 `DynamicTokens` / 配置字段**
那次烘焙修复（2026-10-12，净 +287 条）已经补回来了；剩下的差异是**替换**，不是丢失。

⇒ 结论：**本轮语料里，"运行时条件丢掉地图改动"对原版地图的传送点造成 0 条可测损失。**
丢的是**加出来的新内容**（mod 自己地图上的门/路/装饰，以及本可加在原版图上的通路），
以及**牌子文字**这类小东西。

## 3. 三个选项（给业主定）

| 选项 | 做法 | 代价 / 风险 |
|---|---|---|
| **A. 保持**（现状）| `LocalEdit` 只报不写 | 玩家拿不到那些条件化的新增内容（本语料实测：原版传送点 0 损失；主要损失在 mod 自己地图上的装饰性改动）|
| **B. 分类烤** | 只烤**纯外观**那一档（`TextOperations` 65 条）与**只加不改**（`AddWarps`/`AddNpcWarps` 20 条）| 小、可解释；但 65+20=85 条，占 8% —— 剩下的 928 条结构性改动照旧丢 |
| **C. 一律按"成立"烤** | 所有运行时条件当作真，无条件应用 | 覆盖全部 1013 条。**风险是语义**：条件本来是"进度到了才出现"（`HasSeenEvent: 5553210`），提前固化会让玩家**还没到那时候就多一条通道/多一堵墙**；`Weather`/`Time`/`Day` 这类还会固化一个随机分支。可机械兜底的是**悬空传送**：烤完跑 `check-map-warps.py` 看有没有 warp 指向不存在的地点 |

**建议：保持 A，先把这份量化的账写进"装机后要跟玩家说清楚的损失"**（已有一条，
本轮把"原版传送点 0 损失、损失集中在 mod 自己地图上的装饰性改动"补进去）。
真要动，**C 配 `--bake-runtime-map-conditions` 开关**比 B 更值得做：B 覆盖 8% 却要新增一整套
分类判据，C 是一条开关 + 事后自检兜底，且业主随时能关掉。

## 4. 工具

| 脚本 | 干什么 |
|---|---|
| `tools/lost-warps.py` | 产出比原版少掉的传送点；`--reports` 把"整张 `Load` 替换"单列 |
| `tools/classify_localedit*.py`（临时脚本，在 `D:\tmp`）| 本文 §0/§1 的取数：按政策分类、回读补丁体 |

⚠ 取数时踩的两个坑（都已修在工具里）：
1. **判据要按结构取**：先在整份二进制里抓"五元组形状"，把 `Maps/SeedShop` 数出 25 条 `Warp`——
   而它的 `Warp` 属性其实只有 **1** 条（`6 30 Town 43 57`），"少 18 条"整栏是假的。
   改成按 `Warp\x03` + int32 长度取内容后，同口径重测才是可信的数。
2. **大小写**：报告里 `Load` 的目标按原样存、比较时用小写 ⇒ 一个都匹配不上（"Load 替换 0 张"）。
   统一小写后 33 张 / 84 条才浮出来。
