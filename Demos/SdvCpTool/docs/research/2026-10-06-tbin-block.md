# tbin 块：`EditMap` 与 mod 自带地图

日期：2026-10-06
依据：CP 2.9.1 反编译、游戏包 `xTile.dll` / `TMXTile.dll` / `StardewModdingAPI.dll` 反编译、`XnbConverter.Core` 只读、两份语料实测

---

## 0. 头条：**不缺零件，缺组装**

| 零件 | 在哪 |
|---|---|
| `.tbin` 读写（xTile 官方的 `TbinFormat`，Load + Store） | **游戏包里的 `xTile.dll`**（67 KB）|
| `.tmx` 解析（Tiled XML → `xTile.Map`） | **游戏包里的 `smapi-internal\TMXTile.dll`**（52 KB）|
| tbin 模型 + 读写（我们已在用） | `XnbConverter.Core\Tbin\` |

**SMAPI 就是注册了 `TMXTile.dll` 才能读 `.tmx`**：
反编译见 `FormatManager.Instance.RegisterMapFormat(new TMXFormat(16, 16, 4, 4))`，
`ModContentManager.LoadExact` 按扩展名分派 `".tbin"/".tmx" → LoadMapFile<T>`。

**这条路在构建期完全可复刻，不需要在游戏里装 SMAPI。**
**所以「RSV 只有 159 个 `.tmx`、0 个 `.tbin`」不是障碍。**

---

## 1. `.tbin` 与 XNB 的关系

**`.tbin` 文件 = XNB 载荷里的原始字节**，二者只差一个长度前缀。两处独立证据：

```csharp
// 解包 XNB.cs:296-301 —— 原样落盘，不加不减
case ".tbin":
    TBin10 tBin = (TBin10)Data;
    File.WriteAllBytes(array2[0], tBin.Data);
    break;

// 打包 XNB.cs:564-569 —— 去掉 tilesheet 的 ".png" 再原样包进去
case ".tbin":
    byte[] data2 = File.ReadAllBytes(array2[0]);
    TBin10Reader.RemoveTileSheetsExtension(ref data2);
    TBin10 data3 = new TBin10 { Data = data2 };
    Data = data3;
