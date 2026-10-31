# 地图的条件承载能力：精算

日期：2026-10-06
依据：`D:\sdv_src`（1.6.15 反编译，只读）、`D:\sdv_tree\Maps\*.tbin`（945 张原版地图实物，只读）、
69 份 `report.json` 语料实测（工具见 §6）
目的：**确证/证伪**发射表里 `(地图, 季节)` 与 `(地图, 看过的剧情)` 两格的判定。

---

## 0. 三句话

1. **地图里能承载「条件表达式」的位点只有 2 个**：图块 `Action`/`TouchAction` 的 `ConditionalDoor <GSQ>`，
   和地图属性 `BuildConditions`。**两者都只接受完整 GSQ**——`HasSeenEvent` / 季节 / 年份全都在 GSQ 词汇表里。
2. **但它们都改不了图块内容**：一个只能开门/挡路，一个只能决定「这里能不能盖房子」。
   其余「像条件位」的地图属性（`DayTiles` / `NightTiles` / `Music` / `<季节>_Objects` / `AdditionalRenovations`）
   **语法写死在 C#，数据填不进去**。
3. **两条既有判定都要动**：
   - `(地图, 季节) = Native（tilesheet 前缀）`——机制**是真的**，「仅室外」也**证实**，
     但它只表达「同索引换贴图」，**语料里命中率 ≈ 0**（季节 × 地图的 MapTiles **169 条全部是 `SetProperties`**，
     没有一条 `SetTilesheet`/`SetIndex`）。**判轻了。**
   - `(地图, 看过的剧情) = None（无通用机制）`——**是错的**。`ConditionalDoor PLAYER_HAS_SEEN_EVENT` 与
     `BuildConditions` 是实打实的 GSQ 位；正确档位是 **CapacityExceeded（装不下）**。
     `EmitterTable.cs:105-108` 的 `CapacityExceeded` 判定**正确**，`tools/variant-entropy.py` 的 `None` **应改**。

---

## 1. ★ 地图条件位清单（全部）

「位点」= 游戏会把它当**条件**去求值的位置。按「谁求值」分类：

| # | 位点 | 写在哪个文件 | 解析入口 | 条件语法 | 表达力 |
|---|---|---|---|---|---|
| **1** | 图块 `Action` = `ConditionalDoor <GSQ>` | 地图 `.tbin`：`Buildings` 层图块的 `Action` 属性 | `GameLocation.cs:9546-9563`（`performAction` 的 `case "ConditionalDoor"`）<br>取属性：`GameLocation.cs:7867-7887`（先 `PickTile` 实例属性，再 `doesTileHaveProperty`） | **完整 GSQ 全文**（`ArgUtility.UnsplitQuoteAware(action, ' ', 1)`，支持引号） | 只能「开门」+「弹提示」 |
| **2** | 图块 `TouchAction` = `ConditionalDoor <GSQ>` | 同上：`Back` 层图块的 `TouchAction` 属性 | `GameLocation.cs:3907-3936`（`performTouchAction` 的 `case 15`）<br>取属性与触发：`GameLocation.cs:4221-4229` | **完整 GSQ 全文** | 只能「挡路 / 放行」 |
| **3** | 地图属性 `BuildConditions` | 地图 `.tbin` 的 `Properties` | `GameLocation.cs:2469-2470`（**唯一读取点**，只在 `IsBuildableLocation()` 里） | **完整 GSQ 全文** | 只能决定「此地可否建造」 |
| **4** | 地图属性 `AdditionalRenovations` | 地图 `Properties` | `FarmHouse.cs:1188-1233` | 自有语法 `<id> <mailFlag> <叠加图> <还原图> [x y w h]`，**条件 = mail 标志**，硬编码 | 整块叠加/还原图，**只在 `FarmHouse`** |
| **5** | 地图属性 `DayTiles` / `NightTiles` | 地图 `Properties` | `GameLocation.cs:7048-7060`（进入时）/ `:5274-5290`（`addLightGlows`）/ `:3083-3104`（`switchOutNightTiles`，离去时） | 硬编码：**时刻 + 是否下雨 + `pamHouseUpgrade`** | 切图块索引 |
| **6** | 地图属性 `Music` | 地图 `Properties` | `GameLocation.cs:3428-3445` | 硬编码：`<起始时刻> <结束时刻> <音乐id>` | 只换 BGM |
| **7** | 地图属性 `<season>_Objects`（如 `spring_Objects`） | 地图 `Properties` | `GameLocation.cs:17293`（`HasMapPropertyWithValue(GetSeason().ToString() + "_Objects")`） | **属性名携带季节**；只要存在且值非假，就调 `spawnObjects()` | 只控制「要不要撒觅食物」 |
| **8** | 图块 `Action` 的**隐式**条件 | 地图图块属性 | `GameLocation.cs:9536+`（`LockedDoorWarp` / `Door` / `OpenShop` / `MagicWarp` …） | **写死在 C#**，数据只能填参数，不能填条件 | 各一个固定条件（时间窗 / 2 心 / mail） |
| **9** | tilesheet 的 `@TileIndex@N@Action` | `.tbin` 的 `TileSheet.Properties` | `GameLocation.cs:13185`（`doesTileHaveProperty` 的回退链最后一环） | 与 #1/#2 同一个 switch | 同 #1/#2，但**按图块号整片生效** |
| **10** | 建筑注入的 tile 属性 | `Data/Buildings`（模型不在反推树里） | `Building.cs:2419-2432`（`BuildingData.HasPropertyAtTile`）→ 被 `GameLocation.cs:13153-13164` 优先遍历 | **无条件求值**，只是属性值 | 不含条件，但**优先级最高** |

