# FTM（Farm Type Manager）到底在做什么，以及哪些**能**落成原版 XNB

日期：2026-10-11（接 `2026-10-11-non-cp-frameworks-what-can-bake.md` 的订正）

⚠ **订正上一份的结论**：那份写「FTM 是运行时行为 ⇒ 不值得」，**下得太快**。
把 4 个包的 `content.json` 逐字段挖开之后：**觅食与埋藏物这两类共约 1218 条有原版落点**
（`Data/Locations.<loc>.Forage` / `.ArtifactSpots`），只有**刷怪 / 刷矿 / 大件**三类真的没有。

---

## 1. 4 个 FTM 包声明了什么（同一套 10 个顶层键）

`Mods/` 里 4 个 FTM 包（步行去沙漠 / lnh额外区域 / **SVE[FTM]（875 KB）** / 女巫沼泽扩展）
结构完全一样：

```
ForageSpawnEnabled / Forage_Spawn_Settings.Areas
LargeObjectSpawnEnabled / Large_Object_Spawn_Settings.Areas
OreSpawnEnabled / Ore_Spawn_Settings.Areas
MonsterSpawnEnabled / Monster_Spawn_Settings.Areas
QuarryTileIndex / File_Conditions
```

**一个 area 长这样**（SVE 的「钻石魔杖宝箱」）：

```json
{ "UniqueAreaID": "Diamond Wand Chest", "MapName": "Custom_HighlandsOutpost",
  "SpringItemIndex": [{ "Category": "Chest", "Contents": [{ "Category": "Weapon", "Name": "Diamond Wand" }] }],
  "MinimumSpawnsPerDay": 1, "MaximumSpawnsPerDay": 1,
  "IncludeCoordinates": ["12,6;12,6"],            // "x,y;w,h" 矩形
  "StrictTileChecking": "None",
  "SpawnTiming": { "StartTime": 600, "EndTime": 600, … },
  "ExtraConditions": { "Seasons": [], "GameStateQueries": [], "CPConditions": { … } },
  "DaysUntilSpawnsExpire": 9001 }
```

## 2. SVE 的量（逐个字段数出来的）

| 段 | Areas | 涉及地图 |
|---|---|---|
| **Forage** | **116** | 34（产出树里 34/34 都有 `.tbin` ✓）|
| **Monster** | **246** | 17（`Custom_CrimsonBadlands` 60、`Custom_Highlands` 58…）|
| Ore | 9 | 3 |
| Large_Object | 12 | 7 |

**Forage 的 1250 条物品，按写法分**：

| 写法 | 条数 | 能不能落 |
|---|---|---|
| **原版物品下标**（`16` = 野山葵、`396` = 香味浆果…）| **429** | ✅ `Forage[].ItemId = "(O)16"` |
| **`Category: "Buried"` + `contents: ["FlashShifter.StardewValleyExpandedCP_Gold_Carrot_Seed"]`** | **649** | ✅ 就是**原版挖掘点**：`ArtifactSpots[].ItemId`（同一种限定 id 写法）|
| `str`（限定物品 id，如 `…ExpandedCP_Pear`）| **140** | ✅ 同上（SVE 自己的物品由它的 CP 包注册在 `Data/Objects`）|
| `Category: "None"` + `{category,name}`（小写键）| 32 | ⚠ 要先把 `{category,name}` 翻成限定 id |
| `Category: "Chest"` + `Contents` | 16 | ❌ 原版没有「按坐标放一个宝箱」的机制 |

⇒ **约 1218 / 1250 条可以落**。其余字段也有对应：`Minimum/MaximumSpawnsPerDay` ↔ 原版
`Data/Locations.<loc>.Min/MaxDailyForageSpawn`；`ExtraConditions.Seasons` ↔ `Forage[].Season`；
`ExtraConditions.GameStateQueries` ↔ `Forage[].Condition`。

## 3. 那三类为什么真的没有落点（这次是对着反编译核死的，不是"大概没有"）

