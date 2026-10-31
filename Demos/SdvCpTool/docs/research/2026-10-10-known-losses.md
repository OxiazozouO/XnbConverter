# 已知损失清单：纯 XNB 产出**明确拿不到**的东西

（所有能离线计的类别都已量化到条数；末尾只留真正判不了的那几项。）

日期：2026-10-10

> ⚠ **语料口径已扩大（2026-10-20）**：本文的「DLL 只写 7 个原版资产」「69 个 DLL」是旧语料。
> 当前 220 个 DLL / 270 个内容包的全量账、38 个未转框架包与 633 个 DLL 注册资产条目，
> 见 [`2026-10-20-pure-xnb-dll-losses.md`](2026-10-20-pure-xnb-dll-losses.md)。
> 本文其余 CP 条件损失的逐条数字仍然有效。

把散在各处的「能力边界」收成一张表，方便判断"这个方案到底适合谁"。
每一条都给了**量出来的数**与**证据在哪份文档**；没量过的标「未量化」。

---

## 1. 带条件的**地图结构**改动 —— 约 34 条（SVE 一个包）

原版地图**没有承载条件表达式的槽**（只有 `ConditionalDoor` 开/挡门、`BuildConditions` 能否盖房，
两者都改不了图块内容），所以这些改动落不下去。构成：

| 类 | 条数 |
|---|---|
| 来源块**自身带传送**的 `FromFile`（21 条带条件被拒）| 22 条里拒 21 |
| `MapProperties` 里含 `Warp` | 9 |
| `TextOperations` 指向 `Warp` | 3 |
| `AddWarps` | 1 |
| 其余**地形块**（路/桥/入口，`MapProperties/MapPatches`）| 落在 `LocalEdit` 那 390 条里 |

⚠ **不是**"几百条地形全丢" —— 被拒的 417 条 `FromFile` 里 **105 条是小动物贴图、67 条是作物阶段/节日装饰**，
与"能不能走进去"无关。证据：`2026-10-09-why-new-maps-unreachable.md`（含订正）。

**为什么会是边界 —— 三个角度都核过**（同一份文档里）：

1. 原版地图**没有承载条件表达式的槽**（只有 `ConditionalDoor` / `BuildConditions`，都改不了图块内容）；
2. **没有"按季节换地图名"这条通道** —— 反编译的 `GameLocation` 里季节只影响**图集**名字与音乐/光照，
   地图路径来自 `Data/Locations.CreateOnLoad.MapPath`，是一个字符串。`Maps/Town_spring` 不会被加载；
3. **"按农场类型分地图"这条通道是真的**（原版每种农场类型各有地图文件），
   但本语料里「条件只由 `FarmType`+可烘焙组成」的 `EditMap` 只有 8 条、**没有一条**的目标是农场那类地图
   ⇒ 救回 0 条。（**将来遇到改 `Maps/Farm_*` 的包，这条值得实现。**）

## 1. 带条件的**地图结构**改动 —— **1013 条**（232 包全量实测，2026-10-13 重数）

> ⚠ **订正**：本节原写「约 34 条（SVE 一个包）」—— 那是 32 包语料时代的数。
> 232 包全量重数：`EditMap` 共 2795 条，其中 **`LocalEdit` 1013 条**（全部含运行时令牌）。

原版地图**没有承载条件表达式的槽**（只有 `ConditionalDoor` 开/挡门、`BuildConditions` 能否盖房，
两者都改不了图块内容），所以这些改动落不下去。构成（**回读补丁体**逐条数出来的）：

| 要改的东西 | 条数 | 性质 |
|---|---|---|
| `MapTiles`（逐格改图块）| 358 | 结构性 |
| `FromFile`（换区域块）| 419 | 结构性 |
| `MapProperties` | 84 | 地图属性 |
| `TextOperations` | 65 | **牌子文字，纯外观** |
| `AddWarps` / `AddNpcWarps` | 20 | **加传送点** |
| 其它 / 组合 | 66 | |

