# 剩下这些桶为什么落不了 —— 逐个查到**框架实现层**

日期：2026-10-11
起因：要求「继续啃无法转换成 XNB 的，深入调查模组数据与框架实现的意图，而不是说做不到就做不到」。

本文只记**这一轮新查出来的证据**（尤其是那些把「大概不行」变成「确证不行／确证能行但不够用」的源码行号），
不重复 `2026-10-10-known-losses.md` 已有的账。

---

## 0. 底盘：全语料判定分布（36 包，7259 条）

> ⚠⚠ **数"条数"必须用 `patches[]`，别用 `summary`** —— 这两个**对不上**，而且不报错：
> `summary` 是在 `AnalysisReport.Build` 里、**四季/图集/立绘三套发射器改写之前**算的
> （`AnalysisReport.cs:127-130`），`patches[]` 里的 `Verdict` 是**最终裁决**（发射成功的那些
> 已改写成 `Emitted`）。实测 36 包：`summary` 的 `NoLandingPoint` **1058** vs `patches[]` 的 **741**
> （差 317 ≈ 328 条发射 − 少量从别的档改写来的）。
> 2026-10-11 我自己就先用 `summary` 聚了一遍、对着这一节的数以为"文档错了"，回头逐条数 `patches[]`
> 才发现**文档是对的、我错了**。本文下面所有数都是 `patches[]` 口径。

> ⚠ 这一节是**快照**。之后有两处改动，见 §10：
> ① 令牌枚举展开 ⇒ 总数 **7286**（9 条拆成 36 条），`TargetMissing 80→71`、`Emitted 300→328`；
> ② **条件容量封闭**那几族（日程 / 节日）改成"确证无处安放" ⇒ `NeedsReview 1079→769`、
>   `NoLandingPoint 331→641`（**产出树逐字节零差异** —— 两种判定都不写盘）。
> ⇒ 385 那个"键名维度"桶里，**310 条**能说死；"新建资产带条件"那 155 条里再 **100 条**也能说死
>   （同一个容量论证 —— 它们的 target 也是日程一族）⇒ 两步合计 `NeedsReview 1079→669`、
>   `NoLandingPoint 331→741`。剩下的在容量内（多为 `Season`/`Spouse`/`Day`），**可能是"有落点但没实现"**
>   （日程的 `MAIL`/`marriage_`/`rain` 那几种写法）⇒ 按口径必须留人工，不能替它下结论。

```
2137 ConvertedNoCondition     1702 Converted        218 CreatesNewAsset     300 Emitted
 818 NotApplicable（折算下来不适用，不是损失）
 594 LocalEdit                  80 TargetMissing
1077 NeedsReview               323 NoLandingPoint      4 NeedsRuntime  4 TargetContainsTokens  2 NonTranslatableCondition
```

要人工复核的 **1077** 里 **1040 是 `EditData`**。它的构成（按原因聚过的数）：

| 桶 | 条数 | 落在哪 |
|---|---|---|
| 条目键名编码了条件维度、但补丁条件不被键名完全蕴含 | **385** | 日程 332（`characters/schedules` 174 + `strings/schedules` 165）+ 节日 38 |
| 有键名维度，但本条改的**不是条目**（`TextOperations`/`Fields`） | **164** | 全是 `Data/Festivals` |
| 新建资产带条件、该资产类型的条目**无法承载条件** | **224** | 155 `Unknown` + 41 `DialogueKey` + 28 `KeyEncodedVariant` |
| 对话键 `$query` 内联不成立（同键多条带条件互相嵌套 / 没有无条件旧值） | **108** | `Characters/Dialogue` 一族 |
| 条件含别的 mod 的纯计算令牌 / 值里含代不掉的令牌 | **~60** | 各资产 |
| 「条件可翻译，但**结构无法自动识别**」 | **61** | 见 §4 |

---

## 1. 条件化的地图改动 594（`EditMap`）—— **证不出通道**

这一桶 594 条**全部**是有条件的 `EditMap`。本轮又用两条独立路子核了一遍：

1. **地图属性全表**：把 `D:\sdv_src` 里所有 `TryGetMapProperty("…")` / `Properties["…"]` / `GetMapProperty("…")`
   抠出来去重，只有 26 个键（`Warp` / `Action` / `TouchAction` / `Water` / `Outdoors` / `IsFarm` /
   `SpawnBeachFarmForage` / `SpawnForestFarmForage` / `SpawnMountainFarmOreRect` / `ForceSpawnForageables` / …）。
   **没有任何一个**能按条件改图块或刷怪。
2. **地图从哪来**：`Data/Locations.<地点>.CreateOnLoad.MapPath` 是**一个字符串**，
   没有季节/条件占位 —— `Maps/Town_spring` 这种名字不会被加载（`Maps/spring_outdoorsTileSheet`
   那种季节前缀是**图集**，不是地图）。

原版地图里真能挂条件的只有 `ConditionalDoor`（开/挡**已有的**门）与 `BuildConditions`（能否盖房），
两者都改不了图块内容。⇒ 保持 `LocalEdit`。

**补一条：全游戏只有 `ConditionalDoor` 这一个条件型 tile action。**
把 `GameLocation.cs` 的 tile action 分支全扫一遍（`case "…"`），带 `Condition` 字样的**只有它**
（`:9546`，语义是"GSQ 成立就 `openDoor`，否则挡门 + 可选的 `LockedDoorMessage`"——
连 `Warp` / `LockedDoorWarp` / `TouchAction` 都没有条件变体）。
⇒ 那 594 条里与传送/门沾边的 ~35 条也一并是**确证无落点**，不是"没查"。

## 2. 日程 332 —— 候选键**写死**，值里也只有三个条件槽

`NPC.checkSchedule`（`D:\sdv_src\StardewValley\NPC.cs` 约 5800–5890）依次去试的键是**固定的一串**：

