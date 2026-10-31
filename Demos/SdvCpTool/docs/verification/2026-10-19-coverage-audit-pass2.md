# 2026-10-19 产出物覆盖审计（第二轮）：跨包 Load/Edit 顺序、外部模组令牌、i18n 键名

本轮**没有跑全量**（按业主指令，全量必须单独下命令）。做法是：
以 `D:/tmp/prof-fix26` 的产出树 + 各包 `report.json` 做静态审计，对可疑点
用 `tools/convert-mods.py --packs <子串>` 做**单包/小组定向复跑**（当前 Release 构建），
再对照 CP 反编译源码（`D:/tmp/cp_decompile`）确认语义。

⚠ `prof-fix26` 是**旧构建**的产出。本轮审计里发现的 `Seasonal Cute Characters`、
`Jonghyuk`、`HxW Tilesheets` 等"TargetMissing"在**当前代码单包复跑时已消失**，
不是新 bug，别拿 fix26 的数字当现状。

## 1. 外部模组令牌把 DynamicToken 的默认值顶掉（已修）

**现象**（fix26）：`[CP] GiEX redux` 的 Javi 只有行走图，**立绘全部缺失**
（`Portraits/JaviGiex`、`_indoors`、`_rainy`、`_Younger`、`_Quartermaster`、`_Stranded2` 等）。

**根因**：包里的定义是

```jsonc
{ "Name": "PortraitStyle", "Value": "Vanilla" },
{ "Name": "PortraitStyle", "Value": "{{mistyspring.GiEXredux.code/PortraitStyle}}",
  "When": { "HasMod": "mistyspring.GiEXredux.code" } }
```

DLL 模组**已装** ⇒ 第二个块胜出；但它的值来自 DLL 模组的令牌，离线不可知。
原实现把这个"含字面 `{{…}}` 的值"原样返回，`Load` 的 `FromFile` 展开后仍有令牌 ⇒
判定 `TargetMissing` ⇒ 三条 `Load`（共 7 张立绘）全不写；
后续 `EditImage`（Quartermaster / Stranded2 的覆盖层）也因目标不存在而丢。

**修法**（`DynamicTokenResolver.ResolveValue`）：选中的块展开后仍带**外部模组令牌**
（CP 约定名里带 `/`）时，**退回更早的块**（语料里就是包作者写的无条件默认值），
并记 `Lossy` 如实报告。只对带 `/` 的外部令牌做这件事；
`{{Target}}`/`{{Weather}}` 这类代不掉的仍按原样返回，由下游判定处理。

**验证**：单包复跑 GI Extra locations：

| | 修复前（fix26） | 修复后（当前代码） |
|---|---|---|
| `Portraits/JaviGiex*` | 只剩 `JaviGiex_Book` | 7 张齐全 |
| `EditImage` Quartermaster / Stranded2 | `TargetMissing` | `ConvertedNoCondition`，written=true |

## 2. 跨包 `Load`/`Edit` 顺序：CP 是两阶段，我们原来是逐包单阶段（已修）

CP 的 `PatchManager.ApplyPatchesToAsset`：请求某资产时**先把所有包的 Load 选一个装上去，
再按优先级依次应用全部 Edit\***。所以「后加载的包创建的资产」照样能被先加载的包编辑。

我们原来按包顺序逐条应用，导致两类丢失：

1. **Edit 撞上尚未创建的资产** ⇒ `TargetMissing`。fix26 里例如
   `Lnh's Farms Convenient Road` 创建的 `Maps/Custom_ConvenientRoad`
   被更早的 `Lnh's Community center item output points` 编辑、SVE 编辑
   `Maps/Custom_GrampletonFields`（由后加载的 Grampleton Fields 创建）。
2. **后加载包的 Load 覆盖先加载包的 EditData**，而 CP 会在 Load 之上重放所有 Edit。
   fix26 里 `Data/CraftingRecipes` 少了整个"鸟类观察协会"的配方（被更晚的
   `AutoPetterRecipe` 的 `Load` 冲掉）；只有 `.es-ES` 变体残留着那几条
   （该变体没被同一次 `Load` 覆盖）。

**修法**：
- CLI 新增 `--pass loads|edits`（`CommandLine.Options.Pass`）；
- `tools/convert-mods.py` 改为**两趟**：第 1 趟所有包只装 `Load`，第 2 趟再按包序应用 `Edit*`；
  每包报告分别为 `report.loads.json` / `report.json`（后者只含 Edit 那一趟）。

