# 地图相关的“代码事件”全量清单，以及能否拿来救不可转换补丁

日期：2026-10-23
依据：`D:\tmp\sdv-src`（1.6.15 反编译）、`D:\tmp\full-v6`（本轮 232 包全量产出与报告）
前置：`2026-10-06-map-mechanisms-panorama.md`、`2026-10-13-runtime-conditioned-map-edits.md`

## 0. 结论

1. **原版真正改地图的代码很多，但它们几乎全是按地点写死的 C#**：
   `Town.showImprovedPamHouse()`、`FarmHouse` 装修、`Beach`/`VolcanoDungeon`/`MineShaft`
   的随机或节日换图、`ApplyMapOverride` 的调用者……没有一个是“数据资产里填一个条件就能触发”的。
2. **能从数据/GSQ 驱动的落点仍然只有那几类**：
   `ConditionalDoor <GSQ>`、`BuildConditions <GSQ>`、`LockedDoorWarp`（时间+好感）、
   `MagicWarp`（单个 mail）、`Data/AdditionalRenovations`（仅 FarmHouse）、
   `DayTiles`/`NightTiles`（固定昼/夜）、`PassiveFestivals.MapReplacements`（按日期整地点换）。
3. 本轮 772 条未转换的 `EditMap`（591 `LocalEdit` + 181 `NeedsReview`）里，
   按补丁体分：`FromFile` 303、`MapTiles` 237、`TextOperations` 138、`MapProperties` 88、
   `AddWarps/AddNpcWarps` 15。**绝大多数是结构性换图/逐格换图**，
   上述原生条件落点表达不了。
4. 真正可能被救回的只有**极少数传送/门类**，而且往往还要牺牲“多条件 OR / 事件条件 / 天气时间”；
   **不建议为此把大面积 `LocalEdit` 强行改成近似转换**。

## 1. C# 侧地图修改入口（反编译扫描）

扫描规则：`ApplyMapOverride(`、`setMapTile(`、`removeTile(`、`.TileIndex +=/=`、
`.Tiles[...] =`、`MakeMapModifications(`。按类统计（次数为出现点，不是不同地图）：

| 机制 | 典型类（次数） | 语义 | 数据可达 |
|---|---|---|---|
| `ApplyMapOverride` | FarmHouse 21、GameLocation 11、Forest 7、Town 4、Beach 4、IslandWest 4、VolcanoDungeon 4 | 运行时把一张**独立覆盖地图**按矩形/整图贴到当前地图 | ❌ 只有 C# 调用；没有 `Data/*` 条件槽 |
| `setMapTile` | VolcanoDungeon 28、MineShaft 26、MovieTheater 26、Town 24、IslandSouthEast 20、GameLocation 13、Beach 11 | 直接改某层某格 `TileIndex`/属性 | ❌ 调用者全是地点类硬编码 |
| `removeTile` | Town 32、VolcanoDungeon 14、Beach 12、IslandActivityAssignments 8、Railroad 6 | 删某层某格 | ❌ 同上 |
| `.TileIndex +=` / `=` | Town 11、GameLocation 5、Woods 4、DefaultCommands 1 | 原地换图块（典型：Pam 房子 `+842`，社区中心 `+12`） | ❌ 由 mail/节日/随机数硬编码分支 |
| `.Tiles[...] =` | GameLocation 12、Mountain 6、InteriorDoor 3、CommunityCenter 2、Town 2 | 直接替换图块对象 | ❌ |
| `MakeMapModifications` 重写 | FarmHouse、Town、Forest、Beach、BeachNightMarket、Mountain、Railroad、Sewer、Woods、WizardHouse、Island*、ManorHouse、CommunityCenter 等约 20 个地点类 | 每次进图按存档状态重放地图改动 | ❌ 状态判断写死在 C# |

典型实例：

- `Town.showImprovedPamHouse()`：`Buildings` 矩形 `(69,66,8,3)` 的 `TileIndex += 842`，
  `Front` 上半同样 `+842`，`AlwaysFront` 补屋顶，再 `removeTile` 三格。
  触发条件：`mailReceived.Contains("pamHouseUpgrade")`。
