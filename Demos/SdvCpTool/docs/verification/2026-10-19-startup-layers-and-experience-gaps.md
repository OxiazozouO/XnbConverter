# 启动/建档成本、自定义图层渲染、以及相对「MOD+DLL」的体验缺口

日期：2026-10-19
起因：业主三问：①创建存档为什么久；②超过原版渲染层数的图层（如云雾）不显示，原版有没有替代机制；
③转成纯 XNB 后，相比原 MOD+DLL 还缺什么体验。

## 1. 创建存档为什么久

### 1.1 规模（实测）

| | 原版 | 本产出 |
|---|---|---|
| `Content` 体积 | 544 MB | **1.6 GB** |
| XNB 数量 | 3,550 | **15,626** |
| `Data/Locations` 条目 | 91 | **865**（834 个带 `CreateOnLoad`）|
| 唯一地图 | ~60 | **817** |
| `Data/Characters` 角色 | ~40 | **281** |

### 1.2 建档流程里真正花钱的步骤

`Game1.loadForNewGame()` → `AddLocations()` 对**每个带 `CreateOnLoad` 的地点**执行
`new GameLocation(mapPath, id)`，而构造函数里 `loadMap()` 会做：

1. `content.Load<Map>(mapPath)`（817 张地图，实测 **3.3 秒**，地图 XNB 合计 3.9 MB）；
2. `updateSeasonalTileSheets` + `map.LoadTileSheets` —— 每个地点都过一遍它的图集
   （**610 张唯一图集**，XNB 合计 33.3 MB；ContentManager 按资产名缓存，只解码一次）；
3. `loadObjects()`：扫全图每个格子建对象；
4. 室外地图再建 `WaterTiles`，同样逐格扫。

之后 `AddNPCs()` 为 **281 个角色**建 NPC（精灵/立绘/日程数据），最后把
**约 25 MB 的存档**序列化写盘。

### 1.3 结论

- **不是资产缺失或格式异常**：817 张地图全部能加载（0 失败），体积也正常。
- 主因是**内容体量**：865 个地点、281 个 NPC、817 张地图，每个都要建对象/扫水格；
  这是 232 个内容包叠加后的固有成本。
- 与原 MOD+DLL 的对比要注意：CP 在运行时**同样**要构造这些地点；若那边感觉更快，
  优先排查：首次运行被 Windows Defender 扫描 15,626 个新文件、磁盘冷缓存（第二次启动会快）、
  或者那边实际启用的内容包更少。
- 可做的优化（未实施）：给 `Content` 目录加杀软排除；第二次启动对比；
  以及一个「只保留可到达地点」的转换开关（会删内容，需业主决定，不能默认开）。

## 2. 自定义图层：原版**本来就会画**，只是名字有讲究

`GameLocation.SortLayers()` 的规则（`GameLocation.cs:1903-1938`）：

- 图层 Id 以 `Back`/`Buildings`/`Front`/`AlwaysFront` 开头，且**后面是空或整数** ⇒
  收进对应桶，按整数升序绘制；
- 所以 **`AlwaysFront4`、`AlwaysFront5`、`Back2`、`Buildings3` 原版就会画**（不是只有 8 层）；
- `.NET` 的 `int.TryParse` 还接受正负号与前后空白 ⇒ `Back+1`、`Back-1`、`Back 2` 也能画；
- **不画**的是：
  - 锚点内部被空格拆开：`Always Front`；
  - 完全自定义的名字（`UnderAlwaysFront`、`CustomClouds`…），它们靠 TMXL/PyTK 的
    `Draw`/`DrawBefore`/`DrawAbove` 属性定位（`TMXLoaderMod.cs:2330-2372`）；
  - 数据层（`Set-Up`/`Schedule`/`MainEvent`/`RSVSetup`/…）。

### 修法：落盘前归一图层名

新增 `LayerNameNormalizer`（`Maps/LayerNameNormalizer.cs`）：

- 只处理「想画但名字原版解析不了」的图层；
- 有 `DrawBefore=X` ⇒ `X<min-1>`；有 `Draw=X`/`DrawAbove=X` ⇒ `X<max+1>`；
- 否则按名字推断（`Always Front` ⇒ `AlwaysFront<next>`）；
- 冲突时顺延到下一个空整数，不覆盖别的层；
- **数据层一律不碰**。

### 2.1 实例：SVE 森林雾（2026-10-20 复核）

`[CP] Stardew Valley Expanded/code/Locations/Edits.json` 的 `EditMap Maps/Forest`
用 `Assets/Maps/MistEffects/Forest_Mist.tbin`，条件是 `MistEffects=true` +
`Weather=Rain, Storm`，`ToArea` 120×120。

- `Forest_Mist.tbin` 里只有一层 **`AlwaysFront4`**，5076 个图块，`Draw: AlwaysFront`；
- 转换报告把这条判为 **`LocalEdit` / `written: null`**（天气条件没有原版图块通道）；
- 产出 `Maps/Forest.tbin` 的图层是
  `Back, Back2, Back3, Back5, Buildings, Buildings2, Buildings3, Paths, Front, Front2,
  AlwaysFront, AlwaysFront2, AlwaysFront3, Back4, Buildings4` —— **没有 `AlwaysFront4`**；