**验证**（单包链复跑）：
- `--packs "Lnh"`：`Maps/Custom_ConvenientRoad` 的 `EditMap`
  从 `TargetMissing` 变为 `ConvertedNoCondition`、written=true；地图由第 2 个包的
  `CustomLocations` 在第 1 趟装好。
- GI Extra locations：`EditImage Portraits/JaviGiex_Quartermaster` / `_Stranded2`
  从 `TargetMissing` 变为 written=true（正是第 1 趟 Load、第 2 趟 Edit 的效果）。

## 3. JSON **键名**里的 `{{i18n:…}}` 没翻译（已修）

**现象**：`Data/CraftingRecipes.es-ES.json` 里出现

```json
"{{i18n:big-craftable.HeatedStoneBath.name}}": "…/Baño de piedra climatizado para aves"
```

**根因**：CP 把 JSON 键也当令牌串解析；`TranslationApplier.ContainsToken` 只查**值**，
`CollectI18nSpots` 也只收集 JValue。键里的 i18n 令牌于是原样成为配方键
（制作菜单会直接显示 `{{i18n:…}}`，且各语言资产不会用本地化名）。

**范围**：全语料只有 `[CP] 鸟类观察协会` 的 2 个文件、6 个配方键。

**修法**：
- `ContainsToken` 也看键名；
- 新增 `CollectKeySpots` / `TranslateKeysInPlace` / `ApplyKeyNames`，键名与值一样
  **按语言逐份重放**；基础资产用 default，语言变体用各自语言；
- 键名改写的实现必须**保住 value 节点同一性**（值那批 spot 指着 `prop.Value` 内的节点）：
  用"占位替换 + 摘下 Value 再挂到新属性"的方式，避免 Newtonsoft 克隆整棵子树；
- 语言变体的"只覆盖本补丁改到的顶层键"逻辑同步映射：原始键 `{{i18n:name}}`
  → 基础语言键 → 该语言键；已有本地化资产里补丁没碰的条目继续保留。

**验证**：
- 单测 `键名里的i18n也按语言替换且值同步`、`键名含i18n时语言变体按增量覆盖已有本地化资产`；
- 单包复跑：`Data/CraftingRecipes.json` 键为英文名、`.zh-CN` 为中文名、`.es-ES` 为西语名，
  不再出现 `{{i18n:…}}` 键。

## 4. 已确认**不是我们**的问题（上游资产名/目标本身不存在）

以下在 fix26 里报 `TargetMissing`，CP 同样会失败，**不要试图补**：

- `Content/farmhouse_tiles`、`farmhouse_tiles`（正确资产名是 `Maps/farmhouse_tiles`）；
- `Minigames/boatJourneyMap`：游戏只加载 `Minigames/<season>_boatJourneyMap`
  （`BoatJourney.cs:415`），SVE 的无前缀目标是死补丁；
- `Maps/Custom_CapeBusStopBuildable`：Cape Stardew 既没有 `CustomLocations` 条目，
  也没有任何包 `Load` 它；
- RSV `Data/Characters` 的 `Appearance[].Portrait: Portraits/Jio_Unmask` 等：
  包内没有任何 `Load`/`EditImage` 创建该资产；
- `NCarigon.BushBloomMod/Textures/**`、`Mods/Pathoschild.TractorMod/Tractor`：
  别的框架/DLL 提供的资产，纯 XNB 场景不存在；
- `{{spacechase0.JsonAssets/ObjectId: …}}`（1 处日程台词）：DLL 令牌，静态不可解，
  与 CP 离线产物的已知损失同类。

## 5. `{{InternalAssetKey}}`：整条物品/商店补丁被拒（已修）

**现象**（fix26）：`[CP] Hurst` 的 38 个物品、`[CP] Rodney O'Brien` 的书/稻草人、
`[CP] Eli and Dylan`、Metalcore、Crystal Island 的动画等**整条 `Data/Objects`/`Data/Shops`
补丁不写**。原因：`Texture` 写成 `{{InternalAssetKey: assets/images/WHObjects.png}}`，
静态解析不了 ⇒ 写入闸拒绝整条补丁 ⇒ 游戏里物品不存在、商店空货架。

**语义**（SMAPI 4 `ModContentManager.GetInternalAssetKey`）：资产名 =
`SMAPI/<UniqueID 小写>/<包内相对路径>`，**扩展名保留**（`NormalizeAssetName` 只剥 `.xnb`）。
纯 XNB 里没有 SMAPI 服务这个"包内文件资产"，所以还要把文件**物化进产出树**。

