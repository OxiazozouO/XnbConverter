# 文档索引

`research/` 是**结论**（某个机制查清之后写的），`verification/` 是**验证记录**（跑出来什么）。
每份都以日期开头 —— 日期是**结论成立的那天**，不是最后修改日。

**先读哪份**，看你带着什么问题来：

| 你的问题 | 读这份 |
|---|---|
| **我要自己做一份能装的产出** | [`how-to-build-an-install.md`](how-to-build-an-install.md)（流程 + 六个坑；§5 的探针源码在 [`../tools/AssetProbe/`](../tools/AssetProbe/README.md)）|
| 这工具现在到底能做什么、卡在哪 | [`research/2026-10-06-grand-total.md`](research/2026-10-06-grand-total.md) —— **先看它顶部那个「2026-10-11 的状态」块**（36 包 / 判定 7286 / 回包 6516/0 / 单测 1319 / 下一步卡在哪）|
| 某一档阻塞/拒绝**是不是真的** | 「逐档核查」一组（10-09，把当前所有档逐条核过） |
| CP 的某个字段/写法我们是怎么处理的 | 「CP 语义」一组 |
| 原版有没有承载某条件的机制 | 「原版机制」一组 |
| **为什么地图传送/剧情在纯 XNB 里静默失效**（DLL 注册的动作、事件命令、前置条件、触发器） | [`research/2026-10-18-dll-registered-content-surfaces.md`](research/2026-10-18-dll-registered-content-surfaces.md)（全量扫描 + 逐类替代判断）|
| **纯 XNB 到底会失去哪些 DLL/框架内容**（同伴、建筑、换色、书籍、饰品、技能、UI、存档状态） | [`research/2026-10-20-pure-xnb-dll-losses.md`](research/2026-10-20-pure-xnb-dll-losses.md)（316 个 DLL + 346 个内容包的全量账；含 662 份 manifest 的未吸收清单）|
| **能不能把框架内容当“数据库新增行”烘进原版 XNB**（AT/JsonAssets/SF/CC/CCCB/MFM 的落点） | [`research/2026-10-20-additive-baking-reassessment.md`](research/2026-10-20-additive-baking-reassessment.md)（逐表映射：`Data/Furniture`、`Data/Objects`、`Data/Crops`、`Data/Buildings`、`Data/Pets`…；哪些精确、哪些近似、哪些仍不行）+ [`verification/2026-10-20-at-additive-baking-probe.md`](verification/2026-10-20-at-additive-baking-probe.md)（C# 实跑 AT/SF/CC/CCCB/MFM，回包失败 0、幂等；含 `--dedup-cosmetics` 原版校准 0/0；MoreGrass/Bus/Train 的静态近似与 BBM 的无落点结论见 §2.8/§5.4）|
| **清缓存后的全量复验**（232 包双趟 + 框架烘焙 + 条件落点 + 去重 + 定点回包） | [`verification/2026-10-21-full-verify-after-cleanup.md`](verification/2026-10-21-full-verify-after-cleanup.md)（清理 96.1 GB、单测 1625/1625、全量报告计数；整树回包因环境内存被中止，未声称全绿）|
| **农场走不出去 / 门禁把人拦住** | [`verification/2026-10-18-dll-tile-actions-and-gate-direction.md`](verification/2026-10-18-dll-tile-actions-and-gate-direction.md)（`LoadMap→Warp`、门禁方向、面积守卫）+ [`verification/2026-10-19-farm-type-determinization.md`](verification/2026-10-19-farm-type-determinization.md)（**卡到地图外**：`FarmType` 冻结到 Standard；诊断工具 `tools/check-warps-in-bounds.py`）|
| **建档慢 / 自定义图层（云雾）不显示 / 纯 XNB 到底缺什么** | [`verification/2026-10-19-startup-layers-and-experience-gaps.md`](verification/2026-10-19-startup-layers-and-experience-gaps.md)（建档成本拆解、`SortLayers` 的整数后缀规则、体验缺口审计）|
| **主界面/季节图块没替换、界面还是英文** | [`verification/2026-10-19-flip-baking-and-locale-variants.md`](verification/2026-10-19-flip-baking-and-locale-variants.md)（翻转烘焙进图集、语言变体镜像、`Language` 确定化、中文字符串不再被英文覆盖）|
| **还有哪些资源没覆盖到**（立绘缺失、跨包地图编辑被丢、配方键显示 `{{i18n}}`、物品整批消失） | [`verification/2026-10-19-coverage-audit-pass2.md`](verification/2026-10-19-coverage-audit-pass2.md)（外部模组令牌退回默认块、链式改两趟 `Load→Edit`、JSON 键名 i18n、`{{InternalAssetKey}}`；附「不是我们的」上游清单）+ [`verification/2026-10-19-breadth-verification-list.md`](verification/2026-10-19-breadth-verification-list.md)（装游戏后逐项打勾）|
| **把自定义 NPC 改成原版可结婚**（对话/婚房/立绘/婚礼） | [`research/2026-10-18-marriage-conversion.md`](research/2026-10-18-marriage-conversion.md)（最小资产清单、语料缺口、转换流程）|
| 产出**能不能用** | [`verification/2026-10-11-full-tree-pack-with-rebuilt-packer.md`](verification/2026-10-11-full-tree-pack-with-rebuilt-packer.md)（整树回包 6516/0、无静默跳过、**原版 3550 个 xnb 一个不缺**）+ [`verification/2026-10-11-full-tree-roundtrip-unpack-compare.md`](verification/2026-10-11-full-tree-roundtrip-unpack-compare.md)（全解回逐文件比，差异都能指到既定转换）；历史：`verification/2026-10-08-full-roundtrip.md`、`verification/2026-10-09-determinism.md` |
| **某个非 CP 的框架包能不能转** | [`research/2026-10-11-non-cp-frameworks-what-can-bake.md`](research/2026-10-11-non-cp-frameworks-what-can-bake.md)（逐框架的落点分析）+ [`how-to-add-a-framework-channel.md`](how-to-add-a-framework-channel.md)（真要加时照它六步走）|
| **哪些包没被转进来** | 产出根下的 `_not-converted.txt`（每轮刷新）；当前全量账：**221 个代码模组 + 270 个内容包**（206 CP + 26 FTM 可转，**38 个别的框架包不转**）。其中 AT/JsonAssets/SF/CC/CCCB/MFM 有“新增数据表行”的增量落点，见 [`research/2026-10-20-additive-baking-reassessment.md`](research/2026-10-20-additive-baking-reassessment.md) |
| 某件事以前试过没有 | 「负结果」一组 —— **别重做** |
| 某个"不转/无落点"的**结论**还成不成立 | ★ 拿它对一眼**报告里的判定**：`Mods/` 那条就错在"结论说不转、产出却是 398 Converted + 208 Emitted"（2026-10-11 订正）。**结论性否定断言**必须与产出对得上，对不上就是文档错或代码错 |
| **音频（`Data/AudioChanges`）现在是什么状态** | [`verification/2026-10-10-xact-music-injection.md`](verification/2026-10-10-xact-music-injection.md)（已注入、怎么验的）+ [`verification/2026-10-10-xact-looped-cues.md`](verification/2026-10-10-xact-looped-cues.md)（**循环已修**，⚠ 进游戏听仍属未确证）+ [`research/2026-10-10-why-injected-cues-dont-loop.md`](research/2026-10-10-why-injected-cues-dont-loop.md)（当时为什么卡住）|

