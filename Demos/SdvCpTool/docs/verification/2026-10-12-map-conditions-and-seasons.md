# 地图条件与季节：哪些能烤、哪些只能丢（附实测）

日期：2026-10-12
起因：实机自检（`tools/GameSmoke/`）发现**原版地图的 warp 比原版少**，玩家看到的是
「**地图之间的传送不见了**」。

## 1. 现象与量化口径

用 `Warp` 属性里的五元组计数（与 `tools/check-map-warps.py` 同一份正则），
**我们的产出树 vs 原版树**逐图对比（脚本 `D:\tmp\warp_diff.py`）：

| 地图 | 原版 | 我们 |
|---|---|---|
| `Summit` | 8 | **2** |
| `AdventureGuild` | 10 | **5** |
| `JoshHouse` | 15 | **9** |
| `Forest-IceFestival` / `2` | 9 | **5** |
| `ArchaeologyHouse` | 29 | **26** |
| `ManorHouse` / `AnimalShop` / `Mountain` | … | 各少 2~4 |

共 **9 张**。（`ArchaeologyHouse` 同时也在「地点建不起来」名单里 —— 同源。）

## 2. 成因：`EditMap` 的条件处理

`PatchJudge` 里原本是一刀切：**带条件的 `EditMap` 一律判 `LocalEdit`**，并且这道检查
**挡在通道/烘焙判定之后、`JudgeLoad` 之前**。实测全语料被丢掉 **1353 条**，按条件分两类：

| 条件类型 | 条数 | 能不能离线判 |
|---|---|---|
| 配置字段 / `HasMod` / `HasFile` / 命名空间令牌 / **包内 `DynamicTokens`** | 236 | **能**（读包内文件/已装模组即可）|
| 运行时状态（`Season` / `HasSeenEvent` / `HasFlag` / `HasWalletItem` / `FarmType` / `Relationship`） | 1117 | **不能**（依赖存档状态）|

### 已修（2026-10-12 提交 `0b3949d`）

1. **带条件的 `EditMap` 能判真假的就走烘焙通道**（`BakeableForMap`）：求值为真 ⇒ 按无条件改 tbin
   （`WritePolicy.IsWritableAction` 本来就收 `EditMap`，烘焙之后由既有的地图写入路径落盘）；
   为假 ⇒ 判「不适用」。
2. **包内 `DynamicTokens` 认成可烘焙**（`ConditionVocabulary.Classify` 收 `packTokens`），
   求值用**写入侧同一个** `DynamicTokenResolver.Expand`，并遵守「有损/依赖运行时就不代」；
   CLI 把解析器与烘焙器**双向接上**（解析器求 `When` 时又要问烘焙器，解析器自带环保护）。
3. **`Season` 条件按本项目既有的季节确定化政策冻结到春天**（值上的 `{{Season}}` 一直这么办）。

**受控对比（prof83 → prof84，232 包同参数）**：补丁级 **变好 287 / 变坏 19 / 新增 0 / 消失 0**。
那 19 条**不是功能退化**：它们是「非春天季节」的变体，原来挂 `NeedsReview`（同样没写），
现在被**明确**判 `NotApplicable`（两边都没写，只是标签与说明变准了）。

## 3. 季节：图集**没丢**，丢的是**地图结构**

| 维度 | 结论 | 依据 |
|---|---|---|
| 季节**图集**（图片） | **不丢**，而且比原版全 | 产出树里 `spring_*/summer_*/fall_*/winter_*` = **224 / 226 / 226 / 228** 张（原版 76/76/78/78）；`*_outdoorsTileSheet` 9 张（原版 4）。游戏有**原生通道**：`GameLocation.updateSeasonalTileSheet` 把图集名开头的 `spring_` 换成当季（源码可查）|
| 季节**地图结构**（某季才有的路/门/墙） | **只能留一季**（现在留春天） | 游戏对**地图**没有「按季换一份」的机制（季节只作用于图集）；CP 那边的四季地图结构是**运行时**由 CP 按当季返回不同地图资产 ⇒ 纯 XNB 树里一个名字只能一份地图 |

⚠ **不能**把四季的结构都烤进去 —— 它们互相冲突（春天的路和秋天的路落在同一格上）。
所以这一档只能在「留哪一季」之间选，现在与值侧政策一致：**春天**。

## 4. 仍未解决：运行时条件（**取舍，不是能修的 bug**）

`JoshHouse`（`{Relationship:Alex: Married/Engaged/Dating…}` 才开的那道门）、
`AdventureGuild`（`{HasSeenEvent:…}`）、`Summit`（`{HasFlag: Island_Resort}`）等
**9 张原版地图的 warp 仍比原版少** —— 这些条件的依据是**存档状态**，转换期无从得知。

纯 XNB 安装里**没有**运行时求值的地方：游戏唯一的条件槽是 `ConditionalDoor`（只管室内门、
全游戏仅 `AdventureGuild` 2 处）。三条路：

1. **保持现状**：丢掉（不破坏进度，但地图"接不起来"）；
2. **一律按成立烤**：门/路都在，但**提前开放**本该解锁的内容（剧透 + 可能破坏进度）；
3. **分类烤**：只烤不会破坏进度的（`FarmType` 专属地图、`Season` 已做），`Relationship`/`HasSeenEvent`/
   `HasFlag`/`HasWalletItem` 维持丢掉。

**未定**（等业主口径）。

## 5. 自检工具这一轮也补了一件事

`tools/GameSmoke/` 加了**逐张地图探针**（把 `AssetProbe` 的 `probe_maps` 自动化，不用人工敲命令）：
扫 `Content/Maps/**/*.xnb`，**真建一个 `GameLocation` 并碰 `.Map`**（1.6 里地图走
`Game1.game1.xTileContent`，用 `Game1.content.Load<Map>` 会得到**假失败** —— 照 `AssetProbe` 的口径），
建不起来再按 `Texture2D` 试一次（贴图不算失败）。
