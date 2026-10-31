# schedules 宿主的季节承载能力（原版机制精算）

日期：2026-10-06
依据：`D:\sdv_src`（1.6 反编译）+ `D:\sdv_tree`（原版 Content 的 JSON 导出）+ 语料 B 原始 `content.json`
配套：`2026-10-06-key-encoded-variants.md`、`2026-10-06-implicit-conventions.md`、`2026-10-06-cross-corpus-survey.md`

> 一句话：**四季 `$query` 装不下**（硬上限 2 分支）；但「一季一条日程」这个**意图本身**有原生形式——
> 只是它住在 `Characters/schedules` 的**键名**里，不在 `Strings/schedules` 的**值**里。两者被混为一谈是本格判错的根源。

---

## 0. 结论速览（载体 × 季节 → 最简形式 / 前提 / 条数）

| 载体 | 季节维 | 最简形式 | 前提 | 源码 | 实测条数 |
|---|---|---|---|---|---|
| `Characters/schedules/<NPC>` 裸季节键 `spring`/`summer`/`fall`/`winter` | ✅ **有** | 键名即季节 | 条件只含季节 | `NPC.cs:5878` | — |
| `Characters/schedules/<NPC>` `<season>_<day>`（如 `spring_13`） | ✅ **有** | 组合键 | 季节 + 日 | `NPC.cs:5822` | — |
| `Characters/schedules/<NPC>` `<season>_<dayName>`（如 `spring_Sun`） | ✅ **有** | 组合键 | 季节 + 星期 | `NPC.cs:5855-5861` | — |
| `Characters/schedules/<NPC>` `GOTO season` | ✅ **有** | token 跳转到当季键 | — | `NPC.cs:5539-5550` | — |
| `Characters/schedules/<NPC>` `marriage_<dayName>` + `When.Season` | ❌ **装不下** | 链里只有 `marriage_<season>_<day>`，**没有** `marriage_<season>_<dayName>` | — | `NPC.cs:5802` / `5899` | 43（另有 7 条其他条件） |
| `Strings/schedules/<NPC>` 值内 `$query` 求 4 季 | ❌ **装不下** | `$query` 恰 2 分支；嵌套坏 | 季数 ≤ 2 | `Dialogue.cs:651-652` | **222** |
| `Strings/schedules/<NPC>` 单季 + 任意 GSQ | ✅ **有** | `$query <GSQ>#真|假` | 旧值可静态预期 | `Dialogue.cs:646-652` | **3** |

**`Strings/schedules` × 季节 = 225 条**（任务书作 228，见 §5 口径说明）。
判定：**装不下 222 / 有最简形式 3 / 真无 0**。

---

## 1. `Strings/schedules/*` 到底是什么，谁在读，读了干什么

### 1.1 它不是日程，是「日程命令引用的一段对话文本」

原版 `Strings/schedules/<NPC>` 是 `Dictionary<string,string>`。原版只有一个读取方式——
**由 `Characters/schedules/<NPC>` 的**值**里显式引用一个带引号的资产路径**：

```jsonc
// D:\sdv_tree\Characters\schedules\Abigail.json（原版）
"spring_4": "900 SeedShop 11 5 0 \"Strings\\schedules\\Abigail:spring_4.000\"/1230 Hospital 13 14 0 \"Strings\\schedules\\Abigail:spring_4.001\"/..."
```

对应的 `Strings/schedules/Abigail.json` 收着：

```jsonc
"spring_4.000": "Ugh... I have a doctor's appointment later.$s",
"spring_4.001": "I'm just here for my annual check-up! ...",
"Sun.000":      "We're hiding in here so we don't have to talk to anyone."
```

**键名是任意的**（原版约定 `<scheduleKey>.<NNN>`，但游戏不解析它）。**没有任何调用点写死
`"Strings\\schedules\\..."` 资产名**——全库 `Grep "Strings/schedules"` 只命中测试文件
`StardewValley.Tests\SyntaxAbstractor.cs:37`（它把该资产的值按 **Dialogue 语法**做国际化一致性校验，
这本身就是官方对「它是对话式字符串」的背书）。

### 1.2 读取链（每一环都是题面要的调用点）

