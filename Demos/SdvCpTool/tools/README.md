# `tools/` 索引

这些是**开发期用的脚本**（数据自检、语料量化、跑批量转换），不是产品的一部分。
产品是 `src/`。所有脚本的路径默认写死在脚本里（`D:\Steam\...\Mods`、`D:\sdv_tree`），
在上面那几个目录搬过位置之后要跟着改。

约定：**判据要能对着权威核**（游戏源码 / CP 源码 / 原版实物数据），拿不准就标「未确证」，
**不许用"看起来对"顶**。负结果也写进 `docs/research/`，免得下次重做。

---

## 跑批量转换

| 脚本 | 干什么 |
|---|---|
| `convert-mods.py` | 转**整个 Mods 目录**：按各包 `Dependencies` 自动定序，依次进同一棵树（**CP 包 + 已支持的 FTM 包**，见下）；加 `--bake-frameworks` 会在最后调 C# `SdvCpTool.Cli bake-frameworks` 烘 AT/SF/CCCB/MFM |
| `SdvCpTool.Cli bake-frameworks` | **框架内容增量烘焙（C#）**：AT → `Data/Furniture`/`AdditionalWallpaperFlooring`/`Crops`/`Objects`/`Fences`/`BigCraftables`/配方/商店；SF → `Data/Buildings`；CC → `Data/Pets`；CCCB → `Data/Bundles`；MFM → `Data/Mail`+`TriggerActions`；MoreGrass → `TerrainFeatures/grass`（每季 3 个槽位的静态近似）；BusLocations/TrainStation → `Maps/BusStop`/`Maps/Railroad` 的 Warp 图块（无票价/选站 UI）。`--dedup-cosmetics [--dedup-threshold 0] [--dedup-color-tolerance 0] [--dedup-max-pixel-diff 1.0] [--dedup-color-consistency 2.0]` 可对家具/大件做去重（默认关闭；默认 0/0 = 只去完全相同。原版校准显示最近 dHash 距离为 0，调大就可能误伤原版；像素差/变色一致性上限用于挡住相似与纯变色的误杀）。见 `src/SdvCpTool.Core/Frameworks/FrameworkBaker.cs` |
| `chain-packs.py` | 链式转换，把多个包**依次转进同一棵产出树** |
| `run-packs.py` | 跑指定的几个包，**保留产出树**供实地查看 |
| `corpus-b-survey.py` | 跑遍语料 B 全部 CP 包（只统计，重跑改 `CORPUS_OUT`；**焦点集只提速，不能拿来下结论**）|
| `verify-packs.py` | 一条命令跑完「转换 → 定点回包」，每包一行 |
| `clean-cache.js` | 清 `D:\tmp`（**白名单式**：只留 KEEP，其余全清；`--dry` 先看）|

⚠ **顺序别搞反：先跑检查、再清产出树。** 好几个自检（`check-tmx-tilesheets.py`、
`check-map-*`、`verify-tree.py`）都要一棵**含全部包**的产出树，而它得现跑（几分钟到半小时）。
2026-10-10 我跑完 `verify-tree` 就把树清了，回头想追一项自检的数，只能**再跑一遍**。
树跑出来后先把想跑的检查都跑掉，再 `clean-cache.js`。

⚠ **对比没做完之前别清 `D:\tmp`**：基线树与对照树都堆在那儿，白名单里没有它们 ——
2026-10-10 我就是先清了缓存，再想拿新跑的一棵树跟上一版比，两边都没了，
只能把已提交的状态 stash 回去重跑一轮当对照（11 分钟）。

⚠ 另一个坑：包名带中文和空格时，从 **Git Bash 传参给 Python 会截断+乱码** ——
要么在 Python 里 `glob` 出来，要么在 Python 内 `runpy` 调用。

## 跑批要多久（2026-10-13 量过）

232 包全量（`convert-mods.py`，共用一棵树）：**12 分 37 秒**（平均 3.3 s/包）。
之前是 24 分 45 秒 —— 两刀砍下去的都是"每包重复做的同一件事"：

