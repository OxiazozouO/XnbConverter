# CP `When` 令牌 → GSQ 关键字 对照表

日期：2026-10-05
用途：M2 翻译器的权威依据。
出处：CP 源码 `Framework/Conditions/ConditionType.cs`、`Framework/TokenManager.cs`、`Framework/TokenSaveReader.cs`、
`Framework/Constants/{PlayerType,Profession}.cs`、各 `Framework/Tokens/ValueProviders/*.cs`、官方文档 `docs/author-guide/tokens.md`；
游戏侧 `D:\sdv_src\StardewValley\GameStateQuery.cs`、`Farmer.cs`、`Game1.cs`。真实用例取自 Ridgeside Village。

---

## 一、三条会导致**静默错误**的规则（最重要）

这三条错了不会抛异常，只会产出语义错误的 GSQ 字符串。翻译器必须显式编码。

⚠ 真实样本对账又抓到**同类**的第四条：`|contains=` 的声明值 `false` 是取反，当成占位符会静默翻反 —— 见 §6.1。

### 规则 1：CP 值侧的逗号是"或"，GSQ 顶层逗号是"与"

GSQ 按逗号切分 query，各段之间全 AND（`GameStateQuery.cs:2200-2205, 2211-2248`）。
而**同一个关键字内、空格分隔的多个实参通常是"或"**（`Helpers.AnyArgMatches`，`GameStateQuery.cs:144-164`）。

```
CP:  "Season": "spring, summer"          （或）
对:  SEASON spring summer
错:  SEASON spring, SEASON summer         （变成"既是春又是夏"，恒假）
```

只有一个实参的关键字（如 `FARM_NAME`）无法用空格表达"或"，必须用 `ANY "q1" "q2"`。

### 规则 2：所有 `PLAYER_*` 关键字的玩家键是**必填位置参数**

漏写会把第一个数据值误当玩家键并**静默返回 false**。

```
对:  PLAYER_GENDER Current Female
错:  PLAYER_GENDER Female
```

CP 键侧 → GSQ 实参的映射（`GSQ Helpers.WithPlayer`，`GameStateQuery.cs:97-142`）：

| CP | GSQ |
|---|---|
| 无（默认） | `Current` |
| `currentPlayer` | `Current` |
| `hostPlayer` | `Host` |
| `anyPlayer` | `Any` |
| 数字玩家 ID | 数字 ID |
| （CP 无） | `All`、`Target` |

⚠ **CP 的 player key 不是 `Current`/`Host`/`Any`**，而是 `currentPlayer`/`hostPlayer`/`anyPlayer`（`Framework/Constants/PlayerType.cs:4-14`）。CP **没有 `All`**。
⚠ CP 允许"多玩家键的并集"，GSQ 不支持 —— 只能近似为 `Any`，或用 `ANY` 包两条 query。

### 规则 3：数值型关键字是**闭区间**，CP 的集合相等必须补第二个 `max`

```
对:  PLAYER_HEARTS Current Abigail 10 10       （== 10）
错:  PLAYER_HEARTS Current Abigail 10          （>= 10，语义不同）
```

涉及：`YEAR`、`DAYS_PLAYED`、`TIME`、`PLAYER_HEARTS`、`PLAYER_*_LEVEL`、`PLAYER_FARMHOUSE_UPGRADE`。

---

## 二、对照表

