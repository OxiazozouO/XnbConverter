# 框架模组的 JSON 注册表，哪些能烘焙成 XNB？

日期：2026-10-10

起因：用户提的方向 —— 「很多框架是给**一类数据**加了一套管理机制（游戏源码可能只是按**编号**加载而已），
让别人用 JSON 就能做 mod，比如**服装、发型、食物配方**这种**每一个都不舍得替换**的，
可以考虑**增量加入**。请按这个方向调查**哪些 DLL 模组可以转换**。」

---

## 一、判据（一眼看出能不能烘焙）

看**谁在读这份注册表**，而不是看它像不像数据：

| 消费者形态 | 能不能烘焙成 XNB |
|---|---|
| 注册表被 **`HarmonyPatch` 的补丁体**读走 | ❌ 那是**行为**，XNB 里没有地方放 |
| 注册表被**合并进某个既有的 `Data/*`**（`e.Edit(...)`）| ✅ **可烘焙**，而且天然是"增量加入" |
| 混合：一部分字段映射到既有 `Data/*`，其余是行为 | ⚠ **只烘焙能映射的那部分** |

## 二、本机实况：先说结论

**本语料里没有一个框架注册表可以整份烘焙。** 证据见下。原因不是"数据太少"，
是**1.6 已经把这类数据资产化了**：

- 游戏 1.6 自带 **`Data/Shirts`**（12 字段）、**`Data/Pants`**（11 字段）、
  **`Data/CookingRecipes`**、**`Data/CraftingRecipes`**、**`Data/Furniture`**、
  **`Data/FarmAnimals`**、**`Data/Machines`** …
  ⇒ 服装/裤子/配方这些**"按编号加载"的东西，1.6 已经能按名字加条目了**，
  内容包直接 `EditData Data/Shirts` 就行 —— **CP 那条路已经覆盖，不需要框架**。
- 框架里**留下来的那部分**，恰恰是 1.6 **没有**的（"穿上给 buff"、自定义产出规则、
  自定义掉落…），只能靠 Harmony 补丁实现 ⇒ **XNB 表达不了**。

## 三、本机装了哪些框架

| 框架 | 有独立内容包吗 |
|---|---|
| `Pathoschild.ContentPatcher` | 32 个 ✓ |
| `Esca.FarmTypeManager` | 4 个 ✓（只刷怪/觅食，见 `2026-10-09-non-cp-framework-channels.md`）|
| `PeacefulEnd.CustomCompanions` | 1 个 ✓ |
| `DIGUS.MailFrameworkMod` | 1 个 ✓ |
| **SpaceCore / Item Extensions / Custom Bush / Extra Machine Config / Unlockable Bundles / 鸟会 …** | **没有独立内容包** —— 它们的数据只经 **CP 包**写进来 |

⇒ 所以调查的落点就是：**CP 包往框架注册表里写了什么**。

## 四、本语料实际写过的 5 个注册表 —— 逐条结论

| # | 注册表 | 谁写的 | 内容 | 能不能烘焙 |
|---|---|---|---|---|
| 1 | `spacechase0.SpaceCore/ObjectExtensionData` | 聚宝盆 作物（50 条）| 只有 `CategoryTextOverride` + `CategoryColorOverride` | ❌ **1.6 的 `ObjectData` 没有这两个字段**（分类名/色是代码里的常量表）|
| 2 | `spacechase0.SpaceCore/CropExtensionData` | 聚宝盆 作物 | `YieldData`/`ExperienceGained`/`RegrowDays`/`NewPhase`/`Drops`/… | ⚠ **只有 `RegrowDays` 对上**（`CropData.RegrowDays`）；其余 1.6 没有 |
| 3 | `spacechase0.SpaceCore/CookingRecipeOverrides` | 聚宝盆 制作（15 条）| `{ProductQualifiedId, ProductAmount, Ingredients[]}` | ⚠ **成品 + 材料能映射到 `Data/CookingRecipes`**；材料展示覆盖（`OverrideText`/`OverrideTexture*`）不行 |
| 4 | `furyx639.CustomBush/Data` | 聚宝盆 作物（13 条）| 可收获灌木：`AgeToProduce`/`Seasons`/`ItemsProduced[{Condition,Chance,MinStack,…}]` | ❌ 1.6 **没有"可收获灌木"这种数据**（`Data/WildTrees` 管树不管灌木）|
| 5 | `UnlockableBundles/Bundles` | 社区中心扩建（10 条）| `{Location, BundleReward, ShopPosition, EditMap, EditMapMode, Price, …}` | ⚠ **只有 `EditMap` 那部分我们能做**（就是普通的地图补丁）；兑换/气泡/商店是框架机制 |