按目标分：**62% 打的是 mod 自己的地图**（约 620 条），38% 打原版地图（约 385 条）。
**不要拿"1013 条全丢"下结论** —— 逐条量过它们对玩家可见的东西（见下）。

**为什么会是边界 —— 三个角度都核过**：

1. 原版地图**没有承载条件表达式的槽**（只有 `ConditionalDoor` / `BuildConditions`，都改不了图块内容）；
2. **没有"按季节换地图名"这条通道** —— `StardewValley.GameData.Locations/LocationData.cs` 里地图路径是
   **一个字符串**（`CreateOnLoad.MapPath`）；全库扫 `currentSeason + "…"`，只出现在**节日 / 邮件 /
   NPC 对话与日程 / 图集**（`GameLocation.cs:16439 GetSeasonalTilesheetName`），**没有一处拼地图名**；
3. **"按农场类型分地图"这条通道是真的**（原版每种农场类型各有地图文件）——
   ✅ **已实现**（2026-10-12）：`FarmType: X` 且目标就是 `Maps/Farm_X` 时判「目标自证」、按无条件改。
   ⚠ 只对**同一张图**成立，改别的地图的 `FarmType` 不适用。

### 1.1 玩家**看得见**的损失：量过，原版地图的传送点**真缺口 0**

`tools/lost-warps.py <产出树> <原版树> --reports <产出根>`（2026-10-13，232 包）：

```
原版 259 张地图里，少传送点的 1 张、合计 1 条 —— 逐字节看是被补丁**改指别处**（mod 的意图）⇒ 真缺口 0
另有 33 张 / 84 条是**包自带整张 `Load` 替换**（游戏里 CP 也整张换）—— 不是我们的损失
```

⚠ **订正**：`PatchJudge` 里那段「52 条被丢 ⇒ 9 张原版地图 warp 比原版少（Summit 8→2 …）」
把**整张 `Load` 替换**算成了丢失；真正因条件判不了丢的部分，随着包内 `DynamicTokens`/配置字段那次
烘焙修复已经补回。**丢的主要是 mod 自己地图上的新内容**（门/路/装饰）与牌子文字。

### 1.2 「季节怎么也丢」的另一半是**求值器 bug**，已修（2026-10-13）

`Season |contains=A, B` 的 CP 语义是「把令牌值折成 **true/false 字符串**、再与声明值比」
（声明 `false` = **取反**）—— 我们的求值器把具名参数丢了 ⇒ 全语料 **400 处**（SVE 365）
被判"本就不适用"而整条丢掉。已修，并顺带救回被它拆散的**四季图集组**
（四季变体整族落盘）。见
[`../verification/2026-10-13-season-contains-and-seasonal-groups.md`](../verification/2026-10-13-season-contains-and-seasonal-groups.md)。

**取舍材料**（要不要把运行时条件按"成立"烤）：
[`2026-10-13-runtime-conditioned-map-edits.md`](2026-10-13-runtime-conditioned-map-edits.md)。

## 2. DLL 在**原版资产**上的运行时写入 —— 只有 7 个资产

全量审计 69 个带 DLL 的模组，**写入原版资产**的只有：
`Data/Mail`、`Data/mail`、`Data/CraftingRecipes`、`Data/Objects`、`Data/Shops`、
`Maps/Woods`、`Maps/VFStatue`（后两个是"可见鱼"在装了 SVE 时贴的东西）。都是小体量。
证据：`2026-10-09-dll-mod-audit-and-map-warp-verification.md`。

## 2.5 非 CP 框架包（FTM）—— **觅食/埋藏物已落地**，刷怪/刷矿/大件落不了（2026-10-11 新增）

4 个 FTM 包（Farm Type Manager）现在会被翻成 `Data/Locations` 的补丁：

- **落地的**：全语料新增 `Forage` 644 条 + `ArtifactSpots` 432 条（41 张地图）；
  22 个"一次性"area 改成**邮寄**（`Data/TriggerActions` 一次性动作 + `Data/Mail`，
  条件里加 `PLAYER_VISITED_LOCATION`）—— **投递方式变了**，报告里明说；
