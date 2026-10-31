# 原版「条件字段消费点」权威台账

日期：2026-10-06
依据（全部只读）：
- `D:\sdv_src` —— 1.6.15 反编译源码（逻辑调用点的唯一权威）
- `D:\sdv_gd` —— `StardewValley.GameData.*` 数据模型源码（**模型类不在 `sdv_src` 里**，见 §0 说明）
- `D:\sdv_tree\Data\*.json` —— 解包原版数据（**交叉验证的唯一权威**）

目的：给转换器做验证。**`Condition` 只有落在游戏真的会读的位置才有意义**；落在别处会被静默忽略。

---

## 0. 方法与口径

- `D:\sdv_src` 里 `GameStateQuery.CheckConditions(` 共 **93 次出现 / 91 行**，其中 `StardewValley/GameStateQuery.cs:247`
  是 XML 文档注释（`<see cref="M:...CheckConditions(...)" />`），**不是调用**。
  → **真实调用点 = 92 个**，分布在 **90 行**代码上（`Utility.cs:2269` 与 `ItemDeliveryQuest.cs:89` 各含 2 个调用）。
- `sdv_src` 里**搜不到 `namespace StardewValley.GameData`**：数据模型类未随主程序反编译出来。
  模型字段名与路径以 `D:\sdv_gd` 的模型源码为准，并用 `sdv_tree` 实物数据复核。
- 「已证实」= 在 `D:\sdv_tree\Data\对应资产.json` 里**真的存在该路径的键**（哪怕原版值全为 `null`）。
  值为 `null` 的字段单独标注「字段存在，原版全 null」——它们对 mod 依然可写，属真实消费点。
- 未做任何推测：查不到来源的一律写「未确证 / 非资产」。

---

## 1. 主表：92 个消费点

「资产」列 = 数据来源的 `Data/*` 资产；「路径形状」= 该字段在资产 JSON 里的位置，`<id>` 为条目键。