```
marriage_<季节>_<几号> / marriageJob / marriage_<星期>
<季节>_<几号> / <星期>_<n> / <星期> / bus / rain2 / rain
<季节>_<星期>_<n> / <季节>_<星期> / <季节> / spring_<星期> / spring
```

⇒ 键空间里的维度只有 **{已婚, 季节, 几号, 星期, 周序号}**（外加 `bus`/`rain` 两个特例键）。
被拒那 332 条的条件主体是 `HasSeenEvent` / `Spouse` / `Weather` / `Hearts` —— **一个都不在键空间里**。

值（日程脚本）里能挂条件的也只有三种写法（`parseMasterScheduleImpl`）：
`GOTO <键>`、`NOT friendship <npc> <n>`、开头的 `MAIL <邮件id>`（有这封信跳两条，否则跳一条）。
即"有没有这封邮件"这一个布尔维度。

⚠ **`rain` / `rain2` 顶不上天气条件**：它们排在 `Mon`、`spring_Mon` 这些键**之后**，
是"前面都没命中时"的**兜底**，不是"下雨天覆盖 `Mon`"。CP 的
`When:{Weather: rain}` + `Entries:{Mon: …}` 要的是**覆盖**，键空间给不了。

## 3. 节日 202（`Data/Festivals/*`）

- **164 条**改的是 `TextOperations`/`Fields`（不是 `Entries`）⇒ 条件没有安放处；
- **38 条**是键名维度问题。

**本轮新发现：`_yN` 循环是真的存在。**
`Event.TryGetFestivalDataForYear`（`D:\sdv_src\StardewValley\Event.cs:4412-4438`）：

```csharp
int num = 1;
while (festivalData.ContainsKey($"{key}_y{num + 1}")) num++;   // 这个键有几个连续的 _yN
int num2 = Game1.year % num;  if (num2 == 0) num2 = num;
actualKey = (num2 > 1) ? $"{key}_y{num2}" : key;
```

即：**`_y2` 存在 ⇒ 奇年用 `key`、偶年用 `key_y2`**（原版 `spring13` 就是这套，
`set-up` / `set-up_y2` 同时存在）。理论上能把 `Year` 条件**写进键名**。

**但量下来不够用，判不实现**：

1. 只有 **14 条**的条件是**纯年份**（其余的都缠着 `HasSeenEvent` / `Spouse` / `Hearts`，缠着就没救）；
2. SVE 自己在 `code/Festivals/Fall27.json` 顶部写了注释，说明它用的是**四年周期**
   （`1,5,9,13,…,99` 与 `3,7,11,…,97`）—— 而游戏的循环是 `year % num`、`num` 由**那个键**有几个连续 `_yN` 决定：
   要凑出四年周期得把 `_y2.._y4` 全写齐，而对**第 5 年以后**的映射与 CP 的
   `Year: "3, 4"`（绝对年份）语义**不一致** ⇒ 会写出"第 7 年也放第 3 年的内容"这种**静默错**。
3. 收益 14 / 7259 ≈ 0.2%，风险是节假日行为静默错。

⇒ 按「宁可少写，不可写错」**不实现**。此条留档：将来若遇到只有 `Year` 条件、且周期与 `_yN` 对齐的包，这条路是通的。

## 3.5 条件容量封闭的族：**410 条从"需人工"改成"确证无处安放"**（2026-10-11 晚做的）

做法与口径见 `Assets/ConditionCapacity.cs`：日程（候选键写死 + 值里只有 `GOTO`/`NOT friendship`/`MAIL`）
与节日（只有 `_y<年>` 与 `<NPC>_spouse`）这两族**条件容量是封闭的**，补丁里只要有**一个合取项**在容量外
就能说死；**全在容量内的一律留人工**（可能是"有落点但没实现"）。

```
NeedsReview     1079 → 669     NoLandingPoint   331 → 741
产出树 / total / 写入：**逐字节零差异**（两种判定都不写盘）
```

剩下 669 的构成（改后重量的）：220 键名维度（**容量内**，多为 `Season`/`Spouse`/`Day`）+
55 新建资产（容量内或别的族）+ 172 对话键（理由已写明卡在哪）+ 37 带条件的 `Load` +
23 纯计算令牌 + 12 代不掉的令牌 + 19 改的不是条目 + **67「结构无法自动识别」** + …

那 67 条逐个看过能不能也说死：**schema 能答的只有 10 条**（`Data/BigCraftables` ⇒ `BigCraftableData`
无条件槽）；另 **57 条不在 schema**（schema 只覆盖 33 个资产）：`Data/NPCGiftTastes` 13 条要另立
"值是字符串列表 ⇒ 无条件槽"的规则，`Strings/*` 12 条则**连键维度都认不全**（按口径不敢说死）。
⇒ 为 10–23 条再立两条规则**不值得**，留档。

### 3.6 条件挂在 `Data/Machines` 的 `TargetField` 上 —— 15 条（全在本语料一个包）

`when` 里带 `HasReadLetter`（"装了那个附加包"的开关信）的补丁全语料 **15 条**，全部来自包 21
（聚宝盆 制作），**全部打 `Data/*`**：

| 判定 | 条数 | 是什么 |
|---|---|---|
| `NoLandingPoint` | **12** | 全是 `Data/Machines` + `TargetField`（如 `["(BC)24","OutputRules"]`、`["(BC)12","OutputRules","Default_Juice","Triggers"]`）；理由「定位不到条件落点条目」|
| `Converted` → **写入失败** | 2 | 同形状，但判定侧先合并本补丁、路径被**造生成对象** ⇒ 判可写，写入时才拒（见 §8 的追记）|
| `NotApplicable` | 1 | 条件折叠后恒假 |

