# 修一个真 bug：包内图集的落点撞上另一类资产时，把地图盖成了贴图（进那个地点就炸）

日期：2026-10-12
类别：**行为修正（高影响）** —— 这条会让**整张地图加载不出来**（而且原版地图也会被牵连）

## 症状：`AssetTypeAudit` 报出唯一一条类型不一致

新语料全量跑完之后，`assetType` 自检报 **1 处**「产出与原版类型不一致」：

```
Maps/Coop —— 产出 `.png`（Texture2DReader），原版 `.tbin`（xTile.Pipeline.TideReader）
```

产出树里 `Maps/Coop.png`（6827 B）与 `Maps/Coop.tbin`（11198 B）**同时存在**，而 `Maps/Coop.config`
是**贴图那份**（498 B，与 `Maps/coopTiles.config` 逐字节同形）。打包之后游戏会拿一个
`Texture2D` 去当 `Map` 用 ⇒ **进鸡舍**（`Maps/Coop`）就炸。

## 收窄过程（不猜，一处一处排除）

1. 全语料里 target **精确等于** `Maps/Coop` 的补丁只有一条：包 `Lnh's Leisure Coop 鸡舍扩建`
   的 `Load Maps/Coop ← assets/Coop.tmx`（`written=True`）。它的 tmx 引用的是
   `coopTiles.png` / `townInterior.png` —— **游戏内容**里的图集，不是包内的 ⇒ 不是它写的图。
2. 包 `贴图美化---泥土内饰 [CP]` 有一条 `EditMap` 打在 **29 个逗号分隔的地图资产**上（含 `Maps/Coop`），
   但内容只有 `MapProperties.AmbientLight` ⇒ 它写的是 **tbin**，不是图。
3. 全盘搜 `Coop.png` 大小 6827 的文件：只有
   `[CP] Always Raining in the Valley\assets\Maps\Farm\Coop.png`（96×128，6827 B）——
   **与产出树里那份逐字节同源**。
4. 全语料搜「引用 `Coop.png`」的 tmx：正是那个包的两张地图
   （`assets/Maps/Farm/CooperFarms.tmx`、`CooperFarmsSterling*.tmx`），
   而 `Load Maps/CooperFarms ← assets/Maps/Farm/CooperFarms.tmx` 是它的补丁。

## 根因：图集的**内容键**与另一类资产的资产名重合

`LocalMapTilesheets` 按 SMAPI `GetContentKeyForTilesheetImageSource` 的规则算落点
（**没有 `Maps` 前缀就补上、去掉 `.png`**），于是包内同级图集 `Coop.png` 的落点是
`Maps/Coop` —— **那正是原版地图的资产名**（纯 XNB 产出里 `Maps/Coop` 就是 `Maps/Coop.xnb`）。

一个内容键只能是一类资产。两条路各写各的，**谁后写谁赢**：

- `Load Maps/Coop`（包 135）把地图 `.tbin` + 地图 `.config` 写在那儿；
- 包 159 的图集搬迁（`ImageApplier` → `AssetCreator.CopyImageConfig`）把 `Coop.png` 与
  **贴图模板 `.config`** 盖在同一个键上 ⇒ 地图被换成了贴图。

`docs/research/2026-10-09-mod-local-map-tilesheets.md` 里那套「放到游戏会去请求的名字上」
的判断漏了**撞名**这一档：它不是判错了，是**少了一个前提**（那个名字没有被别的资产占着）。

## 修法（三处，都是「别静默写坏树」）

