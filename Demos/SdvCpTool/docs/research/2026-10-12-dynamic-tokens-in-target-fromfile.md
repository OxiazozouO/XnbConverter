# 修一个真 bug：`Target`/`FromFile` 里的**包内 DynamicTokens** 没被代（整条补丁落空）

日期：2026-10-12
类别：**行为修正（高影响）** —— 包内图集/资产进不了产出树，**引用它们的地图建不出来**

## 症状

全语料里 **326 条**补丁（跨 **36 个包**）因为
「`FromFile` 含无法静态解析的令牌」而整条不写。其中最早被注意到的一条：

```
[CP] DaisyNiko's Tilesheets
  Load Maps/spring_daisyextras, Maps/desert_daisyextras, … ← assets/{{recolour}}/{{TargetWithoutPath}}.png
```

⇒ 那些「图集」资产根本没进产出树，而**引用它们的 29 张地图**（`Beach-Luau`、`Custom_ArkOrchard`…）
在纯 XNB 里就会**找不到图集**。

## 根因：两条替换路径各管一半，DynamicTokens 谁都没管

| 路径 | 谁在做 | 代哪些 |
|---|---|---|
| **值**（`Entries`/`Fields`/`When` 以外的值） | `ValueTokenSubstitutor` | 包内 `DynamicTokens` / 配置字段 / `{{ModId}}` —— **但显式跳过 `Target`/`FromFile`** |
| **`Target`/`FromFile`** | `ConfigTokenSubstitutor` | **只有** `ConfigSchema` 字段 + 内建 `{{Random}}`/`{{Range}}` |

`{{recolour}}` 是 `DynamicTokens` 里按配置字段拼出来的令牌（`ManualRecolourSelection: Off ⇒ Earthy`）——
两条路都不管它，于是补丁被下游「含无法静态解析的令牌」拒掉。
CP 那边 `TokenString` 渲染字符串时**整棵令牌树都会展开**（`TokenString.cs:290`），
`Target`/`FromFile` 并不特殊。

## 修法

`ConfigTokenSubstitutor` 在配置字段之后**再走一步 `DynamicTokenResolver.Expand`**：

- **无损**（`When` 条件能静态判定，例如配置字段、`HasMod` —— 我们**知道本机装着哪些 mod**）⇒ 照代；
- **有损**（条件判不了、只好落到无条件默认块）⇒ **一律不代**，与值那边同一条规矩，
  宁可整条报出来，也不把猜的路径写进产物；
- 确定化（`{{Random}}` 取第一个备选之类）逐条记进那条补丁的 `writeNote`（复用值路径那本账）。

## 验证（prof64 → prof65，同参数全量 232 包）

| | prof64 | prof65 |
|---|---|---|
| `written` | 15060 | **15144（+84）** |
| `writeFailed` | 217 | **208（−9）** |
| 「找不到图片的图集名」 | 46 种 | **21 种**（把检查器的 `../`/`Maps/` 前缀折算修对之后是 **14 种**） |

- **逐条核过判定变化**：**变好 84 条**（`TargetMissing → Converted` 76、其余 8），
  **变坏 0 条**；另有 89 条「两轮都没写、只是判定更精确」（例如 `When` 里的
  `HasFile assets/…/X.png` 以前因为令牌没代而判不了，现在能判 ⇒ 老实报「条件恒为假、
  本补丁不适用」）。
- 定点（`--packs` 只跑两个图集包）：`DaisyNiko's Tilesheets` written 1 → **8**/12
  （`Maps/spring_daisyextras.png` 160 KB 等落地）、`HxW Tilesheets` 1 → **17**/27。
- 修好的 25 种名字里包含 `giex_*` 11 种与 `hime_*` 一批 —— 正是那些「被令牌挡住的图集」。

## 剩下的 14 种是什么（未修）

先把**检查器**修准（原来只扫 `Maps/`+`Tilesheets/`，把 7 个**存在**的引用误报成缺失：
`../LooseSprites/Cursors.png`、`../TileSheets/Craftables.png`、`Maps/Mines/mine.png` 一类）——
修准之后 `prof66` 的真缺口是 **14 种**：

- **上游写坏的引用（9 种）**：`[CP] Integrated Minecarts` 的 tmx 里**字面写着**
  `.paths.png` / `.spring_outdoorsTileSheet.png` / `.DesertTiles.png`（前导点），
  连带 8 张地图（Caldera / Desert / Backwoods / Forest / Island_W…）。CP/SMAPI 下同样找不着，
  **不是我们的锅**，但盘点会报出来。
- **运行时令牌（5 种）**：`hime_outdoorplants`（7 张地图）、`panorama`（5 张）、
  `hime_outdoorfurniture_front`（3 张）、`spring_silo`、`dn` —— 它们的 `Load` 是
  「**同一个资产名、按季换内容**」（`FromFile: Assets/Vanilla/{{Season}}/….png`）。
  纯 XNB 里一个资产键只有一份，得物化成 `spring_X/summer_X/fall_X/winter_X` 四份
  再把引用地图的 `TileSheet.ImageSource` 指到 `spring_X`（既有的
  `SeasonalTilesheetPlanner` 就是这么编 `EditImage` 的），但**只有室外地点**吃这套
  （`GameLocation.cs:16417`）⇒ 得先证明适用面。**这一步没做**，现状是整条不写 + 盘点报出来。
- 别的 mod 的令牌：`{{RMP.…}}` 那类**其实是本包的** DynamicTokens（`Remapping` 自己定义的，
  名字带点），这次一并修好了（84 条）。

## 又一个真 bug 顺手修掉（同一轮发现）

修好 `{{HasSVE}}` 之后，`[CP] The Last Smoluanu` 的 `Load Maps/Summit_Night` 从
「令牌代不掉」变成**真的写出来**，于是暴露出撞名判据的一个盲点：
先 `Load Maps/NightBGDwarf`（进地图内存模型、**还没落盘**），后 `Load Maps/Summit_Night`，
后者的包内图集就叫 `NightBGDwarf.png` —— 判据看不见内存里那张地图，图就写到了它的键上，
随后地图又写回同一键（**一个键两份不同类的东西**，正是 `2026-10-12-tilesheet-key-collision.md` 那一类）。

⇒ 撞名判据（`LocalMapTilesheets.Clashes`）加一条：**本次运行里已经生成/改过、还没落盘的地图**
也算占位（查 `MapCache.TryGetLoaded`）。改后 `prof66` 的盘点回到 **0 处**。
