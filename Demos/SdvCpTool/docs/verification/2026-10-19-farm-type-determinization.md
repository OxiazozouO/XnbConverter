# 农场类型确定化：`FarmType` 冻结到 `Standard`

日期：2026-10-19
起因：实机报「农场传送到巴士站后被卡到地图外，动弹不了」。

## 1. 现象

产出树（fix21）里 `Maps/BusStop` 左侧 12 块图块（`Back` 层 x=0/9/10，y=22–25）的动作全是：

```
TouchAction = Warp Farm 142 16
```

而本产出的 `Maps/Farm` 是 **Grandpa's Farm**（**140×93**）—— `x=142` 已经在图外。
玩家踩上巴士站左侧的返程图块后，`warpFarmer` 把角色放到农场图外，移动被卡死。

## 2. 根因

- 这 12 块的来源是 **Frontier Farm** 的 `Assets/MapPatches/FrontierFarm_BusStopWarps.tmx`
  （`LoadMap Farm 142 16 3`），补丁条件为 `When: { "FarmType": "FrontierFarm" }`；
  `142 16` 对 Frontier Farm 自己的农场图（**156×65**）是合法坐标。
- 转换器此前把 `FarmType` 当成**判不了的运行时条件**，而条件化地图改动「范围过大」时
  走的是「**照常写入、不设门禁**」：Frontier Farm 的整块变体覆盖了先写入的 Grandpa's Farm 变体
  （`Farm 89 33` / `Farm 112 38`）。
- CP 在 Standard 存档上**只会**应用 Grandpa's Farm 那份；我们把两份都写了，后写的赢。

## 3. 修法

- `Baker.FrozenFarmType = "Standard"`：`FarmType` 与 `Season → 春天` 同一条**确定化**政策。
  产物面向 Standard 存档（Grandpa's Farm 本身也是装在 Standard 上的整图替换）。
- `PatchJudge.BakeableForMap` 接受 `FarmType` ⇒ 走既有的烘焙通道：
  求值为真按无条件写；为假整条不适用（跳过）。
- 条件值里还有**未展开令牌**（实测 `Lnh's Farms Convenient Road` 的
  `{{LNHNewFarmReplacement}}`）⇒ 判「不知道」，**不**按字面当成假。
- `|contains=` 的语义与 `Season` 共用同一份实现（先折成布尔，再与声明值比）。

## 4. 验证

- 单测：**1528/1528** 通过（新增 FarmType 求值、`|contains=`、未展开令牌、地图判定通道的断言）。
- 定向转换（SVE + Grandpa's Farm + Frontier Farm）：
  - Frontier Farm 的 `Maps/BusStop` 补丁判定为 **NotApplicable**
    （`FarmType = FrontierFarm` = 假）；
  - 产出 `Maps/BusStop` 的返程图块恢复为 `Warp Farm 89 33`（x=0/10，y=22–25）与
    `Warp Farm 112 38`（x=22–24，y=29）；
  - 三个落点在 `Maps/Farm`（140×93）内且 `Back` 层无 `Buildings` 实体 ⇒ 可走。
- 越界传送审计（`tools/check-warps-in-bounds.py`，只查树里存在的目标地图）：
  - fix21：`checked=473 out-of-bounds=16`，其中 **12 条**就是
    `BusStop -> Farm 142 16 [140x93]`；
  - fix22：**见下节**。

## 5. 全量 A/B（fix21 → fix22）

> 同参数、全语料 232 包。

- 产出树：**31861 → 31861 个文件**，新增 **0**、消失 **0**、内容变化 **80**。
- 变化的 80 个文件全部属于「农场类型变体」这一族：
  - `Data/Events/{BusStop,Farm,FarmHouse,Mine,ScienceHouse,Town}`（37 个，含各语言）；
  - `Data/{Locations,Minecarts×9,WorldMap×7}`；
  - `LooseSprites/map{,_summer,_fall,_winter}.png`（Frontier 的世界地图不再覆盖）；
  - `Maps/{BusStop,Desert,Forest,Woods,Farm,FarmCave,Farm_*,JojaMart,ManorHouse,ScienceHouse,Custom_*}`（22 个）。
- 判定分布（fix21 → fix22）：
  - `Converted` 5759 → 5723，`ConvertedNoCondition` 9041 → 9030
  - `NotApplicable` 10455 → 10577
  - `NoLandingPoint` 2209 → 2169，`TargetMissing` 287 → 264
  - 补丁总数 32194 → 32187：唯一的差额是 `Lnh's Farms Convenient Road` 的 7 条
    `Maps/{{Farm}}` 候选展开 —— 令牌现在按 Standard 解析成 `Maps/Farm`，
    不再把便捷通道同时写进 `Farm_Combat/_Fishing/_Foraging/_FourCorners/_Island/_Mining/_Ranching`
    （CP 在 Standard 存档上也只写 `Maps/Farm`）。
