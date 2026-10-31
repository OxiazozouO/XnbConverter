# `Data/Events/*` 的键内前置条件：令牌全表与能力边界

日期：2026-10-06
依据：`D:\sdv_src`（1.6 反编译）＋ `D:\sdv_merged\Data\Events`（83 个事件资产，662 条键）＋ 语料实测（`D:\tmp\corpus_b2\*\report.json` + `D:\[CP] Ridgeside Village.report.json`）
复现脚本：`D:\tmp\_evtscan\*`（临时产物，未入库）；量化口径复用仓库既有工具 `tools/variant-per-type.py`

---

## 0. 一句话

`Data/Events/<地点>` 的**键**就是 `<事件ID>/<前置条件>/<前置条件>/…`，条件**不在值里，在键里**。
前置条件共 **102 个令牌**（54 个规范名 + 48 个别名）。
**GSQ 的通用入口是 `G`（规范名 `GameStateQuery`），不是 `x`** —— `x` 是被废弃的 `SendMail` 伪事件。
因为 `G` 存在，**原版事件前置条件能内联任意 GSQ，几乎所有 CP 维度都能装**。
四个维度（看过的剧情/婚姻/好感度/年份）在语料里覆盖 **181 个不同的 `Data/Events/*` 补丁**（若并入季节维则 184）。

---

## 1. 键结构与解析入口

### 1.1 资产名与键形态

```csharp
// GameLocation.cs:15464-15467 —— 资产名 = Data/Events/<地点名>（家则固定 FarmHouse）
assetName = ((NameOrUniqueName == Game1.player.homeLocation.Value)
             ? "Data\\Events\\FarmHouse"
             : ("Data\\Events\\" + name.Value));
```

值是 `Dictionary<string, string>`：**键 = 条件串，值 = 事件脚本**。

```
key   = "<事件ID>/<前置条件1>/<前置条件2>/…"
value = "<事件脚本，命令之间也以 / 分隔>"
```

**切分（键）**：

```csharp
// Event.cs:11718-11723
/// <summary>Split an event's key into its ID and preconditions.</summary>
public static string[] SplitPreconditions(string rawScript)
{
    return ArgUtility.SplitQuoteAware(rawScript, '/',
        StringSplitOptions.RemoveEmptyEntries, keepQuotesAndEscapes: true);
}
```

要点：
- **`/` 引号感知**（`ArgUtility.cs:51-65` 的 remarks：引号内的 `/` 不切分；`\"` 转义）。
- **`RemoveEmptyEntries`** —— 空段被丢弃，`id//f X` 与 `id/f X` 等价。
- `array[0]` = 事件 ID，`array[1..]` = 各前置条件段。
- **脚本的切分是另一套**（`Event.cs:11728-11733`，额外做 gender 切换、`TokenParser.ParseText`、`TrimEntries`）——**键与值必须分别解析，不要共用一套 splitter**。

### 1.2 三条读取路径

| 路径 | 源码 | 说明 |
|---|---|---|
| 自动触发（进入地点） | `GameLocation.cs:15542` `checkForEvents()` → `:15658-15666` | 遍历 **全部键**，`checkEventPrecondition(key)`，**第一个通过的就 break** |
| 脚本点名播放 | `Game1.cs:11956` `PlayEvent(id)` → `:11978-11993` | `key.Split('/')[0] == eventId`（**朴素切分**，所以 ID 内不能有 `/`） |
| 按 ID 找事件 | `GameLocation.cs:15707-15713` | `Event.SplitPreconditions(item.Key)[0] == id` |

### 1.3 判定入口

```csharp
// GameLocation.cs:16454-16474
public virtual string checkEventPrecondition(string precondition, bool check_seen)
{
    string[] array = Event.SplitPreconditions(precondition);
    string text = array[0];                                  // ← 事件 ID
    if (string.IsNullOrEmpty(text) || text == "-1") return "-1";
    if (check_seen && (Game1.player.eventsSeen.Contains(text) ||
                       Game1.eventsSeenSinceLastLocationChange.Contains(text)))
        return "-1";                                         // ← 看过就永久/本次不再播
    for (int i = 1; i < array.Length; i++)                   // ← 逐段判定
        if (!string.IsNullOrEmpty(array[i]) && !Event.CheckPrecondition(this, array[0], array[i]))
            return "-1";
    return text;                                             // ← 返回 ID 表示通过
}
```

