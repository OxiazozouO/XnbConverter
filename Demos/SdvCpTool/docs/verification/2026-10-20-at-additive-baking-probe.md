# 框架增量烘焙：定向验证（2026-10-20）

工具：C# `SdvCpTool.Cli bake-frameworks`（`FrameworkBaker`；`convert-mods.py --bake-frameworks` 会调用它）。
口径：不是“保留框架的运行时交互”，而是把内容追加成原版数据表的**新行**。
评估见 [`../research/2026-10-20-additive-baking-reassessment.md`](../research/2026-10-20-additive-baking-reassessment.md)。

覆盖：**Alternative Textures / Solid Foundations / Custom Companions / CCCB / Mail Framework**。

## 1. 测试树

以 `D:/tmp/prof-fix26/tree` 的 `Data/*.json` 为底座，只拷了本工具会写的 8 份数据：

```
Data/Furniture.json
Data/AdditionalWallpaperFlooring.json
Data/Crops.json
Data/Objects.json
Data/Fences.json
Data/BigCraftables.json
Data/CraftingRecipes.json
Data/Shops.json
ContentHashes.json
```

→ `D:/tmp/at-bake-test/tree`（小树，不是全量产出）。

## 2. 选中的 AT 包

| 包 | 覆盖类型 | 新增 |
|---|---|---:|
| `[AT] Shyzie's Rugs` | Furniture（10 个 `texture_N.png` 分离文件）| 10 |
| `[AT] Shyzie's Seasonal Rugs` | Furniture | 4 |
| `[AT]Yomi's cute furniture` | Furniture（单张纵向 sheet + `ManualVariations`）| 139 |
| `[AT] Floors and Walls by Mila` | Decoration（Floor/Wallpaper）| 2 条表项（56/48 变体）|
| `Quaint Living - Flower Garden Variety` | Crop | 20 个种子/作物 |
| `[AT] Low Hedge Fence` | Craftable（Fence/Gate）| 32 个栅栏/门 |
| `Delaney's Energetic Craftables` | Craftable（BigCraftable）| 96 个大件 |

合计（不含最后一行在另一棵小树的测试）：

```
家具 153 / 装饰 2 / 作物 20 / 可制作 32
```

## 3. 结果

- 所有新增 `Data/Furniture` 条目：
  - `Texture` 写成**反斜杠**路径（`Mods\...`）——`Data/Furniture` 是斜杠串，
    写成 `/` 会把字段切开（本轮实际踩到并修正）；
  - `SpriteIndex` 的源矩形逐条校验：**153/153 在图内**；
  - 基础条目 `2/3` 字段为 `-1` 时按 AT 的 `TextureWidth/Height` 写成显式尺寸；
    单张 sheet 与 `texture.json` 尺寸不一致时按实际 PNG 尺寸（实测 Oak End Table）。
- 所有新增 `Data/Crops` 条目：`SpriteIndex` 行号逐条校验 **20/20 在图内**。
- 新作物种子复制了基础种子所在的 `SeedShop` 货架（20 条）。
- 新大件：60 条复制了基础配方；其余基础走商店回退，复制了 30 条 `(BC)` 货架行
  （25 个唯一新物品；同一基础在多个商店槽里出现时各复制一份，没有混入 `(O)` 错类型）。
- 5 个 `Delaney` 大件的 `ItemName` 在产出树里找不到基础条目（Hay Hopper/Jukebox/Junimos/
  Mushroom/Seasonal Decor Fall），报告为跳过，没有硬造。
- `ContentHashes.json` 补了新增 PNG 的 `.xnb` 键；每个新增 PNG 都有 `.config`。

## 4. 回包

```
python tools/pack-check.py D:/tmp/at-bake-test/tree --base D:/sdv_tree
```

- AT 五包（另因 `--packs Shyzie` 命中 SF）小树：新增/改过 853 个文件，**成功 433 / 失败 0**；
- SF 单独：新增/改过 333 个文件，**成功 167 / 失败 0**；
- MFM：新增/改过 5 个文件，**成功 5 / 失败 0**；
- CCCB：新增/改过 1 个文件，**成功 1 / 失败 0**；
- **全框架（AT 17 包 + SF 2 + CCCB 2 + MFM 2）**：家具 1059 / 装饰 4 / 作物 20 /
  可制作 150 / 邮件 3 / 捆绑包 27 / 建筑 21；新增/改过 3401 个文件，
  **成功 1722 / 失败 0**（家具的 63/79 高等素材补到 16px 网格后资产数变多）。