- **刷怪**：原版**唯一的**室外刷怪入口是 `Farm.cs:722` 的 `tenMinuteUpdate` ——
  ① 只在**农场**（`Farm` 类）② 要存档级开关 `Game1.spawnMonstersAtNight`
  （农场类型 / 女巫小屋神龛开的）③ 怪物表**写死**（`ShadowBrute`/`Bat`…）。
  地图方面：`GameLocation.performAction` 的 ~120 个 `Action` 名字里**没有一个**生成物品或怪物
  （`MonsterGrave` 只是句对白）；`Data/Locations` 的 91 个地点共用 38 个字段，
  **没有任何坐标级/刷怪字段**（字段全集见 `data/locations` 的实测清单）。
- **刷矿**：采石场那套在 `Mountain.quarryDayUpdate()` 里，**矩形 `(106,13,22,22)` 与掉落表都写死**。
  它也证明了一件事：`Data/Locations.<loc>.CreateOnLoad.Type` **可以指定 C# 地点类**
  （`StardewValley.Locations.Woods` 之类）—— 但地点类的行为都是**按它自己那张地图的坐标**写死的，
  换一张图只会把矿刷到不相干的位置。**没有 `Quarry` 这个类**，`Quarry` 是 `Mountain` 里的一块。
- **大件**（陨石/巨木）：同上，没有数据驱动入口。

⇒ 这三类的**意图**原版表达不了。**如实记成边界**，不再当"待办"。

## 4. 已经做了：`FtmPackTranslator`（2026-10-11）

**做法**：FTM 包的 `content.json` 在 `ContentPack.Load` 里被**现场翻成一份 CP 形状的
`content.json`**（只有 `EditData` 补丁），后面**整条流水线原样复用** ——
判定、写入、报告都不必知道 FTM 的存在。落在 `src/SdvCpTool.Core/Cp/FtmPackTranslator.cs`。

| FTM 的字段 | 翻成 |
|---|---|
| `{季节}ItemIndex` 里的数字（`16`）| `Forage[].ItemId = "(O)16"` |
| 字符串（限定 id / 物品名）| 原样（1.6 的 `ItemRegistry` 两种都认）|
| `{Category:"Buried", contents:[…]}` | `ArtifactSpots[].ItemId`（**原版挖掘点**）|
| `ExtraConditions.Days`（**几号**，如 `13-15`）| `Condition: DAY_OF_MONTH …` |
| `ExtraConditions.Years`（`2+`）| `Condition: YEAR 2+ 2+` |
| `ExtraConditions.WeatherToday`（`Rain`）| `Condition: WEATHER Here Rain` |
| `ExtraConditions.CPConditions` | 它本来就是 CP 的 `When` 对象，直接并进去 |
| `{季节}` | `Forage[].Season`；**`ArtifactSpots` 没有 Season 槽** ⇒ 写进 `Condition: SEASON <季节>` |

⚠ 三处**实测踩过**的坑（都已进回归测试）：
① `Days` 是**几号**不是星期几 —— 一度错标成 `DayOfWeek`，写出 `DAY_OF_WEEK [
 "2" "4" …]` 那种废条件；
② 值必须**拼成逗号串**再交给 CP→GSQ 翻译器，塞 `JArray` 会被 `ToString()` 成同样的废条件；
③ 改了源码但 `dotnet build` **失败了没看**，于是拿旧二进制跑出一堆"改了却没用"的结论
（`grep -c "个错误"` 把「0 个错误」和「9 个错误」都数成 1 —— 教训：构建完要看那行**内容**）。

**SVE[FTM] 实测（同一棵树、同参数）**：

```
落 700 条（268 条 As Forage + 432 条 As ArtifactSpots），覆盖 26 张地图
另有 24 个 area 因条件代不掉（全是「限次刷新」）整条没写
刷怪 246 / 刷矿 9 / 大件 12 个 area 没有落点
```

**全语料（4 个 FTM 包一起、32+4=36 个包）**：