| 每包固定开销 | 改前 | 改后 |
|---|---|---|
| `AssetTypeAudit`（全树 `.config` 各 JSON 解析两遍）| 1921 ms | 46 ms（接上 `AuditCache`）|
| 扫 Mods 树（一次 convert 里建 2~4 次）| ~1.1 s | **0.02 s**（一进程一份 + `--mod-index-cache`）|

⇒ 工具链跑批**会传 `--mod-index-cache`**（`convert-mods.py` 已传，缓存在产出根、产出树之外）。
⚠ 手工单跑不传也行 —— 就是一个包一次真扫（~0.6 s），行为与以前完全一致。
量法：`SDVCP_PROFILE=1` 看分段计时；判断某项是不是瓶颈用**配对交替**（这台机器单跑能差 30%）。
细节见 [`docs/verification/2026-10-13-corpus-run-twice-as-fast.md`](../docs/verification/2026-10-13-corpus-run-twice-as-fast.md)。

## ⚠ 跑语料前先构建 **Release**

`convert-mods.py` 走的是 `src/SdvCpTool.Cli/bin/**Release**/net8.0/SdvCpTool.Cli.exe`
（`SDVCP_EXE` 可覆盖）。只跑 `dotnet build`（默认 Debug）**不会**更新它 ——
2026-10-10 我因此拿旧二进制跑了两轮对比，得出一堆「改动导致差异」的错误结论。

```
dotnet build src/SdvCpTool.Cli -c Release
```

**做 A/B 对比**：把两版二进制各拷一份出来（`cp -r bin/Release <别处>`），
再用 `SDVCP_EXE` 指向它们、**交替**各跑几轮 —— 单跑数字在这台机器上没有意义。

## `convert-mods.py` 的两个取数细节（2026-10-10 修）

- 子进程输出是 **GBK**（.NET 在 Windows 上按控制台代码页走），不是 UTF-8 ——
  按 utf-8 解会把中文全变成替换符（`encoding='gbk'`）。
- 子进程的 stdout/stderr 默认被 `capture_output` 吞掉。`SDVCP_PROFILE=1` 的分段计时写在
  **stderr** 上，所以只在设了那个环境变量时才把它吐出来 —— 否则跑一整轮，profile 一条不留。

## `convert-mods.py` 的输出结构（2026-10-10 起变了）

**产出树是共用的一棵**：`<out>/tree` —— 全部包（CP 32 + FTM 4）依次写进它
（第 1 个包从 `--tree` 建树，之后每个都 `--tree <同一棵> --out <同一棵>`）。
每个包的 `<out>/<NN>_包名/report.json` 仍然是**每包一份**。

**为什么改**：以前是「每包一棵树」（`--tree <上一个包的产出> --out <每包一棵>`），
于是**每包都把整棵树重拷一遍**（原版树 600 MB 上下），N 个包白拷 N-1 遍 ——
纯 IO 浪费、越往后越慢。链式的正确形态本来就是「在**同一棵**树上依次应用补丁」。

⚠ 所以跑自检（`verify-tree.py` / `check-*`）时，产出树**传 `<out>/tree`** 就行，
不用再去挑"序号最大那个包"。

### 与它配套的：延迟写 + 两个内存模型（地图 / 图片）

共用一棵树（同一个 `--out`）之后冒出一个**语义 bug**，以及一个**性能坑**：

- **延迟写**（`convert` 里始终开）：所有补丁的写入先进内存，跑完一次性 `Flush()`。
  **这是正确性要求，不只是优化** —— 判定用的 `ResourceTree` 读的是磁盘，而产出树也在磁盘上。
  若写是立即的，那么**同一次运行里先写的会被后判的看到**，与旧行为（判定读输入树、写另一棵树）
  不一致。实测这一点让 **13 个 `Data/Events/*`** 两轮不同。
  延迟写之后：`OutputTree.ReadTextOrNull`/`Exists`/`ReadBytesOrNull`/`ResolveReadablePath`
  看得到 pending（同一次运行内后续补丁必须能读前面写的），`ResourceTree` 只看盘、看不到 pending。
  **跨包的链式语义不变**：第 N 个包跑完就落盘了，第 N+1 个包读得到。