```csharp
// Event.cs:4187-4211
public static bool CheckPrecondition(GameLocation location, string eventId, string precondition)
{
    string[] array = ArgUtility.SplitBySpaceQuoteAware(precondition);   // ← 段内按空格切
    string text = array[0];
    bool flag = true;
    if (text.StartsWith('!')) { text = text.Substring(1); flag = false; } // ← `!` 否定前缀
    if (!TryGetPreconditionHandler(text, out var handler))
    {
        Game1.log.Warn("Unknown precondition for event " + eventId + ": " + precondition);
        return false;                                                    // ← 未知令牌 = 静默不播
    }
    return handler(location, eventId, array) == flag;
}
```

**三条硬事实**：
1. **段与段之间是 AND**（任一段 false → 整个键不成立）。
2. **`!` 前缀可否定任意令牌**（`Event.cs:4192-4196`）；原版内容里已见 `!Skill Fishing 1`。
3. **未知令牌不会报错，只会 Warn 并返回 false**（`Event.cs:4197-4201`）→ **事件永不触发**。这是转换器最容易埋的静默故障。

### 1.4 令牌注册（决定「有哪些令牌」）

```csharp
// Event.cs:4143-4166 —— 反射注册：Preconditions 类的全部 public static 方法
MethodInfo[] methods2 = typeof(Preconditions).GetMethods(BindingFlags.Static | BindingFlags.Public);
foreach (MethodInfo methodInfo3 in methods2)
    Preconditions[methodInfo3.Name] = value2;            // ← 规范名（大小写不敏感）
foreach (... OtherNamesAttribute ...)
    RegisterPreconditionAlias(aliases[j], methodInfo4.Name);  // ← 别名
```

- 规范名查找：`Preconditions` 字典是 `StringComparer.OrdinalIgnoreCase`（`Event.cs:3755`）→ **规范名大小写不敏感**（实测语料里 Cornucopia 就写小写 `season spring/year 2/dayofweek monday/sendmail X`，全部命中）。
- 别名查找：`PreconditionAliases.TryGetValue(key, ...)` 先查（`Event.cs:4173-4181`）→ **别名大小写敏感**（`Event.cs:4087` 注释明确写明 *"This is case-sensitive for legacy reasons"*）。
- 注册表**可被 mod 扩充**：`Event.RegisterPrecondition(name, action)`（`Event.cs:4068-4084`）。
  **实测**：RSV 注册了自有令牌 `rsvRidingHorse`（`D:\rsv_src\RidgesideVillage.decompiled.cs:6140`），并在内容里直接写 `…/rsvRidingHorse`。
  → **102 个不是闭集**；转换含自注册令牌的 mod 时必须先扫它的 DLL/源码。

---

## 2. ★ 事件前置令牌全表（54 规范名 + 48 别名 = **102**）

全部定义在 `D:\sdv_src\StardewValley\Preconditions.cs`（`class Preconditions`，`:12`）。
「源码」列 = 该方法体所在行；「别名」列 = 其上方 `[OtherNames(...)]` 所在行。