### 1.1 明确**不是**条件位（澄清，避免再猜）

| 东西 | 结论 | 依据 |
|---|---|---|
| **`Layer.Properties`（图层属性）** | **全库零读取** | `grep "ayer\.Properties" *.cs` → 0 命中 |
| 地图属性 `Warp` / `NPCWarp` | **恰好 5 字段** `fromX fromY toMap toX toY`，**无第 6 个条件字段** | `GameLocation.cs:17147-17169` |
| 地图属性 `Doors` | 每门 4 字段，**无条件** | `InteriorDoorDictionary.cs:131-145` |
| `Data/TriggerActions` | **26 个动作，无一改地图** | `TriggerActionManager.cs:21-363`（名字 = 方法名，反射注册于 `:660-670`） |
| `Trigger` 地图属性 | **不存在**（源码里没有任何读取） | 全量地图属性键扫描（§6） |
| `ApplyMapOverride` | **纯 C# API**；数据侧唯一入口就是 #4 | 全部调用点逐个读过（`GameLocation.cs:1664/1833/1842` + 各 Location 子类），**只有 `FarmHouse.cs:1228/1232` 由数据驱动** |
| `Data/HomeRenovations` | 需求只有 `Type: "Value"`（写死的 `cribStyle` 一类键）与 `Type: "Mail"`，**不是 GSQ** | `HouseRenovation.cs:201-231` |
| 建筑皮肤 `Skins[].Condition` | **是 GSQ**（`Building.cs:240`），但它选的是**建筑外观**，不改地图文件 | 同上 |
| 事件命令 `RemoveTile` / `ChangeMapTile` / `ChangeToTemporaryMap` | 只改内存，且是**一次性**（`eventsSeen`），非每次进图求值 | 见 `2026-10-06-map-mechanisms-panorama.md` §4 |

### 1.2 「条件表达式支持到什么程度」= **完整 GSQ**

不是「少数硬编码字面量」——**#1/#2/#3 三个位点收的都是 `GameStateQuery.CheckConditions` 的全文**。
`GameStateQuery.cs` 里注册的 resolver **100+ 个**（`GameStateQuery.cs:253-1928`），与本任务四维度直接相关的：

