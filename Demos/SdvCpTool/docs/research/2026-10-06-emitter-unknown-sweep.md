# 发射表「待查」清单的清理（2026-10-06）

日期：2026-10-06
数据来源：语料 B（32 个 CP 包）全量跑一遍的 `report.json`，用 `tools/emitter-cells.py` 汇总。

口径：`normalization.cells` 里 `kind == "Unknown"` 的格子 —— **既不是「有」也不是「无」**，
是**待查**。这份文档记的是「这一轮把哪些格查成了结论、依据是什么、哪些**有意**留着不判」。

---

## 1. 数字

| | 格数 | 条数 |
|---|---|---|
| 清理前 | 24 | 45 |
| 清理后 | **2** | **5** |

**剩下的 2 格都是「有意留 Unknown」**（§3），不是没查。

---

## 2. 这轮判成结论的格

### 2.1 判 `Native`（原版有原生最简形式）

| 格 | 结论 | 依据 |
|---|---|---|
| `(Entry, Year, Strings/schedules)` | `$query YEAR <min> <max>#真\|假` | `Dialogue.cs:646-652`（宿主）+ `GameStateQuery.cs:411-422` |
| `(Entry, SeenEvent, Data/Shops)` | `<id>/Owners[]/Condition` 或 `<id>/Items[]/Condition` | 台账 §1.2（`ShopBuilder.cs:117/:122, :211`） |
| `(Entry, SeenEvent, Data/ExtraDialogue)` | 值内联 `$query` | `GameLocation.cs:8483/8487/8491/10000`；`Game1.cs:8929` |
| `(Entry, {Hearts, Marriage, Year}, Data/ExtraDialogue)` | 同上 | 同上 |
| `(Entry, SeenEvent, Data/EngagementDialogue)` | 值内联 `$query` | `NPC.cs:1660/1681/3848/3852` |
| `(Entry, {Hearts, Marriage, Year}, Data/EngagementDialogue)` | 同上 | 同上 |
| `(Entry, SeenEvent, Data/Locations)` | 数组元素上的 `Condition` | 台账 §1.2（`GameLocation.cs:13977, :15142, :14248, :3417`） |
| `(AssetName, Season, Tilesheets/*)` | 同 `(Sprite, 季节)`：四季图集前缀 | `GameLocation.cs:16439-16447` |
| `(AssetName, Season, Characters)` | `<NPC>.Appearance[]` 写 `Season` | `NPC.cs:957-1005` |

### 2.2 判 `None`（原版没有机制 —— 这是**结论**）

| 格 | 一句话依据 |
|---|---|
| `(Entry, Hearts, Data/NPCGiftTastes)`、`(Entry, SeenEvent, Data/NPCGiftTastes)` | `Dictionary<string,string>`，值是空格分隔的物品 ID（`NPC.cs:1413-1414`）—— 无条目对象、非对话宿主 |
| `(Entry, Season, Data/Fish)`、`(Entry, SeenEvent, Data/Fish)` | `Dictionary<string,string>`，值是 `/` 分隔的鱼数据串（`DataLoader.cs:222-228`） |
| `(Entry, SeenEvent, Data/Monsters)` | 同上（`DataLoader.cs:364-367`） |
| `(Entry, SeenEvent, Data/CraftingRecipes)` | 第 5 段叫「解锁条件」但**只认技能要求** `s <技能> <等级>`（`CraftingRecipe.cs:34, :220-224`）—— **不是 GSQ** |
| `(Entry, SeenEvent, Data/mail)` | 条件化投递走 `Data/TriggerActions`，信件条目自身没有 GSQ 位 |
| `(Entry, Season, Strings/animationDescriptions)` | 1.6 里**没有任何运行时读取点** |
| `(Entry, SeenEvent, Data/ConcessionTastes)` | `ConcessionTaste` 模型只有 Name/LovedTags/LikedTags/DislikedTags |
| `(Entry, Hearts, Data/BigCraftables)` | `BigCraftableData` 模型里没有条件字段 |
| `(Sprite, Hearts, Tilesheets/*)` | 图集资产的条件位写死在 C# 里 |

### 2.3 判 `CapacityExceeded`（有机制，但装不下这一格的全部情形）

| 格 | 为什么装不下 |
|---|---|
| `(Entry, {Hearts, Marriage}, Strings/StringsFromMaps)` | `$query` 只在该资产的**部分**消费点生效：走 `new Dialogue(...)` 的（`NPC.cs:4696-4698`）会执行，走 `DialogueBox(string)` 的（`Game1.cs:9648-9654`）是死文本，而键走哪条由**别处**（地图 `Action`）决定 |
| `(AssetName, SeenEvent, Characters/Dialogue)` | 值层面确有 `$query`，但 `Load` 是**整文件替换** ⇒ 假分支要的「合并前旧值」对新键根本不存在 |
| `(AssetName, SeenEvent, Strings/schedules)` | 同上 |

> ⚠ `CapacityExceeded` 这一档的**定义**这轮补宽了：原先只指「分支数超容量」（`$query` 两分支 vs 四季），
> 现在同时涵盖「机制只在部分消费点 / 部分字段上生效」。两条都有实证，`EmitKind` 的文档已写明。

---

## 3. 有意**留着 Unknown** 的两格（不是没查）