- 地图级传送集合：`BusStop/Farm/Forest/Woods/Desert/FarmCave/JojaMart/ManorHouse/ScienceHouse/
  Custom_*` 逐张对比，**条数完全相同**（变化都在图块级）。
- 越界传送审计（`tools/check-warps-in-bounds.py`，全树）：
  - fix21：`checked=4485 out-of-bounds=67`
  - fix22：`checked=4483 out-of-bounds=55`
  - 差集**只有** 12 条 `BusStop -> Farm 142 16`，**没有新增**越界。
- 增量装机（80 个文件打包 80/0）后解包 `Maps/BusStop.xnb` 复核：
  返程图块为 `Warp Farm 89 33` / `Warp Farm 112 38`；
  三个落点 `(24,20)`、`(11,23)`、`(23,28)` 的 `Back` 层都没有 `Buildings` 实体。
- 顺带的正向变化：`Data/Locations.Custom_GrandpasGrove` 补上了 FTM 的觅食条目、
  `Data/WorldMap.Valley` 多出 6 个地图区域 —— 都是原先因 `FarmType: Standard` 判不了
  而整份丢掉的内容，现在按 Standard 正确写入。

## 5.1 追加：候选目标展开不再「无条件枚举」（fix22 → fix23）

同一轮越界审计还揪出一条**同类**的：

```
MAPWARP Farm (60,6) -> FarmCave 37 13   [target 25x20]
```

- 来源：`[CP] Ultimate Farm` 的 `TextOperations`，`Target: "Maps/UltimateFarm, Maps/{{file}}"`。
  它的 8 个 `{{file}}` 块全部以 `When: { ReplaceVanillaFarm: X }` 为条件，而配置里
  `ReplaceVanillaFarm` 是**空值**（`AllowBlank`）⇒ CP 里令牌**没有值**、整条补丁不落。
- 我们原来的 `CandidateTargetExpansion` 在令牌代不掉时按**所有候选值**各写一份，
  于是把 Ultimate Farm 的洞穴传送追加到了 `Maps/Farm`（还有 7 张 `Farm_*` 和它自己的
  `UltimateFarm` 图）—— Grandpa's Farm 上踩 `(60,6)` 就会传进 25×20 的 `FarmCave` 图外。
- 修法：`CandidateTargetExpansion` 先看**所有块的条件**——若**每一块都静态为假**（没有一块
  是「不知道」），令牌在 CP 里就是无值，**不展开**；只有存在「判不了」的块时才按候选枚举。
  新增 `DynamicTokenResolver.TryEvaluateWhen` 作为判据。
- fix22 → fix23：新增 0 / 消失 0 / 改动 **9** 个地图；越界审计 **55 → 46**，
  减少的正是 `Farm` / 7 张 `Farm_*` / `UltimateFarm` 上那 9 条 `FarmCave 37 13`，无新增。
  单测 **1529/1529**。
- 副产物：`Integrated Minecarts` 的 2 条 `Maps/{{WaFFCaveMap}}` 不再被枚举成
  `A_TK.FarmProjectForaging_Custom_waterfall_cave_*`（条件里 `FarmType` 的 WaFF 农场与
  `HasMod A_TK...` 都不成立）⇒ 与 CP 在 Standard 存档上的行为一致；
  `Ultimate Farm CP` 的补丁数 40 → 17，同样是 `{{file}}` 无值后不再写出别的农场图的内容。

## 6. 已知遗留（不属于本次修复）

`--only Farm,BusStop` 过滤后，fix23 里还剩 3 条
（全树 46 条里的大多数在只会在特定农场类型下加载的 mod 地图上，未逐条排查）：

```
MAPWARP Custom_LASV_BiankaDate (0,0) -> BusStop -10 -10  [65x30]
MAPWARP UltimateFarm (32,91) -> Farm 49 97                [140x93]
MAPWARP UltimateFarm (32,92) -> Farm 49 97                [140x93]
```

- `Custom_LASV_BiankaDate -> BusStop -10 -10` 是负坐标；该地点只在特定事件里出现，
  先记录不处理。
- `UltimateFarm` 的两条是 mod 自己那张农场图内部的传送，目标农场图在对应存档里不是
  Grandpa's Farm；该图只会在 Ultimate Farm 作为当前农场时加载，先记录不处理。
