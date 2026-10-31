# 地图的图集引用是**相对地图自己那层目录** —— 一条规则，五个 bug

日期：2026-10-13（起于 2026-10-12 晚的实机自检）
起因：业主让「直接跑游戏测试」，逐张地图探针把 2492 张地图扫了一遍，**FAIL 15**。

## 0. 判据：游戏怎么解析图集引用

`Maps/Mines/19` 的 tilesheet `ImageSource` 写 `Mines/mine` ⇒ 游戏去找
`Content/Maps/Mines/Mines/mine.xnb`（**相对地图自己那层目录**，`../` 可以出 `Maps/` 到底层的
`TileSheets/`、`LooseSprites/`）。实测异常链里两种情况都拍下来了：

```
Maps/ArchaeologyHouse ← Content\Maps/TileSheets/furniture.xnb   ← 少了 ../（被我们剥掉了）
Maps/Mines/19         ← Content\Maps/Mines/Mines/mine.xnb       ← 多了一层（照抄了别的层的相对写法）
```

⚠ 游戏**会自己剥 `.png`**（日志里它找的是 `.../furniture.xnb`）⇒ 带不带扩展名等价，
但归一成原版那种裸名更好（原版 `ImageSource` 一律是 `spring_outdoorsTileSheet` 这种）。

## 1. 五个 bug（都是这一条规则的不同侧面）

| # | 症状 | 成因 | 提交 |
|---|---|---|---|
| ① | 15 张地图引用变成 `Maps/TileSheets/…` | 我为「前导点」加的 `StripDots` 逐段剥点，**把 `..` 也剥了** | `9ac36b5` |
| ② | `Maps/Mines/Mines/mine`（多一层） | `MapPatcher` 拷图集时**照抄**源地图引用，没按目标地图目录重新定基（移植 SMAPI `MapPatcher` 缺的一步） | `b9babec` |
| ③ | `Maps/Maps/Mines/…`（多一层 `Maps/`） | 源引用自己带 `Maps/` 前缀时，`Rebase` 又补了一个（实测 `[CP] East Scarp` 就写 `Maps/Mines/mine.png`） | `37f315f` |
| ④ | `Maps/EastScarp_IntroMine` 仍是 `Maps/Mines/mine.png` | `LocalMapTilesheets` **只在带前导点时才改写引用** ⇒ 这种「已经是资产写法但带 `Maps/` 与 `.png`」的没归一 | `17253e2` |
| ⑤ | `Maps/Mines/19` 出现 `../../mine`（指向内容根） | ④ 的修法**用错了身份**：源地图也按「目标那层目录」写，而源对象被多条补丁共用 ⇒ **每条补丁再定基一次**（见 §4.5） | 本轮 |

④ 的修法把规则收紧成一句话：**每条引用都按地图自己那层目录写回**
（`MapPatcher.Rebase(key, mapAsset, alreadyContent: true)`，与拷图集时用的是**同一份**实现）。
后果：`Maps/` 顶层的地图写法不变（`spring_outdoorsTileSheet` 还是它自己），
子目录地图才变（`Maps/Mines/19` 写 `mine`）。

⑤ 把这句话补全成：**按这张地图自己的身份选基准，且写进去的形态必须幂等** ——
要写进产出树的地图（`Load`）用「相对它自己那层目录」，只当补丁源的地图（`EditMap` 的 `FromFile`）
用「相对 `Maps/`」，由 `MapPatcher` 定基一次。

## 2. 受控对比（同参数）

| 对比 | 报告侧 | 树侧 |
|---|---|---|
| prof85 → prof86（① + ②） | 228 包相同 / 3 包仅备注不同，**0 判定变化** | 差异集中在探针报的那批地图（`Maps/Mines/19` 的 `Mines/mine.png` ⇒ `mine`、`Maps/ArchaeologyHouse` ⇒ `../TileSheets/furniture`）|
| prof86 → prof87（③） | 231 包**完全相同** | **0 差异**（③ 那条路上没有实例）|
| prof87 → prof88（④） | 165 包相同 / 66 包仅备注不同，**0 判定变化** | **1012 个 `Maps/*.tbin`** 的引用写法被归一 |
| prof88 → prof89（⑤） | 230 包**完全相同** / 1 包仅 3 条 `writeNote` 不同，**0 判定变化** | **只有 `Maps/Mines/19.tbin` 一个不同**，无新增/无消失（见 §4.5）|

