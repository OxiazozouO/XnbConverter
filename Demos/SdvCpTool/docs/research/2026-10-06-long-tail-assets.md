# 长尾 Data / Strings 资产的「条件承载」精算

日期：2026-10-06
依据：`D:\sdv_src`（1.6 全量反编译）、`D:\sdv_gd`（`StardewValley.GameData.*` 数据模型，含 XML 注释）、
`D:\Stardew.Valley.v1.6.15`（只读原版 Content）、`src/SdvCpTool.Core/Assets/ConditionLanding.cs`
语料：`D:\tmp\corpus_b2\*\report.json`（32 包）+ `D:\[CP] Ridgeside Village.report.json`

> 前置：`2026-10-06-implicit-conventions.md`（名字即行为 / 索引分组）、
> `2026-10-06-key-encoded-variants.md`（键即条件词表）。

---

## 0. 一句话

**长尾资产里，真正的「原生条件位」只出现在 8 个数据模型上，且字段名全部是 GSQ；
其余 14 个类型是「字符串字典 / 列表 / 纯贴图」，条件只能靠 `$query`（少数）、键名编码（更少）
或**上移到别的资产**来承载——大多数情况下「无处承载」是对的，但「无处承载」不等于「不可转换」。**

---

## 1. 大表：资产类型 → 数据模型 → 条件位 → 最简形式 → 条数

**条数**列 = 本次实测（`corpus_b2` 32 包 + RSV 1664 条补丁，按 Target 前缀匹配统计，可复现，见 §7）。
括号内是任务书给的数——两者口径不同（任务书偏小，疑为另一版语料/只统计带条件项），**以本表口径为准**。