### SpaceCore 全量分类（31 个注册表）

对 `SpaceCore.decompiled.cs` 里每处 `Load<T>("spacechase0.SpaceCore/…")` 看**前后 60 行**
有没有 `HarmonyPatch`：

```
22 个 明确在 HarmonyPatch 补丁体里读   => 运行时行为
 9 个 未判（少见/间接）
 0 个 往既有资产里 e.Edit 合并          => **一个可整份烘焙的都没有**
```

`ObjectExtensionData` 有 13 处加载、其中 8 处在 HarmonyPatch 里；`SpawnableDefinitions` 14 处 / 9 处在补丁里。
**这就是这一类框架的本质：它们给游戏加的是"行为层"，不是"数据"** ——
`WearableData` 整个类只有一个字段 `{ BuffIdToApply }`，而 1.6 的 `ShirtData`/`PantsData`
**根本没有 buff 字段**。

## 五、真能做的两个具体方案（字段映射）

### 方案 A：`CookingRecipeOverrides` / `CraftingRecipeOverrides` → `Data/*Recipes`（增量加入）

原版格式（实测 `D:\sdv_tree\Data\CookingRecipes.json`）：
**`"<材料> / <解锁条件> / <成品 数量> / <显示名> /"`**

```
"Fried Egg": "-5 1/10 10/194/default/"
"Omelet":    "-5 1 -6 1/1 10/195/l 10/"
"Salad":     "20 1 22 1 419 1/25 5/196/f Emily 3/"
```

映射：

| SpaceCore | 1.6 `Data/CookingRecipes` |
|---|---|
| `Ingredients[Type=Item].Value` + `.Amount` | 材料串里的 `<(O)id> <数量>` ✅ |
| `Ingredients[Type=ContextTag].Value` | 材料串里的 **`-<分类号>`**（`category_egg` → `-5`、`category_milk` → `-6`）—— **要一张 tag→分类号 的表**，查不到就没法映射 ⚠ |
| `ProductQualifiedId` + `ProductAmount` | 成品段 `<id> <数量>` ✅ |
| （无对应字段）| **解锁条件段** —— SpaceCore 没有 ⇒ 得挑一个（比如 `none` 或从补丁的 `When` 推）⚠ |
| `Ingredients[].OverrideText/OverrideTexture*` | ❌ 没有对应字段 |

### 方案 B：`UnlockableBundles/Bundles[].EditMap` → 普通地图补丁

条目里的 `{EditMap, EditMapPosition, EditMapMode}` 与 CP `EditMap` 的
`FromFile` / `ToArea` / `PatchMode` **一一对应**（`ReplaceByLayer` 就是我们的缺省）；
其余字段（`BundleReward`/`ShopPosition`/`SpeechBubble*`/`Price`）是框架机制，**丢掉**。

## 六、将来遇到"纯注册表型"框架怎么办

用户举的例子（**服装 / 发型 / 食物配方**）对应的是 **Fashion Sense** 那类：
它按**编号**往 `Maps/springobjects`/`Characters/Farmer/hairstyles` 挤，框架让 modder
写 JSON + PNG，再把 PNG 合进游戏纹理。**本机没装这个框架**（Mods 里没有），
所以下面是**配方**、**未验证**：

1. 框架 JSON 的每条 → **`Data/<类>` 的一条**（1.6 已有 `Data/Shirts`/`Data/Pants`；
   发型在 1.6 是 `Characters/Farmer/hairstyles` 图集，仍是**按编号**的 ⇒ 要**追加一帧**
   并记下新编号，属于"要动图集"的一类，比配方难）；
2. 条目里的 `Texture` 指向框架自己的 PNG ⇒ **把 PNG 复制进产出树**（现成的
   `ImageApplier` 就是干这个的，见 `2026-10-09-mod-local-map-tilesheets.md` 里同一套机制）；