### 1.1 非资产（7 个消费点，条件来自内联字符串，**不在任何 `Data/*` 字段上**）

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/DebugCommands.cs:3860` | 控制台命令参数 `rawQuery`（`GameStateQuery.SplitRaw`） | 无 | 无（SMAPI 控制台输入） | n/a |
| `StardewValley/Dialogue.cs:652` | 对话文本里的 `$query` 命令 | 对话/事件文本（`Characters/Dialogue/*`、`Data/ExtraDialogue`） | 无（写在对话串内） | 未确证（非资产字段） |
| `StardewValley/Preconditions.cs:752` | 事件前置条件 `GameStateQuery`（别名 `G`）的第 1 个参数 | 事件脚本（`Data/Events/*` 等） | 无（写在事件 precondition 串内） | 未确证（非资产字段） |
| `StardewValley/Triggers/TriggerActionManager.cs:58` | `DefaultActions.If` 动作的自带条件串 | 触发动作文本（`Data/TriggerActions` 的 `Actions[]`） | 无（写在动作串内） | 未确证（非资产字段） |
| `StardewValley/GameLocation.cs:2470` | 地图属性 `BuildConditions` | 地图 `.tbin` 的 `Properties` | 地图属性表 | 已证实（见 `2026-10-06-map-condition-slots.md`） |
| `StardewValley/GameLocation.cs:3907` | 图块 `TouchAction` = `ConditionalDoor <GSQ>` | 地图 `.tbin` `Back` 层 | 图块属性 | 已证实（同上） |
| `StardewValley/GameLocation.cs:9549` | 图块 `Action` = `ConditionalDoor <GSQ>` | 地图 `.tbin` `Buildings` 层 | 图块属性 | 已证实（同上） |

### 1.2 资产字段（85 个消费点，覆盖 25 个 `Data/*` 资产）

#### `Data/GiantCrops`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Crop.cs:942` | `GiantCropData.Condition` | `Data/GiantCrops` | `/<id>/Condition` | 已证实（字段存在，原版全 null） |
| `StardewValley/TerrainFeatures/GiantCrop.cs:259` | `GiantCropHarvestItemData.Condition`（`GiantCropData.HarvestItems`） | `Data/GiantCrops` | `/<id>/HarvestItems[]/Condition` | 已证实（字段存在，原版全 null） |

#### `Data/Characters`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Event.cs:5514` | `CharacterData.UnlockConditions` | `Data/Characters` | `/<id>/UnlockConditions` | 已证实 |
| `StardewValley/Game1.cs:7322` | `CharacterData.UnlockConditions` | `Data/Characters` | `/<id>/UnlockConditions` | 已证实 |
| `StardewValley/Utility.cs:2269`（第 2 个调用） | `CharacterData.UnlockConditions` | `Data/Characters` | `/<id>/UnlockConditions` | 已证实 |
| `StardewValley/NPC.cs:857` | `CharacterHomeData.Condition` | `Data/Characters` | `/<id>/Home[]/Condition` | 已证实 |
| `StardewValley/NPC.cs:976` | `CharacterAppearanceData.Condition` | `Data/Characters` | `/<id>/Appearance[]/Condition` | 已证实 |
| `StardewValley/NPC.cs:2410` | `CharacterData.SpouseGiftJealousy` | `Data/Characters` | `/<id>/SpouseGiftJealousy` | 已证实（字段存在，原版全 null） |
| `StardewValley/NPC.cs:4876` | `CharacterData.CanSocialize` | `Data/Characters` | `/<id>/CanSocialize` | 已证实 |
| `StardewValley/NPC.cs:6317` | `CharacterData.SpouseAdopts` | `Data/Characters` | `/<id>/SpouseAdopts` | 已证实（字段存在，原版全 null） |
| `StardewValley/Utility.cs:1352` | `GenericSpawnItemDataWithCondition.Condition`（`WinterStarGifts`） | `Data/Characters` | `/<id>/WinterStarGifts[]/Condition` | 已证实 |
| `StardewValley/Utility.cs:4765` | `CharacterData.SpouseWantsChildren` | `Data/Characters` | `/<id>/SpouseWantsChildren` | 已证实（字段存在，原版全 null） |
| `StardewValley/Utility.cs:6016` | `CharacterData.WinterStarParticipant` | `Data/Characters` | `/<id>/WinterStarParticipant` | 已证实 |
| `StardewValley/Locations/IslandSouth.cs:687` | `CharacterData.CanVisitIsland` | `Data/Characters` | `/<id>/CanVisitIsland` | 已证实 |
| `StardewValley/Quests/ItemDeliveryQuest.cs:89`（第 1 个调用） | `CharacterData.CanSocialize` | `Data/Characters` | `/<id>/CanSocialize` | 已证实 |
| `StardewValley/Quests/ItemDeliveryQuest.cs:89`（第 2 个调用） | `CharacterData.ItemDeliveryQuests` | `Data/Characters` | `/<id>/ItemDeliveryQuests` | 已证实 |

> 注意：这 8 个字段名里**没有 `Condition` 字样**（`CanSocialize` / `CanVisitIsland` / `ItemDeliveryQuests` /
> `WinterStarParticipant` / `SpouseGiftJealousy` / `SpouseWantsChildren` / `SpouseAdopts`），但**值是 GSQ 串、游戏确实当 GSQ 读**。
> 转换器若按字段名筛 `Condition` 会漏掉它们。

#### `Data/FarmAnimals`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/FarmAnimal.cs:929` | `FarmAnimalProduce.Condition` | `Data/FarmAnimals` | `/<id>/ProduceItemIds[]/Condition`、`/<id>/DeluxeProduceItemIds[]/Condition` | 已证实（字段存在，原版全 null） |
| `StardewValley/Utility.cs:4945` | `FarmAnimalData.UnlockCondition` | `Data/FarmAnimals` | `/<id>/UnlockCondition` | 已证实 |
| `StardewValley/Menus/PurchaseAnimalsMenu.cs:482` | `AlternatePurchaseAnimals.Condition` | `Data/FarmAnimals` | `/<id>/AlternatePurchaseTypes[]/Condition` | 已证实 |

#### `Data/PassiveFestivals`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Game1.cs:3483` | `PassiveFestivalData.Condition` | `Data/PassiveFestivals` | `/<id>/Condition` | 已证实 |
| `StardewValley/Game1.cs:8514` | `PassiveFestivalData.Condition` | `Data/PassiveFestivals` | `/<id>/Condition` | 已证实 |
| `StardewValley/Game1.cs:8815` | `PassiveFestivalData.Condition` | `Data/PassiveFestivals` | `/<id>/Condition` | 已证实 |
| `StardewValley/Utility.cs:5507` | `PassiveFestivalData.Condition` | `Data/PassiveFestivals` | `/<id>/Condition` | 已证实 |
| `StardewValley/Menus/Billboard.cs:247` | `PassiveFestivalData.Condition` | `Data/PassiveFestivals` | `/<id>/Condition` | 已证实 |

#### `Data/LocationContexts`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Game1.cs:4585` | `LocationContextData.DefaultMusicCondition` | `Data/LocationContexts` | `/<id>/DefaultMusicCondition` | 已证实 |
| `StardewValley/GameLocation.cs:3246` | `LocationContextData.DefaultMusicCondition` | `Data/LocationContexts` | `/<id>/DefaultMusicCondition` | 已证实 |
| `StardewValley/Game1.cs:5432` | `ReviveLocation.Condition` | `Data/LocationContexts` | `/<id>/ReviveLocations[]/Condition` | 已证实 |
| `StardewValley/Game1.cs:5444` | `ReviveLocation.Condition` | `Data/LocationContexts` | `/<id>/ReviveLocations[]/Condition` | 已证实 |
| `StardewValley/GameLocation.cs:3126` | `LocationMusicData.Condition` | `Data/LocationContexts` | `/<id>/Music[]/Condition` | 已证实 |
| `StardewValley/Farmer.cs:5883` | `PassOutMailData.Condition` | `Data/LocationContexts` | `/<id>/PassOutMail[]/Condition` | 已证实 |
| `StardewValley/Multiplayer.cs:601` | `ReviveLocation.Condition` | `Data/LocationContexts` | `/<id>/PassOutLocations[]/Condition` | 已证实 |
| `StardewValley/Network/LocationWeather.cs:173` | `WeatherCondition.Condition` | `Data/LocationContexts` | `/<id>/WeatherConditions[]/Condition` | 已证实 |

#### `Data/Buildings`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Buildings/Building.cs:240` | `BuildingSkin.Condition` | `Data/Buildings` | `/<id>/Skins[]/Condition` | 已证实（字段存在，原版全 null） |
| `StardewValley/Buildings/Building.cs:768` | `BuildingChest.InvalidItemMessageCondition` | `Data/Buildings` | `/<id>/Chests[]/InvalidItemMessageCondition` | 已证实 |
| `StardewValley/Buildings/Building.cs:1682` | `GenericSpawnItemDataWithCondition.Condition`（`ItemConversions[].ProducedItems`） | `Data/Buildings` | `/<id>/ItemConversions[]/ProducedItems[]/Condition` | 已证实（字段存在，原版全 null） |
| `StardewValley/Menus/BuildingSkinMenu.cs:104` | `BuildingSkin.Condition` | `Data/Buildings` | `/<id>/Skins[]/Condition` | 已证实（字段存在，原版全 null） |
| `StardewValley/Menus/CarpenterMenu.cs:271` | `BuildingData.BuildCondition` | `Data/Buildings` | `/<id>/BuildCondition` | 已证实 |
| `StardewValley/Menus/CarpenterMenu.cs:282` | `BuildingSkin.Condition` | `Data/Buildings` | `/<id>/Skins[]/Condition` | 已证实（字段存在，原版全 null） |

#### `Data/Crops` / `Data/WildTrees` / `Data/FruitTrees`（共用 `PlantableRule`）

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/GameLocation.cs:726` | `PlantableRule.Condition` | `Data/Crops`、`Data/WildTrees`、`Data/FruitTrees` | `/<id>/PlantableLocationRules[]/Condition` | Crops：已证实（`/802/PlantableLocationRules[]/Condition`）；WildTrees/FruitTrees：已证实（字段存在，原版全 null） |

#### `Data/WildTrees`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/TerrainFeatures/Tree.cs:367` | `WildTreeTextureData.Condition` | `Data/WildTrees` | `/<id>/Textures[]/Condition` | 已证实（字段存在，原版全 null） |
| `StardewValley/TerrainFeatures/Tree.cs:625` | `WildTreeItemData.Condition`（基类，含 `WildTreeChopItemData` / `WildTreeSeedDropItemData`） | `Data/WildTrees` | `/<id>/SeedDropItems[]/Condition`、`/<id>/ChopItems[]/Condition`、`/<id>/TapItems[]/Condition`、`/<id>/ShakeItems[]/Condition` | 已证实 |
| `StardewValley/TerrainFeatures/Tree.cs:1385` | `WildTreeTapItemData.Condition` | `Data/WildTrees` | `/<id>/TapItems[]/Condition` | 已证实 |

#### `Data/FruitTrees`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/TerrainFeatures/FruitTree.cs:637` | `FruitTreeFruitData.Condition` | `Data/FruitTrees` | `/<id>/Fruit[]/Condition` | 已证实（字段存在，原版全 null） |

#### `Data/GarbageCans`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/GameLocation.cs:8576` | `GarbageCanItemData.Condition` | `Data/GarbageCans` | `/<id>/BeforeAll[]/Condition`、`/<id>/Items[]/Condition`、`/<id>/AfterAll[]/Condition` | 已证实 |

#### `Data/Machines`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/MachineDataUtility.cs:75` | `MachineOutputTriggerRule.Condition` | `Data/Machines` | `/<id>/OutputRules[]/Triggers[]/Condition` | 已证实 |
| `StardewValley/MachineDataUtility.cs:172` | `MachineItemOutput.Condition` | `Data/Machines` | `/<id>/OutputRules[]/OutputItem[]/Condition` | 已证实 |
| `StardewValley/MachineDataUtility.cs:324` | `MachineEffects.Condition` | `Data/Machines` | `/<id>/LoadEffects[]/Condition`、`/<id>/WorkingEffects[]/Condition` | 已证实（字段存在，原版全 null） |
| `StardewValley/MachineDataUtility.cs:354` | `TemporaryAnimatedSpriteDefinition.Condition` | `Data/Machines` | `/<id>/LoadEffects[]/TemporarySprites[]/Condition`、`/<id>/WorkingEffects[]/TemporarySprites[]/Condition` | 已证实 |
| `StardewValley/Object.cs:1758` | `MachineData.ClearContentsOvernightCondition` | `Data/Machines` | `/<id>/ClearContentsOvernightCondition` | 已证实 |
| `StardewValley/Object.cs:2464` | `MachineData.InvalidItemMessageCondition` | `Data/Machines` | `/<id>/InvalidItemMessageCondition` | 已证实 |
| `StardewValley/Object.cs:2712` | `MachineEffects.Condition`（`WorkingEffects`） | `Data/Machines` | `/<id>/WorkingEffects[]/Condition` | 已证实（字段存在，原版全 null） |

> `TemporaryAnimatedSpriteDefinition` 在全部模型里**只被 `MachineEffects.TemporarySprites` 使用**（已全库 grep 确认），
> 因此该字段的可写位置仅限 `Data/Machines`。

#### `Data/Minecarts`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/GameLocation.cs:10225` | `MinecartNetworkData.UnlockCondition` | `Data/Minecarts` | `/<id>/UnlockCondition` | 已证实 |
| `StardewValley/GameLocation.cs:10246` | `MinecartDestinationData.Condition` | `Data/Minecarts` | `/<id>/Destinations[]/Condition` | 已证实 |

#### `Data/Locations`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/GameLocation.cs:3417` | `LocationData.Music[]`（`LocationMusicData.Condition`） | `Data/Locations` | `/<id>/Music[]/Condition` | 已证实 |
| `StardewValley/GameLocation.cs:13977` | `SpawnFishData.Condition` | `Data/Locations` | `/<id>/Fish[]/Condition` | 已证实 |
| `StardewValley/GameLocation.cs:14248` | `ArtifactSpotDropData.Condition` | `Data/Locations` | `/<id>/ArtifactSpots[]/Condition`（含 `Default`） | 已证实 |
| `StardewValley/GameLocation.cs:14283` | `ArtifactSpotDropData.Condition` | `Data/Locations` | `/<id>/ArtifactSpots[]/Condition`（含 `Default`） | 已证实 |
| `StardewValley/GameLocation.cs:15142` | `SpawnForageData.Condition` | `Data/Locations` | `/<id>/Forage[]/Condition`（含 `Default`） | 已证实 |
| `StardewValley/Objects/TV.cs:394` | `SpawnFishData.Condition` | `Data/Locations` | `/<id>/Fish[]/Condition` | 已证实 |

#### `Data/Shops`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Internal/ShopBuilder.cs:117` | `ShopItemData.Condition`（经 `CheckItemCondition`，调用方 `ShopBuilder.cs:45`） | `Data/Shops` | `/<id>/Items[]/Condition` | 已证实 |
| `StardewValley/Internal/ShopBuilder.cs:122` | `ShopItemData.Condition`（同上） | `Data/Shops` | `/<id>/Items[]/Condition` | 已证实 |
| `StardewValley/Internal/ShopBuilder.cs:211` | `ShopOwnerData.Condition` | `Data/Shops` | `/<id>/Owners[]/Condition` | 已证实 |
| `StardewValley/Menus/ShopMenu.cs:302` | `ShopThemeData.Condition` | `Data/Shops` | `/<id>/VisualTheme[]/Condition` | 已证实（字段存在，原版全 null） |
| `StardewValley/Menus/ShopMenu.cs:767` | `ShopDialogueData.Condition` | `Data/Shops` | `/<id>/Owners[]/Dialogues[]/Condition` | 已证实 |

#### `Data/Powers`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Menus/PowersTab.cs:76` | `PowersData.UnlockedCondition` | `Data/Powers` | `/<id>/UnlockedCondition` | 已证实 |

#### `Data/Pets`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Characters/Pet.cs:769` | `PetGift.Condition` | `Data/Pets` | `/<id>/Gifts[]/Condition` | 已证实（字段存在，原版全 null） |

#### `Data/Movies`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Minigames/CraneGame.cs:300` | `MovieCranePrizeData.Condition` | `Data/Movies` | `/<movieId>/CranePrizes[]/Condition` | 已证实（字段存在，原版全 null） |

#### `Data/IncomingPhoneCalls`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Objects/DefaultPhoneHandler.cs:45` | `IncomingPhoneCallData.TriggerCondition` | `Data/IncomingPhoneCalls` | `/<id>/TriggerCondition` | 已证实 |
| `StardewValley/Objects/DefaultPhoneHandler.cs:65` | `IncomingPhoneCallData.RingCondition` | `Data/IncomingPhoneCalls` | `/<id>/RingCondition` | 已证实 |

> 语义差异：`TriggerCondition` 在**主机**上判定「这通电话要不要发出去」；`RingCondition` 在**每个玩家**上判定「这通电话对谁响」。

#### `Data/SpecialOrders`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/SpecialOrders/SpecialOrder.cs:510` | `SpecialOrderData.Condition` | `Data/SpecialOrders` | `/<id>/Condition` | 已证实 |

#### `Data/Tools`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Internal/ShopBuilder.cs:171` | `ToolUpgradeData.Condition`（`ToolData.UpgradeFrom`） | `Data/Tools` | `/<id>/UpgradeFrom[]/Condition` | 已证实 |

#### `Data/TriggerActions`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Triggers/TriggerActionManager.cs:587` | `TriggerActionData.SkipPermanentlyCondition` | `Data/TriggerActions` | `/[]/SkipPermanentlyCondition`（顶层数组元素） | 已证实 |
| `StardewValley/Triggers/TriggerActionManager.cs:767` | `TriggerActionData.Condition` | `Data/TriggerActions` | `/[]/Condition`（顶层数组元素） | 已证实 |

#### `Data/WorldMap`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/WorldMaps/MapArea.cs:54` | `WorldMapTextureData.Condition` | `Data/WorldMap` | `/<region>/MapAreas[]/Textures[]/Condition` | 已证实 |
| `StardewValley/WorldMaps/MapArea.cs:109` | `WorldMapTooltipData.Condition` | `Data/WorldMap` | `/<region>/MapAreas[]/Tooltips[]/Condition` | 已证实 |
| `StardewValley/WorldMaps/MapArea.cs:111` | `WorldMapTooltipData.KnownCondition` | `Data/WorldMap` | `/<region>/MapAreas[]/Tooltips[]/KnownCondition` | 已证实 |
| `StardewValley/WorldMaps/MapArea.cs:136` | `WorldMapAreaPositionData.Condition` | `Data/WorldMap` | `/<region>/MapAreas[]/WorldPositions[]/Condition` | 已证实 |
| `StardewValley/WorldMaps/MapRegion.cs:73` | `WorldMapTextureData.Condition` | `Data/WorldMap` | `/<region>/BaseTexture[]/Condition` | 已证实 |
| `StardewValley/WorldMaps/MapRegion.cs:111` | `WorldMapAreaData.Condition` | `Data/WorldMap` | `/<region>/MapAreas[]/Condition` | 已证实 |

#### `Data/Weddings`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Utility.cs:2269`（第 1 个调用） | `WeddingAttendeeData.Condition` | `Data/Weddings` | `/Attendees/<id>/Condition` | 已证实 |

#### `Data/FishPondData`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Buildings/FishPond.cs:256` | `FishPondReward.Condition` | `Data/FishPondData` | `/<id>/ProducedItems[]/Condition` | 已证实 |
| `StardewValley/Buildings/FishPond.cs:674` | `FishPondWaterColor.Condition` | `Data/FishPondData` | `/<id>/WaterColor[]/Condition` | 已证实 |

#### `Data/Objects`

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Utility.cs:6780` | `ObjectGeodeDropData.Condition`（`ObjectData.GeodeDrops`） | `Data/Objects` | `/<id>/GeodeDrops[]/Condition` | 已证实 |

#### 跨资产通用模型（2 个消费点）

| 文件:行号 | 数据模型.字段 | 来自资产 | 数据里的路径形状 | 已证实？ |
|---|---|---|---|---|
| `StardewValley/Utility.cs:4354` | `QuantityModifier.Condition`（`ApplyQuantityModifiers`） | 任意挂载 `QuantityModifier` 的资产 | `.../PriceModifiers[]/Condition`、`.../StackModifiers[]/Condition`、`.../ChanceModifiers[]/Condition`、`.../ReadyTimeModifiers[]/Condition`、`.../AvailableStockModifiers[]/Condition`、`.../DaysUntilReadyModifiers[]/Condition` 等 | 已证实（实测见 `Data/Shops`、`Data/Locations`、`Data/Machines`、`Data/WildTrees`） |
| `StardewValley/Internal/ItemQueryResolver.cs:767` | `ISpawnItemData.PerItemCondition`（`TryResolveRandomItem` → `TryResolve`） | 任意 spawn-item 资产（`GenericSpawnItemData` 体系） | 所有 `GenericSpawnItemDataWithCondition` 条目上的 `PerItemCondition` | 已证实（`Data/Shops`、`Data/Locations`、`Data/Objects`、`Data/Machines` 等均有实物） |

---

## 2. ★ 汇总：资产 → 该资产里所有能承载 GSQ 的路径形状

这是转换器真正要用的那张表。**只有列在这里的路径，写 `Condition` 才会被读。**
（`<id>` / `<region>` / `<movieId>` = 条目键；`[]` = 数组元素；`|` = 任一。）

| `Data/*` 资产 | 可承载 GSQ 的路径形状 |
|---|---|
| `Data/GiantCrops` | `/<id>/Condition`；`/<id>/HarvestItems[]/Condition` |
| `Data/Characters` | `/<id>/UnlockConditions`；`/<id>/CanSocialize`；`/<id>/CanVisitIsland`；`/<id>/ItemDeliveryQuests`；`/<id>/WinterStarParticipant`；`/<id>/SpouseGiftJealousy`；`/<id>/SpouseWantsChildren`；`/<id>/SpouseAdopts`；`/<id>/Home[]/Condition`；`/<id>/Appearance[]/Condition`；`/<id>/WinterStarGifts[]/Condition` |
| `Data/FarmAnimals` | `/<id>/UnlockCondition`；`/<id>/ProduceItemIds[]/Condition`；`/<id>/DeluxeProduceItemIds[]/Condition`；`/<id>/AlternatePurchaseTypes[]/Condition` |
| `Data/PassiveFestivals` | `/<id>/Condition` |
| `Data/LocationContexts` | `/<id>/DefaultMusicCondition`；`/<id>/Music[]/Condition`；`/<id>/PassOutMail[]/Condition`；`/<id>/ReviveLocations[]/Condition`；`/<id>/PassOutLocations[]/Condition`；`/<id>/WeatherConditions[]/Condition` |
| `Data/Buildings` | `/<id>/BuildCondition`；`/<id>/Skins[]/Condition`；`/<id>/Chests[]/InvalidItemMessageCondition`；`/<id>/ItemConversions[]/ProducedItems[]/Condition` |
| `Data/Crops` | `/<id>/PlantableLocationRules[]/Condition` |
| `Data/WildTrees` | `/<id>/PlantableLocationRules[]/Condition`；`/<id>/Textures[]/Condition`；`/<id>/SeedDropItems[]/Condition`；`/<id>/ChopItems[]/Condition`；`/<id>/TapItems[]/Condition`；`/<id>/ShakeItems[]/Condition` |
| `Data/FruitTrees` | `/<id>/PlantableLocationRules[]/Condition`；`/<id>/Fruit[]/Condition` |
| `Data/GarbageCans` | `/<id>/BeforeAll[]/Condition`；`/<id>/Items[]/Condition`；`/<id>/AfterAll[]/Condition` |
| `Data/Machines` | `/<id>/ClearContentsOvernightCondition`；`/<id>/InvalidItemMessageCondition`；`/<id>/OutputRules[]/Triggers[]/Condition`；`/<id>/OutputRules[]/OutputItem[]/Condition`；`/<id>/LoadEffects[]/Condition`；`/<id>/WorkingEffects[]/Condition`；`/<id>/LoadEffects[]/TemporarySprites[]/Condition`；`/<id>/WorkingEffects[]/TemporarySprites[]/Condition` |
| `Data/Minecarts` | `/<id>/UnlockCondition`；`/<id>/Destinations[]/Condition` |
| `Data/Locations` | `/<id>/Music[]/Condition`；`/<id>/Fish[]/Condition`；`/<id>/Forage[]/Condition`；`/<id>/ArtifactSpots[]/Condition` |
| `Data/Shops` | `/<id>/Items[]/Condition`；`/<id>/Owners[]/Condition`；`/<id>/Owners[]/Dialogues[]/Condition`；`/<id>/VisualTheme[]/Condition` |
| `Data/Powers` | `/<id>/UnlockedCondition` |
| `Data/Pets` | `/<id>/Gifts[]/Condition` |
| `Data/Movies` | `/<movieId>/CranePrizes[]/Condition` |
| `Data/IncomingPhoneCalls` | `/<id>/TriggerCondition`；`/<id>/RingCondition` |
| `Data/SpecialOrders` | `/<id>/Condition` |
| `Data/Tools` | `/<id>/UpgradeFrom[]/Condition` |
| `Data/TriggerActions` | `/[]/Condition`；`/[]/SkipPermanentlyCondition` |
| `Data/WorldMap` | `/<region>/BaseTexture[]/Condition`；`/<region>/MapAreas[]/Condition`；`/<region>/MapAreas[]/Textures[]/Condition`；`/<region>/MapAreas[]/Tooltips[]/Condition`；`/<region>/MapAreas[]/Tooltips[]/KnownCondition`；`/<region>/MapAreas[]/WorldPositions[]/Condition` |
| `Data/Weddings` | `/Attendees/<id>/Condition` |
| `Data/FishPondData` | `/<id>/ProducedItems[]/Condition`；`/<id>/WaterColor[]/Condition` |
| `Data/Objects` | `/<id>/GeodeDrops[]/Condition` |

**跨资产附属字段（出现在上面任意资产的对应位置）**：
- `PerItemCondition` —— 凡 `GenericSpawnItemDataWithCondition` 条目均可挂（`Data/Shops`、`Data/Locations`、`Data/Objects`、`Data/Machines`、`Data/WildTrees`、`Data/FruitTrees`、`Data/GarbageCans`、`Data/Pets`、`Data/Movies`、`Data/FishPondData`、`Data/Characters`、`Data/Buildings`、`Data/GiantCrops` 均已实测存在）。
- `QuantityModifier.Condition` —— 凡出现 `*Modifiers` 数组的位置：`PriceModifiers` / `StackModifiers` / `ChanceModifiers` / `ReadyTimeModifiers` / `AvailableStockModifiers` / `DaysUntilReadyModifiers` 等。

**非 `Data/*` 的条件位**（另见 `2026-10-06-map-condition-slots.md`）：
- 地图 `.tbin`：图块 `Action`/`TouchAction` = `ConditionalDoor <GSQ>`；地图属性 `BuildConditions`。
- 事件脚本：前置条件 `G` / `GameStateQuery <GSQ>`。
- 对话文本：`$query <GSQ>`。
- `Data/TriggerActions` 的 `Actions` 串内联 `If <GSQ> ## ...`。
- SMAPI 控制台直接输入。

---

## 3. 名字里带 Condition、但**游戏不当 GSQ 读**的字段

逐个核实，附源码依据。

| 字段 | 类型 | 位置 | 为什么不消费 | 源码依据 |
|---|---|---|---|---|
| `WeddingAttendeeData.IgnoreUnlockConditions` | **`bool`** | `Data/Weddings`：`/Attendees/<id>/IgnoreUnlockConditions` | 是**布尔旁路开关**：为 `true` 时**跳过**对 `CharacterData.UnlockConditions` 的 GSQ 求值，本身不是条件串 | 模型：`sdv_gd/StardewValley.GameData.Weddings/WeddingAttendeeData.cs:24`；消费：`StardewValley/Utility.cs:2269` 的 `(value2.IgnoreUnlockConditions \|\| ... GameStateQuery.CheckConditions(data.UnlockConditions, ...))` |
| `IncomingPhoneCallData.IgnoreBaseChance` | **`bool`** | `Data/IncomingPhoneCalls`：`/<id>/IgnoreBaseChance` | 布尔旁路：绕过 1% 基础概率，不是条件 | 模型：`sdv_gd/StardewValley.GameData/IncomingPhoneCallData.cs:37`；消费：`StardewValley/Objects/DefaultPhoneHandler.cs:45` 的 `(flag \|\| item.Value.IgnoreBaseChance) && (...TriggerCondition...)` |
| `GarbageCanItemData.IgnoreBaseChance` | **`bool`** | `Data/GarbageCans`：`/<id>/**/IgnoreBaseChance` | 同上，布尔旁路 | 模型：`sdv_gd/StardewValley.GameData.GarbageCans/GarbageCanItemData.cs:11`；消费：`StardewValley/GameLocation.cs:8576` 的 `(flag \|\| item3.IgnoreBaseChance) && CheckConditions(item3.Condition, ...)` |
| `SpecialOrderData.RequiredTags` | `string`（逗号分隔标签） | `Data/SpecialOrders`：`/<id>/RequiredTags` | **旧版自研标签系统**，用 `SpecialOrder.CheckTags` 解析（`!` 取反 + 白名单标签），**与 GSQ 完全无关**；模型注释明说「多数代码应改用 `Condition`」 | 模型：`sdv_gd/StardewValley.GameData.SpecialOrders/SpecialOrderData.cs:22-24`；解析器：`StardewValley/SpecialOrders/SpecialOrder.cs:307`（`CheckTags`）/`:339`（`CheckTag`）；消费：`SpecialOrder.cs:506` |
| `RandomizedElementItem.RequiredTags` | `string` | `Data/SpecialOrders`：`/<id>/RandomizedElements[]/Values[]/RequiredTags` | 同上，旧标签系统 | 模型：`sdv_gd/StardewValley.GameData.SpecialOrders/RandomizedElementItem.cs:10`；消费：`StardewValley/SpecialOrders/SpecialOrder.cs:559` |
| `LocationContextData.WeatherConditions` | `List<WeatherCondition>` | `Data/LocationContexts` | **字段本身不是条件**——它是容器；真正被求值的是其元素的 `.Condition` | 模型：`sdv_gd/StardewValley.GameData.LocationContexts/LocationContextData.cs:53`；消费：`StardewValley/Network/LocationWeather.cs:171-173` |
| `GameStateQuery.SeasonQueryKeys` | `static string[]` | 代码常量，非数据字段 | 是「可被 `ignoreQueryKeys` 忽略的查询键列表」，传参用，不是条件串 | `StardewValley/GameStateQuery.cs`（被 `ShopBuilder.cs:122`、`FruitTree.cs:637` 当参数传入） |
| `GameLocation` 方法参数 `ignoreQueryKeys` / `bypassConditions` / `ignoreConditionsCheck` | `bool`/列表 参数 | C# 方法签名 | 旁路开关，非数据字段；`Game1.AddCharacterIfNecessary(..., bypassConditions)` 就是它绕过 `UnlockConditions` | `StardewValley/Game1.cs:7313,7322`；`StardewValley/Utility.cs:5491,5507`；`StardewValley/Menus/Billboard.cs:244` |

### 3.1 明确**不存在**的字段（重要，防止再踩）

- **`Data/Objects` 的条目级 `Condition`**：`ObjectData` 模型里**没有** `Condition` 字段（全库 grep
  `sdv_gd/StardewValley.GameData.Objects/ObjectData.cs` 无此属性）；`Objects.json` 里的 11 处 `Condition`
  **全部**位于 `/<id>/GeodeDrops[]/` 之下（另有 `/<id>/GeodeDrops[]/StackModifiers[]/Condition`）。
  → 把条件写到 `Data/Objects` 条目根上，游戏**静默不读**。这一点已被实物数据反证确认。
- `CharacterData` 条目的 `Condition`、`ToolData` 条目的裸 `Condition`、`ShopData` 条目的裸 `Condition`
  等同样**不存在**；对应语义字段是 `UnlockConditions` / `UpgradeFrom[]/Condition` / `Owners[]/Condition` 等。

---

## 4. 遗留与不确定项

1. `Dialogue.cs:652`、`Preconditions.cs:752`、`TriggerActionManager.cs:58`、`DebugCommands.cs:3860` 这 4 处
   的 GSQ 来自**内联字符串**而非 `Data/*` 字段，其「宿主资产」是对话／事件脚本／触发动作文本／控制台，
   不在本台账的字段级口径内，标「未确证（非资产字段）」。
2. `MachineEffects.Condition`（`MachineDataUtility.cs:324`）的调用方既可能来自 `LoadEffects` 也可能来自
   `WorkingEffects`（`PlayEffects` 是共用方法），两处路径均已实测存在，故并列为两个路径。
3. `Utility.cs:2269` 与 `ItemDeliveryQuest.cs:89` 一行内各含 **2 个** `CheckConditions` 调用，已拆成 2 行记录。
