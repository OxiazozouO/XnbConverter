# 非 CP 的「框架型」模组里，有什么值得烘成原版 XNB？

日期：2026-10-11（用户点名要查「服装类的增量」）

> ⚠ **语料已扩大（2026-10-20）**：本文的「6 个非 CP 包、一条都烘不了」是 105 个 manifest 时代的结论。
> 当前语料有 **38 个非 CP/FTM 包**（AT 17、CC 10、SF 2、BL 2、CCCB 2、MFM 2、TS 1、MoreGrass 1、BBM 1），
> 逐框架的数量与可烘性见 [`2026-10-20-pure-xnb-dll-losses.md`](2026-10-20-pure-xnb-dll-losses.md)。
> ⚠ 本文“运行时行为 ⇒ 一条都烘不了”的口径已被
> [`2026-10-20-additive-baking-reassessment.md`](2026-10-20-additive-baking-reassessment.md)
> 修正：AT/JsonAssets/SF/CC 等可以**新增数据表条目**，不是保留运行时替换。
> 本文 §4 的「服装通道」结论仍然成立（语料里仍没有 FashionSense 内容包）。

**一句话结论**：本机装的非 CP 框架内容**一条都烘不了**（它们全是**运行时行为**，不是可落地的内容）；
而**服装/裤子/发型/帽子**这条通道**确有原版落点**、改动是纯文件操作 —— 但**语料里 0 条**，
本机也没装任何服装框架 ⇒ **现在没有可做、也没法验证的对象**。

---

## 1. 先看已有结论（别重做）

| 结论 | 出处 |
|---|---|
| 非 CP 内容包只有 6 个，且都不碰地图 | `2026-10-09-non-cp-framework-channels.md` §1 |
| 框架 JSON 注册表能不能烘，判据是**谁在读**：被 Harmony 读的 ❌（SpaceCore 31 个注册表里 22 个如此）；被 `e.Edit(...)` 并进既有 `Data/*` 的 ✅ | `2026-10-10-which-framework-registries-can-bake-to-xnb.md` |
| **1.6 已把服装/裤子数据化**（`Data/Shirts`/`Data/Pants`），CP 那条路已覆盖 | 同上 §二 |
| DLL 只写了 7 个原版资产（`Data/Mail`、`CraftingRecipes`、`Objects`、`Shops`、`Maps/Woods`…） | `2026-10-09-dll-mod-audit-and-map-warp-verification.md` §1 |
| 服装/裤子的**格子公式**早就量过（衬衫 8×8、左右对半；裤子 16×16、192×688 一格） | `2026-10-06-sprite-index-arithmetic.md` |

## 2. 本机 `Mods/` 的实际枚举（2026-10-11 复核）

```
manifest.json 105 份；带 ContentPackFor 的内容包 38 份：
  Pathoschild.ContentPatcher      32
  Esca.FarmTypeManager             4
  PeacefulEnd.CustomCompanions     1
  DIGUS.MailFrameworkMod           1
没有 ContentPackFor 的 67 份（框架本体 / 独立功能模组），其中含 DLL 的约 69 个目录。
装了 FashionSense 或任何服装框架吗？**没有**。
```

## 3. 那 6 个非 CP 包，逐个看有没有落点

| 包 | 内容形态 | 原版落点 | 结论 |
|---|---|---|---|
| FTM ×4（步行去沙漠/额外区域/SVE[FTM]/女巫沼泽） | 只有 `content.json`，字段是「地图名 + **坐标** + 刷怪/觅食定义」 | 只有 `Data/Locations` 的 `Forage`（**地点级、不定位**）与 `Min/MaxDailyForageSpawn` | ❌ **对不上** —— 坐标级生成是运行时行为，XNB 没地方放 |
| CustomCompanions ×1（野生动物） | 80 张 `companion.png` + 121 份 `companion.json`（按目录约定自动加载） | **没有**「伙伴/游荡生物」这种数据资产 | ❌ 必须框架运行时 |
| MailFrameworkMod ×1 | 只装了一份**模板**（占位 `MyMod.MyMailId`），**0 条真实信件** | `Data/Mail`（`"正文/附件/[#]标题"`）**可以映射** | ⚠ 有落点，但**没有内容可烘** |

DLL 框架本体（可解锁捆绑包、项目扩展、快乐家具设计、食谱技能、水中倒影…）带的是**自家 UI / 功能资源**，
原版没有读它们的地方 —— ❌。

