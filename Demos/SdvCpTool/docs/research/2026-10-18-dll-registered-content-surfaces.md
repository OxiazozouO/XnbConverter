# 纯 XNB 下「DLL 才认识的字符串指令」全量调查

日期：2026-10-18

起因：Grandpa's Farm 的农场出口写成 `TouchAction = LoadMap Forest 69 2`。`LoadMap` 不是原版动作，
是 TMXL Map Toolkit / PyTK 注册的（`TMXLoaderMod.cs:1740`；SVE 在没装 TMXL 时自己兜底，
`TouchActionProperties.cs:47`）⇒ 纯 XNB 装机（无 SMAPI、无任何 DLL）里踩上去**什么都不发生**。
由此把**所有**「注册表式字符串扩展点」扫了一遍：地图动作、事件命令、事件前置条件、GSQ、
TriggerActions、ItemQueryResolver。

> 口径：三个并行调查代理分别扫描，脚本与原始输出在
> `D:/tmp/research-tbin/`、`D:/tmp/research-events/`、`D:/tmp/research-dll/`。
> 数字是**文本出现次数**，不等于运行时执行次数（分支/条件/触发器可能不满足）。

## 0. 未知键在原版里的行为（决定"静默还是报错"）

| 未知类型 | 行为 | 源码 |
|---|---|---|
| 未知 `Action`（图块） | `performAction` 的 `default: return false;` —— **静默** | `GameLocation.cs:10175` |
| 未知 `TouchAction`（图块） | `switch` 无 default，外层 `catch` 为空 —— **静默** | `GameLocation.cs:3605-4012` |
| 未知事件命令 | 记 Error 后 `CurrentCommand++` —— **跳过该条、事件继续** | `Event.cs:4238-4241`、`:4944-4952` |
| 未知事件前置条件 | 记 Warning 并 **返回 false**，且 **`!` 不生效**（`!未知` 仍是 false） | `Event.cs:4197-4200` |
| 未知 GSQ 关键字 | 记 Error 并返回 false，**取反也不生效** | `GameStateQuery.cs:2184-2190`、`:2222-2224` |
| 未知 TriggerAction 动作 | 记 Error 后跳过该动作（同条目其它合法动作仍执行） | `TriggerActionManager.cs:715-738` |
| 未知 Trigger 类型 | 记 Error，整条**永不触发** | `TriggerActionManager.cs:743-747` |
| 未知 ItemQueryResolver 键 | 退化成 `ItemRegistry.Create(query)` ⇒ 玩家可见的 **Error Item** | `ItemQueryResolver.cs:743,774-786` |

## 1. 地图图块动作（tbin/tmx）

- 语料：Mods 源 **2576** 个地图文件（2000 tmx + 576 tbin）、产出树 **1246** 个 tbin、原版 **259** 个 tbin。
- 产出树里 **207/1172** 张地图含至少一个非原版动作（共 983 个动作项）；其中 **795** 个能指到已安装 DLL 的注册键。
- 最严重的传送类：

| 动作 | 键 | 语料（tree） | 提供者 | 原版等价 |
|---|---|---:|---|---|
| `LoadMap <loc> <x> <y> [facing]` | TouchAction | **193 处 / 33 张** | TMXL/PyTK（未安装） | `Warp <loc> <x> <y>`（丢 facing；TMXL/SVE 兜底都是立即传送） |
| `RSVTeleport <loc> <x> <y>` | Action | 53 处 / 3 张 | RidgesideVillage | `Warp <x> <y> <loc>`（**Action 版顺序相反**） |
| `SBVSMAPI_MineLadder <x> <y> <loc>` | Action | 19 处 / 19 张 | SunberryVillage | `Warp <x> <y> <loc>`（顺序一致） |
| SpaceCore 地牢（入口/电梯/梯子） | Action | 91 处 / 38 张 | SpaceCore | **无**（运行时地牢） |
| Sunberry `ChooseDestination` | Action | 16 处 | SunberryVillage | 无（菜单） |
| Downtown Zuzu 电梯/售票等 | Action | 30 处 | Downtown_Zuzu | 无 |
| Cape 宝珠传送 | TouchAction | 22 处 | CapeStardewCode（Harmony） | 无（带持有物条件） |
| `SVE_PlaySound` | TouchAction | 239 处 / 5 张 | SVE（Harmony 轮询） | 无 TouchAction 版；改 `Action playSound` 会改成"点击" |