| # | 规范名 | 别名（大小写敏感） | 参数 | 语义 | 源码 |
|---:|---|---|---|---|---|
| 1 | `SawEvent` | **`e`** | `<id>` …（多个=任一） | 看过任一给定事件 | :16-17 |
| 2 | `MissingPet` | `h` | `[petType]` | 尚未领养宠物（可限定偏好） | :35-36 |
| 3 | `IsHost` | `H` | — | 当前是主机 | :51-52 |
| 4 | `HostMail` | `Hn` | `<mailId>` | **主机**收过该邮件 | :63-64 |
| 5 | `NotHostMail` | `Hl` | `<mailId>` | 主机没收过（⚠️ Obsolete，改用 `!HostMail`） | :76-77 |
| 6 | `WorldState` | `*` | `<worldStateId>` | 任意位置的世界状态激活 | :88-89 |
| 7 | `HostOrLocalMail` | `*n` | `<mailId>` | 主机**或**本玩家收过 | :100-101 |
| 8 | `NotHostOrLocalMail` | `*l` | `<mailId>` | 两者都没收过（⚠️ Obsolete） | :117-118 |
| 9 | `EarnedMoney` | `m` | `<int>` | 累计赚过 ≥ N（含已花） | :125-126 |
| 10 | `HasMoney` | `M` | `<int>` | 当前持有 ≥ N | :137-138 |
| 11 | `FreeInventorySlots` | `c` | `<int>` | 背包空格 ≥ N | :149-150 |
| 12 | `CommunityCenterOrWarehouseDone` | `C` | — | 社区中心/Joja 仓库已完成 | :161-162 |
| 13 | `NotCommunityCenterOrWarehouseDone` | `X` | — | 未完成（⚠️ Obsolete） | :174-175 |
| 14 | `Dating` | `D` | `<npc>` | 正在与该 NPC 交往 | :182-183 |
| 15 | `DaysPlayed` | `j` | `<int>` | 游戏天数 **> N**（注意是严格大于） | :198-199 |
| 16 | `JojaBundlesDone` | `J` | — | Joja 线全部完成 | :210-211 |
| 17 | **`Friendship`** | **`f`** | `<npc> <int>` …（成对，可多对） | 对每个 NPC 好感点 ≥ N | :218-219 |
| 18 | `FestivalDay` | （无） | — | 今天是节日 | :237 |
| 19 | `NotFestivalDay` | `F` | — | 今天不是节日（⚠️ Obsolete） | :245-246 |
| 20 | `Random` | `r` | `<float>` | 概率命中（`NextDouble() <= p`） | :253-254 |
| 21 | `Shipped` | `s` | `<itemId> <int>` … | 各物品出货数 ≥ N | :265-266 |
| 22 | `SawSecretNote` | `S` | `<int>` | 看过该秘密纸条 | :284-285 |
| 23 | `ChoseDialogueAnswers` | `q` | `<answerId>` …（全部） | 选中过全部给定对话答案 | :296-297 |
| 24 | `LocalMail` | `n` | `<mailId>` | **本玩家**收过该邮件 | :315-316 |
| 25 | `GoldenWalnuts` | `N` | `<int>` | 全体金核桃 ≥ N | :327-328 |
| 26 | `NotLocalMail` | `l` | `<mailId>` | 本玩家没收过（⚠️ Obsolete） | :340-341 |
| 27 | `InUpgradedHouse` | `L` | `[minLevel=2]` | 当前是农舍/小屋且 ≥ 等级 | :348-349 |
| 28 | **`Time`** | **`t`** | `<min> <max>` | 时刻在 [min,max] 闭区间 | :365-366 |
| 29 | `Weather` | `w` | `<rainy\|sunny\|id>` | 天气匹配 | :381-382 |
| 30 | `DayOfWeek` | （无） | `Mon Tue …`（任一） | 星期几 | :401 |
| 31 | `NotDayOfWeek` | `d` | 同上 | 非这些星期（⚠️ Obsolete） | :425-426 |
| 32 | **`Spouse`** | **`O`** | `<npc>` | **已婚或订婚**于该 NPC | :433-434 |
| 33 | `NotSpouse` | `o` | `<npc>` | 配偶不是该 NPC（⚠️ Obsolete） | :446-447 |
| 34 | `Roommate` | `R` | — | 有室友 | :454-455 |
| 35 | `NotRoommate` | `Rf` | — | 无室友（⚠️ Obsolete） | :463-464 |
| 36 | `NpcVisible` | `v` | `<npc>` | 该 NPC 在任意位置且可见 | :471-472 |
| 37 | `NpcVisibleHere` | `p` | `<npc>` | 该 NPC **在当前地点**且可见 | :488-489 |
| 38 | **`Season`** | （无） | `<season>` … | 当前季节命中 | :507 |
| 39 | `NotSeason` | **`z`** | `<season>` … | **不是**该季节（⚠️ Obsolete，语义取反！） | :526-527 |
| 40 | `SpouseBed` | `B` | — | 家里有配偶床 | :534-535 |
| 41 | `ReachedMineBottom` | `b` | `[minTimes=1]` | 到达矿底次数 ≥ N | :542-543 |
| 42 | **`Year`** | **`y`** | `<int>` | N==1 → **恰为**第 1 年；N≠1 → **≥ N** 年 | :554-555 |
| 43 | `Gender` | `g` | `<male\|female>` | 玩家性别 | :570-571 |
| 44 | `HasItem` | `i` | `<itemId>` | 背包/手持持有 | :583-584 |
| 45 | `NotSawEvent` | `k` | `<id>` … | 未看过任一（⚠️ Obsolete，改用 `!e`） | :604-605 |
| 46 | `Tile` | `a` | `<x> <y>` … | 玩家站在给定格 | :612-613 |
| 47 | `ActiveDialogueEvent` | （无） | `<id>` | 该对话事件激活 | :645 |
| 48 | `NotActiveDialogueEvent` | `A` | `<id>` | 未激活（⚠️ Obsolete） | :657-658 |
| 49 | **`SendMail`** | **`x`** | `<mailId> [inMailbox]` | **发信伪事件**：发信、标记事件已看、**恒返回 false** | :666-667 |
| 50 | `DayOfMonth` | `u` | `<int>` … | 月内第 N 天（任一） | :687-688 |
| 51 | `UpcomingFestival` | （无） | `<int>` | N 天内将有节日 | :708 |
| 52 | `NotUpcomingFestival` | `U` | `<int>` | N 天内无节日（⚠️ Obsolete） | :736-737 |
| 53 | **`GameStateQuery`** | **`G`** | `<query>` | **任意 GSQ** | :744-745 |
| 54 | `Skill` | （无） | `<name> <int>` | 技能等级 ≥ N | :757 |