| resolver | 行号 | 语义 |
|---|---|---|
| `PLAYER_HAS_SEEN_EVENT` | `GameStateQuery.cs:1210` | 看过的剧情（本维度） |
| `SEASON` | `GameStateQuery.cs:394` | 季节 |
| `LOCATION_SEASON` | `GameStateQuery.cs:740` | 地点上下文季节（温室等） |
| `SEASON_DAY` / `DATE_RANGE` / `DAY_OF_MONTH` | `:276` / `:259` / `:293` | 季-日 / 日期区间 / 日 |
| **`YEAR <min> [max]`** | **`GameStateQuery.cs:411-424`** | **年份** |
| `PLAYER_NPC_RELATIONSHIP` | `GameStateQuery.cs:1529` | 婚姻 / 恋爱 |
| `ANY` / `TRUE` / `FALSE` | `:253` / `:1921` / `:1928` | 组合 |

> **所以「条件维度」本身不是瓶颈——瓶颈是「位点太少、且都改不了图块内容」。**

### 1.3 原版实物用量（945 张 `.tbin` 全扫）

| 属性/值 | 出现的原版地图 |
|---|---|
| `ConditionalDoor` | **只有 `Maps/AdventureGuild.tbin` 1 张，共 2 处**（`ConditionalDoor PLAYER_STAT Current monstersKilled 1000`）|
| `BuildConditions` | **0 张** |
| `AdditionalRenovations` | **0 张**（代码路径存在但原版没人用）|
| `DayTiles` / `NightTiles` | 各 40 张 |
| `TreatAsOutdoors` | 3 张（BugLand / IslandNorthCave1 / Island_CaptainRoom）|
| `forceLoadObjects` | 2 张（Caldera / Island_CaptainRoom）|
| 带 `Outdoors` 属性 | **71 张**（含节日变体图） |

> **原版自己几乎不用这些位点。** 这就是为什么 `(地图, ×)` 的每一格都「有机制、但覆盖不了语料」。

---

## 2. `ConditionalDoor` 的完整语义

入口有两处，**语义不同**，必须分开说。

### 2.1 `Action`（点击）— `GameLocation.cs:9546-9563`

```csharp
case "ConditionalDoor":
    if (action.Length > 1 && !Game1.eventUp) {
        if (GameStateQuery.CheckConditions(ArgUtility.UnsplitQuoteAware(action, ' ', 1))) {
            openDoor(tileLocation, playSound: true);   // 真 → 开门
            return true;
        }
        string text7 = doesTileHaveProperty(tileLocation.X, tileLocation.Y, "LockedDoorMessage", "Buildings");
        ... // 假 → 弹 LockedDoorMessage，没有就弹 Strings\Locations:LockedDoor
    }
```

**`openDoor` 的真实边界**（`GameLocation.cs:8221-8248`）：

```csharp
public void openDoor(Location tileLocation, bool playSound) {
    ...
    if (!interiorDoors.ContainsKey(key)) return;   // ← 不是门 → 直接返回
    interiorDoors[key] = true;
    ...playSound...
}
```

而 `interiorDoors` 的键来自**地图属性 `Doors`**，且：

```csharp
// InteriorDoorDictionary.cs:99-109
public void ResetSharedState() {
    if (location.isOutdoors.Value) return;         // ← 室外地点直接不登记
    foreach (Point item in GetDoorTilesFromMapProperty(location)) base[item] = false;
}
```

> **结论（Action 版）**：适用对象 **= 室内地点 + 被 `Doors` 属性登记过的格子**。
> 挂到别的格子上：条件真 → **静默什么也不发生**；条件假 → 弹提示。
> 所以它**实质上只对门有意义**，且**只在室内**。

### 2.2 `TouchAction`（踩上）— `GameLocation.cs:3907-3936`

```csharp
case 15:   // "ConditionalDoor".Length == 15
    if (!(value == "ConditionalDoor") || action.Length <= 1 || Game1.eventUp
        || GameStateQuery.CheckConditions(ArgUtility.UnsplitQuoteAware(action, ' ', 1)))
        break;                                  // 真 → 什么也不做（放行）
    Game1.player.Position -= Game1.player.getMostRecentMovementVector() * 2f;   // 假 → 顶回去
    Game1.player.yVelocity = 0f; Game1.player.Halt();
    ...
    string text = doesTileHaveProperty(..., "LockedDoorMessage", "Back");
    ... 弹提示
```

