# 跨语料普查（Task 13）

日期：2026-10-06
目的：**此前所有判定、优先级、预期数字的样本量都是 1（RSV 一个包）。**
本文件补上第二个语料。

---

## 0. 为什么做这个

`D:\sdv_merged*` 共 **22 个目录，全是 RSV 的跑**。
而 `D:\Steam\steamapps\common\Stardew Valley\Mods` 下有 **32 个 CP 内容包**，工具**一次都没跑过**。

**样本量 1 的结论，会把优先级判断反。** 本文件用实测补上。

跑法：`tools/corpus-b-survey.py`（已入库，可复现）。

---

## 1. 结果

### 1.0 ⚠️ 本文件首版漏了最大的那个包（**已修正**）

首版跑出 31/32，缺的是 `FlashShifter.StardewValleyExpandedCP`（SVE 主包，122 MB）——
它因为一条含 `*` 的资产路径崩了（Task 16 修好后重跑）：

```
total=4425  convertible=3481  written=2155（49%）  writeFailed=274
```

**SVE 一个包 4425 条补丁，占语料 B 的 71%。** 首版把最大的包漏了。

### 1.1 修正后的合计

| | 首版（31 包，缺 SVE）| **修正后（32 包全）** |
|---|---|---|
| 补丁合计 | 1785 | **6210** |
| 已写入 | 742 | **2897** |
| SVE 占比 | — | **4425 / 6210 = 71%** |

**任何「语料 B 的结论」都必须以含 SVE 的数字为准。**

### 1.2 SVE 的画像（4425 条）

| 判定 | 条数 |
|---|---|
| `Converted` | 1379 |
| `NeedsReview` | 931 |
| `ConvertedNoCondition` | 841 |
| **`NoLandingPoint`** | **453** |
| `TargetMissing` | 248 |
| `CreatesNewAsset` | 229 |
| **`LocalEdit`** | **216** |
| `NotApplicable` | 101 |

**写入失败 274 条的原因**：

| 条数 | 原因 | 归属 |
|---|---|---|
| **88 + 54 + 38 = 180（66%）** | **`.tbin` / `.xnb` / `.tmx` 不是数据资产** | **Task 12（tbin 块）** |
| 28 | 关系类条件需要 NPC 名，须写在键侧 | **Task 14a** |
| 25 | 属性无法映射到 GSQ | 待查 |
| 6 | 输出路径非法 | 其余（Task 16 已兜住）|

> **SVE 的写入失败大头是地图（66%）——这把 Task 12 的优先级大幅抬高。**
> 而 `.xnb`（54 条）是一条新形态：`Load` 的源文件本身是 `.xnb`。

### 1.3 单包耗时

14~27 秒（小包）；**SVE 用 127 秒**。

---

## 2. ★ 判决画像：**语料 B 比 RSV 更差**

| 判定 | RSV | 语料 B | 差异 |
|---|---|---|---|
| **`NeedsReview`** | 221（13%）| **740（41%）** | **↑ 3 倍** |
| `ConvertedNoCondition` | 232（14%）| **674（38%）** | ↑ |
| `Converted` | 445（27%）| 88（5%）| ↓ |
| `CreatesNewAsset` | 409（25%）| 75（4%）| ↓ |
| `NoLandingPoint` | 0 | 65 | 新增 |
| `TargetMissing` | 187 | 35 | ↓ |
| `LocalEdit` | 2 | 30 | ↑ |
| `NotApplicable` | 107 | 64 | — |

> **`written` 率：RSV 57% → 语料 B 42%。**
> **RSV 是「好案例」，不是典型。** 之前拿它定的优先级，方向是偏的。

---

## 3. ★ 语料 B 的 `NeedsReview` 构成（740 条）

| 条数 | 原因 |
|---|---|
| **528（71%）** | **`条件含不认识的令牌`** |
| 69 | 条目键名编码了条件维度但不被完全蕴含（多为 `Characters/schedules/*`）|
| 41 + 10 | 新建资产带条件，落点 Unknown |
| 31 | 对话键只支持受限语法 |
| 23 | 条件含纯计算令牌 |
| 23 | 条件可翻译但结构无法识别 |
| 15 | Load 带条件 |

### 那 528 条的令牌**全是 `ConfigSchema` 字段名**

```
76 Goat Skins   76 Rabbit Skins   58 Sheep Skins   50 Pig Skins
48 White/Brown Cow Skins   33 White/Brown Chicken Skins
33 Enable Extended Crops Pack   12 Enable Herbs Pack   7 Enable Extended Trees Pack
 9 Ostrich/Dinosaur Skins   8 Blue Chicken Skins   5 Void Chicken Skins …
```