**计数**：`public static bool` 方法 **54** 个（`Preconditions.cs` 内 `grep -c` 实测）；无别名的方法恰 6 个（`FestivalDay` / `DayOfWeek` / `Season` / `ActiveDialogueEvent` / `UpcomingFestival` / `Skill`），`[OtherNames]` 条目 **48** 个 → **54 + 48 = 102 个可用令牌**。

**实测语料佐证（`D:\sdv_merged\Data\Events`，662 条键）**：出现 41 种前导令牌，覆盖上表 25 个；
使用频次前列：`f` 366 / `t` 347 / `e` 289 / `w` 235 / `z` 70 / `p` 60 / `n` 60 / `H` 56 / `O` 52 / `o` 52。
真实语料里出现的 GSQ 写法：`…/G !IS_PASSIVE_FESTIVAL_TODAY SquidFest`（`Beach.json`）——**直接坐实 `G` 是 GSQ 入口**。

---

## 3. ★ 维度 × `Data/Events/*` → 能否承载 / 最简写法 / 条数

「条数」= 语料里 **`Target` 以 `Data/Events/` 开头、且 `When` 命中该维度**的补丁数，口径与 `tools/variant-per-type.py` 完全一致（已复现该工具输出的 130/60/13/4）。

| # | CP `When` 维度 | 能否承载 | 承载令牌 | 最简写法（键内一段） | 语义对齐度 | 条数 |
|---|---|---|---|---|---|---|
| 1 | **看过的剧情** `HasSeenEvent` | ✅ **完全能** | `e` / `k` | `e <id>`（多个 ID 直接空格并列；取反写 `k <id>` 或 `!e <id>`） | ✅ **完全一致**：`SawEvent` 是「任一命中」（`Preconditions.cs:19-29`），CP 也判「任一命中」（`Condition.cs:77`） | **130** |
| 2 | **婚姻** `Spouse` / `Relationship:* Married` | ✅ **能（有语义差）** | `O <npc>` / `o` / `R` / `Rf` / `B`；精确写 `G` | `O <npc>`；要区分「订婚 vs 已婚」或「离婚」时必须 `G PLAYER_NPC_RELATIONSHIP Current <npc> Married` | ⚠️ `O` = **已婚**或**订婚**（`Preconditions.cs:440`）；CP `Relationship…Married` 只要已婚 | **60** |
| 3 | **好感度** `Hearts:X` | ✅ **能（需换算）** | `f <npc> <points>` | `f <npc> <hearts × 250>` | ⚠️ CP 是**心数**（可小数，5.5 心 = 1375 点），事件令牌是**点数**；`f` 用 `Friendship.Points`（`Preconditions.cs:227`），非整数心须 ×250 后取整 | **13** |
| 4 | **年份** `Year` / `IsOddYear` | ⚠️ **仅连续区间能**；离散集合（**含奇偶**）❌ | 区间 `G YEAR <min> <max>`；单点 `y 1` | `Year: "1"` → `y 1`；`Year: "2 20"` → `G YEAR 2 20` | ⚠️⚠️ **本行 2026-10-06 二次订正**：① `y n`（n≠1）**不是相等**而是 **≥ n（上无界）**（`Preconditions.cs:551-566`）—— 原写的 `"2,3,…,20" → y 2` 会把 20 年以后也**误命中**；② GSQ **没有任何年代奇偶关键字**（`GameStateQuery.cs` 130 个关键字全列无 `IsOddYear`/`IsEvenYear`，`YEAR` 只吃 `min max` 闭区间，见 `:411-422`）—— 原写的「奇偶用 `G`」是**错的**。`IsOddYear` 实为 **Ridgeside 内容包自建的 DynamicToken**（`{{Query: {{Year}} % 2 = 1}}`） | **4** |
| 5 | **季节** `Season`（背景未列，但同表可回收） | ✅ 能 | `Season <s>`（正）/ `z <s>`（**反**） | 正：`Season spring`；反：`z spring` 或 `!Season spring` | ⚠️ **`z` 是 `NotSeason`，语义与外号直觉相反**；且 `Season` 的多参数有实现瑕疵（见 §8） | **3** |

