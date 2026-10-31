# CP「状态类」令牌的设计意图 与 原版替代路径

日期：2026-10-05
调研对象：`NoEquivalent` 表列出的 8 个「原版做不到」令牌
源码依据：`D:\tmp\cp_decompile`（CP 2.9.1）、`D:\sdv_src` / `D:\sdv_gd`（星露谷 1.6）、`D:\rsv_src`

---

## 0. 一句话结论

**`NoEquivalent` 这张表的 8 个令牌里，至少 2 个判错了、1 个分意图一半可解。**
它是「**我们没做」被写成「原版没有**」的标本。

| 令牌 | 原判定 | **复核结论** | 语料用量 |
|---|---|---|---|
| `HasWalletItem` | ❌ 不可攻破 | ✅ **无损可替代**（11 项逐项映射） | 1（RSV） |
| `DailyLuck` | ❌ 不可攻破 | ⚠️ **分意图**：概率加成无损 / 数值比较不可 | 令牌 0 次；原生写法 16 次 |
| `HasActiveQuest` | ❌ 不可攻破 | ❌ 真不可（9 条机制穷尽） | **0** |
| `PlayerName` | ❌ 不可攻破 | ⚠️ **分场景**：NPC 对白无损 / 事件文本框不可 | 22（全是插值，条件 0） |
| `ChildNames` | ❌ 不可攻破 | ⚠️ 对话插值无损（`%kid1`）/ 身份条件不可 | **0** |
| `ChildGenders` | ❌ 不可攻破 | ✅ **意图已被原版满足** | 6（全是条件） |
| `HavingChild` | ❌ 不可攻破 | ❌ 真不可（孕期状态无 GSQ） | **0** |
| `Pregnant` | ❌ 不可攻破 | ❌ 真不可（同上，且 ≠ `HavingChild`） | **0** |

**这一族整体使用率极低**——8 个令牌在两个语料里合计 29 次用法，其中 22 次是 `PlayerName`。
**优先级应据此下调。**

---

## 1. `HasWalletItem` —— 判错了，无损可替代

### 关键事实：钱包位在存档里**没有独立存储**

它是从 `mailReceived` / `eventsSeen` **派生**出来的（`TokenSaveReader.cs:314-364` 的 11 个 getter）。
而 GSQ 正好直接查那些底层存储：

```csharp
// GameStateQuery.cs:1154-1177  PLAYER_HAS_MAIL
"received" => target.mailReceived.Contains(mailId),   // ← 与 CP 的 getter 完全同构
```

### 逐项映射（11/11）

| CP `HasWalletItem` | GSQ |
|---|---|
| `BearsKnowledge` | `PLAYER_HAS_SEEN_EVENT Current 2120303` |
| `ClubCard` | `PLAYER_HAS_MAIL Current HasClubCard Received` |
| `DarkTalisman` | `PLAYER_HAS_MAIL Current HasDarkTalisman Received` |
| `DwarvishTranslationGuide` | `PLAYER_HAS_MAIL Host HasDwarvishTranslationGuide Received` |
| `KeyToTheTown` | `PLAYER_HAS_TOWN_KEY Current`（**有专用关键字**）|
| `MagicInk` | `PLAYER_HAS_MAIL Current HasMagicInk Received` |
| `MagnifyingGlass` | `PLAYER_HAS_MAIL Current HasMagnifyingGlass Received` |
| `RustyKey` | `PLAYER_HAS_MAIL Host HasRustyKey Received` |
| `SkullKey` | `PLAYER_HAS_MAIL Host HasSkullKey Received` |
| `SpecialCharm` | `PLAYER_HAS_MAIL Current HasSpecialCharm Received` |
| `SpringOnionMastery` | `PLAYER_HAS_SEEN_EVENT Current 3910979` |

**注意**：`PLAYER_HAS_MAIL` 第 3 参缺省是 `any`（`hasOrWillReceiveMail`），**必须显式写 `Received`**
才与 CP 逐位相等。`DwarvishTranslationGuide` / `RustyKey` / `SkullKey` 查的是**主机**的 mail。

### 判错的原因（值得记住）