- `Town.MakeMapModifications()`：`trashBearDone` 时
  `ApplyMapOverride("Town-TrashGone", null, (57,68,17,5))`、
  `ApplyMapOverride("Town-DogHouse", null, (51,65,5,6))`，再删若干垃圾图块。
- `FarmHouse`：`ApplyMapOverride("FarmHouse_*")` 21 处，全是装修/儿童床/地窖等硬编码 mail。
- `Beach`/`VolcanoDungeon`/`MineShaft`/`MovieTheater`：随机或节日替换图块/整图。

> 结论：这些代码**必须保留其运行效果**（产出树里的覆盖地图、原图集索引、mail/world state 都要在），
> 但**不能拿来当 CP 条件的通用落点**——它们没有“从数据读一个 GSQ 再改图”的入口。

## 2. 数据 / 资产侧的地图机制

| 机制 | 条件能力 | 用途 | 备注 |
|---|---|---|---|
| `ConditionalDoor <GSQ>`（图块 `Action`） | **任意 GSQ** | 条件成立才打开**室内门**（`openDoor` 查 `interiorDoors`） | 不是任意 warp/换图；只对门类语义有效 |
| `BuildConditions`（地图属性） | **任意 GSQ** | 能否在该地点建造 | 只影响建造判定 |
| `LockedDoorWarp`（图块 `Action`） | 时间窗 + NPC 好感 | 条件传送 | 不支持事件/天气/日期 |
| `MagicWarp <loc> <x> <y> [mail]` | **单个 mail flag** | 条件传送（带特效） | 不支持 GSQ / 多 flag OR |
| `Data/AdditionalRenovations`（地图属性） | mail flag | mail 成立时贴 `map_name`，否则贴 `map_name2` | **只有 FarmHouse 会读** |
| `DayTiles` / `NightTiles`（地图属性） | 固定昼/夜 | 固定时间替换指定格 | 不能表达任意 `Time` 区间 |
| `Data/Locations.CreateOnLoad.MapPath` | 构建时 CP `When` 展开 | 整张地点地图选择 | 运行时无 GSQ 选择 |
| `PassiveFestivals.MapReplacements` | 日期 | **整地点**替换 | 不是单张地图的 EditMap |
| `Data/Buildings.TileProperties` + `Skins[].Condition` | GSQ | 给**建筑图块**注入属性 | 不能改地形/地图瓦片 |
| `Data/ChairTiles` | 无 | 给图块加座椅行为 | 不是换图 |
| 事件命令 `ChangeMapTile` / `RemoveTile` | 事件前置 `G <GSQ>` | 事件期间改当前地图内存 | **一次性/非持久**，换图/重进即丢 |
| `ChangeToTemporaryMap` | 事件 | 临时地点 | 不改原图 |

## 3. 本轮未转换地图补丁的构成

`full-v6` 报告里 `EditMap` 且 verdict 为 `LocalEdit` 或 `NeedsReview`：

```text
合计 772
  LocalEdit    591
  NeedsReview  181
```

按补丁体（可组合）：

| 补丁体 | 条数 |
|---|---:|
| `FromFile`（区域换图） | 303 |
| `MapTiles`（逐格改图） | 237 |
| `TextOperations`（牌子文字） | 138 |
| `MapProperties` | 88 |
| `AddWarps` / `AddNpcWarps` | 15 |

`MapTiles` 的 `SetProperties` 键分布：

```text
CustomCompanions 300   ← DLL 框架生成动物；纯 XNB 无对应
Action            41
mushymato.MMAP_TAS 17  ← 另一个 DLL 图块动作
NoSpawn            7
TouchAction        5
Remove             2
```

`Action`/`TouchAction` 的值分布（46 条）：

```text
Message 14、Warp 10、OpenShop 7、JojaShop 4、MagicWarp 2、
LockedDoorWarp 2、PlayEvent 1、MinecartTransport 1、其它 5
```

`MapProperties` 里真正有原生条件语义的 `BuildConditions` 为 **0**；
`DayTiles` 9 / `NightTiles` 10 是时间昼/夜机制，不等价于任意 `Time` 条件。

## 4. 能不能救？