四处改动都没有改变任何**判定**（变好 0 / 变坏 0 / 新增 0 / 消失 0），只有备注与引用写法的差异。
测试 **1420 条全绿**（新增：`..` 不被当点剥、拷图集按目标目录重定基、`Maps/` 前缀不重复补、
同一份源地图反复用不被反复重定基）。

## 3. 自检工具

`tools/GameSmoke/` 现在会：
1. 自己新建存档（`TitleMenu.createdNewCharacter(skipIntro: true)`）→ 跳过开场 → 起床；
2. 逐张地点 `getLocationFromName` + 真传送（38 个）；
3. 播 6 条**我们注入的** cue（波形库是 MS-ADPCM，播一次才走加载/解码那条路）；
4. `Game1.NewDay(0f)` 跨 3 天（走 `NPC.dayUpdate` → `RequireLocation` 那条路）；
5. **逐张地图探针**：扫 `Content/Maps/**/*.xnb`，真建 `GameLocation` 并碰 `.Map`
   （1.6 里地图走 `Game1.game1.xTileContent`，用 `Game1.content.Load<Map>` 会得到**假失败**），
   贴图按 `Texture2D` 再试一次记"跳过"；失败时印**整条异常链**（缺哪个文件只有内层说得清）。

结果落 `Mods/GameSmoke/smoke-result.txt`，边跑边写。

## 4. 验收（改后重装，同一条探针）

`packed88` 装机（整树回包 **成功 15416 / 失败 19**、`Destination array` 0 次、
`Wave Bank.xwb` 1 390 030 804 B 照旧；清单 15 465 条、缺 0 多 0）：

| | 改前 | 改后 |
|---|---|---|
| 地图探针 PASS | 1230 | **1244** |
| 贴图跳过 | 1247 | 1249 |
| **FAIL** | **15** | **2** |
| 巡图（38 个地点） | 33 成功 / 5 失败 | **34 / 4** |
| 注入音乐 / 跨天 | 6 条 / 3 天（不崩） | 同左 |

**剩下 2 张，改前就在失败，不是新回归**：

1. `Maps/EastScarp_Village` ← `Content\Maps\spring_silo.xnb` —— **上游缺**：没有任何包提供这个资产
   （Rodney 那条 `Load` 的 `FromFile` 自己就少文件），已记档；
2. `Maps/Mines/19` ← 游戏去**顶层**找 `Content\mine.xnb` —— 见 §4.5（已修）。

## 4.5 `Maps/Mines/19`：同一条引用被**反复重定基**（本轮的第二个 bug）

### 现象与定位

- 单图迭代（新加的 `probe-only.txt` 入口）确认**稳定复现**，且同一轮里 `Maps/Mines/18`、`/20` 都 PASS
  ⇒ 是这张地图自己的问题，不是顺序/缓存。
- 逐字节比：我们的 `Mines/19.tbin` 54 992 B vs 原版 49 244 B，**差 20 527 字节**，第一处差异就在
  第 27 字节的计数域（`1` → `4`）。
  ⚠ **我先前写「字符串与原版完全一致」是方法错了**：当时只取了**前 30 条**字符串、还按**集合**比 ——
  改成按顺序取全部（我们 255 条 vs 原版 55 条）后，一眼看到只多了一条：**`../../mine`**。
- `../../mine` 是「出到**内容根**」的写法 ⇒ 游戏相对地图那层目录解析 ⇒ `Content/mine.xnb` ✗，
  而**原版和包里都没有这个资产**（原版的是 `Maps/Mines/mine`，就在盘上）。
