# 农场走不出去：DLL 图块动作改写 + 门禁方向订正 + 大范围门禁守卫

日期：2026-10-18

业主报：「农场地图传送不到巴士站、下面的森林、上面的走廊」「我连农场地图都走不出去」。
结论：**模组提供的传送确实没转成原版指令**，而且门禁通道还有三处会放大问题。本轮全部修掉，
并顺手把事件前置条件 / 物品查询里同类的「DLL 专有字符串」也做了安全替换。

## 1. 根因链

### 1.1 `LoadMap` 是 TMXL 的动作，纯 XNB 里是死图块（主因）

导出的 `Maps/Farm.tbin` 里，三个出口全是 `TouchAction` 图块：

- 下方森林：`(67..72, 92) LoadMap Forest 69 2`、`(39..40, 92) LoadMap Forest 39 6 2`
- 上方走廊：`(49..53, 0) LoadMap Backwoods 13 24`、`(51..53, 17) LoadMap Backwoods 14 38`
- 巴士站：`(110..114, 17) LoadMap BusStop 24 20 0`

`LoadMap` 由 TMXL Map Toolkit / PyTK 注册（`TMXLoaderMod.cs:1740`），原版 1.6 没有。
SVE 自己在没装 TMXL 时用 `UpdateTicked` 兜底（`TouchActionProperties.cs:47-58`），
兜底实现就是**立即** `Game1.warpFarmer(loc, x, y, false)`。
纯 XNB 没有这些 DLL ⇒ `performTouchAction` 找不到处理器（无 default、外层空 catch）
⇒ 踩上去**静默无反应**。

**修法**：`LoadMap <loc> <x> <y> [facing]` → 原版 TouchAction `Warp <loc> <x> <y>`
（`GameLocation.cs:3699-3710`）。选 `Warp` 而不是 `MagicWarp`：后者多 1 秒延迟和闪屏，
而 TMXL / SVE 的语义都是立即传送。**第 4 参必须丢** —— 原版 `Warp`/`MagicWarp` 的第 4 参
是 `mailRequired`，把朝向 `0/1/2/3` 留着会被当成邮件 ID，导致永远不传送。

### 1.2 门禁方向写反了（会把条件成立的人弹回去）

`GameLocation.performTouchAction` 的 `ConditionalDoor` 分支（`:3907`）：

```csharp
if (!(value == "ConditionalDoor") || … || GameStateQuery.CheckConditions(…)) break;
// 条件为真 ⇒ break（放行）；为假 ⇒ 继续执行，把玩家弹回
```

第一版门禁写的是 `ConditionalDoor Negate(gsq)`，于是 **Standard 农场（条件为真）反而被弹回**。
Grandpa's Farm 的 Backwoods 楼梯（`ToArea 76,0,6,3`）正好在门禁上 ⇒「上面的走廊」走不上去。
已改为直接写**补丁条件本身**。

### 1.3 重判路径丢了 `DoorGateGsq`

`Program.cs` 对初判 `TargetMissing` 的补丁做重判，逐字段拷贝 `Verdict/Reason/Channel/…`，
**漏了 `DoorGateGsq`**。Grandpa's Farm 的 `Maps/Farm,Maps`（多目标里的 `Maps` 不存在）
初判 TargetMissing、重判成 ConvertedNoCondition，但门禁 GSQ 是 null ⇒ 图改了、门禁没写。
已补齐（并加注释说明为什么必须一起带）。

### 1.4 整片变体不能逐格设门禁（新守卫）

修好方向后暴露一个更危险的问题：`Custom_FerngillRepublicFrontier` 有一条 `ToArea` = **24336 格**
的整图变体（`HasSeenEvent 1337427: false`）。逐格设门禁后，玩家**看过事件**时整张图都会被弹回。
门禁只适用于**局部新增**（矿车、楼梯），不适用于整片地形变体。

**修法**：`ConditionPersistence.MaxDoorGateCells = 64`。语料实测 497 条走门禁的补丁：
格数中位数 **3**、75 分位 40、90 分位 216；64 保留约 79% 的补丁、总共 3240 格。
超过上限的**照常写入、但不设门禁**（这类补丁常带新通路/新图块，整条丢掉会连内容一起丢；
两害相权，宁可让变体无条件可见，也不把整张图写死）。

## 2. 代码改动