---

## 逐档核查（判断"该做没做"看这组，别查旧表）

| 文档 | 结论 |
|---|---|
| [`2026-10-09-blocked-1151-breakdown.md`](research/2026-10-09-blocked-1151-breakdown.md) | **1151 条「无法合并」逐类分解 + 为它做过的努力** —— 想一次看清"卡在哪"读这份 |
| [`2026-10-10-token-gate-is-per-entry-not-per-patch.md`](research/2026-10-10-token-gate-is-per-entry-not-per-patch.md) | **用户实机可见的 bug**：一条条目带 `{{FarmName}}` 就让整条补丁不写 ⇒ 地图上的字符串显示成**裸键** `Strings\StringsFromMaps:BlueMoonVineyard.7`。改成**逐条**取舍（CP 的粒度）——已修 |
| [`2026-10-09-mod-local-map-tilesheets.md`](research/2026-10-09-mod-local-map-tilesheets.md) | **修一个真 bug（高影响）**：模组地图引用的「包内同级图集」没被搬进产出树 —— SMAPI 会先按模组目录解析图集，纯 XNB 里没有那一层 ⇒ **整张地图建不出来**（含原版 `Maps/Forest`）。已修 + 4 条测试 |
| [`2026-10-09-textoperation-map-properties-rewrote-whole-table.md`](research/2026-10-09-textoperation-map-properties-rewrote-whole-table.md) | **行为修正**：`EditMap` 的 `TextOperations` 原来把整张地图属性表按字符串重写（CP 只动点名那一个）—— 已修 + 两条新测试 + 10249 文件零差异对照 |
| [`2026-10-09-editmap-remaining-divergences.md`](research/2026-10-09-editmap-remaining-divergences.md) | `EditMap` 写路径上**还剩**两处与 CP 的偏差（FromFile 失败的处理），**故意先不改** —— 两处语料影响都是 0，理由写在里面 |
| [`2026-10-09-dll-mod-audit-and-map-warp-verification.md`](research/2026-10-09-dll-mod-audit-and-map-warp-verification.md) | 69 个 DLL 模组全量反编译审计（当时只有 7 个原版资产被 DLL 写入；⚠ 当前 221 个 DLL 的新下界见 [2026-10-20](research/2026-10-20-pure-xnb-dll-losses.md)）+ 三条证伪，其中一条是**纠正我自己上一轮的说法** |
| [`2026-10-11-ftm-can-land-forage-buried.md`](research/2026-10-11-ftm-can-land-forage-buried.md) | **FTM 包怎么转**：觅食/埋藏物 → `Data/Locations` 的 `Forage`/`ArtifactSpots`（全语料 1076 条）；一次性 area → 邮寄；刷怪/刷矿/大件逐条试过，**确认无落点**（连"换地点类"那条路也走到头了）|
| [`2026-10-11-clothing-channel.md`](verification/2026-10-11-clothing-channel.md) | **服装通道端到端**（语料 0 条，用合成包自证）：`Data/Shirts` 条目 + 专属贴图 → 打包 2/0 → 解包**逐字段读回**；含「新贴图必须带 Texture2DReader 的 `.config`，否则打包器静默跳过」这条钉子 |
| [`2026-10-11-non-cp-frameworks-what-can-bake.md`](research/2026-10-11-non-cp-frameworks-what-can-bake.md) | **非 CP 的框架型模组里有什么值得烘成 XNB**：本机 6 个非 CP 包全是运行时行为（FTM 坐标刷怪 / CC 伙伴 / MFM 只有模板）⇒ **一条都烘不了**；服装/裤子/发型/帽子**确有原生落点**（`Data/Shirts` 的 `Texture`+`SpriteIndex`，格子公式已量过），但**语料 0 条**、本机也没装服装框架 ⇒ 现在没有可做可验的对象 |
| [`2026-10-11-remaining-buckets-why-they-have-no-landing.md`](research/2026-10-11-remaining-buckets-why-they-have-no-landing.md) | **剩下那些桶为什么落不了**（全语料 7259 条判定的构成）：条件化地图 594 / 日程 332 / 节日 202 / 新建资产带条件 224 —— 逐个查到了源码级：地图属性全表 26 个键无图块口、日程候选键**写死**且值里只有 `GOTO`/`NOT friendship`/`MAIL`、`_yN` 循环**真的存在但只有 14 条够得着**（SVE 自己的注释用的是四年周期）、`Data/BigCraftables` 之类拿 `D:\sdv_gd` 的模型类核过**确证没有条件槽** |
| [`2026-10-11-patch-field-guard-and-case.md`](research/2026-10-11-patch-field-guard-and-case.md) | **补丁字段守卫**（FTM 漏读教训的产物）：一上线就在全语料上抓到 **CP 的字段名大小写不敏感** —— `"when"` / `"Patchmode": "Overlay"`（SVE 共 12 处）在我们这边被当成"没写"，条件被丢、语义反了。修法是加载时把字段名规范成 CP 的拼写（只动名字不碰值，下游 45 处直取一处不用改）。同参数对比：判定 −2 ConvertedNoCondition / +2 NeedsReview，产出树只变 1 个文件（那 2 条 `"when"` 从"无条件写进去"变成"不写" —— 宁可少写不可写错）。另记 `MigrateLegacyNames` 属于 `CustomLocations`、**CP 自己也不读** |
| [`2026-10-11-injected-music-is-pcm-vanilla-is-adpcm.md`](research/2026-10-11-injected-music-is-pcm-vanilla-is-adpcm.md) | **产出为什么 1.2 GB**：树里 437 个 `.wav` 有 **431 个是 MS-ADPCM**（`Wave Bank(1.4)` 26/26），而注入的 ogg 被解成 PCM ⇒ 57.9 MB 变 635 MB。按 ADPCM 存只要 ~160 MB（整份回到 ~700 MB）；编码器已有、引擎支持有硬证据，卡在条目 `Format` 的 ADPCM 编码没实现/没逐位核过 + 没实机听过 ⇒ **2026-10-12 已做**（[验证记录](verification/2026-10-12-injected-music-as-adpcm.md)），2026-10-14 实测确认波形库回包已是**纯拷贝 26 秒**（[验证记录](verification/2026-10-14-pack-cost-is-per-asset.md)）|
| [`2026-10-11-seasonal-dynamic-token-enumeration.md`](research/2026-10-11-seasonal-dynamic-token-enumeration.md) | **季节分块的动态令牌：枚举展开**（本轮唯一落地的新通道）—— SVE 季节性服装的 `{{Aerobics}}` 让 9 条补丁整条被拒（盖 9 个 NPC + 一条 14 目标）。拆成各季一条后走现成的立绘发射器：立绘组 10→19、覆盖补丁 28→56，产出树**只多 56 个文件**、别的一处没动；五条"证明不了互斥就不拆"的前提各有测试 |
| [`2026-10-20-pure-xnb-dll-losses.md`](research/2026-10-20-pure-xnb-dll-losses.md) | **纯 XNB 的 DLL 侧损失全量账**：221 个 DLL 不运行；38 个非 CP/FTM 包不转（AT 381 个纹理条目/1399 变体、CC 216 个同伴、SF 21 个建筑、CCCB 2×48 个捆绑包、MFM 3 封信…）；232 个可转包里仍有 **230 条补丁 / 633 个条目**写向已安装 DLL 注册资产（33 包）；425 次自定义令牌 / 14 个命名空间。附可烘/只能近似/必丢的优先级 |
| [`2026-10-20-additive-baking-reassessment.md`](research/2026-10-20-additive-baking-reassessment.md) | **重新评估：把原版 XNB 当数据库加行**。AT 变体 → `Data/Furniture`/`Data/AdditionalWallpaperFlooring`/`Crops`；JsonAssets 12 类（11 有落点）→ `Data/Objects`/`Crops`/`BigCraftables`/`Weapons`/`Shirts`/…；SF → `Data/Buildings`（含 DrawLayers）；CC → `Data/Pets`；CCCB → `Data/Bundles`；MFM → `Data/Mail`。明确哪些是精确、哪些只是“新增物品”而不是运行时替换 |
| [`2026-10-10-known-losses.md`](research/2026-10-10-known-losses.md) | **已知损失清单**（带数）：带条件的结构改动 ≈34 条 / DLL 原版写入 7 个资产（⚠ 69 个 DLL 时代的旧数，当前 221 个 DLL 的下界见 [2026-10-20](research/2026-10-20-pure-xnb-dll-losses.md)）/ 框架注册表 5 个 / 音频那两条已修（注入 + 循环）/ ~~打包器打不出 5 个 `Data/*`~~（**已订正：今天打得出**）/ 逐条丢的令牌条目 |
| [`2026-10-10-why-injected-cues-dont-loop.md`](research/2026-10-10-why-injected-cues-dont-loop.md) | 注入的 cue **能播但不循环**：症结是「变体类型 0」没有 `XactClip`（循环信息在它的 `loopCount` 里，255 = 无限）；查清了原版走的是「简单 Cue → sound 记录 → `XactClip`」。附要修的话落在哪、难在哪（`Sounds` 段在中段，加一条要整体平移 + 重算所有绝对偏移）|
| [`decompiled-sources.md`](research/decompiled-sources.md) | 文档里约 80 处拿 `D:\sdv_src` / `D:\sdv_gd` / `D:\rsv_src` / `D:\tmp\cp_decompile` 当**源码依据**——这些反编译目录已作为临时产物清掉，这里写清它们是什么、以及怎么重建（含一处"文件名带空格"的坑）|
| [`2026-10-10-which-framework-registries-can-bake-to-xnb.md`](research/2026-10-10-which-framework-registries-can-bake-to-xnb.md) | **哪些框架的 JSON 注册表能烘焙成 XNB** —— 判据是"谁在读它"：Harmony 补丁读的转不了（本机 31 个里 22 个如此）；本语料 5 个真实注册表只有两个子集能转（配方成品+材料、`EditMap`）|
| [`2026-10-09-non-cp-framework-channels.md`](research/2026-10-09-non-cp-framework-channels.md) | **除 CP 外还有谁在补地图** —— FTM/CC/MFM 六个未处理包逐个读完：**没有一个碰地图**；`AddWarps` 只有 CP 用；另纠正「`gv2` 是过期产出」。⚠ **§7.2 两轮订正**（`Mods/` 目标那条：先是结论错 → 后是判据错），见下 |
| [`2026-10-11-mod-namespace-ownership-and-audit-allowlist.md`](verification/2026-10-11-mod-namespace-ownership-and-audit-allowlist.md) | **复核 §7.2 那条结论**：① 数重算（手搓聚合少算 17 条、错配 10 条 ⇒ 自己 633 / 别人 81，写了 640 = 90%，判据固化进 `tools/namespace-ownership.py`）；② **判据本身换掉** —— 「归谁」≠「有没有消费方」（反例 10 条：别的包写进 SVE 命名空间、我们照写且对），该看**引用**；③ 自检第十行唯一"多出"是 **SVE 自己的笔误**（`AxelVendor2.json:13` 把物品 id 当路径，`ShopMenu.cs:800-813` 证明不崩）⇒ 进白名单后回到 0；④ 顺手记两个会静默出错的口径坑（`summary` vs `patches[]`、`ContentHashes.json` 不是消费方）。代码侧只把**措辞**按归属分开（判定不变），同参数 A/B 全量对比见文末 |
| [`2026-10-11-cp-strips-xnb-from-target.md`](research/2026-10-11-cp-strips-xnb-from-target.md) | **CP 会把目标名结尾的 `.xnb` 剥掉**（`PatchLoader.cs:151-158`），我们原来没剥 ⇒ 包 01 那 8 条 `"Target":"Data/Festivals/spring13.xnb"` 被当成**新建**资产、报告对着一个不存在的资产谈落点。已修（只剥 `Target`、逐段剥、不碰 `FromFile`）；同参数 A/B：**只有那 8 条**变（目标名归位 + 理由变成真的），产出树 13562 文件**逐文件相同**，单测 1342/0。另记两条量过但**没做**的 CP 行为（`FromFile` 自动补 `.xnb`：语料 0 条；SMAPI 资产名完整归一：**未确证**）|
| [`2026-10-11-cp-format-migrations-we-dont-run.md`](research/2026-10-11-cp-format-migrations-we-dont-run.md) | **CP 的格式迁移器**（`AggregateMigration.cs:36`：比包 `Format` 新的迁移全跑）：声明 `Format < 2.0.0` 的包，CP 会把 `Data/ObjectInformation` 这类老名字**改名**到 1.6 的资产、把老形状的条目**重写**（`Migration_2_0`，两张表）。我们没实现 ⇒ 加一道**只报不修**的守卫（判 `NeedsReview` + 写清迁移目标），语料 CP 包 **0 条命中**、A/B 报告与产出树**逐文件相同**。⚠ 第一版守卫**误伤过 FTM 包**（翻译器硬写 `Format 1.28.0`、产物正好落 `Data/Locations`）⇒ 26 条觅食没写、树掉 220 KB，已修 + 回归测试；这处又是"报告比假绿、树比兜住"的活例 |
| [`2026-10-12-merged-corpus-first-run.md`](research/2026-10-12-merged-corpus-first-run.md) | **合并新语料（`Mods.zip`，105 → 276 个 mod）后的第一轮全量**：85 包 / 10963 条；逮到并修掉最大一类写入失败 —— **内容包没有 i18n 不等于失败**（CP 走 SMAPI 的占位符 `(no translation:key)`），A/B：writeFailed **369 → 137**、written **+232**、判定分布逐项相同；另记 tmx 外部 `.tsx` 这个能力缺口（16 条）与「不是我们的问题」的两条（mod 自己的 96×31 PNG / NPC 不在 `Data/Characters`）。附**每包 2.4–3.1 s** 的实测成本（"一小时"是我拿 `run-packs.py` 的每包拷树时间外推错了）|
| [`verification/2026-10-13-unconvertible-values-are-dropped.md`](verification/2026-10-13-unconvertible-values-are-dropped.md) | ★ **一个装不进去的值会弄丢整份资产**：整树回包 19 条失败里 13 条是同一类（`Data/Characters` 9 + `Data/Weapons` 4）。覆盖式装机下那份资产**退回原版** ⇒ `Data/Characters` 里 **221 个 mod 新增 NPC 定义全体消失**（自检 mod 不查 NPC ⇒ 一直没暴露）。修法：schema 出 `EnumFields`（判据是源码里的 `enum X` 声明）+ 写入时**删掉装不进去的字段并报出来**（不猜值）；自检 mod 补 `Game1.characterData.Count` 判据。验证：受控对比报告 231 包全同、树只差那两份；**单文件回包 1/13 → 14/0**；实机 **48 → 269** |
| [`verification/2026-10-13-query-token-not-expanded.md`](verification/2026-10-13-query-token-not-expanded.md) | `{{Query:}}` 只代了内层 ⇒ 残留花括号进产物 ⇒ 游戏 `Failed parsing warp`（10 行）⇒ **整串传送被丢弃**（`Custom_ConvenientRoad`）。来源是包用 ConfigSchema+DynamicTokens 拼出的 `{{Query:{{Map_Coordinates_Y}} + 2}}`。修法：新 `LiteralQueryEvaluator`（**只认纯字面量算式**）+ 解析器**先代内层再求值**。受控对比：变好 1（SVE 一条事件从"代不掉⇒不写"变成落地）+ 9 个文件 |
| [`verification/2026-10-13-ftm-day-ranges-are-invalid-gsq.md`](verification/2026-10-13-ftm-day-ranges-are-invalid-gsq.md) | FTM 的 `Days` **可以写区间**（`"1-4"`），而 GSQ 的 `DAY_OF_MONTH` 没有区间语法 ⇒ 产出 `DAY_OF_MONTH 1-4` 是**非法 GSQ**（实机 121 行、产出树 48 条）⇒ 那些觅食条目的条件整条失效。修法：展开成离散天数（越界原样留着不猜）。实机 `Failed parsing condition` 121 → 0 |
| [`tools/triage-game-log.py`](../tools/triage-game-log.py) + 装机文档 §5.1 | **把"整份日志按类别过一遍"固化** —— 上面两个 bug 都是靠它（手搓版）逮到的；§5.1 给出**已知且非我们的**六类基线，口径是"表外的才要查" |
| [`verification/2026-10-13-season-contains-and-seasonal-groups.md`](verification/2026-10-13-season-contains-and-seasonal-groups.md) | **「季节怎么也丢」查到根**：`Baker` 的 `Season` 分支把具名参数丢了 ⇒ `"Season \|contains=Summer, Fall, Winter": false`（CP 里是**取反**，官方文档例子写着 `// NOT year 1`）被判成假 ⇒ 全语料 **400 处**（SVE 365）整条丢掉。修好后**顺带暴露第二个**：四季图集组只在"本来会被丢掉"的补丁里凑四季，"春"变成可直写就散组 ⇒ 四季变体全丢（树侧少 8 文件）。两处都修（新增 `GroupOwned` 抑制直写；`GroupEmitted` 是报告口径**不能**拿来挡，按它挡的第一版让 13 条底图退化）。受控对比 89 → 92：**只在 A：0**、新增 24 文件、判定「变好 63 / 『变坏』10（实为改打包，工具口径局限）」；实机全量探针 **FAIL 1**（仍是上游缺 `spring_silo`）、改指的 8 张地图 **PASS 8/0** |
| [`2026-10-13-runtime-conditioned-map-edits.md`](research/2026-10-13-runtime-conditioned-map-edits.md) | **带运行时条件的地图改动：量清楚再决定**（决策材料）—— 全语料 `EditMap` 2795 条里 `LocalEdit` **1013** 条；逐条按政策重算**只有 2 条**漏网（都是 `Query`，本就是静态判不了）⇒ 不是判定漏、是真判不了。这些改动本来要改什么：`MapTiles` 358 / `FromFile` 区域块 419 / `MapProperties` 84 / `TextOperations` 65 / `AddWarps` 20；62% 打的是 **mod 自己的地图**。★ **订正**：新工具 `lost-warps.py` 逐张比原版 ⇒ 原版地图的传送点**真缺口 0**（33 张"少 warp"是包自带整张 `Load` 替换，CP 也一样），旧注释里"9 张原版地图 warp 比原版少"把替换算成了丢失。建议**保持现状**，真要做就做「一律按成立烤 + `--bake-runtime-map-conditions` 开关 + 烤完跑悬空 warp 自检」而不是只烤 8% 的分类档 |
| [`2026-10-12-tmx-external-tsx.md`](research/2026-10-12-tmx-external-tsx.md) | **支持外部 `.tsx` 的 tmx→tbin**（Tiled 的 `<tileset source="…"/>`，`firstgid` 在引用处）：新语料这一档 16 条以前**整张地图不写**，现在写。A/B（prof61→prof62）：**2 包变**（Crystal Island 16 条 + GI Extra 1 条）、written **+17**、writeFailed **234→217**、树新增 2 个文件（其余 15 条是改既有地图）；图集集合仍拒但理由说清是集合 |
| [`2026-10-09-needsreview-top-buckets.md`](research/2026-10-09-needsreview-top-buckets.md) | `NeedsReview` 1129 条逐桶核过 —— 全是正确拒绝；含 `NoLandingPoint` 长尾抽查 |
| [`2026-10-09-write-failed-eight-classified.md`](research/2026-10-09-write-failed-eight-classified.md) | 8 条写入失败逐条定性：3 条是**假失败**（产出本就对）、4 条能力边界、1 条 CP 也做不了 |
| [`2026-10-09-why-new-maps-unreachable.md`](research/2026-10-09-why-new-maps-unreachable.md) | **能力边界**：装进游戏后进不去 mod 新地图 —— 量清了是那 **390 条带条件的 `EditMap`**（缺的其实是三十几条结构改动，不是几百条地形）；附三条订正（季节变体地图不存在、农场类型通道存在但语料用不上）|
| [`2026-10-09-tilesheet-extension-in-tbin.md`](research/2026-10-09-tilesheet-extension-in-tbin.md) | **修一个真 bug（高影响）**：地图 tbin 里的图集名带着 `.PNG` 后缀 ⇒ XnbConverter 剥扩展名时大小写不匹配 ⇒ **整张地图创建不出来** |
| [`2026-10-09-include-gap-is-not-a-gap.md`](research/2026-10-09-include-gap-is-not-a-gap.md) | 负结果 + §4.3.1（数组落点缺口的实况与方案） |
| [`2026-10-07-refusal-audit.md`](research/2026-10-07-refusal-audit.md) | 更早一轮的拒绝理由复核 |
| [`2026-10-06-cross-corpus-survey.md`](research/2026-10-06-cross-corpus-survey.md) | 跨语料普查的方法与数字 |