- 谁写的它：`Spiral Mine Shortcuts` 用**同一份** `assets/19-edit.tmx` 打 **4 条** `EditMap` 到 `Maps/Mines/19`
  （`FromFile` 那条路），而源地图对象由 `MapCache.TryGetSource` **按路径共用** ⇒ 每条补丁都在
  **同一个对象**上再写一遍它的图集引用。

### 机制（已确证，两处独立证据）

`LocalMapTilesheets.Copy` 原来把**源地图**的引用就地改写成「相对**目标**那层目录」的形态：

```
补丁 1：`Mines/mine.png` ⇒ `mine`
补丁 2：`mine`          ⇒ `../mine`        ← 把「相对 Maps/Mines」的说法又当成「相对 Maps/」算了一遍
补丁 3：`../mine`       ⇒ `../../mine`
补丁 4：`../../mine`    ⇒ `../../mine`     （稳定在这里）
```

⇒ 不是"定基两次"，是**每条补丁定基一次、无上界**。两处证据：

1. **报告里的 `writeNote`**（改前 prof88 那一包，3 条补丁各一条）逐字就是上面这个序列；
2. **回归测试**（`MapApplierTests.同一份源地图被反复用时图集引用不被反复重定基`）把 `forTarget` 临时
   改回旧语义跑：4 个 `z_` 图集的引用拿到 `mine` / `../mine` / `../../mine` / `../../mine`，
   与实机那份逐字对上；改后 4 个全是 `mine`。

### 修法：**按地图的身份选基准 + 幂等**

- **要写进产出树的地图**（`Load` 那条路）：引用按**它自己那层目录**写 —— 游戏就是这么解析的；
- **只当补丁源用的地图**（`EditMap` 的 `FromFile`）：引用保持「**相对 `Maps/`**」的基准
  （`MapPatcher.MapsRelative`），由 `MapPatcher.PatchMap` 按目标目录定基**一次**。
  这个形态**幂等**（`Maps/Mines/mine` ⇒ `Mines/mine` ⇒ 再算还是 `Mines/mine`）—— 源对象被多条补丁共用，
  写进去的形态必须幂等，否则就是上面那条无上界的链。
- `GameContentKey` 同时改成**完整解析 `..`**（旧的只剥一层，`../../mine` 会留成 `../mine`）。
  绝对内容路径现在只用来做**落点 / 撞名判据 / 找图**，不再当引用写进地图。

⚠ 顺带订正一处与 SMAPI 的**口径差异**：修后 4 条补丁各挂一个 `z_mine` 图集（共 4 个），
旧代码只挂 2 个 —— 旧的那是**巧合**：引用被写坏成 `mine` 之后恰好与目标自己那条**归一后相等**，
于是"复用"了目标的图集。SMAPI 的判据是
`NormalizeTilesheetPathForComparison(dst) != NormalizeTilesheetPathForComparison(src)`
（`Mines/mine.png` ⇒ `Mines/mine` **≠** 目标的 `mine`）⇒ **SMAPI 也会建这 4 个** ✓ 修后一致。
差别只在它照抄源的 `ImageSource`（它有模组目录可用），纯 XNB 场景必须写成目标能解析的形态。

### 受控对比（同参数，232 包全量）

```
python tools/convert-mods.py D:\tmp\prof88     ← 改前（同一条命令、同一个 Release exe 前的版本）
python tools/convert-mods.py D:\tmp\prof89     ← 改后
python tools/compare-runs.py       D:\tmp\prof88 D:\tmp\prof89
python tools/compare-trees-by-hash.py D:\tmp\prof88\tree D:\tmp\prof89\tree
```

| 侧 | 结果 |
|---|---|
| 报告 | 230/231 包**完全相同**；只有那一包 3 条 `writeNote` 不同（1 条措辞变准、2 条消失）⇒ **判定 0 变化** |
| 产出树 | 31 569 个文件里**只有 `Maps/Mines/19.tbin` 一个不同**；**无新增 / 无消失** |
| 退出码非 0 的包 | 两边**同为 102 个、名单完全一致** |
| 静态自检 | 「每张地图引用的图集图片」**4 → 3**（这条不再报），其余 10 项一字不变 |
| 测试 | **1420 条全绿**（1419 + 新增 1 条回归测试） |