- **落不了的**：刷怪 **246** 个 area（17 张地图）、刷矿 9、大件 12 —— 原版没有数据驱动入口
  （连"换地点类"这条路也逐条试过，见 `2026-10-11-ftm-can-land-forage-buried.md` §5）。
  ⚠ **SVE 自定义地图在原版机制下不会有怪**，装机时要说清楚。

## 2.7 地点的类来自 DLL（`CreateOnLoad.Type`）—— **3 个地点**（2026-10-13 新增自检）

`Data/Locations` 的地点条目可以写 `CreateOnLoad.Type = 类型, 程序集`（原版全都**不写程序集**，
如 `StardewValley.Locations.Town`）。包把这些类**定义在自己的 DLL 里**时：

```json
"Custom_CapeKeyMaze": { "CreateOnLoad": {
    "MapPath": "Maps\Custom_CapeKeyMaze",
    "Type": "CapeStardewCode.CustomLavaLogic, CapeStardewCode" } }
```

纯 XNB 装机里**没有那个程序集** ⇒ 游戏建地点时直接抛
`System.Exception: Invalid type for location Custom_CapeKeyMaze: CapeStardewCode.CustomLavaLogic, CapeStardewCode`
⇒ **地点根本创建不出来**（实机日志：`Couldn't create the 'Custom_CapeKeyMaze' location.
Is its data in Data/Locations invalid?`）。后果不只是"那个地点进不去" ——
它的**地图、从别处指向它的传送、挂在它下面的内容全都到不了**。

**全语料唯一命中**：`Cape Stardew` 的 `Custom_CapeKeyMaze` / `…3` / `…4`（类 `CustomLavaLogic`，
承载岩浆洞的逻辑）。⚠ 链式跑法（`convert-mods.py`）里这份自检**逐包都跑**，
所以这三个会被记在每个**后续**包的报告里（171 = 3 × 57 包）—— 单包跑时就是这一包的结果。

**报告里看得见了**（`report.json` 的 `locationClass` 节 + 文字摘要里一行 ⚠）：
`LocationClassAudit`（`src/SdvCpTool.Core/Reporting/`）。它**不需要基准树**，逐包都跑。

⚠ **只报不改**（这是个**取舍**，没有默认动作）：把 `Type` 删掉，地点会以普通 `GameLocation`
建起来、地图也就到得了 —— 但那个类承载的行为（伤害 / 传送 / 生成逻辑）会一起消失，
可能比"地点不存在"更糟（比如没有出口）⇒ 留给装机的人决定。要改的话，
改的应该是**产出树**（`Data/Locations.json`）而不是转换器。

## 3. 别的框架注册的数据资产 —— 5 个（本语料）

**实测（2026-10-11 晚按报告重数）**：这一档共 **10 条 / 5 个资产**：

| 资产 | 条数 | 包 |
|---|---|---|
| `furyx639.CustomBush/Data` | 5 | 08 聚宝盆 作物 |
| `spacechase0.SpaceCore/ObjectExtensionData` | 2 | 08 |
| `spacechase0.SpaceCore/CropExtensionData` | 1 | 08 |
| `spacechase0.SpaceCore/CookingRecipeOverrides` | 1 | 21 聚宝盆 制作 |
| `platinummyr.CustomBackpackFramework/dictionary` | 1 | 10 更多背包升级 |

它们的消费者是框架的 **Harmony 补丁**（SpaceCore 全 31 个注册表里 22 个如此、0 个往既有资产合并），
XNB 里没有地方放。**报告里是 `NoLandingPoint`、缘由写明"别的 mod 注册的自定义资产"** ⇒ 看得见，不是静默丢。
证据：`2026-10-10-which-framework-registries-can-bake-to-xnb.md`。

