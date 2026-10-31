# 地图 tbin 里图集名带着 `.PNG` ⇒ 整张地图创建不出来

日期：2026-10-09

**怎么发现的**：**用户实际游玩**时发现两个症状 ——

1. **mod 新增的地图全都看不到**
2. **新地图的传送代码不触发**

对照游戏日志（`%APPDATA%\StardewValley\ErrorLogs\`）：

```
ERROR game] Location 'Woods' failed to load seasonal asset name 'Maps\spring_outdoorsTileSheet2.PNG' for tilesheet ID 'v16_Outdoors2'.
ERROR game] Couldn't create the 'Woods' location. Is its data in Data/Locations invalid?
```

**症状与日志对得上**：地点创建失败 ⇒ 地图进不去 ⇒ 传送点自然也不会触发。

## 根因（两处，缺一不可）

### 1. `TmxToTbin` 把 `<image source>` 原样照抄

SVE 的 `Assets/maps/locations/Woods2.tmx` 里，那张图集写的是：

```xml
<tileset firstgid="11659" name="v16_Outdoors2" ...>
  <image source="spring_outdoorsTileSheet2.PNG" width="256" height="1120"/>
```

**带 `.PNG`（大写）** —— 同一文件里其他图集都是无扩展名或小写 `.png`。

### 2. `TBin10.RemoveTileSheetsExtension()` 只认小写

它**唯一的调用点**是 `XNB.cs:566` 的 `case ".tbin"`（**打包 tbin→xnb 时**），
所以设计上"打包时统一剥掉扩展名"是对的，但实现是：

```csharp
tileSheet.Image = tileSheet.Image.Replace(".png", "");   // 大小写敏感 ⇒ .PNG 溜过去
```

## 依据：xTile 的规则是「剥掉图片扩展名」

抽查 **150 张原版地图**的 `TileSheet.Image`，带 `.png/.jpg/.bmp` 的是 **0 个**。

⇒ 症状也印证：**对原版地图的 `EditMap` 改动是正常的**（那些基于原版 tbin，
`ImageSource` 本来就没扩展名），只有 **`Load` 一份 `.tmx`** 换来的新地图全坏。

## 修法（改在 `D:\XnbConverter`，提交 `bdd45b7`）

`TBin10.RemoveTileSheetsExtension()` 改成**只剥图片扩展名**且**大小写不敏感**。
改上游那一处的原因：它是 `XNB.cs:566` 打包路径的唯一入口，**修好一处、所有打包受益**。

## 验证

拿产出树里那份**确实带 `.PNG`** 的 `Maps/Woods.tbin`（SVE 的，**222007 字节**），
用**修好的 exe** 打包再解包：

| | 字节 | ImageSource |
|---|---|---|
| 输入（产出树的 tbin） | 222007 | `spring_outdoorsTileSheet2.PNG` |
| **回包解出的 tbin** | **221979** | **`spring_outdoorsTileSheet2`** ✓ |

## 影响面（已量化）

扫全语料 32 个产出树的 `Maps/*.tbin`（**6266 个**）：

```
带图片扩展名的 ImageSource：227 个
   182  大型扩展---SVE
    26  地图资源---lnh额外区域
     6  地图资源---lnh的步行去沙漠
     5  地图资源---社区中心扩建
    ...
```

⇒ **SVE 占 80%**，与用户报的症状一致。

⚠ **这是"静态验证全绿、实机才发现"的典型**：

- 回包验证（3794 成功 / 0 失败）**过了** —— 带 `.PNG` 的 tbin 打得出来也能读回来；
- 探针（`Helper.GameContent.Load` 逐个加载 3324 个资产，**3324/0**）**也过了** ——
  反序列化没问题；
- **坏在语义**：游戏拿到 tilesheet 名后**按那个名字去拼资源路径**，
  名字里带着 `.PNG` 就找不到东西。**只有真的把地图建起来才会暴露。**

## 两边的关系

- **`D:\XnbConverter`** 侧已修（`bdd45b7`）；
- **`SdvCpTool` 侧不用改** —— 产出树里的 `.tbin` 仍带扩展名（那是中间产物），
  **打包成 XNB 时会被剥掉**。
- ⇒ ⚠ **用户手上那份 `out_publish\XnbConverter.Cli.exe` 是旧构建，要重新发布**，
  否则修复不生效。
