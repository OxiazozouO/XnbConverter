# 雨天/昼夜地图叠加 → DayTiles / NightTiles

日期：2026-10-28

## 1. 这次补的洞

`[CP] Stardew Valley Expanded` 的 7 张雾地图补丁长这样：

```json
{
  "Action": "EditMap",
  "Target": "Maps/Forest",
  "FromFile": "Assets/Maps/MistEffects/Forest_Mist.tbin",
  "When": { "MistEffects": "true", "Weather": "Rain, Storm" },
  "ToArea": { "X": 0, "Y": 0, "Width": 120, "Height": 120 }
}
```

`MistEffects` 能离线折成真，剩下 `Weather: Rain, Storm`；旧判定把它归为
`LocalEdit`，产出地图里**没有雾**。

原版没有条件图层，但 `GameLocation.resetForPlayerEntry` 有两个地图属性：

- 晴天白天：应用 `DayTiles`；
- 夜晚**或正在下雨**：应用 `NightTiles`。

所以把叠加层在目标地图里先写成**透明图块**，再用：

- `DayTiles`：同一批格子换回透明索引；
- `NightTiles`：同一批格子换回源图块索引；

就能得到“雨天显示、晴天白天不显示”的近似。代价已写进判定理由：
**晴天夜里也会显示**（原版这两个属性共用“夜晚/下雨”分支）。

## 2. 判定

新增 `WeatherMapOverlay.CanApproximate`，只接：

- `Action: EditMap`，必须有 `FromFile`；
- 条件折叠后只剩 `Weather`（值只来自 `Rain` / `Storm` / `GreenRain`）或**纯夜间/纯白天** `Time`
  （`{{Range: 1900, 2500}}`、`1900 2500`、`1900, 2500`；夜间起点 ≥ 1800、终点 ≤ 2600；
  白天起点 < 1800、终点 ≤ 1900）；
- 白天 `Time` 可带 `Weather: Sun, Wind` 作为“非雨天”旁证；
- 不带 `Weather: island` 这类地点上下文；
- 补丁只有 `FromFile` / `FromArea` / `ToArea` / `PatchMode: Overlay|ReplaceByLayer`
  （带 `MapTiles` / `MapProperties` / `AddWarps` / `TextOperations` 的仍走旧路径）；
- `PatchMode: Replace` 不接；
- 配偶房间地图不接（`ApplyMapOverride` 会复制图块、还会删 `DayTiles/NightTiles`）。

命中后判 `ConvertedNoCondition`、`Channel.DayNightTiles`，理由里明确写“近似”。
`PatchVerdict.DayNightVisibleAtDay` 记录朝向（`false` 夜间/雨天，`true` 白天），
逐条报告 JSON 也带 `dayNightVisibleAtDay` 字段。
`WritePolicy` 原有的“ConvertedNoCondition 地图可写”分支自然接住它。

## 3. 写入

`MapApplier.ApplyWeatherDayNightOverlay`：

1. 把源叠加层里每个非空图块换成一个**同一图集内的透明图块索引**，
   通过 `MapPatcher.PatchMap` 建出目标图层；这样目标图层初始不可见；
2. 夜间模式：`NightTiles` 写源图块、`DayTiles` 写透明索引；
   白天模式反过来：`DayTiles` 写源图块、`NightTiles` 写透明索引；
3. 已有 `DayTiles` / `NightTiles` 是**追加**，不覆盖；
4. 目标已有同名图层时，改用下一个 `AlwaysFrontN`（或 `BackN` / `FrontN` /
   `BuildingsN`），避免盖掉原地图图层；
5. 只克隆 `ToArea` 内的图块：区域外若用了另一张图集，不会把它带进目标地图，
   也不会因为没算那张图集的空白索引而崩；
6. 源图里的 `Paths` / `Schedule` / `MainEvent` / `Set-Up` 是数据/指令层，不是视觉层：
   跳过并在写入笔记里报出来，目标地图保留自己原有的那一份。

透明索引从图集 PNG 的 alpha 通道找：

- SVE 7 张里 5 张本身有全透明图块（例如 `z_Forest_mist` 的索引 4）；
- `z_AdventurerSummit_Mist` 和 `Highlands_Fog` 没有透明图块 ⇒
  在产出图片末尾追加一行透明像素，索引 = 原总格数；
- `z_Forest_mist.png` 实际宽度 906，而 `SheetSize` 是 56×94（56×16=896）：
  按 `SheetSize` 取格，右侧多出的 10 像素不参与索引，不能因为“宽度不整除”判死。

## 4. 实跑

用 SVE 内容包 + 7 张目标地图跑两趟（`convert`，包含 `Load`）：

```
Maps/Railroad              ConvertedNoCondition DayNightTiles written=true
Maps/Backwoods             ConvertedNoCondition DayNightTiles written=true
Maps/Mountain              ConvertedNoCondition DayNightTiles written=true
Maps/Forest                ConvertedNoCondition DayNightTiles written=true
Maps/Custom_ForestWest     ConvertedNoCondition DayNightTiles written=true
Maps/Custom_AdventurerSummit ConvertedNoCondition DayNightTiles written=true
Maps/Custom_Highlands      ConvertedNoCondition DayNightTiles written=true
```

输出地图里能读到：