```
新增 Forage 644 条 + ArtifactSpots 432 条 = 1076 条，覆盖 41 张地图
与「不加 FTM」那一轮逐字节对比：**只有 Data/Locations.json 变了**，其余 13505 个文件一字不动 ✓
```

`Data/Locations` 打包 **1/0** ✓。语料链也把 4 个 FTM 包纳进来了
（`convert-mods.py` 认 FTM，并**替它补上「同容器目录下的 CP 包」这层隐式前置** ——
FTM 的 manifest 里不写这层依赖，而它改的 `Data/Locations.<地图>` 条目正是那些 CP 包创建的）。
⚠ 反过来那条边要**掐掉**：SVE 自己声明依赖 `FlashShifter.SVE-FTM`（SMAPI 的「装了才算数」，
不是"要先跑"）。认了它就成了环，兜底排序会把 SVE[FTM] 排到 SVE 前面 ⇒ 它的 `TargetField` 全落空，
而且 SVE 的产出跟着变了 **395 个文件**（实测）。⇒ **CP → FTM 的依赖不算排序边**。

## 4.5 一次性 area（限次刷新 = 1）—— 也啃下来了：改成**邮寄**

`ExtraConditions.LimitedNumberOfSpawns` 的取值实测只有 `1`（**22 个 area**）、`3`、`8`。
原来整条跳过；但原版**有一条路**：`Data/TriggerActions` 的**一次性动作**
（官方那 31 条**全部** `MarkActionApplied: true`，语义就是"只做一次"）。

于是把「在地图上捡到一次」换成「**条件满足且到过那张地图**时邮寄一次」：

```
Data/TriggerActions += { Id, Trigger: "DayEnding", MarkActionApplied: true,
                         Condition: "SEASON <季>, PLAYER_VISITED_LOCATION Current <地图>, <原条件>",
                         Action: "AddMail Current <邮件 id>" }
Data/Mail[<邮件 id>] = "<area 名> 的东西已经送到。%item id (O)372 1 %%[#]<area 名>"
```

- **物品、数量、条件都不变**，额外加了 `PLAYER_VISITED_LOCATION` —— **到过那张地图之后**才寄到
  （否则"从没去过的地方"的东西凭空到邮箱，那是另一回事）；
- `Trigger: DayEnding` 与官方那 24 条邮件一致（夜里入箱、次日早晨看到）；
- SVE[FTM] 实测：**22 个 area → 84 封**（TriggerActions + Mail 各 84 条）—— 每个 (area × 季节) 各一封。

  ⚠ **三处"看着对、其实丢内容"都踩过**（都有回归钉子）：
  ① **悬挂 else**：`if (A) foreach (…) if (…) …; else foreach …;` 里的 `else` 按规则绑到**最内层 if**，
  于是"非容器"这一支整个不执行 —— 实测 22 个 area 里只有 4 个宝箱的寄出来了，另 18 个**一声不响**地没有；
  ② **邮件 id 撞车**：id 原先是 `地图_季节`，而同一张地图同一季常有多个一次性 area
  （`Custom_ForestWest` **7 个**）⇒ 后一个覆盖前一个。加 area 序号后：8 封 → 36 封 → **84 封**；
  ③ **宝箱内容没摊开**：一次性 area 常常只有一个宝箱（「Diamond Wand Chest」），
  箱子搬不进原版，但**箱子里的东西**才是玩家该拿到的 ⇒ 现在摊平寄过去。
  `Data/TriggerActions` 与 `Data/Mail` 各自打包 **2/0** ✓；
- ⚠ **投递方式确实变了**（不是在地图上捡到的）—— 报告里明说，并给出"不接受就删掉
  `Id` 以 `SdvCpTool_FTM_` 开头那几条"的路子；
- 限次 `3` / `8` 的两个 area 仍然跳过（"只做一次"这一档之外没有对应机制）。

## 4.6 对着**游戏的消费代码**核过：这个映射是游戏真的会读的

不是"形状像"就完事，去看游戏自己怎么用这两张表：

