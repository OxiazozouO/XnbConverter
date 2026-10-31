# 「键即条件」：原版用键名编码的变体维度

日期：2026-10-06
依据：`D:\sdv_tree` 全量 2267 个 json 的**统计式扫描**（变体配对测试）+ `D:\sdv_src` 逐条核对读取代码

> Task 10g 的配套文档。词表已落地在 `src/SdvCpTool.Core/Assets/KeyEncodedVariants.cs`，
> 本文件是它的**证据与理由**。

---

## 0. 为什么要有这份文档

`ConditionLanding` 曾把「字符串字典但键维度未识别」直接判成 `None`，
上层再翻译成 **`NoLandingPoint` = 断言「无处承载条件」**——**那是一句我们没有资格说的断言**。

实情是：原版有**一整族资产，条件就写在键名里，由调用方硬编码选键**。
这份文档是那族维度的**证据目录**，也是「判据不能只看 `/`」的依据。

---

## 1. 统计方法（可复现）

遍历 `D:\sdv_tree` 全部 2267 个 json（跳过 11 种 locale 副本），对每个 dict 做
**变体配对测试**：若键 `X_<tail>` 存在**且 `X` 也在同一 dict 中**，则该 `<tail>` 是一个变体后缀。

这个判据能自动排除人名/数字噪声，且**不依赖我们的猜测**——
**游戏支持某个约定，原版内容就一定会用它。**

---

## 2. 已确证的维度（A 档：游戏源码**显式读取**）

| 后缀 | 出现资产/次数 | 游戏读取点 | 语义 |
|---|---|---|---|
| `_yN` | 8 资产 / 276 键（仅 `Data/Festivals`）| `Event.cs:4417-4448` | **年份循环变体**：数出一共有几个 `_yN`，用 `Game1.year % n` 选 |
| `_spouse` / `_roommate` | 7 资产 / 58 键 | `Event.cs:12609-12665` | 节日里配偶/室友的专属台词。**先试 `_roommate` 后试 `_spouse`**，可与 `_yN` 叠加成 `<NPC>_spouse_y2` |
| `.<locale>` / `_international` | 11 语种 × 181–183 文件 / 4 个 | `LocalizedContentManager.cs:388-441` | 语言回退链 |
| **`_Male` / `_Female`** | **10+ 资产**（`Data/mail`、`Strings/UI` 52 键、`Strings/Shirts` 12 键…）| `FarmerTeam.cs:1315-1317` `genderedKey()` | **性别变体——全游戏分布最广的后缀维度** |
| `_dark` | `Characters/` 3 文件 | `Child.cs:109,113` | 肤色变体（`Toddler_girl_dark` 是三维叠加）|
| `_dangerous` | 地图名、怪物贴图名 | `MineShaft.cs:2907,1148` | 危险矿井变体，可与 `_dark` 叠加 |
| `_memory_oneday` … `_memory_oneyear` | `Characters/Dialogue/*` | `Farmer.cs:3617-3637` | 对话「记忆」时效变体 |
| `_sleep` | `Data/animationDescriptions`（28 键）| `NPC.cs:4559,5631` | 睡眠动画 |
| `_beach_towel` / `_beach_dance` / … | `Data/animationDescriptions` | `IslandSouth.cs:99-135` | 姜岛度假村活动动画 |
| `_Ambient` / `_Entry` | `Characters/Dialogue/*` | `NPC.cs:3396,3500` | 地点环境音/进入闲聊 |
| `_inlaw_<spouse>` / `_roommate_<spouse>` | `Characters/Dialogue/*` | `NPC.cs:4023,4031` | 姻亲/室友对话（**与 `_spouse` 是不同的系统**）|
| `_AfterWedding` / `_Engaged` | `Strings/StringsFromCSFiles` | `Event.cs:4635`、`NPC.cs:1661-1686` | 婚姻状态文案 |
| `_Replacement` | `Characters/schedules/*` | `NPC.cs:5430-5434` | 地点替换日程 |
| **日程键全家**（`marriage_<season>_<day>` 等 15 种形态）| `Characters/schedules/*`、`Strings/schedules/*` | `NPC.cs:5765-5900` 整段回退链 | **日程选择全是键名编码条件** |
| `<season>_<day>[_<year>]` | `Data/mail`（179 键）| `Game1.cs:8296-8302` | 邮件按日期自动投递 |
| `_Billed` / `_NotBilled` | `Data/mail` | `Farmer.cs:5908`、`GameLocation.cs:10566` | 晕倒邮件变体 |
| `_Winter` | `Characters/` 31、`Portraits/` 30 | `SkillsPage.cs:660` | 冬日星盛宴冬季贴图 |
| `_Beach` | `Characters/` 21、`Portraits/` 19 | `IslandSouth.cs:819`、`NPC.cs:1027,1060` | 姜岛着装贴图 |
| `_PaintMask` | `Buildings/` 24 文件 | `Building.cs:871` | 建筑涂装遮罩 |
| `<Season>_Objects` | `Maps/*.tbin` 的 MapProperties | `GameLocation.cs:17293` | 按季节决定是否刷物件 |
| **`spring_` / `summer_` / `fall_` / `winter_`（前缀）** | `Maps/*`、`Minigames` | `GameLocation.cs:16440-16448`、`BoatJourney.cs:415` | **季节前缀**维度的瓷砖表切换 |
| `Y<year>_`（前缀）| `Strings/1_6_Strings` | `DesertFestival.cs:971,976` | 沙漠节年度变体 |
| `changeSprite <actor> [suffix]` / `changePortrait` | 任意 `Characters/<Name>_*` | `Event.cs:2570-2589, 2665-2684` | **事件脚本可指定任意 `_suffix` 贴图** |