| 地图 | 新图层 | DayTiles 字符数 | NightTiles 字符数 |
|---|---|---:|---:|
| Railroad | `AlwaysFront4` | 67,788 | 66,678 |
| Backwoods | `AlwaysFront2` | 45,077 | 45,967 |
| Mountain | `AlwaysFront10` | 118,488 | 133,578 |
| Forest | `AlwaysFront4` | 106,761 | 120,915 |
| Custom_ForestWest | `AlwaysFront6` | 622,724 | 611,614 |
| Custom_AdventurerSummit | `AlwaysFront3` | 66,040 | 64,930 |
| Custom_Highlands | `AlwaysFront4` | 622,751 | 611,641 |

（字符数是属性串长度；NightTiles 少了“源图块本来就是透明”的格子，所以可能更短。）
输出 tbin 回读成功，图层顺序/索引正常。

另外用 `[CP] Lnh's Community center item output points` 的纯夜间 `Time` 补丁实跑：

```
Maps/Custom_FishTank       ConvertedNoCondition DayNightTiles written=true
Maps/Custom_BulletinBoard  ConvertedNoCondition DayNightTiles written=true
```

两张输出地图都回读到 `DayTiles` / `NightTiles`，图层数分别 9 / 7。

RSV 的昼夜成对补丁（`Time 0600-1900` + `Time 1900-2600`）也实跑：

```
Maps/Custom_Ridgeside_RSVCliff   2/2 written=true
Maps/Custom_Ridgeside_RSVTheRide 2/2 written=true
```

`RSVCliff` 的白天源图 5 层、夜间源图 3 层分别落到不同新图层：
白天层 `Back1` / `Back3` / `Buildings1` / `Front1` / `AlwaysFront1` 与
夜间层 `Back4` / `Front2` / `AlwaysFront2` 的 `DayTiles` / `NightTiles` 互为透明底，
源图里的 `Paths` / `Schedule` 被跳过并记入写入笔记。

## 4.1 写进全量树

为了避免重跑 232 个包，另建一个**只包含这 13 条天气/时间补丁**的临时 CP 包
（`D:\tmp\weather-apply`），以全量树的 11 张目标地图为输入跑 `convert --pass edits`：

```
13 条补丁：13 ConvertedNoCondition / 13 DayNightTiles / written=13 / failed=0
改到 11 张唯一地图（SVE 7 + RSV 2 + Lnh 2 个目标，其中两张图各两条补丁）
```

把 11 张地图 + 6 张被追加/新增的图集复制回 `D:\tmp\full-v11\tree`，
再用打包器回包：

```
成功: 17  失败: 0
```

17 个 XNB 已覆盖到 `D:\tmp\full-v11\packed\Maps` 与 `D:\tmp\gv\Content\Maps`。

⚠ 这只更新了地图与对应图集；**没有重新生成整树 zip**，`sdv-content-baked.zip`
仍是旧包。

## 4.2 AmbientLight 昼夜属性

新增 `Channel.DayNightAmbient` 与 `MapAmbientRewriter`：

- 纯 `MapProperties.AmbientLight` + 白天/夜间 `Time` 条件可写成原版
  `AmbientLight`（白天）/ `AmbientNightLight`（夜晚）；
- `When` 被删除，原版只读这两个属性；
- 原版只有两个状态，六段时分的 SVE 城堡会被压成最后一段的白天/夜间值。

实跑 `D:\tmp\ambient-apply`：12 条候选，8 条写入、4 条因目标/配偶房限制仍是 `LocalEdit`。
写入的两张地图：

```
FarmCave: AmbientLight=255 255 255, AmbientNightLight=90 90 35
Custom_CastleVillageOutpost: AmbientLight=90 90 90, AmbientNightLight=200 200 200
```

两张地图已覆盖回全量树，并回包 2/0。

## 5. 边界

- **不是精确天气图层**：晴天夜里也会显示雨雾/雨效；
- 只接 `Rain` / `Storm` / `GreenRain`（三者在 `LocationWeather.IsRaining` 里都算下雨）；
  `Sunny`、`Sun`、`Wind`、`Weather: island` 仍走旧判定；
- `Time` 只接**纯夜间**（起点 ≥ 1800、终点 ≤ 2600）或**纯白天**（起点 < 1800、
  终点 ≤ 1900）；跨昼夜/黄昏区间不接；
- 白天模式的叠图在**雨天白天会隐藏**（原版雨天走 `NightTiles`）；没有
  `Weather: Sun, Wind` 旁证的纯白天补丁，雨天白天原本应显示，这是已知近似；
- 不区分室内/室外：原版两个属性对所有地点生效，室内天气叠加也会在夜里显示；
- 全语料除 SVE 外还有 6 条同形状候选：3 条 `Rain/Storm`（Bear Family 2 条、
  Eli and Dylan 1 条）和 3 条 `GreenRain`（Bear Family 2 条、Eli and Dylan 1 条）
  都会走这条通道；
- 源图层必须是静态图块；动画叠加层会判写入失败并报出来，不硬写；
- 没有启动游戏；以上为 tbin 写入/回读与报告验证。

单测新增 14 条（8 条判定 + 4 条地图写入 + 2 条 Ambient 改写），
完整单测 **1688 通过，0 失败**。