**修法**（`Cp/InternalAssetKeyExpander`）：
- 判定前把值里的令牌换成 `SMAPI/<id>/<路径>`，并记下「资产名 → 包内路径」；
- 写盘阶段用 `ImageApplier` 物化 `<资产名>.png` + `.config`
  （得到 `WHObjects.png.png`，回包为 `WHObjects.png.xnb`，游戏请求 `…/WHObjects.png` 命中）；
- 文件不存在 / 参数里还有未展开令牌 / 非图片 ⇒ 原样保留（不猜）。

**验证**：
- 单测 `InternalAssetKeyTests` 3 条（展开、缺文件、参数含令牌）；
- 单包复跑 Woodsman Hurst：`Data/Objects` 从 0 → **39 个** `sprinton.hurst_*` 物品，
  `Texture = SMAPI/sprinton.hurst/assets/images/WHObjects.png`，素材与 `.config` 都在；
- 单包复跑「内容拓展---新武器」：`Data/Weapons` 写入 **16 把**
  `RadiantAdditionalWeapons.*`（此前整条被拒，连带 95 条商店货架指向它们）；
- 小树回包：`WHObjects.png.png` → `WHObjects.png.xnb`（成功 1 / 失败 0）；
  解回后尺寸仍是 64×192，字节差属于既有的预乘 alpha 重编码（不是内容差异）。

## 6. 关系令牌的**布尔**默认块（已修）

`FreezeRomanceFlags` 只处理带婚房的 `CanBeRomanced`；其余布尔字段
（`CanReceiveGifts` / `ItemDeliveryQuests` 等）带 `{{关系令牌}}` 时有损 ⇒ 整条 NPC 不写
（fix26 里 `WoodsmanHurst` 不在 `Data/Characters`）。现在 `ValueTokenSubstitutor` 对
**结果恰好是 `true`/`false`** 的有损展开放行（其它类型仍不猜），并把 `Data/Characters`
里所有声明为 bool 的字段的字符串形态折成真布尔
（`MarriageReadiness.BooleanFields`，按反编译的 `CharacterData`）。
无婚房的 `CanBeRomanced` 冻结成**默认块**（通常是 `false`）：NPC 存在、只是不能结婚 ——
比"NPC 根本不存在"好，也不会出现"娶进 Abigail 默认房间"。

## 7. `When` 里引用 ConfigSchema 字段（已修）

CP 允许 DynamicToken 的块条件直接写配置字段名（**不是** `{{…}}` 形式），例如 Rodney：

```jsonc
{ "Name": "Meredith Portrait Style Path", "Value": "Nyapu",
  "When": { "Meredith Portrait Style": "Nyapu" } }
```

原实现的 `EvaluateWhen` 只把**包内 DynamicTokens** 当本地令牌，配置字段被丢给外部求值器
（它不认识）⇒ 判不了 ⇒ 令牌无值 ⇒ **Rodney / Meredith 整个 NPC 不写**。
现在 `EvaluateWhen` 同时认 `_configTokens`（含 `|contains=`），单测
`动态令牌的When可以判配置字段` 锁住。

## 8. 所有块都判不了时取第一块（已修）

Rodney/Nora 的 `*Datability` 是「没在约会→false / 在约会→true」两块，**没有无条件块**。
原实现扫完所有块没匹配就返回 `null` ⇒ 整个 NPC 条目不写。现在：
若至少有一块是**运行时判不了**（不是静态为假），取包作者写的**第一块**（语料约定 = 默认状态），
记 `Lossy`；全是静态为假时仍返回 `null`（真·无值，不猜）。
单测 `全部块判不了时取第一块并标为有损`。

## 9. 枚举字段的有损默认值（已修）

`Data/Characters` 的 `Manner` / `Gender` / `Age` / `SocialAnxiety` / `Optimism` /
`HomeRegion` / `Calendar` / `SocialTab` / `EndSlideShow` 取值是闭集枚举。
实测 Aster 的 `Manner: {{PolitenessAster}}`（Polite/Rude）条件判不了 ⇒ 整个 NPC 不写。
现在 `ValueTokenSubstitutor.ToleratesLossy` 放行这些字段（结果仍不得含未展开令牌），
落到默认块只是"闭集里的另一个合法值"。

## 10. 本轮广度审计的交叉验证

- **贴图引用**（子代理全树扫描 32,311 条字段）：解析到 21,072 条，悬空 18 个唯一引用，
  **转换器造成的真缺口 = 0**；全部可归因到上游缺资产（RSV `Jio_Unmask`、`_Beach` 可选变体）、
  原版占位（`MOD_FARM`、`Monsters\Shadow_Brute`）或框架 DLL（Parchment）。