**四维并集（去重补丁）**：**181** 条；并入季节维为 **184** 条。
**另**：若改用逐维「计数对数」相加 = 130+60+13+4 = **207**（同一补丁命中多维度会重复计入；实测重叠 26，见 §7）。
与既有文档 `docs/superpowers/specs/2026-10-06-variant-normalizer-design.md:232-237` 的「≈190」一致（该处对婚姻维估的是 ≈40，实测为 60）。

---

## 4. ★ 更正：`x` **不是** GSQ 入口，`G` 才是

任务书假设「`x` 前缀是通用 GSQ 入口」。**源码与实测都否定了它。**

| 令牌 | 规范名 | 真实行为 | 源码 |
|---|---|---|---|
| **`x`** | `SendMail` | **发信伪事件**：`Game1.addMailForTomorrow(mailId)` / 进邮箱，然后 `Game1.player.eventsSeen.Add(eventId)`，**`return false`** —— 事件脚本永不执行 | `Preconditions.cs:663-683` |
| **`G`** | `GameStateQuery` | `Args[1..]` 用空格重拼成整串 → `GameStateQuery.CheckConditions(text, location)` | `Preconditions.cs:742-753` |

```csharp
// Preconditions.cs:744-753 —— G 是真·通用入口
[OtherNames(new string[] { "G" })]
public static bool GameStateQuery(GameLocation location, string eventId, string[] args)
{
    string text = ArgUtility.UnsplitQuoteAware(args, ' ', 1);
    if (string.IsNullOrWhiteSpace(text))
        return Event.LogPreconditionError(location, eventId, args, "must specify a game state query");
    return StardewValley.GameStateQuery.CheckConditions(text, location);
}
```