1. **撞名就让路**（`LocalMapTilesheets`）：落点被**另一类**资产占着（树里同键有
   `.tbin` / `.json` / `.xnb`），或图集跟**地图自己**同名时，把落点换成
   `Maps/<名>_tilesheet`（还撞就缀 `2`、`3`…，只看树里的现状，确定性），
   并**把地图里的 `ImageSource` 一起改掉** —— 游戏正是按 tbin 的 `ImageSource` 找图的
   （`gameloc.cs:1138-1143`：名字没有目录时按 `Maps/` 找）。季节族（`spring_X`）**整族按新名搬**。
   - 改完名要**重新落盘**：`Load` 包里的 `.tbin`（原样字节）那条路上，只改内存对象等于没改，
     所以 `CopyLocalTilesheets` 会重新序列化并把新字节交回去；`Load` 的 tmx 那条路则把图集搬迁
     挪到**地图落盘之前**（否则改名写不出去）。
2. **图片不许盖掉另一类资产**（`ImageApplier` + `AssetCreator.NonImageOccupant`）：
   目标处已有 `.tbin` / `.json` / `.xnb` ⇒ **整条失败**并说明原因，不再静默换类型。
3. 保留 `AssetTypeAudit` 当兜底的自检（它只报告，不当闸门）。

**没有**改成「一律用 `Maps/<UniqueID>/<名>` 的模组内键」：那会动到已经在实机验证过的
20 多处图集落点，收益只是「理论上更接近 SMAPI」。撞名这一档现在有明确判据与报告，
留着 `Maps/<名>` 这套（`2026-10-09-mod-local-map-tilesheets.md` 的结论）改动面最小。

## 验证

1. **测试先行**（3 条改前红、改后绿）：`LoadApplierTests` 的
   `图集名撞上另一类资产时改名并把地图引用一起改`、
   `tbin地图的图集撞名时改名要写回tbin`（原样字节那条路的重新序列化）、
   `图集撞名时季节族跟着一起改名`；`ImageApplierTests.目标处已有另一类资产时拒绝覆盖`。
   全量：**1368 通过 / 0 失败**。
2. **同参数端到端受控对比**（`prof62` 改前 → `prof63` 改后，同一语料 232 包同一棵 `D:\sdv_tree`）：
   - 报告：`total=32771 written=15060 writeFailed=217` **两轮一字不差**（没有任何判定回归）；
     有差异的 5 个包（`Downtown Zuzu` 21 条、`Always Raining in the Valley` 3 条、
     `Sunberry NPC - Jonghyuk` 2 条、`Crystal Island` 1 条、`The Last Smoluanu` 1 条）
     **全是 `writeNote` 多了一条改名说明**，verdict / written 一字未动。
   - 逐字节树：只有 **51 个文件**不同，全部属于这一类 —— 7 份图集从 `<名>.png` 搬到
     `<名>_tilesheet.png`（+ 各自的 `.config`，共 14 个新文件、7 个旧文件消失）、
     **23 张地图的 tbin** 里那条 `ImageSource` 改了名（`Maps/CooperFarms.tbin` +10 字节，
     正好是 `Coop` → `Coop_tilesheet` 的长度差）、**6 张地图的 `.config`** 从「贴图」恢复成「地图」、
     外加 `ContentHashes.json`（内容变了哈希自然变）。
   - 类型自检：`assetType` `checkedAssets=7435 / typeMismatch=0`（改前最后一份是 `Maps/Coop` 1 处）。
   - 图集盘点（`check-map-tilesheet-assets.py`）：改前改后**都是 46 种**「找不到图片的图集名」，
     名字集合**逐项相同** ⇒ 这次改动既没引入新的缺失，也没掩盖旧的（那 46 种是另一类问题：
     包内图集**不在地图同级目录**，SMAPI 都找不到，见 `2026-10-09-mod-local-map-tilesheets.md`）。
   - **`mine` 这条撞得最广**：产出树里**裸引用** `mine`（解析成 `Maps/<名>`）的地图，
     改前 **66 张**（49 张原版 `Maps/Mines/*` + `Downtown Zuzu` 的 **17 张**），
     改后 **49 张** —— **与原版树的 49 张一字不差**（原版那 49 张本来就是按「地图自己所在目录」
     找到 `Maps/Mines/mine.png`，不涉及这个键）。