**528 / 1785 = 30% 的补丁，纯粹因为我们不读 `ConfigSchema` / `config.json` 而被判死。**
→ 已排成 **Task 15d（最高优先）**。

---

## 4. 写入失败 72 条的构成

| 条数 | 原因 | 归属 |
|---|---|---|
| 28 + 8 + 2 + 1 | **i18n 解析失败**（内嵌令牌 / 缺键 / 带参数）| **Task 15** |
| 12 | `<X> 的目标条目 X 不存在` | 待查 → **见下方订正** |
| 7 | `.tmx` 不是数据资产 | **Task 12**（tbin 块）|
| 5 | 关系类条件需要 NPC 名，须写在键侧 | **Task 14a** |
| 3 | `<X> 无法定位或创建` | 待查 → **见下方订正** |

⚠ **订正（2026-10-10 复核）**：上面两处「待查」，以及 §1 那处「25 条 属性无法映射到 GSQ」，
**用当前代码重跑 32 包后，未写补丁的理由里已经搜不到这几个关键词**（各 0 条）。
⇒ 它们**不再挂在那里**了。

⚠ **但这不是"确认解决"**：我只搜了理由文本，**没有逐条追溯**这些补丁现在归到哪一类
（理由文案在这几轮改过好几版，很可能是**换了归类的说法**，而不是真的能写了）。
要说清"到底怎么了"，得拿 10-06 的那批补丁清单逐条对 —— **这件事没做**。

**参考**：当前（10-10）32 包合计补丁 **7048**、written **3767**；
未写的主要类别是「条件可静态求值叶子折叠后恒假」818、「条件化地图改动无通道」551 等，
明细见 `2026-10-10-known-losses.md`。

---

## 5. 已在语料 B 上**验证过的「正确拒绝」**（不是我们的能力缺口）

| 现象 | 结论 |
|---|---|
| ~40 条 `Characters/schedules/*` | 原版日程键链是**优先级列表**，`spring_14` 排在 `rain` 前——**「某天且下雨」这个组合表达不了**。有天气维度，但不可与日期组合。 |
| 32 条 `sophie.ConversationTopics` | 第三方 C# mod 的私有资产，纯 XNB 下**没有消费者**。 |
| 12 条「目标条目不存在」 | 待查，但形态上是 mod 引用了不存在的条目。 |
| **SVE 的 212 条「季节性大 craftable 贴图」** | **原版对物件贴图没有季节维度**——见下 |

### 5.1 ⚠️ 季节性 BigCraftable：**我判「无解」是错的——原版有原生机制**

> **本节曾写着「读过源码后确认真无解」。那是错的。**
> 用户提示「源代码也无解时，去看看实际的图片是怎么组织的 / 你自己看图不就知道了」——
> 看图后发现 SVE 的四季是**连续的 4 格**（X=0/16/32/48），这才去搜「谁按 4 取模」。

**原生机制（`StardewValley/Object.cs:1940-1944`，反编译原文）**：

```csharp
if (bigCraftable.Value && name.Contains("Seasonal"))
{
    int num = base.ParentSheetIndex - base.ParentSheetIndex % 4;
    base.ParentSheetIndex = num + location.GetSeasonIndex();   // 0=春 1=夏 2=秋 3=冬
}
```

（另一处同构：`Object.cs:2918`，`onReadyForHarvest` 后 `ResetParentSheetIndex()`。）

**机制**：**只要物品的 `Name` 里含子串 `"Seasonal"`**，
游戏就把 `ParentSheetIndex`（精灵索引）**向下取到 4 的倍数，再按季节加 0/1/2/3**。

**也就是：把四季摆在连续的 4 个精灵槽里，游戏自己按季节选。原版就有。**

**SVE 的转换路径（无损）**：

| 步 | 做法 |
|---|---|
| 1 | 把 `SeasonalJA.png` 那一行的 4 格（FromArea X=0/16/32/48，Y=192，各 16×32）拼成目标资产的 **64×32 四联图** |
| 2 | `Data/BigCraftables.<id>.Name` 里**加 `Seasonal`** |
| 3 | `SpriteIndex` 设为 **4 的倍数** |
| 4 | **删掉那 4 条条件 `EditImage`** |

**实测确认 SVE 没用这个机制**：`code/Items/BigCraftables.json:672` 的物品名是 `"Little Shrub"`——**没有 `Seasonal`**。
作者是手工用 4 条条件补丁模拟了原版本来就有的东西。

**教训**：我当时只搜了 `spriteIndex` / `seasonIndex` 同现，**没去搜「按 4 取模」**，
也没想到机制会挂在**物品名的子串**上。**用户让我看图，图的布局（连续 4 格）才是线索。**