| # | 资产类型 | 数据模型（源码） | 原生条件位？字段名 | 是否 GSQ | 最简形式（前提） | 实测条数 |
|---|---|---|---|---|---|---|
| 1 | **`Data/Objects`** | `Dictionary<string,ObjectData>`（`DataLoader.cs:410-413`；模型 `StardewValley.GameData.Objects/ObjectData.cs`） | **条目级：无**。**嵌套级有**：`GeodeDrops[].Condition`（`ObjectGeodeDropData : GenericSpawnItemDataWithCondition`） | ✅ 仅 `GeodeDrops[].Condition`（`Utility.cs:6780`） | **加新物品**没有条件位 → 只能「无条件加、其可获得性另挂条件」（见 §3）；**改 GeodeDrops** 可直接写 `Condition` | 133（任务 33）|
| 2 | **`Strings/StringsFromMaps`** | `Dictionary<string,string>`（`Game1.content.LoadString("Strings\\StringsFromMaps:"+key)`） | **无**（无对象、无字段） | — | 值内嵌 `$query`，**且仅当该键走 `setNewDialogue` 路径**（`GameLocation.cs:9650`）；`message`/`letter` 路径（`:9616`、`:9760`）里 `$query` 是死文本 | 23（任务 ~13）|
| 3 | `Data/NPCGiftTastes` | `Dictionary<string,string>`（`DataLoader.cs:402-405`）；value = `love物品/爱文案/like物品/…` 斜杠分隔 | **无** | — | **响应段（奇数下标）内嵌 `$query` 可生效**——它走 `new Dialogue(this, null, text5)`（`NPC.cs:3832`）；键名 = NPC 名，无维度 | 120（任务 17）|
| 4 | `Data/CustomNPCExclusions` | `Dictionary<string,string>`（NPC名 → 空格分隔的排除标记）。**不是原版资产**，由 CP 自建（RSV 在 `Data/DependencyData/CustomNPCExclusions.json` 用 `EditData` 造），消费方是第三方框架 Mod | **无**（且非原版） | — | 无条件可承载。条件只能在 CP 补丁级 | 14（任务 14）|
| 5 | `Data/MoviesReactions` | `List<MovieCharacterReaction>`（`DataLoader.cs:387-390`）→ `[].{NPCName, Reactions[]}`；`MovieReaction = {Tag, Response, Whitelist[], SpecialResponses, Id}` | **无** | — | 无 GSQ。可用的「伪条件」只有 `Whitelist`（要求同场有指定 NPC，`MovieReaction.ShouldApplyToMovie`）、`Tag` 命中电影 ID/tag/`*`/`love|like|dislike` | 23（任务 10）|
| 6 | `Strings/StringsFromCSFiles` | `Dictionary<string,string>`（`LoadString`） | **无** | — | **仅 `Event.cs.1527` 这一个键**走 `new Dialogue`（`Event.cs:11574` → `:12561`），可嵌 `$query`；其余键的 `$query` 是死文本 | 34（任务 ~6）|
| 7 | `Data/Shops` | `Dictionary<string,ShopData>`（`DataLoader.cs:483-486`） | **有条目级**：`Items[].Condition`（`ShopItemData : GenericSpawnItemDataWithCondition`）、`Owners[].Condition`、`VisualTheme[].Condition`、`Owners[].Dialogues[].Condition` | ✅ 全部 GSQ（`ShopBuilder.cs:117/211` 等） | 直接写 `Condition`，原样搬运即可 | 92（任务 2）|
| 8 | `Data/PassiveFestivals` | `Dictionary<string,PassiveFestivalData>`（`DataLoader.cs:432-435`） | **有**：`Condition` | ✅ GSQ（`Utility.cs:5507`、`Game1.cs:8815`） | 直接写 `Condition` | 2（任务 2）|
| 9 | `Data/Locations` | `Dictionary<string,LocationData>`（`DataLoader.cs:322-325`）；`LocationData` 本身**无条件字段** | **顶层无**；嵌套有：`Fish[].Condition`、`Forage[].Condition`、`ArtifactSpots[].Condition`、`Music[].Condition` | ✅ 全部 GSQ（`GameLocation.cs:13977 / 15142 / 14248 / 3126`） | 想给**整张地图**加条件 → 没有落点；给刷鱼/觅食/挖掘/音乐加条件 → 直接写 `Condition` | 68（任务 1–5）|
| 10 | `Data/Mail` | `Dictionary<string,string>`（`DataLoader.cs:336-339`）；值 = 信件正文 + `%item`/`%action` 等命令（`LetterViewerMenu.cs:296-315`） | **无字段级条件**；**但有键名条件**：键 `<season>_<day>[_<year>]` 会被当日自动投递（`Game1.cs:8296-8302`） | ❌ 非 GSQ（是日期键） | 把条件编进键名（仅季节/日/年三维）；`%item` 等命令里**没有** GSQ 命令 | 10（任务 1–5）|
| 11 | `Data/Fish` | `Dictionary<string,string>`（`DataLoader.cs:225-228`）；键 = 鱼 item ID，值 = 斜杠字段（难度/运动等） | **无** | — | 无条件可承载。**要按季节挑鱼请改 `Data/Locations.<loc>.Fish[]`（有 `Season` + `Condition`）** | 9（任务 1–5）|
| 12 | `Data/Weddings` | `WeddingData = {EventScript: Dictionary<string,string>, Attendees: Dictionary<string,WeddingAttendeeData>}`（`DataLoader.cs:534-537`） | **有**：`Attendees[].Condition` | ✅ GSQ（`Utility.cs:2269`） | 直接写 `Condition`。`EventScript` 是纯脚本字典，无落点 | 19（任务 1–5）|
| 13 | `Data/ExtraDialogue` | `Dictionary<string,string>`（`DataLoader.cs:195-198`；**不在 DataLoader 之外另有模型**） | **无字段** | — | **值内嵌 `$query` 可生效**——关键路径走 `new Dialogue(..., "Data\\ExtraDialogue:"+k)`（`GameLocation.cs:8483/8487/8491/10000`、`NPC.cs:1841`、`Game1.cs:9596-9599`） | 11（任务 1–5）|
| 14 | `Data/EngagementDialogue` | `Dictionary<string,string>`（`DataLoader.cs:195-198`） | **无字段** | — | **值内嵌 `$query` 可生效**——`NPC.cs:1660/1681/3848/3852` 都是 `new Dialogue(this, "Data\\EngagementDialogue:"+…)` | 9（任务 1–5）|
| 15 | `Data/ConcessionTastes` | `List<ConcessionTaste>`（`DataLoader.cs:157-160`）→ `{Name(=Id), LovedTags[], LikedTags[], DislikedTags[]}` | **无** | — | 无条件。`Name` 支持 `"*"` 通配（`ConcessionTaste.cs` 注释） | 14（任务 1–5）|
| 16 | `Strings/SpecialOrderStrings` | `Dictionary<string,string>`（`LoadString("Strings\\SpecialOrderStrings:"+text)`，`SpecialOrder.cs:650`） | **无** | — | 值被 `[key]` 替换进订单文本，**不经过 `new Dialogue`** → `$query` 不生效。无落点 | 17（任务 1–5）|
| 17 | `Strings/animationDescriptions` | `Dictionary<string,string>`（`Strings` 目录下的独立资产） | **无** | — | **运行期零引用**（`grep -rn "animationDescriptions" D:\sdv_src` 只命中 `Data/animationDescriptions` 与测试文件 `SyntaxAbstractor.cs:29`）。无落点 | 4（任务 1–5）|
| 18 | `Data/animationDescriptions` | `Dictionary<string,string>`（`DataLoader.cs:77-80`）；键 = 动画名，值 = `intro/loop/outro` 帧串 | **无字段**；**键名有条件维度**：`_sleep`、`_beach_dance` 等（`NPC.cs:4559/5631`、`IslandSouth.cs:105`） | ❌ 非 GSQ（代码硬编码选键） | 条件编进键名；无通用落点 | 42（另计）|
| 19 | `Tilesheets/*` | `Texture2D`（纯贴图） | **无**（不是数据资产） | — | 无。条件补丁 → 无法烧进 XNB | 50（任务 ~12）|
| 20 | `LooseSprites/*` | `Texture2D`（纯贴图） | **无** | — | 无 | 153（任务 ~12）|
| 21 | `Characters/Dialogue/*` | `Dictionary<string,string>`（`NPC.cs:449`、`:4730` 拼 `"Characters\\Dialogue\\"+sheet`） | **无字段**；**键名条件丰富**（`_spouse`、`_male/_female`、季节前缀、`_yN`…，见 key-encoded 文档） | — | **值内嵌 `$query` 可生效**（这是 `ConditionLanding` 已认的宿主） | 1030（任务 ~3）|
| 22 | `CharacterFiles/Dialogue/*` | **原版源码零引用**（`grep -rn "CharacterFiles" D:\sdv_src` 无结果；`D:\sdv_gd` 亦无） | **无** | — | **疑似 1.3 及以前的历史路径**，1.6 只加载 `Characters\Dialogue\<NPC>`（`NPC.cs:449,4730`）。SVE 仍在 Target 它 → 引擎侧应是死补丁。**「CP 是否有兼容别名」未确证** | 1（任务 ~3）|