## 实测：这一档不止 1 处，审计只看见 1 处

`AssetTypeAudit` 报出来的那条（`Maps/Coop`）是**撞上原版地图**的那一例。改名日志显示实际有 **7 处**：

| 包内图集 | 算出来的落点 | 撞上谁 |
|---|---|---|
| `Coop.png`（Always Raining in the Valley） | `Maps/Coop` | 原版地图 `Maps/Coop`（包 135 刚换过的那张鸡舍） |
| `mine.png`（Downtown Zuzu） | `Maps/mine` | 原版地图 `Maps/mine`（磁盘上是 `Mine.tbin`，Windows 上同一个键）—— DTZ **17 张**地图都引用它 |
| `mine.png`（Crystal Island 的 `EditMap` 补丁块） | 同上 | 同上（这块补丁的图集最终没被合进目标地图，见下） |
| `NightBGDwarf.png` | `Maps/NightBGDwarf` | **它自己**那张地图（矮人拓展） |
| `SterlingDate1.png` / `SterlingDate3.png` | 同名键 | **它自己**那两张地图 |
| `JonghyukSpouseRoom.png` / `JonghyukSpousePatio.png` | 同名键 | **它自己**那两张地图 |

后 5 处**审计看不见**：那些地图是模组自己建的（原版没有同名资产 ⇒ 没有对照），
而且**地图后写**，`.config` 最后又变回地图那份 ⇒ 唯一还被暴露的就是 `Maps/Coop`。
它们同样是坏的（地图的图集引用指向地图自己），只是坏得静默。

## 一个坑：**包里根本没有那份图**时不能改名

第一版是「只要落点撞上别的资产就改名」。造合成包走 `.tbin` 那条路时逮到它会把
**指向游戏内容**的引用也改掉（那份图包内没有 ⇒ 没东西可搬 ⇒ 改完只是把
「指向一张地图」变成「指向一个不存在的资产」，还给盘点添一条**假缺失**）。
CP 那边这种引用是**原样留着**的，所以判据收紧成：**包内真有这份图才改**。

## `.tbin` 那条路的端到端（单测之外，合成包实测）

语料里 7 处改名**全走 `.tmx`** 那条路（`Load`/`EditMap` 的 `FromFile` 都是 tmx），
`.tbin` 那条「原样字节 + 重新序列化」只有单测覆盖 ⇒ 造两个合成包实测：

| | 包内有没有那份图 | 产出 |
|---|---|---|
| 包 A（`Load Maps/SynthSrc ← assets/M.tmx`，图集名 `Coop.png`） | 没有 | tbin 2146 B，引用**原样** `Coop.png`（**没**改名）|
| 包 B（`Load Maps/SynthDst ← assets/D.tbin`＝A 的产物） | 有（`assets/Coop.png`） | tbin **2166 B**（+20 = 两条图集引用各 +10 字符）、引用 `Coop_tilesheet.png`；写出 `Maps/Coop_tilesheet.png`（6977 B，与源逐字节相同）+ 贴图 `.config`；`Maps/Coop.config` 383 B **仍是地图那份**；报告 `writeNote` 写了改名 |

再**真的回包一次**（`pack-check.py` → 2 个资产 0 失败；解包回来）：
`SynthDst.tbin` 的图集引用读回来是 `Coop_tilesheet`（**打包器会把 tbin 图集名里的 `.png` 去掉**，
这是它的既有行为），图集 PNG **逐字节相同** ⇒ 游戏按 `Coop_tilesheet` 找的就是我们写的那份
（我们的落点正好是 `Maps/Coop_tilesheet.png`，两边都按「去掉扩展名」算键）。

## 事后审计兜住这一档（`check-map-tilesheet-assets.py` 新增一类）

`AssetTypeAudit` 只在「产出与原版**同名**资产的类型」上比，所以自己建地图的那 5 处它看不见。
补在**批量盘点**这一层（不放进 CLI 每包重跑：1237 张 tbin 逐包解析会白加十几分钟）：

