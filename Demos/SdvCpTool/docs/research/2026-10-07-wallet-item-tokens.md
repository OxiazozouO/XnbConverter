# `HasWalletItem` 是可译的（一条被错判成「无解」的条件）

日期：2026-10-07
起因：用户指出「程序说不可解析的地方，你其实没认真考察过 —— 去实地考察，可能要到 mod 的 dll、配置里去」。
结论：**他说对了。** 这一条曾判「GSQ 里没有等价关键字」，实际完全可译，语料里 **76 条**因此被卡。

---

## 1. 原来的判断（错的）

```csharp
// ConditionVocabulary.cs，2026-10-07 之前
"HasWalletItem"    // 无通用钱包关键字；仅 KeyToTheTown 例外
```

报告里对每条都写「在 GSQ 里没有等价关键字，也没有对应的名字维度（GSQ 无法返回集合或遍历实体）」。

**错在「只查了 GSQ 的关键字表就下结论」** —— 没去看**游戏数据**。

---

## 2. 实地考察的三层

### 2.1 第一层：钱包物品就是原版 **power**，而 `Data/Powers.json` 里每个 power **自带 `UnlockedCondition`**

`D:\sdv_tree\Data\Powers.json`（原版数据，**游戏自己写的**）：

```jsonc
"RustyKey":            { "UnlockedCondition": "PLAYER_HAS_MAIL Host HasRustyKey" },
"ClubCard":            { "UnlockedCondition": "PLAYER_HAS_MAIL Current HasClubCard" },
"MagicInk":            { "UnlockedCondition": "PLAYER_HAS_MAIL Current HasMagicInk" },
"KeyToTheTown":        { "UnlockedCondition": "PLAYER_HAS_MAIL Current HasTownKey" },
"BearPaw":             { "UnlockedCondition": "PLAYER_HAS_SEEN_EVENT Current 2120303" },
"SpringOnionMastery":  { "UnlockedCondition": "PLAYER_HAS_SEEN_EVENT Current 3910979" },
...
```

**那句 `UnlockedCondition` 本身就是一句 GSQ。** 权威性最高 —— 它不是推出来的，是游戏自己的配置。

### 2.2 第二层：与 `Farmer.cs` 的实现交叉核对

```csharp
public bool hasRustyKey   { get { return Game1.MasterPlayer.mailReceived.Contains("HasRustyKey"); } }
public bool hasClubCard   { get { return mailReceived.Contains("HasClubCard"); } }
```
（`StardewValley/Farmer.cs:1388-1400`、`:1334-1340`）

与 `Powers.json` **逐条吻合**。⇒ ⚠ **`Host` / `Current` 的差别是真的**：
`RustyKey`/`SkullKey`/`DwarvishTranslationGuide` 读的是 **`Game1.MasterPlayer`**（主机），
其余读实例字段（当前玩家）。**不能改成一律 `Current`。**

### 2.3 第三层：CP 认哪些名字 —— 从 **CP 的 DLL** 里读

`ContentPatcher.dll` 里能读到 7 个属性 getter
（`get_hasClubCard` / `get_hasDarkTalisman` / `get_hasMagicInk` / `get_hasRustyKey` /
`get_hasSkullKey` / `get_hasSpecialCharm` / `get_hasMagnifyingGlass`）
＋ 4 个字面量（`BearsKnowledge` / `KeyToTheTown` / `SpringOnionMastery` / `DwarvishTranslationGuide`）。
**共 11 个名字**，就是我们可以放行的集合。

---

## 3. ★ 唯一的改名，以及它是怎么被钉死的

CP 叫 `BearsKnowledge`，而游戏的 power id 叫 **`BearPaw`**。

**佐证链**：CP 的 DLL 里**同时**有字符串 `BearsKnowledge` 与事件 ID **`2120303`**；
而 `Data/Powers.json` 里 `BearPaw` 的 `UnlockedCondition` 正是
`PLAYER_HAS_SEEN_EVENT Current 2120303`。同理 `SpringOnionMastery` ↔ `3910979`。

