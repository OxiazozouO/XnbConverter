# 截图反查地图 / 图块 / 补丁来源

日期：2026-10-25  
状态：设计已确认，开始实现

## 目标

输入一张 Stardew Valley 游戏截图，自动反查：

- 地图资产名、地图尺寸；
- 截图对应的地图格坐标；
- 该格每一层的图块：图层、图集、索引、源像素区域、动画帧；
- 该格的 `Action` / `TouchAction` / `Warp`；
- 所有覆盖该格的 CP `EditMap` 补丁：包、源文件、index、`When`、`FromFile`、`ToArea` / `MapTiles`；
- 与原版树的逐层差异（可选）。

定位不确定时输出候选地图/坐标和标注 PNG；可通过 `--map` / `--tile` 强制重跑。

## 非目标

- 不做 GUI，不做给人点的确认页；产物是 CLI + JSON + PNG，供代理/脚本读取。
- 第一版不判断“最后写入者”，只按包序列出所有覆盖补丁。
- 第一版按整数缩放、16×16 图块设计；非整数缩放退化为候选。

## 接入

CLI 子命令：

```text
SdvCpTool.Cli.exe trace-screenshot --image <png> --tree <树> --mods <Mods>
    [--base <原版树>] [--season spring] [--crop x,y,w,h]
    [--map <地图名>] [--tile x,y] [--out <目录>]
    [--rebuild-cache] [--min-score 0.6]
```

核心单元：

- `Maps/MapRenderer`：tbin → 1x RGBA；按图层顺序合成；动画取第一帧；季节图集替换。
- `Tracing/TileFingerprint`：16×16 图块的 4×4 块平均值量化指纹。
- `Tracing/MapFingerprintIndex`：渲染全部地图、建 `指纹 → (地图,x,y)` 索引；磁盘缓存。
- `Tracing/ScreenshotLocator`：缩放 1–4、相位 0–15 抽样投票、验证、候选排序。
- `Tracing/TileInspector`：指定坐标的图层/图集/动画/地图属性/原版差异。
- `Tracing/PatchProvenance`：扫描 CP 包，列出覆盖该格的 `EditMap` 补丁。
- `Tracing/TraceReport`：JSON、txt、标注 PNG、地图上下文 PNG、图层裁剪 PNG。

## 匹配

1. 可选裁剪；高饱和红/橙标注像素标记为忽略。
2. 对缩放 1/2/3/4：按相位 0..15 在截图中央区域抽 8×8 图块，算指纹，在索引里投票 `(地图, 偏移)`。
3. 取票数前若干候选，用全部抽样图块复核得分。
4. 最高分 ≥ `--min-score` 且领先候选足够多 → `matched`；否则 `candidates`，退出码 2。
5. 有 `--map` 时只在该地图内搜索偏移；有 `--tile` 时直接使用坐标。

缓存放在 `<out>/.trace-cache/`，key 由树的绝对路径和地图/图集文件清单指纹决定；首次建索引几分钟，之后秒级。

## 输出

- `trace.json`：schema 1，包含 `status`、`confidence`、`match`、`candidates`、`layers`、`mapProperties`、`vanilla`、`patches`。
- `trace.txt`：一屏可读结论。
- `annotated.png`：截图叠淡网格、目标格红框。
- `map-context.png`：目标格周围 15×15 格地图渲染 + 坐标刻度。
- `layers/<id>.png`：每层该格放大 8 倍；动画帧横向拼接。

退出码：0 已匹配；2 只有候选；3 参数/读图错误。

## 补丁追溯

- 扫描 `--mods` 下 CP 包（展开 Include），找目标等于该地图的 `Load` / `EditMap`。
- `EditMap` 覆盖判断：`MapTiles[].Position` 命中，或 `ToArea` 包含该格。
- 输出包名、UniqueID、源文件、index、action、`When`、`FromFile`、`ToArea`、`PatchMode`、`Update`、`coversTile`。
- 无补丁覆盖时明确标注“来自地图底图 / Load”。

## 测试

- `MapRenderer`：合成 tbin + PNG 图集，验证图层合成、动画首帧、季节替换。
- `TileInspector`：验证图层/索引/源矩形/动画帧/地图属性/原版差异。
- `ScreenshotLocator`：渲染一张合成地图，截取区域，叠加红框/噪声/缩放 1/2/4，验证地图和坐标。
- `PatchProvenance`：临时 CP 包，`ToArea` / `MapTiles` 覆盖与不覆盖各一。
- 实机回归：用 2026-10-24 的五张截图跑一遍，人工核对地图/坐标。

## 风险

- 动态图块、角色遮挡、HUD 会降低得分；靠多数票和 `--crop` 兜底。
- 非整数缩放不在第一版保证范围。
- 全地图索引首次构建耗时；缓存后只读。
