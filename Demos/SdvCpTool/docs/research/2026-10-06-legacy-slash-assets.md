# 旧式 `/` 分隔 Data 资产普查（1.6.15）

日期：2026-10-06
依据：`D:\sdv_src`（1.6 反编译源码）、`D:\sdv_tree\Data`（1.6.15 解包 Content，只读）、
`D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods`（CP 2.9.1 本体 + RSV）、
`D:\Steam\steamapps\common\Stardew Valley\Mods`（语料 B，32 个 CP 包）

> 前置：`2026-10-06-implicit-conventions.md`（名字即行为 / 索引分组）。
> 本文件是它的补集：**语义藏在「第 N 个 `/` 之间」**的那一类资产。

---

## 0. 方法与口径

- 清单用**实测**得出，不照抄 1.5 资料：对 `D:\sdv_tree\Data\**` 全量解析，
  把「值是字符串且含 `/`」的资产挑出来；再逐个人工核对源码读取点。
- 「字段数」是**实测值域**（`value.Split('/').Length` 的分布），
  **不是**「规范长度」——原版有大量「尾部字段留空/省略」的行。
- 每条结论后附 `源码文件:行号`，都在 `D:\sdv_src` 下。
  查不到的写「**未确证**」，不猜。

---

## 1. 清单：1.6.15 里**仍然**用 `/` 分隔字符串的 `Data/*`

### 1.1 A 类：值 = `/` 分隔记录（本任务的目标）

| 资产 | 条目 | 实测字段数 | 定长？ | 关键源码依据 |
|---|---|---|---|---|
| `Data/Furniture` | 645 | 8 / 10 / 11 / 12 | **否**（尾部可选） | `StardewValley.ItemTypeDefinitions/FurnitureDataDefinition.cs:84` |
| `Data/hats` | 122 | 6 / 7 | **否** | `ItemTypeDefinitions/HatDataDefinition.cs:83` |
| `Data/Boots` | 18 | 7（另有 3 个可选尾字段） | **否** | `ItemTypeDefinitions/BootsDataDefinition.cs:83` |
| `Data/Monsters` | 51 | 15 | **是** | `StardewValley.Monsters/Monster.cs:426` |
| `Data/CraftingRecipes` | 150 | 6 | **是** | `StardewValley/CraftingRecipe.cs:106` |
| `Data/CookingRecipes` | 81 | 5 | **是** | `StardewValley/CraftingRecipe.cs:106` |
| `Data/Fish` | 74 | **14**（普通鱼）/ **8**（蟹笼鱼） | **否**（两种形态） | `StardewValley/GameLocation.cs:14044`、`StardewValley.Objects/CrabPot.cs:402` |
| `Data/Quests` | 66 | 9 / 10 | **否** | `StardewValley.Quests/Quest.cs:279` |
| `Data/Bundles` | 31 | 值 7 段 + **键 2 段** | 是（值是） | `StardewValley.Menus/Bundle.cs:37,103` |
| `Data/ChairTiles` | 60 | 值 7 段（第 8 可选）+ **键 3 段** | 否 | `StardewValley/MapSeat.cs:72` |
| `Data/animationDescriptions` | 105 | 3 / 4 / 5 / 6 | **否** | `StardewValley/NPC.cs:4667` |
| `Data/NPCGiftTastes` | 39 | **11**（NPC）/ **1**（Universal_*） | **否**（两种形态） | `StardewValley/NPC.cs:1499` |
| `Data/HairData` | 23 | 6 | 是（尾可省） | `StardewValley/Farmer.cs:5276` |
| `Data/AquariumFish` | 72 | 2 / 5 / 6 / 8 | **否** | `StardewValley.Objects/TankFish.cs:108` |
| `Data/PaintData` | 12 | 偶数（成对） | **否** | `StardewValley.Menus/BuildingPaintMenu.cs:876` |
| `Data/TV/CookingChannel` | 32 | 2 | 是 | `StardewValley.Objects/TV.cs:553-558` |
| `Data/Events/*`（45 个文件） | — | **键 1~6 段**；值 = 命令脚本 | **否** | `StardewValley/Event.cs:11720,11728` |
| `Data/Festivals/<season><day>`（8 个文件，另加无分隔符的 `FestivalDates`） | — | 部分键是脚本；`conditions` 是 `地点/时间` | **否** | `StardewValley/Game1.cs:9958` |