`GameStateQuery.CheckConditions`（`GameStateQuery.cs:2074`）= **游戏里所有 `Condition` 字段用的同一个引擎**，共 **123** 个查询键（`grep -oP 'public static bool \K[A-Z_0-9]+' GameStateQuery.cs | sort -u | wc -l`），含 `SEASON` `YEAR` `TIME` `DAY_OF_WEEK` `DAY_OF_MONTH` `WEATHER` `PLAYER_HEARTS` `PLAYER_NPC_RELATIONSHIP` `PLAYER_HAS_SEEN_EVENT` `IS_PASSIVE_FESTIVAL_TODAY` `LOCATION_SEASON` 等；且**逗号 = AND 列表、`ANY`/`ALL` 等组合子齐备**（`GameStateQuery.cs:251`）。

**实证**：
- 原版事件键 `288847/…/G !IS_PASSIVE_FESTIVAL_TODAY SquidFest`（`D:\sdv_merged\Data\Events\Beach.json`）。
- 语料里 `x` 键的**值全是 `null`**（如 `7472145/f Andy 1753/x AndyJojaCola` → `"null"`，SVE `code/other/mail.json`）—— 正是「伪事件、只为发信」的形状。

> **所以任务书第 3 问的答案**：`x` 不是 GSQ 入口；**但「存在一个通用 GSQ 入口」这个结论成立，入口是 `G`**。因此「几乎所有维度都能装」也成立 —— 装不下时一律退到 `G`。

---

## 5. 容量：一段键里能塞多少条件

| 问题 | 答案 | 依据 |
|---|---|---|
| 代码有上限吗？ | **没有**。`for (int i = 1; i < array.Length; i++)` 只受字符串长度限制 | `GameLocation.cs:16466-16472` |
| 段内（同一令牌）有上限吗？ | **没有**。`f`/`e`/`s`/`q`/`u` 等内部都是 `for (… < args.Length …)` | `Preconditions.cs:19,221,268,299,691` |
| 段之间什么逻辑？ | **AND** | `GameLocation.cs:16466-16472` |
| 段内多参数什么逻辑？ | 因令牌而异：`e`/`q`/`S`/`N`… = **任一**；`f`/`s` = **逐对全部满足** | 同上各方法体 |
| 实测最深键 | **27 段（= 1 ID + 26 个前置条件）** —— `Saloon.json` 的 `195099/f Shane 2500/…/k 195013`、`HaleyHouse.json` 的 `195019/…` | `D:\sdv_merged\Data\Events\Saloon.json` |

**实测段数直方图**（原版+RSV 共 662 键）：`1:172, 2:36, 3:95, 4:115, 5:119, 6:67, 7:35, 8:13, 9:4, 12:1, 15:1, 26:2, 27:2`。
**带 ≥2 个前置条件（即 ≥3 段）的键有 454/662 = 68.6%**——多条件组合是常态，不是边角。

---

## 6. 最简模型：带条件的 `Data/Events` 补丁

**结论：目标键直接写成 `<事件ID>/<前置条件段>…`，其余一切照旧。**

```
{
  "<资产相对路径>": {
    "<unique-event-id>/<token> <args>/<token> <args>": "<事件脚本>"
  }
}
```

三条落地写法，按条件复杂度选：

| 场景 | 最简键 |
|---|---|
| 只含上表某维度 | `<id>/e 5553210`、`<id>/f Abigail 2500`、`<id>/O Penny`、`<id>/y 2`、`<id>/Season spring` |
| 多维度 AND | 直接并列：`<id>/e 5553210/Season spring/y 2/t 600 1800` |
| 任意 CP `When`（含 GSQ、含多分支、含取反） | **单段收口**：`<id>/G <完整 GSQ>` |

**前提（全部必须有源码依据）**：

1. **ID 必须是键的第一段**，且**不含 `/`** —— `PlayEvent` 走的是朴素 `key.Split('/')[0] == eventId`（`Game1.cs:11980`）。
2. **ID 是 `eventsSeen` 的键**（`Event.cs:4747-4754`）→ 同一 ID 被第二条键复用，第一次播完第二条就永久失效（`GameLocation.cs:16462`）。
   想表达「同一内容、两套条件」**只能**：① 两个不同 ID，或 ② 一个 ID + `G (A), (B)` 之类**单段内合并**。