⇒ CP 也在用**同一份条件**。⚠ 但**这是推断**（没读到 CP 源码，只看到了它 DLL 里的字面量），
生成物与代码注释里都标了这一点。

**反面校验**：`BearsKnowledge` 与 `BearPaw` 在**游戏 DLL 的字节里一次都不出现**，
`sdv_tree` 全树也搜不到 —— 说明它们都是**数据里的 id**（Powers.json 由数据驱动），
不是 C# 字面量。这也解释了为什么只搜源码会一无所获。

---

## 4. 实现

- `tools/gen-wallet-items.py` → `src/SdvCpTool.Core/Validating/WalletItems.cs`
  （**从 `Data/Powers.json` 生成**，与 `GameDataSchema` 同一套路：单一来源、可 `--check`）
- `ConditionVocabulary`：`HasWalletItem` 从 `NoGsqEquivalent` **移出**，加入 `GsqTranslatable`
- `TokenTranslator`：`haswalletitem` → 查表；**表外的值一律拒绝**
  （CP 的取值是**固定词表**，放行一个它不认的名字 = 把一个 CP 本会拒绝的条件翻译出去）

### 两条刻意的限制（都写明理由，不是省事）

1. **多值拒绝**：`|contains=A, B` 语义是「任一命中」，要表达成带引号的 `ANY "…" "…"` ——
   那种形式本工具**从没发射过**，没验证过不硬上。**实测语料里这类用法一处都没有。**
2. **表外的值拒绝**：见上。

---

## 5. 受控对比

见提交信息与 `docs/superpowers/plans/2026-10-05-m3-merge.md` 的 Task 35。
（同语料、同参数，只换代码；判定分布与 `written` 的前后对比记在那里。）

---

## 6. 顺带查过、但**确实没有**原版等价物的相邻两类（如实记）

- **`TheMightyAmondee.CustomTokens/Child:…|daysold`**（20 条）：
  读 **`CustomTokens.dll`** 得到它的取值集合
  （`birthdayday` / `birthdayseason` / `daysold` / `darkskinned` / `hat`），
  语料里用的是 `daysold >= N`（N = 0/8/15/16/22/30/35/57/60/70/80/100，**孩子的天数**）。
  GSQ 的 130 个关键字里**只有 `PLAYER_HAS_CHILDREN`（计数）**，没有任何「孩子年龄」关键字
  ⇒ **整体无等价物**。唯一能译的是 `daysold >= 0`（≡ 该序号的孩子存在），语料里 2 条。
  ⚠ 未确证：孩子不存在时该令牌返回什么（负值哨兵？报错？）没查清，所以**没实现**。
- **`{{Random: …}}`**：绝大多数（170+133+97+…）出现在**别人 mod 的 C# 读的图块属性**里
  （如 `CustomCompanions: "Spawn {{Random: 0,1,2,3}} …"`）。纯 XNB 场景下 `{{Random}}` 无法复刻，
  只能取其一，**那是语义近似**，不许默不作声地做。

---

## 7. 受控对比（32 个 CP 包，同参数）

| 判定 | 改前 | 改后 |
|---|---|---|
| 可合并 | 5537 | **5541** |
| 已写入 | 3558 | **3560** |
| 写入失败 | 89 | 89 |
| **`NonTranslatableCondition`** | **76** | **0** |
| `Converted` | 1548 | 1550 |
| `LocalEdit` | 460 | **504** |
| `TargetMissing` | 225 | **253** |

**76 条全部不再卡在条件上**，随后落到**真正的下一个障碍**（条件化 `EditMap` 44 条、
目标资产不在树里 28 条）—— **这是归因变准，不是回归**：
`+44 +28 +2 = 74 ≈ 76`，三处数字自洽。修之前它们连"下一个障碍是什么"都看不到。

★ 一个副作用值得记：**`LocalEdit` 涨到 504 成了最大的一类**。
于是顺手把「条件化地图改动到底有没有原版通道」**从假设升级成枚举验证**（§8）。

---