`ConditionVocabulary.cs:40` 的注释自己写着「无通用钱包关键字；**仅 KeyToTheTown 例外**」——
**而 GSQ 就有 `PLAYER_HAS_TOWN_KEY`**。作者察觉到了例外，却因为「没有通用关键字」就整项判死，
没往下追一层看**每个具体值**能不能映射。这与 `Random` 误判是同一类错误：**看名字下结论，没看实现**。

---

## 2. `DailyLuck` —— 实践中根本不是阻塞项

### 真实情况：语料已经全用原生写法

```csharp
// GameStateQuery.cs:196-215  RandomImpl
if (query[i].EqualsIgnoreCase("@addDailyLuck"))
    value += (float)Game1.player.DailyLuck;      // 幸运直接加到概率上
return random.NextDouble() < value;
```

**RSV 自己就在用，16 处**（`TrashCanData.json` 15 处 + `Shops.json` 1 处），
写法 `"Condition": "RANDOM 0.1 @addDailyLuck"`。
而 CP 的 `{{DailyLuck}}` 令牌在两个语料里 **0 次**。

### 分意图结论

- **概率加成意图 → 无损**：用 `RANDOM <chance> @addDailyLuck`；需「按天稳定」时用
  `SYNCED_RANDOM <interval> <key> <chance> @addDailyLuck`。
- **数值比较/插值意图 → 不可**（已穷尽）：`WORLD_STATE_FIELD` 读不到（`sharedDailyLuck` 在
  `FarmerTeam`，不在 `NetWorldState`）；`PLAYER_STAT` 无 luck 键；
  `PLAYER_LUCK_LEVEL` 是**幸运技能等级**，与每日幸运无关。

`Farmer.DailyLuck`（`Farmer.cs:1618`）= `clamp(team.sharedDailyLuck + charm, -0.2, 0.2)`，
是**队伍共享**的，每天由主机重掷（`Game1.cs:7935`）。

---

## 3. `HasActiveQuest` —— 真不可替代（已穷尽）

### 根因：任务状态没有镜像到任何 GSQ 可读的存储

```csharp
// Farmer.cs:7993-8014  addQuest
// 只做 questLog.Add(...) + HUD 提示 —— 不写 mail、不写 worldStateID、不写 modData、不写 stat
```

### 穷尽的 9 条机制

1. GSQ 115 关键字：**无任何 quest 关键字**（全文搜 `quest` 只命中对话应答）
2. 原版数据字段：无任何字段承载 `questLog`
3. 任务启动无副作用镜像（见上）
4. `WORLD_STATE_FIELD`：有 `QuestOfTheDay` 但那是**每日公告板任务**，非 `questLog`；且只处理 bool/int/string
5. `PLAYER_STAT`：只有聚合计数 `BillboardQuestsDone` / `questsCompleted`，**无逐任务键**
6. `PLAYER_MOD_DATA`：`Quest` 有自己的 modData 但 GSQ 够不到；CP 也写不进去
7. `Quest.questComplete()` 会 `autoGenerateActiveDialogueEvent`，可用 `PLAYER_HAS_CONVERSATION_TOPIC` 查——
   但那是**完成**不是**进行中**，且**4 天后消失**，不是持久标记
8. `PLAYER_HAS_RUN_TRIGGER_ACTION`：仅当任务恰经**已标记**的 `Data/TriggerActions` 条目添加时可用，非通用
9. **特殊订单是另一套系统**：`PLAYER_SPECIAL_ORDER_ACTIVE` 查的是 `FarmerTeam.SpecialOrders`，**不是 questLog**

### 近似方案（有偏差）

若任务由事件 X 触发 → `PLAYER_HAS_SEEN_EVENT <p> X`。**偏差**：事件看过即**永久为真**，
是「进行中」的**超集**（任务完成后仍匹配）。

---

## 4. `PlayerName` / `ChildNames` / `ChildGenders` —— **第三条替代路径：对话占位符**

### 原版自己有一套对话占位符（`Dialogue.cs:1097-1135`）