- CC 单独（小树带 `Animals`）：166 个 `PetData`；**成功 213 / 失败 0**。
- **`--dedup-cosmetics` 默认 0/0**（只去完全相同）：家具 1059→1024、可制作 150→142，
  合计去掉 **43** 个；耗时约 **6.6 秒**。**原版校准**（`--calibrate-dedup`）：
  原版家具 645 条、去完全相同后 642 条，最近 dHash 距离 **0**（近邻 50940 对），
  最近纯变色 MAD **0.29** ⇒ 推荐阈值 **0**、纯变色容差 **0**。
- **激进参数** `--dedup-threshold 6 --dedup-color-tolerance 18 --dedup-max-pixel-diff 1.0
  --dedup-color-consistency 2.0`：视觉抽查后加了 1% 画面差上限、2% 变色一致性和 20% 单格偏移上限，
  之前误杀的窗帘/书脊/篱笆花色已保留；仍只建议明确接受“相似/换色也算重复”时用。
- 第二次跑新增 **0**（`_frameworks-dedup.json` 记录上轮跳过的 ID；换阈值/容差会重新计算）。

## 5. 幂等

同一命令再跑一次：

```
家具 0 / 装饰 0 / 作物 0 / 可制作 0 / 邮件 0 / 捆绑包 0 / 建筑 0 / 宠物 0
```

没有重复追加；`Data/*.json` 不再变化；本次 0 新增时保留上一份 `_frameworks-report.json`。

## 5.1 C# 其他框架（同一命令）

| 框架 | 小树新增 | 回包 |
|---|---:|---|
| Solid Foundations | 21 建筑（2 包）| **167 / 0** |
| CCCB | 27 条捆绑包（默认 `Pure_Vanilla`）| **1 / 0** |
| Mail Framework | 3 封（含 `Mail.zh-CN`）| **5 / 0** |
| Custom Companions | 166 个 `PetData`（小树带 `Animals`；完整树应更多）| **213 / 0** |

CC 的 `TileSheetPath` 是资产名、不带扩展名；实现里依次试 `.png`/`.xnb`。
原版 `Pet` 固定按 32×32 解释，CC 的 16×16/16×32 帧会报“可能显示错位”；
检视对话/多只/跟随 AI 仍丢失（原版宠物一次只有一只）。

## 5.2 去重样本视觉抽查（`D:/tmp/dedup-visual`）

把全量树和激进去重树的同一批条目按“左=保留、右=被跳过”做成对照图后逐张看：

| 样本 | 像素差 | 肉眼结论 |
|---|---:|---|
| Yomi `S. Pine` 各变体 / `Oak Chair` | 0% 左右 | 基本是同一张图，去重合理 |
| Nano `Bamboo Mat` 2/3/4/5 | 4–11% | 纯换色，去重合理（若只想去同图则默认 0 会保留） |
| Nano `Bed` 1/2/3/4 | 0.34–0.75% | 几乎同一张图，去重合理 |
| Nano `Artist Bookcase` 7/8 | 6.35% | **书脊内容不同**，视觉上是两个变体；激进阈值误杀 |
| Nano `Basic Window` 1/3/4 | 1.94–2.44% | 窗帘细节不同；激进阈值误杀 |
| **Low Hedge `Fall Evergreen` vs `Fall Flowering`** | **8.22%** | **绿色篱笆 vs 带橙色花**，明显不同；激进阈值误杀 |
| Low Hedge `Fall Seasonal` vs `Spring Seasonal` | 8.55% | 季节外观不同；激进阈值误杀 |
| Low Hedge `Fall Evergreen` vs `Spring Evergreen` | 0.00% | 真同图，去重合理 |

⇒ 视觉证据支持：**早期的激进参数会把“不同花色/不同书脊/不同季节”的变体删掉**；
默认 `0/0` 只去同图像素，安全。纯变色样本（Bamboo Mat/Kitchen Island）确实适合去重，
但需要用户明确接受“换色也算重复”时才开 `--dedup-color-tolerance`。

### 5.3 误杀修复后复跑

修复内容：

- 纯变色一致性上限从 8.0 收紧到 **2.0**（`--dedup-color-consistency` 默认已改）；
- 新增单格最大偏移上限 **20%**（`CosmeticDeduper.PureRecolorMaxDeviation`）；
- 相似/纯变色比较按 **基础物品 + 季节** 分组，春季/秋季同款不再互相顶掉。