- **地图内存模型**（`MapCache`）：`EditMap` 同一个目标改 N 次，不再「读整张 tbin → 改 → 写整张 tbin」
  N 遍，而是改内存里的**同一个对象**、最后写一次。实测本语料 `EditMap` 802 条打在 122 个目标上，
  **80 个目标被多条改**（`Maps/Town` 81 条、`Maps/Forest` 80 条）。
- **图片内存模型**（`ImageCache`）：同理。实测 `EditImage` **862 条**打在 **274 个目标**上，
  **133 个目标被多条改**（`LooseSprites/Cursors` 一张被改 **107** 次）。
  改动落在内存里的像素缓冲区上，`Flush()` 时每张图**重编码一次**。
  ⚠ PNG 的字节按行紧密排布（还有 zlib 流与滤波器），改一个像素也得整张重编码 ——
  **做不到「只写变化的那几个区域」**，这里定点的只是「解码/重编码各一次」。
  ⚠ **产出树里的 `.png` 由它独家持有**：读走 `TryGetPixels`、写走 `SetPixels`/`NoteWholeFile`，
  别再对产出树里的图片直接 `Png.Read`/`Png.Write`/`File.Exists` —— 那会看到没被它落盘的旧内容。
- **源图解码缓存**（`ImageCache.TryReadSource`）：`EditImage` 的 `FromFile` 是包里的源图，
  同一张源图常被贴到多个目标上；按绝对路径缓存解码结果，整个运行里同一个源文件只解一次。
- **`.config` 候选特征缓存**（`OutputTree.GetConfigFacts`）：给新资产选 `.config` 模板时要把
  **整个目录**的候选比一遍（形状分读同名 `.json`、泛型分读 `.config`）——
  `Data/` 有 **263** 份候选（含 1.2 MB 的 `Shops.json`，共 29 MB），而本语料新建 `Data/*` 资产上千个。
  缓存「根 Reader 类型 + 同名 `.json` 的根形状/首 entry 值类型」这几个字节级的小特征；
  该路径或它的同名 `.json` 一被写过就作废重算。
- **暂存拷贝**（`StageCopy`）：音频等只记**最终来源**，中间那些来源一次都不拷。

### 跑语料时的两个开关（都为提速，不影响产出）

- **全树自检总是跑**，没有开关。那几道自检（条件落点 / 形状 / 游戏数据 / 贴图 / 令牌 /
  键冲突）的代价与**树的大小**成正比、与这一包改了多少无关 —— 逐包各跑一遍等于把那批文件
  解析 32 遍。靠 `AuditCache` 顶住：每个包是一个独立进程，缓存文件固定落在
  `<产出根>/.audit-cache.json`，把「这份文件上次的结论」跨包记住，文件没变就不重算 ——
  于是**报告完整**而自检从 3m28s 降到 1m49s。⚠ 它与这一轮的树同生共死，别跨轮搬。

**实测**（同一台机器、全量 32 包那会儿、与旧基线逐字节相同）：

| | 用时 |
|---|---|
| 优化前 | 8m44s / 10m42s / 11m03s |
| 延迟写 + 两个内存模型那一轮 | **1m37s** |
| 现在（自检一律跑、缓存自动落 `<out>/.audit-cache.json`） | 同上量级，**配对比较再快约 12%** |

⚠ **这台机器单跑数字不可信**：同一份二进制两次跑能差 30%（全量 83s 与 120s 都出现过）。
要看快没快，用**新旧交替各跑几轮**的配对比较，别拿单跑结果说事。

逐项改动与踩到的坑记在 `docs/verification/` 下那两篇：
`2026-10-10-deferred-write-and-resource-models.md`（延迟写、两个内存模型）、
`2026-10-10-lazy-writes-and-inplace-merge.md`（变体延迟序列化、EditData 原地合并）。