| 落点 | 消费点 | 核到什么 |
|---|---|---|
| `Forage` | `GameLocation.spawnObjects`（`GameLocation.cs:15132-15175`）| 先按 `Condition`（GSQ）与 `Season` **过滤**，每天刷 `Min..MaxDailyForageSpawn` 次，每次**从过滤后的表里随机挑一条**再按 `Chance` 掷一次；条目由 `ItemQueryResolver.TryResolveRandomItem` 建物品 —— **认不出来会写日志**（`failed parsing item query`），不是静默丢 |
| `ArtifactSpots` | `tryGetRandomArtifactFromThisLocation`（`GameLocation.cs:14236-14259`）| `Default` 地点的表**并上**本地点自己的表，按 `Precedence` 排、`Chance` 与 `Condition` 都查 ✓ —— 所以往**具体地点**追加是对的 |

⇒ 落点的两个槽（`Forage[].Season` / `[].Condition`、`ArtifactSpots[].Condition`）都是**游戏真的会读**的，
坐标级没有槽也是真的（那张表**没有**坐标字段）。

⚠ 顺带把 `Chance` 的语义也校准了：它是**过滤**不是"选中概率"（挑中之后再掷一次），
所以"期望条数 = 尝试次数 × 该条的 Chance"。我们现在给的是
`area 的 MaximumSpawnsPerDay / 该 area 条目数` —— 在"每天 1 次尝试"的默认下与意图吻合，
**多 area 共享同一张表时会有偏差**（原版是全局挑一条），**未确证**精确到多少。

## 5. 仍然做不到的（如实记）

1. **刷怪**（SVE 246 个 area、17 张地图）—— 这是 SVE 自定义地图的主要玩法之一，
   而原版**没有任何数据驱动入口**。装机时应当明确告诉用户："这些地图在原版机制下不会有怪"。
2. **刷矿 / 大件**（9 + 12 个 area）—— 同上。
3. **坐标精度**（105/116 个 forage area 原本按坐标刷）—— 原版 `Forage`/`ArtifactSpots` 是地点级。

**"能不能用别的机制近似刷怪"这条路也走到头了**（逐条试过、都不是路）：

| 试过的路 | 为什么不行 |
|---|---|
| 把这些地图声明成 `Type: StardewValley.Locations.Woods` | `Woods.resetSharedState` **确实**会刷怪（50 次随机尝试、按季节刷史莱姆）—— 但刷的是**史莱姆**（SVE 要的是虚空魂/影蛮/红史莱姆那套），而且**不看 area 的季节/日期/年份条件**，还捎带一堆 Woods 自己的行为（入口、清理）。⇒ 近似到"怪不对、条件不对"就不是近似了 |
| 声明成 `BugLand` | 它按**图块下标 28** 刷 Grub —— 固定一种怪，还要求改地图的**瓦片层**（我们只做属性改动） |
| 声明成 `MineShaft` / `VolcanoDungeon` | 那是"矿洞/火山"整套语义（层数、出入口、生成规则），换掉整个地点的性质，不是"加点怪" |
| `Data/TriggerActions` 顶上 | 它只能做**动作**（发信/给物品/置旗标），刷不了"地图上会走动的怪" |
4. **宝箱**（16 条）—— 没有对应机制（`%item` 邮的是物品，不是"地图上的一个箱子"）。

---

## 6. 补两个**读漏了的字段**（2026-10-11 晚，同参数对比：产出树**逐字节零差异**）

对着 4 个包的实际数据把顶层键过了一遍（`File_Conditions` / `*SpawnEnabled` / `*_Spawn_Settings` /
`QuarryTileIndex`），发现**两个字段我们压根没读** —— 它们在这份语料里恰好都不触发，
但**将来遇到真会触发的包就会写错内容**：