| 步 | 位置 | 做什么 |
|---|---|---|
| 1 | `NPC.cs:5677` / `5685` | 解析 `Characters/schedules` 值时，遇到第 4 个参数以 `"` 开头 → 存成 `endMessage`（原样保留资产路径） |
| 2 | `NPC.cs:4621-4625` | `getRouteEndBehaviorFunction` 把 `endMessage` 写进 `nextEndOfRouteMessage`，最终落到 `endOfRouteMessage.Value`（`NPC.cs:372`） |
| 3 | `NPC.cs:4342` | 到达路线终点时 `_PushTemporaryDialogue(endOfRouteMessage.Value)` |
| 4 | `NPC.cs:4356-4376` | **`new Dialogue(this, translationKey)`**（`NPC.cs:4371`）。注意 `4363`：若存在 `<key>_<friendshipStatus>` 会先改选那个变体 |
| 5 | `Dialogue.cs:588` `parseDialogueString` | 解析对话命令：`$query` 在 `Dialogue.cs:646-652` |

**所以 `Strings/schedules/*` 值 = 对话语法宿主**，与 `Characters/Dialogue/*` 同一解析器。
`$query`、`$e`、`$b`、`%season` 等命令都生效。

### 1.3 `Characters/schedules/*` 自己怎么被读

`NPC.cs:5987-6011` `getMasterScheduleRawData()` → `Game1.content.Load<Dictionary<string,string>>("Characters\\schedules\\" + Name)`，
并用 `StringComparer.OrdinalIgnoreCase` 包一层（`NPC.cs:6002`）——**键名大小写不敏感**。
旁支读取：`Utility.cs:5796`（`IsHospitalVisitDay`，只看 `currentSeason + "_" + dayOfMonth`）、
`IslandSouth.cs:207`+`938`（姜岛度假村日程拼接）。

---

## 2. 四季装不下：`$query` 的确切失败点

### 2.1 `$query` 硬上限 = 2 分支

```csharp
// Dialogue.cs:646-653
if (!(text2 == "$query")) { goto IL_08a2; }
string queryString = commandArgs;
string[] array4 = ArgUtility.Get(masterString.Split('#', 2), 1)?.Split('|') ?? LegacyShims.EmptyArray<string>();
array2 = (GameStateQuery.CheckConditions(queryString)
          ? array4[0].Split('#')                       // 真分支
          : ArgUtility.Get(array4, 1, array4[0]).Split('#'));  // 假分支
i--;
```

**失败点一（分支数）**：`Dialogue.cs:651` 的 `Split('|')` **不带 limit**。
写 `$query Q#A|B|C|D` → `array4 = [A,B,C,D]`，而 `Dialogue.cs:652` **只取 `array4[0]`（真）与 `array4[1]`（假）**，
第 3、4 个分支**被静默丢弃**。→ 一个值**最多 2 个季节分支**。

**失败点二（嵌套不可救）**：`Dialogue.cs:651` 切的是**顶层** `masterString.Split('#', 2)`，
不是「当前分支的文本」。所以想在假分支里再嵌一层 `$query` 表达第 3、4 季时，
内层 `$query` 仍会去切**顶层**字符串 → 取到外层 `$query` 自己的文本 → 解析错乱。
（`2026-10-06-key-encoded-variants.md` §5.2 已记同一结论。）

**结论：4 个季节分支在单个 `Strings/schedules` 值里不可表达。** 这是**能力边界的精确位置**，
不是「没落地」也不是「原版没有」。

### 2.2 token 路线也救不了

- `%season`（`Dialogue.cs:121, 1155`）→ `str.Replace("%season", Game1.CurrentSeasonDisplayName)`。
  它只把**季节的显示名**填进文本（`Game1.CurrentSeasonDisplayName`），是**文本替换**，
  **不是分支选择器**。若四季文本只是「同一句话换个季节名」→ 可用；
  但语料 B 的四季值是**四段完全不同的台词**（见 §5.2）→ **不可用**。
- 不存在「按季节选 `Strings/schedules` **键**」的机制：引用是写死的资产路径字符串（§1.2 第 1、4 步），
  资产路径本身**不经过**任何 token/GSQ 解析。→ 把 4 季拆成 4 个键也接不上，除非同时改
  `Characters/schedules` 侧的引用——那已超出「改 `Strings/schedules` 的值」这 228 条的范围。