⚠ **订正两处**（原表是"5 个"：3 个 SpaceCore + CustomBush + `UnlockableBundles/Bundles`）：
① `platinummyr.CustomBackpackFramework/dictionary`（包 10）原来漏了；② `UnlockableBundles/Bundles`
（包 26）**不在这 5 个里** —— 它第一段**不带点**，判据认不出它是"mod 命名空间"，于是走的是另一条理由
（`NotApplicable` 1 条 + `NeedsReview` 1 条："新建资产带条件，但该资产类型的条目无法承载条件"）。
⇒ 两条路的**结论相同**（都不写、都看得见），但**理由不同**，别把两者混成一档。
这一轮"自己 / 别人命名空间"的完整口径见
[`2026-10-09-non-cp-framework-channels.md`](2026-10-09-non-cp-framework-channels.md) §7.2。

## 4. **音频：13 张地图没有音乐** —— **已改为音频注入，但残留「不循环」**（2026-10-10 更新）

### 原来的样子

SVE 的 `Data/AudioChanges` **37 条**全被拒（值是 `{{AbsoluteFilePath: assets/Music/*.ogg}}`，
绝对路径没法静态化）。

⚠ **订正（2026-10-10 复核）**：原稿写「14 个 id 被 19 张地图引用」，那个数字**不准**
（当时是拿短名做**子串**匹配数出来的）。按 tbin 的字符串格式精确解析后：

**被 `Music` 属性指向、且正好是我们注入的 cue（长名）的地图 = 13 张**：

```
ZCCC_Crowd_Ambience ← 4 张（FoodCourt/MainHall/Stage/VendorFloor）
FirstSlashTheme     ← 2 张（GuestRoom/Hallway）
ZuzuCity_Ambience   ← 2 张（ZCCC_Entrance / ZuzuCityStreet）
CrimsonSands / GrenvilleFalls / GrampletonTrainStationAmbience /
CrystalCavern / OminousCavern 各 1 张
```

⚠ 另有 `OminousCavern` / `CrimsonSands` / `FirstSlashTheme` / `Atlantis` /
`CastleVillageOutpostAmbience` 这些**短名**写在地图里（`DesertRailway`、`IridiumQuarry`、
`FirstSlashGuild`、`Atlantis`、`CastleVillageOutpost` 等）—— **那些不是我们的损失**：
查过原版声库里**没有**这些短名，而 CP 的 cue 名取 `AudioChanges` 的 `ID`（是长名），
所以**在 CP 下它们本来也响不了**，是 SVE 自己地图写短名写错了。

（`Custom_EnchantedGrove` 的 `Nexus`、`Custom_Highlands` 的 `HighlandsTheme` 走的是 CP
`EditMap` 的 `MapProperties.Music`，属**条件化地图改动**，能不能落地看那条补丁本身。）

**为什么不写绝对路径**：技术上可以写"这台机器上真实的绝对路径"（CP 运行时就是这么解析的），
但那条路径指向 `Mods/` 里的 `.ogg`；一旦模组被删，游戏加载这条 cue 就会失败
（可能启动就报错）。按"宁可少写，不可写错"**不写**。

### 现在改成什么了（`AudioChangesMerger`）

那条补丁不再被拒。1.6 的音频本来就是 XACT，于是改成三件纯文件操作、**全程在 xnb 体系里**：
ogg 复制进 `XACT/Wave Bank/Music/`、在 `Wave Bank.xwb.config` 的 `Entries` 加一条
（`Offset = -1`，`Length`/`Format` 留 0 由打包器解码回填）、在 `Sound Bank.xsb.json` 里
加一条 Cue 指向它，**cue 名就用 `AudioChanges` 的 ID**（地图 `Music` 引用的正是它，
所以地图不用动）。解码用**移植进 XnbConverter 的 NVorbis**，不依赖 ffmpeg。

端到端（SVE 全包）：波形 Entries **437 → 474**、Sound Bank Sounds **465 → 502**，
`CategoryId` 分布 `{2:27, 3:7, 4:3}`（按 `.xgs` 类别表解析，Music/Sound/Ambient）。

### ~~残留的损失：这 37 条播一遍就停~~ —— **已修（2026-10-10 同日）**

（原稿记：新 cue 写成「复杂 Cue + 变体类型 0」，MonoGame 走
`XactSoundBankSound(...)`、`complexSound = false` ⇒ 没有 `XactClip` 也就没有 `loopCount`；
语料里 `Looped: true` 的 **30 条**会播一遍就停。）

