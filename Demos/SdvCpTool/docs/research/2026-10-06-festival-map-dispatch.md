# 节日地图的数据驱动调度（Festival map dispatch）普查

日期：2026-10-06
依据：`D:\sdv_src`（1.6.15 反编译）、`D:\sdv_tree\Data\Festivals\*.json`（原版数据 JSON 解包）、
**`D:\sdv_tree\Maps\*.tbin` 10 张节日地图逐字节解析**（用 `D:\XnbConverter\XnbConverter.Core\Tbin\Readers\`
的读取器语义复刻的 Python 解析器）、CP 侧只读 `D:\SdvCpTool\src`

> 前置阅读（避免重复）：`2026-10-06-map-mechanisms-panorama.md`、`2026-10-06-custom-tile-actions.md`、
> `2026-10-06-tbin-block.md`。本文只补它们没覆盖的**节日调度**部分。

---

## 0. 三句话结论

1. **1.6 里没有 `Festival` 类了。节日就是一种 `Event`**（`Event.isFestival` / `Event.festivalData` /
   `Event.festivalUpdate`）。所以「节日脚本语言」**就是事件命令语言本身：140 条指令 + 1 个别名**。
   原版节日数据实际只用了其中 **37 条**。
2. **`/4` 的真相**：它不是 `Data/Festivals` 的字段，而是**节日地图图层（`Set-Up` / `MainEvent`）里图块的 `TileIndex`**。
   `TileIndex / 4` = **actor 索引**，`TileIndex % 4` = **朝向**。4 = 每个 NPC 有 4 个朝向的精灵格。
   actor 索引查的是 **`Data/Characters` 的 `FestivalVanillaActorIndex`**（0..34，共 35 个 NPC）。
3. **节日地图是「字符串即行为」最密集的原版地图**：10 张地图里 15 个地图属性键、31 种 `Action` 取值、
   3 个约定层名（`Set-Up` / `MainEvent` / `Main-Event`）、1 个约定 tilesheet 名（`fest` / `characters`）
   全部是纯字符串/数字约定，没有任何 schema。

---

## 1. 数据入口：`Data/Festivals/<season><day>`

```
GameLocation.cs:15630   string festival = $"{Game1.currentSeason}{Game1.dayOfMonth}";
GameLocation.cs:15633   if (Event.tryToLoadFestival(festival, out var ev)) currentEvent = ev;
```

- 资产名 = `Data\Festivals\` + `<season><day>`（`Event.cs:4314`），如 `spring13`、`fall27`。
- 类型是 `Dictionary<string, string>`（**键名即字段名，不是位置编码**）。
- 哪几天是节日，由**另一份资产** `Data/Festivals/FestivalDates` 决定（`<season><day>` → 节日名）：
  `DataLoader.cs:216-221`、`Utility.cs:318-325`（`isFestivalDay`）。
  注释自证：*"This doesn't match passive festivals like the Night Market."*
- 校验入口：`Event.tryToLoadFestivalData`（`Event.cs:4312-4359`），**唯一必需字段是 `conditions`**（`Event.cs:4341-4345`）。

---

## 2. `Data/Festivals/<id>` 完整字段表

### 2.1 元数据字段（4 个）

| 键 | 值形态 | 语义 | 源码依据 |
|---|---|---|---|
| `name` | 纯文本 | 节日显示名 | `Event.cs:3939-3946`（`Event.FestivalName`）；`Game1.cs:5941` |
| `conditions` | `<地点名>/<开始> <结束>` | **唯一必需字段**。地点名必须等于 `Game1.currentLocation.Name`；时间是 `HHMM` 整数对 | `Event.cs:4341-4357`；时限判定 `Event.cs:4372` |
| `startedMessage` | TokenParser 文本 | 节日开始时全屏提示。**原版 1.6 数据里 0 条使用** | `Game1.cs:5924-5926` |
| `locationDisplayName` | 纯文本 | 提示里用的地点显示名。**原版 1.6 数据里 0 条使用** | `Game1.cs:5930-5940` |

### 2.2 脚本字段（每条都是 `/` 分隔的**事件脚本**）

解析器统一是 `Event.ParseCommands`（`Event.cs:11728-11733`）：
`applyGenderSwitchBlocks` → `TokenParser.ParseText` → 按 `/` 切开（去空、trim、保留引号转义）。

取值统一走 `Event.TryGetFestivalDataForYear(key, ...)`（`Event.cs:4417-4443`）：
它会向上找 `{key}_y2`、`{key}_y3`…，然后 `Game1.year % N` 选一个变体——
**这就是 `_yN` 年份后缀的全部机制**（不是「第二年专用的键」那么简单，是**取模循环**）。

| 键 | 何时被加载 | 读的是哪行代码 | 有 3 段脚本头？ |
|---|---|---|---|
| `set-up`（必需） | 节日开始时 | `Event.cs:4386-4390` | **有** |
| `mainEvent` | 主持人对话后 | `Event.cs:11700-11709`（`forceFestivalContinue`，配 `eventSwitched = true`）<br>`Event.cs:11798`（`setUpFestivalMainEvent`，春季舞会专用） | 无 |
| `afterEggHunt` | 找蛋计时结束（`playerControlSequenceID == "eggHunt"`） | `Event.cs:11202-11207` | 无 |
| `afterIceFishing` | 冰钓计时结束 | `Event.cs:11180-11185` | 无 |
| `secretSanta` | 冬季星盛宴交换礼物 | `Event.cs:11090-11097` | **有** |
| **任意自定义名** | 由脚本里的 `fork <名>` / `switchEvent <名>` 加载 | `Event.cs:1839-1846` / `Event.cs:1876-1883` | 无（`forked = true` 跳过初始化） |

**原版实际用到 3 组自定义脚本字段**（全部由 `fork` / `switchEvent` 拉起）：

```
spring13: fork AbbyWin                                     → AbbyWin
winter8 : fork DickWin / fork OtherPlayerWin               → DickWin, OtherPlayerWin
summer11: switchEvent governorReaction<N>                  → governorReaction0..6
           （N 由 luau 汤评分算出：Event.cs:13442）