---

## 2. ★ 同义条件字段名 → 是否 GSQ：确证清单

**《14 个不同条件字段名》= 1 个字面 `Condition` + 13 个同义名。逐个核对如下。**

| # | 字段名 | 定义处（`D:\sdv_gd`） | 消费点（`D:\sdv_src`） | 是否 GSQ |
|---|---|---|---|---|
| 0 | `Condition` | 30 处（见 §2.1） | 多处 | ✅ **是** |
| 1 | `TriggerCondition` | `GameData/IncomingPhoneCallData.cs:12` | `DefaultPhoneHandler.cs:45` `GameStateQuery.CheckConditions(item.Value.TriggerCondition, …)` | ✅ **是** |
| 2 | `RingCondition` | `GameData/IncomingPhoneCallData.cs:17` | `DefaultPhoneHandler.cs:65` `GameStateQuery.CheckConditions(call.RingCondition, …)` | ✅ **是** |
| 3 | `UnlockCondition` | `GameData.FarmAnimals/FarmAnimalData.cs:57`；`GameData.Minecarts/MinecartNetworkData.cs:11` | `Utility.cs:4945`（动物）；`GameLocation.cs:10225` `CheckConditions(network.UnlockCondition, this)`（矿车） | ✅ **是** |
| 4 | `UnlockConditions` | `GameData.Characters/CharacterData.cs:173` | `Event.cs:5514`、`Game1.cs:7322`、`Utility.cs:2269` 全走 `CheckConditions` | ✅ **是** |
| 5 | `BuildCondition` | `GameData.Buildings/BuildingData.cs:87` | `CarpenterMenu.cs:271` `CheckConditions(buildingDatum.Value.BuildCondition, targetLocation)` | ✅ **是** |
| 6 | `BuildConditions` | **地图属性**（非 C# 字段）：`GameLocation.cs:2469` `getMapProperty("BuildConditions")` | `GameLocation.cs:2470` `CheckConditions(mapProperty, this)` | ✅ **是** |
| 7 | `SkipPermanentlyCondition` | `GameData/TriggerActionData.cs:22` | `TriggerActionManager.cs:587` `CheckConditions(data.SkipPermanentlyCondition)` | ✅ **是** |
| 8 | `DefaultMusicCondition` | `GameData.LocationContexts/LocationContextData.cs:20` | `GameLocation.cs:3246`、`Game1.cs:4585` `CheckConditions(context.DefaultMusicCondition)` | ✅ **是** |
| 9 | `InvalidItemMessageCondition` | `GameData.Machines/MachineData.cs:51`；`GameData.Buildings/BuildingChest.cs:25` | `Object.cs:2464`（机器）、`Building.cs:768`（建筑箱）`CheckConditions(…InvalidItemMessageCondition, …)` | ✅ **是** |
| 10 | `ClearContentsOvernightCondition` | `GameData.Machines/MachineData.cs:110` | `Object.cs:1758` `CheckConditions(machineData.ClearContentsOvernightCondition, …)` | ✅ **是** |
| 11 | `KnownCondition` | `GameData.WorldMaps/WorldMapTooltipData.cs:18` | `MapArea.cs:111` `CheckConditions(tooltip.KnownCondition)` | ✅ **是** |
| 12 | `UnlockedCondition` | `GameData.Powers/PowersData.cs:24` | `PowersTab.cs:76` `CheckConditions(item.Value.UnlockedCondition)` | ✅ **是** |
| 13 | `IgnoreUnlockConditions` | `GameData.Weddings/WeddingAttendeeData.cs:24`（bool）；同名 bool 见 `CharacterData` 家族 | **不是查询**——它是「跳过 `UnlockConditions` 检查」的布尔开关（`Utility.cs:2269` `value2.IgnoreUnlockConditions || …CheckConditions(data.UnlockConditions…)`） | ❌ **不是 GSQ** |