## 4. 服装这条通道（唯一"值得做"的）：落点确凿，但**语料是空的**

1.6 的服装**已经是数据驱动**（对着反编译核过）：

- `Data/Shirts`：`ShirtData` 有 `Texture` + `SpriteIndex` —— **没有 `Texture` 时**才回落
  `Characters/Farmer/shirts`；
- `Data/Pants`：同构，回落 `Characters/Farmer/pants`；
- 发型/帽子也有落点：`Data/HairData`（值形如 `"hairstyles2/0/0/true/-1/false"`）、`Data/hats`；
- 贴图布局（`ShirtDataDefinition.GetSourceRect` / `PantsDataDefinition.GetSourceRect`，本仓库
  `2026-10-06-sprite-index-arithmetic.md` 已逐条量过并与原版实物对照）：衬衫**左半＝精灵、
  右半＝染色遮罩**、每格 8×8；裤子每格 16×16。
- ⇒ **「条目 + 一张专属贴图」是原版原生支持的**。要烘只做三件事：
  ① 往 `Data/Shirts`/`Pants` **追加**条目（新数字 ID，不动原版那些）；
  ② 把那张 PNG 作为**新 XNB 资产**放进产出树（或并进 `Characters/Farmer/shirts.png`），`SpriteIndex` 指过去；
  ③ 文案直接内联（纯文本令牌原样返回，不必动 `Strings/Shirts`）。

**障碍只有一条**：贴图必须满足「左右对半 + 8×8（裤子 16×16）」的格子约定 —— 仍是纯文件操作，
不需要任何框架运行时。

**但当前没有对象**（三条独立的核查，都指向 0）：

1. 本机**没装** FashionSense 或任何服装框架；
2. 当前 32 包语料的报告里 `Data/Shirts` / `Data/Pants` / `Data/hats` / `Data/HairData` **0 命中**；
3. 全 `Mods/` 的 `*.json` 里搜这几个资产名也是 **0 命中**。

⚠ 顺带订正一处**过时口径**：`2026-10-06-implicit-conventions.md` §8 写「新增家具/服装条目 50 / ~104」——
那是**当时另一份语料**（Ridgeside 那批）的数，**不是现在这 32 包**。现在这棵树上，服装类**一条都没有**。

## 5. 再挖一层（2026-10-11 晚）：逐个框架的**意图**能不能用原版机制顶

用户的要求是「别一句做不到就算了，去挖框架与数据的意图」。挖完的结果：

### 5.1 FTM —— **已经做了**，见 `2026-10-11-ftm-can-land-forage-buried.md`

觅食 / 埋藏物 → `Data/Locations.<地图>.Forage` / `.ArtifactSpots`，全语料落 **1076 条**。
剩下三段（刷怪 246 / 刷矿 9 / 大件 12 个 area）**确认没有落点**，证据比上次硬：

- `Data/Monsters` 是**怪物定义表**（血量/伤害/掉落），不是刷怪配置；
- 全库 `GameLocation.performAction` 的 ~120 个 `Action` 里没有一个生成怪物或物品；
- 地点类里只有两处与怪物有关，都**写死**：`Woods.resetSharedState`（50 次随机尝试、按季节刷史莱姆、
  坐标随机）与 `BugLand`（**按图块下标 28** 刷 Grub —— 这是唯一"地图数据驱动"的刷怪，
  但类型与下标都写死、而且要求改地图的**瓦片层**，我们只做属性改动）。

### 5.2 MailFrameworkMod —— **有落点**（`Data/TriggerActions`），只是本机没内容

原版 `Data/TriggerActions` 是 `{ Id, Trigger, Condition(GSQ), Action }` 的**列表**，
官方那 31 条里 **23 条**就是 `Action: AddMail Current <mailId>`（例：`Mail_MarniePetAdoption`
= `Trigger: DayEnding` + `Condition: PLAYER_HAS_MAIL Current rejectedPet Received, YEAR 2`）。

⇒ MFM 的三件事**逐一可对**：`mail.json` 的 `Id/Text/附件/Title` → `Data/Mail` 的
`"正文/附件/[#]标题"`；它的触发条件 → `TriggerActions[].Condition`；自动投递 →
`Action: AddMail Current <Id>`。**本机装的是模板包（0 条真实信件）**，所以没有对象可做，
但**通道成立**（与服装那条一样：将来遇到真包就能落地）。