## 8. 「条件化地图改动没有原版通道」—— 现在是枚举过的结论

原来这句话是**推断**。这轮把两处可能的通道**都枚举了一遍**：

1. **`Data/TriggerActions` 的动作表**：`TriggerActionManager.cs` 里 `DefaultActions` 的
   公开静态方法就是动作名（反射注册），共 **26 个**：
   `AddBuff, AddConversationTopic, AddFriendshipPoints, AddItem, AddMail, AddMoney, AddQuest,
   AddSpecialOrder, If, IncrementStat, MarkActionApplied, MarkCookingRecipeKnown,
   MarkCraftingRecipeKnown, MarkEventSeen, MarkQuestionAnswered, MarkSongHeard, Null,
   RemoveBuff, RemoveConversationTopic, RemoveItem, RemoveMail, RemoveQuest, RemoveSpecialOrder,
   RemoveTemporaryAnimatedSprites, SetNpcInvisible, SetNpcVisible`
   ⇒ **没有一个碰地图图块**。（`If` 只是把别的动作套上条件。）
2. **图块动作表**：`GameLocation.cs` 里 `case "…"` 字面量共 **231 个**，
   其中与 tile / map / prop / condition / layer / index 相关的**只有 `ConditionalDoor` 一个**，
   而它只能开门/挡路，改不了图块内容。

⇒ **「条件化地图图块改动」在原版确实没有通道** —— 现在有据可查，不是"我没找到"。

### 顺带把 459 条 `LocalEdit` 按实际操作分了类

| 操作 | 条数 |
|---|---|
| `EditMap` + `FromFile` + `ToArea`（**按条件把一块 tmx 区域贴进地图**） | **294** |
| `MapTiles` 的 `SetProperties`（改图块属性） | 95 |
| `TextOperations` | 35 |
| `MapProperties` | 32 |
| `MapTiles` 的 `Remove` / `SetIndex` | 2 |
| `AddWarps` | 1 |

（合计 459；改后 504 条的差额是 `HasWalletItem` 那条修好之后新落到这里的。）

---

## 9. ★ 顺带查出来的**测量方法问题**：语料数字被压低了

### 9.1 现象

`TargetMissing`（253 条）+ `CreatesNewAsset`（312 条）里，一眼看去大半是
`Maps/Custom_ForestWest`（32）、`Maps/Farm_FrontierFarm`（16）、`Maps/Spring_z_extras`（15）……
**这些图不是"找不到"，是本来就不该在这一包里**：
`Custom_ForestWest` 是 **SVE** 的地图，别的包只是去 `EditMap` 它。

### 9.2 根因：普查是「**一包一棵新树**」跑的

`tools/corpus-b-survey.py` 每包都从**原版树**拷一份再转 —— 于是 A 包改 B 包的资产时必然找不到。
**真实用法是把所有包依次转进同一棵树。**

### 9.3 受控实验（SVE 之后接野生动物包，同参数）

| | 单包（基线） | 串行（SVE 之后） |
|---|---|---|
| `TargetMissing` | 63 | **19** |
| `LocalEdit` | 126 | **169** |
| `ConvertedNoCondition` | 2 | 3 |
| `written` | 6 | 7 |

**44 条从 `TargetMissing` 转成 `LocalEdit`** —— 目标找到了，然后撞上**真正的**障碍
（条件化地图改动，§8 已枚举证明无通道）。**净收益只有 +1 条写入。**

### 9.4 全语料量化

`TargetMissing + CreatesNewAsset` 共 **552** 条，其中目标**被语料里别的包造出来过**的：

**97 条**（`Characters/` 47、`Maps/` 45、`Data/` 5）。

⇒ 语料普查的 `written 3560` 是**保守值**（不是虚高，是虚低）；
但按实验比例推算，链式转换的**净收益也只有个位数条**，因为下游是那堵地图墙。

**这一条记下来是为了**：以后看 `TargetMissing`/`CreatesNewAsset` 的数，**先问是不是测量方法造成的**。
（新增 `tools/cross-pack-targets.py` 专门量这个。）