**⚑ 关键更正（1.5 资料会带错路）：**

| 任务里给的起点 | 1.6.15 实况 |
|---|---|
| `Data/ObjectInformation` | **已不存在**（1.6 并入 `Data/Objects`，且是 JSON 对象） |
| `Data/BigCraftables` | **已是 JSON 对象**（`BigCraftableDataDefinition.cs`，不是 `/`） |
| `Data/Fish`（旧） | **仍是 `/`** ✅，但 1.6 新增了 `Data/Locations[*].Fish`（对象数组）作为生成规则 |
| `Data/CraftingRecipes` / `Data/CookingRecipes` | **仍是 `/`** ✅ |
| `Data/Furniture` | **仍是 `/`** ✅ |
| `Data/Objects`、`Data/Weapons`、`Data/Tools`、`Data/Shirts`、`Data/Pants`、`Data/Crops`、`Data/Buildings`、`Data/Machines`、`Data/Shops`、`Data/FruitTrees`、`Data/Fences`、`Data/FloorsAndPaths`、`Data/GiantCrops`、`Data/HomeRenovations`、`Data/Locations`、`Data/SpecialOrders`、`Data/Powers`、`Data/Pets`、`Data/Trinkets`、`Data/WildTrees` | **全是 JSON 对象/数组** |

### 1.2 B 类：同族但**分隔符不是 `/`**（极易误判）

| 资产 | 分隔符 | 依据 |
|---|---|---|
| `Data/Achievements` | **`^`**（5 段：名`^`描述`^`bool`^`前置`^`精灵号） | `StardewValley.Tests/SyntaxAbstractor.cs:60`；实测 39/39 含 `^` |
| `Data/mail`、`Data/SecretNotes` | `%item … %%` 命令 + `^` 换行 + `[#]` 标题 | `SyntaxAbstractor.cs:68-70`；实测 0/179、0/38 含 `/` |
| `Data/Festivals/FestivalDates` | 无分隔符 | 实测 8/8 无 `/` |
| `Data/ExtraDialogue`、`Data/EngagementDialogue` | 纯对白（9/147 含 `/` 是对白文本本身） | `SyntaxAbstractor.cs:29-42` |

---

## 2. 字段位次表（位次 → 语义 + 源码依据）

### 2.1 `Data/Furniture`

（`FurnitureDataDefinition.cs:43,84,92`；`StardewValley.Objects/Furniture.cs:244-251,315-321,356-367`）

| # | 语义 | 依据 |
|---|---|---|
| 0 | 内部名 `InternalName` | `FurnitureDataDefinition.cs:43` |
| 1 | 类型名（`chair`/`bed double`/`decor`…）→ 类型号 | `Furniture.cs:315`（`getTypeNumberFromName`） |
| 2 | **精灵尺寸 `w h`**（16px 格）；`-1` → 按类型默认 | `Furniture.cs:244-251` |
| 3 | **包围盒 `w h`**；`-1` → 按类型默认 | `Furniture.cs:356-367` |
| 4 | 旋转次数 | `Furniture.cs:320` |
| 5 | 价格 | `Furniture.cs:321` |
| 6 | **未确证**（1.6 源码无读取点；原版全为 `-1`） | — |
| 7 | 显示名（`[LocalizedText …]`，可含令牌） | `FurnitureDataDefinition.cs:43`（`TokenParser.ParseText`） |
| 8 | 精灵索引；空/非整数 → 回退到**数字 ID** | `FurnitureDataDefinition.cs:92-101` |
| 9 | 贴图资产名，默认 `TileSheets\furniture` | `FurnitureDataDefinition.cs:43` |
| 10 | `ExcludeFromRandomSale` bool | `FurnitureDataDefinition.cs:43` → `ParsedItemData.cs:72` |
| 11 | **ContextTags**（空格分隔） | `StardewValley/ItemContextTagManager.cs:56-57` |