- **物品跨表**（子代理）：真缺口集中在 `{{InternalAssetKey}}`（Hurst 39、Radiant 16、Rodney 2）
  与 DLL 配置令牌（GiEX `ExcludeFishes`/`ExcludeAnimalProduce`，待办）；
  另 1 条 `Cornucopia_FlavoredYogurtSilver` 是**上游 ID 笔误**。
- **NPC 覆盖**（子代理）：fix26 缺 16 个；本轮修掉 Hurst / Rodney / Meredith（配置 When、
  布尔默认块、InternalAssetKey），剩 Aster（枚举，已修）/ Nora（首块，已修）/ Cambria（布尔，
  已修），以及条件创建型（Lavril/CharlieChicken/GoblinZic，预期缺失）。

## 11. 代码模组（DLL）配置令牌的窄规则（已修）

`[CP] GiEX redux` 用 `{{mistyspring.GiEXredux.code/ExcludeFishes}}` /
`ExcludeAnimalProduce`；代码模组把这些 config 字段注册成 CP 令牌，但**令牌名与字段名不同**：
`ExcludeFishes → ExcludeFromFishingCollection`、
`ExcludeAnimalProduce → ExcludeAnimalProduceFromShippingCollection`。
原 `ModConfigTokens` 只认**同名**字段 ⇒ 28 个鱼 + 31 个动物产物定义整条不写。

新增 `DllConfigTokens`（窄规则，不猜）：
- 只对 `manifest.EntryDll` 的代码模组生效；
- 同名 → 白名单（上面两个 + `PortraitStyle → Portrait`）→ **唯一前缀**；
- 只接受顶层 bool / 数字 / 短字符串（不含逗号换行、≤64 字符）；bool 输出 `True`/`False`；
- 其余返回 null，保持未解析。
单测 `DllConfigTokensTests` 2 条（映射 + 无 EntryDll 内容包不猜）。
单包复跑 GI Extra：`Data/Objects` 里 `GiEXredux_*` 共 **106 条**，
`Dragonfish` / `Omnifish` / `LavaSnail` / `HarpSponge` / `Milk_V` / `IridiumEgg` 等全部在；
仅 `VoatMilk_V` 仍缺（`Animals.json` 引用了它，但包里**没有任何定义** —— 上游笔误）。

## 12. 条件创建的新条目（NPC/物品/商店/能力…）（已修）

原版 `Data/Characters` 的条目没有条件字段可落，所以「看过某事件后才创建这个 NPC」的补丁
原先整条 `NoLandingPoint` 丢掉 —— Lavril / CharlieChicken / GoblinZic 在纯 XNB 里**根本不存在**。
新增 `ConditionalNewEntryCreation`：对白名单资产（NPC / 物品 / 商店 / 能力 / 作物 / 机器 /
邮件 / 任务 / 礼物偏好…，**不含 `Data/Locations`、`Data/TriggerActions`、`Data/Events`、
`Data/Festivals`、`Data/SpecialOrders`**）里**目标还没有的条目**，把补丁条件拆掉、按无条件写；
静态为假的条件仍跳过；**已有条目**的条件修改保持原判（那会改错已存在的内容）。
报告逐条写明「运行时可能提前出现」。单测 3 条；
单包已验 Lavril、`Aimon111.WitchSwampOverhaulCP_GoblinZic` 出现
（二者在 fix26 里完全不存在）。

**运行时状态被冻结成 `false` 的不算静态为假**：当 `When` 引用了 `HasSeenEvent` / `HasFlag` /
`Relationship` 等（包括 `ApplesHere = {{HasSeenEvent…}}` 这种中转令牌）时，仍按"未知"处理、无条件创建；
只有 `HasMod` 这类**真的静态为假**才跳过。单测 `运行时状态冻结为假时仍写`。

⚠ **必须跳过带 `TargetField` 的补丁**：那时 `Entries` 的键是目标条目的**字段名**
（`BirthSeason` / `CanBeRomanced` / `Price`…），不是新条目。第一次跑 East Scarp 时踩到过 ——
它把"条件成立时改字段"当成"新条目创建"，虽然在产出里没写坏键（`TargetField` 仍在），
但语义上把条件去掉了。已加守卫 + 单测。另外这个确定化只在提供 `--mods`（能判断 `HasMod` 等静态为假）时启用。

### 12.1 两个补充

- **空日程/空对话资产**：`Load` 一个空 JSON 建出 `{}`、再用条件 `EditData` 加条目的形态
  （实测 SVE `CharlieChicken` 的日程）—— 目标资产**是空对象**时才把条件条目拆成无条件补；
  已有日程/对话的资产绝不动。