```

游戏侧同源：`xTile.Pipeline.TideReader.Read` = `int32 长度` + `TbinFormat.Load(stream)`，
然后 `map.assetPath = AssetName` + `map.FlattenTileSheetPaths()`。

---

## 2. tbin 里存什么

`XnbConverter.Tbin.Entity` 与 xTile 的 `TbinFormat` **逐字段比对一致**：

| 类 | 字段 |
|---|---|
| `TBin10` | `Data`、`Format`、`Id`、`Description`、`Properties`、`TileSheets`、`Layers` |
| `TileSheet` | `Id`、`Description`、`Image`、`SheetSize`、`TileSize`、`Margin`、`Spacing`、`Properties` |
| `Layer` | `Id`、`Visible`、`Description`、`LayerSize`、`TileSize`、`Properties`、`Tiles`、`Index`、`_sizeArr`、`_currTileSheet` |
| `StaticTile : BaseTile` | `TileSheet`、`TileIndex`、`BlendMode`、`Properties` |
| `AnimatedTile : BaseTile` | `FrameInterval`、`Frames` |
| `Propertie` | `Key`、`Type`(0=bool/1=int/2=float/3=string)、`Value` |

**属性有四个作用域**：地图级 / 层 / 图块实例 / **tilesheet 的「按图块号」属性**
（键形如 `@TileIndex@128@Passable`，即 Tiled 里 tileset 的 tile 属性）。

**层的图块是「运行长度 + 切换指令」的字符流**：
`N`(空格) / `S`(静态) / `A`(动画) / `T`(切换 tilesheet)。

---

## 3. `TBin10Reader` 的公开 API

| 签名 | 说明 |
|---|---|
| `static TBin10Reader Create(byte[] data)` | 建实例 |
| `static TBin10 FormFile(string path)` | 读成完整 `TBin10` |
| `void Save(TBin10 input, string path)` | 写回 |
| `static void RemoveTileSheetsExtension(ref byte[] data)` | **只重写头部区**，去掉 tilesheet 的 `.png` |
| `void FormTmx(string path)` | **空实现 `{ }`**（`TBin10Reader.cs:150-152`），全库零调用点 |

**`XnbConverter` 没有任何 TMX 解析能力。**

---

## 4. ⚠️ 三个必须先知道的坑

### ① tilesheet 身份不由 tile 决定

`S` 记录里**不存 tilesheet**——它只由前面最近的 `T` 指令决定
（`StaticTileReader.Write` 只写 TileIndex/BlendMode/Properties）。

**改 `tile.TileSheet` 无效，甚至会写坏**。要换 tilesheet 必须自己往 `Index` 插 `'T'`、
往 `_currTileSheet` 加 Id。

> **这正是「第一批只做属性类改动」的理由。** 一旦要动图块/图层结构，就应切到 `xTile.Map`
> （`Layer.Tiles[x,y]` setter 自己维护这套编码）——CP 的 `EditMapPatch` 也正是这么用的。

### ② tilesheet 的 `Image` 是相对名，别写全路径

tbin 里应是 `spring_zridgeside`；打包时 `RemoveTileSheetsExtension` 帮你去 `.png`，
运行时 `FlattenTileSheetPaths` 帮你加 `Maps/`。

**写成 `Maps/spring_zridgeside` 会被拼成 `Maps/Maps/...`。**

### ③ `Save` 有 10 MiB 上限

`Pool.LongSize = 10485760`，`BufferWriter` 用的是 `Array.Copy` 且**无自动扩容**，超了直接抛。
当前语料安全（原版 `Beach.tbin` 才 106 KB），但要有意识。

另：`RemoveTileSheetsExtension` **只允许新头 ≤ 旧头**——决定「tbin→XNB 打包」环节**不能在 tilesheet 里增加信息**。

---

## 5. CP `EditMap` 的完整字段

| 字段 | 说明 |
|---|---|
| `Target` | **地图资产名**（`Maps/Town`），可逗号多目标 |
| `FromFile` | **源地图**（相对内容包），**可以是 `.tmx`** |
| `FromArea` / `ToArea` | 矩形 |
| `PatchMode` | `Overlay`(默认) / `ReplaceByLayer` / `Replace` |
| `MapProperties` | 改**地图级属性**；值为 `null` → 删除该属性 |
| `AddWarps` / `AddNpcWarps` | 追加 `Warp` / `NPCWarp` 属性 |
| `TextOperations` | **只支持 `["MapProperties", <名>]` 这一种路径** |
| `MapTiles` | 图块级改动列表 |

- **`Target` 不会指向 `.tmx`**——它是资产名。**但 `FromFile` 可以是 `.tmx`**（RSV 有 15 条这样）。
- **不存在 `RemoveWarps` 字段**；要删只能 `MapProperties: { "Warp": null }`。
- `AddWarps` 每条**恰好 5 个空格分隔字段** `fromX fromY toMap toX toY`，**新的拼在旧的前面**。

### `MapTiles` 条目（`PatchMapTileConfig`）

`Layer` / `Position` / `SetTilesheet` / `SetIndex` / `SetProperties` / `Remove`。

- 空位上要放图块，**必须同时给 `SetTilesheet` + `SetIndex`**，否则 CP 直接判失败
- **原图块是 `AnimatedTile` 时，CP 会把它替换成 `StaticTile`**（丢动画）——要对齐
- **只改已存在图块的属性**是最常见的形态，也最安全

---

## 6. mod 自带地图：`.tmx → xTile.Map → tbin → XNB`

```
mod 的 Assets/Maps/IndoorMaps/X.tmx
   │  ①Tiled XML → xTile.Map      ← TMXTile.TMXFormat.Load(Stream)
   ↓