- 雾贴图 `Maps/z_Forest_Mist.png` 已经因为无条件 `Load` 在产出树里，但地图层没写。

⇒ **森林地图目前看不到 SVE 的雨雾**。这不是层名/层数问题：`AlwaysFront4` 原版会画，
卡点是“Weather=Rain, Storm”这条条件 EditMap 没有静态通道。无条件写入的话晴天也会起雾，
只能算视觉近似。

### 2.2 能不能用 DayTiles / NightTiles 做“只下雨显示”？

原版确实有 **`DayTiles` / `NightTiles` 两个地图属性**（不是图块属性），格式是
`图层 x y 图块索引` 四元组重复；玩家进入地点时由 `GameLocation.resetForPlayerEntry`
（反编译 `:97911-97933`）应用：

```
if (timeOfDay < darkTime - 100 && (!IsRainingHere() || name == "SandyHouse"))
    → 应用 DayTiles
else
    → switchOutNightTiles()  // 应用 NightTiles
```

关键点：**下雨和夜晚走同一条 NightTiles 分支**。所以：

- 把雾图块写成 `NightTiles`、把同一格写成透明图块（SVE 的 `z_Forest_mist` 索引 **4** 是空图）
  写进 `DayTiles` ⇒ **雨天显示 + 每天夜里也显示**，晴天白天不显示；
- 无法只让“雨天的夜晚显示、晴天的夜晚不显示” —— 原版没有天气专属的 tile 切换通道；
- 想精确到“只有雨/雷雨”只能靠 DLL 或事件/脚本运行时改地图。

这是目前最接近 SVE `Weather: Rain, Storm` 的纯 XNB 方案；代价是夜晚会多一层雾。
`Forest_Mist.tbin` 的 5076 个图块全部来自 `z_Forest_mist` 一张图集，所以
“加 `AlwaysFront4` 层 + Day/Night 两串属性”在实现上是可行的（但还没落代码）。

## 3. 相对「原 MOD+DLL」的体验缺口

### 3.1 总量（fix26，232 包）

- 补丁 **32,762**：写出类 `Converted` 5,783 + `ConvertedNoCondition` 9,114；
- 未落地：`LocalEdit` **590**、`NoLandingPoint` **2,183**、`NeedsReview` **2,105**、
  `TargetMissing` **202**、写入失败 **630**；
- **没有转换**：代码模组（DLL）**220 个**、其他框架的内容包 **38 个**
  （`_not-converted.txt`）。

### 3.2 按体验分类

| 体验 | 状态 |
|---|---|
| 静态图片 / 地图 / 家具 / 作物 / NPC 定义 / 商店 / 邮件 / 任务 / 世界地图 | **基本完整**（含四季图集、翻转烘焙、语言变体）|
| 对话文本 | 静态部分完整；**684 条 `NeedsReview`**（动态键/运行时条件/结构无法自动识别）|
| 日程 | **823 条里 264 `NoLandingPoint` + 100 `NeedsReview`** —— 约 44% 的日程没有静态落点 |
| 事件 | 1,649 条里 214 `NeedsReview` + 11 `NeedsRuntime` + 8 `NonTranslatable`；事件脚本**内部的运行时分支**原样保留，但没有运行时求值 |
| 条件化地图结构 | **590 条 `LocalEdit`**：按天气/时间/剧情标记变化的道路、门、桥、传送**没有应用**（只保留一种状态）|
| 条件化数值/内容 | `Season`/`FarmType`/`Language`/随机数等**冻结为单一值**（春/Standard/zh/第一个随机项）|
| DLL 行为 | **完全没有**：Automate、Lookup Anything、SpaceCore 自定义物品/技能、Custom Companions、Alternative Textures、Solid Foundations、TMXL/PyTK 的自定义构建/图层绘制等 |
| FTM 刷怪/刷矿 | 觅食/埋藏物已转（Forage/ArtifactSpots）；**刷怪 246 个区域、刷矿 9、大件 12 没有原版数据入口** |
| 音频 | 已注入 cue、循环已实现；**实机听感仍未确证** |
| 婚姻 | 已转换（花束/订婚/婚礼/婚房/对话） |
| 多人在线 | 原版联机可以，但 DLL mod 的同步/网络行为不存在 |

### 3.3 一句话概括

纯 XNB 产出是**一份内容快照**：

- 它把「静态资产 + 静态数据」做全了；
- 把「按运行时状态切换的内容」压成了**一个确定状态**；
- 把「DLL 才有的行为」整类拿掉了。

## 4. 验证

- 单测 **1,551/1,551**。
- fix27（图层名归一 + FTM `TargetField` 镜像）全量结果见文末（跑完后补）。
- 建档耗时构成：地图加载 817 张 / 3.3 s（探针实测，不含对象/水格扫描与 NPC/存档序列化）。