## CP 语义（对着反编译源码核过）

| 文档 | 结论 |
|---|---|
| [`2026-10-08-cp-source-audit.md`](research/2026-10-08-cp-source-audit.md) | CP 有什么、我们做了什么 —— 总览 |
| [`2026-10-08-editdata-entry-semantics.md`](research/2026-10-08-editdata-entry-semantics.md) | `Entries` 的条目是**整值**替换，没有逐字段合并 |
| [`2026-10-09-cp-write-semantics.md`](research/2026-10-09-cp-write-semantics.md) | 对着源码改正的三组写入语义 |
| [`2026-10-08-cp-xnb-value-dialect.md`](research/2026-10-08-cp-xnb-value-dialect.md) | CP 的值与 XNB 树的值是**两种 JSON 方言**（两条规则：结构化标量写成字符串、**整数值写成小数记号**；都按**声明类型**换写法、不改取值）。第二条的受控验证：[`verification/2026-10-12-integer-notation-dialect.md`](verification/2026-10-12-integer-notation-dialect.md) |
| [`2026-10-08-field-name-case.md`](research/2026-10-08-field-name-case.md) | 字段名要按**不区分大小写**匹配 |
| [`2026-10-08-move-entries.md`](research/2026-10-08-move-entries.md) | `MoveEntries` 的语义 |
| [`2026-10-09-token-comma-is-not-a-separator.md`](research/2026-10-09-token-comma-is-not-a-separator.md) | `{{…}}` **内部**的逗号不是 `Target`/`FromFile` 的分隔符 |
| [`2026-10-07-cp-lexer-port.md`](research/2026-10-07-cp-lexer-port.md) | `{{…}}` 的结构判定交给 CP 自己的词法器 |
| [`2026-10-05-cp-to-gsq-token-mapping.md`](research/2026-10-05-cp-to-gsq-token-mapping.md) | `When` 令牌 → GSQ 对照表 |
| [`2026-10-05-cp-runtime-tokens-and-native-substitutes.md`](research/2026-10-05-cp-runtime-tokens-and-native-substitutes.md) / [`…state-tokens…`](research/2026-10-05-cp-state-tokens-and-native-substitutes.md) | 令牌的设计意图与原版替代路径 |
| [`2026-10-07-event-key-selection.md`](research/2026-10-07-event-key-selection.md) | 同 ID 多键的事件怎么选（事件键的候选链） |
| [`2026-10-07-wallet-item-tokens.md`](research/2026-10-07-wallet-item-tokens.md) | 一条被错判成「无解」的条件 |
| [`2026-10-07-i18n-arg-token-leak.md`](research/2026-10-07-i18n-arg-token-leak.md) | i18n 实参里的令牌 |