| CP 令牌 | GSQ 关键字 | 参数重排 | 陷阱 |
|---|---|---|---|
| `Day` | `DAY_OF_MONTH` | 每值→空格实参（或） | GSQ 额外支持 `even`/`odd` |
| `DayEvent` | `IS_FESTIVAL_DAY`（**仅布尔近似**） | 无 | 无法匹配节日名；`wedding` 无关键字 |
| `DayOfWeek` | `DAY_OF_WEEK` | 每值→实参 | — |
| `DaysPlayed` | `DAYS_PLAYED` | 单值→`min max` | 区间语义，非集合 |
| `Season` | `SEASON` | 每值→实参 | — |
| `Time` | `TIME` | 时间转整数 | 前导零 `0600` 要写 `600` |
| `Weather` | `WEATHER` | **先插地点参数**（`Here`），再列值 | 第一参必须是地点名，不能是 CP 的 `island`/`valley` 上下文 |
| `Year` | `YEAR` | 单值→`min max` | 区间语义 |
| `FarmhouseUpgrade` | `PLAYER_FARMHOUSE_UPGRADE` | `P min max` | — |
| `HasCaughtFish` | `PLAYER_HAS_CAUGHT_FISH` | `P` + 每ID→实参 | — |
| `HasConversationTopic` | `PLAYER_HAS_CONVERSATION_TOPIC` | `P` + 每话题→实参 | — |
| `HasCookingRecipe` | `PLAYER_HAS_COOKING_RECIPE` | `P` + 单个整串（剩余） | 单值；多值须 `ANY` |
| `HasCraftingRecipe` | `PLAYER_HAS_CRAFTING_RECIPE` | 同上 | 同上 |
| `HasDialogueAnswer` | `PLAYER_HAS_DIALOGUE_ANSWER` | `P` + 每ID→实参 | — |
| `HasFlag` | `PLAYER_HAS_MAIL ... Any` 或 `WORLD_STATE_ID` | `P id Any`；世界状态另起一条 | CP 把 mail 与世界状态 ID 混在一个集合；GSQ 分两类 |
| `HasProfession` | `PLAYER_HAS_PROFESSION` | `P` + **整数ID** | 必须用 CP 的 `Profession` 枚举转 ID，有 3 处改名陷阱（Mariner↔baitmaster、Luremaster↔mariner、Prospector↔burrower） |
| `HasReadLetter` | `PLAYER_HAS_MAIL`（`Received`） | `P id Received` | — |
| `HasSeenEvent` | `PLAYER_HAS_SEEN_EVENT` | `P` + 每ID→实参 | — |
| `HasVisitedLocation` | `PLAYER_VISITED_LOCATION` | `P` + 每地点→实参 | — |
| `HasWalletItem` | 仅 `KeyToTheTown` → `PLAYER_HAS_TOWN_KEY` | 无参 | 无通用钱包关键字 |
| `IsMainPlayer` | `IS_HOST`（**近似**） | 无参 | 无玩家参数；CP 带 player key 时表达不了 |
| `IsOutdoors` | `LOCATION_IS_OUTDOORS` | 地点参数，当前→`Here` | 按地点不按玩家 |
| `LocationContext` | `PLAYER_LOCATION_CONTEXT` | `P` + 每上下文→实参 | — |
| `LocationName` | `PLAYER_LOCATION_NAME` | `P` + 每名称→实参 | — |
| `LocationUniqueName` | `PLAYER_LOCATION_UNIQUE_NAME` | `P` + 每名称→实参 | — |
| `PlayerGender` | `PLAYER_GENDER` | `P` + `Male`/`Female` | GSQ 里非 `Male` 一律当 Female |
| `PreferredPet` | `PLAYER_PREFERRED_PET` | `Current` + `Cat`/`Dog` | CP 无玩家参数 |
| `SkillLevel` | `PLAYER_<SKILL>_LEVEL` | 技能名决定**关键字**；`P min max` | 必须用**非 BASE** 版（CP 取含 buff 的等级） |
| `Hearts` | `PLAYER_HEARTS` | `Current` + NPC名 + `min max` | NPC 名是位置参数（非键侧） |
| `Relationship` | `PLAYER_NPC_RELATIONSHIP` | `Current` + NPC名 + 类型（可多=或） | ①`Unmet` 无对应 → 用 `!PLAYER_HAS_MET`；②CP `Married` **含**室友婚、GSQ `Married` **排除** → 须补 `Roommate`；③配偶像为玩家时换 `PLAYER_PLAYER_RELATIONSHIP` |
| `Roommate` | `PLAYER_NPC_RELATIONSHIP`（`Roommate`） | `Current` + 名 + `Roommate` | CP 返回名字集合；"任意室友"→`... Any Roommate` |
| `Spouse` | `PLAYER_NPC_RELATIONSHIP`（`Married`） | `Current` + 名 + `Married` | 配偶为另一玩家时换 `PLAYER_PLAYER_RELATIONSHIP` |
| `FarmCave` | `FARM_CAVE` | 每值→实参 | GSQ 用主玩家 caveChoice |
| `FarmName` | `FARM_NAME` | 单个整串 | 单值；多值须 `ANY` |
| `FarmType` | `FARM_TYPE` | 每值→实参 | 同时匹配数字 ID 或 key |
| `IsCommunityCenterComplete` | `IS_COMMUNITY_CENTER_COMPLETE` | 无参 | GSQ 额外要求**无** `JojaMember`，边界情形与 CP 不一致 |
| `IsJojaMartComplete` | `IS_JOJA_MART_COMPLETE` | 无参 | 基本等价但非逐位相同 |