> **结论（TouchAction 版）**：**与门完全无关**。任何 `Back` 层的图块都能挂，效果是
> 「GSQ 为假时把**本地玩家**顶回去 2px 并弹提示」。它不设通行位、不影响 NPC、不影响寻路。

### 2.3 逐条回答

| 问题 | 答案 | 依据 |
|---|---|---|
| 适用对象？只有门？任何 tile？ | **Action 版 = 只有门（且必须室内 + `Doors` 登记）**；**TouchAction 版 = 任何 tile** | `:8221-8248` / `InteriorDoorDictionary.cs:99-127` / `:3907-3936` |
| 条件支持多复杂？ | **完整 GSQ**，无长度/项数限制（`UnsplitQuoteAware` 只切第一个空格） | `:9549` / `:3907` |
| 能承载 `HasSeenEvent`？ | **能**（`PLAYER_HAS_SEEN_EVENT`） | `GameStateQuery.cs:1210` |
| 能承载季节？ | **能**（`SEASON` / `LOCATION_SEASON`） | `:394` / `:740` |
| 能承载年份？ | **能**（`YEAR <min> [max]`） | `:411-424` |
| 能承载婚姻？ | **能**（`PLAYER_NPC_RELATIONSHIP <npc> Married`） | `:1529` |
| **能不能「让图块按条件存在」？** | **不能。** 它只能改「这一个格子的行为」，改不了图长什么样 | 全 `.tbin` 里 `ConditionalDoor` 只出现 2 处 |

> **一句话**：`ConditionalDoor` 是**完整 GSQ + 极窄效果**——条件侧满分，效果侧只有一个 bit。
> 说它「只对门生效」**对 Action 版成立、对 TouchAction 版不成立**；
> 但说它「装不下按条件换图块内容」**两版都成立**。

---

## 3. 季节 × 地图

### 3.1 那处到底是不是「tilesheet 前缀」机制？**是。**

```csharp
// GameLocation.cs:16411-16435
public virtual void updateSeasonalTileSheets(Map map = null) {
    if (map == null) map = Map;
    if (!(this is Summit) && (!IsOutdoors || Name.Equals("Desert"))) return;   // :16417
    map.DisposeTileSheets(Game1.mapDisplayDevice);
    foreach (TileSheet tileSheet in map.TileSheets) {
        tileSheet.ImageSource = GetSeasonalTilesheetName(tileSheet.ImageSource, GetSeasonKey());
        ...
    }
}

// GameLocation.cs:16439-16447
public static string GetSeasonalTilesheetName(string sheet_path, string current_season) {
    string fileName = Path.GetFileName(sheet_path);
    if (fileName.StartsWith("spring_") || fileName.StartsWith("summer_")
     || fileName.StartsWith("fall_")   || fileName.StartsWith("winter_"))
        sheet_path = Path.Combine(Path.GetDirectoryName(sheet_path),
                                  current_season + fileName.Substring(fileName.IndexOf('_')));
    return sheet_path;
}
```

**它做的事**：把地图 `TileSheets` 里每个 `ImageSource` 的 `spring_` 前缀换成当季前缀，**只换图集文件，不换任何 `TileIndex`**。

### 3.2 只在室内生效吗？——**「仅室外」证实**

早退条件 `:16417` 展开：

- 若 `this is Summit` → **总是继续**（无论室内外）
- 否则若 `!IsOutdoors` **或** `Name == "Desert"` → **直接返回**

核对：

| 情形 | 是否生效 | 补充 |
|---|---|---|
| 非 Summit + 室外 + 非 Desert | ✅ | 原版 71 张带 `Outdoors` 的图 |
| 非 Summit + **室内** | ❌ | 原版 874 张室内图**全不生效** |
| `Desert` | ❌ | 即使 `Desert.tbin` **带 `Outdoors` 属性**，也被名字排除 |
| `Summit` | ✅（豁免） | 但 `Summit.tbin` 本身就带 `Outdoors`，**所以这个豁免对原版是冗余的** |
| 所有 `IslandLocation` 子类 | ❌ | **`IslandLocation.updateSeasonalTileSheets` 被覆写成空**（`IslandLocation.cs:437-439`）——岛图永远没有季节图集 |