3. **段不能为空**（`RemoveEmptyEntries`）；段内空格分隔，引号可保护空格（`ArgUtility.cs:51-65`）。
4. **顺序有意义**：`checkForEvents` 遍历 `events2.Keys`，**首个通过者 break**（`GameLocation.cs:15658-15666`）。原版 `Dictionary` 无移除时大体按插入序；**合并多条同 ID 变体时不能依赖这件事**（见前提 2）。
5. **`G` 段里不能出现裸 `/`**（会被键切分吃掉）；含 `/` 只能靠引号保护，而引号会被 `UnsplitQuoteAware` 重新转义 —— **此路径未实测，标「未确证」**。
6. **CP `Fields` 型补丁**（改既有条目的某个命令）落在**既有键**上，必须先按 ID 找到那条键再改。语料里本次四个维度 **0 条**走 `Fields`（§7），但通用实现仍须处理。

---

## 7. 量化（可复现）

**口径**：`report.json` 的每条补丁 → 按 `source`+`index` 回到原始 `content.json` 的 `Changes[i].When` → 展平成字符串 → 正则匹配维度 → 按 `Target` 分桶。**与仓库既有工具 `tools/variant-per-type.py` 完全一致**。

```
$ python tools/variant-per-type.py D:/tmp/corpus_b2/*/report.json "D:/[CP] Ridgeside Village.report.json"
--- 看过的剧情：1024 条 … 130  12%  Data/Events/*
--- 婚姻：634 条 …            60   9%  Data/Events/*
--- 年份：132 条 …              4   3%  Data/Events/*
--- 好感度：104 条 …           13  12%  Data/Events/*
--- 季节：839 条 …              3   0%  Data/Events/*
合计 2733 条带维度条件的补丁
```

**逐条汇总**：

| 维度 | 能否转 | 计数对数 | 去重补丁 | 条目键**已含** `/` | 条目键**不含** `/` | 用 `Fields` 的补丁 |
|---|---:|---:|---:|---:|---:|---:|
| 看过的剧情 | ✅ | 130 | 130 | 128 | 19 | 0 |
| 婚姻 | ✅（语义差） | 60 | 60 | 59 | 8 | 0 |
| 好感度 | ✅（×250 换算） | 13 | 13 | 9 | 4 | 0 |
| 年份 | ✅（`IsOddYear` 需 `G`） | 4 | 4 | 4 | 0 | 0 |
| **四维合计** | | **207**（含重复） | **181**（并集） | | | **0** |
| 季节（附带） | ✅ | 3 | 3 | 3 | 0 | 0 |
| **五维合计** | | | **184**（并集） | | | |

**命中多维度组合的补丁分布**（合计 184）：

```
112  仅看过的剧情
 40  仅婚姻
 12  婚姻 + 看过的剧情
  8  好感度 + 婚姻
  5  好感度 + 看过的剧情
  3  仅年份
  3  仅季节
  1  年份 + 看过的剧情
```

**结论**：
- **可回收量 = 181 个不同的 `Data/Events/*` 条件补丁**（含季节为 184），对应既有文档里的「≈190」。
- 其中 **203/234 个条目键本来就带 `/`**（128+59+9+4+3 = 203 带 `/`，19+8+4 = 31 不带，季节 0 不带）→ **绝大多数补丁本来就在写键内前置条件，我们的工作往往是「改写/追加一段」，而不是从零构造**。
- 这 184 条的那部分我们此前判为「无落点」；按 §3 全部可承载，**唯一必须先处理的是令牌语义差（婚姻的订婚、好感度的心→点换算、年份的奇偶）**。

---

## 8. 已知坑（写转换器前必须知道）