```

### 2.3 对话字段（键名 = NPC 名）

| 键形态 | 语义 | 源码依据 |
|---|---|---|
| `<NPC名>` | 节日里该 NPC 的对话 | `Event.cs:2871`、`Event.cs:5538-5541`、`Event.cs:12559-12561` |
| `<NPC名>_spouse` | 已婚时的对话 | `Event.cs:12617`、`Event.cs:12661` |
| `<NPC名>_roommate` | 室友（Krobus）时的对话 | `Event.cs:12613`、`Event.cs:12657` |
| `<key>_yN` | 任意上述键的年份变体 | `Event.cs:4417-4443` |

解析入口：`Event.TryGetFestivalDialogueForYear`（`Event.cs:4401-4410`）→ `new Dialogue(npc, asset:key, text)`。
**注意 `_spouse` / `_roommate` 是硬编码后缀**，不是通用的状态后缀。

### 2.4 actor 专用字段

| 键形态 | 语义 | 源码依据 |
|---|---|---|
| `<图层名>_additionalCharacters` | **额外** NPC 摆放，一行一个：`<NPC名> <x> <y> <朝向>` | `Event.cs:3307-3330` |

### 2.5 遗留 / 运行时注入

| 键 | 说明 | 依据 |
|---|---|---|
| `file` | **运行时注入**（值 = 节日 id），不是作者写的 | `Event.cs:4385` |
| `Event.cs.1862` | **死键**。1.5 时代从节日数据读，1.6 已改成从 `Strings\StringsFromCSFiles:Event.cs.1862` 读 | 对比 `spring13.json` 有该键 vs `Event.cs:13475` |
| `shop_y2` | **死键**。1.5 时代的节日内联商店库存（`<type> <itemId> <price> <stock>` 四元组），1.6 已迁到 `Data/Shops.Festival_SpiritsEve_Pierre` | 读取路径仍存在（`Event.cs:12418-12448`），但 `fall27` 里只剩 `shop_y2` 且该路径用 `TryGetValue` 精确匹配（无 `_yN` 展开），故取不到 |
| `AbbyWin` / `DickWin` / `OtherPlayerWin` | 不是元数据，是**脚本字段**（见 2.2） | — |

> ⚠️ 发现：`Event.cs:12418-12448` 那段 legacy 商店解析里，变量 `array` 是 **action 字符串**而不是数据串 `array2`
> （`ArgUtility.Get(array, num, ...)` 应为 `array2`）——**疑似原版 bug**，但该路径在 1.6 已无数据触发。标「未确证是否有意」。

---

## 3. `/4` 机制：`TileIndex / 4` = actor number

### 3.1 算在哪

```csharp
// Event.cs:3264  LoadActors —— 「把地图某图层上的图块变成节日 NPC」的命令
public static void LoadActors(Event @event, string[] args, EventContext context)
{
    Layer layer = @event.temporaryLocation?.map.GetLayer(value);   // value = "Set-Up" / "MainEvent" / "Main-Event"
    ...
    // Event.cs:3279-3287  先建索引 → NPC 名的字典
    foreach (var characterDatum in Game1.characterData)            // ← Data/Characters
    {
        int festivalVanillaActorIndex = characterDatum.Value.FestivalVanillaActorIndex;
        if (festivalVanillaActorIndex >= 0 && !dictionary.TryAdd(festivalVanillaActorIndex, characterDatum.Key))
            Game1.log.Warn($"NPC '{...}' has the same festival actor index as '{...}' ...");
    }
    // Event.cs:3289-3306  逐格扫图层
    for (int i = 0; i < layer.LayerWidth; i++)
        for (int j = 0; j < layer.LayerHeight; j++)
        {
            Tile tile = layer.Tiles[i, j];
            if (tile == null) continue;
            int tileIndex = tile.TileIndex;
            int key = tileIndex / 4;            // ← Event.cs:3297  ★ actor 索引
            int facingDirection = tileIndex % 4; // ← Event.cs:3298  ★ 朝向 0上 1右 2下 3左
            if (dictionary.TryGetValue(key, out var name) && Game1.getCharacterFromName(name) != null
                && (!(name == "Leo") || Game1.MasterPlayer.mailReceived.Contains("leoMoved")))
                @event.addActor(name, i, j, facingDirection, @event.temporaryLocation);
        }
    ...
}
```

`addActor` 定义在 `Event.cs:5501`。

### 3.2 为什么是 4

**因为 `characters` tilesheet 上一个 NPC 占 4 格**——4 个朝向各一格。所以
`TileIndex = actorIndex * 4 + facingDirection`。这是「用图块编码实体」的等价于
`NPC.cs:823` 里把 `FestivalVanillaActorIndex` 直接当 `npc.id` 用的同一套编号。

### 3.3 actor number 的来源与消费

| 环节 | 位置 |
|---|---|
| 定义 | `D:\sdv_gd\StardewValley.GameData.Characters\CharacterData.cs:269` — `public int FestivalVanillaActorIndex = -1;` |
| 写入 npc.id | `NPC.cs:823` — `id = (data.FestivalVanillaActorIndex > -1) ? data.FestivalVanillaActorIndex : Game1.hash.GetDeterministicHashCode(name.Value);` |
| 消费 | `Event.cs:3282-3283`（`LoadActors` 建字典）、`Event.cs:3299`（查表） |

**原版 35 个 NPC 的索引（实测 `Data/Characters.json`）**：

```
0 Abigail    1 Caroline  2 Clint     3 Demetrius 4 Willy    5 Elliott  6 Emily
7 Evelyn     8 George    9 Gus      10 Haley    11 Harvey  12 Jas     13 Jodi
14 Alex     15 Kent     16 Leah     17 Lewis    18 Linus   19 Marlon  20 Marnie
21 Maru     22 Pam      23 Penny    24 Pierre   25 Robin   26 Sam     27 Sebastian
28 Shane    29 Vincent  30 Wizard   31 Dwarf    32 Sandy   33 Krobus  34 Leo
```

**所以节日地图的 `characters` 图层上，TileIndex 只在 `0..139` 区间**（`35*4`）。

### 3.4 实测证据（`D:\sdv_tree\Maps\*.tbin` 逐字节解析）

以 `Forest-FlowerFestival.tbin`（100×50）为例：

| 层 | 非空图块 | 图块示例 | tilesheet |
|---|---|---|---|
| `Set-Up` | 32 | `(24,33) idx=10 → /4 = 2 (Clint), %4 = 2 (朝下)`；`(4,35) idx=1 → /4 = 0 (Abigail), %4 = 1 (朝右)` | `Characters` |
| `MainEvent` | 32 | `(22,20) idx=2 → /4 = 0 (Abigail), %4 = 2 (朝下)` | `Characters` |

- 全图 `idx ∈ [0,139]`，**与 3.3 的 35×4 完全吻合**。
- `Town-EggFestival.tbin` 的 `Set-Up` 31 格 / `MainEvent` 30 格，同样区间。
- **层名是数据里的字符串**：`loadActors Set-Up` / `loadActors MainEvent`。
  `winter8`（冰节）用的层名是 **`Main-Event`（带连字符）**——`Maps/Forest-IceFestival.tbin` 里
  层名是 `Main-Event`，而 `winter8.json` 的 `mainEvent` 脚本写的是 `loadActors Main-Event`。
  **这说明层名只能靠字符串精确对上，写错了 `LoadActors` 只会 `log.ErrorAndSkip`。**
- 另一条额外摆放通道：`<图层名>_additionalCharacters`（`Event.cs:3307-3330`），
  格式 `<NPC名> <x> <y> <朝向>`，**给原版 35 人之外的 NPC 用**。

### 3.5 `Set-Up` 层的额外含义

`Event.cs:3331-3350`：`if (value == "Set-Up")` —— 该层加载完 NPC 后，
**还会把这个 NPC 的已成年孩子也摆进来**。所以 `Set-Up` 这个名字带硬编码语义（`MainEvent` 不带）。

---

## 4. 节日地图的「字符串即行为」清单

对 `D:\sdv_tree\Maps\` 下 **10 张节日地图**做全量解析：
`Beach_SquidFest{,_Revert,Sign_Revert}`、`Desert-Festival`、
`Forest-FlowerFestival{,2}`、`Forest-IceFestival{,2}`、`Town-EggFestival{,2}`。

### 4.1 地图属性（`MapProperties`）——15 个键

| 键 | 语义 | 解析入口 |
|---|---|---|
| `Outdoors` | 标记为室外（值 `T`） | `GameLocation.cs:1957`（`map.Properties.ContainsKey("Outdoors")`） |
| `Warp` | 传送表，5 元组 `fromX fromY toMap toX toY`；`-1` 表示整边 | `GameLocation` warp 解析 |
| `NPCWarp` | NPC 专用传送表 | 同上 |
| `Light` | 光源表，3 元组 `x y 半径` | `GameLocation.cs`（`GetMapPropertySplitBySpaces("Light")`） |
| `DayTiles` | 白天换图块，4 元组 `图层 x y index` | `GameLocation.cs:5282`、`7051` |
| `NightTiles` | 夜晚换图块，同上 | `GameLocation.cs:3085` |
| `BrookSounds` | 水面音效，3 元组 `x y soundId` | `GameLocation.cs:7012` |
| `Spring_Objects` | **季节键**：键名带季节，值 `T` 即在本季节生成物件 | `GameLocation.cs:17293`（`HasMapPropertyWithValue(GetSeason() + "_Objects")`） |
| `Summer_Objects` | 同上 | 同上 |
| `Fall_Objects` | 同上 | 同上 |
| `Winter_Objects` | 同上 | 同上 |
| `AllowGrassSurviveInWinter` | 冬天草不死（值以 `f` 开头则否） | `Grass.cs:356` |
| `LoadTreesFrom` | 从另一张地图拷贝树 | `GameLocation.cs:17272` |
| `LocationContext` | 覆盖地点上下文（值如 `Desert`） | `GameLocation.cs:12950` |
| `forceLoadPathLayerLights` | 强制加载 `Paths` 层灯光 | `GameLocation.cs:1969` |

> **最意外的一条**：`Spring_Objects` 那一组。**键名本身就是一个条件维度**（季节），值只是 `T`。
> 这是一条「**键名当条件**」的原生活样本——和 CP 里 `<NPC>_spouse` / `<key>_yN` 是同一类手法。
> 而且它出现在**地图属性**层，是全游戏唯一把「季节」编码进 map property 键名的地方。

### 4.2 图块属性（tile `Properties`）——节日地图上只有 2 个键

| 键 | 语料计数 | 语义 |
|---|---|---|
| `Action` | 142 处（7 张地图） | 点击行为，`GameLocation.performAction` |
| `TouchAction` | 4 处（仅 `Desert-Festival`） | 踩上行为，`GameLocation.performTouchAction` |
| `NoSpawn` | 2 处（`Beach_SquidFest`） | 禁止生成 |

另一类图块属性挂在 **tilesheet** 上，键形如 `@TileIndex@<n>@<名>`（节日地图实测 238 个不同键，
涉及 `Water` / `Passable` / `Type` / `Diggable` / `Spawnable`）——
**这些不扩充动作集**，只参与 `doesTileHaveProperty` 取值（见 `custom-tile-actions.md` §5）。

### 4.3 `Action` / `TouchAction` 取值——31 种

按首 token 聚合（7 张主地图 + SquidFest）：

| 次数 | 取值 | 次数 | 取值 |
|---|---|---|---|
| 35 | `LockedDoorWarp` | 2 | `DwarfGrave` / `IceCreamStand` / `HMTGF` / `FixRaccoonStump` |
| 17 | `Garbage` | 2 | `DesertVendor` / `DesertShadyGuy` / `DesertCactusMan` / `DesertFishingBoard` |
| 14 | `Message` | 1 | `none` / `DesertFestivalMineExplanation` / `DesertMarlon` / `DesertScholar` |
| 12 | `Shop` | 1 | `DesertGil` / `DesertEggShop` / `DesertFood` / `DesertRacerMan` |
| 8 | `EnterSewer` | 1 | `SquidFestBooth`（SquidFest 4 处） |
| 6 | `TownMailbox` | 1 | `legendarySword` / `DesertBus` / `MakeoverBox` / `DesertMakeover`（`TouchAction`） |
| 4 | `MasteryRoom` / `WarpCommunityCenter` / `Bookseller` / `Billboard` | | |

**要点：节日地图里的 `Action` 全是普通图块行为，没有一条是节日专用的新机制。**
唯一「节日专用」的是 `Shop shop` 这个**占位值**——`Event.cs:12391-12413` 会把它
**按节日 id 重写成**具体商店（`festival_fall27` → `Festival_SpiritsEve_Pierre`，等等）。

> 与 `custom-tile-actions.md` 的关系：那份文档统计的是**两语料**（RSV / 语料 B）。
> 本节补的是**原版节日地图实物**这一路，结论一致：`Action` 集不因「在节日地图里」而变。

### 4.4 约定层名与 tilesheet 名

| 字符串 | 谁在解析 | 依据 |
|---|---|---|
| 图层名 `Set-Up` | `loadActors Set-Up`；且**额外触发「带孩子」逻辑** | `Event.cs:3264-3330`、`3331` |
| 图层名 `MainEvent` / `Main-Event` | `loadActors MainEvent` / `loadActors Main-Event` | 同上（`winter8.json`） |
| 图层名 `Paths` | `loadPathsLayerObjectsInArea`（图块号 → 树/杂草/资源块） | `GameLocation.cs:17294`、`17309+` |
| tilesheet 名以 `fest` 开头 | 找蛋小游戏：把 `Paths` 层上 `fest` 图的格子变成可拾取道具 | `Event.cs:11602-11613`（`tile.TileSheet.Id.StartsWith("fest")`） |
| tilesheet 名 `characters` | actor 摆放（`TileIndex/4` 的载体） | 实测 `Maps/*.tbin` |

---

## 5. 节日脚本语言：**140 条指令（+1 别名）**

### 5.1 注册机制

```csharp
// Event.cs:4116-4142  SetupEventCommandsIfNeeded
MethodInfo[] methods = typeof(DefaultCommands).GetMethods(BindingFlags.Static | BindingFlags.Public);
foreach (MethodInfo m in methods) { Commands.Add(m.Name, ...); CommandNames.Add(m.Name); }
// 再读 [OtherNames] 特性注册别名
```

`DefaultCommands` 是 `Event.cs` 里从 **第 44 行** 开始的嵌套静态类（`Event.cs:44`），
命令名 = 方法名，**大小写不敏感**（`StringComparer.OrdinalIgnoreCase`，`Event.cs:3745`）。

派发：`Event.tryEventCommand`（`Event.cs:4230-4252`）→ `TryGetEventCommandHandler`（`Event.cs:4217-4224`）→
`TryResolveCommandName`（`Event.cs:4053-4066`）。未知命令 → `LogCommandErrorAndSkip`。

### 5.2 量化

| 指标 | 数量 |
|---|---|
| **注册指令数** | **140** |
| 别名数 | **1**（`mailReceived` → `AddMailReceived`，`Event.cs:2493`） |
| 原版节日数据实际用到的指令 | **37** |

### 5.3 全部 140 条（按 `Event.cs` 中定义顺序，括号内为行号）

```
IgnoreEventTileOffset(47)  Move(54)  Action(133)  Speak(155)  BeginSimultaneousCommand(201)
EndSimultaneousCommand(209)  MineDeath(216)  HospitalDeath(234)  ShowItemsLost(248)  End(257)
LocationSpecificCommand(263)  Unskippable(283)  Skippable(290)  SetSkipActions(297)  Emote(326)
StopMusic(359)  PlayPetSound(366)  PlaySound(406)  StopSound(420)  TossConcession(433)
Pause(474)  PrecisePause(487)  ResetVariable(511)  FaceDirection(518)  Warp(566)
WarpFarmers(622)  Speed(705)  StopAdvancedMoves(734)  DoAction(757)  RemoveTile(770)
TextAboveHead(782)  ShowFrame(801)  FarmerAnimation(856)  IgnoreMovementAnimation(869)  Animate(900)
StopAnimation(945)  ChangeLocation(998)  Halt(1014)  Message(1027)  AddCookingRecipe(1040)
ItemAboveHead(1052)  AddCraftingRecipe(1104)  HostMail(1116)  Mail(1132)  MailToday(1148)
Shake(1163)  TemporaryAnimatedSprite(1183)  TemporarySprite(1264)  RemoveTemporarySprites(1276)  Null(1284)
SpecificTemporarySprite(1289)  PlayMusic(1301)  MakeInvisible(1335)  AddObject(1370)  AddBigProp(1389)
AddFloorProp(1404)  AddProp(1410)  RemoveObject(1440)  Glow(1455)  StopGlowing(1467)
AddQuest(1475)  RemoveQuest(1487)  AddSpecialOrder(1499)  RemoveSpecialOrder(1511)  AddItem(1523)
AwardFestivalPrize(1539)  AttachCharacterToTempSprite(1781)  Fork(1796)  SwitchEvent(1868)  GlobalFade(1905)
GlobalFadeToClear(1926)  Cutscene(1947)  WaitForTempSprite(2084)  Cave(2097)  UpdateMinigame(2112)
StartJittering(2124)  Money(2131)  StopJittering(2147)  AddLantern(2154)  RustyKey(2170)
Swimming(2177)  StopSwimming(2208)  TutorialMenu(2239)  AnimalNaming(2248)  SplitSpeak(2267)
CatQuestion(2301)  AmbientLight(2312)  BgColor(2324)  ElliottBookTalk(2336)  RemoveItem(2348)
Friendship(2360)  SetRunning(2378)  ExtendSourceRect(2385)  WaitForOtherPlayers(2423)  RequestMovieEnd(2453)
RestoreStashedItem(2459)  AdvancedMove(2466)  StopRunning(2473)  Eyes(2480)  AddMailReceived(2494)
AddWorldState(2506)  Fade(2519)  ChangeMapTile(2542)  ChangeSprite(2570)  WaitForAllStationary(2598)
ProceedPosition(2631)  ChangePortrait(2665)  ChangeYSourceRectOffset(2693)  ChangeName(2713)  TranslateName(2733)
ReplaceWithClone(2753)  PlayFramesAhead(2783)  ShowKissFrame(2798)  AddTemporaryActor(2828)  ChangeToTemporaryMap(2885)
PositionOffset(2913)  Question(2950)  QuickQuestion(2971)  DrawOffset(2988)  HideShadow(3008)
AnimateHeight(3028)  Jump(3089)  FarmerEat(3123)  SpriteText(3136)  IgnoreCollisions(3168)
ScreenFlash(3198)  GrandpaCandles(3210)  GrandpaEvaluation2(3223)  GrandpaEvaluation(3244)  LoadActors(3264)
PlayerControl(3482)  RemoveSprite(3495)  Viewport(3508)  BroadcastEvent(3615)  AddConversationTopic(3634)
Dump(3651)  EventSeen(3677)  QuestionAnswered(3693)  GainSkill(3705)  MoveToSoup(3717)
```

### 5.4 脚本的「3 段头」——最容易踩的坑

带头的脚本（`set-up`、`secretSanta`）**前 3 段不是命令，是元数据**：

```csharp
// Event.cs:5031  InitializeEvent
ArgUtility.TryGet(eventCommands, 0, out musicId,         allowBlank: true,  "string musicId");
ArgUtility.TryGet(eventCommands, 1, out rawCameraPosition, allowBlank: false, "string rawCameraPosition");
ArgUtility.TryGet(eventCommands, 2, out rawCharacterPositions, allowBlank: false, "string rawCharacterPositions");
ArgUtility.TryGetOptional(eventCommands, 3, out rawOption, allowBlank: true, "string rawOption");
...
CurrentCommand = 3;   // Event.cs:5107
```

| 段 | 例（`spring13.set-up`） | 语义 |
|---|---|---|
| `[0]` | `fallFest` | 音乐 track id；`none` / `continue` 特殊 |
| `[1]` | `27 60` | 相机位置，或字面量 `follow` |
| `[2]` | `farmer 12 54 1` | **玩家起始位置**（不是命令！`farmer` 是这里的格式，不是指令） |
| `[3]` | `ignoreEventTileOffset` | 可选；同时也是个**真命令** `IgnoreEventTileOffset` |

**什么时候不需要头**：`Update` 里 `if (CurrentCommand == 0 && !forked)`（`Event.cs:4999`）
且 `num = (!eventSwitched) ? 1 : 0`（`Event.cs:5001`）。
- `fork` 会把 `forked = true`（`Event.cs:1864`）；
- `forceFestivalContinue` 把 `mainEvent` 装上时设 `eventSwitched = true`（`Event.cs:11707`）。

所以 `mainEvent` / `afterEggHunt` / `afterIceFishing` / `AbbyWin` / `governorReactionN` **都没有 3 段头**——
实测数据完全印证：它们的 `[0]` 直接就是 `pause 500` / `pause 100` 这类真命令。

### 5.5 原版节日脚本用到的 37 条

```
addTemporaryActor  advancedMove  animate  awardFestivalPrize  changeLocation
changeToTemporaryMap  cutscene  emote  end  extendSourceRect  eyes  faceDirection
fork  globalFade  globalFadeToClear  jump  loadActors  message  move  moveToSoup
null  pause  playerControl  playMusic  playSound  positionOffset  restoreStashedItem
shake  showFrame  speak  specificTemporarySprite  stopAnimation  textAboveHead
viewport  waitForOtherPlayers  warp  warpFarmers
```

`loadActors` / `playerControl` / `changeToTemporaryMap` / `warpFarmers` 是**节日主干的 4 条**。

---

## 6. 与 CP 的关系

### 6.1 CP `EditMap` 落在节日地图上

`EditMap.Target` = **地图资产名**（`Maps/Town-EggFestival`），`EditMap.FromFile` 可以是 `.tmx`。
改动落在 tbin 的**地图级属性 / 图块属性 / 图块内容**上，与本文的 `MapProperties`×15、
`Action`/`TouchAction`、`Set-Up`/`MainEvent` 图层完全同构——
**也就是说 `EditMap` 能改节日地图的每一条「字符串行为」，只要值本身对得上**：

| 改动 | 可行？ | 边界 |
|---|---|---|
| 改 `MapProperties`（含 `Spring_Objects` 这类键名即条件的） | ✅ | `{ "键": null }` 即删除 |
| 改/加 `AddWarps` / `AddNpcWarps` | ✅ | 只是往 `Warp` / `NPCWarp` 里追加字符串 |
| 改图块 `Action` / `TouchAction` | ✅ | 值仍是纯字符串 |
| 给 `Set-Up` / `MainEvent` 图层摆 actor | ✅ **但极易错** | 要自己算 `npcIndex*4 + facing`；且 `Main-Event` 这个名字必须**逐字符对上** |
| `MapTiles[].Remove` | ✅ | 空位即「不摆这个 NPC」 |
| **给节日加新脚本字段**（`mainEvent` / `fork` 目标） | ❌ **不在 `EditMap` 能力域** | 那是 `EditData` 打 `Data/Festivals/<id>` |
| 按条件改节日地图 | ❌ | 见 §5 与 `map-mechanisms-panorama.md`：图块内容没有条件位 |

**注意一个真实用例**：`tbin-block.md` §7 记录，RSV 有 **15 条 `EditMap` 用 `FromFile + PatchMode: Replace`
整图替换原版节日地图**——这正是「节日地图」这个目标最集中的一处用法。

### 6.2 转换器当前的支持边界（`D:\SdvCpTool\src`）

| 位置 | 现状 |
|---|---|
| `src/SdvCpTool.Core/Merging/WritePolicy.cs:25-28` | `IsWritableAction` **只有 `Load` / `EditData` / `EditImage`**；`EditMap` 明确排除，注释写「它要 tbin 合并（另一块）」 |
| `src/SdvCpTool.Core/Merging/WritePolicy.cs:22` | 明确写死：**通道 C 烘出的「条件为真」只降级为无条件，不会让 `EditMap` 变可写** |
| `src/SdvCpTool.Core/Judging/PatchJudge.cs:255-263` | `EditMap` + 有条件 → `Verdict.LocalEdit` 直接拒；无条件 → `ConvertedNoCondition`「直接改 tbin」**但下游不写** |
| `src/SdvCpTool.Core/Normalizing/VariantNormalizer.cs:222-223` | `EditMap` → `VariantCarrier.Map` |
| `src/SdvCpTool.Core/Assets/ConditionLanding.cs:20-21,32,47` | `DialogueQueryHost.Festivals` = `Data/Festivals/<id>` 里**以 NPC 名为键**的对话，是 `$query` 落点 |
| `src/SdvCpTool.Core/Merging/SpouseKeyRewriter.cs:26,42-47` | `<NPC>_spouse` 重写：`Data/Festivals/<id>` + `Relationship:<NPC> \|contains= married` |
| `src/SdvCpTool.Core/Assets/KeyEncodedVariants.cs:18` | `{key}_y{year}` 变体，注释直接引 `Event.cs:4417-4448` |

**结论：转换器对节日的支持目前全在「数据层」**——
`Data/Festivals` 的 NPC 对话键（`$query`、`_spouse`、`_yN`）。
**「节日地图」这一层（tbin）目前 0 覆盖**：能判、能报，但不写。

### 6.3 若要补 `EditMap`，节日地图的优先顺序（按风险升序）

1. **只改 `MapProperties` + `Action`/`TouchAction`** —— 纯属性，不动 `Index`/`_sizeArr`/`_currTileSheet`
   （`tbin-block.md` §4-① 警告：tbin 的 tilesheet 身份由 `T` 指令的**位置**决定，改结构极易写坏）。
2. `AddWarps` —— 纯字符串追加，无结构风险。
3. `MapTiles[].Remove` —— 需要写 `N` 运行长度，中等风险。
4. 摆/改 `Set-Up` / `MainEvent` actor —— 需要重算 `idx = npcIndex*4 + facing`，
   且必须保证层名逐字符匹配（`MainEvent` vs `Main-Event`）。
5. `FromFile + Replace` 整图替换 —— 需要 tmx→xTile.Map→tbin 全链路（`tbin-block.md` §6）。

---

## 7. 未确证 / 存疑

| 项 | 状态 |
|---|---|
| `startedMessage` / `locationDisplayName` 是否在 mod 生态里被广泛使用 | **语料 0 例（2026-10-10 量过）**：原版数据 0 例，本机全部 32 包里也**一次都没出现**（`.json`/`.jsonc`/`.tmx`/`.tbin`/`.txt` 都扫了）。⇒ **没有暴露面**，不必为它做兼容 |
| `Event.cs:12418-12448` 用 `array` 而非 `array2` 是否为原版 bug | **未确证**（该路径在当前数据下不触发） |
| `characters` tilesheet 上 35 个 NPC 之外是否还有第 36 格 | 实测节日地图 `idx` 最大 139 → **恰好 35 人**；无第 36 格证据 |
| `Main-Event`（连字符）与 `MainEvent` 是否两个都应支持 | 源码 `GetLayer(value)` 精确匹配 → **只有写对的那个生效**；`winter8` 写的是 `Main-Event` |
| `Data/Festivals` 是否还有本文未覆盖的键（mod 自定义键） | `TryGetFestivalDataForYear` 是**通用键查找**，任何键都能被 `fork`/`switchEvent` 拉起——**理论上无上限**，但原版只有 §2.2 那几组 |
| `D:\sdv_src` 是否为完整反编译 | 全库 `grep "class Festival"` **0 命中**，只有 `DesertFestival`；1.6 已把节日并入 `Event`（`Event.cs:3781` `public bool isFestival;`）。**若反编译有遗漏，§5 的 140 仍成立**（命令注册机制与 `Festival` 类无关） |