**为什么落不了**：`MachineData`（`D:\sdv_gd\StardewValley.GameData.Machines/MachineData.cs`）**没有**任何
`Condition` 槽；`MachineOutputRule`（`OutputRules[]` 的元素类）**也没有** —— 只有**再下面一层**
`MachineOutputTriggerRule`（`Triggers[]` 的元素类）才有 `Condition`。
而这些补丁的 `TargetField` 指的多半是 `…OutputRules`（列表）或 `…OutputRules.<规则Id>`（规则对象）
⇒ 条件要落的那两个位置**都没有槽** ⇒ 确证无处安放 ✓（`HasReadLetter` 本身**可译**成
`PLAYER_HAS_MAIL … Received`，卡的是**落点**不是条件）。
⇒ 这 12 条的**内容是实打实的丢**（附加包的机器规则在本产出里不存在），报告里逐条有理由。

⚠ **量化过、判不做的一条**：那 **2 条** `TargetField` 指到 `…Triggers` 的（`extruder`/`preservesjar`
的附加包文件），加进去的**触发器元素**是有 `Condition` 槽的 ⇒ 原理上可以像 `Forage[]` 那样
**逐元素落条件**（CP 的列表编辑器对字符串键是「匹配不到就 `Add`」，见
`ListKeyValueEditor.TryGetEntry`/`SetEntry`）。但那要给 `EntryPaths` 加一种新落点
（`TargetField` 指向列表 ⇒ 路径得在**合并之后**按元素算），换来的只有 **≤2 条**
⇒ **不做**，留档。
（那 2 条现在表现为 `Converted` + **写入失败**，见 §8 追记 ✓ 没写错东西。）

## 4. 「结构无法自动识别」61 —— 用**生成的数据模型**逐个核对

`ConditionLanding.ProbeToken` 在认不出结构时返回 `Unknown`（→ 人工复核），**故意不冒充**「确证没有落点」。
本轮拿 `D:\sdv_gd`（反编译的 `StardewValley.GameData.*` 模型类）去核这几桶：

- `Data/BigCraftables` → `BigCraftableData`：字段只有
  `Name / DisplayName / Description / Price / Fragility / CanBePlacedOutdoors / CanBePlacedIndoors /
  IsLamp / Texture / SpriteIndex / ContextTags / CustomFields` —— **没有**任何 GSQ 槽。
- 全库带条件类字段的类**只有 19 个**（`grep -l "public string Condition" StardewValley.GameData.*/*.cs`）：
  `BuildingSkin` / `CharacterAppearanceData` / `CharacterHomeData` / `AlternatePurchaseAnimals` /
  `FarmAnimalProduce` / `FishPondWaterColor` / `GiantCropData` / `PassOutMailData` / `ReviveLocation` /
  `WeatherCondition` / `LocationMusicData` / `MachineEffects` / `MachineOutputTriggerRule` /
  `MinecartDestinationData` / `ShopDialogueData` / `ShopOwnerData` / `ShopThemeData` / `SpecialOrderData` /
  `ToolUpgradeData` / `WeddingAttendeeData`。

⇒ 这些**不是我们没查**，是**确证没有落点**。报告的措辞可以更硬（不必写"需人工确认"），
但那只是措辞：**产物一个字节都不变**，所以这一轮没有动代码。

## 5. FTM 刷怪 246 —— 又一条独立证据

除了 `2026-10-11-ftm-can-land-forage-buried.md` §5 已经逐条试过的那些路（换地点类 / `Data/Monsters` /
`performAction`），本轮补的是**地图属性全表**（同 §1）：与"生成"沾边的只有
`ForceSpawnForageables`、`SpawnBeachFarmForage`、`SpawnForestFarmForage`、`SpawnMountainFarmOreRect`，
**全是农场/觅食**，没有刷怪口。

**本轮第三条、也是最直接的一条：`Data/TriggerActions` 的动作表全文**。
`StardewValley.Triggers/TriggerActionManager.cs` 的 `DefaultActions` 一共这么多：

```
Null  If  AddBuff  RemoveBuff  AddMail  RemoveMail  AddQuest  RemoveQuest
AddSpecialOrder  RemoveSpecialOrder  AddItem  RemoveItem  AddMoney
AddFriendshipPoints  AddConversationTopic  RemoveConversationTopic  IncrementStat
MarkActionApplied  MarkCookingRecipeKnown  MarkCraftingRecipeKnown  MarkEventSeen
MarkQuestionAnswered  MarkSongHeard  RemoveTemporaryAnimatedSprites
SetNpcInvisible  SetNpcVisible
```

**没有任何"生成/刷出"动作**（连 `AddItem` 都只是给玩家物品，不是往地图上放东西）
⇒ 一次性邮寄那条路（我们用 `AddMail` ✓）是**能表达"给东西"的唯一手段**，
而"在地图上刷出东西"连一次性都表达不了。

⇒ 刷怪无落点这条现在有**三条独立证据**（换地点类逐条试过 / 地图属性全表 / 动作表全文）**+ 一份全文清单**。

## 6. 顺带确证：`HasMod` 这类条件**已经在烘焙**，不用再挖

`Merging/Baker.cs`（通道 C）已经把 `HasMod` / `ModId` / `HasFile`、包内 `config.json` 的
`ConfigSchema` 令牌、以及 `模组ID/令牌名` 命名空间令牌**离线求值成真/假**（`--mods` 给出已装模组表）。
⇒ 「条件是别的 mod 在不在」这一类**不是**在本轮那些桶里被误杀的，别照着这个方向改。

## 7. `TargetMissing` 80 里，真正由**跨包顺序**造成的是 **18 条**（量化后仍不做）