```csharp
str = str.Replace("@", text);                    // 玩家名
str = str.Replace("%firstnameletter", ...);
str = str.Replace("%spouse", ...);               // 配偶名
str = str.Replace("%farm", ...);  str = str.Replace("%favorite", ...);
str = str.Replace("%kid1", ...); str = str.Replace("%kid2", ...);   // 孩子名
str = str.Replace("%pet", ...);
```

`Utility.FilterUserName` 在 1.6 是**恒等函数**（`Utility.cs:7775-7778`）
→ **`@` 与 CP 的 `{{PlayerName}}` 逐字节等价**。

### 但只覆盖 1/6 的真实用法

`ReplacePlayerEnteredStrings` **只对 `Dialogue` 类对象执行**：

| 位置 | 显示路径 | `@` 生效？ |
|---|---|---|
| `Characters/Dialogue/*`、事件 `speak` | `DialogueBox(Dialogue)` → 替换 | ✅ |
| 事件 **`message`** | `DialogueBox(string)` → **不替换** | ❌ |
| 事件 **`textAboveHead`** | `showTextAboveHead` → 只做性别开关 | ❌ |
| 事件 **`question`** | `drawObjectQuestionDialogue` → **不替换** | ❌ |

实测 6 处真实用法里 **5 处落在 ❌**。所以「`PlayerName` 可无损替代」**对多数用法不成立**。

### `ChildGenders` 的意图原版早就满足了

```csharp
// StardewValley.Characters\Child.cs:113
Sprite.textureName.Value = "Characters\\Toddler" + ((Gender == Gender.Male) ? "" : "_girl") + (darkSkinned.Value ? "_dark" : "");
```

**游戏自己就按性别选幼儿贴图。** 那个 mod 想做的事，原版零配置已做到。
若 mod 想换这几张贴图，直接覆盖 `Characters/Toddler` / `Toddler_girl` / `Toddler_dark` / `Toddler_girl_dark` 四个资产即可。

---

## 5. `HavingChild` / `Pregnant` —— 真不可，且**两者不是同一状态**

### `Pregnant` ≠ `HavingChild`

`HavingChildValueProvider.cs:18-86`：

- `HavingChild` = 「要添孩子了（**含领养**）」，集合含**夫妻双方**名字
- `Pregnant` = 排除领养、**只保留怀孕的那位（女方）**

### 原版确实没有可查的孕期状态

- **「有孩子」** = `FarmHouse.getChildrenCount()`（**已出生**）→ GSQ `PLAYER_HAS_CHILDREN` 可查
- **「怀孕中」** = `%spouse` 的 `Friendship.NextBirthingDate` 已设且在未来 → **GSQ 完全查不到**

原版**有**孕期对白，但**完全硬编码**（`NPC.cs:6522-6567`）：产期前 7 天、区分领养/女方/男方三套。
`Data/Characters` 只有 `SpouseAdopts`（是否领养）与 `SpouseWantsChildren`（是否来问要不要孩子）
两个 GSQ 字段，**都不表达「当前是否孕期」**。

---

## 6. 通用「逃生舱」全部解剖（据我们早先的猜测逐个证伪）

| 关键字 | 实际能读什么 | 结论 |
|---|---|---|
| `PLAYER_STAT` | Farmer 的**整数统计**（`stats.Get`） | 无 quest 逐项键、无 luck 键 |
| `PLAYER_MOD_DATA` | Farmer 的**字符串 modData**，精确相等 | CP 无法写入；且目标状态不在此 |
| `WORLD_STATE_FIELD` | `NetWorldState` 的**公有属性**，仅 bool/int/string | `sharedDailyLuck` 在 `FarmerTeam`；`QuestOfTheDay` 是 Quest 对象 |
| `LOCATION_HAS_CUSTOM_FIELD` | 地点的 `CustomFields` | 与玩家状态无关 |
| `PLAYER_HAS_RUN_TRIGGER_ACTION` | `triggerActionsRun`（存 TriggerActionData.Id） | 仅特定条件下可用，非通用 |

**它们的语义边界很清楚，别再把它们当万能钥匙。**

---

## 7. 对工具的直接影响