- **外观/住所变体全被删光**时保留第一条并去掉 `Condition`（否则 NPC 没有任何外观/住所）；
  部分变体可保留时照旧只删代不掉的那条。
- **键比较必须大小写不敏感**：游戏数据字典按不敏感处理，`wizard_sleep` 与已有
  `Wizard_Sleep` 是同一个键；不做这一步会写出两条仅大小写不同的键（SVE 婚姻动画），
  `KeyCaseAudit` 直接报撞车。
- **`Strings/*` 的新键**无条件写（纯文本，只在被引用时显示）；实测 SVE 的
  `StringsFromCSFiles` / `StringsFromMaps` / `Strings/schedules` 一批年份/剧情字符串。
  其中 `Strings/schedules` 的键条件**无处可落**：原先把条件编码成
  `CommunityDay_Kent/G …` 的键，游戏按字面键查找会**找不到**；现在写成 `CommunityDay_Kent`，
  条件丢失（多个季节变体最后一个胜出），但至少台词能显示。

## 13. 条件 `Load`（文本/贴图/对话）按无条件加载（已修）

fix26 里 68 条带条件的 `Load` 全被判 `NeedsReview` 丢掉（27 条 `Characters/*`）：
例如 SVE 的 `Characters/Dialogue/CharlieChicken`（看过事件后才有对话）。
新增 `ConditionalLoadDeterminization`：目标**尚不存在**、条件依赖运行时状态
（或静态为真）时，去掉条件、按无条件加载。
只做 `Characters/Dialogue/`、`Strings/`、`Portraits/`、`Characters/`（**排除 schedules**）、
`TileSheets/`、`LooseSprites/`；**地图/日程/数据资产不动** ——
节日地图可能互斥、日程会直接改 NPC 行为。
逗号分隔的多目标（`Characters/X, Portraits/X`，实测 Always Raining in the Valley）
**只有全部目标都不存在**时才去条件；只要有一个已存在就不动，避免连带覆盖。
单测 3 条；SVE 复跑：`Apples` 与 `CharlieChicken` 均写入。

## 14. 两趟链的性能：第 1 趟跳过全树自检

两趟链会让每个包跑两次；全树自检（条件落点/形状/模型/贴图/令牌/大小写）每次都要扫整棵树。
第 1 趟只装 `Load`，最终树由第 2 趟负责 —— 所以 `--pass loads` 时跳过自检块，
第 2 趟（和单趟模式）照常跑。功能上第 2 趟覆盖全部最终产物；第 1 趟的报告不再带自检数字。

单包（Lavril）实测：第 1 趟 **29 s → 9 s**；第 2 趟数字受机器噪声影响（4 s vs 11 s），
不作为结论 —— 优化只发生在第 1 趟。

## 15. 本轮新增/改动的代码

- 新增：`Cp/InternalAssetKeyExpander.cs`、`Cp/DllConfigTokens.cs`、
  `Cp/ConditionalNewEntryCreation.cs`、`Cp/ConditionalLoadDeterminization.cs`、
  `Cp/RuntimeConditions.cs`（均带单测）。
- 改动：`DynamicTokenResolver`（外部令牌退回默认块、配置字段 When、首块回退、候选枚举开关）、
  `ValueTokenSubstitutor`（布尔/枚举默认块、外观变体回退）、`TranslationApplier`（键名 i18n）、
  `CandidateTargetExpansion`（候选枚举关闭首块回退）、`MarriageReadiness`（bool 字段）、
  CLI `--pass`、`convert-mods.py` 两趟链、三个检查器工具。

## 16. 代码审查后的加固（独立审查代理）

- **DynamicToken 条件互相引用**：`ResolveValue`/`EvaluateWhen` 增加深度上限（32），
  防止 A 的 When 问 B、B 的 When 问 A 触发 StackOverflow（整包无报告丢失）。
- **混合条件不再误拆**：`RuntimeConditions.AllMayDependOnSave` 要求**所有**子句都可能依赖运行时；
  只要有一个已知静态子句（`HasMod`/`Season`/配置字段…）判假，就不做无条件化。
  未知外部令牌按"运行时"保守处理（宁可写）。
- **拆分补丁保持源码顺序**：不再倒序追加，避免"早期补丁覆盖后期补丁"。
  同包 `Load Assets/Blank.json` 打底的目标在单趟模式下也视为空对象（`ExistingFor`）。
- **首块回退跳过静态为假的块**；**多值配置令牌按集合比较**（`A,B` 命中 `A`、
  `|contains=No` 不再误命中 `None`）。
- **大小写不敏感撞车**：键名翻译与令牌键改名都按 `OrdinalIgnoreCase` 检测；
  条件创建也不会写出仅大小写不同的重复键。