**能再嵌令牌/条件的位次**：7（`TokenParser.ParseText`）、11（ContextTag 供 GSQ `ITEM_CONTEXT_TAG`）。

### 2.2 `Data/hats`

（`HatDataDefinition.cs:42,83`；`StardewValley.Objects/Hat.cs:155-184`）

| # | 语义 | 依据 |
|---|---|---|
| 0 | 内部名 | `Hat.cs:156` |
| 1 | 描述 | `HatDataDefinition.cs:42` |
| 2 | `hide` / `true` / `false` → `hairDrawType` 2/0/1 | `Hat.cs:157-169` |
| 3 | `ignoreHairstyleOffset` bool | `Hat.cs:183` |
| 4 | **ContextTags**（空格）；`Prismatic` 是特殊值 | `ItemContextTagManager.cs:89-90`；`Hat.cs:175-182` |
| 5 | 显示名 | `HatDataDefinition.cs:42` |
| 6 | 精灵索引；空 → 用数字 ID | `HatDataDefinition.cs:91-100` |
| 7 | 贴图，默认 `Characters\Farmer\hats` | `HatDataDefinition.cs:42` |

### 2.3 `Data/Boots`

（`BootsDataDefinition.cs:42,83,91`；`StardewValley.Objects/Boots.cs:89-95,151-155`）

| # | 语义 | 依据 |
|---|---|---|
| 0 | 名 | `Boots.cs:90` |
| 1 | 描述 | `BootsDataDefinition.cs:42` |
| 2 | 价格 | `Boots.cs:91` |
| 3 | 防御 | `Boots.cs:92` |
| 4 | 免疫 | `Boots.cs:93` |
| 5 | 颜色表索引 `indexInColorSheet` | `Boots.cs:94` |
| 6 | 显示名 | `BootsDataDefinition.cs:42` |
| 7 | 颜色串（可选，`颜色:索引`） | `Boots.cs:152-154` |
| 8 | 精灵索引（可选）；空 → 用数字 ID | `BootsDataDefinition.cs:91-100` |
| 9 | 贴图（可选），默认 `Maps\springobjects` | `BootsDataDefinition.cs:42` |

### 2.4 `Data/Monsters`

（`Monster.cs:426-463`；`GameLocation.cs:4466-4475`；`TokenParser.cs:168`）

| # | 语义 | 依据 |
|---|---|---|
| 0 | 生命 | `Monster.cs:427` |
| 1 | 对农夫伤害 | `Monster.cs:429` |
| 2 | **未确证**（1.6 源码无读取点） | — |
| 3 | **未确证** | — |
| 4 | `isGlider` bool | `Monster.cs:430` |
| 5 | **未确证** | — |
| 6 | 掉落 `物品ID 概率 …`（两个一组） | `Monster.cs:431-439`；`GameLocation.cs:4468-4474` |
| 7 | resilience | `Monster.cs:440` |
| 8 | jitteriness | `Monster.cs:441` |
| 9 | moveTowardPlayer 阈值 | `Monster.cs:443` |
| 10 | 速度 | `Monster.cs:444` |
| 11 | missChance | `Monster.cs:445` |
| 12 | `isMineMonster` bool | `Monster.cs:446` |
| 13 | 经验 | `Monster.cs:456` |
| 14 | 显示名（可含令牌/翻译引用） | `Monster.cs:462`；`TokenParser.cs:168` |

### 2.5 `Data/CraftingRecipes` / `Data/CookingRecipes`

（`CraftingRecipe.cs:18-37,106-134,223-224`；`LevelUpMenu.cs:582,593`；`LetterViewerMenu.cs:450`）

| # | CraftingRecipes | CookingRecipes |
|---|---|---|
| 0 | 材料 `物品ID 数量 …` | 同左 |
| 1 | 菜单分类串（`Field`/`Home`/`Crafting`/`Ring`…）——**1.6 源码未发现读取点，未确证** | **未确证**（原版都是 `"10 10"` 这类） |
| 2 | 产物 `物品ID 数量 …` | 同左 |
| 3 | `bigCraftable` bool | **解锁条件**（`index_cookingUnlockConditions`） |
| 4 | 解锁条件 | 显示名（可选） |
| 5 | 显示名（可选） | — |