xTile.Map
   │  ②xTile.Map → tbin 字节       ← FormatManager.Instance.BinaryFormat.Store(map, stream)
   ↓
Maps/X.tbin（tilesheet 的 Image 可能带 ".png"）
   │  ③按 XNB.cs:564-569 打包
   ↓
Content/Maps/X.xnb + X.config（Readers[0] = "xTile.Pipeline.TideReader, xTile"）
   │  ④游戏运行：TideReader → TbinFormat.Load → assetPath="Maps/X"
   ↓                  → FlattenTileSheetPaths → ImageSource 变 "Maps/spring_zridgeside"
   Content/Maps/spring_zridgeside.xnb  ← 由 mod 的 Load(.png) 落在这里
```

**④ 是整个族能成立的关键**，也是 RSV 作者早就设计好的：`Data/Tilesheets.json` 里有几十条
`Load Maps/spring_zridgeside ← Assets/Maps/TileSheets/{{TargetWithoutPath}}.png`，
正好补齐地图 tmx 里 `<image source="spring_zridgeside.png">` 需要在 `Maps/` 下存在的那张纹理。

> **所以图片层是地图层的必要前置。** Task 10a（图片 `Load`）已通，这一条已满足。

### `TMXTile.dll` 的覆盖度（实测扫 RSV 全部 159 个 tmx）

`csv` 1016 处 / `base64` 114 处（`compression="zlib"`）/ `<animation>` 3655 处
/ Tiled 版本 1.5.0–1.10.2 多种 → **RSV 用到的东西它一个不缺**。

### 需不需要自己写 tmx 解析？

**不必**——前提是愿意引用 `TMXTile.dll` + `xTile.dll` 两个托管程序集。
要自己补的只有 **「谁来调用它们」** 这一小段胶水（约 50-150 行）。

**若走自写路线**：只做单向（tmx → 我们的模型），估 400-700 行 + 测试，
且最容易写错的是**构造 `Index`/`_sizeArr`/`_currTileSheet`**。

> **建议：先用现成 dll 把端到端打通，再视依赖问题决定是否自写。不要一开始就自写。**

**工程风险在装配不在代码**：两个 dll 都是 `PublicKeyToken=null` 的非强命名程序集；
`D:\XnbConverter` 一行不能改，所以要**在 `SdvCpTool` 侧**挂 `AssemblyLoadContext.Resolving`
或把 dll 放到我们自己的运行目录。

---

## 7. 规模（实测）

### RSV `EditMap` 225 条

| 判定 | 条数 | 字段分布 |
|---|---|---|
| `TargetMissing` | **111** | MapTiles 41 / FromFile 9 / MapProperties 2 / **AddWarps 59** |
| `NeedsRuntime` | 75 | MapTiles 23 / FromFile 40 / MapProperties 12 |
| `NotApplicable` | 21 | — |
| `ConvertedNoCondition` | **16** | **FromFile 15** / AddWarps 1 |
| `LocalEdit` | 2 | MapTiles 2 |

> **111 条 `TargetMissing` 的根因不是「目标不存在」，而是「那 111 个目标全由 mod 自己的 `Load(.tmx)` 创建，而 `.tmx` 写不出来」。**
> 最大子类：**`AddWarps` 59 条**（纯字符串追加）。

> **16 条 `ConvertedNoCondition`** = 15 条 `FromFile + PatchMode:Replace`（整图替换原版节日地图）
> + 1 条 AddWarps。**目标在输入树里就是 `.tbin`，只差 tmx 解析 + tbin 写。**

### 语料 B `EditMap` 1246 条

912 条打**原版**地图（原版图已在树里是 `.tbin`）、334 条打自定义地图。
贡献者高度集中：`野生动物 [CP]` 624、`SVE` 517，两者占 91%。

### ⚠️ 一条重要的诚实修正

> **语料 B 的 912 个 `MapTiles` 条目里 85% 带 `{{Random}}`**
> （野生动物 mod 用它随机撒 NPC 生成点，形如 `"CustomCompanions": "Spawn {{Random: 0,0,1,2,3}} ..."`）。
>
> **那在统计上是「地图改动」，本质上是「运行时不确定性」——不可静态烘焙。**
> **净可转换的 `MapTiles` 只有约 133 条（语料 B）+ 133 条（RSV），不是 912。**

### 地图类 `Load` 与 `.tmx`/`.tbin` 比例

| | 地图 Load 规则 | 展开后资产 | `.tmx` | `.tbin` |
|---|---|---|---|---|
| RSV | 9 条 | **94** | 91 | 0 |
| 语料 B | 166 条 | 166 | 66 | **100** |

> 语料 B 里 mod 自带 **`.tbin` 431 个、`.tmx` 231 个**，且 `Load` 里 `.tbin` 规则多于 `.tmx`
> —— **在更广的生态里，作者直接发 `.tbin` 已经是主流**；`.tmx` 主要是 SVE/RSV 这类大包。

---

## 8. 优先级建议（收益/成本比从高到低）

**① tbin 读-改-写，只做「属性」类（先做，不碰 tmx）**

三类：`MapProperties`、`AddWarps`/`AddNpcWarps`、`TextOperations(["MapProperties", *])`。

- **完全不碰瓦片几何**，只动 `TBin10.Properties` 的字符串。
- RSV 光 `AddWarps` 就有 **59 条**；SVE 的 `Music`/`Light`/`DayTiles`/`NightTiles` 正是这个形状。
- **不需要 tmx**，原版地图在输入树里就是 `.tbin`。
- 顺带覆盖 `Data/Locations.<loc>.Music[].Condition` 的替代面（约 12 条）。

**② `tmx → tbin`（紧随）**

引 `TMXTile.dll` + `xTile.dll`。**这是 RSV 那 111 条的唯一前置。**
先做 **`Load(.tmx)`（整图换，比 `EditMap.FromFile` 简单）**。

**③ `MapTiles` 的 `SetProperties`（无 `Random`）**

RSV 133 条 / 语料 B ~76 条。走 ① 那条安全路径（只改 `StaticTile.Properties`）。
**照抄 CP 的两个细节**：空位报错不创建、`AnimatedTile` 会被替换成 `StaticTile`。

**④ `FromFile` 面积叠加（最后）**

语料 B 480 条、RSV 9 条。**先只做「整图 + `PatchMode:Replace`、无 FromArea/ToArea」**，
它退化成「用源图覆盖目标图」。带 `FromArea`/`ToArea` 的真叠加要复刻 SMAPI 的
`AssetDataForMap.PatchMap`（tilesheet `z_` 前缀改名、`ExtendMap` 扩图、三态 PatchMode、AnimatedTile 重建）。

**不动**：`NeedsRuntime` 的 75 条（运行时刷新语义，静态 XNB 表达不了）。

---

## 9. ⚠️ 订正：`PassiveFestivalData.MapReplacements` 覆盖面 ≈ 0

本项目计划曾把它列为 `EditMap` 的原生替代落点。**不该列**：

- 它的语义是「该被动节日期间，**把对地点 A 的引用整体重定向到地点 B**」——**整地点换图**。
- RSV 那 111 + 16 条**绝大多数是图块/属性/传送点级别的局部改动**，粒度对不上。
- 唯一沾边的「整图替换」那 15 条**也用不着它**——原版本来就按命名约定加载节日地图
  （`Maps/Town-EggFestival` 等），**直接把 tmx 转出的 tbin 写进对应资产名即可**。
- 走 `MapReplacements` 反而要额外造一个地点 + `Data/Locations` 条目。

**结论：不列为替代路径**，仅作为「个别 mod 的整图按日/按节日切换」的备选提及。