**已落地**（见 `verification/2026-10-18-dll-tile-actions-and-gate-direction.md`）：
- `LoadMap` → `Warp`（193 处）；`RSVTeleport` → `ObeliskWarp`（53 处）；
  `MineLadder` → `Warp`（19 处）；BETAS 的 `PlaySound` 代理 → `playSound`。
- `PoolExit` → `PoolEntrance`（90 处；游泳中踩上去就是出水）。
- `RSVShop` → `OpenShop`（RSV 的 `Data/Shops` 已有这些 ID）；`ZuzuBarber` → `WizardShrine`。
- `Say` → `Dialogue`（TMXL 的 Say 是原始文本，不是翻译 key）。
- 大小写笔误：`playsound`/`PlaySound`（Action）→ `playSound`；`ChangeIntoSwimSuit`/`ChangeOutOfSwimSuit` → 原版拼写。

**刻意不替换**：
- `SVE_PlaySound`（239 处）：唯一能借的 `PlayEvent` 会启动事件（淡入淡出、锁玩家、隐藏 HUD），
  走过即触发会打断移动；保留为"没有环境音"，比每走一步闪屏好。
- `TownMailbox`→`Mailbox`、`Sleep2`→`Sleep`、`MakeoverBox`→`DesertMakeover`：
  **原版地图里就有这些死值**（如 `Town.tbin` 25 格 `TownMailbox`），改它们等于改原版行为。

**已做 / 未做**（需要生成 `Data/Events` 条目）：
- ✅ `CapeOrbTouchAction`（2026-10-27 已实现）：`CapeOrbBridge` 生成
  `PlayEvent` + `HasItem (O)OrbOfTheTides` + 事件脚本
  `playSound wand/warp farmer <x> <y>/end`；实测 22 条动作、7 个事件文件。
  见 [`2026-10-27-cape-orb-event-bridge.md`](../verification/2026-10-27-cape-orb-event-bridge.md)。
- ❌ `SVE_Lock`（1 处，尚未做）：`PlayEvent` + `PLAYER_HAS_ITEM` + 事件脚本 `removeItem`/`addItem`；
  按格子上的 `Success`/`Failure`/`Default` 属性生成。
- GiEX / Downtown Zuzu / Sunberry 的购买/消耗/传送动作：同一套事件桥；
  自定义 UI/代码级世界效果不可行（见下）。

**作用域警告**：原版只从 **Back 层**读 `TouchAction`（`GameLocation.cs:4224`）、只从 **Buildings 层**
读 `Action`（`:7870`、`:14197`）。语料里有 layer/objectgroup 级属性、以及放错层的动作
（Cape 的 layer 级 `TouchAction`、Front/AlwaysFront 的 `Action`）—— 这些在装 DLL 时也是死数据，
不是纯 XNB 新增的问题。

## 2. 事件脚本（Data/Events、Data/Festivals）

- 扫描 **360 个文件 / 2574 个脚本条目 / 165,778 条命令**（含 `quickQuestion` 分支）。
- 原版 1.6.15：**140 个 canonical 命令 + 1 个别名**（`Event.DefaultCommands`，`Event.cs:4111-4123`）。
- 语料用到 **124 个原版命令名**；自定义命令 **20 个 / 220 次**（占 0.13%），来自 11 个 DLL。
- 自定义命令里 **31 次影响流程/状态**，**189 次是纯演出**：

