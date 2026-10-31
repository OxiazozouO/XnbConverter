# 字段名要按**不区分大小写**匹配（打包器与游戏都这么认）

日期：2026-10-08

## 1. 现象

SVE 的 `Data/WorldMap` **回包失败**（这正是它剩下的最后一个失败文件）：

```
[ERROR] … 无效的JSON文件格式: Cannot deserialize the current JSON object (e.g. {"name":"value"})
        into type 'System.Collections.Generic.List`1[StardewValley.GameData.WorldMaps.WorldMapTooltipData]'
        because the type requires a JSON array …
```

## 2. 根因：三处按**字面**匹配字段名

mod 的原文（`code/Locations/WorldMap.json`）：

```json
"TargetField": [ "Valley", "MapAreas", "Mountain", "ToolTips", "Mines" ],
"Entries": { "PixelArea": { "X": 220, "Y": 14, "Width": 9, "Height": 9 } }
```

而模型（`WorldMapAreaData.cs:31`）里的字段名是 **`Tooltips`**（小写 t）。
我们按字面查 ⇒ 三处一起踏空：

| 位置 | 字面查的后果 |
|---|---|
| `EditDataMerger.TargetFieldKinds` | 查不到 `ToolTips` ⇒ 整条路径推不出类型（`expected = null`） |
| `EditDataMerger.Descend` | 已有键是 `Tooltips`，字面查不到 ⇒ **新建了一个 `ToolTips` 对象** |
| `GameDataValidator` | 按模型字段名 `Tooltips` 去查数据键，字面查不到 ⇒ **这一处什么都没报** |

于是产出里多出 `Valley.MapAreas[7].ToolTips = { "Mines": {…}, "AdventureGuild": {…} }`，
**形状是对象**；打包器照认这个键（见下），按 `List<WorldMapTooltipData>` 校验 ⇒ 失败。

## 3. 判据：打包器对字段名**大小写不敏感**（探针实测）

拿产出树里的 `Data/WorldMap.json`（连 `.config`）做三个变体，其余字节不动：

| 变体 | 打包 |
|---|---|
| A 原样（`ToolTips` 是对象） | **失败 1** |
| B 对象→数组，**保留 `ToolTips`** 这个拼写 | **成功** |
| C 对象→数组，并改名 `Tooltips` | **成功** |

B 成功就说明了：打包器把 `ToolTips` 认成了 `Tooltips` 这个字段（不敏感），
只是形状不对才报错。所以「不敏感」不是我推的，是打包器的行为。

## 4. 改法

| 改动 | 边界 |
|---|---|
| `GameDataSchema.Types` 的字段表改成 `OrdinalIgnoreCase`（生成器一并改，可重生成） | 外层的**类名**表保持 `Ordinal`（类名是 C# 标识符，没有大小写不同的情形） |
| `TargetField` 下钻：**字段名**按不敏感找已有键，命中则沿用**原拼写** | **字典键仍精确匹配** —— 键是数据本身，大小写不同就是两个键 |
| `GameDataValidator` 取数据键按不敏感（报的是**模型**字段名） | 名字里全是标量字段的类本来就不进表，无影响 |

「字典键仍精确」不是随手定的：`Descend` 靠 `expected[i-1]` 是不是容器来区分这一段是
**键**还是**字段名**，只有后者才放宽；模型认不出的资产（`expected == null`）整条不放宽，
保持原行为 —— 那是**如实留缺口**，不是「猜着覆盖」。

## 5. 受控对比（同机、同包、改动前后各一次）

SVE 的 Data 资产 905 个一起打包：

| | 改动前 | 改动后 |
|---|---|---|
| 打包 | 成功 904 / **失败 1** | **成功 905 / 失败 0** |

`Data/WorldMap` 逐字段结构差异 **只有该变的那些**：

```
- Valley.MapAreas[7].ToolTips            （错的、多出来的对象，没了）
- Valley.MapAreas[8].ToolTips            （同上）
  Valley.MapAreas[7].Tooltips[0].PixelArea.X  216 → 220
  Valley.MapAreas[7].Tooltips[0].PixelArea.Y   23 → 14
  Valley.MapAreas[7].Tooltips[2].PixelArea.X  201 → 213
  Valley.MapAreas[7].Tooltips[2].PixelArea.Y   23 → 14
  Valley.MapAreas[8].Tooltips[0].PixelArea.Height 17 → 6
```

—— 值落进了**已有的** `Tooltips` 数组（按 `Id` 命中 `AdventureGuild` / `Mines` / `Railroad`），
与 mod 的原意一致。`written` / `writeFailed` / 判定分布逐包不变（只改形状与落点）。

## 6. 暴露面（实测）

全语料 32 个包、`TargetField` 里**能对上模型**的字段段共 **756** 段，其中拼写与模型不一致的：

```
   3  大型扩展---SVE   Data/WorldMap   ToolTips → Tooltips
```

**只有这一处**（3 段是同一条路径在 3 个 patch 里出现）。这与「全量语料逐包计数零变化」一致 ——
这条改动的收益就是把这一个包、这一个资产救回来，没有别的连带。

（扫法：按 `GameDataSchema.cs` 的两张表逐段走，与转换器同一份事实。）

## 7. 未确证

`ToolTips` 这种拼写在**游戏里**是否也被认（打包器不敏感是间接证据；游戏侧没实地验过）。
我们没有把 mod 的拼写「纠正」成模型拼写 —— 只在**已有键**存在时沿用它，
新建键仍用 mod 的写法，所以这条疑问不影响产出。