> **`IsOutdoors` 的取值来源**：地图属性 `Outdoors`（`GameLocation.cs:1957-1960`）；
> 另有 `Woods.cs:48` 在构造函数里硬置 `true`。

**结论：表里注的「仅室外」——证实。** 唯二例外是 `Summit`（冗余）与 `Desert`（被排除）。

### 3.3 258 条里有多少是室内？

**方法**：扫 69 份 `report.json`，取 `target` 以 `Maps/` 开头的补丁，
读其源 CP 文件的 `Changes[i].When`，用正则判定维度；再用「该图 `.tbin` 是否含 `Outdoors` 属性」判室内外。

| 口径 | 条数 |
|---|---|
| 季节 × `Maps/*`，**原始补丁条数** | **318** |
| 同上去重（`target`+`When`+`action`） | 216 |
| ├ 打到**原版室外**图 | **137** |
| ├ 打到**原版室内**图 | **5** |
| └ 打到 mod 自建图（室内外未知） | 176 |

**（5 条室内明细）**：`Maps/AnimalShop` 1、`Maps/ElliottHouse` 1、`Maps/SebastianRoom` 1、`Maps/SamHouse` 1、`Maps/SkullCave` 1。

> 按文档里 258 的口径换算：**室内占比 ≈ 1.6%**。
> **「季节 × 地图几乎全是室外图」——成立。**
> （258/266/61/47 这几个数与本次复现的 318/513/76/92 有约 ±20% 差，是语料快照/去重口径不同；结论不受影响，见 §6。）

### 3.4 ⚠️ 但机制对不上语料（这才是要改的地方）

季节 × `Maps/*` 的 `EditMap` **形态**：

| 形态 | 条数（原始） |
|---|---|
| `MapTiles` | 95 |
| `FromFile`（+ `ToArea` / `FromArea` / `PatchMode`） | 107 |
| `MapProperties` | 9 |
| `TextOperations` | 5 |

**关键**：全部 **169 个 `MapTiles` 条目**里，字段只有 `Position` / `Layer` / **`SetProperties`**——
**`SetTilesheet` 与 `SetIndex` 一条都没有**。典型样本：

```jsonc
{ "Position": {"X":41,"Y":19}, "Layer":"Buildings", "SetProperties": {"Remove": "true"} }
{ "Position": {"X":41,"Y":19}, "Layer":"Buildings", "SetProperties": {"Action": "Door June"} }
{ "Position": {"X":40,"Y":23}, "Layer":"Back",      "SetProperties": {"CustomCompanions": null} }
```

也就是说：**语料的「季节 × 地图」= 改图块属性 / 叠加一块图 / 改地图属性**，
没有一条是「同索引换贴图」。

> **判定**：`(地图, 季节) = Native("tilesheet 前缀")` **机制为真、但档位判轻了**。
> 建议改为 **`CapacityExceeded`**：`NativeForm` 保留（它确实是唯一的季节原生位），
> `Evidence` 补上「仅室外（`GameLocation.cs:16417`）」「仅同索引换贴图」「语料 MapTiles 169/169 为 SetProperties，命中 0」。
> **「仅室外」这一半：证实。**

---

## 4. 年份 × 地图

### 4.1 原版有没有「按年份分叉地图」的机制？**没有。**

- `GameLocation.cs` 里 `Game1.year` 的全部用法（`:3717` / `:9579` / `:10348` / `:11039` / `:11066` / `:11586` / `:11620`）
  **全是写死的 C# 逻辑**：绿雨第一年、宠物领养第 2 年、鱿鱼节计分——**没有一处读地图属性或图块**。
- **没有任何地图属性键**含年份语义（§6 的全量键扫描：`GetMapProperty*("...")` 全部键中无 year 相关）。
- 地图**资产名**也没有年份后缀约定（节日图是 `-EggFestival` 这类**节日**后缀，不是年份）。

### 4.2 部分可行的替代——**有，但只有两个退化位**