> ⚠ 先纠一个**桶名会骗人**的地方：`TargetMissing` 并不都是"目标资产不在树里"。拆开看 ——
>
> ```
> 目标真缺失（`资源树中找不到资产 \`…\``） 66
> FromFile 侧（`FromFile 含无法静态解析的令牌` / `FromFile 不存在`）**14**
> ```
>
> 那 14 条是**源图**的问题（SVE 季节性服装的 `FromFile` 里带 `{{Aerobics}}` 这种本包 config 令牌），
> 拆再多模组资源也没用。**看 `reason`，别照着桶名去解包** —— 这条与
> `tools/README.md` 里给 `targetmissing-with-condition.py` 标的「负结果工具，别照着改代码」是同一件事。
> 下面这 18 条用的是**更严的判据**（目标由**更晚**的包创建），与那 14 条不重叠。

现象（SVE 那批）：`EditMap Maps/Custom_GrampletonFields` 报「资源树中找不到该资产」，
**但那张图在最终树里是有的** —— 它是 28 号包 `格兰普尔顿旷野` 用 `Load` 造出来的，而 SVE 是 11 号。

**不是排序 bug**：`flashshifter.GrampletonFields` 的 manifest 明写
`Dependencies: [Pathoschild.ContentPatcher, FlashShifter.StardewValleyExpandedCP]` ——
它**依赖 SVE**，拓扑序**必须**排在 SVE 之后；而 SVE 反过来要改它造的那张图。
CP 侧没这个问题（所有补丁打在**同一个内容**上，`Load` 靠 `Priority` 先跑），我们是**逐包落盘**的。

量：全语料「目标由**更晚**的包创建」的补丁 **18 条**（17 `EditMap` + 1 `EditData`，都在 `Maps/`），
占 7259 的 **0.25%**。两条做法都**不做**：

1. **延后重试**（把没命中目标的补丁存边车、链末再跑一遍）—— 要引跨包状态、要防重复应用，
   为 18 条不值；
2. **按 `HasMod X` 反推顺序** —— 会与 manifest 声明的方向**成环**，而且重排会改动整棵产出树，
   得重做全量受控对比。

留档：语料若换成「互相造资产」很密的组合，这条值得做。

另：`Load` 的 `TargetMissing` 4 条全是 `FromFile` 带 **`{{season}}`**（`assets/animals/SnowshoeHare_{{season}}.png`
这种"按季节换文件"的写法）—— 静态烘焙没法让**一个资产名**按季节换内容。原版对图片按条件换图只有三种机制
（四季 BigCraftable 索引 / 图集文件名季节前缀 / NPC 立绘的 `Data/Characters` `Appearance`），都不是这套 ⇒ 保持不写。

---

## 8. 逐条看**写入失败那 9 条** —— 全是"该拒"，其中一条还查出**包侧写错了目标名**

> **订正（2026-10-11 晚重数）**：这一节原来把**两种数法混在了一起** ——
> 表里的「四季图集组发射失败 5」属于**三套发射器**的拒绝（记在
> `normalization.rejectedPatches`），**不是** `writeFailed`。`writeFailed` 的正确定义是
> `patches[].written == false`（**试过写、失败了**；`written = null` 是"压根没尝试"）：
>
> 全语料 `attemptedWrite 4066 / written 4057 / writeFailed 9`（当前产出 `prof37`），9 条逐条是：
>
> | 类 | 条数 | 谁 |
> |---|---|---|
> | 对话值内联 GSQ 失败（新值/旧值含 `|` 会撑破 `$query` 分支，或取不到旧值）| **5** | 礼物对话 `Haley`、SVE `Claire`/`Edmund`/`Martin`、鸟类观察协会 `OrinothlogistsGuild_…` |
> | 定位不到条件落点条目 | **2** | 聚宝盆 制作 `Data/Machines` ×2（`…OutputRules….Triggers` 这类 `includes` 数组）|
> | `EditMap` 的 `FromFile` 含代不掉的令牌 | **1** | 身临其境 谢恩 `Maps/spouseRooms`（`{{MessyCleanToken}}`）|
> | `EditImage` 目标不存在 | **1** | SVE 季节性服装 `Maps/Stadium` |
>
> ⇒ 结论不变（**没有一条是"能修而没修"的内容丢失**），但**别用"`writeNote` 含『失败』"去数** ——
> 那样数出来是 **26**（含发射器的拒绝）。下面 `boatJourneyMap` / `ChairTiles` 那两条是**发射器**的例子，
> 内容本身仍然成立，只是它们不属于 `writeFailed`。

> **追记（同日稍晚）：那 2 条 `Data/Machines` 的根因查清了，**归因也换了****
> （原先写的是"落点模型缺口（`includes` 数组）"，不对）。
> 原文是这样（`machines-essentialcrops/extruder.json`）：
> `"TargetField": ["(BC)Cornucopia_Extruder","OutputRules","Cornucopia_GlassNoodles","Triggers"]`
> \+ `Entries: { "(O)Cornucopia_Cassava": … }` —— 合法 CP 写法：先定位到那台机器的某条输出规则的
> `Triggers`，再往里加条目。
> **真正的根因是「判定看到的数据 ≠ 写入看到的数据」**：
> - **判定**跑在**这个包的输入树**上（那时 `(BC)Cornucopia_Extruder` **还不存在** —— 它由**同一个包里更早
>   的一条补丁**建出来）⇒ `RefusalFor` 先合并本补丁再核对，路径被合并**造了成对象** ⇒ 判 `Converted`
>   （"条件可写入条目的 Condition 字段"）；
> - **写入**跑在**累积状态**上（前面那条补丁已生效）⇒ 真实的 `OutputRules` 是**数组**、
>   `Triggers` 也是数组 ⇒ `Descend` 拿到非 `JObject` ⇒ 拒写。
> ⇒ 这类是「**同包内前序补丁改变形状**」造成的判定/写入分歧，报告里表现为 `writeFailed`（**看得见**、
> 也没写出错东西）。要根治得让判定侧也模拟同包前序补丁（成本高、本语料只影响这 2 条），
> **本轮只纠正归因、不改代码**。

**★ `Minigames/boatJourneyMap` 这条值得记**：SVE 写的是

```json
{ "Action": "EditImage", "Target": "Minigames/boatJourneyMap",
  "FromFile": "Assets/Maps/WorldMaps/boatJourney/{{season}}_SVE_boatJourneyMap.png" }