| # | 坑 | 源码/实证 |
|---|---|---|
| 1 | **`z` 是「非」季节**，不是季节 | `Preconditions.cs:526-527`（`NotSeason`）；原版常见写法 `z spring/z fall/z summer` = 冬季 |
| 2 | **`Season` 多参数实现有瑕疵**：循环里恒取 `args[1]`，故 `Season spring summer` 只判 spring | `Preconditions.cs:509-519`（`TryGetEnum<Season>(args, 1, …)` 在 `for (i…)` 内**未用 i**）。要表达「多季节之一」应拆成多段或走 `G` |
| 3 | **`y 1` 与 `y n` 语义不同**：`1` = 恰好第 1 年，其他 = ≥ N | `Preconditions.cs:561-565` |
| 4 | **`j` 是严格大于**：`Game1.stats.DaysPlayed > value`（不是 ≥） | `Preconditions.cs:205` |
| 5 | **`O` 含订婚**：`Game1.player.spouse == value`，而 `spouse` 在订婚时就已写入 | `Preconditions.cs:440` |
| 6 | **未知令牌 = 静默不触发**（只 Warn） | `Event.cs:4197-4201` |
| 7 | **令牌表不是闭集**：mod 可自注册（RSV 的 `rsvRidingHorse`） | `Event.cs:4068-4084`；`D:\rsv_src\RidgesideVillage.decompiled.cs:6140` |
| 8 | **13 个令牌已标 `[Obsolete]`**：12 个 `Not*` 别名（`Hl`/`*l`/`X`/`F`/`l`/`d`/`o`/`Rf`/`z`/`k`/`A`/`U`）+ `x`（`SendMail`）。新内容应写 `!规范名`；转换器改写时应归一化到规范名 | `Preconditions.cs` 各处 `[Obsolete]`（:75,:116,:173,:244,:339,:424,:445,:462,:525,:603,:656,:665,:735） |
| 9 | 值（脚本）的切分与键不同（额外做 gender 切换、token 展开、Trim） | `Event.cs:11728-11733` vs `:11720-11723` |

---

## 9. 不确定 / 未确证

| 项 | 状态 |
|---|---|
| `G` 段内出现裸 `/`（如 CP `{{Mod/Token}}`）能否正确承载 | **未实测，但语料暴露面 = 0（2026-10-10 量过）** —— 扫了全部 32 包的 `When`，**值里带斜杠的 0 处**（`HasMod`/`ModId`/`HasFile` 尤其没有）。所以不追这条路：真遇到再实测。理论上可用引号保护（`ArgUtility.cs:56-59`），但 `G` 侧 `UnsplitQuoteAware` 会重新加引号/转义（`ArgUtility.cs:213-226`），round-trip 确实没测过 |
| `Season` 多参数的常量折叠式（JIT 是否会消除循环） | **未确证**（结论基于反编译源码逐字读；即便被优化，也只影响 args[1] 之外的参数是否被忽略，不改变「别依赖多参数」的建议） |
| 多维度补丁合并到同一 ID 时的字典遍历顺序保证 | **未确证**。`Dictionary` 顺序非契约；本报告的建议是**不要依赖它**（改走不同 ID 或单段 `G`） |
| 「190」的原始口径 | 既有文档写「≈190」（`variant-normalizer-design.md:232`），本次精算为 **184（五维并集）/ 181（四维并集）**；差额来自当时把婚姻维估成 ≈40（实测 60） |
| `x` 在**原版 1.5 内容**里是否曾是别的语义 | 未查（1.5 内容未在本次输入范围）。1.6.15 反编译里 `x` 唯一映射到 `SendMail` |

---

## 10. 给 `EntryKeyConditions` 这一档的三条落地判据

1. **判据**：`Target` 以 `Data/Events/` 开头（大小写不敏感）→ 条目键即条件槽，落点 = `EntryKeyConditions`。
2. **可自动转换**：`When` 的全部叶子能被 §2 的 102 令牌逐一表达（或整体收口成一段 `G <GSQ>`），且无自注册令牌、无 `Fields`。
3. **必须人工**：命中 §8 的坑 2/3/6/7，或 `When` 含 mod 自定义令牌/CP 令牌（会引入 `/`）。