用干净的 1597 条家具底座（`D:/tmp/at-bake-big/tree`）重跑 AT 17 包，激进参数
`6 / 18 / 1.0 / 2.0`：

```
家具 771（修复前同参数 603；无损基线 1059）
Bookcase 1–9 全部保留（含此前被删的 8）
Basic Window 1–7 全部保留（含此前被删的 3/4）
Low Hedge Hardwood_Fence_Fall_Flowering / Spring_Seasonal 均保留
像素完全相同（0.00%）的四季 Evergreen 仍只留一条 —— 精确去重不分季节
```

对照图 `D:/tmp/dedup-visual/focus` 里这些条目的像素差分别为 6.35%（书脊）、
1.94–2.44%（窗帘）、8.22–8.55%（花期/季节），与“明显不同”的肉眼结论一致。

升级已有产出：`_frameworks-dedup.json` 现在带 `version`（旧文件没有），判据一改就作废旧 IDs，
重跑同一命令即可把上次误杀的行补回。在旧算法产物（AT 家具 603 条）上实测补回
**家具 174 / 可制作 16**，总量 777（同参数干净基线是 771；多出的 6 条是旧树本来就保留、
重跑不会主动删除的条目）。Nano Basic Window 1–7、Artist Bookcase 1–9 均已恢复。
重跑会先把已落盘条目按“基础物品 + 季节”喂回去重表，**不会**把仍应去重的同图/纯换色变体
过度补回：把侧车 `version` 手动删掉再跑一遍，新增为 0。

## 5.4 运行时框架的静态近似

新增 `MoreGrass` / `BusLocations` / `TrainStation` 三条通道，落在既有 XNB 上：

| 框架 | 实测 | 结果 |
|---|---|---|
| MoreGrass | 小树 + `TerrainFeatures/grass.png`，`--packs Wildflower` | 4 季 × 3 张；`grass.png` 66×240 的 0/20/40/80 行被替换；二次跑草地 0 |
| BusLocations | 小树 + `Maps/BusStop`，`--packs "Downtown Zuzu"` | `Buildings (19,11)` 新增 `Action: Warp 17 42 Custom_DTZ_ZuzuCity1` + `Passable: T`；空位避开所有非 Back 图层；与模板机留 1 格；Desert 站点跳过（原版已有）；二次跑交通 0 |
| TrainStation | 小树 + `Maps/Railroad`，同上 | `Buildings (48,40)` 新增 `Action: Warp 7 5 Custom_DTZ_TrainStation` + `Passable: T`；空位避开所有非 Back 图层；二次跑交通 0 |
| 多目的地 | 临时 Mods：2 个 BL 包 + 1 个 TS 包（2 站） | BusStop `(19,11)/(21,11)`、Railroad `(48,40)/(50,40)`，两两间隔 1 格；交通 4 |

代价：MoreGrass 是**全局换草**（不是新增草类型）；交通图块没有票价/条件/选站 UI，
只是“点一下直达”。Bush Bloom 没有原版落点，仍是纯运行时（原版花期和掉落物写死）。
`pack-check` 对合并小树回包 **129 / 0**；**C# 全量测试 1621/1621 通过**
（含 MoreGrass 覆盖与幂等用例）。

## 6. 未验证 / 仍需实机

- **未进游戏**：本轮只到 XNB 回包，没有实机看家具目录、栅栏连接形状、作物生长。
- **栅栏**：AT 的每变体 sheet 被按 `TextureHeight` 切成 48×128 / 48×224；
  原版 `Fence` 的源矩形公式能覆盖，但四种连接/开门形状要实机核对。
- **家具目录**：依赖原版 `Furniture Catalogue` 的 `ALL_ITEMS (F)`；实机应能看到新条目。
- **作物**：新种子复用基础产物；种子图标复用基础种子；生长动画的每一行要实机确认。
- **大件**：`ReserveExtraIndex`/多帧动画的 AT 变体只保留第一帧（如果有）。
- **CC 宠物**：帧尺寸不匹配（16×16/16×32）时原版会按 32×32 裁切；
  166 个是**小树**的数（完整树里 `TileSheetPath` 指向的 mod 资产更多）。
- **SF 灯光 / DrawLayer 跟随皮肤**：灯光丢失；`DrawLayers` 用基础建筑贴图。
- **完整全量**未跑（按用户约束）；本轮是定向包 + 小树。