| 字段 | FTM 的语义 | 漏读的后果 | 现在的做法 |
|---|---|---|---|
| `<段>SpawnEnabled`（4 个布尔）| **显式 `false`** ⇒ 这一段它自己没开 | 会写出**它本来不刷**的觅食（写错，不是少写）| 显式 `false` 就整段跳过，并在报告里说明"**该包自己就没开**"（不再报成"没写"）|
| `File_Conditions` | 整份文件只在它成立时生效 | 会写出**它根本不会刷的东西** | `OtherMods` → `HasMod`（可离线烘焙，交给 Baker 折）；`FarmTypes` → CP 的 `FarmType`；`FarmerNames`/`SaveFileNames` 是**运行时**才知道的 ⇒ **整份文件都不写**并报出 |

**受控对比**（同参数，36 包）：`total` 7286 不动、**判定一条没变**、**产出树 13562 个文件
逐字节零差异**；唯一的变化是报告里的账更准了：

```
−  FTM 刷矿 1 个 area（1 张地图）**没写**：原版没有数据驱动的入口…
+  FTM `OreSpawnEnabled` 是 `false` ⇒ 刷矿这一段**该包自己就没开**（FTM 也不会刷），没写
+  FTM `MonsterSpawnEnabled` 是 `false` ⇒ 刷怪这一段**该包自己就没开**…
+  FTM `LargeObjectSpawnEnabled` 是 `false` ⇒ 大件这一段**该包自己就没开**…
```

（那三条来自包 1 与包 4：它们把对应的段自己关掉了 —— 原来我们把它们记成"我们没写"，是**多报**。）
单测 1308 通过 / 0 失败（FTM 那组新增 6 条，把上面四种情形都钉住）。

### 6.1 段内**还有一批旋钮我们装不下** —— 现在**报出来**了（同一轮补的）

把 <code>Forage_Spawn_Settings</code> 与每个 area 的实际键过了一遍，除了上面两个"漏读"，
还有一批是**原版根本没有对应槽**的（`Forage`/`ArtifactSpots` 是**地点级**的槽：
只有物品、季节、概率、条件）：

```
段级：CustomTileIndex、PercentExtraSpawnsPerForagingLevel
area：MinimumSpawnsPerDay、IncludeCoordinates 的 ExcludeCoordinates、
      IncludeTerrainTypes / ExcludeTerrainTypes、StrictTileChecking、
      SpawnTiming、DaysUntilSpawnsExpire
```

⇒ 报告里现在按包汇总一条（SVE 那份最典型：**92 个 area** 带 `IncludeTerrainTypes`/`SpawnTiming`/
`StrictTileChecking`/`DaysUntilSpawnsExpire`、**78 个**带 `MinimumSpawnsPerDay`、**19 个**带
`ExcludeCoordinates`）：

> FTM 觅食/埋藏物有 N 个**放不下的旋钮**：… —— 原版 `Forage`/`ArtifactSpots` 是**地点级**的槽，
> 「刷在哪些图块上 / 至少几个 / 会不会过期 / 什么时段」这层**没有对应字段** ⇒
> **物品与季节对得上，位置与数量这层丢了**。

**受控对比**：产出树**逐字节零差异**（只动报告），判定一条没变。

### 6.2 那 84 封邮件的 `%item` 语法，对着**游戏的消费代码**核过了

`StardewValley.Menus/LetterViewerMenu.HandleItemCommand`（`:296-345`）：

- **循环**扫出每一段 `%item … %%`，**每段都给一样** ⇒ 多段 = 多样（**不是**"多段里随机挑一段"）；
- 段内 `%item id` 后面：只给一个 id ⇒ 数量按 1；**给两对以上**（`A 1 B 1`）⇒
  `Game1.random.Next(array2.Length)` 取偶数下标 ⇒ **只给随机那一对**。

⇒ 我们那条"**每样一段**"的写法（`%item id (O)16 1 %%` 重复 N 次）与这个语义**完全吻合**：
N 段就是 N 样，一件不少 ✓（原版 `Linus` 那封信写的是 `%item id (O)136 1 (O)143 1 …` ——
那种写法游戏只给**其中随机一对**）。