**结论：13 个同义名里 12 个确证是 GSQ，唯一不是的是 `IgnoreUnlockConditions`（布尔旁路开关）。**

> 注意本清单比任务书举的 7 个多出 5 个：`UnlockConditions`（复数）、`BuildConditions`（复数/地图属性）、
> `ClearContentsOvernightCondition`、`KnownCondition`、`UnlockedCondition`。**它们同样是 GSQ。**

### 2.1 字面 `Condition` 的定义处（30 处，GSQ 全覆盖）

`BuildingSkin`、`CharacterAppearanceData`、`CharacterHomeData`、`AlternatePurchaseAnimals`、
`FishPondWaterColor`、`GiantCropData`、`PassOutMailData`、`ReviveLocation`、`WeatherCondition`、
`MachineEffects`、`MinecartDestinationData`、`ShopDialogueData`、`ShopOwnerData`、`ShopThemeData`、
`ToolUpgradeData`、`WeddingAttendeeData`、`WildTreeTextureData`、`WorldMapAreaData`、
`WorldMapTextureData`、`WorldMapTooltipData`、`PassiveFestivalData`、`PlantableRule`、
`QuantityModifier`、`TemporaryAnimatedSpriteDefinition`、`TriggerActionData`、
`GenericSpawnItemDataWithCondition`（之下再派生 `ShopItemData` / `SpawnFishData` / `SpawnForageData` /
`ArtifactSpotDropData` / `ObjectGeodeDropData`）等。

> 本仓库无需逐个列消费点：`Condition` 语义已被 `ConditionLanding.HasConditionFieldAnywhere`
> （`:124-136`）覆盖，**只要有消费点都走 `GameStateQuery.CheckConditions`**（39 个文件，见 §6）。

---

## 3. ★ 深挖一：`Data/Objects`

### 3.1 数据模型

`Dictionary<string /* itemID */, ObjectData>`（`DataLoader.cs:410-413`）。
`ObjectData` 共 **22 个字段**（`D:\sdv_gd\StardewValley.GameData.Objects\ObjectData.cs` 全文核实）：
`Name / DisplayName / Description / Type / Category / Price / Texture / SpriteIndex /
ColorOverlayFromNextIndex / Edibility / IsDrink / Buffs / GeodeDropsDefaultItems / GeodeDrops /
ArtifactSpotChances / CanBeGivenAsGift / CanBeTrashed / ExcludeFromFishingCollection /
ExcludeFromShippingCollection / ExcludeFromRandomSale / ContextTags / CustomFields`。

