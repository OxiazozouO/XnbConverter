# 整树回包（用**重建的**打包器）—— 成功 6488 / 失败 0，音频真的进去了

日期：2026-10-11

## 参数（可复现）

```bash
python tools/stage-packer.py                                  # → D:\tmp\packer
python tools/pack-tree.py D:\tmp\prof19\tree --out D:\tmp\prof19_xnb2
```

- 输入树：`D:\tmp\prof19\tree`，**全语料 36 包**（CP 32 + FTM 4）链式产出，13 506 个文件；
  这一棵**先过了 `tools/verify-tree.py`**（6 项 0 + 3 项已知：语料 tmx 图集 4 / 地图 Message 2 /
  `LocalizedText` 1，与 `research/2026-10-06-grand-total.md` 记的基线**一致**，无回归）。
- 打包器：`tools/stage-packer.py` 产出的那份（`dotnet build -c Release` 重建 + 补齐
  `xna-shim\` 与 `.config\custom dll\`）。**不是** `out_publish` 那份，见下。

## 结果

```
成功: 6488  失败: 0            ← 一条 [ERROR] 都没有
产出：D:\tmp\prof19_xnb2（6492 个文件）
XACT/  FarmerSounds.xgs        1404 B
       Sound Bank.xsb         43010 B     ← 原版 32062：注入的 cue 进去了
       Wave Bank(1.4).xwb 37258692 B
       Wave Bank.xwb    1095519576 B      ← 1.1 GB