---

## 3. `Characters/schedules` 的键支不支持季节前缀（并纠正两处题面引用）

### 3.1 支持。判定点**不是** `Game1.cs:8296-8302`，也**不是** `GameLocation.cs:16442`

题面猜测的这两处**都不是**日程季节键的判定点：

- **`Game1.cs:8296-8302` = 邮件投递键**，与日程无关：
  ```csharp
  if (DataLoader.Mail(content).ContainsKey(currentSeason + "_" + dayOfMonth + "_" + year))
      mailbox.Add(currentSeason + "_" + dayOfMonth + "_" + year);
  else if (DataLoader.Mail(content).ContainsKey(currentSeason + "_" + dayOfMonth))
      mailbox.Add(currentSeason + "_" + dayOfMonth);
  ```
  这是 `Data/mail` 的 `<season>_<day>[_<year>]` 约定（同 `key-encoded-variants.md` §2）。
- **`GameLocation.cs:16440-16448` `GetSeasonalTilesheetName` = 地图瓷砖表的季节前缀**，
  判的是 `spring_/summer_/fall_/winter_` **前缀**、换的是 `Maps/*` 的 tilesheet（`GameLocation.cs:16427`），
  与日程键无关。

**真正的日程季节键判定点 = `NPC.cs` 的 `TryLoadSchedule()` 回退链**（`NPC.cs:5754-5905`）：

```csharp
// NPC.cs:5822  —— 季节 + 日
if (TryLoadSchedule(currentSeason + "_" + dayOfMonth)) return true;
// NPC.cs:5855  —— 季节 + 星期 + 好感度
if (TryLoadSchedule(currentSeason + "_" + text + "_" + num3)) return true;
// NPC.cs:5861  —— 季节 + 星期
if (TryLoadSchedule(currentSeason + "_" + text)) return true;
// NPC.cs:5878  —— 裸季节
if (TryLoadSchedule(currentSeason)) return true;
// NPC.cs:5882-5888 —— 硬编码 spring 兜底
if (TryLoadSchedule("spring_" + text)) return true;
if (TryLoadSchedule("spring")) return true;
```

`currentSeason` 是小写（"spring"…），所以 **`spring_13` / `spring_Sun` / `spring` 都是原生合法键**。
另有 `GOTO season`（`NPC.cs:5539-5550`）：把 `season` 当 token 换成 `Game1.currentSeason` 再 GOTO，
等于「一条日程按当前季节分发到 `spring`/`summer`/`fall`/`winter` 键」。

### 3.2 因此「季节 × `Characters/schedules`」= **「有原生形式」**

题面引 `2026-10-06-variant-normalizer-design.md` §9.4 的自相矛盾（Python 表 `None` vs C# 表 `None`、但 `KeyEncodedVariants` 又说 `_<season>` 前缀原生）——
**正解是「有」**，只是**有前提**：**仅当条件只含季节（或季节+日/星期）时**。
一旦与**天气/剧情/婚姻/年份**叠加，才落到无解：
键链里 **`rain`（`NPC.cs:5841-5851`）优先于裸 `spring`**，且**不存在 `spring_rain` / `rain_spring` 这类组合键**
（`key-encoded-variants.md` 记录的 15 种日程键形态里没有）→「某天且下雨」表达不了
（这正是 `cross-corpus-survey.md` 里 ~40 条 `Characters/schedules` 的结论）。
而**「季节 + 日/星期」可以**（`spring_13`、`spring_Sun`）。

### 3.3 一个连带发现：`marriage_<dayName>` + `When.Season` 装不下

链里只有 `marriage_<season>_<day>`（`NPC.cs:5802`）与 `marriage_<dayName>`（`NPC.cs:5899`），
**没有 `marriage_<season>_<dayName>`**。所以语料 B 那批 `marriage_Mon`+`When:{Season}` 补丁
**没有对应的原生组合键**。实测：`Characters/schedules` 带 `Season` 的 43 条里，
36 条属「叠了别的条件」（多为 `marriage_*` + `Day`），7 条其他。**季节 × 婚姻 = 无解**（与既有结论一致）。

---

## 4. 最简模型：「一季一条 schedule」用原版机制怎么写