## 原版机制（"有没有通道"的权威）

| 文档 | 结论 |
|---|---|
| [`2026-10-06-condition-consumers.md`](research/2026-10-06-condition-consumers.md) | 原版「条件字段消费点」台账 —— **判"某位置能不能挂条件"先查它** |
| [`2026-10-06-map-condition-slots.md`](research/2026-10-06-map-condition-slots.md) | 地图只认 `ConditionalDoor` / `BuildConditions`，**都改不了图块内容** |
| [`2026-10-06-map-mechanisms-panorama.md`](research/2026-10-06-map-mechanisms-panorama.md) | 地图机制全景（四路彻查） |
| [`2026-10-06-key-encoded-variants.md`](research/2026-10-06-key-encoded-variants.md) | 「键即条件」：原版用键名编码的变体维度 |
| [`2026-10-06-implicit-conventions.md`](research/2026-10-06-implicit-conventions.md) | 名字即行为、索引的分组聚合 |
| [`2026-10-06-events-key-preconditions.md`](research/2026-10-06-events-key-preconditions.md) | `Data/Events/*` 键内前置条件（`G <GSQ>`） |
| [`2026-10-06-schedules-season-capacity.md`](research/2026-10-06-schedules-season-capacity.md) | schedules 的季节承载能力 |
| [`2026-10-06-long-tail-assets.md`](research/2026-10-06-long-tail-assets.md) | 长尾 Data / Strings 资产的条件承载 |
| [`2026-10-06-legacy-slash-assets.md`](research/2026-10-06-legacy-slash-assets.md) | 旧式 `/` 分隔 Data 资产普查 |
| [`2026-10-06-festival-map-dispatch.md`](research/2026-10-06-festival-map-dispatch.md) | 节日地图的数据驱动调度 |
| [`2026-10-06-custom-tile-actions.md`](research/2026-10-06-custom-tile-actions.md) | 非原版 tile action 全量普查 |
| [`2026-10-06-image-block.md`](research/2026-10-06-image-block.md) / [`…tbin-block…`](research/2026-10-06-tbin-block.md) | 图片块 / tbin 块 |
| [`2026-10-06-sprite-index-arithmetic.md`](research/2026-10-06-sprite-index-arithmetic.md) | sprite 索引 → 图内矩形 |
| [`2026-10-06-emitter-unknown-sweep.md`](research/2026-10-06-emitter-unknown-sweep.md) | 发射表「待查」清单的清理 |
| [`2026-10-06-xnb-roundtrip-verification.md`](research/2026-10-06-xnb-roundtrip-verification.md) | 端到端回包（**早期**，数字以 `verification/` 那两份为准） |