## 三、**没有 GSQ 等价物**的令牌

| CP 令牌 | 原因 |
|---|---|
| `DailyLuck` | 小数日运；GSQ 无比较关键字（`RANDOM @addDailyLuck` 只是概率加成） |
| `HasActiveQuest` | GSQ 完全没有任务关键字 |
| `PlayerName` | GSQ 无玩家名关键字（只有 `FARM_NAME`） |
| `ChildNames` | GSQ 只有 `PLAYER_HAS_CHILDREN` 计数，无名字 |
| `ChildGenders` | 同上，无性别 |
| `HavingChild` | 需遍历所有玩家读 `NextBirthingDate`，GSQ 无此能力 |
| `Pregnant` | 同上 |
| `HasWalletItem`（通用） | 无钱包关键字；其余物品底层是 mail/事件标记 |
| `DayEvent`（按名称） | `IS_FESTIVAL_DAY` 只能答"今天是不是节日" |

**根因**：GSQ 原生关键字全是"检查当前上下文/单玩家/单地点是否为真"的布尔解析器，**没有返回集合或遍历实体的能力**。凡 CP 里"返回多值并做成员/计数/遍历"的令牌都翻译不了。要覆盖只能给目标 mod 写自定义 GSQ 关键字（`GameStateQuery.Register`），或在翻译器里明确报不可转换。

## 四、只能**近似**、不要当精确映射

1. `DayEvent` → 只有 `IS_FESTIVAL_DAY` 布尔近似；被动节日另属 `IS_PASSIVE_FESTIVAL_TODAY`
2. `Weather` 带 `island`/`valley` 上下文 → GSQ 第一参只认 `Here`/`Target`/真实地点名
3. `IsMainPlayer` → `IS_HOST` 是"本机是否主机"；CP 是逐玩家标志
4. `IsOutdoors` 带 player key → 无法表达
5. `HasFlag` 的世界状态 ID 分支 → 等价性未逐条核对
6. `IsCommunityCenterComplete` → GSQ 多一个 `!JojaMember`
7. `Relationship` 的 `Unmet` 与"配偶像为玩家"
8. `Spouse`/`Roommate` 当配偶是另一名玩家
9. `HasProfession` 的 name→id（须用 CP 的枚举）
10. `RandomNumber` 一类 pack 自定义动态令牌 → `SYNCED_CHOICE` 是最近似的，但随机模型不同，不能自动翻译

## 五、对 M1 分类表的订正

本表推翻了 `ConditionVocabulary.Translatable` 里的若干条目：

| 问题 | 条目 | 订正 |
|---|---|---|
| 拼写错误 | `IsJoMartComplete` | → `IsJojaMartComplete` |
| 无等价物却被列为可翻译 | `DailyLuck`、`HasActiveQuest`、`PlayerName`、`ChildNames`、`ChildGenders`、`HavingChild`、`Pregnant`、`HasWalletItem` | → 移出 `Translatable` |
| 只能近似 | `DayEvent`、`IsMainPlayer`、`IsOutdoors`（带 player key 时） | → 新增 `GsqApproximate` 类 |
| 非 CP 内置令牌 | `RandomNumber` | → 移出 `Translatable`（它是 pack 用 `DynamicTokens` 自建的） |
| 分类表遗漏 | `FarmMapAsset`、`Language`、`LocationOwnerId`、`Random`、`IsJojaMartComplete` | → 待定，先归 `Unknown` |