| 命令 | 次数 | 提供者 | 影响 | 建议 |
|---|---:|---|---|---|
| `sns_finale_phase2` | 15 | SwordAndSorcery | S&S 终章状态 | 删除（没有 S&S 系统） |
| `setDating` / `setEngaged` | 5 / 3 | SpaceCore | 恋爱/婚姻状态 | 删除（原版无事件命令等价物） |
| `if` / `append` | 3 / 2 | MistyCore | 事件内分支/插入 | `append` 可静态内联；`if` 用 `fork` 改写 |
| `makeInvincible` | 2 | MistyCore | 暂时无敌 | 删除，风险低 |
| `SVE_BadlandsDeath` | 1 | SVE | 死亡惩罚 | 可用 `hospitalDeath` 近似 |
| `addColouredSlime` | 103 | MEEP | 演出 | 删除 |
| `setRaining` / `setActorScale` / `cycleActorColors` | 17 / 15 / 12 | SpaceCore | 演出 | 删除 |
| `addScene` / `removeScene` | 12 / 12 | MistyCore | 演出（贴图在 Mods 目录） | 删除 |
| `screenShake` | 10 | SpaceCore | 演出 | 删除/近似 |
| 其余（灯光/图片/手机 UI） | 约 30 | Sunberry/RSV | 演出 | 删除 |

**最致命的不是命令，是前置条件**：

- 22 个 Sunberry 事件的前置条件里写了 `!SunberryTeam.SBVSMAPI_IsPassiveFestivalToday`
  （`SunberryVillage.dll:1744` 注册）。纯 XNB 下未知前置条件 **返回 false 且 `!` 不生效**
  ⇒ **这 22 个事件永远不会触发**。
- 可替换为原版 GSQ：`G !IS_PASSIVE_FESTIVAL_TODAY skellady.SBVCP_TwilightFestival`
  （`GameStateQuery.cs:384-392`；被动节日数据在 `Data/PassiveFestivals.json`）。
  这是**下一步最值得做的一条**（22 个事件全部恢复）。
- 另 1 个 `Mon` 是数据笔误（疑似漏写 `DayOfWeek`）。

## 3. 其它注册表

- **GSQ**：语料实际只出现 2 个非原版键（`CAULDRON_CONFIG_VALUE` 1 处，提供者未安装；
  `1` 1 处，是数据笔误）。地图上的 14,794 条 `ConditionalDoor` 查询里**没有**非原版关键字。
  ⇒ **GSQ 不是主要风险面**。
- **TriggerActions**（681 条）：
  - 27 条挂的是**自定义触发器类型**（SpaceCore `OnItemUsed/OnItemEaten`、BETAS 的
    `MonsterKilled/ItemEquipped/GiftGiven`、Cape 戒指触发等），纯 XNB 下**永不触发**，
    其中 11 条连动作也全是自定义的。
  - 54 条含自定义动作（`ContentPatcher_MigrateIds` 57 处、SpaceCore 播放音效/提示 44 处等）；
    未知动作被跳过，同条目其它动作仍执行。
- **ItemQueryResolver**：`VMV.MISSING_ARTIFACT_OR_ITEM` 4 处基础条目（含本地化 30 处），
  纯 XNB 下会掉出 **Error Item**。建议替换为其 fallback 物品 ID。
- **AssetRequested**：81 个 DLL 运行时直接注入资产 —— 这些不是字符串指令；纯 XNB 下除非
  转换器已经把结果落盘，否则内容缺失。

## 4. 原版机制的再挖掘（第二轮，2026-10-18）

第一轮有些"没有等价物"下得太快。把原版的**组合机制**过了一遍后，至少这些是真实存在的：