⚠ 这两个缓存都**不是**「把资源读进语法树里算」：判定阶段仍然只做静态分析（不看像素、不解析 tbin
的内容），资源只在**产出阶段**被碰，且每份只碰一次。

## 非 CP 的框架包（FTM）

`convert-mods.py` 现在也收 **Farm Type Manager** 包（`ContentPackFor = Esca.FarmTypeManager`）：
它们在读取时被 `FtmPackTranslator` 翻成 `Data/Locations` 的 `EditData` 补丁（觅食/埋藏物 → `Forage`/`ArtifactSpots`）。
脚本替它们补一层**隐式前置**：「同容器目录下的 CP 包」先跑（FTM 改的条目正是那些 CP 包创建的）；
反过来 **CP → FTM 的依赖不算排序边**（SVE 自己声明依赖 `SVE-FTM`，那是"装了才算数"不是"要先跑"，
认了会成环、把顺序搞反）。细节与实测数字见 `docs/research/2026-10-11-ftm-can-land-forage-buried.md`。

## 对比两轮产出

> ⚠ **报告和产出树两样都要比**（`compare-runs.py` + `compare-trees-by-hash.py`）——
> 2026-10-11 晚我只比了报告，而那段临时脚本的路径替换有 bug，两边被替换成了同一个根，
> 于是"36 包逐项相同"**是假的**：真差异（26 条 FTM 觅食被新守卫拦下、`Data/Locations.json`
> 掉 220 KB）是树比兜住的。现在报告比固化成 `compare-runs.py`，**会先 assert 两个根不同**。

| 脚本 | 干什么 |
|---|---|
| `diff-trees.py` | 逐字节对比两棵产出树 —— 「改代码前后必须同参数对比」的现成工具 |
| `compare-trees-by-hash.py` | 同上，但**多进程算 sha1**（全量 25 万文件 / 几十 GB 用这个，单线程跑不完）|
| `compare-runs.py` | 比**两轮全量转换**的报告（逐包、逐补丁）：一进来先 assert 两个根不同，**报告侧的受控对比用这个** |
| `compare-pack-runs.py` | 比**同一个包**两次跑的报告（比如「单独转」vs「链式接在别的包后面」）|
| `corpus-diff.py` | 逐项对比两轮语料普查（**必须同参数**）|
| `written-assets.py` | 比**真正写到的资产集合**（不是补丁条数）—— 区分「内容变化」与「记账变化」|

## 产出树自检