3. **增量加入**：往既有 `Data/*` 里**加条目**，不替换整份 —— 与 CP 的
   `EditData` 语义一致，我们本来就这么做。

⚠ **判据先跑一遍**：如果那个框架的注册表是被 `HarmonyPatch` 读的（本机 31 个注册表里 22 个如此），
那就**只有映射得上的字段能烘焙**，别指望整份转。

## 七、这条调查的净结论

1. **"框架让 modder 写 JSON" 不等于"能烘焙"** —— 要先看消费者：Harmony 补丁读的，XNB 里没地方放。
2. **1.6 已经把服装/裤子/配方/家具这些数据化了**，所以这些"按编号加载"的东西
   **直接写 `Data/*` 就行**（CP 那条路已覆盖）；框架剩下的部分是**行为**。
3. 本机 5 个真实注册表里，**能烘焙的只有两个子集**（配方成品+材料、`EditMap`），
   其余字段 1.6 没有对应物。
4. 真要做，先做**方案 A**（配方）—— 它的映射最干净、`Data/CookingRecipes` 是纯字符串格式、
   而且"增量加入"天然成立。

---

## 八、顺着结论核一件事：那些 `Data/*` 我们**加**得对不对

用户说的「增量加入」到底有没有实现 —— 拿改前那棵全量产出与**原版**逐资产对条目：

| 资产 | 原版 | 产出 | 新增 | **原版键消失** |
|---|---|---|---|---|
| `Data/CookingRecipes` | 81 | **143** | +62 | 0 |
| `Data/CraftingRecipes` | 150 | 181 | +31 | 0 |
| `Data/Objects` | 807 | **1350** | +543 | 0 |
| `Data/BigCraftables` | 182 | 325 | +143 | 0 |
| `Data/Furniture` | 645 | 744 | +99 | 0 |
| `Data/Crops` | 50 | 134 | +85 | **1** |
| `Data/FruitTrees` | 8 | 40 | +32 | 0 |
| `Data/Machines` | 39 | 60 | +21 | 0 |
| `Data/FarmAnimals` | 14 | 17 | +3 | 0 |
| `Data/WildTrees` | 11 | 14 | +3 | 0 |
| `Data/Buildings` | 25 | 28 | +3 | 0 |
| `Data/Boots` | 18 | 20 | +2 | 0 |
| `Data/Weapons` | 67 | 73 | +6 | 0 |
| `Data/Bundles` | 31 | 31 | 0 | 0 |

⇒ **全部是"加"，不是"换"** —— 用户设想的「增量加入」在 1.6 的数据资产上**已经成立**。

### 唯一那个「原版键消失」查清了：是**内容包有意删的**

`Data/Crops` 少的是 **`301`（葡萄）**。追到来源：聚宝盆 作物的
`data/objectreplacements.json` 里一条

```json
{ "Action": "EditData", "Target": "Data/Crops",
  "When": { "Enable Extended Crops Pack": true, "Disable Grape Bushes": false },
  "Entries": { "301": null } }
```

- CP 的规矩：`Entries` 里值为 **`null` = 删掉该条目**；
- 该包 `config.json` 里 `"Disable Grape Bushes": "false"` ⇒ 条件**成立**（`false == false`）
  ⇒ 这条**本来就该生效**；
- 而且它成对：同一个包在 `furyx639.CustomBush/Data` 里给 `(O)301` 加了**可收获灌木**的定义
  —— 也就是「葡萄从作物改成灌木」✓

⇒ **不是 bug，是我们把 CP 的 `null = 删除` 语义实现对了的证据。**

---

## 九、方案 A 量了一下：**现在做不了**（负结果）

上面把「配方 → `Data/CookingRecipes`」列为"最干净、先做这个"。**量完发现不行**：

本语料 `CookingRecipeOverrides` 共 **15 条**，其中**材料全是 `Item` 型**的 —— **0 条**。
每种材料都混着 `ContextTag`（16 处 tag vs 23 处 item）：

```
Omelet               [ContextTag, ContextTag]
Cheese Cauli.        [Item, ContextTag]
Parsnip Soup         [Item, ContextTag, Item]
Complete Breakfast   [Item, ContextTag, Item, Item]
Pizza                [Item, Item, ContextTag]
```