⚠ **将来真要做，先知道哪些写不了**（这一轮读它自带模板补的）：`LetterBG`/`WhichBG`（自定义信纸 ——
DLL 在信纸菜单里换图，`Data/Mail` 没有这个槽）、`Recipe`、工具类附件（`Type: Tool` + `UpgradeLevel`）、
DGA 附件（`Type: DGA`，物品由 DGA 的 C# 生成）—— 这几样**只能报告**；
能落地的是**正文 / `%item` 类附件（Object・BigCraftable・QualifiedItemId）/ 条件 / 投递**。

### 5.3 CustomCompanions（野生动物）—— 只有"半个"落点

原版 `Data/Pets` 是数据驱动的（`Cat`/`Dog`/`Turtle` 三条，字段含贴图与偏移、`AdoptionEventLocation`…），
可以**新增一只宠物**。但宠物是**农场常驻**的，而 CC 的伙伴是**跟着玩家到处走**的 ——
"跟着走"那半个意图原版表达不了。⇒ **半落点**，本机也只有一个包。

### 5.4 框架本体自带的资源 —— 逐个看过，**不是内容**

- **快乐家具设计**（`tlitookilakin.HappyHomeDesigner`）：`assets/furniture.json` **确实是原版
  `Data/Furniture` 的斜杠串格式**（`名称/类型/尺寸/包围盒/价格/…`），一共 **3 条**（三个"图鉴"）。
  但它们的**意图是"点一下打开这个 mod 的 UI"** —— 没有那个 DLL，烘进 `Data/Furniture` 只是
  三个点了没反应的物品。⇒ 不烘（**理由不是"做不到"，是"烘了也没用"**）。
  旁边的 `ui.png`/`catalog.png`/`mail.png` 也都是它自己的界面资源。
- **可解锁的捆绑包**（`DLX.Bundles`）：DLL + 自己的商店贴图与音效（41 个文件），没有内容数据。
- 其余（项目扩展 / 食谱技能 / 水中倒影 / 鸟类观察协会本体…）：同上，都是自家 UI 与功能资源。

## 6. 建议

- **这条通道留着**（"将来遇到服装框架包就能用"）。虽然语料是空的，但**已经用合成包把它自证了一遍**：
  造一份带 `Load Characters/Farmer/testsuit` + `EditData Data/Shirts` 的包，跑完
  「转 → 打包（2/0）→ 解包读回（条目逐字段还原）」—— 见 `../verification/2026-10-11-clothing-channel.md`，
  回归钉子在 `tests/.../ClothingChannelTests.cs`。
  ⇒ **通道本身不需要新代码**：框架包只要能把内容翻成「`Data/Shirts` 条目 + 一张贴图」这两件事，
  现有的 Load/EditData 路径就吃得下。**要新写的只有「读框架自己的格式」那一段**，
  而那必须先有一个真实的服装框架包当输入。
- 真要现在推进，能自证的最近一步是**反向**的：拿一份 `Data/Shirts` 条目 + 一张贴图，
  验证「并进产出树 → 打成 XNB → 游戏读得到」这条链 —— 但语料里连这个输入都没有，得先造。
- 其余框架（FTM/CC/MFM/各 DLL 本体）**不必再立项** —— 这次已经逐个核过，都是运行时行为或自家资源。

---

## 附：本机**别的框架的内容包**有几个（2026-10-11 晚量的，`cppacks.find_other_packs`）

`convert-mods.py` 现在一开头就会报：

```
⚠ 另有 2 个**别的框架**的内容包，**没有转**：
    · 内容拓展---野生动物 [CC]----------9063  ← ContentPackFor=PeacefulEnd.CustomCompanions
    · 前置-------邮件框架模组 模板 [MFM]--1536  ← ContentPackFor=DIGUS.MailFrameworkMod
```

另外还会报一行**代码模组**的个数（判据：manifest 里有 `EntryDll`、没有 `ContentPackFor`）：

```
ℹ 本机另有 67 个**代码模组**（DLL）—— 纯 XNB 场景里没有 SMAPI，它们用不上，也不会进产物
```

⇒ 本机 Mods 目录的**全账**：**67 个代码模组**（进不了产物，那是项目前提）+ **38 个内容包**
（36 个要转：CP 32 + FTM 4；**2 个别的框架**：CC 的野生动物包、MFM 的模板包），
没有"既不是内容包也没有 EntryDll"的怪东西。两个别的框架包的落点分析都在上面
（CC 半个、MFM 有落点但本机只有模板）。