# 「条件写在游戏不读的位置」这条自检：判据换成**模型里有没有这个字段**

日期：2026-10-13
起因：链式跑法（`convert-mods.py`：`--tree` 与 `--out` 同一棵）下，`conditionPlacement` 与
`outputShape` 这两项自检**无从对照 ⇒ 整体跳过**（报告里 `skippedSameTree: true`，
文档里写明「0 是没比，不是没问题」）。补了个 `tools/audit-vs-vanilla.py`：**逐包各自对原版树**
跑一遍（跑完删树，不然 232 棵会吃掉 400+ GB）。

## 1. 一上来就撞见一堆假阳性

`Data/FruitTrees` 的 `Fruit[]/Condition` —— **每个包报 19 处**，把自检淹了。

核事实：`Condition` 是 `FruitTreeFruitData` 从基类 **`GenericSpawnItemDataWithCondition` 继承**
来的（子类注释还专门指向它，`StardewValley.GameData.FruitTrees/FruitTreeFruitData.cs`），
**游戏会反序列化它**，只是**原版自己没用过**这种写法。

原判据只有一条：「原版同一形状上有没有这个条件字段」⇒ 「合法但原版没用过」全成"写在游戏不读的位置"。

## 2. 改法

- 生成器多出一张 **`KnownFields`**：类 → **全部字段名**（含标量、含**继承来的**）。
  `Types` 那张表只收容器型字段（标量没有形状可查），回答不了"这个位置上模型有没有这个字段"；
  继承那一半是必须的 —— 不合并基类，上面那 19 处假阳性就还在。
  （实测对照：`FruitTreeFruitData` 有 `Condition` ✓；`WorldMapAreaData` 没有 `KnownCondition` ✓。）
- 自检判据改成**两条并用、都成立才报**：
  ① **模型里没有**这个字段（沿类型图下钻查 `KnownFields`）；
  ② 原版**同一形状上也没用过**这个条件字段（兜住模型认不出的位置，比如类名下钻不到）。
- 顺带修好下钻本身的两处：**字典型资产那一层**（键是条目标识、不是字段 ⇒ 类名要沿用而不是丢）、
  **数组那一层**要补 `[]`（不然形状路径与原版对不上，仍会全报）。

## 3. 验证（配对单包）

| 包 | 改前 | 改后 |
|---|---|---|
| `Lumisteria Tilesheets - Outdoor` | misplaced **19** | **0** |
| `CapeStardew` | misplaced 1（被淹没在 19 里）| **1 —— 真问题** |

那 1 处：`Data/Objects` 的 `UnlockConditions: "f Annetta 1500"` ——
`UnlockConditions` 只声明在 **`CharacterData`** 上（`NPC.cs` 用它），`ObjectData` **没有**这个字段
⇒ 游戏整段忽略（而且 `f Annetta 1500` 是 CP 的简写、不是合法 GSQ）⇒ 包自己的 bug。

测试：**+2 条**（继承来的合法字段不报 / 模型里没有的照报），并**订正一条旧测试**：
它原本断言 `PowersData.UnlockedCondition` 该被报 —— 而那正是 powers 数据自己的字段
（`StardewValley.GameData.Powers/PowersData.cs:24`，文档里早写着"每个 power 自带 `UnlockedCondition`"）
⇒ 反过来断言**不报**。**1435 条全绿**。

## 3.5 逐条归因（已扫到的两类都是**包自己的**）

判据修好后逐条核过已扫出的 findings：

| 形态 | 归因 |
|---|---|
| `Data/Locations` 的**地点级** `Condition`（`LOCATION_Season Here Spring Summer…`）| **包的 bug**：`LocationData` 里**没有** `Condition` 这个字段（`StardewValley.GameData.Locations/LocationData.cs`）⇒ 游戏整段忽略它，**CP 下同样**（CP 对 `Data/Locations` 的条目不做条件）；GSQ 串逐字来自 `[CP] Frontier Farm\content.json` |
| `Data/Objects` 的 `UnlockConditions: "f Annetta 1500"` | **包的 bug**：该字段只声明在 `CharacterData` 上（`NPC.cs` 用它），`ObjectData` 没有；值还是 CP 的简写、不是合法 GSQ |

⇒ 「我们自己写错位置」的那一类，到目前扫到的包里**一处都没有**。

## 4. 全量逐包对照（跑完了）

`tools/audit-vs-vanilla.py`：232 包、**63.4 分钟**（每包一棵新树、跑完即删）⇒
**13 个包有 findings / 共 31 处**，`outputShape` **0 处**。

| 形态 | 处数 | 归因 |
|---|---|---|
| `Data/Locations` 的 `*/Condition` | 20 | **包自带**（`Frontier Farm` 两个包各 10）—— `LocationData` 没有这个字段 |
| `Data/WorldMap` 的 `**[]/KnownCondition` | 6 | **包自带**（SVE 等）—— `WorldMapAreaData` 没有这个字段（2026-10-09 那份复核就定过）|
| `Data/Objects` 的 `*/UnlockConditions` | 5 | **包自带** —— 该字段只在 `CharacterData` 上 |

⇒ **全语料里这两项自检没有一处是我们的**。

⚠ 对比判据修正的效果：改之前，光 `Data/FruitTrees` 的 `Fruit[]/Condition` 就**每包 19 处**假阳性
（232 包 ≈ 4400 处噪音），上面那 31 处真 findings 根本看不见。

⚠ 每包一棵树很贵（~16 s/包 ⇒ 63 分钟）；只要一包的结论就别跑全量。