| 替代 | 可行度 | 依据 |
|---|---|---|
| **`ConditionalDoor` + GSQ `YEAR <min> [max]`** | ✅ 语法上完全可行 | `GameLocation.cs:9549` / `:3907` + `GameStateQuery.cs:411-424` |
| **`BuildConditions` + `YEAR`** | ✅ 可行，但只控「可否建造」 | `GameLocation.cs:2470` |
| `Data/Festivals` 的 `<key>_yN` | ❌ **不是地图**（是节日键） | `Event.cs:4417-4448` |
| 事件命令 `ChangeMapTile` + `YEAR` 前置 | ❌ 内存 + 一次性 | panorama §4 |

### 4.3 语料

| | 条数 |
|---|---|
| 年份 × `Maps/*`，原始 | **76** |
| 去重 | 59 |
| ├ 原版室外 | 55 |
| ├ **原版室内** | **1**（`Maps/SamHouse`） |
| └ mod 自建 | 20 |

形态：`EditMap` 55 / `Load` 4；`MapTiles` 42、`FromFile*` 13。

> **判定：`(地图, 年份)` = 真无（对「地图内容按年份分叉」）。**
> 机制侧一格都没有；只有 `ConditionalDoor` / `BuildConditions` 两个点能让 `YEAR` 生效，
> 且**都不改变图块内容**。语料 61/76 条**没有可烘焙的等价物**。
> 这一格此前在表里是 `Unknown`——**现在可以落 `None`**（附「`YEAR` GSQ 只在两个退化位可用」的备注）。

---

## 5. ★ 维度 × 地图 → 结论 / 条数

**原始条数 = 未去重的补丁实例数（与 `variant-per-type.py` 同口径）；去重 = `target`+`When`+`action` 唯一。**

| 维度 | 原版最简形式 | 档位 | 原始条数 | 去重 | 室内 | 室内占比 | 语料形态是否被覆盖 |
|---|---|---|---|---|---|---|---|
| **看过的剧情** | `ConditionalDoor PLAYER_HAS_SEEN_EVENT <id>`（tile，室内门/任意 tile 挡路）<br>`BuildConditions PLAYER_HAS_SEEN_EVENT <id>`（地图属性，只控建造） | **装不下**<br>（`CapacityExceeded`） | **513** | 212 | **53** | 10.3% | ❌ 主要形态是 `FromFile` 叠加（172）、`MapTiles`（24）、`Load`（26） |
| **季节** | tilesheet `spring_/summer_/fall_/winter_` 前缀<br>（**仅室外**，且**仅同索引换贴图**） | **装不下**<br>（机制为真、覆盖 ≈ 0） | **318** | 216 | **5** | 1.6% | ❌ `MapTiles` 169/169 = `SetProperties`；`FromFile` 107 |
| **年份** | **无**（只有 `ConditionalDoor` / `BuildConditions` 能塞 GSQ `YEAR`，都不改图块） | **真无**（`None`） | **76** | 59 | 1 | 1.3% | ❌ |
| **婚姻** | 同「看过的剧情」两个退化位（`PLAYER_NPC_RELATIONSHIP <npc> Married`） | **装不下**<br>（`CapacityExceeded`） | **92** | 41 | **18** | **19.6%** | ❌ 主要形态是 `FromFile` 叠加（33） |

### 5.1 逐格结论

- **看过的剧情 × 地图**：**推翻「无」**。原版**有** GSQ 位（两处），实物证据 `Maps/AdventureGuild.tbin`；
  但承载不了「按剧情换图」。→ `CapacityExceeded`（与 `EmitterTable.cs:105-108` 一致）。
- **季节 × 地图**：**判定须降级**。`Native` 的机制**确证存在**且**仅室外**，
  但它只换图集；语料 318 条里能落进去的**趋近 0**。→ `CapacityExceeded`。
- **年份 × 地图**：**判「无」正确**。补充：`YEAR` GSQ 可用，但只在两个退化位。
- **婚姻 × 地图**：**判「装不下」**。注意它**室内占比最高（19.6%，14 张不同室内图）**——
  恰好 `ConditionalDoor`(Action) 也只对室内门生效，**这一格是四格里唯一「机制位置与语料位置部分重合」的**，
  但「重合的只是门」这一点重合量仍远不足以覆盖 41 条去重。→ `CapacityExceeded`。