```

而游戏**从来不加载这个名字** —— `StardewValley.Minigames/BoatJourney.cs:415`：

```csharp
return "Minigames\\" + Game1.currentSeason + "_boatJourneyMap";   // 前缀式，且没有回退到基名
```

即真正被加载的是 `Minigames/spring_boatJourneyMap` 这类**季节前缀名**（树里也只有这 4 个）。
⇒ **这条补丁在 CP 下也是空转**（EditImage 要求目标已存在；就算造出来，
`Game1.temporaryContent.Load` 也只会去取带季节前缀的那个名字）。
我们拒掉它**没有丢任何东西** —— 报告把原因写出来了，但原因说的是"找不到底图"，
真正的原因更硬：**这个名字框架根本不查**。

**两个图集发射失败也独立核过**：`Tilesheets/ChairTiles` 与 `Maps/MovieTheaterJoja_TileSheet`
在产出树**和原版树**里都被**0 张地图**引用（`grep -a` 扫全部 tbin）⇒ 写了确实是死资产 ✓。

---

## 9. 图片那三套发射器：已经救回多少、还差多少（含一条**量化后放弃**的改进）

图片（`EditImage`）是损失最大的一类，原版只有三条条件通道，我们已经各写了一套发射器。
**下表是 2026-10-11 晚按当前产出（`prof37`）重数的**（报告里三个 `*Emission` 段直接相加，
`groups` / `emitted` / `patchesCovered` / `rejected` 四个字段）：

| 发射器 | 原生机制 | 组 | 已发射 | 覆盖补丁 | 拒 |
|---|---|---|---|---|---|
| `seasonalEmission`（四季贴图组）| `Object.cs:1940-1943`：四季图排成**4 个连续索引** + `Data/BigCraftables` 条目 `Name` 含 `Seasonal` | 205 | **52** | **208** | 153 |
| `tilesheetEmission`（图集季节前缀）| 地图 `ImageSource` 的**季节前缀名**（`<season>_<图集>`，没有就回退基名）| 205 | **3** | **40** | 202 |
| `appearanceEmission`（NPC 立绘 `Appearance[]`）| `Data/Characters.<id>.Appearance[]` 的 `Condition` | 38 | **17** | **69** | 21 |
| 合计 | | | **72** | **317** | |

⇒ 图片这一路其实**已经救回 317 条（按覆盖补丁算）**，不是"全丢"。
（前两行的「组」都是 205 不是巧合：两套规划器吃的是**同一批**带条件的 `EditImage`，
各自判断自己那套机制适不适用 —— 所以组数相同、发射数不同。）

> ⚠ 本表原来写的是「196 / 52 / 208」「196 / 3 / 40」「**29 / 10 / 41**」、合计"约 290" ——
> 那一版已过期：组数 196→205、**立绘那套涨得最多**（组 29→38、发射 10→**17**、覆盖 41→**69**）。
> 数字是**快照**，重数用同一段聚合（`report.json` 的
> `seasonalEmission` / `tilesheetEmission` / `appearanceEmission`）。

### 引擎侧的真面目：`GetSeasonalTilesheetName` **没有回退**

两套机制在"缺季"时的行为**完全不同**，这决定了能不能部分发射（`GameLocation.cs:16436-16446`）：

```csharp
if (fileName.StartsWith("spring_") || … )      // 只有图集名**本身**带季节前缀才换
    sheet_path = Path.Combine(dir, current_season + fileName.Substring(fileName.IndexOf('_')));
