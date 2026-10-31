# 最终状态快照：`prof77` 整树自检（对照原版树 `D:\sdv_tree`）

日期：2026-10-12
命令：`python tools/verify-tree.py D:\tmp\prof77\tree D:\sdv_tree --report D:\tmp\verify77.txt`

> ⚠ 口径：下表是「**产出相对基准的新增问题数**」（有基准树那一列）。
> 早先我自己跑的 `prof63` 那一次**没给基准树**，数字是"绝对缺失数"，**两者不可直接比** ——
> 记在这里是给下一轮当**同口径的对照基线**。

| 检查项 | 新增问题数 |
|---|---|
| 每条 Warp 的目标地点 | 4 |
| 地点的 `CreateOnLoad.MapPath` | 1 |
| 每张地图引用的图集图片 | **11**（本会话从 12 降下来：`panorama` 修好）|
| **图集落点被地图占着**（本会话新加的一道） | **0** |
| 图块上的 Warp 动作 | 10 |
| 物品图集/精灵下标 | 1 |
| 季节性图集变体 | 0 |
| 语料 tmx 的 tileset 图片 | 133 |
| 地图 Message 的文本 | 39 |
| LocalizedText 引用 | 7 |
| 数据资产里的贴图引用 | 19 |

同时（同一天的对照）：

- 报告侧 `total=32771 / written=15244 / writeFailed=201`（基准 `prof76 → prof77`：+3 / 0 退化）；
- 产出树 `prof76 → prof77` 差异只有那 3 条收获（`Maps/panorama.*`、`Data/SpecialOrders.*`）+ `ContentHashes.json`；
- 图集盘点：找不到图片的图集名 **11 种**（其中 **10 种是上游数据** —— 9 种前导点错名字，
  外加 `DN.SnS_Water` 那种"包里有图但包从不发布"）、`spring_silo` 归"条件驱动的 Load"；
  落点被地图占着 **0 处**；
- 整树回包：**成功 15374 / 失败 25**，8 个资产打不出包（1 个是我们自己的哈希清单、
  3 个上游 1.5 时代枚举值、4 个 SMAPI 专属）—— 但 **`Wave Bank.xwb` 产不出**：
  注入的音乐是 PCM，204 条 ≈2.6 GB，撞上**三处 32 位上限**（打包器的 `byte[]`、
  清单 `int` 偏移、引擎侧 int 偏移）⇒ **只能靠把库做小**（注入音乐改 MS-ADPCM，回到 ~1.1 GB），
  见 [`2026-10-12-wave-bank-not-produced.md`](2026-10-12-wave-bank-not-produced.md)。
