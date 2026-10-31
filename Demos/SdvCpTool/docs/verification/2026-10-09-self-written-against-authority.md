# 自研算法对着权威实现核一遍（2026-10-09）

本工具里有几处**不是从 CP 抄来的**、而是自己写的算法 —— CP 那边只是转调 SMAPI/游戏。
自己写错**不会有人报错**，只能自己对着权威实现逐行核。权威同样是反编译的：

```bash
cp "<游戏目录>/StardewModdingAPI.dll" D:\tmp\cp\
cd D:\tmp\cp && ilspycmd StardewModdingAPI.dll > smapi.cs     # 24891 行
```

## 1. `EditImage` 的 `Overlay` 合成 —— 对上

SMAPI `AssetDataForImage.PatchImageImpl`（`smapi.cs:21729+`）：

```csharp
if (source.A >= 5) {
    if (target.A < 5 || source.A == 255) { out = source; continue; }   // 两条快速路径
    float f = 1f - source.A / 255f;
    out = new Color((int)(source.R + target.R * f),
                    (int)(source.G + target.G * f),
                    (int)(source.B + target.B * f),
                    Math.Max(source.A, target.A));
}
```

逐项对过 `Merging/EditImageApplier.cs`：

| 项 | SMAPI | 我们 |
|---|---|---|
| 透明度阈值 | `A >= 5` | `MinOpacity = 5` ✓ |
| 目标透明 ⇒ 直拷源 | ✓ | ✓ |
| 源不透明（`A == 255`）⇒ 直拷源 | ✓ | ✓ |
| 混合系数 | `1 - srcA/255f` | 同 ✓ |
| 结果 alpha | `max(srcA, dstA)` | 同 ✓ |
| 颜色夹紧到 0..255 | `new Color(int,…)` 内部夹紧 | 显式夹紧 ✓ |
| **只动首/末非透明源行之间**的区域 | ✓ | ✓ |
| 源区整块透明 ⇒ 什么都不做 | ✓ | ✓ |

## 2. `EditMap` 的 `PatchMap` 缺省区域 —— 对上

SMAPI `AssetDataForMap.PatchMap`（`smapi.cs:21852+`）：

```csharp
sourceArea ??= new Rectangle(0, 0, sourceMapArea.Width, sourceMapArea.Height);
targetArea ??= new Rectangle(0, 0, Math.Min(sourceArea.Width,  targetMapArea.Width),
                                    Math.Min(sourceArea.Height, targetMapArea.Height));
```

`Maps/MapPatcher.cs:71-72` 与之一致（源缺省 = 整张源图；目标缺省 = `min(源, 目标)`）。

## 2.1 两个枚举也对上

```csharp
public enum PatchMapMode { Overlay, ReplaceByLayer, Replace }   // 下标 1 = ReplaceByLayer
public enum PatchMode    { Replace, Overlay }
```

`PatchMapMode` 的**下标 1 是 `ReplaceByLayer`** —— 与 CP `PatchLoader` 里那个
`(PatchMapMode)1` 的缺省一致，也与 `MapApplier` 认的三个名字一致 ✓；
`PatchMode` 两个值与 `PatchImageMode` 一致 ✓。

## 3. `PatchMap` 的**主体**（2026-10-09 补核）—— 对上

上一轮只核了缺省区域。这一轮把 `PatchMap` 主体逐段比过
（SMAPI `smapi.cs:27403-27560` ↔ `Maps/MapPatcher.cs:59-182`）：

| 项 | SMAPI | 我们 |
|---|---|---|
| 图集匹配 | 按 **Id** 找；找不到**或** `NormalizeTilesheetPathForComparison` 不等 ⇒ 新建 | 同 ✓ |
| 新图集命名 | `z_<id>`，撞名再 `_2`/`_3`… | 同 ✓ |
| 新图集内容 | 照抄 `ImageSource`/`SheetSize`/`TileSize` + `Properties` | 同 ✓ |
| 图层匹配 | 按 Id；源里有而目标没有 ⇒ **新建**（尺寸取 `target.Layers[0].LayerSize`）| 同 ✓ |
| 图层属性 | `value2.Properties.CopyFrom(layer.Properties)` | 同 ✓ |
| 图块属性 | `tile2.Properties.CopyFrom(tile.Properties)` | 同 ✓ |
| 静态块 | 照抄索引/混合模式 | 同 ✓ |
| 动画块 | 逐帧换成目标图集的静态块，保留 `FrameInterval` | 同 ✓ |
| 写入规则 | `if (tile2 != null \|\| ReplaceByLayer \|\| Replace)` —— Overlay 下源里为空**保留目标** | 同 ✓ |
| `Replace` | 源里没有的图层，在**写入位置**上清空 | 同 ✓ |
| `ExtendMap` | 每层撑到 `max(min, 当前)`；返回值表示有没有动 | 同 ✓（额外做 RLE 重建，见下）|

**已知的实现差异（等价）**：SMAPI 用反射写 `xTile` 的私有字段
（`m_tiles` / `m_tileArray` / `m_layerSize`），我们直接改模型的 `Tiles` 再
`LayerRle.Rebuild` —— tbin 的 RLE 是模型里的一等公民，不重建就写出与模型对不上的二进制。

**已知的判据差异（更宽松，未处理）**：SMAPI 在图块引用了一个**不在 `source.TileSheets` 里**
的图集时会 `dictionary[tile.TileSheet]` 直接抛；我们用 `sheetMap.TryGetValue` 取不到就**沿用原 Id**。
前者会崩、后者写出一个可能对不上的图集 Id —— 实测语料里没有这种补丁，先记着。

## 4. 这一轮仍然没核到的

`EditMapPatch` 之外的**写入器**没逐段核：`EditDataMerger`（`MoveEntries` / 键改写）、
`LoadApplier`、`AssetCreator`。要核就得按同样方式对着 CP 反编译逐段比。