- 判据：图集引用解析出来的**内容键上有 `.tbin`** 就算（`Maps/Mine.tbin` 与 `Maps/mine.png`
  是同一个键 —— 谁赢看打包顺序 ⇒ 图集引用可能拿到一张地图）。解析顺序按游戏：
  **先「地图自己那一层」，再 `Maps/<名>` / `Tilesheets/<名>`**。
- 校准与效果（同一工具跑三棵树）：

| 树 | 找不到图片 | 落点被地图占着 |
|---|---|---|
| 原版 `D:\sdv_tree` | 0 种 | **0 种** |
| 改前 `prof62` | 46 种 | **7 种 / 25 张地图**（`mine` 17 张、`coop` 1、`nightbgdwarf` 1、`sterlingdate1/3` 2、`jonghyukspouse*` 2） |
| 改后 `prof63` | 46 种 | **0 种** |

  ⚠ 第一版判据按「先 `Maps/<名>`」写，**原版自己就报 1 种**（`mine`）——那是假阳性：
  原版 `Maps/Mines/*.tbin` 引用的裸名 `mine` 是按**地图自己那层目录**找到
  `Maps/Mines/mine.png` 的。顺序改对之后原版归零。**判据必须先用原版校准到 0**。
- `tools/verify-tree.py` 加了一行 `图集落点被地图占着`，每次整树自检都会带出来。

## 补充（同日）：撞名判据要看得见**还没落盘**的地图

`LocalMapTilesheets.Clashes` 原来只查树里的文件。实测 `[CP] The Last Smoluanu`
先 `Load Maps/NightBGDwarf`（tex 走 tmx 那条路 ⇒ 地图只在 `MapCache` 内存里）、
再 `Load Maps/Summit_Night`，后者的包内图集就叫 `NightBGDwarf.png` —— 判据看不见内存里那张地图，
图就写到了它的键上，随后地图又写回同一键。修法：撞名判据加一条
**本次运行里已经生成/改过、还没落盘的地图**（查 `MapCache.TryGetLoaded`）。
`prof65 → prof66`：判定与总数**一字不差**，盘点那一项 **1 处 → 0 处**，
树里只差 3 个文件（`Maps/NightBGDwarf.png` 消失、`Maps/NightBGDwarf.config` 恢复成地图那份、
`Maps/Summit_Night.tbin` 的引用改名）+ `ContentHashes.json`。

⚠ **反过来那一档仍未修**：地图**比图集晚**写时才撞（图先占了键、地图后盖回描述符）。
判据那时看不到未来，只能靠**盘点**兜 —— `prof65` 就是这么抓到的（`nightbgdwarf`）。

## 已知残留（实测到的，未确证会不会坏事）

**同名图集塌到同一个键**：本语料有 **6 个包各自带一份 `mine.png`，内容各不相同**
（`Bear Family` 与 `Crystal Island` 那份同源 66231 B、`SkullCavernElevator` 52876 B、
`Downtown Zuzu` 45525 B、`Lunna` 87735 B、`梦幻矿洞` 52685 B），落点都是
`Maps/mine`（改后 `Maps/mine_tilesheet`）—— **后者覆盖前者**，产出树里留下的是 **DTZ 那份**。
实际只有 DTZ 的地图引用这个裸名（Crystal Island 那块 `EditMap` 的图集最终没被合进目标地图，
实测目标 tbin 与改前逐字节相同），所以**这次没坏事**；但只要有第二个消费者，
它拿到的就是别人的图（画面错，不炸）。这一档 `AssetTypeAudit` **查不出来**（两边都是图）——
判它得按「谁引用它」逐张比，属更远的一步。

`maps/mines/mine` 那 2 张（`EastScarp_DateMine` / `IntroMine`）引用的嵌套名在产出树里没有，
属于上面那 46 种「图集不在地图同级目录」的一类，与本次改动无关（改前改后同数同集合）。

