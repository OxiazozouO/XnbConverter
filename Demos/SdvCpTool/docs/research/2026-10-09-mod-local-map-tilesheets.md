# 修一个真 bug：模组地图引用的「包内同级图集」没被搬进产出树（地图整个建不出来）

日期：2026-10-09
类别：**行为修正（高影响）** —— 这条会让**整张地图加载失败**

## 症状

实机（探针日志）报过一条我没能解释的错：

```
✗ Maps/Custom_Ivy_OrnithologistsGuild  ::  Could not find file 'D:\tmp\gv2\Content\Maps\Binoculars.xnb'
```

我当时的结论是「包自己没把这些图集发布成地图会去找的名字，是包侧问题」——
**那个结论是错的**。这是**我们**漏了东西。

## 根因：SMAPI 会按**模组目录**解析地图的图集

`StardewModdingAPI.Framework.ContentManagers.ModContentManager`（反编译）：

```csharp
// 地图图集：先按「地图文件所在目录」在模组里找
private static readonly string[] LocalTilesheetExtensions = { ".png", ".xnb" };

// TryGetTilesheetAssetName(modRelativeMapFolder, relativePath, …)
//   1) Path.Combine(modRelativeMapFolder, relativePath)（+ .png / .xnb）在**模组目录里存在**
//      ⇒ 图集名换成该模组的内部资产键（SMAPI 从模组目录供图）
//   2) 否则回落游戏内容：GetContentKeyForTilesheetImageSource(relativePath)
//      —— 没有 `Maps` 前缀就补上、去掉 `.png`，也就是 `Maps/<名>
```

也就是说：**`Load` 一份 `assets/Maps/X.tbin` 时，它引用的图集先看 `assets/Maps/` 里有没有同名图片**，
有就当作模组自己的资产，**根本不走游戏内容那条路**。

**纯 XNB 产出里没有"模组目录"这一层** ⇒ 那些图片必须**当成普通图片资产写进产出树**，
放到游戏会去请求的名字上（也就是第 2 条算出来的 `Maps/<名>`）。没搬 ⇒ 游戏去找
`Content/Maps/Binoculars.xnb` ⇒ 找不到 ⇒ **整张地图建不出来**。

### 两个坑

1. **季节族要整族搬**：室外地图的图集名会被 `GameLocation.GetSeasonalTilesheetName`
   按当前季节换前缀（`spring_X` → `summer_X`…），只搬被点名的那一季，换季照样找不到。
2. **`EditMap` 的 `FromFile` 补丁块也一样**：`PatchMap` 会把块里的图集作为 `z_<id>`
   挂到目标地图上、名字照抄块里的 —— 所以块引用的包内图片也得搬。

## 影响面（实测）

全语料 `FromFile → 地图` 引用 **686 处**，其中**包内同级图集没进产出树**的 20 处、
14 个图集名，跨 3 个包：

| 图集 | 出处 | 落点 |
|---|---|---|
| `spring_exterior` / `winter_exterior` | 鸟会 `assets/map/Forest_OrnithologistsGuild.tmx` 等 | **`Maps/Forest`**、`Forest-FlowerFestival`、`Forest-IceFestival` |
| `spirit_sewer` | 热闹的下水道 `Sewer_aimon111_mundane*.tmx` | `Maps/Sewer` |
| 9 个（binoculars/perch/…）| 鸟会 `assets/map/OrnithologistsGuild.tmx` | `Custom_Ivy_OrnithologistsGuild` |
| `spring_jaspartytiles` | 谢恩 `assets/Maps/JasParty.tbin` | `Maps/JasParty` |

⚠ **注意前三行落到的是原版地图**（`Forest`、`Sewer`）—— 原版地图被内容包改过之后引用了包内图集，
不修就是**连原版地图都进不去**。这正对着用户报的「进不去」。

## 修法

新增 `SdvCpTool.Core/Maps/LocalMapTilesheets.cs`，照抄 SMAPI 的两条规则：

- `FindLocal`：按 `原样 / +.png / +.xnb` 在地图所在目录找；
- `GameContentKey`：`../` 去掉、没 `Maps/` 前缀就补、去掉 `.png`；
- 季节族：`spring_X` 的另外三季一并搬；
- 落到树里用**现成的** `ImageApplier.Apply`（逐字节复制 + 补图片 `.config`），不新写一套。

接线四处：`LoadApplier.PlaceMap`（tbin/xnb 地图）、`LoadApplier.LoadMapTmx`、
`MapApplier.ApplyFromFile`（补丁块）。找不到包内同级文件的**什么都不做**
（那是游戏内容的图集，不能凭空造）；只有 `.xnb` 形式的记一条提示，不静默跳过。

## 验证

1. **测试先行**（`LoadApplierTests` 3 条 + `MapApplierTests` 1 条）：
   - `Load地图时把包内同级图集一起搬进产出树` —— **改前红、改后绿**；
   - `季节性图集把四个季节都搬过来` —— 改前红（顺带修掉剥季节前缀时多留一个 `_` 的错）；
   - `图集不在包内时不动它`；
   - `FromFile补丁块的包内同级图集也搬进产出树`。
   - 全量测试：**1198 通过 / 0 失败**（改前 1195）。
2. **端到端受控对比**：只跑受影响的那 3 个包（鸟会 / 热闹的下水道 / 谢恩），
   用 `tools/check-map-tilesheet-assets.py` 查产出树 ——
   **改前 14 种图集找不到，改后 0 种**；抽查 `Maps/Forest`、`Maps/JasParty`、
   `Custom_Ivy_OrnithologistsGuild` 引用的图集，产出树里现在都有 `.png` + `.config`，
   季节族的四季也齐了。

## 顺带订正

`2026-10-09-full-chain-static-audit.md` §检查四 与 `2026-10-09-dll-mod-audit-…` 里
「13 种 tileset 图片名找不到 —— 证据指向包侧」这段**是错的**，已按本文订正：
那是**我们**漏搬，不是包的问题。