<details>
<summary>（以下是**错误**的旧结论，保留以示记录）</summary>

### 5.1-old 季节性 BigCraftable 贴图：~~读过源码后确认无解~~

SVE 的形态（`code/Items/BigCraftables.json:1861+`）：

```json
{ "Action": "EditImage",
  "Target": "Mods/FlashShifter.StardewValleyExpandedCP/Little Shrub",  ← SVE 自己 Load 出来的资产
  "FromFile": "assets/tilesheets/SeasonalJA.png",                       ← 一张图装四季
  "FromArea": {…}, "ToArea": {…},
  "When": {"Season": "Spring", "IsOutdoors": true},
  "Update": "OnLocationChange" }
× 4 个季节
```

**查过并排除的机制**：

| 机制 | 结论 |
|---|---|
| `Game1.objectSpriteSheet = content.Load<Texture2D>("Maps\\springobjects")` | **硬编码字面量**，不带季节拼接 |
| `GetSeasonalTilesheetName`（`spring_` 前缀维度）| **只被 `loadTilesheets` 调用**——**只对地图 tilesheet 生效** |
| 全库搜 `currentSeason + "objects"` | **0 命中** |
| 原版树里有没有 `summerobjects`/`fallobjects`/`winterobjects` | **只有 `springobjects`**——连回退都无处可退 |
| `Data/BigCraftables` / `Data/Objects` 的 `Texture` 有无 `Condition` | **无**（已逐字段读过模型）|

**结论：这 212 条是真·无解**，不是我们没找到——**原版没有「物件贴图按季节变」这个概念**。

> 对照：**地图 tilesheet 的季节维度存在**（`spring_` 前缀），所以「地图上的季节贴图」是免费的。
> 差的就是「物件贴图」这一层没有。

</details>

---

## 6. 已在语料 B 上**证伪的我们自己的猜想**

| 猜想 | 实测 |
|---|---|
| 「新建资产找不到 `.config` 模板」是失败大头 | **0 条** ❌ 猜想不成立 |
| 卡住 i18n 的是 `{{Random}}` | **是 `DynamicTokens`**（`WhenSpouseIs*` / `Baby*Gender`）❌ |
| 「C# 注册的自定义动作表达不了」 | **`RSVTeleport`（10 条里 5 条）就是原生 `Warp`** ❌ |

---

## 7. 图片条件落点（配套调研的结论）

**原版所有「条件选图」机制都是「条目级」的**：每个候选必须是一张**独立资产**、挂在某张 `Data/*` 表里。
**没有任何机制能表达 CP `EditImage` 那种「同一张资产、内容随条件变」。**

| CP 目标类 | 有条件落点吗 | 语料 B 的量 |
|---|---|---|
| `Characters/X`、`Portraits/X`（X 在 `Data/Characters`）| ✅ `Data/Characters.<NPC>.Appearance[]`（完整 GSQ + Season + Indoors/Outdoors，可**分别**换立绘/行走图）| 388 / 32 个目标 |
| **`LooseSprites/X`** | ❌ **没有通用落点** | **138 个目标（104/107 带 `When`）← 最大的一块无解需求** |
| `Maps/X` | ⚠️ 整图可换（`CreateOnLoad.MapPath`）但无条件位；季节免费 | 170 |
| `Buildings/X` | ❌ `Skins[].Condition` 只决定**菜单里能不能选**，不自动生效 | 0 |
| `TileSheets/X` | ❌ `spring_` 前缀只对**地图 tilesheet** 生效 | 4 |
| `Mods/…`（模组自建资产）| ❌ 不在任何 Data 表里 | 224 |

**清单外、源码真实、且语料有实证的四条落点**：
`Data/WildTrees.Textures[].Condition`、`Data/WorldMap.*.Textures[].Condition`（SVE 用了 8 条 `SEASON X`）、
`Data/Shops.Owners[].Portrait`+`Condition`（语料 B 29 处）、`Data/TemporaryAnimatedSpriteDefinition`。

---

## 8. 结论：语料 B 上最该先修的三类

1. **Task 16**（坏路径不杀整次转换）——**SVE 整包零产出**，不修它最大的包永远测不了
2. **Task 15d**（读 `ConfigSchema`）——**30% 的补丁卡在这一个缺失上**
3. **Task 15**（i18n 六项，尤其 15f 的「一处失败连坐整资产」）——写入失败的主要来源

**这三条合计能覆盖语料 B 的绝大多数失败面。**

---

## 9. 方法学

- 脚本：`tools/corpus-b-survey.py`（入库、可复现）
- **每个包独立跑、独立报告**（一个包崩不影响别的）
- 统计脚本一律写文件后读，避免终端中文乱码
- **所有数字都能追到命令**
