# 为什么装进游戏后进不去 mod 新增的地图

日期：2026-10-09

**症状**（用户实机游玩报的，三次启动都复现）：

> 尝试进入被 SVE 改造后的地图，总是**被原地 TP** —— 说明地图未能全部整合进来，
> 因为**多个地方可以对地图有多处修改**。

"原地 TP" = 玩家踩到原版地图上的位置，**新地图的入口（传送点）不在**。

## 量出来的东西

SVE 的 `EditMap` 补丁共 **517 条**，判定分布：

| 判定 | 条数 | 含义 |
|---|---|---|
| `LocalEdit` | **390** | 折完还剩条件 ⇒ **拒** |
| `NotApplicable` | 56 | 条件折成**恒假** ⇒ 本就不适用 ✓ |
| `ConvertedNoCondition` | 36 | 无条件 ⇒ **已写入** ✓ |
| `TargetMissing` | 32 | 目标不在树里 |
| `TargetContainsTokens` | 3 | Target 含令牌 |

被拒的 390 条，条件键分布（一条补丁可命中多个键）：

```
Season 147 / Time 129 / Weather 122 / HasSeenEvent 221 / HasMod 74
Day 69 / Random 62 / FarmType 35 / DayOfWeek 18 / Flag 类 ~40 …
```

典型长这样（`Maps/Farm_FrontierFarm` 加随机小动物）：

```json
{"FarmType": "FrontierFarm", "Time": "{{Range: 2000, 2600}}",
 "Season |contains=Winter": false, "Weather |contains=Rain, Storm": false,
 "Random: {{Range:1, 20}}": 1}
```

## 被拒的到底是"什么改动"（把 517 条按改法分类）

| 改法 | 条数 | 说明 |
|---|---|---|
| `FromFile` | **417** | 把一块 `.tmx` **贴到地图上**（路、桥、建筑、入口）|
| （空） | 47 | |
| `MapProperties` | 45 | 地图属性 |
| 传送动作（`AddWarps` / 图块 `Warp`） | **7–8** | ✅ **无条件的都已写入**；被拒的只有 2 条 |

⇒ **"原地 TP" 的主体原因是那 417 条「贴地形块」** —— 它们里面就有通往新区域的**路与入口**，
没贴上去，玩家自然走不过去（或踩不到该有的传送格）。**真正是"传送点"的只有 7–8 条，且大多已写**。

所以问题**不是**"传送代码没写"，而是**那 417 块地形（含路）落不下来**。

### ⚠ 订正（2026-10-09 复核）：上面这 417 条里**四分之一是小动物装饰**，表述夸大了

把 417 条 `FromFile` 按**来源文件所在目录**分：

| 来源 | 条数 | 是什么 |
|---|---|---|
| `assets/Maps/MapPatches/` | **245** | 真地形块（路、桥、建筑、入口）|
| `assets/Maps/Critters/` | **105** | **小动物贴图**（鸟/松鼠/猫头鹰的 2×2 叠加，条件全是 `Time`+`Season`+`Weather`+`Random`）|
| 其它 | 67 | `CropPhases/` 作物生长阶段、`FestivalMaps/`、`GrandpasShed/` 等 |

再把「来源块**自身带传送**」的挑出来（tmx 看 `Action/TouchAction="Warp"` 与地图属性 `Warp`；
tbin 按 `Warp x y 地名 …` 的形状匹配）：

**417 条里只有 22 条**，其中 **21 条带条件（被拒）**、1 条无条件（已写）：

```
Maps/Town           MapPatches/JojaDayReducedHours.tbin          HasSeenEvent 502261 + Day 22
Maps/Forest         MapPatches/Forest_Joja.tmx                   HasSeenEvent 5553210
Maps/Railroad       MapPatches/Railroad_Joja.tmx                 HasSeenEvent 5553210
Maps/Mountain       MapPatches/Mountain_Joja.tmx                 HasSeenEvent 5553210
Maps/BusStop ×3     GrandpasShed/BusStopShedPath.tmx             HasMod / FarmType
Maps/Custom_Forestwest ×2  MapPatches/ForestWest_Farmable.tbin   HasSeenEvent / HasFlag
Maps/Island_W       MapPatches/IslandWestLanceSecretWarpPath.tmx HasSeenEvent 65360194
…（共 21 条）
```

⇒ **"结构类"（会改传送 / 地图属性 / 地形的）被拒总数**：
`22 条带传送的来源块（拒 21）` + `AddWarps 1` + `MapProperties 含 Warp 9` +
`TextOperations 指向 Warp 3` ≈ **34 条**，不是 417。

**其余那 383 条落不下来，与"能不能走进去"无关**（小动物、作物阶段、节日装饰）。
这份订正不改结论（"进不去"仍是能力边界），但把"缺的是什么"说准了：
**缺的是这三十几条结构改动**，而不是几百条地形。

## 为什么拒得对，以及为什么救不回来

**拒得对**：原版地图**没有任何条件槽**。`2026-10-06-map-condition-slots.md` 逐点位核过 ——
地图里能承载条件表达式的只有 `ConditionalDoor`（开/挡门）与 `BuildConditions`（能否盖房），
**两个都改不了图块内容**，而 390 条要改的正是图块/传送点/地图属性。

**救不回来**：可离线求值的条件只有一张很短的名单 ——
`Condition/A ConditionVocabulary.cs:54` 的 `Bakeable = { HasMod, ModId, HasFile, FirstValidFile }`
外加包内 `ConfigSchema` 字段。**`FarmType` 都不在里面**（它读存档，离线不知道玩家选的哪种农场）。