…
catch (Exception) { tileSheet.ImageSource = imageSource; }   // 失败就退回**换名前**那个名字
```

- **图集（`tilesheetEmission`）**：名字换过去以后**没有"不存在就用基名"这一步** ——
  缺哪一季，那一季就去加载一个不存在的文件、抛错、退回**换名前**的名字，
  结果是**那一季显示另一季的图**（而且日志里一条 ERROR）。
  ⇒ 四季**必须凑齐**，`SeasonalTilesheetPlanner` 的严格是对的。
- 反例（世界地图贴图，`StardewValley.WorldMaps/MapArea.cs:186-192`）**有**回退：
  先试 `assetName + "_" + season`，`DoesAssetExist` 不成立就用基名 ⇒ 那种资源部分发射是安全的。
  **两套机制不能混为一谈。**

### ★ 量化后**放弃**的改进：给"凑不齐四季"补基图

`SeasonalSpritePlanner.PlanOne` 第一句就是 `if (bucket.Count != 4) return Reject(...)`，
而**四季机制确实只需要"那一格"**：缺的季把**基图**填进去、基图本身也正是 CP 在那些季节会留的东西
⇒ 看起来能救下一大批（SVE 报了 36 组"只有 1 条"、10 组"只有 2 条"）。

**量了一下，够不着**：全语料带 `Season` 条件的 `EditImage` 按 (包, target) 分组，
补丁数分布是 `{4条: 77 组, 1条: 14 组, 3条: 2 组, 5/7/12/32条: 各若干}`
⇒ 「补基图」能救的只有 **~20 条**（1 条与 3 条的组），而代价是给发射器加一条新路径
（要读基图、要处理**基图后来又被动过**的情形，否则合成的四格就会写错一季）。
**20 条不值得动这个机制**，判不做 —— 但留档：语义是清楚的，将来若语料里这类变多，这条路通。

⚠ 注意「这组里有一条补丁**没给 Season**」（图集那套里 50+36 组）**看着更诱人，其实更不能动**：
那说的是同一 target 上还有一条**非季节**的补丁，而图集机制按**整张文件**换名、又没有回退 ——
基图里已经带着那条非季节补丁的结果，季节变体却要按**原图坐标**去合成区域，
两边坐标系不一定一致 ⇒ 会合成出**看着对、位置错**的图。这条同样留档不做。

## 10. 收口：这一轮"看着能救"的路，**天花板都是 ~15–20 条**

把上面几节量出来的机会摆一起，结论很清楚 —— **这份语料的边际收益已经见底**：

| 想法 | 能救多少 | 判 |
|---|---|---|
| 节日 `_yN` 循环（§3）| **14** | 语义会漂（第 7 年放第 3 年内容），不做 |
| 四季贴图组「补基图」（§9）| **~20** | 要新增发射路径 + 处理"基图后来又被改过"，不做 |
| 图集那套「有补丁没给 Season」（§9）| ~86 组 | **坐标系不一定一致 ⇒ 会合成错位图**，不做 |
| ~~展开**枚举型动态令牌**~~ | **9 条 / 28 条补丁** | ⚠ **已做**（见下），不是"不做" |
| 跨包顺序延后重试（§7）| **18** | 要引跨包状态，不做 |
| 「定位不到条件落点条目」下沉到**数组元素**（新查的）| **21** | 见下，不做 |

**最后这一类**：21 条全是 `NoLandingPoint`，理由是 ``定位不到条件落点条目：`<路径>` `` ——
补丁给一个**列表元素**加/改内容（`Data/Machines.<id>.OutputRules` 10 条、`Data/Shops.<id>.Items` 3 条、
`Owners` 1 条…），而补丁自己的 `When` 要挂到那个**元素**的 `Condition` 上，
我们现在的落点查找只下钻到**路径本身**（那里是个数组，不是对象）⇒ 判"定位不到"。

机制清楚：按元素 `Id` 再下沉一层，把补丁的 GSQ **AND** 进元素的 `Condition`。**但有真 subtlety**：
① 元素自己可能带着 `Condition`（正确结果是"补丁 GSQ **且** 元素原本的条件"）；
② 补丁可能自己把 `Condition` 写成 `null`（例如 `tenthousandcats.ShaneShop` 那条）——
  那表示"元素无条件"，AND 之后就是补丁的 GSQ ✓。
**21 条 / 7286 = 0.3%**，与上面几项同一个量级 ⇒ **量化后不做，留档**。

⚠ **订正**：写完本节后把"枚举型动态令牌"那条**做了** —— 它的形状能被证明互斥
（`Season` 各一季 + 一个无条件默认块），拆开之后走的是**现成的**四季/立绘发射器，
风险与"同包其它 NPC"完全一致。实测：9 条 → 36 条，包 27 的立绘组 10→19、覆盖 28→56，
产出树 **只多 56 个文件**（7 个 NPC × 四季 × 2），别的一处没动。
⇒ `2026-10-11-seasonal-dynamic-token-enumeration.md`。

**枚举型动态令牌那条**是这样量的：全语料**被令牌拦下的补丁只有 38 条**，涉及的令牌是

```
Spouse 9（CP 内置=玩家配偶，运行时）、Target/TargetWithoutPath 9（**本来就可展开**，见下）、
Aerobics 9、Baby1/2Gender* 5、season/Season 5、SophiaMakeup 2、
GirlName/BoyName/PlayerName/PlayerTitle/SpouseTitle/WhenSpouseIsHaley/QualParsMove/
Ench_Grove_X/Y/InternalAssetKey/Day/Query 各 1、别的 mod 的命名空间令牌 6
```

其中 `{{Aerobics}}`（SVE 季节性服装的 `DynamicTokens`：`Spring` / `Summer` / `When:Season`…）
**是可以枚举展开的** —— 展开成 4 条带 `Season` 条件的补丁，再交给现成的四季/立绘发射器。
但那 9 条里 `{{Target}}` / `{{TargetWithoutPath}}` 与它**同时出现**，而这两个是**本来就能展开**的
（写入器一直会展开；判定这边 `CheckEditImageSource` 只在**展开后**仍含 `{{` 时才拒 ——
⚠ 顺带记一处**潜伏不一致**：判定与写入对这两个内置令牌的处理不一样，当前语料**没有**因此损失
（这 9 条另有 `{{Aerobics}}` 兜着），但将来若出现"只差这两个令牌"的补丁，判定会把能写的拒掉）。

⇒ 就算把枚举展开做出来，**上限 ≈15 条 / 7259（0.2%）**，而它要给令牌解析加一条"一拆多"的路径。
**不做**；留档给将来遇到同类密集语料时用。

**这一轮的结论**：剩下能拿的都拿了；再往上只有两条路 ——
①**换语料**（服装框架 / 真实 MFM 内容包，本机没有）；②**进游戏实测**（本机跑不了探针）。

---

## 汇总：确证的边界（本轮加强）

| 桶 | 条数 | 为什么落不了 | 证据强度 |
|---|---|---|---|
| 条件化地图改动 | 594 | 图块无条件槽（26 个地图属性全查过、地图路径是单个字符串）| 源码级，**确证** |
| 日程（键名/值） | 332 | 候选键写死、值里只有 GOTO/NOT friendship/MAIL | 源码级，**确证** |
| 节日 `TextOperations` | 164 | 改的不是条目 | 设计使然，**确证** |
| 新建资产带条件 | 224 | 该资产类型的条目没有条件槽 | 模型类核对，**确证**（`Unknown` 那 155 条建议措辞改硬）|
| 「结构无法自动识别」 | 61 | 同上一行的模型类核对 | **确证** |
| 节日键名维度 | 38 | `_yN` 循环能表达，但只有 14 条够得着且语义会漂 | **查清后判不实现** |
| 跨包顺序（`TargetMissing`）| 18 | manifest 声明的依赖方向与「谁造资产」成环 | 量化后**判不做**（见 §7）|
| 写入失败 | 9 | 逐条看过，全是该拒；`Minigames/boatJourneyMap` 那条**包侧目标名写错了**（见 §8）| **确证** |

**仍未确证**：事件的**脚本体内**运行时分支（那是脚本语义，静态判不了）；以及实机行为
（探针跑不了 x86 的游戏 MonoGame，见 `known-losses` §4）。

---

## 12. 2026-10-20 回看：哪些“不能转”有原版近似解

> 触发：复核 `DayTiles` / `NightTiles` 时发现 §1 的“26 个地图属性里没有一个能按条件改图块”
> **漏了这两个**。它们不是图块属性，而是**地图属性**；`GameLocation.resetForPlayerEntry`
> 在**雨天**会跳过 `DayTiles`、直接走 `NightTiles`。所以旧结论要改成：
> **条件地图改动不是“全无落点”——天气/昼夜这一档有近似，其余仍无。**
> 详情见 `../verification/2026-10-19-startup-layers-and-experience-gaps.md` §2.2。

### 12.1 可承接的“原版特性”总表

| 原判定桶 | 原版特性 | 近似做法 | 边界/代价 |
|---|---|---|---|
| 地图：`Weather` 条件 | `NightTiles` + `DayTiles` | 雨天/夜晚写目标图块，晴天白天写透明图块 | **雨天=夜晚**；晴天夜晚也会显示；只适用于“雾/雨幕”这类叠加层 |
| 地图：`Time` 条件 | `DayTiles` / `NightTiles` | 只保留“白天/夜晚”两态 | 任意时间区间（如 1900–2500）只能取最近的昼夜边界；精确区间做不到 |
| 地图：`Season` 条件 | `SeasonOverride`、`{Spring/Summer/Fall/Winter}_Objects` | 冻结一季；季节对象按季节刷 | 冻结不是切换；四季地形结构不能并存 |
| 地图：`Day`/`DayOfWeek`/`Year` | `DayTiles`/`NightTiles`（没有日期维度）、`Spawn...` | 冻结一个日期变体；或转成 `DayStarted` 触发器只做“给邮件/物品” | 原版没有“按日期换图块”的属性 |
| 地图：`HasFlag`/`HasSeenEvent` | `ConditionalDoor`、`PlayEvent <id>` 桥 | 门/传送用 GSQ 门禁；装饰/结构只能冻结一个状态 | 没有通用“条件图块层”；`PlayEvent` 会走事件演出 |
| 地图：`AddWarps` | `Warp` + `ConditionalDoor` | 无条件加 warp，再用门禁挡条件不成立 | 天气/时间条件无法门禁；悬空 warp 要跑 `check-map-warps.py` |
| 地图：`TextOperations` | 无 | 冻结成一条文本 | 纯外观 |
| 地图：野生小动物/`Load Wild ...` | 无原版 critter 数据 | 只能保留 `Load` 的静态贴图，或删掉 | 不能刷出/跟随 |
| 日程：`Season`/`Day`/`DayOfWeek`/`IsMarried` | 候选键 `<季节>_<几号>`、`<星期>`、`marriage_*` | 直接落到对应键 | 值里仍只有 `GOTO`/`NOT friendship`/`MAIL` |
| 日程：`Weather=rain` | `rain`/`rain2` 兜底键 | 无更具体的日键时可用 | 它排在 `Mon`/`spring_Mon` 之后，**不能覆盖**已有日键 |
| 日程：`Hearts`/`HasSeenEvent`/`HasFlag` | 值开头的 `MAIL <id>` 布尔跳转 | 若状态已有对应邮件可用；否则冻结一个分支 | 没邮件就只能二选一 |
| 节日：`Year` | `_yN` 循环 | 周期与 `year % N` 对齐时精确 | 只 14 条纯年份；周期不对会静默错年 |
| 节日：`Spouse` | `<NPC>_spouse` 键 | 直接落 | 只覆盖配偶维度 |
| 节日：`TextOperations`/`Fields` 带条件 | 无 | 冻结一条文本/字段 | 条件丢失 |
| 对话：`$query` / 带条件 `Load` | 无 | 冻结一个分支（选真或假） | 不能运行时切换；`fork` 只解决事件，不解决对话 |
| 图片：NPC 立绘/行走图条件 | `Data/Characters.<id>.Appearance[].Condition` | 把 CP `When` 翻成 GSQ 写进 Appearance | 已有一套发射器；非 NPC 图片不适用 |
| 图片：四季贴图 | 四季 BigCraftable 连续索引、图集 `<season>_` 前缀 | 已实现 3 套发射器（覆盖 317 条） | 必须凑齐四季；缺季会显示错季 |
| 图片：其它条件 `EditImage` | 无 | 冻结一张 | 原版只有上述三条条件换图通道 |
| `spacechase0.SpaceCore/TextureOverrides` | 基础贴图 + 第一帧 | 取动画第一帧写进底图 | 动画丢失；多帧/循环不可表达 |
| `SpaceCore/CraftingRecipeOverrides`、`CookingRecipeOverrides` | `Data/CraftingRecipes`、`CookingRecipes` | 逐字段映射 | 覆盖里没有原版字段的部分丢弃 |
| `SpaceCore/CropExtensionData` | `Data/Crops` | 映射 `RegrowDays`、产物、种子等 | 只有部分字段有槽 |
| `SpaceCore/ObjectExtensionData` | 无 | 最多把分类塞 `ContextTags`/`CustomFields` | 分类文字/颜色、触发器语义无法复现 |
| `SpaceCore/WearableData`、`FurnitureExtensionData`、`NpcExtensionData` | 无 | 不落 | 原版对应数据类没有 buff/扩展槽 |
| `BushBloom` / `CustomBush` | 无自定义灌木数据 | 把 CustomBush 当普通灌木摆到地图；BBM 不做 | 花期/掉落写死；自定义物品不可用 |
| `Data/Machines` 的 `OutputRules` 条件 | `MachineOutputTriggerRule.Condition` | 把条件 AND 到输出规则的 `Triggers[]` | 不是每个位置都有槽；需按元素下沉 |
| `Data/Shops` / `Data/Fish` / `Data/Locations.Forage` 条件 | `GenericSpawnItemDataWithCondition.Condition` | 直接写元素级 `Condition` | 补丁必须定位到**元素**，不能停在数组 |
| `Data/Festivals` / `Data/Characters` / `Data/Buildings` 等 | 19 个带 `Condition` 的模型类（见 §4） | 元素级写 GSQ | 没列进那 19 类的资产仍无条件槽 |
| `TriggerActions` 自定义触发器 | 只有 `DayEnding`/`DayStarted`/`LocationChanged`/`Manual` | 映射到四种之一；地图交互用 `PlayEvent` 桥 | `ItemEquipped`/`GiftGiven`/`MonsterKilled` 等无等价 |
| 自定义 TriggerAction 动作 | 26 个原版动作 + 事件命令 | `AddItem/AddMail/AddMoney/AddQuest/If/...`；`PlaySound` 走事件 | `OpenGuidebook`/自定义生成/伤害无等价 |
| `setDating`/`setEngaged` | 无写入路径 | 用 `XBG_Dating_<npc>`/`XBG_Engaged_<npc>` 邮件模拟后续事件条件 | 约会/送礼/求婚/婚礼/配偶房全部降级 |
| `setRaining` | `Data/LocationContexts.WeatherConditions` 只算**下一天** | D+2 生效；当天只能 `playSound rain` 伪装 | 当前场景不能改天气 |
| 事件 `screenShake`/`setActorScale`/`cycleActorColors` | 事件命令/TAS | `viewport move`+`shake`/预生成缩放染色贴图 | 近似演出，不是同一效果 |
| FTM 刷怪/刷矿/大件 | 无生成动作；只有 `Data/Monsters` 定义 | 刷怪不做；大件可尝试 `Data/Objects`+地图摆放 | 原版没有数据驱动刷怪入口 |
| `Data/Locations.CreateOnLoad.Type` 自定义类 | 无 | 删 `Type` 让地点以普通 `GameLocation` 创建 | 地点行为一起消失，可能没出口；需人工决定 |
| 自定义 SpecialOrder 板 | 默认板/`AddSpecialOrder` | 改 `OrderType` 进默认板，或 TriggerAction 直接投放 | 自定义板 UI 不再有 |

### 12.2 建议的实现顺序（按“可机械修复 + 玩家可见”）

1. **天气/时间地图层**：`NightTiles` + `DayTiles`（SVE 雾、雨幕、夜晚灯饰）；接受“雨=夜”副作用。
2. **元素级 `Condition` 落点**：`ShopItemData`、`SpawnFishData`、`SpawnForageData`、`MachineOutputTriggerRule`、`CharacterAppearanceData`——这几类能直接把 `NoLandingPoint/NeedsReview` 变成可写。
3. **日程键映射**：`Season/Day/DayOfWeek/IsMarried` 落候选键；`rain` 仅在没有更具体键时兜底。
4. **立绘/四季图片**：扩展 `Appearance[]` 与四季/图集发射器，剩余冻结。
5. **SpaceCore 配方/作物/第一帧**：有明确字段的做，没有槽的只报告。
6. **TriggerActions/事件桥**：`PlayEvent` + 原版 26 动作；自定义触发器只映射四种。
7. **明确不做**：BushBloom、野生 critter 刷出、FTM 刷怪、`setDating/setRaining` 精确复现、自定义 UI。

> 这张表是“能到哪一步”的边界，不是承诺全部实现。每一项落地前仍要按现有规矩做同参数 A/B + 报告逐条可见。

### 12.3 已落地的低风险批次（2026-10-20）

先做“便宜且不碰结构”的两类，代码里都带 `近似` / `TODO(近似)` 注释：

1. **条件化 `TextOperations` 先无条件落一条**（`PatchJudge`）
   - 只接目标键为 `Message`/`Sign`/`Text`/`DisplayName`/`Description`/`Name` 的纯文字操作；
   - 不接 `Warp`/`Music`/`Light` 等地图属性，也不接 `MapTiles`/`FromFile`/`AddWarps`；
   - 报告 reason 明写“近似：条件化的牌子文字，冻结为一条文本”；
   - 代价：条件不成立时也会显示这条文字；以后可改条件文本/DayTiles。

2. **补齐模型背书的元素级 `Condition` 落点**（`ConditionWriter`）
   - 新增：`Data/Weddings.Attendees`、`Data/Shops.Items/Owners/Dialogues`、
     `Data/Fish.Fish`、`Data/Locations.Fish/Forage/ArtifactSpots`、
     `Data/Characters.Appearance/Home`、`Data/FarmAnimals.Produce`、
     `Data/Buildings.Skins`、`Data/WorldMap.Areas`、`Data/Objects.GeodeDrops`、
     `Data/FruitTrees.Fruit`、`Data/LocationContexts.WeatherConditions/Music/ReviveLocations`、
     `Data/Machines.Triggers/LoadEffects/WorkingEffects`、`Data/GiantCrops`/`PassOutMail`/`ToolUpgrades`；
   - 这些都是 `GenericSpawnItemDataWithCondition` / 模型类里**确实有** `Condition` 的位置；
   - `Data/Weddings.Attendees` 是**字典**：按子键各建 `LandingState`，多补丁 OR，不再互相覆盖；
   - `Data/Shops.Items` **嵌套数组**：`TargetField: [<shop>, "Items"]` 的路径分隔符
     从 `.` 改成内部 `\u001F` —— 商店 ID 本身带点（`MadDog.HashtagBearFam_GunShop`），
     原来按点切会把 ID 拆碎，直接判“定位不到”；
   - 单包实跑 `Bear Family Custom NPCs`：
     - `Data/Weddings` Attendees 条件已写入，`Gus` 拿到
       `ANY "…Sigurd…" "…Gunnar…" "…Teren…"`；
     - `Data/Shops` 的 5 条嵌套 `Items` 条件从 `NoLandingPoint` 变 `Converted`，
       商品上保留原条件并 AND 新条件（例：`SEASON fall, PLAYER_HAS_MAIL Current …`）。

**未做、留给后面**：`DayTiles/NightTiles` 天气地图层、日程键映射、SpaceCore 配方/作物/第一帧、
图片 `Appearance` 扩展、TriggerActions/`PlayEvent` 桥。它们要么要改地图几何，要么要新发射器，
不属于这次“低风险文字 + 条件槽”的批次。