### 3.2 有没有原生条件位？

**条目级：没有。** 整个 `ObjectData` 类里 **一个 `Condition` 都没有**（全文核实）。

**嵌套级：有一个。** `GeodeDrops[].Condition`
——`ObjectGeodeDropData : GenericSpawnItemDataWithCondition`（`ObjectGeodeDropData.cs:7`），
消费于 `Utility.cs:6780`：

```csharp
if (!random.NextBool(drop.Chance) || (drop.Condition != null && !GameStateQuery.CheckConditions(drop.Condition, null, null, null, null, random)))
    continue;
```

✅ **是 GSQ**。同级的 `Buffs[]`（`ObjectBuffData`）**没有**条件字段。

### 3.3 值里有没有可承载条件的机制？

| 机制 | 是否可用 | 依据 |
|---|---|---|
| `Condition` 字段 | ❌ 不存在 | 全文核实 |
| `$query`（值内联 GSQ） | ❌ **不生效**。`DisplayName`/`Description` 走 `TokenParser.ParseText`（见 `NPC.cs:4865` 同构用法），**不是** `Dialogue.parseDialogueString` | `TokenParser` vs `Dialogue.cs:588` |
| `ContextTags` + GSQ `ITEM_CONTEXT_TAG` | ⚠️ **可以间接表达**：把「本物品属于哪个池子」写进 tag，再由**别的**资产用 GSQ 命中 | 机制在 `ItemContextTagManager.cs`，但 this 是**反方向**（tag 被查询，不是物品被条件化）|
| `Texture` 指向季节图集 | ⚠️ 仅对 `BigCraftable` + 名字含 `Seasonal` 生效（`Object.cs:1940`，见 implicit-conventions §1）——**`Data/Objects` 非 bigCraftable 分支不适用** | `Object.cs:1940` |

### 3.4 实测：mod 到底怎么用条件？

语料里 `Data/Objects` 的带 `When` 补丁**几乎全是「加新条目」**，且条件都是 **CP 配置项 / HasMod**：

```jsonc
// 内容拓展---聚宝盆 作物/data/objects.json
{ "Action": "EditData", "Target": "Data/Objects",
  "When": { "Enable Herbs Pack": true },
  "Entries": { "Cornucopia_Aloe": { … } } }

// 内容拓展---聚宝盆 制作 [CP]/data/objects.json
{ "Action": "EditData", "Target": "Data/Objects",
  "When": { "HasMod|contains=Cornucopia.MoreCrops": true },
  "Entries": { "Cornucopia_AvocadoMayonnaise": { … } } }
```

**这两类条件都是「编译期常量」**：配置项在转换时已知，`HasMod` 在转换时已知。
→ **可以直接烧**（取默认配置、或按用户配置定死）。

真正的运行时 GSQ（`Season`/`HasFlag`…）加新物品**没有落点**。

### 3.5 最简形式（前提）

1. **条件 = 配置项/HasMod** → 转换时求值，无条件写入 `Data/Objects`。**前提：能读到 mod 的 config 默认值。**
2. **条件 = 运行时 GSQ，且物品是「从某处获得」** → **拆成两步**：
   - 物品**无条件**进 `Data/Objects`；
   - 获得渠道挂条件：商店挂 `Data/Shops.<shop>.Items[].Condition`、
     野生产出挂 `Data/Locations.<loc>.Forage[].Condition/Season`、
     钓鱼挂 `Fish[].Condition`、礼物挂 `Data/NPCGiftTastes`（无落点，此路不通）。
   - **前提：该物品本来就是靠这些渠道获得的**；若靠事件/邮件，落到 `Data/TriggerActions[].Condition`。
3. **条件只影响被动掉落（砸晶球）** → 直接写 `GeodeDrops[].Condition`（唯一原生落点）。

---

## 4. ★ 深挖二：`Strings/StringsFromMaps`

### 4.1 数据模型

`Dictionary<string /* 键 */, string /* 文本 */>`，通过
`Game1.content.LoadString("Strings\\StringsFromMaps:" + key)` 读取。
**没有 C# 模型类、没有专门加载器**——`DataLoader.cs` 里**没有** `StringsFromMaps` 条目，
它是靠 `LoadString` 的 `assetName:key` 语法现查的通用 `Strings` 资产。