**现在的形态**：走「简单 Cue → sound 记录 → `XactClip` 的 `loopCount(255)`」，
逐字节照原版音乐（`spring_day_ambient`）的样子写。`Looped` 为假仍走复杂 Cue + 变体类型 0。
落点在 XnbConverter 的 `XactClipReader.Write` / `XactSoundReader.Write` /
`SoundBankReader.BuildAppendingCues`，加上 SdvCpTool 这边把 `Looped` 登记进 `LoopedCues`。

⚠ **仍未确证的是"进游戏真的会循环"**：游戏那份 MonoGame 是 x86，本机跑不了探针，
所以只有字节级与读回级证据，没有实机证据（别把它记成已验证）。
证据：`docs/verification/2026-10-10-xact-looped-cues.md`；
原研究过程：`2026-10-10-why-injected-cues-dont-loop.md`。

## ~~5. 打包器打不出的 5 个 `Data/*`~~ —— **订正：今天能打（2026-10-10 晚）**

（原稿记：`Data/{Buildings,FarmAnimals,Fences,Machines,Pets}` 打不出 XNB ⇒ 7 个包对它们的改动拿不到 ⇒
必须覆盖式安装。）

今天用**同一份打包器**重跑：那 5 个资产单独打 **5/0**；**原版** `Data` 整份 **851/0**；
**产出树** `Data` 整份（含 Machines 的 11 个语言变体）**982/0**。那 5 份 `.config` 的 md5
与当时的记录一致。⇒ **这条不是损失。**

⚠ 但有一条**真实前提**：**必须在打包器自己的目录下跑**（它按相对路径 `.\.config\custom dll\`
找模型程序集）。从别处跑会失败，报的是「请检查程序集 `StardewValley.GameData.…`」，
与当时记的那条 Newtonsoft `Vector2` 消息**不是同一条**。
⚠ 「当时为什么失败」**仍未确证**（同一份二进制、同样的 `.config`、同样的原版数据，今天却打得出）。
整树也复验过：`tools/pack-check.py` 打**全部 6461 个改过/新增的文件** —— **成功 3486 / 失败 0**。
证据：`2026-10-09-five-data-assets-cannot-be-packed.md`（顶部订正）。

## 6. 值里含**代不掉的运行时令牌**的条目 —— 逐条丢

修好"逐条取舍"之后，只有**那条**不写（整条补丁的其余条目照写），并在 `writeNote` 里报出丢了哪些。
全语料这类补丁 75 条；地图文本那一类的效果：**557 种 → 2 种**。
证据：`2026-10-10-token-gate-is-per-entry-not-per-patch.md`。

> **订正（2026-10-11 晚重数）**：那句话把**补丁数**和**条目数**混了。按当前产出（`prof37`）：
> 带「含代不掉令牌的条目 N 个已跳过」的 `writeNote` 是 **48 条补丁，共跳过 74 个条目**
> （孩子剧情 34 条、SVE 9 条、谢恩 4 条、SVE 季节性服装 1 条）。
> 原来的「75」更接近**条目数**那一栏。地图文本那项**独立复核过**：自检
> 「地图 Message 的文本」报 **★2**（与基线比多出 2 种），`557 → 2` 仍然成立。

## 7. 事件：**基本落地了**（636 条里写 520 条）

`Data/Events/<loc>` 的键**本身就是条件槽**（`<id>/<前置条件>/<脚本>`），所以这一类本来就能落地
（`EventKeyRewriter`）。全量实测：

| | 条数 |
|---|---|
| 补丁总数 | **636** |
| **已写** | **520**（296 `Converted` + 152 `ConvertedNoCondition` + 72 `CreatesNewAsset`）|
| 拒 | 116（52 `NeedsReview` + 58 `NotApplicable` + 4 `NeedsRuntime` + 2 `NonTranslatableCondition`）|

被拒的缘由（前几类）：

```
20  条件含别的 mod 的纯计算令牌（Query: '{{TheMightyAmondee.CustomTokens/Child:…}}'）
 5  条件翻译不成 GSQ：条件 `Hearts` 的值里嵌套了令牌
 6  条件 `DayEvent`（GSQ 只有 IS_FESTIVAL_DAY 布尔，匹配不了节日名）
 6  值里含代不掉的 CP 令牌（`{{Spouse}}` / `{{Baby1GenderToddler}}`…）
 2  条件 `HasActiveQuest` 在 GSQ 里没有等价关键字