| 脚本 | 干什么 |
|---|---|
| `audit-overwrites.py` | **跨包覆盖审计**：同一个资产被多个包写过时，**谁把谁冲掉了**（尤其是后来的 `Load` **整张替换**）。逐包报告看不出这类丢失 —— 每个包都说自己 `written=True`。实测语料：被写过的资产 11089、被多个包写过的 2548、其中被后来的 `Load` 冲掉的 **2708 处**；`--lost` 只列这一类。它复现了「`Maps/Farm` 的通道被 `Grandpa's Farm` 的整张 `Load` 冲掉」那个实机 bug（同一处冲掉 3 个包的改动）|
| `audit-vs-vanilla.py` | **逐包对原版树**跑那两项「链式跑法里会跳过」的自检（`conditionPlacement` / `outputShape`）：每包起一棵新树、跑完即删。⚠ 每包 ~28 s（整树拷贝是大头）⇒ 全量约 1.8 小时；一次大规模排查用 |
| `verify-tree.py` | **一条命令跑完所有自检**并和基准树对照。要手工跑七八个脚本时用它 |
| `check-map-warps.py` | 地图/地点图自洽（`Warp` + `NPCWarp`），必带基准树对照 |
| `lost-warps.py` | **产出比原版少掉的传送点**（同一张图逐五元组比）——回答"地图之间的传送不见了"到底少了哪些；`--reports <产出根>` 把"包自带整张 `Load` 替换"的图**单列**（那不是丢失）。⚠ 判据按 tbin 结构取 `Warp`/`NPCWarp` 属性，**别**在整份二进制里抓五元组形状（那会数出噪声，见脚本头部）|
| `check-map-tile-warps.py` | 地图**图块上**的 `Warp` 动作，目标地点在不在 |
| `check-map-tilesheets.py` | 地图 tbin 引用的**季节性图集**在不在 |
| `check-map-tilesheet-assets.py` | 逐张地图：引用的**每一个图集**都有图吗 |
| `check-tmx-tilesheets.py` | 全语料的 `.tmx` 引用的 tileset 图片在不在 |
| `check-map-messages.py` | 地图 `Message "键"` 在 `Strings/StringsFromMaps` 里有文本吗 |
| `check-object-sprites.py` | `Data/Objects`/`BigCraftables`/`Furniture` 的精灵下标在图集范围内吗（判据照抄游戏算法，别简写成"下标<格数"）|
| `check-localized-text.py` | 产出里 `[LocalizedText 资产:键]` 引用的键都在吗 |
| `triage-game-log.py` | 把一轮实机的 **SMAPI 日志按错误类别过一遍**（装机验收的最后一环 —— 自检 mod 只覆盖它自己检查的东西）。2026-10-13 靠它逮到两个真 bug（FTM `Days` 区间写成非法 GSQ、`{{Query:}}` 只代内层）。已知类别与判读口径见 `docs/how-to-build-an-install.md` §5.1 |
| `GameSmoke/`（**SMAPI mod，不是脚本**）| 装机自检：自己建号 → 巡 38 个地点 → 播注入的音乐 → 跨 3 天 → **逐张地图探针**（2495 张，真建 `GameLocation` 碰 `.Map`）。可选清单：`probe-only.txt`（只探这几个）、`season-sheets.txt`（核四季图集族的四季四份在不在）。⚠ 结果与用法见 `docs/verification/2026-10-12-in-game-first-playable-crash-hunt.md` |
| `AssetProbe/`（**SMAPI mod，不是脚本**）| 装机探针：逐张地图 `new GameLocation` 建一遍、报 PASS/FAIL/跳过。源码在这个目录里，用法见它的 `README.md`（⚠ 没在游戏里跑过）|
| `audit-texture-refs.py` | `Data/*.json` 里的**贴图引用**（`Texture`/`Portrait`/`TextureName`/…）指得到东西吗 —— 补的是"地图那侧查不到数据资产里的图片名"这一块。带 `--base` 时只报**比基准多出来的**（原版自己也有一批按粗判据找不到的）|
| `residual-token-scan.py` | 扫产出树里残留的 `{{…}}`（排除通道 B1 的 `{{i18n:…}}`）|
| `report-invariants.py` | 把 `report.json` 当数据核几条必须成立的不变量 |
| `namespace-ownership.py` | 目标资产名**归谁**（包自己 / 别的 mod / 原版）与判定分布；`--tree <产出树>` 再把**没写的**拿去查有没有被引用。**"该不该写"看引用，不看归属** —— 2026-10-11 那条错报（`Mods/` 目标一律不转）的病根就是把两件事混了，见 `docs/research/2026-10-09-non-cp-framework-channels.md` §7.2 |

## 量化 / 归因（回答"还剩多少、卡在哪"）