## 负结果（**别重做**）

| 文档 | 结论 |
|---|---|
| [`2026-10-09-target-missing-condition-order-negative.md`](research/2026-10-09-target-missing-condition-order-negative.md) | 「目标缺失时先报条件」看着更准，**实际是错的**（会把 `convertible` 虚抬） |
| [`2026-10-09-include-gap-is-not-a-gap.md`](research/2026-10-09-include-gap-is-not-a-gap.md) | 「物理补丁数 − 加载数」**不是**缺口指标 |

## 工具链（打包/解包时踩的坑）

| 文档 | 结论 |
|---|---|
| [`2026-10-09-five-data-assets-cannot-be-packed.md`](research/2026-10-09-five-data-assets-cannot-be-packed.md) | ⚠ **已订正（10-10 晚）**：当时记「`Data/{Buildings,FarmAnimals,Fences,Machines,Pets}` 打不出 XNB」—— 今天**复现不了**（5 个资产单独打 5/0、原版 Data 851/0、产出树 Data 982/0、整树定点回包 3486/0）。真实前提是**打包器要在自己目录下跑**（相对路径找 `.config\custom dll`）。「当时为什么失败」仍未确证 |
| 跑自检 | `python tools/verify-tree.py <产出树> <基准树>` —— 一条命令跑完七项检查并对照原版 |
| **要加一个新框架通道**（服装 / MFM / 别的）| [`how-to-add-a-framework-channel.md`](how-to-add-a-framework-channel.md) —— 照 FTM 那套的六步：先判值不值得做 → manifest 认包 → **现场翻成 CP 形状**让下游一行不用改 → notes 进报告 → **把框架的真实键枚举出来与自己读的逐个对**（FTM 就漏了两个）→ 受控对比验收 |
| **要写脚本/查工具** | [`../tools/README.md`](../tools/README.md) —— 60 个开发期脚本的索引（跑批量转换 / 比对两轮产出 / 产出树自检 / 量化归因 / 回包 / 生成器），含 `jsonc.py` 那个「扫语料必须用」的坑 |
| [`2026-10-09-packer-silent-skips.md`](research/2026-10-09-packer-silent-skips.md) | 打包器**静默跳过**没有 `.config` 边车的文件，而「失败: 0」根本不反映这件事——手工打包时拿 XNB 数对一下预期资产数 |