58  NotApplicable —— 条件折成恒假，本就不适用（不是损失）
```

⇒ **不是"事件全丢"**；剩下的是**别的 mod 的自定义令牌**与**GSQ 表达不了的几类条件**。

## 8. NPC 日程：516 条里写 222 条（43%）

`Characters/schedules/*` 的键也编码条件维度（`<NPC>/<星期>` / `<NPC>/<季节>_<星期>`），
但**补丁的 `When` 往往不被键名完全蕴含** ⇒ 落不下去。实测：

| 资产 | 补丁 | 已写 |
|---|---|---|
| `Characters/schedules/*` | **516** | **222**（166 `ConvertedNoCondition` + 40 `Converted` + 16 `CreatesNewAsset`）|
| `Data/ExtraDialogue` | 10 | **10** |
| `Data/EngagementDialogue` | 9 | **8**（1 条"结构无法自动识别"）|
| `Strings/StringsFromCSFiles` | 17 | **9**（7 条"键名不被完全蕴含"）|

被拒的主体（287 条）是**同一条规则**：「资产 X 的条目键名编码了条件维度，但补丁条件不被键名完全蕴含」。

⚠ **这条规则不是新发现**：`2026-10-06-map-mechanisms-panorama.md:164` 写着
「**逐条看过后，这个拒绝是对的**」，`2026-10-09-needsreview-top-buckets.md` 同结论。
⇒ 日程这一类**是已审过的边界**，不是漏做。

★ **订正（2026-10-11 晚）**：其中 **310 条**已经从 `NeedsReview`（"需人工确认落点"）改成
**`NoLandingPoint`（"确证无处安放"）** —— 日程与节日的**条件容量是封闭的**（候选键写死 + 值里只有
`GOTO`/`NOT friendship`/`MAIL`；节日只有 `_y<年>` 与 `<NPC>_spouse`），补丁里只要有一项在容量外
就能说死。**产出树逐字节零差异**（两种判定都不写盘），改的只是报告的诚实度：
全语料 `NeedsReview 1079→769`、`NoLandingPoint 331→641`。口径见
`Assets/ConditionCapacity.cs`（含"全在容量内就不下结论"的反向边界）。

## 9. 仍未量化的

- **事件脚本内部的运行时分支**（脚本体里按条件 `fork`）—— 那是脚本语义，静态判不了；
- 个别"结构无法自动识别"的落点（如 1 条 `Data/EngagementDialogue`）。

⇒ 保持**未确证**。

---

## 附：测试覆盖率的一次测量（2026-10-10，供参考，不是损失）

跑 `dotnet test --collect:"XPlat Code Coverage"`，排除**移植/引用进来的**代码
（`NVorbis` 6243 行、`Squish` ~4300 行 DXT 压缩、`Microsoft.Xna.*` 与 `XnbConverter.*`）后：

```
我们自己的类型 152 个、23680 行，行覆盖率约 69.5%（全量 1214 个测试）
```

⚠ **未覆盖的主要是两类，都不值得补测**：

1. **错误路径**（`catch` 分支、`MergeOutcome.Fail(...)`）—— 从全量回归看，
   `LoadApplier` 那几条（"空地图"、"找不到可复制的 .config 模板"、"不是数据资产（扩展名）"）
   **在语料里从没触发过**（writeFailed 9 条全是别的理由）；
2. **`ReportWriter` 的报告文案**（`sb.AppendLine(...)`）—— 文案这几轮改过好几版，测它等于锁死措辞。

⇒ **核心正常路径都覆盖到了**，覆盖率数字低不代表测试没写。
（`Program`（CLI 入口，964 行）覆盖 0% 是正常的：它靠端到端跑，不走单测。）