### 实机（同一条探针、同一份装机）

装机方式：`packed88` 的 Content 不动，只把这一张重打覆盖 —— **先证明了单文件打包与整树打包等价**：
拿 **prof88 那棵树** mini-pack 出来的 `19.xnb` 与整树 `packed88` 的**字节完全相同**
（md5 `4f78be57…`）⇒ 只有 tbin 变了的这轮不必重跑 48 分钟整树回包。随后
`gen-content-hashes.py` 重建清单（15 465 条，与改前同数）。

| | 改前（`packed88`） | 改后（同一份装机 + 这一张） |
|---|---|---|
| 单图探针（`Maps/Mines/{18,19,20}`） | PASS 2 / **FAIL 1**（`Content\mine.xnb`） | **PASS 3 / FAIL 0** |
| 全量探针 2495 张 | PASS 1244 / 跳过 1249 / **FAIL 2** | PASS **1245** / 跳过 1249 / **FAIL 1** |
| 巡图 / 音乐 / 跨天 | 34 成功·4 失败 / 6 条 / 3 次 | 同左（无退化） |

⇒ **唯一剩下的失败是 `Maps/EastScarp_Village`**，那是**上游缺资产**（`spring_silo`，没有任何包提供），
我们补不出来 —— 本轮的验收口径「FAIL 15 → 0」按**我们能力范围内**达到 **0**。

## 5. 同一天重跑**静态自检**（大改动之后必须做的对照）

`python tools/verify-tree.py D:	mp\prof88	ree D:\sdv_tree`（口径：产出相对基准的**新增问题数**）：

| 检查项 | 上一份基线（prof77 口径） | prof88 | prof89（§4.5 修后） |
|---|---|---|---|
| 每张地图引用的图集图片 | 11 | **4** | **3** |
| 语料 tmx 的 tileset 图片 | 133 | **122** | 122 |
| 数据资产里的贴图引用 | 19 | **16** | 16 |
| 地点的 `CreateOnLoad.MapPath` | 1 | **0** | 0 |
| 图集落点被地图占着 | 0 | 0 | 0 |
| 季节性图集变体 | 0 | 0 | 0 |
| 每条 Warp 的目标地点 | 4 | 4 | 4 |
| 图块上的 Warp 动作 | 10 | 10 | 10 |
| 物品图集/精灵下标 | 1 | 6（**未查**：可能是本轮把更多图集落进树之后暴露出来的，待逐条看） | 6 |
| 地图 Message 的文本 / LocalizedText 引用 | 39 / 7 | 39 / 7 | 39 / 7 |

⇒ **没有一项变坏**；三项明显变好（图集图片缺 11→4、tmx 图集 133→122、数据贴图 19→16）。
⚠ 「物品图集/精灵下标」从 1 变成 6 —— **已查清，不是本轮的错误**：

6 条逐条看过，全是**贴图只能由 DLL 模组提供**的那些条目
（`LitD.*` ← `Lurking in the Dark`、`7thAxis.*` ← 那个代码模组、`DN.SnS_DeepDarkChest` ← 图集尺寸不合）：

- 产出树里**没有任何补丁**碰过这些贴图（`Characters/Skrell`、`Characters/Ebb`、`Textures/7thAxis.LitD/*`）；
  包目录里**有**这些 png（`Lurking in the Dark/Assets/Characters/FakeNPCs/Skrell.png`），
  但它们是**代码模组用 C# 注册**的资产 ⇒ 纯 XNB 安装拿不到（与「DLL 自定义地点类」同一类，**只能报**）。
- **为什么现在才报出来**：本轮的「条件烘焙」把更多条目烤进了 `Data/*`（净 +287 条）⇒ 这些条目引用的
  贴图才第一次被静态检查看到。也就是说这是**能力变强之后暴露的既有缺口**，不是回归。