值里能用的是**对话语法**：`SyntaxAbstractor.cs:42` 把它登记为 `DialogueSyntaxHandler`
（与 `Characters/Dialogue/*`、`Strings/StringsFromCSFiles` 同一处理器）。
即 `%season`、`%spouse`、`${…}$` 性别块、`#` 分页、`@` 玩家名、`$h/$s/$l` 表情等**全部可用**。

### 4.2 有没有原生条件位？没有。

**没有条件位，也没有键名条件维度。**

### 4.3 值里能不能承载条件？——**取决于「谁引用这个键」，这是本类型的全部难点**

`StringsFromMaps` 的键由**地图 action** 引用，共三条路径，`$query` 命运不同：

| 地图 action | 读取点 | 是否 `new Dialogue` | `$query` |
|---|---|---|---|
| `message <key>` | `GameLocation.cs:9616` `Game1.drawDialogueNoTyping(LoadString(...))` | ❌ 直接进 `DialogueBox(string)` | **死文本** |
| `NPCSpeechMessageNoRadius <npc> <key>` | `GameLocation.cs:9650` `nPC2.setNewDialogue("Strings\\StringsFromMaps:"+key, add:true)` → `NPC.cs:4696-4698` `new Dialogue(this, translationKey)` | ✅ | **生效** |
| `letter <key>` | `GameLocation.cs:9760` `Game1.drawLetterMessage(LoadString(...))` | ❌ | **死文本** |
| 事件脚本 `Strings\StringsFromMaps:<key>` | `Event.cs:12479` `Game1.drawObjectDialogue(LoadString(...))` | ❌ | **死文本** |

**结论：`Strings/StringsFromMaps` 的条件承载力 = 「同一个键，只在 `NPCSpeechMessageNoRadius` 引用下可嵌 `$query`」。**
它**不是一个整类可用的宿主**，而是一个**逐键判定**的宿主。

### 4.4 最简形式（前提）

1. **键只被 `NPCSpeechMessageNoRadius` 引用** → 把 CP `When` 翻译成值内的
   `$query <GSQ>#<真文本>|<假文本>`（`Dialogue.cs:646-652` 的语法）。
   **前提：必须确认所有引用点都走 `setNewDialogue`。**
2. **键被 `message`/`letter`/事件引用** → **无处承载**。退路：
   - 若条件能编进**键名**（如季节前缀）→ 改行动态选键（**但地图 action 的键是字面量，无此机制**）；
   - 否则只能「两个键都保留 + 让地图按条件开关」，落到 `Data/Locations.<loc>` / 地图 patch，**超出本类型**。

### 4.5 语料实测

23 条补丁里 11 条 `NeedsReview`——**正好是工具自己发现了「是字符串字典但键维度一个都没认出来」**
（`ConditionLanding.cs:111-115` 的 `Unknown` 分支）。这与本节的结论一致：**它确实没有键维度。**

---

## 5. `$query` 宿主：`ConditionLanding` 只认 4 个，是漏的

`ConditionLanding.ClassifyDialogueQueryHost`（`:39-59`）只认
`Characters/Dialogue/`、`Strings/schedules/`、`Data/Festivals/`、`Strings/StringsFromCSFiles`。
**按「值是否经过 `new Dialogue(...)`」逐条核对，实际宿主至少多 4 个：**

| 资产 | 证据（源码） | 是否在现宿主表 |
|---|---|---|
| `Data/NPCGiftTastes`（响应段） | `NPC.cs:3832` `stack.Push(new Dialogue(this, null, text5))`，`text5` 来自 gift taste 值的奇数下标 | ❌ **缺** |
| `Data/ExtraDialogue` | `GameLocation.cs:8483/8487/8491/10000`、`NPC.cs:1841`、`Game1.cs:9596-9599`（`DrawDialogue(npc,key)` → `new Dialogue(npc,key)`） | ❌ **缺** |
| `Data/EngagementDialogue` | `NPC.cs:1660 / 1681 / 3848 / 3852` | ❌ **缺** |
| `Strings/StringsFromMaps`（仅 `NPCSpeechMessageNoRadius` 路径） | `GameLocation.cs:9650` → `NPC.cs:4696-4698` | ❌ **缺**（且是**逐键**条件宿主） |

