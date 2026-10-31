# 三处修改后的全量产出：改前 / 改后逐字节对比 + 五项检查

日期：2026-10-09

这一轮改了三处（都在同一天，各自单独记了文档）：

| # | 改动 | 文档 |
|---|---|---|
| 1 | `EditMap` 的 `TextOperations` 原来把整张地图属性表按字符串重写 | `research/2026-10-09-textoperation-map-properties-rewrote-whole-table.md` |
| 2 | `EditMap` 五段改动的**顺序**与 CP 不一致 | 同上那轮的 commit `269277c` |
| 3 | **模组地图引用的「包内同级图集」没被搬进产出树** | `research/2026-10-09-mod-local-map-tilesheets.md` |

## 产出规模（全量 32 包，同一顺序）

| | 改前 `chain` | 改后 `chain3` |
|---|---|---|
| 文件数 | 13,301 | **13,335** |
| `Data/Locations` 地点 | 252 | 252 |
| `Maps/` 地图（`.tbin`）| 376 | 376 |
| warp 条目 | 995 | 995 |

## 改前 / 改后逐字节对比（`tools/diff-trees.py`）

```
只有旧树有：0        只有新树有：34        内容不同：1
```

- **只有旧树有 0** —— 没有丢任何东西；
- 只有新树有的 34 个 = **17 个图集 × 2**（`.png` + `.config`）：
  鸟会那张地图的 9 个（Binoculars / Hummingbird / Perch / PlasticTube / SeedHuller /
  SpellNSpeak / StoneBath / WoodenHopper / WoodenPlatform）、
  `*_exterior` 四季、`*_JasPartyTiles` 四季；
- 内容不同 1 个 = `ContentHashes.json`（新增资产的 key，预期内）。

⇒ **没有任何地图文件被改写**，也没有别的资产被动过。

**SVE 那一步单独比：10,249 个文件 0 差异** —— 三处修改对 SVE 全是 0 影响
（SVE 的地图图集都走 `Load Maps/<图集>`，本来就在树里）。

## 五项检查（都在改后的最终树上跑，都带原版基准）

| 检查 | 产出 | 原版基准 | 结论 |
|---|---|---|---|
| 每张地图引用的图集图片是否存在（`check-map-tilesheet-assets.py`）| 445 张地图 / 1792 处引用 / **0 种找不到** | 259 张 / 663 处 / 0 | **产出新增 0**（改前 12 种）|
| 每条 `Warp` 的目标地点（`check-map-warps.py` 检查一）| 376 张 / 995 条 / 3 个查不到 | 190 张 / 786 条 / 3 个 | **产出新增 0** |
| 每个地点的 `CreateOnLoad.MapPath`（检查二）| 240 个地点 / 0 找不到 | 79 个 / 0 | **产出新增 0** |
| 季节性图集引用（`check-map-tilesheets.py`）| 499 处 / 0 问题 | 147 处 / 0 | 两边 0 |
| 语料 tmx 的 tileset 图片（`check-tmx-tilesheets.py`）| 1220 处里 **2 种**找不到 | — | 见下 |

最后那 2 种**不是缺口**，是「引用了它们的补丁**根本没被应用**」：

- `spirit_sewer`：只出现在热闹的下水道那两个 `.tmx` 里，而用它们的补丁带 `When` 被拒
  ⇒ 产出树里没有任何地图引用它（树侧检查也是 0）；
- `spring_daisyextras`：由**没装的** `DaisyNiko.SVR3` 提供，引用它的补丁是
  `HasMod |contains=DaisyNiko.SVR3` 门控的。

## 单元测试

**1195 → 1198 通过 / 0 失败**（这一轮新增 3 条：`LocalMapTilesheets` 的
Load 搬运 / 季节族 / 不在包内时不动；`MapApplierTests` 的补丁块搬运是第 4 条，
与其中一条一并计入）。