依据：`CraftingRecipe.cs:19,22,25,28,31,34,37`（各 `index_*` 常量）、`:107-121`（读取）、
`:117`（字段 5/4 取显示名）、`:121`（字段 3 = bigCraftable）、`:224`（条件位次 = cooking?3:4）。

**能再嵌条件**：位次 3/4（配方解锁条件，`s Farming 2`、`f Emily 3`、`default`）——
`CraftingRecipe.cs:221-260` `TryParseLevelRequirement`；`Farmer.cs:2418,2425`。

### 2.6 `Data/Fish`

（`GameLocation.cs:14044-14136`；`BobberBar.cs:161-182`；`TV.cs:416-427`；
`ItemContextTagManager.cs:161-176`；`CrabPot.cs:301-303,402-426`；`TankFish.cs:144`）

**普通鱼（14 段）**

| # | 语义 | 依据 |
|---|---|---|
| 0 | 名 | `BobberBar.cs:161` 段首 |
| 1 | 难度（`trap` 表示蟹笼鱼，走另一形态） | `GameLocation.cs:14045,14062` |
| 2 | 运动：`mixed`/`dart`/`smooth`/`floater`/`sinker` | `BobberBar.cs:163-180` |
| 3 | 最小尺寸 | `BobberBar.cs:181` |
| 4 | 最大尺寸 | `BobberBar.cs:182` |
| 5 | 时间段 `起 止 …`（两两一组） | `GameLocation.cs:14087-14104`；`TV.cs:417-426` |
| 6 | 季节串 —— **未确证**（1.6 源码未见读取点；生成季节已移到 `Data/Locations`） | — |
| 7 | 天气 `rainy`/`sunny`/`both` | `GameLocation.cs:14112-14126`；`TV.cs:419,427` |
| 8 | `物品ID 概率 …` —— **未确证**（1.6 源码未见读取点） | — |
| 9 | 最大水深 | `GameLocation.cs:14136` |
| 10 | 概率 | 同左（`float chance`） |
| 11 | 深度倍率 | 同左（`float depthMultiplier`） |
| 12 | 最低钓鱼等级 | `GameLocation.cs:14128` |
| 13 | 教学鱼 bool | `GameLocation.cs:14074` |

**蟹笼鱼（8 段，位次整体前移）**

| # | 语义 | 依据 |
|---|---|---|
| 0 | 名 | `CrabPot.cs:402` |
| 1 | 固定 `trap` | `CrabPot.cs:398`、`ItemContextTagManager.cs:162` |
| 2 | 概率（<0.1 时 ×4，<0.2 时 ×3，否则 ×2） | `CrabPot.cs:426-430` |
| 3 | **未确证**（形如 `688 .45 689 .35`） | — |
| 4 | 水域 `ocean`/`freshwater` | `CrabPot.cs:403`、`ItemContextTagManager.cs:164` |
| 5 | 最小尺寸 | `CrabPot.cs:302` |
| 6 | 最大尺寸 | `CrabPot.cs:303` |
| 7 | **未确证**（原版全 `false`） | — |

### 2.7 `Data/Quests`

（`Quest.cs:279,289`；`NPC.cs:1840-1841`；`ItemDeliveryQuest.cs:476-477`；`SlayMonsterQuest.cs:261-262`）

| # | 语义 | 依据 |
|---|---|---|
| 0 | 任务类型（`Basic`/`Location`/`Crafting`/`Building`/`ItemDelivery`/`LostItem`/`SlayMonster`…） | `Quest.cs:295-430` |
| 1 | 标题 | `Quest.cs:185,289` |
| 2 | 描述 | `Quest.cs:209,289` |
| 3 | 目标 | `Quest.cs:230,289` |
| 4 | **条件**（按任务类型换语义） | `Quest.cs:299,326,340,353,370,402,415,428` |
| 5 | 后续任务（空格分隔） | `Quest.cs:289,293` |
| 6 | 金钱奖励 | `Quest.cs:289` |
| 7 | 奖励描述 | `Quest.cs:289` |
| 8 | `canBeCancelled` bool | `Quest.cs:289` |
| 9 | 目标对白 / 感谢语（可含条件） | `NPC.cs:1841`；`ItemDeliveryQuest.cs:477`；`LostItemQuest.cs:137` |