| 现有不可转换类别 | 可用的原生机制 | 结论 |
|---|---|---|
| `FromFile` 结构换图（303） | 无 | ❌ 保持 |
| `MapTiles` 换 `SetIndex`/`Remove`（大部分） | 无 | ❌ 保持 |
| `MapTiles` 设 `Action=Warp`（10） | `MagicWarp`（单 mail）/`LockedDoorWarp`（时间+好感） | ⚠️ 只有极少数能近似；事件/天气/多 flag 不行 |
| `MapTiles` 设 `Action=Message/OpenShop/...` | 无原生“条件动作” | ❌ 条件会丢 |
| `MapTiles` 设 `CustomCompanions`（300） | 无（CC 是 DLL 框架） | ❌ 不能用地图机制补 |
| `MapTiles` 设 `MMAP_TAS`（17） | 无（Mushymato DLL） | ❌ |
| `AddWarps`（15） | `MagicWarp` / `LockedDoorWarp` | ⚠️ 仅个别 `HasFlag` + 有格子可挂；`-1` 边缘 warp、天气/日期/多 flag 不行 |
| `MapProperties` 的 `DayTiles/NightTiles`（19） | `DayTiles/NightTiles` | ⚠️ 只有条件本来就是昼夜时才有意义；本轮条件多为 `Time/Weather` |
| `TextOperations`（138） | 无 | ❌ 只能固化一条文案，条件丢失 |
| `FarmHouse` 的 mail 装修 | `Data/AdditionalRenovations` | 本轮匹配 **0** 条 |

### 具体候选（人工核过）

- `AddWarps` 15 条里，条件较接近原生的是：
  - `Cape Stardew` 的 `HasFlag ccVault/jojaVault`（两个并列，不是单 mail）
  - SVE 的 `HasFlag Farm_Eternal`（单 mail，源格存在）
  - Sunberry 的 `HasFlag: anyPlayer ...`（不是本地玩家 mail）
  - 其余含 `Day`、`Weather`、`DayOfWeek`、`-1` 边缘坐标
  → 严格无损可用的基本为 **0～2 条**，且需要额外放 `TouchAction=MagicWarp` 的格子，语义从“踩到即传送”变成“踩上触发魔法传送”，不是逐字节等价。
- `Action=Warp` 10 条里：
  - `Lnh` 的 `HasMod + UnlockTheWoods`、Cape 的 `SecretCaveKeyUnlocked` 等是**配置/DynamicToken**，不是新的地图机制问题；如果令牌能静态求值，直接按普通 `Warp` 烘即可（属于 token 解析改进，不属于本节“代码事件复用”）。
  - `TE` 的 `HasSeenEvent`、SVE 的 `SelectedWarpMap` 仍然不能用 `MagicWarp` 表达。
- `ConditionalDoor`：本轮没有匹配到“GSQ 门”类的未转换补丁（0）。
- `BuildConditions`：0。
- `AdditionalRenovations`：目标不是 FarmHouse（0）。

## 5. 建议

1. **保持现状**：不把 `LocalEdit`/`NeedsReview` 大面积改成近似转换。
2. 只做两类**明确无损**的补充：
   - 继续完善 **静态 token / 配置字段求值**（例如 `UnlockTheWoods`、`SelectedWarpMap` 这类），把“其实已经确定”的 `Warp`/`MagicWarp` 补丁按普通地图改动写入；
   - 对 `AddWarps` 中**单一 mail flag + 非 -1 源格 + 无条件 OR** 的极少数，才考虑 `MagicWarp` 近似，并在报告里注明“触发方式从地图 warp 变为踩格施法”。
3. **不要**把 `ApplyMapOverride`、`showImprovedPamHouse`、`setMapTile/removeTile` 当作通用条件槽：它们没有数据入口；我们能做的是保证这些代码运行所需的**基础地图、覆盖地图、图集索引、mail/world state 资产**都在产出树里。
4. 对 `CustomCompanions`（300 条）和 `MMAP_TAS`（17 条），不要用地图机制硬凑；它们是 DLL 运行时行为，纯 XNB 下保持“不转换”比生成错误实体更安全。