> ⚠️ **别只查后缀**：`spring_` / `Y<year>_` 是**前缀**维度。

---

## 3. B 档：条件由数据自身或调用方显式引用

这一档的关键性质：**选择逻辑不写代码，而写「哪个键」**。

| 键形 | 位置 | 引用它的地方 |
|---|---|---|
| `<NPC>_<状态>`（`Morris_Greeting` vs `_CommunityCenterComplete` …）| `Data/ExtraDialogue` | `JojaMart.cs:96-108` |
| `Mines_PlayerKilled_Spouse_PlayerMale` / `_Maru_Spouse` … | `Data/ExtraDialogue` | `GameLocation.cs:15566,15576` |
| `Town_DumpsterDiveComment_{Child,Teen,Adult}` | `Data/ExtraDialogue` | `GameLocation.cs:8483-8491` |
| `PurchasedItem_<n>_<QualityHigh>_<Rude>_<NPC>` | `Data/ExtraDialogue` | `FishShop.cs:47-61` |
| `DesertFestival_<NPC>[_marriage]` | `Strings/1_6_Strings` | 由日程键选中（`NPC.cs:5779-5803`）|
| `<scheduleKey>.<NNN>` | `Strings/schedules/<NPC>` | 由 `Characters/schedules/<NPC>` 显式引用 |
| `Farm_Standard` / `Farm_Beach` / … | `Data/Locations`（13 键）| 由农场类型/房屋升级决定 |
| `Festival_<Festival>_<Shop>` | `Data/Shops` | 由节日脚本显式开 |

---

## 4. 对工具的直接影响

### 4.1 `ConditionLanding` 的 `None` 语义必须收紧

**`None` 只用于「我们确证没有落点」**（结构认识清楚了，且确实没有条件位）。
**字符串字典但键维度未识别 → `Unknown`（→ `NeedsReview`）。**

### 4.2 判据不能用 `/`

`LooksLikeEventKeys` 的「至少一半的键含 `/`」**只适用于 `Data/Events/*`**。
把它当通用判据会漏掉整个 A/B 族——这正是 `Data/Festivals` 被误判的直接原因。

**改用「键的后缀或前缀命中维度词表」。**

### 4.3 转换规则只做「条件被键名蕴含」

例如 `When: {Season: spring}` 打在 `Data/mail:spring_13` 上 → **删条件、无条件写入该键**。
不蕴含 → `NeedsReview`，**不转**。

### 4.4 已知误报

`strings/StringsFromCSFiles` 里的 `SocialPage_Relationship_Single_Female/Male` 等键，
其 `_Male` / `_Female` **只是标签文本**，不是维度。由于探针只看键名、看不到补丁写的具体键，
该资产会被标为 `KeyEncodedVariant`。

**影响仅限报告里的 `suggestedChannel` 文案**——去向仍是 `NeedsReview`、不写、不转换。
**选择保留并如实报告，不做特例排除**（加特例会引入更隐蔽的规则）。

---

## 5. 相关：另外两条「条件落点」机制

### 5.1 事件前置条件 `G` = 内联任意 GSQ

```csharp
// Preconditions.cs:744-752
[OtherNames(new string[] { "G" })]
public static bool GameStateQuery(GameLocation location, string eventId, string[] args)
{
    string text = ArgUtility.UnsplitQuoteAware(args, ' ', 1);
    return StardewValley.GameStateQuery.CheckConditions(text, location);
}
```

### 5.2 对话值内联 `$query`

```csharp
// Dialogue.cs:646-652
if (!(text2 == "$query")) { goto IL_08a2; }
string[] array4 = ArgUtility.Get(masterString.Split('#', 2), 1)?.Split('|') ?? ...;
array2 = (GameStateQuery.CheckConditions(queryString)
          ? array4[0].Split('#') : ArgUtility.Get(array4, 1, array4[0]).Split('#'));
```

格式 `$query <GSQ>#<真分支>|<假分支>`。**原版自己就在用**（`Characters/Dialogue/Abigail.json` 的 `Mon8`）。

> ⚠️ **每个值只有 2 个分支**：嵌套 `$query` 会用**原始 masterString** 重新 `Split('#', 2)`，
> 所以同一键上两条条件补丁会导致后一条解析出前一条的真分支值。
>
> **正确规则：同键恰有一条条件补丁 + 至少一条无条件补丁（旧值可静态预期）才转。**