| 格 | 条数 | 为什么故意不判 |
|---|---|---|
| `(AssetName, Year, Maps)` | 4 | SVE 有 4 条 `Load Maps/Town-Halloween`，按 `Year: "1,5,9,…"`（≡1 mod 4）与 `"3,7,11,…"`（≡3 mod 4）分组 —— 万圣节节日地图按年份换外观。机制上**也许**能挂到 `Data/Festivals/fall27` 的 `_yN` 姐妹键（`Event.cs:4417-4448`），但那要 ①**跨资产改写**（从 `Maps/*` 的 `Load` 改到 `Data/Festivals` 的键）②按「模数由兄弟键数出来」那条规则补齐 `_y2`/`_y4`。**没确证，不做** |
| `(Entry, Marriage, "Character/Dialogue/Sophia")` | 1 | **不是知识缺口，是 mod 拼错了资产名**：SVE 写 `Character/Dialogue/Sophia`（少一个 `s`），资源树里既没有 `Character/` 目录也没有它对得上的资产 —— 这条产物游戏永远读不到。写成 `Characters/Dialogue/Sophia` 的话落的就是表里已有的 `(Entry, Marriage, Characters/Dialogue) → Native` 那一格 |

---

## 4. 顺带记下的一条**否定性结论**：`$query` 表达不了「条件性存在」

这一条关掉了语料里**最大的一类** `NeedsReview`（385 条里的绝大多数）。

**问题**：CP 里 `EditData` 带 `When` 常常是「**条件成立时才加这个条目**」
（新增对话键、新增日程键）。看上去 `$query <GSQ>#<新值>|<假分支>` 能接 —— 假分支留空不就行了？

**不行，而且原因在游戏侧**：

```
NPC.tryToRetrieveDialogue(...)            // NPC.cs:4012 起
    Dialogue dialogue = TryGetDialogue(候选键);
    if (dialogue != null) return dialogue; // ← 只判 null，**不判空**
```
（`NPC.cs:4024-4026` / `:4032-4034` / `:4042-4044` / `:4048-4050` / `:4053-4055` / `:4062-4068` 都是这个形状；
`Dialogue.TryGetDialogue`（`Dialogue.cs:447-455`）只在**键找不到**时返回 `null`，
值若是空串照样 `new Dialogue(speaker, key, "")`。）

⇒ 假分支留空的 `$query` 会让这条键**命中并返回一句空话**，而**不是**继续往下试候选键。
「条件不成立时不显示」与「显示空对话」是**两种语义**。

⇒ 结论：**「条件性存在」在原版没有通道**。`$query` 的两个分支**都必须是有内容的值**。
本工具的拒绝（`DialogueQueryWriter.Refuse`）与判定（`NeedsReview`）都按这条来，是对的。

---

## 5. 顺带记下的两条「别再被带偏」

1. **`StardewValley.Tests/SyntaxAbstractor` 那张「资产 → 翻译语法」表不能当 `$query` 宿主名单。**
   它管的是**翻译语法**（值要不要按对话语法抽语法骨架），与「`$query` 会**不会被执行**」是两回事：
   它同样把 `Strings/StringsFromMaps` 列为对话语法，而该资产的主要消费点是
   `Game1.drawObjectDialogue` → `new DialogueBox(string)`，`$query` 在那里是**死文本**。

2. **判断宿主的方法是「把 `new Dialogue(` 的实参字面量单独数」**，不能把所有
   `*Dialogue*` 调用混在一起数 —— 混在一起数会把死路那侧算进来，结论正好反。

   | | `new Dialogue(` / `TryGetDialogue(`（活） | `drawObjectDialogue(`（死） |
   |---|---|---|
   | `Strings\StringsFromCSFiles` | 36 | 34 |
   | `Strings\Characters` | 18 | 24 |
   | `Data\ExtraDialogue` | 11 | **0** |
   | `Data\EngagementDialogue` | 4 | **0** |
   | `Strings\Locations` | 0 | 121 |
   | `Strings\UI` | 0 | 21 |
   | `Strings\StringsFromMaps` | 0（另有动态键 1 处，`GameLocation.cs:9650`） | 5 |

   ⇒ **两侧都非 0 的资产不收**（`Strings/Characters`、`StringsFromCSFiles`——后者表里只放行了
   逐条查过的 `Event.cs.1527` 一个键）。

---

## 6. 三套集合发射在全语料上的实际战绩（2026-10-06，32 个包）

| 机制 | 候选组 | 发射 | 拒绝 | 覆盖补丁 |
|---|---|---|---|---|
| 四季 BigCraftable 索引 | 165 | **52** | 113 | **208** |
| 图集文件名季节前缀 + 地图改指 | 165 | **0** | 165 | 0 |
| 立绘/行走图 `Appearance[]` | 21 | 2 | 19 | 12 |

### 6.1 图集前缀那条**一条都没发射** —— 查过了，是**对的**，不是坏了

拒因（按组）：
- **61 组「没有任何地图把它当 TileSheet 图集引用」** —— 这些资产是当 **BigCraftable 贴图**用的，
  本来就该走**另一条**（四季索引），而那条发射了 52 组。两条的分工是**按消费方**分的，没重叠也没漏。
- 余下是「凑不齐四季」（9+4+2 组）与「When 里还有装不下的条件」（4 组）。

★ 顺手核对了这条机制**不是**因为找不到地图而空转：原版树 `Maps/` 下是 **190 个 `.tbin`**
（不是 `.tmx`），`FindReferencingMaps` 扫的正是输出树里的 `*.tbin`，能扫到。
