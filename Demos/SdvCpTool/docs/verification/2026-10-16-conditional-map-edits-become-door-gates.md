# 条件化的地图改动 ⇒ **无条件落图 + 图块门禁**（去源码里找替代，而不是判"没有通道"）

日期：2026-10-16

起因：实机「农场图上的矿车**一个都没有**／该出去的传送出不去」。原先的判词是
「条件化的地图改动在原版没有通道（图块级只有 ConditionalDoor，且不适用于此）」——
**后半句是错的**：`ConditionalDoor` 恰恰**能用**。这一轮按业主口径
（"能完全用 GSQ 代替就代替；只降低一点生动感也勉强做；实在不行再汇报"）逐层查了源码，
找到了**可落地的替代**。

## 1. 先查"条件侧"：GSQ 到底认什么

`D:\sdv_src\StardewValley\GameStateQuery.cs` —— **115 个关键字**（`grep 'public static bool [A-Z_]+('`），
覆盖 `PLAYER_HAS_MAIL` / `PLAYER_HAS_SEEN_EVENT` / `PLAYER_NPC_RELATIONSHIP` / `PLAYER_HEARTS` /
`PLAYER_FARMHOUSE_UPGRADE` / `FARM_TYPE` / `YEAR` / `SEASON` / `WEATHER` / `TIME` / `DAY_OF_WEEK` /
`RANDOM` / `PLAYER_VISITED_LOCATION` / `MUSEUM_DONATIONS` / `PLAYER_SPECIAL_ORDER_COMPLETE` …
并且**取反算符就是 `!`**（`:2174` `bool flag = text.StartsWith('!');`）。

而且**翻译侧早就在跑** —— 产出的 `Data/Minecarts` 里那批条件本身就是翻译出来的，例如
`PLAYER_HAS_MAIL Any jojaMember Any`（CP 的 `HasFlag`）。

⇒ **条件不是障碍**：卡住的一直是"**往哪写**"。

## 2. 再查"落点侧"：地图上能承载 GSQ 的位置有哪些

把 `GameLocation.cs` 里**全部** `GameStateQuery.CheckConditions` 调用点（14 处）过了一遍：

| 落点 | 依据 | 能承载什么 |
|---|---|---|
| **图块动作 `ConditionalDoor <GSQ>`** | `:3907`、`:9549`（GSQ 为假 ⇒ 把玩家**弹回**）| ✅ **唯一的通用图块机制** |
| `TouchAction` 属性 | `:3593` "Handle a `TouchAction` property from a **`Back`** map tile" | ✅ 且**只在 `Back` 图层读** |
| `BuildConditions` | `:2470` | 只控"可否建造" |
| `Data/Locations` 条目（刷怪/采集/掉落/音乐）| `:3126/3417/3246/13977/14248/14283/15142` | 已走 |
| `Data/Minecarts` 的 `UnlockCondition` / `Destinations[].Condition` | `:10225/:10246` | ✅ 矿车网络（本轮已用上）|
| `LockedDoorWarp` | `:9536` | 只认**时间段 + NPC 好感**，不是通用 GSQ |

**没有条件化 `Warp`**（`Warp` 那几处都不查 GSQ），`LocationData`（`D:\sdv_gd`）也**没有** warp 字段
（只有 `DisplayName`/`DefaultArrivalTile`/`CreateOnLoad`/`Music*`/`CustomFields`）。

## 3. 于是替代就唯一了：**无条件落图 + 门禁**

- 改动**无条件写上去**（图/传送/装饰都在），
- 条件由**同一格的 `Back` 层**上 `TouchAction = ConditionalDoor <取反的 GSQ>` 承担
  ⇒ 条件不成立时**踩上去被弹回**（"看得见、进不去/用不了"）—— 正是"只降低一点生动感"那一档。

**取反**：单条 ⇒ `!q`；顶层逗号是 **AND** ⇒ 德摩根取反成 OR，写成 `ANY "!q1" "!q2"`
（与 `GsqTranslator.RenderAny` 同一种写法）。实测产出：

```
TouchAction = ConditionalDoor ANY "!PLAYER_HAS_MAIL Any ccBoilerRoom Any" "!FARM_TYPE Standard"
```

（补丁里 `Enablejibb.IF2R` / `HasMod` 两个叶子**离线求值为真**、已被折叠掉 ✓。）

### 3.1 只对**不受轮流型条件影响**的补丁做（判据：黑名单）

**轮流型**（`Weather` / `Time` / `Season` / `Day` / `DayOfWeek` / `Random`）落到**同一片区域**
是**互斥变体**，无条件全写会互相覆盖 ✗ ⇒ 这些维持原判（只报不写）。

⚠ 判据用**黑名单**而不是白名单：配置键（`Enablejibb.IF2R` 这种）离线是**冻结值**、只会产生一个变体，
不该拦；而条件名是开放集合（各框架都能定义），白名单必然漏 ⇒ 漏的后果是"**该拦的没拦**"，
黑名单漏的后果只是"少落一处门禁"。

### 3.2 只对**能定位到图块**的补丁做

要放门禁得知道"它改了哪几块图"：`MapTiles` 的 `Position`（**精确**）或 `ToArea`（矩形）。
两者都没有的（只改 `MapProperties`/`AddWarps` 那种）**维持 `LocalEdit`** 。

## 4. 验证

| 手段 | 结果 |
|---|---|
| 单测 | **+10 条**（`ConditionPersistenceTests`：配置键不拦 / 六种轮流型都拦 / 无条件不走这条路 / 取反两种写法 / 引号里的逗号不切）⇒ 全量 **1465 全绿** |
| 定点（小树只放 `Maps/Farm` + `Data/Minecarts`，跑 `[CP] Integrated Minecarts`）| 两条 `Maps/Farm` 补丁由 `LocalEdit` **转成 `ConvertedNoCondition` written=True**；产出 `Maps/Farm.tbin` 里 `ConditionalDoor` **1 次**、包自己的 `MinecartTransport` **1 次**（`Action` 没被冲掉 ✓）|
| 语料（232 包） | `LocalEdit` **1102 → 601**（501 条落地）、`ConvertedNoCondition` +500；产出 **1246 张地图里 59 张**带门禁；对改前基线**消失 0**、新增 18（含此前因条件被丢、从未建出来的地图，如 `Maps/JojaMartBackDoor`）|
| 实机（`D:	mp\gv` 纯 XNB 装机）| 巡图 **传送成功 35 / 建不起来 3**、音乐 6 条、跨天 3 次；**地图探针 PASS 1246 / 贴图跳过 1259 / FAIL 0（共 2505 张 —— 比上一轮多 6 张 = 新落地的那批地图）**；日志类别与基线表**逐条一致、无新增**（`ConditionalDoor` 一条错都没报）|

⚠ **未验**：门禁在实机"踩上去被弹回"的效果 —— `ConditionalDoor` 的弹回逻辑抄的是游戏源码注释与
那两处调用点，**没有实机走过那一步**（GameSmoke 的巡图是直接传送，不会去撞门禁）。