**注**：`RandomNumber` 是 Ridgeside 在 `content.json` 的 `DynamicTokens` 里定义的（`content.json:286-287`），
不是 CP 内置也不是 C# 注册的。

> ⚠ **订正（2026-10-10）**：这句下面原写「本工具目前**不读 `DynamicTokens`** —— M2 的一个待补项」，
> **已经过时**：`src/SdvCpTool.Core/Cp/DynamicTokenResolver.cs` 实现了它，
> SVE 跑起来会报「内容包 DynamicTokens：17 个」。**这条不是待办**。

## 六、真实样本对账补记（2026-10-05，M2 Task 6）

拿 Ridgeside Village 2.5.17 的 856 条带 `When` 补丁对账时，发现三条本表漏写、且直接让翻译器出错的
事实。详细统计见 `docs/verification/2026-10-05-translator-real-samples.md`。

### 6.1 `|contains=` 的声明值是**取反开关**（最严重）

`|contains=x, y` 是全局具名参数：它问"令牌值集合与 x/y 有没有交集"，**返回 `true`/`false`**；
`When` 里的值就是拿这个布尔去比。所以

```jsonc
{ "Year |contains=1": "true"  }   // 是第 1 年
{ "Year |contains=1": "false" }   // NOT year 1 —— 取反，不是"占位值"
```

依据：CP 源码 `Framework/Tokens/Token.cs:209` `ApplyContains` 返回 `InvariantSets.FromValue(found)`，
`Framework/Conditions/Condition.cs:110` 用声明值与该结果比交集；`Token.cs:112-124` 的
`TryValidateValues` 明确要求"用了 `contains` 时声明值只能是 true/false"（否则 CP 直接报错拒绝内容包）。
官方文档 `docs/author-guide/tokens.md` §contains 的示例里就写着 `"Year |contains=1": "false"`，
注释是 `// NOT year 1`。

**真实数据**：Ridgeside 的 856 条带 `When` 补丁里 `|contains=` 出现 589 次，其中 **126 处声明值是
`false`**（46 条补丁最终翻译成功）。把 `false` 当占位符会**静默翻出取反的语义** —— 正是 §一 警告
的那类错误。翻译器必须对整条 query 加 GSQ 的 `!` 前缀
（`GameStateQuery.cs:2166` 解析 `!`，`2237` 处生效）。

### 6.2 CP 条件名**大小写不敏感**

CP 用 `Enum.TryParse<ConditionType>(name, ignoreCase: true)` 解析条件名
（`Framework/ConfigFileHandler.cs:108`、`Framework/ScreenManager.cs:280,336`），
所以真实数据里的 `FarmHouseUpgrade`（4 处）与 §二 写的 `FarmhouseUpgrade` 是同一个令牌。
翻译器与 `ConditionVocabulary` 都必须按**大小写不敏感**匹配，否则真实补丁会被误判成"没有翻译规则"。

### 6.3 `Relationship` 的合法类型里有 `Friendly`

CP 的 Relationship 令牌直接返回 `friendship.Status.ToString()`
（`Framework/Tokens/ValueProviders/VillagerRelationshipValueProvider.cs:44`），而 SDV 的
`FriendshipStatus` 枚举是 `Friendly, Dating, Engaged, Married, Divorced`；GSQ 的
`PLAYER_NPC_RELATIONSHIP` 同样接受 `Friendly`（`GameStateQuery.cs:1527`）。
§二 只列了 `Married`/`Roommate`/`Dating`/`Engaged`/`Divorced`/`Unmet`，**漏了 `Friendly`**，
真实数据 `{"Relationship:Alissa": "Friendly"}` 因此被判成"没有对应值"。

### 6.4 未被真实数据触达的部分

`HasProfession` 在本内容包（以及本机全部已装 mod）的 `When` 里出现 **0 次**，§二/§四 记的
"职业名 → 整数 ID 映射表缺失"这个缺口没有被真实数据走到，仍属该失败。