> ⚠⚠ **数"条数"一律用 `report.json` 的 `patches[].verdict`，别用 `summary`**：`summary` 是
> `AnalysisReport.Build` 里在**三套发射器改写之前**算的（`AnalysisReport.cs:127-130`），
> `patches[]` 才是最终裁决（发射成功的那些已改写成 `Emitted`）。两者**对不上而且不报错** ——
> 实测 36 包：`summary.NoLandingPoint` 1058 vs `patches[]` 741。
>
> ⚠⚠ **校验段（`spriteSheet` / `gameData` / `conditionPlacement` / …）是"按包累积"的，别跨包求和**：
> 每个包的树包含**前面所有包**的产出（链式转换）⇒ 同一条 finding 会在后面每个包的报告里重复出现。
> 数全语料就数**最后一个包**（或对整棵树单独跑一次校验器）—— 实测把 36 个包的
> `spriteSheet.badSheetWidth` 相加得到 26，真值只有 3。
> 同理 `writeFailed` 才是"试过写但失败"，`writeNote` 含"失败"会把三套发射器的拒绝也算进来（26 ≠ 9）。
>
> ⚠⚠ **`conditionPlacement` / `outputShape` 两段只在"逐包对原版树"那次跑法下有效**：
> `convert-mods.py` 是链式（`--tree <同一棵> --out <同一棵> --append`）⇒ 自检拿产出跟自己比，
> `findings` 恒 0。现在这种情形会置 `skippedSameTree: true` 并在报告里写"跳过" ——
> **看到 0 先看有没有这个标记**；要真跑这两项就用 `tools/run-packs.py`。
>
> ⚠⚠ **跑批期间别动 `Mods`**（也别动产出根本身）：`convert-mods.py` 是**一边扫一边转**的，
> 中途往 `Mods` 里加/删包 ⇒ 这一轮的输入**前后不是同一棵树**，报告里会多出/少掉包，
> **整轮作废**（2026-10-11 晚上实测：一边跑一边合并新语料，`prof43` 前两个包成了刚并进来的，
> 只能丢掉重跑）。要合语料就等这批跑完。
>
> ⚠ **`unexpandedTokens.checkedAssets` 的口径与别人**相反**：它是"**扫到过令牌**的资产数"
> （`UnexpandedTokenAudit` 里明写的取舍：避免把"一个都没有"算成"已核对"）⇒ **0 = 干净**，
> 不是"没检查"。另外它**跳过本地化变体**（`X.ko-KR.json` 这种），所以
> `residual-token-scan.py` 会多报几条 —— 实测那 2 条是**原版就有**的 `${{0}` 韩文格式化，
> 原版树同样是 2 ⇒ 拿它下结论要**跟基准树对比**，别看绝对数。

| 脚本 | 干什么 |
|---|---|
| `asset-coverage.py` | 按**资产**（不是补丁条数）统计覆盖与缺口 |
| `gap-assets.py` | 列出资产级缺口**具体是哪些**（配 `asset-coverage.py`）|
| `report-reasons.py` | 把多份报告的**判定理由**按类聚合 —— 现在卡在哪一类 |
| `not-written.py` | 真正没写出去的补丁有哪些、为什么（按 `written` 算，不按判定类）|
| `write-note.py` | 「判定说可写却没写」的按 `writeNote` 聚合 |
| `fold-reasons.py` | 「靠常量折叠判死」的补丁按依据聚合 |
| `judged-vs-written.py` | 判定 vs 结局两个方向的一致性 |
| `cross-pack-targets.py` | 量化跨包目标（`TargetMissing` / `CreatesNewAsset`）|
| `same-pack-order.py` | 「新建资产」里有多少目标**是同包前面某条 `Load` 造的** |
| `map-load-chain.py` | 地图没造出来的下游影响 |
| `interleave-value.py` | 「判定→写入交织」能救回多少 |
| `event-text-channels.py` | 原版事件脚本哪些文本通道真支持 `@` |
| `token-blocked-patches.py` | 真写出去的补丁里，值还带无法解析令牌的有多少 |
| `unexpanded-tokens.py` | 汇总报告里的未展开令牌自检 |
| `variant-entropy.py` / `variant-per-type.py` | 变体归一化的熵 / 按资产类型拆开 |
| `emitter-cells.py` | 发射表格子汇总（哪些有原生最简形式、哪些待查）|
| `dyntoken-in-when.py` / `dyntoken-scope.py` | `DynamicTokens` 的规模与影响面 |
| `token-comma-scan.py` | Target/FromFile 里「逗号落在 `{{…}}` 内」的字符串 |
| `audit-events-loss.py` | `Data/Events/*` 写了多少、拒了多少、为什么 |
| `audit-farmtype-channel.py` | `FarmType` 门控的地图改动里，目标本身按农场类型分的有多少 |
| `audit-tag-equivalence.py` | context tag 是否恰好等价于某个分类号（从游戏数据推）|
| `dll-asset-audit.py` | **带 DLL 的模组**运行时写了哪些**原版**资产 |
| `custom-locations.py` | CP 的 `CustomLocations` 这块值多少 |
| `prior-in-base.py` | `$query` 没有无条件补丁提供原值的那批 |
| `targetmissing-with-condition.py` | TargetMissing 里带条件的（**负结果工具，别照着改代码**）|