而**常量折叠已经把能折的都折了**（上面 56 条 `NotApplicable` 就是折成恒假的）。
剩下 390 条里残留的是 `HasSeenEvent`（玩家进度）、`Time`/`Weather`（运行时）、
`Random`（随机）、`Day`/`DayOfWeek` —— **离线无论如何判不了**。
⇒ 这批改动**没有"再修一下就能救回来"的部分**。

⚠ 反过来也要说清：**不能靠"假设"硬写**。按某个假设把带 `HasSeenEvent` 的传送点写进去，
在那个事件没触发的存档里就会凭空多出一条路 —— 那是**造错**，比缺更糟。

## ⚠ 订正：地点与地图**都写进去了**（我先前读错了中间态）

初稿里我写过「产出的 `Data/Locations` 只比原版多 2 个地点，其余很可能由 SVE 的 C# 程序包
动态创建」——**那是我读错了**：`run-packs.py` 的后台任务还没写完，我就去读了产出树，
读到的是中间态（91 个地点、0 个 `Custom_*`）。

**重跑核实（等任务真正结束）**：

| 项 | 实测 |
|---|---|
| 产出 `Data/Locations` | **219 个地点**（原版 91 + **128 个 `Custom_*`**）|
| `Custom_DayEnd_WarpRoom` 等 57 个 | **都在** |
| 产出 `Maps/*.tbin` | 337 张 |
| CP `Load` 的 67 张 `Custom_*` 地图 | **一张不缺** |

对照 SVE 源码也印证：`code/Locations/LocationsData.json` 里那条 `EditData Data/Locations`
**无条件**、一次带 57 个地点定义，每个是
`{"CreateOnLoad": {"AlwaysActive": false, "MapPath": "Maps\\Custom_X"}}` —— 我们照写 ✓。

⇒ **「地点不存在」这一条不成立，也不是"未确证"，是证伪。**
SVE 的 C# 程序包（`StardewValleyExpanded.dll`，反编译逐段读过）**完全不创建地点**：
它的 `Entry` 只注册图块动作（`GameLocation.RegisterTileAction("DarkShrineInvasion", …)`）、
自定义背景、条件光源、Harmony 补丁；`loadMap` 只出现一次（强制重载已有地点）。
`locations.Add`/`new GameLocation`/`MapLoader` **0 处**。

⇒ **"进不去新地图"的原因就只剩一条**：上面那 390 条**条件化的 `EditMap`（入口传送点/地图改动）被拒**。

⚠ 附带一条操作教训：**别在后台任务没结束前读它的产出树**。这次差点把一条错误结论写进文档
（"少 2 个地点"），幸好用"直接调 CLI + 检查退出码"的另一条路径复核时对不上，才追出真相。

## 这一段的净结论

**"进不去新地图"不是某个 bug，是纯 XNB 方案的能力边界**：
地图的条件化改动（SVE 有 390 条）在原版里没有对应机制，而它们的条件依赖运行时状态。
能做的只有那 36 条无条件的 + 56 条恒假的，都已经做了。

---

## 补充核查（2026-10-10）：**「季节变体地图」这条通道不存在**（负结果，别重做）

一直有个念头：既然那 390 条是**条件化**的地图改动，而**季节**是其中最"静态"的一类
（`Season: spring` 之类），能不能物化成 `Maps/<名>_<季节>` —— 让游戏自己按季节挑地图？

**量出来的机会**：SVE 带条件的 `EditMap` 共 492 条，其中**条件只由 `Season`/可烘焙组成**的
**只有 34 条**，而这 34 条里大半是 **`HasMod`-only**（那种我们本来就烘焙成真/假、已经在写），
真正的 `Season`-only 只有个位数（`Maps/Town`、`Mountain`、`Railroad`、`Custom_Highlands`、
`Woods` …）。

**然后去查这条通道在不在**：反编译的 `StardewValley.GameLocation`（`gameloc.cs`）里
**没有任何「给地图路径拼季节后缀」的逻辑** —— 季节只影响

- `updateSeasonalTileSheets`（**图集**名字换季节前缀，不是地图）、
- 音乐/光照等。

地图路径来自 `Data/Locations` 的 `CreateOnLoad.MapPath`，是**一个字符串**，没有季节维度。

⇒ **这条通道不存在**，`Maps/Town_spring` 不会被加载。（`Maps/Town-Halloween` 那种是
**节日**名，由游戏按节日挑，属另一套。）

⇒ 那 390 条「进不去」的原因因此**两边都核过了**：既没有条件槽（`2026-10-06-map-condition-slots.md`），
也没有"按季节换地图名"这条路。**是边界，不是漏做。**

## 再补一条（同日）：**农场类型**这条通道**是真的**，但本语料里救不回东西

原版**每种农场类型各有自己的地图文件**（`Maps/Farm_Combat` / `Farm_Fishing` /
`Farm_Foraging` / `Farm_FourCorners` …，`Data/Locations` 里也有对应条目
`Farm_Standard`/`Farm_Beach`/`Farm_Forest`/… 共 8 种）。

⇒ 所以「`When: FarmType: X` 门控的地图改动」**在目标是农场那类地图时**是可以物化的
—— **这条通道确实存在**（与上一节"季节变体地图不存在"相反）。

**但本语料用不上**：SVE 里「条件 = `FarmType` + 可烘焙/`Season`」的 `EditMap` 只有 **8 条**，
目标分别是 `Maps/Custom_GrandpasShed` ×3、`Custom_GrandpasShedRuins` ×3、`Maps/BusStop` ×2
—— **没有一条**落在按农场类型分的那类地图上（`Maps/Farm*` / `Maps/{{SelectedWarpMap}}`）。
⇒ 本语料里这条通道能救回 **0 条**。

⇒ 结论不变，但记清楚：**未来遇到改 `Maps/Farm_*` 的包，这条通道值得实现**。