## 验证记录

| 文档 | 结论 |
|---|---|
| [`verification/2026-10-18-marriage-conversion-fixes.md`](verification/2026-10-18-marriage-conversion-fixes.md) | **婚姻转换的九处修复**：关系状态令牌冻结（Mateo/Cirrus/Hector/Sigurd/Gunnar/Teren 六人恢复可婚 + 婚房 + 礼物条目）；`Include` 路径里的配置令牌；角色外观条件只删代不掉的变体、布尔折成合法 GSQ；`TrueName` 的 i18n-only 有损放行；Jade 婚后对话别名（含中文变体）；删除 `setDating`/`setEngaged`；跨包整条替换保留 `Appearance`、同名生成外观复用、TMX 婚房共享图保留更大版本。全量 232 包同参数 A/B：丢失资产 **0**、地图只有婚房 12×9→30×27 两处差异、`(no translation:…)` 25653→2038、可婚 NPC 38→58（礼物/婚房矩形全部合法）。单测 1525 全绿 |
| [`verification/2026-10-15-token-args-and-candidate-targets.md`](verification/2026-10-15-token-args-and-candidate-targets.md) | **实机追出来的四处转换 bug**：① `{{Random:…|inputSeparator=++}}` 的具名参数没处理 ⇒ 整串当成一个备选写进产物（日程解析失败、一批对白把几句粘一起）；② `Target` 里 `{{Farm}}` 这类**代不掉但取值可枚举**的令牌原来整条不写（农场图上的便捷通道全丢）⇒ 按候选资产名各写一份（多目标要**按项拆**，否则不含令牌那项被写 N 遍）；③ 包顺序用了 Kahn 而 SMAPI 是「按名字遍历 + 依赖先行」⇒ **谁最后写**对不上；④ `Data/Minecarts` 的**数组元素自带 `Condition`**（源码 `GameLocation.cs:10246`）原来判「没有落点」⇒ 矿车目的地整批丢，修后带条件的条目 52 → 71。⚠ 农场图上**运行时条件**的 `EditMap` 仍在取舍（1011 条那一档）|
| [`verification/2026-10-14-pack-cost-is-per-asset.md`](verification/2026-10-14-pack-cost-is-per-asset.md) | **整树回包 69 分 14 秒花在哪**：**不是波形库** —— `XACT/`（1.36 GB / 641 条）单独打只 **26 秒**（纯拷贝，`Length`÷磁盘 = 1.00、635/641 条 MS-ADPCM，ogg 解 PCM 那条路早已不在），**99% 在其余 15435 个资产的逐资产固定开销**（11–12 核 / 15 并发、0.5 核·秒/资产，且**与载荷大小无关**：1.3 MB 与 31.9 MB 两类各 200 个都是 30–33 s）⇒ 订正装机文档「48 分钟大头是波形库」那条；「只打改过的资产」只省 **16%（2406/15449）**，不够本 ⇒ 不做。⚠ 本机单跑波动大（同一输入 47.6 s vs 23.3 s），前后对比必须**交替配对跑** |
| [`verification/2026-10-13-map-tilesheet-reference-shape.md`](verification/2026-10-13-map-tilesheet-reference-shape.md) | **地图的图集引用是「相对地图自己那层目录」** —— 一条规则牵出五个 bug（`..` 被当前导点剥、`MapPatcher` 照抄源引用不重定基、`Maps/` 前缀重复补、只在带前导点时才归一）；受控对比都是**0 判定变化**，树侧累计 1013 个地图文件的写法被归一；末一个（⑤ 源引用被反复重定基 ⇒ `Maps/Mines/19` 的 `mine` 变成 `../../mine`）改后实机逐张地图探针 **FAIL 2 → 1**（剩的那张是上游缺资产），见该文 §4.5。⚠ 判据由自检 mod 的**逐张地图探针**（2495 张）给出|
| [`verification/2026-10-12-map-conditions-and-seasons.md`](verification/2026-10-12-map-conditions-and-seasons.md) | **地图条件与季节**：带条件的 `EditMap` 原来被一刀切丢掉（1353 条），其中能离线判真假的（配置/`HasMod`/包内 `DynamicTokens`/`Season`）现在走烘焙通道 ⇒ 受控对比**变好 287 / 功能退化 0**；**季节图集没丢**（四季 224/226/226/228 张，游戏原生按季换），丢的是**地图结构**（游戏对地图没有按季通道，只能留一季=春天）；**运行时条件**（`Relationship`/`HasSeenEvent`/`HasFlag`，9 张原版地图的 warp 缺口）是**取舍**，未定 |
| [`verification/2026-10-12-injected-music-as-adpcm.md`](verification/2026-10-12-injected-music-as-adpcm.md) | **注入音乐改存 MS-ADPCM**（原版 431/437 条本来就是）：波形库从「撞三处 32 位上限、一个字节都产不出」变成 **1.29 GB 正常产出**（原版 460 MB ＋ 追加 886.5 MB）；整树回包 `Destination array` **0 次**、成功 15375 / 失败 25（与 `prof74` 同一批已知失败）；产出的库尺寸与 `GrowHeader` 记账**差 0**，204 条追加条目的载荷与我们的 wav **逐条字节全等**。★ 格式依据对着 MonoGame 的 `DecodeFormat` 与**游戏那份 `.xwb` 的原始字节**（437 条）核过。⚠ 「进游戏听」**只能由人做，未做**；顺手修掉撞名静默覆盖 4 处与 wav 源 2 处 |
| [`verification/2026-10-12-integer-notation-dialect.md`](verification/2026-10-12-integer-notation-dialect.md) | **整数值写成小数记号**（`IconSpriteIndex: 0.0` 写进 `int` 字段）⇒ `Data/Objects` 连 5 个语言变体打不出包。按声明类型换成 `0`（**同一个数、不改取值**）：`Data/` 子树回包 失败 **19 → 13**（修好的正是那 6 个，**新增 0**）；报告侧 231 包零差异，树侧 3.15 万文件只动那 6 个（各 35 处 token）。另两类（`Precision: 0.5`、枚举位上写布尔）会**改取值/无依据**⇒ 不猜、只记 |
| [`verification/2026-10-11-full-tree-roundtrip-unpack-compare.md`](verification/2026-10-11-full-tree-roundtrip-unpack-compare.md) | **整树往返**：13506 → 打包 6492 → 全解回 13501，逐文件比。2188 张 `.png` 字节不同**全是预乘 alpha**（原版资产往返 7168 个像素**完全幂等** ⇒ 不是损失）；72 个 `.json` 差异逐类查过，全是缺省物化/大小写归一/框架专属字段/non-vanilla 命名空间资产的模板不匹配 —— **没有影响玩法的**。真正"少"的只有 35 个 `.ogg`（变成波形条目）与账本文件 |
| [`verification/2026-10-11-full-tree-pack-with-rebuilt-packer.md`](verification/2026-10-11-full-tree-pack-with-rebuilt-packer.md) | **整树回包（重建的打包器）**：成功 6488 / 失败 0、**零条 ERROR**、6492 个文件、约 48 分钟；`Wave Bank.xwb`（1.1GB）在、`Sound Bank.xsb` 43010（原版 32062）。对照旧打包器同一棵树：汇总行同样是 6488/0 却**少那个 1.1GB 文件**、声库还是原版 ⇒ 假绿。定点往返：`.png`/`.config` 字节相同，`.json` 只差缺省物化与 `ItemID→ItemId`，`.tbin` 只差 4 处 `.png` 被剥 |
| [`verification/2026-10-10-xact-music-injection.md`](verification/2026-10-10-xact-music-injection.md) | **XACT 音乐注入的验证**：拿游戏自己那份 `MonoGame.Framework.dll` 反射加载（`AudioEngine`→`WaveBank`→`SoundBank`→`GetCue`）；含一条反面教训——**主菜单不加载声库**，所以"启动零报错"证明不了声库写对了 |
| [`verification/2026-10-10-token-gate-corpus-effect.md`](verification/2026-10-10-token-gate-corpus-effect.md) | **逐条取舍修复的全语料效果**：改后只多不少（+136 个新资产 / 122 个补回条目）；地图 Message 的文本 **557 → 2**；外加一套自检入口 `tools/verify-tree.py` |
| [`verification/2026-10-09-final-tree-after-three-fixes.md`](verification/2026-10-09-final-tree-after-three-fixes.md) | **三处修改后的全量产出**：改前/改后逐字节对比（14 个文件差，全是新搬的图集 + ContentHashes）、SVE 步 0 差异、五项检查全绿 |
| [`verification/2026-10-09-full-chain-static-audit.md`](verification/2026-10-09-full-chain-static-audit.md) | **全量链式产出的四项静态审计**（带原版基准）：warp 目标 / 地点 MapPath / 季节性图集 三项**产出新增 0**；tmx 图集引用 13 种未解析，证据指向包侧 |
| [`verification/2026-10-09-in-game-probe-after-tilesheet-fix.md`](verification/2026-10-09-in-game-probe-after-tilesheet-fix.md) | **实机探针**：改后安装里 **376 张真地图全部建得起来**（改前有 2 张建不了）；含装机必须**覆盖式**的实测依据 |
| [`verification/2026-10-09-first-in-game-launch.md`](verification/2026-10-09-first-in-game-launch.md) | **第一次实机验证**：产出装进游戏副本、原版启动 —— 零报错；但只覆盖到主菜单加载的那批 |
| [`verification/2026-10-08-full-roundtrip.md`](verification/2026-10-08-full-roundtrip.md) | 全语料 32 包回包 —— 最新 **3794 成功 / 0 失败**（§3.3） |
| [`verification/2026-10-09-determinism.md`](verification/2026-10-09-determinism.md) | 同参数跑两遍逐字节相同（**只核了 5 个包**，见文末「没覆盖到的」） |
| [`verification/2026-10-09-self-audit-findings-reviewed.md`](verification/2026-10-09-self-audit-findings-reviewed.md) | 六道自检 31 条 —— 没有一条是**我们**写错的 |
| [`verification/2026-10-09-self-written-against-authority.md`](verification/2026-10-09-self-written-against-authority.md) | 自研算法对着权威实现核一遍 |
| [`verification/2026-10-05-ridgeside-reconciliation.md`](verification/2026-10-05-ridgeside-reconciliation.md) | M1 验收：Ridgeside Village 对账 |
| [`verification/2026-10-05-translator-real-samples.md`](verification/2026-10-05-translator-real-samples.md) | M2 验收：翻译器真实样本对账 |
| [`verification/2026-10-05-merge-result.md`](verification/2026-10-05-merge-result.md) | M3 验收：合并产出结果 |

（上面那份 M1~M3 是**样本量 1**（RSV 一个包）的阶段验收，别拿它当全语料结论。）

---

## 一句话口径

**「能被游戏读到」这一层：2026-10-09 起有了实机正面证据**
（[`verification/2026-10-09-in-game-probe-after-tilesheet-fix.md`](verification/2026-10-09-in-game-probe-after-tilesheet-fix.md)
—— 覆盖式装进游戏副本后 376 张地图全建得起来）。此前那句"没有实机验证"作废。

⚠ **但实机只覆盖到那几条路径**：自检通过 / 回包成功 / 回环无损都只是**必要不充分**证据
（见 [`research/2026-10-06-grand-total.md`](research/2026-10-06-grand-total.md) §8）。
按资产分，**音频那条只有"游戏的加载器认"这一层证据**（拿 `MonoGame.Framework.dll`
反射加载验的），**没有实机听过** —— 见 `2026-10-10-why-injected-cues-dont-loop.md`。