## 回包 / XNB

| 脚本 | 干什么 |
|---|---|
| `roundtrip-all.py` | 全语料逐包回包（转完就打、打完删树）|
| `stage-packer.py` | 把**重建的**打包器凑成一份跑得起来的目录（`D:\tmp\packer`）—— 回包前的前置步骤 |
| `pack-check.py` | 定点回包：只挑本轮真改过的文件连 `.config` 一起打 |
| `pack-tree.py` | **整树**回包：把整棵产出树打成可安装的 XNB 目录（装机流程的最后一步）|
| `xnb-roundtrip-check.py` | 把产出树喂给 XnbConverter，报**哪些资产打不出来** |

### ⚠ 打包器要用哪一份

| 打包器 | 能音频？ | 说明 |
|---|---|---|
| `D:\XnbConverter\out_publish\XnbConverter.Cli.exe` | ✗ | 2026-10-05 的发布版，**早于**「波形库直接吃 `.ogg`」。拿它打整树**静默丢音频**（`.ogg` 报 `无效的 WAV 格式：OggS`、`Wave Bank.xwb` 根本不生成），汇总行还写着「成功 N 失败 0」 |
| `…\bin\Release\net8.0\XnbConverter.Cli.exe` | ✓ | 但**缺运行时两件套**（`xna-shim\MonoGame.Framework.dll`、`.config\custom dll` 里三个数据 dll），直接打真资产是 **0/6488**，报错还长得像「JSON 格式不对」 |
| `D:\tmp\packer\XnbConverter.Cli.exe` | ✓ | `tools/stage-packer.py` 产出的那份，回包脚本的默认值 —— **用这个** |

## 生成器

| 脚本 | 干什么 |
|---|---|
| `gen-game-data-schema.py` | 从游戏源码生成 `GameDataSchema`（资产→元素类、类→字段）|
| `gen-wallet-items.py` | 生成 `WalletItems`（CP 钱包物品名 → 等价 GSQ）|

## 共用 / 杂项

| 脚本 | 干什么 |
|---|---|
| `cppacks.py` | CP/FTM 内容包清单 —— 语料脚本们共用那一份；另有 `find_other_packs()` 列出**别的框架**的内容包（当前语料 38 个：AT 17、CC 10、SF 2、BL 2、CCCB 2、MFM 2、TS 1、MoreGrass 1、BBM 1），`convert-mods.py` 会在开头报出来（**这类包默认不转**，别让它是静默的）；`count_dll_mods()` 数**代码模组**（字段名大小写不敏感），同样在开头报一行（纯 XNB 场景里它们用不上，但"有多少个"该说）；跑完还会把这份账落成产出根下的 `_not-converted.txt`（`manifest.json` 全部标准可读，不受 JSONC 影响）|
| `jsonc.py` | 容错 JSONC 解析（尊重字符串字面量地剥注释与尾逗号）。⚠ **扫语料必须用它**：语料里 870 个文件带注释，`json.load` 读不了，用错了会静默漏掉一大半 |
| `FrameworkBakerTests.cs` | C# 框架烘焙的最小回归（`tests/SdvCpTool.Core.Tests/`）：合成 AT/MFM/CCCB/SF 包 → 断言新增行、反斜杠贴图路径、SourceRect、幂等。 |

---

⚠ **一处待更新的说明**：`audit-audio-loss.py` 的 docstring 还写着「被拒的 `Data/AudioChanges`
条目有多少张地图在用」—— 那条补丁**已经不再被拒**了（改成注入进 XACT，见
`docs/research/2026-10-10-why-injected-cues-dont-loop.md`），这个脚本量的是**当时**的边界。
留着当历史参照，别照着它的口径下现在的结论。