反向也要核对：**`ConditionLanding` 注释里说「`DialogueBox(string)` 路径里 `$query` 是死文本」是正确的**
——`Game1.drawDialogueNoTyping(string)` / `drawLetterMessage(string)` / `drawObjectDialogue(string)`
都直接构造 `DialogueBox(string)`，不做 `parseDialogueString`。
**所以上面 4 个新增宿主必须按「引用点」而不是「资产名」判定**，这一点与现有 4 个宿主（整资产可用）性质不同。

---

## 6. 对 `ConditionLanding.cs` 的两条修正

### 6.1 `:129` 只匹配字面属性名 `"Condition"` → **会漏掉 12 个 GSQ 字段**

```csharp
case JObject o:
    if (o.Property("Condition") != null) return true;   // ← 只认这一个
```

**实证影响**：以下资产**有** GSQ 条件位，但工具会判 `Unknown`/`NoLandingPoint`（危险方向）：

| 资产 | 被漏掉的字段 | 语料佐证 |
|---|---|---|
| `Data/PassiveFestivals.<id>` | `Condition`（**能命中，无影响**） | 2 条 `Converted` |
| `Data/Buildings.<id>` | `BuildCondition` | 未在本次语料 |
| `Data/Locations.<loc>.Fish[]` | `Condition`（命中） | — |
| `Data/Machines.<id>` | `InvalidItemMessageCondition`、`ClearContentsOvernightCondition` | — |
| `Data/WorldMap.*` 工具提示 | `KnownCondition` | — |
| `Data/Powers.<id>` | `UnlockedCondition` | — |
| `Data/IncomingPhoneCalls.<id>` | `TriggerCondition`、`RingCondition` | — |
| `Data/FarmAnimals.<id>` | `UnlockCondition` | — |
| `Data/TriggerActions[]` | `SkipPermanentlyCondition` | — |
| `Data/Minecarts.<id>` | `UnlockCondition` | — |
| `Data/Characters.<npc>` | `UnlockConditions` | — |
| 地图 `.tbin` 属性 | `BuildConditions`（**注意：不是 JSON，本探测走不到**） | — |

> **建议**：把 `HasConditionFieldAnywhere` 的匹配从字面 `"Condition"` 扩成
> **「以 `Condition` 结尾的字符串属性名」**（`p.Name.EndsWith("Condition") && p.Value.Type == String`），
> 一次性覆盖表中 12 个。**唯一的假阳性是 `IgnoreUnlockConditions`（布尔）**——
> 用 `Value.Type == String` 即可排除。

### 6.2 `ClassifyDialogueQueryHost` 的 4 宿主清单 → 至少 8 个（且新增 4 个需逐键判定）

见 §5。**建议**：新增的 4 个不要混进「整资产可嵌 `$query`」的枚举值里，
单开一个 `PerKey` 语义（如 `DialogueQueryHost.AssetWide` / `DialogueQueryHost.PerReference`），
否则会把 `StringsFromMaps` 的 `message` 引用误判成可嵌。

---

## 7. 附录：条数与复现

**统计口径**：按 `Target` 做前缀匹配（`Data/Shops` 命中 `Data/Shops` 及 `Data/Shops/...`），
数据源 = `D:\tmp\corpus_b2\*\report.json`（32 包）+ `D:\[CP] Ridgeside Village.report.json`。