> `SyntaxAbstractor.cs:66` 只对 1/2/3/9 做「可翻译文本」抽取——**这四位是文本位**。

### 2.8 `Data/Bundles`

**键** = `区域名/编号`（2 段）；**值** = 7 段（`Bundle.cs:37` `FieldCount`）。

（`Bundle.cs:15-37,103-150`；`Game1.cs:3402-3411`）

| # | 语义 | 依据 |
|---|---|---|
| 0 | 内部名 | `Bundle.cs:104` |
| 1 | 奖励（`Utility.getItemFromStandardTextDescription` 语法） | `Bundle.cs:106,168` |
| 2 | 所需物品 `物品ID 数量 品质 …`（**3 个一组**） | `Bundle.cs:128-134`；`Game1.cs:3404-3409` |
| 3 | 颜色 0-6 | `Bundle.cs:144` |
| 4 | 槽位数（可选，默认 = 物品数） | `Bundle.cs:145` |
| 5 | `贴图名:spriteIndex` 或裸 `spriteIndex` | `Bundle.cs:107-127` |
| 6 | 显示名 | `Bundle.cs:105` |

### 2.9 `Data/ChairTiles`

**键** = `贴图名/x/y`（3 段）——由 `GameLocation.cs:1875-1891` 现算：贴图基名 + 图块 `x = tileIndex % sheetWidth` / `y = tileIndex / sheetWidth`。

**值**（`MapSeat.cs:72-98`）：

| # | 语义 | 依据 |
|---|---|---|
| 0 | 座位宽（格） | `MapSeat.cs:74` |
| 1 | 座位高（格） | 同左 |
| 2 | 朝向 `opposite`/`up`/`down`/`left`/`right` | `MapSeat.cs:77-88` |
| 3 | 座位类型串 | `MapSeat.cs:75` |
| 4 | **绘制 X** | `MapSeat.cs:89` |
| 5 | **绘制 Y** | 同左 |
| 6 | `seasonal` bool | `MapSeat.cs:90` |
| 7 | 贴图（可选） | `MapSeat.cs:91-98` |

> ⚠️ 与 `2026-10-06-implicit-conventions.md` §8.1 的更正一致：`Data/ChairTiles` 是**地图座位表**，不是家具图集坐标表。

### 2.10 `Data/animationDescriptions`

（`NPC.cs:4662-4682`；`IslandSouth.cs:82-154`）

| # | 语义 | 依据 |
|---|---|---|
| 0 | intro 帧序列 | `NPC.cs:4668` |
| 1 | 主循环帧序列 | `NPC.cs:4669` |
| 2 | outro 帧序列 | `NPC.cs:4670` |
| 3 | 结束后对白键（可选） | `NPC.cs:4671-4674` |
| 4+ | 动画元数据（可选） | `NPC.cs:4675-4678` |

> **不是三个朝向**，是 `intro / 循环 / outro`。

### 2.11 `Data/NPCGiftTastes`

（`NPC.cs:1413-1417,1497-1533`；`SyntaxAbstractor.cs:65`）

**NPC 条目（11 段）**——注意**对白与物品交替**：

| # | 语义 |
|---|---|
| 0 | 喜爱 对白 |
| 1 | 喜爱 物品（空格分隔） |
| 2 | 喜欢 对白 |
| 3 | 喜欢 物品 |
| 4 | 厌恶 对白 |
| 5 | 厌恶 物品 |
| 6 | 憎恨 对白 |
| 7 | 憎恨 物品 |
| 8 | 中立 对白 |
| 9 | 中立 物品 |
| 10 | **未确证**（原版为空） |

依据：`NPC.cs:1501`（`for i in 0,2,4,6,8` 取 `array5[i+1]`）、`:1514-1529`
（`list2[0]=喜爱`、`list2[3]=憎恨(6)`、`list2[1]=喜欢(2)`、`list2[2]=厌恶(4)`）。

**`Universal_*` 条目**：1 段，纯空格分隔物品列表（`NPC.cs:1413-1417`）。