- **两趟报告合并**：`convert-mods.py` 把 `report.loads.json + report.json` 合并回
  `report.json`（`twoPassMerged: true`），原始两趟保留为 `report.loads.json` / `report.edits.json`；
  既有报告消费方统计不再把 Load 资产当"没写"。脚本失败时返回码不再是 0。
- 素材物化失败、语言变体的布尔归一、Target 的 `\`→`/` 归一也一并补上。
- **地图属性里的 `{{i18n:…}}`**：`MapApplier`/`MapCache.Flush` 按中文（缺则 default）展开；
  实测 SVE 的 `Custom_HenchmanBackyard` / `Custom_MorrisHouse` / `Custom_TreasureCave` 三张图 8 处。

### 16.1 已知取舍（审查建议但未改）

- `EvaluateWhen` 里**本地令牌的有损/确定化**仍按"该块不生效（false）"处理，不向调用方标 Lossy。
  改成三态会让 `ConfigTokenSubstitutor` 的"有损一律不代"拒掉 SCC 的季节服装（历史实测
  231/334 → 233/334），所以保留老行为；代价是极少数嵌套令牌的默认块选择不会被标成有损。
- `{{ i18n:key }}`（花括号内有空白）仍按固定前缀识别；语料扫描 0 处，暂不扩。

> 键名 i18n 镜像的已知缺口已修：本包没有中文 i18n、树里却有 `asset.zh-CN` 时，
> 镜像必须用**翻译后的基础键名**（`effectiveChangedKeys`）查找；用原始 `{{i18n:…}}`
> 键会找不到属性，进而把新增键当成“本补丁删除了它”而不写进中文变体。

## 17. 验证与现状

- 全量单测：**1613** 通过（新增：InternalAssetKey 4、键名 i18n 3、布尔/枚举默认块 2、
  配置字段 When 1、首块回退 1、DLL 配置令牌 2、多目标条件 Load 1、Include 动态令牌 1、
  自引用动态令牌 1、多值 HasMod 2、FTM 全局回退 1、现成 tbin 地图归 1，及扩展断言）。
- 地图重复落盘：`MapCache.Flush` 第二次调用会复用 `TBin10Reader`，而 `Save` 不重置
  `BytePosition`，把同一张图追加成两份 tBIN10（池化缓冲里可能混入别的文档）。
  现在每次写成功都换干净写入器并清 `Dirty`；`spouseRoom` 用例额外断言文件只有一份文档。
- 定向单包复跑（当前 Release 构建）：GI Extra locations、Lnh ×7、HxW Tilesheets、
  Seasonal Cute Characters ×4、Jonghyuk、鸟类观察协会、Woodsman Hurst、
  Rodney O'Brien（NPC + WWWA 物品 + 172 个 SMAPI 素材）、
  Lumisteria Mt Vapius（Aster 的 `Manner=Polite`）、Metalcore（Cambria/Orchid）、
  Lavril、Distant Lands（GoblinZic）、SVE（Apples/CharlieChicken/日程/GrapeMastery）、
  S&S（Hector/Mateo/Cirrus + 6 Power）、Ridgeside（108 NPC）、Downtown Zuzu。
- 定向复跑（2026-10-20）：Rose and the Alchemist（5 种 SASS 动物 + 21 个动物素材）、
  Nora（`Data/Characters.Nora` + 冬季外套素材）、Nora/Juliet 的自引用令牌事件、
  Always Raining in the Valley（Sterling/Mia/Henry 的 Characters/Portraits）均已写入；
  Sunflower Acres FTM 生成 32 条，但 Standard 存档被 `FarmType` 条件判不适用（预期）。
- 确定性：Lavril 同参数两次 7,829 个文件逐字节相同。
- 两趟报告合并：SVE `report.json` 带 `twoPassMerged: true`（4,542 条 = Load + Edit）。
- 仍**未跑全量**，也**未装机**；装游戏后的逐项打勾清单见
  [`2026-10-19-breadth-verification-list.md`](2026-10-19-breadth-verification-list.md)。

## 18. 定向审计修复（2026-10-20）

对 `Rose and the Alchemist` / `The Herpetologist` / `Sunflower Acres` /
`Always Raining in the Valley` / `Juliet, Jessie and Lodging House` 做单包定向审计，
修了 5 类真缺口（均带单测）：

- **Include 的 `FromFile` 动态令牌**（Rose）：路径含 `{{...}}` 的 Include 现在也留到
  `ResolveIncludes`，用同一个 `DynamicTokenResolver` 展开。原先 Load 阶段没有求值器，
  整份 Include 被判“目标不存在”（Rose 的 5 种动物 + 21 个素材）。
  这类 Include 没有 `When`，必须直接按无条件展开；交给求值器会被当成“判不了”并内联，
  内联里的 `Load` 又会被跳过（第二轮复跑发现并修掉）。
- **无扩展名的 `InternalAssetKey`**（Nora）：引用 `assets/Characters/nora_sprites_winter_coat`
  时按 SMAPI 规则试 `<路径>.png`；资产名仍保持无扩展名，物化文件为 `.png`。
- **自引用 DynamicTokens**（Nora / Juliet）：`{{Self}}` 在后面的块里不是环，而是
  “在基础值后追加”。现在按该块之前最近的匹配块递归求值；首块自引用仍按环处理。
- **FTM 全局季节表回退**（Sunflower Acres）：area 的 `*ItemIndex` 为 `null` 时回退到
  `Forage_Spawn_Settings` 段级表（反编译 `ModEntry.cs:7967-7982`），两个 area 共 32 条。
  ⚠ 该包文件级条件是 `FarmType = SunflowerAcres`；纯 XNB 把 FarmType 冻结成 Standard，
  所以 Standard 存档里这 32 条仍判 `NotApplicable` 是**预期**，不是没修好。
- **多值 `HasMod |contains=A, B`**（Juliet）：按 CP `Token.ApplyContains` 的集合语义
  **任一命中**；原先整串当一个 mod id，SVE 已装时取反条件会被判错。

同轮还补了两处：

- `Load` 现成 `.tbin` / 解码 XNB 的地图也展开地图属性 `{{i18n}}`、归一自定义图层名
  （原来只在 TMX/`MapCache.Flush` 那条路做）。
- `check-map-warps.py` 改按 tbin 属性结构取 `Warp`/`NPCWarp`，不再把 `Doors`
  的五元组当 Warp（Downtown Zuzu 4 张公寓图误报 `indoor` 已消失）。

## 19. 第二轮定向审计修复（2026-10-20，Lavender / Lumisteria）

- **`Data/Locations` 键大小写**：`TargetField` 首段按树里已有地名忽略大小写归一
  （`LocationKeyNormalizer`）。实测 FTM 写 `RailRoad`、真名 `Railroad`，6 条觅食/埋藏物
  落在一个不存在的字典键上；现在归到 `Railroad`。
- **FTM `CPConditions.HasMod` 离线烘焙**：`ContentPack.Load` 接收已装模组谓词，
  判真就从 area 条件里去掉，判假则整条不适用。实测 Secret Wood Weeds 的
  `mistyspring.ItemExtensions`，4 条季节物品不再白丢。
- **FTM 刷新量分母按当季**：原先用四季条目总数，当季期望量被压到约 1/4
  （Forest 1：春季 2 条时每条 0.2，正确 0.5）。
- **`convert-mods.py --packs`**：同时匹配 manifest `Name` 与目录名；`IsRequired` 依赖
  自动加入（可选依赖不拉），可用 `--no-required-deps` 关闭。实测 Lumisteria 的 CP 包
  Name 被改成无关中文串，只按 Name 会漏整包，FTM 的地点条目因此没有 `CreateOnLoad`。
- **两趟报告 notes 去重**：FTM 翻译说明两趟相同，合并后不再整段重复。
- **条件新条目说明**：`when == true`（例如 HasMod 已装）不再写成“条件无法静态表达”，
  改成“条件静态为真，按无条件写入”。
- **更多 `|contains` 任一命中**：值路径的外部 `{{HasMod |contains=A, B}}`、跨模组配置令牌
  `{{UID/field |contains=A, B}}`、包内 DynamicToken 条件、`HasWalletItem`
  的多值 `contains` 都改为集合 any-of；多值配置（JSON 数组）按 `", "` 渲染，
  `HasWalletItem` 多值发射 `ANY "q1" "q2"`（语法已用游戏 `GameStateQuery.Parse` 验证）。
- **新建地图的 ContentHashes 键**：`ContentHashesWriter` 只认 `.json`/`.png`，
  `.tbin` 地图回包后同样是 `.xnb`，原先**所有新建地图都没有键**。现已补上 `.tbin`；
  已有键按**大小写不敏感**判重，避免同一资产在清单里出现两条。
- **重复语言变体省略**：某语言的译文与本补丁的基础值逐字相同（包里有语言文件但该键没翻、
  或资产里没有可翻译点）时，不再写整份重复的 `<asset>.<locale>.json`，让游戏按默认语言回退。
  实测 Lavender 的 `Data/Objects.fr-FR`（730 KB）与 `Data/Locations.zh-CN`（562 KB）、
  `Data/FruitTrees.fr-FR` 与基础完全相同；复跑后三个变体都不落盘
  （Objects/FruitTrees 写入时跳过，Locations 由结束清理）。
  `convert-mods.py` 结束时还会做一次**最终清理**：删除基线树里没有、且与基础资产逐字相同的
  语言变体，并同步移除 ContentHashes 键（实跑三包链删掉 `Locations.zh-CN` 562 KB；
  清单写到产出根 `_deduped-locales.txt`，报告数字仍按写入时统计）。
  对旧 East Scarp 树试跑：清掉 18 个变体、2.2 MB。
  加 `--prune-baseline-locales` 还会清理**原版**里与基础逐字相同的语言副本
  （Zuzu 树实测 131 个 PNG、26.4 MB；游戏走 LocalizedContentManager 回退，默认关闭）。
  `--keep-identical-locales` 可关闭。游戏 `LocalizedContentManager` 在本地化资产不存在时
  回退到基础资产，因此不损失内容。
- **季节覆盖补齐**：四季图集/BigCraftable 规划器不再要求“正好四季”。
  缺的季用**底图**补那一格/那一份（CP 下该季没有补丁就是保持底图）；
  一条补丁写 `Season: "Spring, Summer, Fall"` 或
  `Season |contains=Summer, Winter` 时按**多季**复制，不再整组拒绝。
  `SeasonFromFileExpansion` 遇到原本就有 Season 限制的补丁时，也只展开到允许的季节，
  并把条件收窄成单季（否则同一补丁会被算进多季、互相覆盖）。
  SCC 的 `Maps/townInterior`（Summer/Winter 椅子）与 `LooseSprites/Cursors`
  （Spring/Summer/Fall）因此可以发射，Winter/秋季用底图补齐。
  实测 SCC 单跑后 `spring/summer/fall/winter_townInterior` 四份齐全，
  引用它的 `AbandonedJojaMart` 等地图已改指 `spring_townInterior`；
  `check-map-tilesheets` 对 SCC 树 148 处季节图集引用报 0 缺失。
  SVE 仍有 122 个 BigCraftable 组：52 个发射（全部标近似），其余是
  PatchMode/缺 Season/缺区域/额外运行时条件，保持原判而不是半套发射。
- **其它重复资源**：SVE 多个 `Maps/Custom_*_WarpRoom.tbin`、East Scarp 大量空
  `Characters/Dialogue*.json` 与空事件资产内容相同，但**资产名各有消费方**（NPC/剧情按名字取），
  不能删；由 `pack-tree.py` 的硬链接去重统一优化。旧树里的
  `Characters/Dialogue/Morrigan.zh-CN.json` 这类重复变体，重跑后会按上一条直接省略。
- **重复图片的落盘优化**：产出树里的重复 PNG 大多是**资产名不同但都必须存在**
  （NPC 默认立绘 vs vendor 贴图、空白占位图、原版语言副本；事件命令
  `changeSprite Clint Magician` / `changePortrait Lavril default` 也会按空格拼成
  `Characters/Clint_Magician` / `Portraits/Lavril_default` 这类名字），不能删路径。
  `pack-tree.py` 打包后按内容哈希把相同文件改成**硬链接**（默认开启，`--no-dedupe` 关闭），
  保留所有资产名，只省磁盘；不同文件系统/无权限时自动退回原样。

已知取舍（未改）：纯显示字段里的运行时令牌仍会让整条不写。实测 Lavender 的
`Data/Minecarts` 洞穴目的地只在 `DisplayName` 里含 `{{FarmName}}`，整条被丢；
放宽成“显示名降级”会把字面令牌或猜测文本写进玩家可见界面，暂不做。

复跑验证（Lavender + Lumisteria CP + FTM 三包链）：

- `Data/Locations` 里只有 `Railroad`、没有 `RailRoad`；`Railroad.Forage` 12 条、
  `ArtifactSpots` 9 条 = 原版 11/4 + FTM 1/5，6 条不再丢。
- Secret Wood Weeds 的 4 条 `Lumisteria.SereneMeadow_Node_SeedWeed*` 已写入 `Woods.Forage`。
- Forest 春季每条 `Chance` 从 0.2 变成 0.5（分母 2 条当季）。
- 报告 notes 9 条、无重复；`--packs` 自动带上 required 的 CP 包，
  `Lumisteria.SereneMeadow_ForestMeadow` 有 `CreateOnLoad` 且 Forage/ArtifactSpots 合并。