| 机制 | 能替代什么 | 依据 |
|---|---|---|
| `PlayEvent <id> <checkPre?> <checkSeen?> <fallbackAction>` | **通用桥**：任意"点一下/踩一下做一件事"的 DLL 动作。事件不存在时执行后面的原版 Action（`Warp`/`OpenShop`/`Message`…） | `GameLocation.cs:3634-3650`（TouchAction）、`:9044-9059`（Action）。⚠ 会 `startEvent`：淡入淡出、锁玩家、隐藏 HUD（`:15717-15760`）⇒ **不适合走过触发的环境音**（`SVE_PlaySound` 因此不替换） |
| 事件前置条件 `HasItem <itemId>` | `SVE_Lock`、Cape 宝珠传送这类**持有物**条件 | `Preconditions.cs:584-600` |
| 事件前置条件 `WorldState <id>` + 事件命令 `AddWorldState <id>` | 自定义**持久状态**（`sns_finale_phase2`、恋爱/婚姻剧情分支的近似） | `Preconditions.cs:89-96`、`Event.cs:2506-2516` |
| 事件前置条件 `Dating`/`Spouse`/`Roommate` | 依赖恋爱/婚姻状态的后续事件（注意：只能**读**，不能设置） | `Preconditions.cs`（同名方法） |
| `fork <mailId> <eventKey>` | MistyCore `if <GSQ>##真##假`（GSQ 是 `PLAYER_HAS_MAIL` 时） | `Event.cs:1796-1864`；本轮已实现 |
| `quickQuestion` 分支命令用 `\` 分隔 | 分支里的 `append`/命令 | `Event.cs:12817-12823`；本轮已实现 |
| 事件命令 `RemoveItem` / `AddItem` / `AddMoney` / `Friendship` / `Mail` | 状态类自定义动作的可达部分 | `Event.cs` 同名声明的命令 |
| 原版 TriggerActionManager 只有 **4 种触发器**：`DayEnding`/`DayStarted`/`LocationChanged`/`Manual` | ⇒ 自定义触发器（ItemEquipped/GiftGiven/MonsterKilled/OnItemUsed/SaveLoaded）**确实没有原版等价物**（这是源码级结论，不是没查到） | `TriggerActionManager.cs:660` |

仍然**真正做不到**的（会直接改存档/世界状态，没有数据入口）：

- `setDating` / `setEngaged`：SpaceCore 直接写 `Friendship.Status`、`spouse`、`WeddingDate`
  （`SpaceCore.decompiled.cs:6625-6710`）。数据资产只能**读**状态（`Dating`/`Spouse` 前置条件），
  不能写。
- `setRaining`：直接改 `Game1.netWorldState.Value.GetWeatherForLocation(...).IsRaining`
  （`:6971-6978`）。没有事件命令/Data 资产能改当前天气。
- `sns_finale_phase2`：S&S 自己的静态状态；可近似成 `AddWorldState`，但依赖它的 S&S 内容本身要 DLL。
- `screenShake` / `setActorScale` / `cycleActorColors`：SpaceCore 自己的渲染 patch，
  原版事件命令没有对应表达；只能删或近似（`Shake` 演员/`ScreenFlash`）。

## 5. 下一步优先级（按"玩家可见 + 可机械修复"排序）

1. **Sunberry 事件前置条件**（22 个事件永不触发）→ 原版 `IS_PASSIVE_FESTIVAL_TODAY`。
2. **`VMV.MISSING_ARTIFACT_OR_ITEM`** → fallback 物品（避免 Error Item）。
3. **事件 `append` 静态内联 / `if` 改写 `fork`**（GiEX，影响剧情分支）。
4. 大小写笔误修正（`playSound` 等）。
5. 其余自定义菜单/地牢/触发器动作：无原版等价物，只能删除或保留 DLL；
   转换报告里应逐条列出，避免"静默失效"。

⚠ 以上第 1–3 条是**调查结论**，代码尚未实现；实现前要逐条加测试并做同参数 A/B。

## 6. 第二轮：事件命令逐条深挖（2026-10-18）

### 6.1 已实现

- `addColouredSlime <x> <y> <r> <g> <b>`（MEEP，103 处）→ 原版
  `temporaryAnimatedSprite Characters\Monsters\Green Slime 0 0 16 24 200 4 999 … color #RRGGBB`
  （TAS 的 `color` 选项见 `Event.cs:1195-1211`）。只是精灵，不是 actor。