### 5.2 对发射表的建议改动（供后续）

| 格子 | 现在 | 建议 |
|---|---|---|
| `(Map, Season)` | `Native` + 「仅室外已确证」 | **`CapacityExceeded`**，`NativeForm` 保留、追加「仅同索引换贴图」；`Evidence` 加 `GameLocation.cs:16417` `:16442-16444` |
| `(Map, SeenEvent)` | `CapacityExceeded` + 「只对门的 Action/TouchAction 生效」 | 维持 `CapacityExceeded`，但**订正措辞**：Action 版限室内门（`InteriorDoorDictionary.cs:99-127`），**TouchAction 版对任意 tile 生效**（`:3907-3936`）|
| `(Map, Year)` | 未收录（`Unknown`） | **`None`**，备注「`YEAR` GSQ 仅在 `ConditionalDoor`/`BuildConditions` 可用」 |
| `(Map, Marriage)` | 未收录（`Unknown`） | **`CapacityExceeded`** |

---

## 6. 方法与可复现

| 步骤 | 做法 |
|---|---|
| 源码核实 | 在 `D:\sdv_src` 全量 `grep`：`GameStateQuery.CheckConditions`（120+ 命中，逐条判是否地图侧）、`getMapProperty*` 全族、`.Properties.ContainsKey("…")`、`ApplyMapOverride`、`RegisterAction` |
| 原版实物 | `D:\sdv_tree\Maps\*.tbin` **945 张**，按 `struct.pack('<i', len(key)) + key` 的**长度前缀**在字节流里定位属性键（避免 `TreatAsOutdoors` 误命中 `Outdoors`）|
| 室内外判定 | 该图 `.tbin` 是否含**地图属性 `Outdoors`**（`GameLocation.cs:1957-1960` 的唯一来源）→ 71 张室外 |
| 语料 | 69 份 `report.json`：`D:\sdv_out{1..5}`、`D:\tmp\corpus_b*\<pkg>\report.json`、`D:\tmp\corpus_b2`、`D:\*.report.json`；按 `report['pack'] + patch['source'] + patch['index']` 回读 CP 源 json 的 `Changes[i].When` |
| 工具 | `D:\SdvCpTool\tools\variant-per-type.py`（本次沿用其口径），另写一次性聚合脚本做室内外分类 |

**口径差异说明**：本文的原始条数（318/513/76/92）与
`2026-10-06-variant-normalizer-design.md` §9.1 的（258/266/61/47）**不一致**，
系语料快照与去重口径不同（本文未对同义 `When` 做跨包去重）。**两套数的结论方向一致**（均为「极少数室内」），
差异不影响判定。**如需绝对数对齐，须先统一语料快照。**

---

## 7. 不确定项（**未确证**，不得当结论用）

| 项 | 状态 |
|---|---|
| `Data/Buildings` 的 `BuildingData` / `TileProperties` / `HasPropertyAtTile` 字段名与形状 | **未确证**——`BuildingData` 类型**不在 `D:\sdv_src` 反推树里**（只有 `Building.cs:2422` 的调用点），`D:\sdv_gd` 里也没有该定义 |
| `Data/Locations` 的 `WarpData` 是否有 `Condition` 字段 | **未确证**——同上，类型不在树里。但**地图 `.tbin` 里的 `Warp` 属性确定无第 6 字段**（`GameLocation.cs:17157-17167`），这条与地图无关 |
| `Data/Buildings` 的 `Skins[].Condition`（`Building.cs:240`）能否改地图 | **未确证**——它选的是建筑外观；是否连带改 tile 属性未读透 |
| 176 条打在 mod 自建图上的「季节 × 地图」的室内外分布 | **未统计**——需先解析各 mod 自带的 `.tbin`/`.tmx` |
| `Data/HomeRenovations` 的 `Type: "Value"` 支持哪些 `Key` | **未读完**——`HouseRenovation.cs:201-231` 只确认有 `Value`/`Mail` 两型 |
| 「258 → 室内 5」这一换算 | **口径近似**——分母用的是本次复现的 318，非原文的 258 |