`ContextTag` 的值形如 `"category_egg, egg_item"`。1.6 的 `Data/CookingRecipes` 材料串**确实支持**
"任一分类"（原版就是这么写的：`"Omelet": "-5 1 -6 1/…"`，`-5` = 蛋类、`-6` = 奶类），
所以 `category_egg → -5` 这个对应**原则上是通的**。

**但那张「context tag → 分类号」的表我们手上没有**：游戏侧的分类常量在
`StardewValley/Object.cs`（`Object.EggCategory = -5` 这类），而本机只有
`StardewValley.GameData.*` 的反编译，**没有 `Object.cs`**。硬编码 `-5`/`-6` 就是**猜** ——
按本仓库的铁律（宁可少写，不可写错），**不猜**。

⇒ **方案 A 搁置**，等能拿到权威的分类号表再说（或者只转 `Item` 型材料的那种配方 —— 而本语料里恰好一条都没有）。

**同一批注册表现在的判定已经是对的**：报告里它们是 `NoLandingPoint`，缘由写明
「`spacechase0.SpaceCore/CookingRecipeOverrides` 是**别的 mod 注册的自定义资产**……
纯 XNB 场景里没人读它」 —— 也就是说**这个缺口在报告里是看得见的**，不是静默丢掉。

---

## 十、方案 A 再核一次：**那张表从游戏 DLL 里拿到了，但结论不变**（2026-10-10）

上次卡在"「context tag → 分类号」那张表本机没有"。**这次拿到了** ——
反编译 `StardewValley.Object`（游戏 DLL 就在 `D:	mp\gv2`）：

```
EggCategory=-5  MilkCategory=-6  VegetableCategory=-75  FruitsCategory=-79
FlowersCategory=-80  GreensCategory=-81  FishCategory=-4  MeatCategory=-14 …
```

共 **35 个**分类常量。`category_egg` → `EggCategory` → `-5`（名字归一化后匹配）✓。

### 本语料的映射率

`CookingRecipeOverrides` 里的 `ContextTag` 材料共 **29 处**：
**能映射 13 处**、**映射不上 16 处** —— 后者全是**物品族标签**（不是 `category_*`）：

```
milk_item ×12     cheese_item ×3     egg_item ×1
```

### 这些物品族标签能不能用"等价推导"救回来？—— 只有一半

判据（**从游戏自己的数据推，不猜**）：对某个 tag，看原版 `Data/Objects` 里
①带它的物件是否**只有一个** `Category`；②那个 `Category` 的物件是否**全都带**它。
两条都成立才算等价。

| tag | 结论 |
|---|---|
| `milk_item` | 等价 `-6`（4 个物件）✓ |
| `flower_item` | 等价 `-80` ✓ |
| `egg_item` | **跨 2 个分类** `[-5, 0]` ⇒ 不等价 ✗ |
| `fruit_item` | `-79` 里还有 18 个不带它 ⇒ 不等价 ✗ |
| `cheese_item` / `vegetable_item` / `fish_item` / `meat_item` / `seed_item` | **原版没有物件带这个 tag** ⇒ 无从判定 ✗ |

⇒ 一条配方**只要有一个材料映射不上就整条不能转**：本语料那 15 条**仍然一条都转不了**
（早先量到"材料全是 `Item` 型的 0 条"，加上 tag 映射后仍是 0 条）。

### 所以**仍然搁置**，但这次是"有据地不做"

- **方法留着了**（tag ⊆ 分类 与 分类 ⊆ tag 两向检验 + 35 个分类常量）；
  将来遇到材料只用 `category_*` / `milk_item` / `flower_item` 的包，可以直接做；
- **不做的理由变了**：不是"没有表"，而是"**本语料里映射不上的材料占多数、且一条配方要求全映射**"；
- ⚠ 还留一条**未确证**：原版 `Data/CookingRecipes` 的材料里**没有任何非数字的负号写法**
  （`CookingRecipes`/`CraftingRecipes` 两边都实测过）⇒ **看不出原版配方支持直接写 context tag**，
  所以"不映射、原样写 tag"这条路也走不通。