| 文件 | 改动 |
|---|---|
| `Core/Maps/MapTileActions.cs`（新） | `LoadMap→Warp`（丢 facing）；`RSVTeleport <loc> <x> <y> → Warp <x> <y> <loc>`（**Action 版参数顺序相反**）；`SBVSMAPI_MineLadder→Warp`；BETAS 动作代理→`playSound` |
| `Core/Merging/LoadApplier.cs` | `Load` 的 tbin/tmx 路径都调用改写；有改写时重新序列化 |
| `Core/Merging/MapApplier.cs` | 编辑后、落盘前调用改写；门禁改为写条件本身 |
| `Core/Maps/MapCache.cs` | `Flush` 落盘前统一改写（覆盖所有缓存地图） |
| `Core/Judging/PatchJudge.cs` | 面积守卫；超过 64 格不设门禁 |
| `Core/Assets/ConditionPersistence.cs` | 新增 `MaxDoorGateCells`；删除不再使用的 `Negate` |
| `Core/Assets/EventPreconditions.cs`（新） | `!SunberryTeam.SBVSMAPI_IsPassiveFestivalToday` → `!G IS_PASSIVE_FESTIVAL_TODAY skellady.SBVCP_TwilightFestival`（22 个事件恢复可触发；`G` 是原版事件前置条件 `GameStateQuery` 的别名） |
| `Core/Assets/ItemQueryFixes.cs`（新） | `VMV.MISSING_ARTIFACT_OR_ITEM <fallback> --filter …` → `<fallback>`（避免 Error Item） |
| `Core/Merging/TranslationApplier.cs` | `WriteLocalized` 这个唯一漏斗里调用上面两个归一 |
| `Cli/Program.cs` | 重判时复制 `DoorGateGsq` |
| `Core/Maps/MapTileActions.cs`（扩展） | 追加：`RSVTeleport`→`ObeliskWarp`；`RSVShop`→`OpenShop`；`ZuzuBarber`→`WizardShrine`；`Say`→`Dialogue`；`PoolExit`→`PoolEntrance`；`playsound`/`PlaySound`（Action 版）与 `ChangeIntoSwimSuit` 等大小写修正。**`SVE_PlaySound` 不替换**：唯一能借的 `PlayEvent` 会淡入淡出/锁玩家 |
| `Core/Assets/EventCommandFixes.cs`（新） | MistyCore `append` 静态内联；`if <PLAYER_HAS_MAIL…>##真##假` → 原版 `fork`（含 `quickQuestion` 的 `(break)`/`\` 分支；引号未闭合时按 MistyCore 的朴素切分）；`addColouredSlime` → 带 `color` 的 `temporaryAnimatedSprite`；`SVE_BadlandsDeath` → `HospitalDeath/Money -65666` |
| `Core/Assets/DataFixes.cs`（新） | `Data/SpecialOrders` 的自定义 `OrderType` → `""`（112 个订单至少进入默认板）；`Data/Machines` 的 OrnithologistsGuild `OutputMethod` → 29 条加权 `OutputItem`（11 处） |
| `Core/Assets/SpouseRoomMaps.cs`（新）+ `PatchJudge` | **配偶房间地图不设门禁**：婚房由 `FarmHouse.loadSpouseRoom()` 用 `ApplyMapOverride` 把 6×9 图块复制进农舍（`FarmHouse.cs:1988-2065`），门禁会被一起复制 ⇒ 条件化的婚房变体照常写入、不设 `ConditionalDoor` |

单测：**1499 条全绿**（新增：门禁方向、面积守卫、`LoadMap`/RSV/MineLadder/BETAS 改写、
事件前置条件、物品查询、`append`/`if` 改写、`addColouredSlime`/`SVE_BadlandsDeath`、订单板/机器产出、
配偶房间地图不设门禁）。

## 3. 定点验证（Grandpa's Farm，同参数）

| 地图 | 改前 | 改后 |
|---|---|---|
| `Maps/Farm` | 53 `LoadMap`；0 门禁（重判丢失） | 0 `LoadMap` / 53 `Warp`；18 块门禁（条件本身） |
| `Maps/BusStop` | 11 `LoadMap`；205 块门禁（反向） | 0 / 11 `Warp`；0 门禁（面积 216 > 64，照常写入） |
| `Maps/Forest` | 2 `LoadMap`；46 块门禁（反向） | 0 / 2 `Warp`；46 块门禁（非取反） |
| `Maps/Woods` | 2 `LoadMap`；54 块门禁（反向） | 0 / 2 `Warp`；54 块门禁 |
| `Maps/Backwoods` | 150 块门禁（反向） | 0 门禁（面积 150 > 64） |

## 4. 全量 A/B（232 包，同参数）

跑批根：`D:/tmp/prof-fix13`（最终代码）；基线：`D:/tmp/prof-fix8`。

- 判定分布：与基线 **逐项相同**（32,795 条；LocalEdit 601、written 16,123）。
- 产出树：**31,601 个文件，只在 A/B 各 0 个，内容不同 273 个**：
  - `Maps/*.tbin` **204**（`LoadMap→Warp`、门禁方向/面积、`RSVTeleport→ObeliskWarp`、`MineLadder→Warp`、配偶房间不设门禁）
  - `Data/Events*` **39**（GiEX `append`/`if`、`addColouredSlime`、`SVE_BadlandsDeath`、Sunberry 前置条件）
  - `Data/Locations`/`Data/Objects` **15**（`VMV.MISSING_ARTIFACT_OR_ITEM` → fallback）
  - `Data/SpecialOrders` **10**（自定义板 `OrderType → ""`）
  - `Data/Machines` **5**（喂食器 `OutputMethod` → 加权 `OutputItem`）
  - **其它资产 0 差异**。
- 残留扫描（最终树）：
  - `LoadMap` **0**、`addColouredSlime` **0**、`SVE_BadlandsDeath` **0**、`/if PLAYER_HAS_MAIL` **0**；
  - `append GiEX` **4**（`GiEX_Javi_4h_read`：片段在 `GiEX_Hut`、事件在 `GiEX_SouthWest`，
    MistyCore 的 `append` 只查**当前地点**，装 DLL 时也找不到 ⇒ 本来就不生效，保留原告警）；
  - `SVE_PlaySound` **239 / 5 张**（**刻意不替换**：`PlayEvent` 会淡入淡出/锁玩家）；
  - `ConditionalDoor` **2,326 块 / 129 张图**（基线 14,794；面积守卫 + 配偶房间排除后）；
  - `Maps/spouseRooms` 的 `ConditionalDoor` **0**；
  - `RSVTeleport` **0**（`ObeliskWarp` 53 处）；`MineLadder` **0**；
  - 自定义 `OrderType` **0**（178 个默认板 / 12 个 Qi / 3 个 DesertFestivalMarlon）；
  - OrnithologistsGuild `GetFeederOutput` **0**。

## 5. 未确证 / 未做

- **实机未再验**：本轮只做了代码 + 产出树对照；「踩上去真的传过去」「门禁弹回」「`fork` 分支真的走对」仍要游戏内探针确认。
- **已做的第二层等价替换**：`RSVTeleport`→`ObeliskWarp`、`RSVShop`→`OpenShop`、`ZuzuBarber`→`WizardShrine`、
  `Say`→`Dialogue`、`PoolExit`→`PoolEntrance`、大小写笔误修正；MistyCore `append` 静态内联、`if`→`fork`
  （含 `quickQuestion` 的 `(break)`/`\` 分支，三处 GiEX 事件在基础语言与所有本地化里都已无 `append`/`if`）。
- **已做（2026-10-27，静态验证）**：
  - Cape 宝珠传送 → `CapeOrbBridge`：生成 `PlayEvent` 事件 + `HasItem (O)OrbOfTheTides`，
    改 `CapeOrbTouchAction` 图块；22 条动作全部落成，事件/地图回包 14/0。
    详见 [`2026-10-27-cape-orb-event-bridge.md`](2026-10-27-cape-orb-event-bridge.md)。
- **仍未做**（需要生成事件/改后续事件，尚未实机验证）：
  - `SVE_Lock` 仍需按 `Success`/`Failure`/`Default` 生成 `RemoveItem`/`AddItem` 事件。
  - `setDating`/`setEngaged`：SpaceCore 直接写存档字段，数据层只能**读**（`Dating`/`Spouse`
    前置条件），不能写 ⇒ 只能做"邮件/world state + 改后续事件"的剧情近似。
  - `setRaining`：直接写 `netWorldState`，没有数据入口。
  - SpaceCore 地牢、RSV 菜单、Downtown Zuzu、Sunberry 菜单等：没有原版等价物（清单见 research 文档）。
- `TownMailbox`→`Mailbox`、`Sleep2`→`Sleep`、`MakeoverBox`→`DesertMakeover`：这些死值**原版地图自己就在用**，
  改它们等于改原版行为，先不动。