**意图**：NPC 在每个季节走不同日程。
**最简形式**：把季节写进**键名**，值就是日程脚本，**不需要任何条件位**：

```jsonc
// Characters/schedules/<NPC>（烧成 XNB 后的形状）
{
  "spring": "900 SeedShop 39 5 0/1030 Town 73 54 2/1630 SeedShop 3 6 0/1930 SeedShop 1 9 3 npc_sleep",
  "summer": "900 SeedShop 39 5 0/1030 Railroad 30 45 0/...",
  "fall":   "900 SeedShop 39 5 0/1030 BusStop 22 5 2/...",
  "winter": "900 SeedShop 39 5 0/1030 ScienceHouse 5 19 1/..."
}
```

（这就是原版 `Characters/schedules/Abigail.json` 里的真实形态：`spring`/`summer`/`fall`/`winter` 四键俱在。）

**变体**：条件含日/星期时用组合键 `spring_13`、`spring_Sun`（甚至 `spring_Sun_2` 带好感度层，`NPC.cs:5855`）。

**前提条件（必须写进 EmitPlan，否则表不可信）**：

1. **条件只含季节**（或季节 + 日 / 星期）→ 用键名编码，成立。
2. 键链**优先级**：`GreenRain` > 姜岛 > 被动节日 > `marriage_*` > **`<season>_<day>`(5822)** > `<day>_<好感度>` > `<day>` > `bus` > **`rain`(5841)** > `<season>_<dayName>`(5861) > `<dayName>` > **`<season>`(5878)** > `spring_<dayName>`(5882) > `spring`(5886)。
   所以裸季节键**排在 `rain`、`<day>`、`<dayName>` 之后**——「裸 `spring`」只在当天没有更具体的键时兜底。
3. 季节与**天气/剧情/婚姻/年份/好感度**叠加 → **无解**（无组合键，或键链顺序吃掉语义）。

**反例（本文档要钉死的那格）**：若意图是「**同一个日程命令**引用的那段 `Strings/schedules` 文本，按季有 4 个值」，
则**上面的键名法接不上**——因为引用是写死的资产路径，键名解不了。
这时 4 季落在**值**里，撞上 `$query` 的 2 分支上限 → **装不下**。

---

## 5. 量化

数据源：`D:\Steam\steamapps\common\Stardew Valley\Mods` 全量 `content.json`（含 `Include` 展开）原始补丁体，
用 `D:\tmp\jsonc.py` 容错解析。**只读，未改动。**

### 5.1 口径说明

- 实测 `Strings/schedules` 且 `When` 含 `Season` 的补丁 = **225 条**。
- 任务书给的是 **228**。**差 3 条未复现**：可能是 RSV 原始包（本机只有 `D:\[CP] Ridgeside Village.report.json`，
  该 report **不含补丁体**，无法逐条重算）或另一版计数口径。**此差异标「未确证」**，不硬凑。

### 5.2 `Strings/schedules` × 季节 的形状分布（实测 225）

| 形状（`When` 键） | 条数 | 判定 |
|---|---|---|
| `{Season: X, "Season \|contains=<其余三季>": false}` —— **纯四季互斥** | **218** | ❌ 装不下（>2 分支） |
| 同上 + `HasSeenEvent` —— 四季 + 一条 GSQ | **4** | ❌ 装不下（>2 分支） |
| `{Season: spring, HasSeenEvent: true}` —— 单季 + 一条 GSQ | **1** | ✅ 有最简形式 |
| `{"Season \|contains=Summer": true, "Day \|contains=25": true, Relationship:…}` —— 单季 + 日 + 关系 | **2** | ✅ 有最简形式 |

**装不下 = 222 / 有最简形式 = 3 / 真无 = 0。**

四段值是**四段不同台词**（以 SVE 的 `CommunityDay_Lewis` 为例，同键 4 个互斥补丁）：

