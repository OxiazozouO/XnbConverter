# 截图反查地图 / 图块 / 补丁来源

```bash
SdvCpTool.Cli.exe trace-screenshot \
  --image shot.png \
  --tree D:\tmp\full-v11\tree \
  --mods D:\Steam\steamapps\common\Stardew Valley\Mods \
  [--base D:\sdv_tree] [--season spring] [--crop x,y,w,h] \
  [--map Town] [--tile 94,57] [--scale 4] \
  [--out D:\tmp\trace-out] [--cache <目录>] [--rebuild-cache]
```

产物：

- `trace.json`：地图、坐标、每层图块/图集/索引/源矩形/动画帧、地图属性、原版层、覆盖该格的补丁。
- `trace.txt`：一屏结论。
- `annotated.png`：截图 + 网格 + 目标格红框。
- `map-context.png`：目标格周围 15×15 地图 + 网格。
- `layers/<层>.png`：该格每层放大；动画帧横向拼接。

## 定位

- 全树建“图块指纹 → (地图,x,y)”索引，缓存在 `%LOCALAPPDATA%\SdvCpTool\TraceCache`；树变了自动失效。
- 截图按缩放 1–4、屏幕像素相位投票；红色标注只认 **纯红 (R≥220, G/B≤60) 的闭合框**，其余红/棕像素忽略。
- 有红框时目标格取框中心；没有红框时取截图中心。
- 默认 `--min-score 0.6`；低于阈值退出码 2，只输出候选，不猜。
- 已知限制：非整数缩放（截图被聊天工具缩放/重采样）不在第一版保证范围；用 `--crop` / `--map` / `--scale` 缩小范围。

## 追溯

- `--mods` 会扫所有 CP 包（展开 Include），列出目标为该地图的 `Load` / `EditMap`。
- `*` 表示该补丁的 `ToArea` 或 `MapTiles` 覆盖了目标格；没有 `*` 的是同地图但没覆盖该格。
- 无任何 CP 补丁覆盖时写“来自地图底图 / Load”。