### 2.12 其余小表

| 资产 | 字段 |
|---|---|
| `Data/HairData`（`Farmer.cs:5276-5299`） | 0 贴图文件（前缀 `Characters\Farmer\`）、1 tileX、2 tileY、3 `usesUniqueLeftSprite` bool、4 `coveredIndex`、5 `isBaldStyle` bool |
| `Data/AquariumFish`（`TankFish.cs:26-47,108-137`；`Summit.cs:767-770`） | 0 精灵索引、1 类型、2 空闲动画、3 冲刺起始帧、4 冲刺保持帧、5 冲刺结束帧、6 贴图、7 **帽子偏移 `x y`** |
| `Data/PaintData`（`BuildingPaintMenu.cs:876-899`） | 偶数段成对：`区域名` / `最小亮度 最大亮度` |
| `Data/TV/CookingChannel`（`TV.cs:553-558`） | 0 菜谱键、1 对白键 |
| `Data/Events/*`（`Event.cs:11720,11728`） | **键** = `<ID>/<前置条件…>`（`SplitPreconditions`）；**值** = 命令脚本，命令按 `/` **引号感知**切分 |
| `Data/Festivals/<season><day>`（`Game1.cs:5916,9958`） | `conditions` = `地点/起时间 止时间`；`set-up`/`mainEvent`/`<NPC>`/`*_y2` 值都是事件脚本 |

---

## 3. CP 怎么写这类资产（含我们自己的实现）

### 3.1 三条路，CP 都支持

| 手段 | 作用面 | 实证 |
|---|---|---|
| `EditData.Entries` | **整体替换那一行的字符串** | RSV `Data/Objects/Boots.json`：`"Entries": {"Rafseazz.RSVCP_FRA_Combat_Boots": "FRA Combat Boots/…/500/7/7/11/…//0/RSV\\Objects\\Boots"}` |
| **`EditData.Fields`** | **字段级！键是数字下标** | Cornucopia（语料 B）`Data/CookingRecipes`：`"Fields": {"Tom Kha Soup": {"0": "Cornucopia_CoconutMilk 1 720 1 404 1"}}`（共 9 处） |
| `TextOperations` | `Target: ["Entries", 键]` 改整串；`Target: ["Fields", 条目, 下标]` 改单字段 | 语料 B 中 **34 处** `Data/NPCGiftTastes` 的 `["Fields", <NPC>, <下标>]` Append |

### 3.2 CP 内部实现（从 `ContentPatcher.dll` 2.9.1 提取的类型/字符串）

- 类型：`IKeyValueEditor`、`DictionaryKeyValueEditor<TKey,TValue>`、`DelimitedStringKeyValueEditor`、
  `ModelKeyValueEditor`、`ListKeyValueEditor<T>`、`EntryEditor`、`KeyValueEditorFactory`、
  `GetStringFieldDelimiter`、`EditDataPatchField`、`EditDataPatchRecord`、`EditDataPatchMoveRecord`。
- 报错串（证实「字符串条目按**下标**编字段」）：
  - `' is a string, so it requires a field index between 0 and '`
  - `' has no field with index '`
  - `'referring to field which doesn't exist and can't be added.'`
  - `"is invalid: value can't contain field delimiter character '/'"`（改字段时新值不能含分隔符）
  - `'using TextOperations with the Fields target'`（迁移提示，说明 `EditData.Fields` 是取代写法）
- 分隔符：`GetStringFieldDelimiter` 默认 `/`，**`Data/Achievements` 例外为 `^`**（DLL 内字面量 `'Data/Achievements'`）；
  与 `SyntaxAbstractor.cs:60` 一致。

### 3.3 我们自己的实现（现状 + 缺口）

`src/SdvCpTool.Core/Merging/EditDataMerger.cs`：

- `ApplyEntries`（:71-122）——字典资产 `obj[key] = value`，**整体替换字符串，正确** ✅
- `ApplyFields`（:124-144）——**只按名字递归合并**：遇到字符串条目会走 else 分支
  `obj[prop.Name] = prop.Value.DeepClone()`，把 `{"0": "…"}` **写成一个 JSON 对象覆盖掉原字符串**
  → **对旧式 `/` 资产是破坏性的**。这是当前最大缺口（语料 B 已有 9 处真实触发）。
- `TextOperationMerger.cs`（:88-157）——`["Fields", 条目, 下标]` 已实现，按 `assetName` 派发 `/` 或 `^` ✅
- `MoveEntries`（CP 有，语料 B 用了 **20 次**）——`src` 下只在 `EditDataMerger.cs:44` 的注释里出现，
  **未见实现**（`PatchApplier.cs` 无处理）。它只影响顺序，不改字符串，风险低但语义会丢。

---

## 4. 哪些位次是「位置字段」

即值本身是坐标 / 矩形 / 偏移：

| 资产 | 位次 | 形式 | 解析方 |
|---|---|---|---|
| `Data/Furniture` | 2 | `w h`（16px 格，精灵框） | `Furniture.cs:248-251` |
| `Data/Furniture` | 3 | `w h`（包围盒） | `Furniture.cs:366-367` |
| `Data/ChairTiles` | 0,1 | 座位 `w h` | `MapSeat.cs:74` |
| `Data/ChairTiles` | 4,5 | 绘制 XY | `MapSeat.cs:89` |
| `Data/ChairTiles` | **键** | `贴图/x/y`（图块坐标） | `GameLocation.cs:1889-1891` |
| `Data/AquariumFish` | 7 | `x y`（帽子像素偏移） | `TankFish.cs:130-131` |
| `Data/Fish` | 5 | `起 止`（时间对，非坐标但同为空格二元组） | `GameLocation.cs:14091-14104` |
| `Data/Events/*`、`Data/Festivals/*` | 各命令参数 | `x y` / `x y 朝向`（如 `farmer 12 54 1`、`viewport -1000 -1000`） | `Event.cs` 各 `DefaultCommands.*` |

**不是位置字段但常被误判的**：`Data/Furniture` 位次 1（类型名）、位次 8（精灵**索引**，非坐标）；
`Data/ChairTiles` 的键是**图集格号**而非像素。

---

## 5. 未确证清单（明确标注）

1. `Data/Furniture` 位次 6 的语义——1.6 源码无读取点，原版全为 `-1`。
2. `Data/Monsters` 位次 2、3、5 的语义——1.6 源码无读取点。
3. `Data/Fish` 位次 6（季节）、位次 8（`物品ID 概率`）——1.6 源码无读取点；
   季节生成已迁到 `Data/Locations[*].Fish`（`GameLocation.cs:13936-13952`）。
4. `Data/Fish` 蟹笼形态的位次 3、7。
5. `Data/CraftingRecipes` 位次 1（`Field`/`Home`/`Ring`…）、`Data/CookingRecipes` 位次 1——
   1.6 源码无读取点，语义只能从原版数据推断。
6. `Data/NPCGiftTastes` 位次 10。
7. 语料 B 中 `EditData.Fields` 数字下标在**字符串条目上越界追加**是否被允许——
   DLL 里有「field 不存在且无法添加」的报错串，倾向**不允许**；与 `TextOperationMerger.cs:128-133`
   当前「越界补空字段」的行为可能不一致，需实测。

---

## 6. 对转换器的直接影响（可落地）

1. **`EditDataMerger.ApplyFields` 必须支持字符串条目 + 数字下标**（按 `assetName` 取 `/` 或 `^`），
   否则语料 B 已有的 9 处 `Data/CookingRecipes` 补丁会产出非法 JSON。
2. 补 `MoveEntries`（只改顺序，可低优先级，但别静默丢弃）。
3. 上表的四类「位置字段」在改数据时要连带换算（尤其 `Data/Furniture` 位次 2/3 —— 见隐式约定文档 §8.6）。
4. 新增条目时**必须补齐尾部可选字段**：`Data/NPCGiftTastes` 要凑满 11 段、
   `Data/Boots` 要凑满 7 段、`Data/Furniture` 字符串 ID 条目要补位次 8，
   否则会落进「两种形态」的另一支（如 `Universal_*` vs NPC）。
5. `Data/Events/*` 的切分是**引号感知**的（`Event.cs:11732`），不能用朴素 `Split('/')`。