```

- **输入 13 506 → 产出 6 492 个 XNB**；
- **耗时约 48 分钟**（`XACT/` 源 535 MB，其中 35 个 `.ogg` 共 57.9 MB 要**解成 PCM** 塞进波形库 —— 大头在这）；
- 汇总行里读不到的 `Wave Bank.xwb` **这次在**，`Sound Bank.xsb` 也从原版那 32062 变成 43010。

### 对照：同一棵树、旧打包器（`out_publish`，2026-10-05 版）

| | 旧 | 新 |
|---|---|---|
| 汇总行 | 成功 6488 / 失败 0 | 成功 6488 / 失败 0 |
| `[ERROR]` | 1 条（`无效的 WAV 格式：OggS，应为 RIFF`）| **0 条** |
| 产出文件数 | 6491 | **6492**（多的那个就是 `Wave Bank.xwb`）|
| `Sound Bank.xsb` | 32062（= 原版）| **43010** |
| `Wave Bank.xwb` | **不存在** | 1.1 GB |

⇒ **旧打包器的「成功 6488 / 失败 0」是假绿**：整段音频功能丢了却照样报成功。

### ⚠ 副作用：装下来的 **Content 从 544 MB 涨到 1.2 GB**

XACT 波形库存的是**未压缩 PCM**（原版也是这样：`archaeo.wav` 文件 1 044 630 字节、
库里条目 Length 1 044 540，差的就是 90 字节 WAV 头）。所以注入的 **57.9 MB `.ogg` 会解码成 ~635 MB PCM**：

```
原版 Wave Bank.xwb  460 453 688 B
我们 Wave Bank.xwb 1 095 519 576 B     差 635 065 888 B（= 605 MiB）
原版 Content 目录      544 MB   →   产出 1.2 GB
```

⇒ **不是格式退化**（两边都是 PCM），是"多装了一小时音乐"的必然体积。
要压下来只能给波形库加 ADPCM 编码 —— 那是 `XnbConverter` 侧的新功能，本轮不做。

## 定点往返（解回来比）

把产出树里的三个代表性文件解回原格式，与写进打包器前的**源文件**逐字节比：

| 文件 | 结果 |
|---|---|
| `Characters/Marlon.png` | **字节相同** ✓ |
| `Data/Locations.config` | **字节相同** ✓ |
| `Data/Locations.json` | 语义相同：252/252 个键、键集合一致；差异是**反序列化的必然产物** —— 缺省字段被物化（`ArtifactSpots[].ContinueOnDrop: false` 之类）、`ItemID` 写成模型属性名 `ItemId` |
| `Maps/JojaMart.tbin` | 小 16 字节 = **恰好 4 处 `.png` 被剥掉**（源里 4 次、解回 0 次），即 `TBin10.RemoveTileSheetsExtension()` 的既定行为（`research/2026-10-09-tilesheet-extension-in-tbin.md`）|

⇒ 打包这一段**没有引入内容丢失**：唯一的字节差都能指到一处**已文档化的既定转换**。

## 完整性：**没有静默跳过**（这才是「失败 0」该被信的那部分）

`research/2026-10-09-packer-silent-skips.md` 记过：打包器对**没有 `.config` 边车**的文件是**静默跳过**的，
「失败: 0」对此毫无反映。所以这回不只看汇总行，逐层对了一遍：

| 核对 | 结果 |
|---|---|
| 产出树里的 `.config` | **6490** |
| 产出里的 `.xnb` | **6488** = 6490 − 2 |
| 差的那 2 个 | `XACT/Wave Bank.xwb`、`Wave Bank(1.4).xwb` —— 是**容器**，不产 `.xnb`，按原样落在产出目录里（已核，两个都在）|
| 没有 `.config` 的树内文件 | **只有**：XACT 的 463 个 `.wav` + 35 个 `.ogg` + `FarmerSounds.xgs`/`Sound Bank.xsb`（都被 XACT 那一步吃掉了；474 = 437 + 37 正好对上波表条目数）+ 我们自己的 `ContentHashes.json` |
| 原版资产的覆盖面 | 原版 `D:\sdv_tree` 的 **3552 个 `.config`，产出树一个不缺**；产出树共 6490 个 ⇒ **超集** |

⇒ 这一份产出**没有"以为打进去了、其实没打"的文件**。

### 顺带：**定点回包**（只打相对原版改过/新增的那批）

```bash
python tools/pack-check.py D:\tmp\prof19\tree          # 用默认（=暂存的那份）打包器
产出相对输入：新增/改过 6461 个文件
小树：7009 个文件（含同名 .config）；缺 .config 的 36 个
成功: 3486  失败: 0
```

- **3486 / 0 与 `research/2026-10-09-five-data-assets-cannot-be-packed.md` 里记的整树定点回包数字一模一样**
  （那次也是 6461 个改过的文件、3486/0）⇒ 换打包器、换语料轮次，这个数**稳定复现**；
- ★ **当前那棵树（13562 个文件，含本轮全部改动）重跑过**：

  ```
  产出相对输入：新增/改过 6517 个文件
  小树：7065 个文件（含同名 .config）；缺 .config 的 36 个（35 个 .ogg + 1 个 .xsb）
  成功: 3514  失败: 0
  ```

  6517 − 6461 = **56**（那 7 个 NPC × 四季 × 2），3514 − 3486 = **28**（资产数）—— 两头都对得上 ✓；
- 「缺 `.config` 的 36 个」= 35 个 `.ogg` + 1 个 `Sound Bank.xsb`，正是上面说的「XACT 那批不逐个打包」✓
  ——**不是**遗漏。

### ★ 装机侧再补一条：**原版资产一个不缺**（拿打包产物对原版 `Content` 数）

```
原版 Content 的 .xnb        3550
我们打包产出的 .xnb         6516
**原版有、产出没有的**         0        ← 静态层面"替换式安装不会缺资产"
产出比原版多的              2966       ← 模组内容
```

⇒ 加上前面那两条（6490 个 `.config` ↔ `.xnb` 一一对应、无静默跳过），
「把产出直接当 `Content` 用」在**静态层面**已经没有缺口。
⚠ 但**实机没验过**（见下节），所以 `how-to-build-an-install.md` §4 照旧给覆盖式。

**对「装机能不能直接替换」的意义**（静态层面，**不是**实机结论）：
`how-to-build-an-install.md` §4 当初要求「覆盖式」的理由是「有 16 个资产打不出来，只放我们的产出会启动即崩」——
那条理由早就订正掉了，而这一次又多了「原版资产一个不缺 + 无静默跳过」两条静态证据。
⚠ **仍然没有实机验证过替换式**（见下节），所以 §4 照旧给覆盖式（它总是对的）。

## 音频往返：**474 条一条不少**，但解回来的**名字**不是清单里那些

把产出的 `Wave Bank.xwb` 拿打包器**解回来**跟源目录逐字节比：

| 项 | 结果 |
|---|---|
| 原有 437 个 `.wav` | **437/437 逐字节相同，0 个不同** ✓（重打包没有碰过原版音频载荷）|
| 产出 xwb 头里的 `EntryCount` | **474** = 清单的 437 + 注入的 37 ✓ **一条不少** |
| 解回来的文件数 | 468（少了 6 个，见下）|

**解回来的名字对不上，是命名机制使然，不是丢数据**：这个库（Version 46）里**没有条目名表**，
解包器是**照 Sound Bank 反推名字**的 —— 所以注入的那些在解回后叫
`Music/FlashShifter.StardewValleyExpandedCP_Atlantis.wav`（cue 名），而不是清单里的 `Music/Atlantis.ogg`。
其中 **7 条**（`DistantTrain` / `Taxi` / `ZCCC_Applause` / `Truck*` / `GrampletonTrainStationArrival`）
是**变体 Cue**（`Sound Bank.xsb.json` 的 `Variations` 里 `Cue: null` + `WaveBankIndex`/`TrackIndex`），
反推不出名字 ⇒ 名字为空 ⇒ 都往同一个空名文件上写、**只剩最后一个**（那 6 个文件差与
解回总字节比数据区少 15.5 MB 都对得上）。

⇒ **对播放无影响**：接线是按下标走的（`Variations` 里明写 `WaveBankIndex` / `TrackIndex`），
名字只是工具链的标签。⚠ 但**再解一次再打包**这套循环会丢这 7 个名字 —— 记在这儿，
别把"解回来的文件名"当成"库里有什么"。

### 顺手把**接线**也核了：37 条注入**逐条**都指向正确下标

既然后端是按下标接的，那就直接核下标：

```
Sound Bank.xsb.json 的 Variations       132 条，WaveBankIndex 全是 0
TrackIndex 范围                          0 … 473
TrackIndex ≥ 437 的条数                  **37**，取值正好是 437,438,…,473（无缺、无重）
```

⇒ 37 条复杂 Cue 的变体**一对一**指向新追加的 37 个波形条目（追加在 437..473）✓。
配合头里的 `EntryCount = 474`，这条链路在**下标层面**是闭合的。

## 附带审计：`Data/*.json` 里所有**贴图引用**都指得到东西

扫产出树的 `Data/*.json`（只看主资产，跳过语言变体），把 `Texture` / `Portrait` / `TextureName` /
`Image` / `ImageSource` / `SpriteSheet` 这类字段的字符串值（含数组与嵌套对象）全抽出来，
按 `/` 归一化后去树里找（试 `.png` / `.xnb` / `.tbin` / `.json` …）：

```
产出树：解析到 1251 处，**找不到 10 处 / 9 种**
原版树：解析到  448 处，**找不到  5 处 / 5 种**（ParrotBoy / Monsters/Shadow_Brute / MrQi / Mariner / MOD_FARM）
```

原版那 5 个**两边一模一样**（我的解析规则比游戏粗：NPC 的 `TextureName`/`Portrait` 是相对
`Characters/`、`Portraits/` 解析的），剩下的逐条查清：

| 处 | 引用 | 查证 |
|---|---|---|
| 3 | `MarlonFay` ×2、`OrinothlogistsGuild_Kyle` | **是误报**：`Portraits/` 与 `Characters/` 下**都有这张图** |
| 1 | `Characters/Monsters/Shadow_Brute` | 与**原版同样找不到** ⇒ 不是我们引入的 |
| 1 | `Portraits/GuntherSilvian_Beach`、`Characters/…` | **游戏会优雅跳过**：`NPC.cs:1027/1060` 是 `if (content.DoesAssetExist<Texture2D>(..._Beach))` 才加载，缺了就用常服（日志一条 Warn）。SVE 季节性服装想加"海滩装"，而目标不存在 ⇒ **CP 下也加不上**（EditImage 要目标已存在）|
| 1 | `Mods\FlashShifter.StardewValleyExpandedCP_Aged_Blue_Moon_Wine\Axel_Vendor` | ★ **包侧写错了**：SVE 源码里 `Load` 建的是 `Mods/FlashShifter.StardewValleyExpandedCP/Axel_Vendor`，而 `Data/Shops` 里写的却是把**物品名**拼进路径的那一串 ⇒ **CP 下同样是死引用** |

⇒ **我们的翻译没有造出过悬空贴图引用**；唯二真正悬空的两处都能在**模组自己的数据**里指出来。

（这一轮把这个审计收成了脚本 `tools/audit-texture-refs.py` —— 补的是"`verify-tree.py` 那几项只查地图那侧、
查不到数据资产里的图片名"这一块。带上 `--base` 之后它自己就会把上面那张表简化成一句话：

```
产出树  解析到 1257   ★找不到 3    ○变体缺失 1
基准树  解析到  451   ★找不到 2
⇒ 产出比基准**多出**的「找不到」：1  —— 就是 SVE 那条 Axel_Vendor
```）

## 附：FTM 邮寄链路在**全量产出树**上的完整性检查

一次性 area 改成「`Data/TriggerActions` 一次性动作 + `Data/Mail`」这条路（见
`research/2026-10-11-ftm-can-land-forage-buried.md`），在 `D:\tmp\prof19\tree` 上逐项核过：

```
产出 Data/mail.json          319 条（原版 179）  其中 FTM 生成 84
产出 Data/TriggerActions.json 135 条（原版  31）  其中 FTM 生成 84
邮件 id 重复 0        TriggerActions Id 重复 0
84 个 AddMail 引用**全部指向存在的邮件**（0 个悬空）
与原版同名而会**覆盖**原版键的：0（id 全带 `SdvCpTool_FTM_` 前缀，撞不上）
缺 MarkActionApplied 0    缺 Condition 0
```

一条实样（`Condition` 的写法与**原版那 31 条同一套**）：

```
"Id": "SdvCpTool_FTM_mail_Custom_HighlandsOutpost_Spring_1",
"Trigger": "DayEnding",
"Condition": "SEASON Spring, PLAYER_VISITED_LOCATION Current Custom_HighlandsOutpost,
              PLAYER_HAS_SEEN_EVENT Current 65360187, !PLAYER_HAS_SEEN_EVENT Current 67363138",
"Action": "AddMail Current SdvCpTool_FTM_Custom_HighlandsOutpost_Spring_1",
"MarkActionApplied": true
```

`PLAYER_HAS_SEEN_EVENT` / `PLAYER_VISITED_LOCATION` 都是原版 GSQ 里**确证存在**的
（`GameStateQuery.cs:1435` 与同表其它 PLAYER_* 项；原版的 `Data/TriggerActions` 里也在用）。

### 落下来的 1260 条 `Forage`/`ArtifactSpots`，物品引用能不能解析？

把产出树里 `Data/Locations` 全体 `Forage`/`ArtifactSpots` 的 `ItemId` 抽出来，逐个去
`Data/Objects` / `Data/BigCraftables` / `Data/Weapons` 里按 **id 或 Name** 找：

```
条目 1260    不同 ItemId 188    解析不到：23 处 / 6 种
```

那 23 处**全是误报**，逐条查清了：

| 写法 | 处数 | 真相 |
|---|---|---|
| `(W)Tempered Broadsword` / `(W)Steel Falchion` / `(W)Claymore` | 12 | 在 `Data/Weapons` 里**按 Name** 有（我第一遍只查了 Objects/BigCraftables）|
| `LOST_BOOK_OR_ITEM (O)770` / `SECRET_NOTE_OR_ITEM (O)390` | 10 | **是游戏自己的物品查询语法**：`ItemQueryResolver.LOST_BOOK_OR_ITEM [备选查询]`（`ItemQueryResolver.cs:323`）、`SECRET_NOTE_OR_ITEM`（`:563`）|
| `RANDOM_ARTIFACT_FOR_DIG_SPOT` | 1 | 同上，`ItemQueryResolver.cs:450`：按地点的刷出权重随机挑一件神器（原版 `ArtifactSpots` 用的就是这个哨兵）|

⇒ **FTM 那 4 个包里写的就是原版查询语法**（`LOST_BOOK_OR_ITEM` 只出现在 SVE 的 FTM 数据里，
我们这边**原样透传**，代码里没有这三个串）。**1260/1260 都解析得到。**

### 写出去的地点**不是空壳**

`Data/Locations.<地图>.Forage` 这种写法，如果那张地图**在 `Data/Locations` 里没有条目**，
合并会**新建**一个只有 `Forage` 的键 —— 那会是个没有地图的假地点。核了一遍：

```
带 Forage/ArtifactSpots 的地点          70
其中没有 CreateOnLoad.MapPath 的         9   —— 全是原版就有的**农场类型**
                                        （Farm_Standard / Farm_Beach / Default …，它们本来就没有 MapPath）
**原版没有、又没有 MapPath 的键**         0
```

⇒ 没有造出过一个空壳地点 ✓。

## ⚠ 后续增量（2026-10-11 晚，**没有重跑整树**）

在那之后树上又发生了这些变化，**都是可加的、单独验过**，所以上面的整树数字仍指向它们之前那棵树：

| 变化 | 验法 |
|---|---|
| **+56 个外观资产**（7 个 NPC × 四季 × {`.png`,`.config`}，令牌枚举展开的产物）| 单独打包 **28/0**（`2026-10-11-seasonal-dynamic-token-enumeration.md`）|
| **−2 条对话条目**（SVE 那两条 `"when"` 补丁，从"无条件写进去"改成"不写"）| 同参数对比：树只变这 1 个文件（`2026-10-11-patch-field-guard-and-case.md`）|
| FTM 的 `When`（文件级条件）/ 报告说明 | 同参数对比：**产出树逐字节零差异** |

⇒ 结论（产出树打得全、无静默跳过、往返无内容丢失）**不变**。

### ★ 已经重跑过一遍（2026-10-11 更晚，**含上面全部增量**）

```
成功: 6516  失败: 0            产出 6520 个文件（= 6516 个 .xnb + XACT 那 4 个）
输入 .config 6518 → 产出 .xnb 6516，差的 2 个仍是那两个波形库容器（都在：37 MB + 1.1 GB）
       （比上一轮多 28 个 = 7 个 NPC × 四季的外观资产）
```

解回来再比（13562 → 解回 13557）：类别与上一轮**逐项相同** ——
`.png` 2196（预乘，比上轮 +8 就是那几张新外观图）、`.tbin` 232（4 处 `.png` 被剥）、
`.json` 差异 72（同一批：缺省物化 / 大小写归一 / 框架专属字段 / 非原版命名空间资产的模板不匹配）、
源树有解回没有的仍是 `ContentHashes.json` + 35 个 `.ogg`。

⇒ **当前这棵树（13562 个文件）的回包与往返都是上面这组数**，不再是"改动前那棵树"的。

## ⚠ 未确证

**没有实机验证**（没人进游戏看/听）。本机游戏那份 MonoGame 是 x86，探针跑不了 ——
与 `2026-10-10-known-losses.md` §4 记的是同一个限制。这份记录能证明的是
「打得出来、解回去对得上、音频确实进了库」，**不能**证明「进游戏就正常」。