| 资产 | corpus_b2 | RSV | 合计 | verdict 分布 |
|---|---|---|---|---|
| `Data/Objects` | 131 | 2 | **133** | ConvertedNoCondition 96 / Converted 28 / NeedsRuntime 5 / NotApplicable 4 |
| `Strings/StringsFromMaps` | 20 | 3 | **23** | ConvertedNoCondition 10 / NeedsReview 11 / NotApplicable 2 |
| `Data/NPCGiftTastes` | 112 | 8 | **120** | ConvertedNoCondition 102 / NeedsReview 16 / NotApplicable 2 |
| `Data/CustomNPCExclusions` | 0 | 14 | **14** | NeedsReview 11 / NotApplicable 2 / CreatesNewAsset 1 |
| `Data/MoviesReactions` | 22 | 1 | **23** | ConvertedNoCondition 14 / NoLandingPoint 6 / NotApplicable 3 |
| `Strings/StringsFromCSFiles` | 17 | 17 | **34** | Converted 14 / NeedsReview 11 / ConvertedNoCondition 8 / NotApplicable 1 |
| `Data/Shops` | 90 | 2 | **92** | ConvertedNoCondition 88 / NeedsReview 4 |
| `Data/PassiveFestivals` | 2 | 0 | **2** | **Converted 2**（唯一 100% 有原生条件位） |
| `Data/Locations` | 66 | 2 | **68** | ConvertedNoCondition 61 / NotApplicable 6 / Converted 1 |
| `Data/Mail` | 8 | 2 | **10** | ConvertedNoCondition 8 / NotApplicable 1 / NeedsReview 1 |
| `Data/Fish` | 7 | 2 | **9** | NeedsReview 6 / ConvertedNoCondition 3 |
| `Data/Weddings` | 2 | 17 | **19** | **Converted 18** / ConvertedNoCondition 1 |
| `Data/ExtraDialogue` | 10 | 1 | **11** | ConvertedNoCondition 8 / NeedsReview 3 |
| `Data/EngagementDialogue` | 9 | 0 | **9** | ConvertedNoCondition 8 / NeedsReview 1 |
| `Data/ConcessionTastes` | 13 | 1 | **14** | ConvertedNoCondition 13 / NoLandingPoint 1 |
| `Strings/SpecialOrderStrings` | 16 | 1 | **17** | ConvertedNoCondition 16 / NeedsReview 1 |
| `Strings/animationDescriptions` | 2 | 2 | **4** | ConvertedNoCondition 2 / NeedsReview 1 / NotApplicable 1 |
| `Data/animationDescriptions` | 41 | 1 | **42** | ConvertedNoCondition 36 / NeedsReview 5 / NotApplicable 1 |
| `Tilesheets/*` | 49 | 1 | **50** | NoLandingPoint 19 / NotApplicable 17 / Converted 11 / ConvertedNoCondition 2 / NeedsReview 1 |
| `LooseSprites/*` | 133 | 20 | **153** | NoLandingPoint 65 / NotApplicable 47 / Converted 17 / TargetMissing 14 / ConvertedNoCondition 10 |
| `Characters/Dialogue/*` | ~1027 | ~3 | **1030** | （主战场，另文） |
| `CharacterFiles/Dialogue/*` | 1 | 0 | **1** | NotApplicable 1 |

**关键自查（两条）**

1. `Tilesheets/*` / `LooseSprites/*` 的 `Converted 11 / 17` 是**误报方向**——
   这些是纯贴图，`Converted` 只表示「像素被写进 XNB」，**不代表条件被承载**。
   `NoLandingPoint 19+65` 才是对这些资产带条件 `EditImage` 的正确判决。
2. `Strings/StringsFromMaps` 11/23 是 `NeedsReview`、`Data/Fish` 6/9 是 `NeedsReview`、
   `Data/CustomNPCExclusions` 11/14 是 `NeedsReview`——**这三个都是「字符串字典 + 键维度未识别」**
   命中了 `ConditionLanding.cs:111-115`。本文件 §4 证明：**它们确实没有键维度，`NeedsReview` 是对的**。

---

## 8. 给转换器的落地清单

1. **`HasConditionFieldAnywhere` 扩成「属性名以 `Condition` 结尾且值为字符串」**（§6.1）。
   → 直接解锁 `BuildCondition` / `UnlockCondition(s)` / `TriggerCondition` / `RingCondition` /
   `SkipPermanentlyCondition` / `DefaultMusicCondition` / `InvalidItemMessageCondition` /
   `ClearContentsOvernightCondition` / `KnownCondition` / `UnlockedCondition` 共 12 个。
2. **`$query` 宿主按「引用点」分档**（§5/§6.2）：整资产可嵌 4 个 + 逐键可嵌 3 个。
3. **`Data/Objects` 的条件走「拆解」而不是「承载」**（§3.5）。
4. **`Data/Mail` 的条件走键名 `<season>_<day>[_<year>]`**（`Game1.cs:8296-8302`），
   其余维度无处承载。
5. **`Data/Fish` 要按季节挑鱼 → 改嫁到 `Data/Locations.<loc>.Fish[]`**（有 `Season` + `Condition`）。
6. **`Data/PassiveFestivals` / `Data/Weddings.Attendees` / `Data/Shops.Items` 是三个「原生条件位」直通车**，原样搬运即可。