### 7.1 `TokenAction` 必须能表达**分意图 / 分场景**

`DailyLuck` 同一令牌在「概率加成」与「数值比较」两种意图下结论相反；
`PlayerName` 同一令牌在「NPC 对白」与「事件文本框」两种场景下结论相反。

**单一分类表达不了这个。** 判定必须**按用途分别做**——这正是这条调研最重要的方法论产出。

### 7.2 必须先看「令牌落在哪个字段/命令」

同一条文本走不同显示路径，行为完全不同（`@` 在 `Dialogue` 里生效、在事件 `message` 里不生效）。
**只按令牌名判定必然出错。**

### 7.3 语料样本

**必须用两个语料**，不能只拿 RSV：

- 语料 A：`D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods\`（14 个顶层目录）
- 语料 B：`D:\Steam\steamapps\common\Stardew Valley\Mods\`（**96 个顶层目录 / 1413 个 json / 38 个 content.json**）

本次「这一族整体用量极低」这一结论，**只有用两个语料才能得出**——
只看 RSV 会把它当成重要战场。

---

## 8. 证据索引

**CP（`D:\tmp\cp_decompile`）**
- `TokenManager.cs:152, 167, 169, 189, 196, 197, 208, 209`
- `TokenSaveReader.cs:67-70, 314-364, 366-377, 379-405, 424-459`
- `Conditions\Condition.cs:77`；`Conditions\ConditionType.cs`
- `Tokens\Token.cs:78-97, 125-188`；`InternalConstants.cs:14`
- `Tokens.ValueProviders.Players\PerPlayerValueProvider.cs:97-177`
- `Tokens.ValueProviders\HavingChildValueProvider.cs:18-86`
- `Migrations\Migration_1_5/1_6/1_10/1_15_Rewrites/1_22/1_23/1_24/1_25.cs`

**星露谷（`D:\sdv_src`、`D:\sdv_gd`）**
- `GameStateQuery.cs:99-142`（WithPlayer）、`196-215`（RandomImpl `@addDailyLuck`）、
  `814-851`（WORLD_STATE_FIELD）、`1154-1177`（PLAYER_HAS_MAIL）、`1190-1197`、`1200-1227`、
  `1305-1312`（MOD_DATA）、`1348-1375`（SPECIAL_ORDER_*）、`1420-1429`（PLAYER_STAT）、
  `1469-1480`（PLAYER_HAS_CHILDREN）、`1676-1707`（RANDOM/SYNCED_*）
- `Dialogue.cs:1097-1135`（占位符）；`Utility.cs:7775-7778`（FilterUserName 恒等）
- `Menus\DialogueBox.cs:101-113, 140-165`；`Event.cs:155-196, 782-799, 1027-1037, 2950-2967`
- `Characters\Child.cs:48, 54-61, 85-138`（性别贴图）
- `Farmer.cs:199, 1618, 3675, 3914-3921, 7993-8042`；`FarmerTeam.cs:216`；`Friendship.cs:118-128`
- `NPC.cs:1372-1382, 6306-6320, 6522-6590`
- `GameData.Locations\SpawnFishData.cs:51-53`（`ApplyDailyLuck`）
- `Netcode\NetWorldState.cs:160-161`；`Constants\StatKeys.cs`

**真实语料**
- 语料 A：RSV `[CP] Ridgeside Village\Data\DynamicChanges\MapChanges.json:294-307`（`HasWalletItem: RustyKey`）、
  `Data\Extras\TrashCanData.json`（`RANDOM 0.1 @addDailyLuck` ×15）、`Data\Shops.json:2075`、
  `Data\Events\Events.json:1629`（`message` + `{{PlayerName}}`）
- 语料 B：`人物对话---孩子剧情--------------18972\content.json:52,59,65,70,75,81`（`ChildGenders |valueAt=`）；
  `…身临其境 谢恩…\assets\Events.json:67,85,109,112`（`textAboveHead`/`question` + `{{PlayerName}}`）；
  `…鸟类观察协会…\assets\npc\Kyle.json:193`（`{{uppercase: {{PlayerName}}}}`）