- `SVE_BadlandsDeath` → `HospitalDeath/Money -65666`（SVE 的损失是
  `min(33333 + random(0..66666), money)`；事件命令没有 RNG，取中值近似）。
- `if`/`append` → `fork`/静态内联（含 `quickQuestion` 的 `(break)`/`\` 分支）。

### 6.2 调研清楚、但未实现（需要联动改事件/生成资产）

- `setDating` / `setEngaged`：**没有任何数据路径能写** `Friendship.Status`/`spouse`/`WeddingDate`
  （SpaceCore `6625-6710`；原版只有花束/吊坠/多人求婚/读档/调试命令）。可行近似：
  `addMailReceived XBG_Dating_<npc>` / `XBG_Engaged_<npc>`，并把后续事件 key 里的
  `D <npc>` / `O <npc>` / `PLAYER_NPC_RELATIONSHIP … Dating/Engaged` 改成邮件条件。
  代价：约会对话、送礼反应、求婚、婚礼、配偶房间、S&S 的雇佣兵分支全部丢失。
  受影响资产约 15 个（`Railroad`、`EastScarp_Underforge`、`EastScarp_LionsMane`、`Mountain`、
  `Saloon`、`EastScarp_Village`、`Custom_SBV_RipleyHouse`、`Farm`、`Town` 等）。
- `sns_finale_phase2`：只设 S&S 的 `State.DoFinale`；Boss/雇佣兵/战斗不可能复现。
  剧情可继续：删掉该命令，在中场事件末尾 `switchEvent SnS.Ch4.Victory.<Partner>`，
  并按需 `addItem (W)DN.SnS_PaladinShield 1` + `addMailReceived DN.SnS_IntermissionShield`。
- `setRaining`：事件当天/次日无数据路径；`Data/LocationContexts.WeatherConditions` 只在
  “新的一天”计算**下一天** ⇒ 最早 D+2。当前场景只能 `playSound rain`/`playMusic rain` 伪装。
- `setActorScale` / `cycleActorColors`：原版 actor 没有 scale/tint 参数；只能预生成缩放/染色贴图，
  再用 `addTemporaryActor`（显式帧尺寸）或 `changeSprite` 序列近似。
- `screenShake`：原版没有相机震动字段；可用 `viewport player clamp true` + 交替
  `viewport move` + `shake`/`screenFlash` 近似。
- `totemWarpEffect`：`playSound` + 多条 `temporaryAnimatedSprite` + `glow` + `screenFlash` 近似。
- Sunberry `AddLight`：`AddLantern <idx> <x> <y> <int r>` + `pause 100` + `removeSprite <x> <y>`
  （灯光保留、贴图消失；颜色固定偏蓝）。`RemoveLight` **做不到定点移除**。
- `RSVShowImage` / `PhoneConfession`：全屏 UI 无等价；图片可用世界内
  `temporaryAnimatedSprite` 近似，电话只能改成 `message`/`speak`/`quickQuestion` 文本。

### 6.3 `fork` 的邮件语义（重要）

`fork <mail> <key>` 只认 `mailReceived` + `dialogueQuestionsAnswered`（`Event.cs:1796-1866`）；
而 GSQ `PLAYER_HAS_MAIL` 默认是 `Any`（mailbox + tomorrow + received）。
⇒ 用 `fork` 改写 `PLAYER_HAS_MAIL … Any` 时可能有细微差异；`Current`/`Received` 等价。

## 7. TriggerActions / SpecialOrders / Machines（第三轮）

### 7.1 原版只有 4 种触发器

`TriggerActionManager.cs:659`：`DayEnding`、`DayStarted`、`LocationChanged`、`Manual`。
`ItemUsed/ItemEaten/GiftGiven/MonsterKilled/ItemEquipped/SaveLoaded` **不存在**；
`Manual` 在原版没有任何 `Raise` 调用者（只有 mod 的 tile action 会按 Id 调）。
⇒ 语料 27 条自定义触发器条目：7 条可基本保留功能、1 条部分保留、19 条不可能；
连同自定义动作，受影响条目合计 **64 条**。

原版触发器动作只有 **26 个**（`AddBuff/AddMail/AddItem/AddMoney/AddQuest/…/Null/If`），
**没有** `PlaySound`/`ShowHudMessage`/`PlayEvent`/`OpenGuidebook`/`SpawnGroup`/`DamageCurrentFarmer`。
能精确映射的只有 `SpaceCore_ApplyBuff → AddBuff`（语料 0 次）；`addMail` 本来就是原版（大小写不敏感）。

⚠ **`MarkActionApplied` 陷阱**：自定义动作全部解析失败时，条目仍会被标记为已执行
（`TriggerActionManager.cs:583-608`）—— 纯 XNB 下这些条目会“静默消费”自己。

⚠ **26 条 `Trigger: "Manual"` 在原版没有调用者**：如果原交互点是地图图块，可以把图块改成
`PlayEvent <eventId>`，再用原版事件命令（`PlaySound`/`Message`/`AddItem`/`Mail`/`Money`/
`AddCookingRecipe`/`Action <原版触发器动作>`）重写；伤害/自定义生成组/指南书除外。

### 7.2 SpecialOrders：自定义板在纯 XNB 里永远打不开

- 原版只有三种板：`""`（`Action SpecialOrders`）、`"Qi"`（`QiChallengeBoard`）、
  `"DesertFestivalMarlon"`；`Action = SpecialOrders <id>` 的参数会被忽略。
- 语料 193 个订单中 **112 个**是自定义板/占位板，纯 XNB 下不可达。
- **已实现**（`DataFixes`）：把自定义 `OrderType` 改成 `""`，至少进入默认板候选池。
  备选方案（未实现）：`Data/TriggerActions` 的 `AddSpecialOrder <id>` 直接投放（绕过板子/RequiredTags）。
- `Custom` 目标在原版没有 `CustomObjective` 类，会被静默跳过；订单会一直 InProgress 到超时。
- `FIVE_PLAGUES`/`QI_DOUBLE` 是原版 1.6.15 数据自带的规则（挂在带 `NOT_IMPLEMENTED` 的订单上）；
  `LEGENDARY_FAMILY` 是原版规则；`atelier99.SBV.NPCLani_CanBrewSoju` 作为 flag 在
  `PLAYER_SPECIAL_ORDER_RULE_ACTIVE` 里原生可用。这三类都不需要替换。

### 7.3 Machines：`OutputMethod` 必然失败

`StaticDelegateBuilder` 用 `Type.GetType` 找 mod 类型；纯 XNB 没有 DLL ⇒
`MachineDataUtility.cs:204-210` 返回 null，机器无产出。语料 **11 处**全部是
OrnithologistsGuild 的喂食器（固定权重 747×25 / 335×3 / 336×1）。
**已实现**（`DataFixes`）：展开成 29 条 `OutputItem` + `UseFirstValidOutput: false`，
概率与原方法一致。

### 7.4 ItemQuery：VMV 缺失文物无法精确表达

原版 17 个查询里没有"博物馆未捐赠物品"；`MUSEUM_DONATIONS` 只有总数，
`RANDOM_ARTIFACT_FOR_DIG_SPOT` 不看博物馆。**已实现**（`ItemQueryFixes`）：
直接取 fallback 物品（确定性，但失去"补全博物馆"玩法）。