```jsonc
{"Season":"Spring", "Season |contains=Summer, Fall, Winter": false} → "CommunityDay_Lewis": "{{i18n:CommunityDay.Dialogue.01}}"
{"Season":"Summer", "Season |contains=Spring, Fall, Winter": false} → "CommunityDay_Lewis": "{{i18n:CommunityDay.Dialogue.02}}"
{"Season":"Fall",   "Season |contains=Spring, Summer, Winter": false} → "CommunityDay_Lewis": "{{i18n:CommunityDay.Dialogue.03}}"
{"Season":"Winter", "Season |contains=Spring, Summer, Fall":   false} → "CommunityDay_Lewis": "{{i18n:CommunityDay.Dialogue.04}}"
```

其引用侧是 `Characters/schedules/Lewis` 的**无条件**自定义键
`FlashShifter.StardewValleyExpandedCP_CommunityDay`，值里写死 `"Strings\\schedules\\Lewis:CommunityDay_Lewis"`。
→ 引用固定 + 值需 4 分支 = **正是 §2 的不可表达格**。

### 5.3 `Characters/schedules` × 季节（对照，实测 43）

| 形状 | 条数 | 判定 |
|---|---|---|
| `marriage_<dayName>` + `When.{Season[, Day]}` | 36 | ❌ 装不下（无 `marriage_<season>_<dayName>` 键） |
| 其他（`HasSeenEvent` 等，非纯季节） | 7 | ❌ 装不下 / 组合无解 |

**这 43 条里 0 条是「键名直接编码季节」**——语料 B 的季节日程**都写在 `Strings/schedules` 那一侧**，
`Characters/schedules` 侧反而都是婚姻/剧情组合。这解释了为什么造表工具会把「季节 × 日程」
误挂到 `Strings/schedules` 上：**载体名相似，语义相反**。

---

## 6. 未确证清单

| 项 | 状态 |
|---|---|
| 任务书 228 vs 实测 225 的 **3 条差** | **未确证**（疑 RSV 或口径；RSV report 无补丁体，无法重算） |
| `$query` 假分支里嵌套 `$query` 的具体错乱输出 | 由 `Dialogue.cs:651` 用**顶层** `masterString` 推得「必错」，**未实机跑** |
| 语料 B 是否有「季节 × `Strings/schedules` 但只有 2 季」的形态 | 本批**未见**（四季是全部）；不能断言生态里没有 |
| `When` 里 `Season: X` 与 `Season \|contains=其余三季: false` 中，**负子句是否多余** | 只影响「原样保留是否必要」，不影响可表达性，**未确证** |

---

## 7. 源码索引（本文档引用的每一处）

| 主题 | 位置 |
|---|---|
| `Strings/schedules` 值按对话语法校验（官方侧证） | `StardewValley.Tests\SyntaxAbstractor.cs:37` |
| 日程值里的引号消息 → `endMessage` | `NPC.cs:5677`、`NPC.cs:5685` |
| `endMessage` → `endOfRouteMessage.Value` | `NPC.cs:4621-4625`（`NPC.cs:372` 定义字段） |
| 终点推入对话 | `NPC.cs:4342` |
| **`new Dialogue(this, translationKey)`** | `NPC.cs:4356-4376`（尤其 4371；变体 4363） |
| 对话解析入口 / `$query` | `Dialogue.cs:588` / `Dialogue.cs:646-653` |
| `%season` token | `Dialogue.cs:121`、`Dialogue.cs:1155` |
| `Characters/schedules` 加载 | `NPC.cs:5987-6011`（大小写不敏感 6002） |
| 日程回退链（季节键） | `NPC.cs:5754-5905`（5822 / 5855 / 5861 / 5878 / 5882-5888） |
| `GOTO season` | `NPC.cs:5539-5550` |
| `<location>_Replacement` 键 | `NPC.cs:5430-5434` |
| 医院日程键判定 | `Utility.cs:5796-5798` |
| 姜岛度假村日程拼接 | `IslandSouth.cs:207-214`、`IslandSouth.cs:938` |
| ❌ 题面误引：邮件 `<season>_<day>[_<year>]` | `Game1.cs:8296-8302` |
| ❌ 题面误引：地图 tilesheet 季节前缀 | `GameLocation.cs:16439-16447`（调用点 16427） |
| 原版季节日程样本 | `D:\sdv_tree\Characters\schedules\Abigail.json`（`spring`/`summer`/`fall`/`winter`） |
| 原版 `Strings/schedules` 样本 | `D:\sdv_tree\Strings\schedules\Abigail.json` |
