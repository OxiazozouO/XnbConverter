# 纯原版 XNB 装机：DLL 侧到底会失去什么

日期：2026-10-20

> ⚠ **本文第 1 节的“不能烘”结论已被同日重新评估修正**：原版 1.6 有大量可追加的数据表
> （`Data/Furniture`、`Data/AdditionalWallpaperFlooring`、`Data/Objects`、`Data/Crops`、
> `Data/Buildings`、`Data/Pets`…）。AT/JsonAssets/SF/CC 等应转成**新增条目**，不是“无落点”。
> 逐框架的映射见 [`2026-10-20-additive-baking-reassessment.md`](2026-10-20-additive-baking-reassessment.md)。
> 本文继续有效的是 **DLL 行为/UI/运行时状态** 那一层的损失，以及“不处理时会丢什么”的账。

口径：`D:\Steam\steamapps\common\Stardew Valley\Mods`（当前全量语料）。
**纯原版 XNB 装机** = 原版游戏 + 静态 `Content/` 树，没有 SMAPI、没有 DLL、没有内容包加载器。
本文只回答「哪些东西本来由 DLL/框架提供，静态 XNB 拿不到」，不重复
[`2026-10-10-known-losses.md`](2026-10-10-known-losses.md) 里已经逐条量过的 CP 条件损失。

---

## 0. 先给结论

当前语料的全账：

```
491 份 manifest
├─ 221 个带 EntryDll 的代码模组（220 个不同 UniqueID；2 个 manifest 没写 ID）
└─ 270 个内容包
   ├─ 206 个 ContentPatcher 包        ✅ 可转
   ├─  26 个 FarmTypeManager 包       ✅ 可转（只转得到觅食/埋藏物）
   └─  38 个别的框架包                ❌ 一个都不转
```

另外，270 个内容包里有 **48 个声明了至少一个非 CP/FTM 的必需 DLL 依赖**（共 31 个不同的 DLL）；
这些包即使内容格式是 CP/FTM，也不能在纯 XNB 下独立成立。

> 口径订正：`Fish Info` 的 manifest 写的是 `EntryDLL`（全大写），SMAPI 照常加载。
> manifest 字段应按不区分大小写读；本轮已同步修 `tools/cppacks.py` 的计数与
> `DllConfigTokens.HasEntryDll`（含单测），所以总数从 220 订正为 221。

所以纯 XNB 的损失分三层：

1. **38 个非 CP/FTM 内容包整包不转**（AT 17、CC 10、SF 2、BL 2、CCCB 2、MFM 2、
   TS 1、MoreGrass 1、BBM 1）。
2. **232 个 CP/FTM 包里，仍有 230 条补丁 / 633 个条目写的是已安装 DLL 注册的资产**
   （33 个包；另有 81 条 / 123 个条目写的是未安装框架或别的包命名空间）。
   这些补丁在报告里是 `NoLandingPoint`：XNB 里没有消费者。
3. **221 个 DLL 的行为、UI、运行时状态全部不存在**：同伴 AI、技能/职业、机器规则、
   捆绑包、书、笔记、饰品、光照/着色器、自动化/QoL，以及地图动作、事件命令、
   触发器、自定义地点类。即使对应的地图/贴图已经烘进 XNB，交互也可能静默失效。

一句话：**CP/FTM 里 authored 的“数据”大多保住了；DLL 提供的“运行时语义 + UI + 状态”没有。**
其中物品/家具/作物/建筑/宠物/邮件这些**可数据化的内容**，可以按新增条目补回
（不是保留运行时替换），见 [`2026-10-20-additive-baking-reassessment.md`](2026-10-20-additive-baking-reassessment.md)。

### 0.1 同日后续：语料扩到 662 份后的“仍未吸收”清单

`D:\Steam\...\Mods`（491 份 manifest）+ `D:\corpusC\Mods`（171 份）重扫：

```
内容包 346 个
├─ CP 262 + FTM 39                                  ✅ 转换器吃
├─ AT 17 + CC 14 + CCCB 4 + MFM 2 + SF 2            ✅ bake-frameworks 增量烘
└─ BusLocations 2 + BushBloomMod 2 + MoreGrass 1 + TrainStation 1   ⚠ 运行时框架（已做静态近似）
```

- 上述 4 个框架**都没有原版数据表**，但其中 3 个已经做了可落 XNB 的近似：
  Bus/Train 在 `Maps/BusStop` / `Maps/Railroad` 上复制售票机/箱子图块并加
  `Action: Warp <x> <y> <地图>`（点一下直达；票价/条件/选站 UI 丢失）；
  MoreGrass 把每季 3 张草覆盖进原版 `TerrainFeatures/grass`（全局换草，其余 10 张/季丢失）。
  **BushBloom 仍然没有落点**：原版灌木花期写死春 15–18 / 秋 8–11，掉落物写死 (O)296/(O)410，
  没有自定义计划/物品/贴图槽位。细节见重新评估 §2.8。
- 316 个 DLL 模组（Steam 221 + corpusC 95）按“纯 XNB”前提本来就不运行；
  其中 JsonAssets / ProducerFrameworkMod / FashionSense / SpaceCore / CustomBush / ShopTabs 等
  **当前语料没有它们的内容包**，所以没有可烘对象。
- 已有 CP/FTM 包里的 DLL 目标（SpaceCore、BushBloom、CustomBush、ItemExtensions 等）
  仍按 `NoLandingPoint` 报告；Steam 全量那轮是 230 条补丁 / 633 个条目（33 个包；另有 48 个包
  声明了至少一个非 CP/FTM 的必需 DLL 依赖）。corpusC 尚未做同口径全量统计（未确证）。

---

## 1. 38 个非 CP/FTM 框架包：逐包量过的内容

> 2026-10-20 后续：AT/SF/CC/CCCB/MFM 已实现增量烘焙；Bus/Train/MoreGrass 做了静态近似；
> 真正完全没有落点的只剩 BushBloom（见 §0.1 与重新评估 §2.8）。

| 框架 | 包数 | 量出来的内容 | 纯 XNB 下玩家看到什么 | 能不能烘 |
|---|---:|---|---|---|
| Alternative Textures | 17 | **381 个 `texture.json`、1399 个变体**；251 家具 / 121 可制作物 / 5 作物 / 4 装饰 | 所有“漆桶/换色”变体都没有；原版家具仍是原样 | ✅ **可增量**：变体追加成新家具/可制作物/作物/墙纸条目；不是替换原物（见重新评估 §2） |
| Custom Companions | 10 | **216 个 `companion.json`** + 1 个 `object.json`（戒指） | 216 只伙伴/小动物全部没有；跟随、碰撞、闲逛、检视对话、肖像、生成规则都消失 | ⚠ **可增量**：落成 `Data/Pets` 宠物类型/品种；跟随/多只/检视对话降级（见 §4.2） |
| Solid Foundations | 2 | **21 个建筑、145 个皮肤、10 个 DrawLayer** | 木匠菜单里没有这些建筑；已有存档里的建筑没有定义 | ✅ **可增量**：基础建筑 + 每个 Skin 一条独立 `Data/Buildings`；`DrawLayers` 原版有；灯光丢失（见 §4.1） |
| Challenging CC Bundles | 2 | 每包 **48 个捆绑包定义**；其中 **27 个名字**能对上原版 `Data/Bundles` 的 31 条，另外 21 个要别的包先提供同名的 mod 捆绑包 | 社区中心仍是原版捆绑包 | ✅ 高：源码只做“按首段名字替换已有 `Data/Bundles` 值”，不新增槽位；选一套变体写进去即可，框架的配置选择逻辑丢失 |
| Mail Framework | 2 | **3 封信** | 信件、附件、触发都不存在 | ⚠ 中高：正文/标题/普通附件可落 `Data/Mail`，条件大多可落 `Data/TriggerActions`；自定义信纸、指定 `Recipe`、`RandomChance` 需要近似或报告为丢失 |
| Bus Locations | 2 | **2 个巴士目的地**（沙漠、Zuzu） | 巴士仍只有原版沙漠目的地 | ✅ 静态近似：`Maps/BusStop` 加 `Action: Warp` 图块；沙漠原版已有，跳过；票价/选站 UI 丢失 |
| Train Station | 1 | **1 个火车站** | 没有 Zuzu 的火车目的地 | ✅ 静态近似：`Maps/Railroad` 加 `Action: Warp` 图块；票价/条件/选站 UI 丢失 |
| More Grass | 1 | **52 张草贴图** | 原版草；模组的草地外观不出现 | ⚠ 静态近似：每季 3/13 张覆盖原版 `TerrainFeatures/grass`；全局换草，其余变体/位置白名单丢失 |
| Bush Bloom | 1 | **1 条灌木开花计划** | 灌木不会按计划开花/结果 | ❌ 原版没有灌木数据表；地图图块只能放基础灌木 |

> 这些包不是“转换器漏了某条补丁”，而是**它们的加载器本身就是 DLL**。
> `convert-mods.py` 目前只在开头和 `_not-converted.txt` 里列名字，不会把上表的数量写进报告。
>
> **Json Assets 当前语料是 0 个内容包**（只有框架 DLL、以及 CP 包里的
> `{{spacechase0.JsonAssets/ObjectId:…}}` 引用）；但它的 12 类内容里 11 类有原版数据落点，
> 遇到真实包时应按重新评估 §3 映射，不要再判成 `NoLandingPoint`。

### 1.1 Custom Companions：能落成宠物，但跟随 AI 会降级

它的 `companion.json` 里不只有贴图，还有：

- `IdleBehavior` / `IdleArguments`（Pacing、Wander、跟随距离）；
- `TravelSpeed`、`EnableFarmerCollision`、`EnableShadow`、`SpawnDirection`；
- `UpAnimation` / `DownAnimation` / `LeftAnimation` / `RightAnimation` 的帧区间；
- `InspectionDialogue`、`Portrait`、`DialogueSequence`、`Sounds`。

原版 `Data/Pets` 并不是“只能一只猫/狗”：`PetData` 有 `Behaviors` 状态机、`Breeds`、
`AdoptionEvent*`、`MoveSpeed`、礼物；`CharacterCustomization` 会遍历所有类型/品种。
所以 216 个同伴可以逐个落成宠物类型/品种，**资产/动画/名字能保住**；
丢的是“跟着玩家走、同时召唤多只、按路径巡逻、被检视时说那句话”。

### 1.2 Alternative Textures：不能保留漆桶，但可以逐个变体加条目

AT 的 `texture.json` 描述的是**同一物品的多个可选外观**：

```json
{ "ItemName": "Bed", "Type": "Furniture", "TextureWidth": 64,
  "TextureHeight": 64, "Variations": 9 }
```

原版 `Data/Furniture` 只有一个 `Texture` + `SpriteIndex`，**确实不能表达“同一件物品的运行时替换”**。
但可以反过来做：**每个变体追加成一条新家具**，`Texture` 指向该变体的专属贴图。
`Furniture Catalogue` 的 `ALL_ITEMS (F)` 会自动列出新家具；Decoration 更是 AT 自己就在往
`Data/AdditionalWallpaperFlooring` 追加。丢的是漆桶/剪刀 UI，不是外观本身。
详见 [`2026-10-20-additive-baking-reassessment.md`](2026-10-20-additive-baking-reassessment.md) §2。

---

## 2. 232 个可转包里，仍然写向 DLL 的部分

全量报告（`D:\tmp\prof-fix26`，231 个有补丁的包）统计：

- **230 条补丁 / 633 个条目**，目标命名空间属于**已安装的 DLL**；
- 分布在 **33 个包**；
- 另有 **81 条补丁 / 123 个条目**写向未安装框架或别的包的命名空间
  （这些在 SMAPI 下也未必成立，不能全算成纯 XNB 的新损失）。

### 2.1 按框架汇总

| 目标命名空间 | 补丁 | 条目 | 包 | 报告判定 |
|---|---:|---:|---:|---|
| `spacechase0.SpaceCore` | 163 | 438 | 21 | 153 `NoLandingPoint`，9 `NotApplicable`，1 `TargetMissing` |
| `NCarigon.BushBloomMod` | 28 | 44 | 5 | 7 `NoLandingPoint`，21 `TargetMissing` |
| `furyx639.CustomBush` | 16 | 24 | 2 | 16 `NoLandingPoint` |
| `mushymato.TrinketTinker` | 6 | 7 | 1 | 6 `NoLandingPoint` |
| `sophie.Calcifer` | 5 | 9 | 4 | 5 `NoLandingPoint` |
| `SunberryTeam.SBVSMAPI` | 5 | 5 | 1 | 4 `NoLandingPoint`，1 `NotApplicable` |
| `Esoterick.DynamicWindows` | 1 | 1 | 1 | 1 `Converted`（普通资产） |
| `mistyspring.itemextensions` | 1 | 3 | 1 | 1 `NoLandingPoint` |
| `mushymato.MMAP` | 1 | 5 | 1 | 1 `NoLandingPoint` |
| `platinummyr.CustomBackpackFramework` | 1 | 5 | 1 | 1 `NoLandingPoint` |
| `KCC.SnS` | 1 | 27 | 1 | 1 `NoLandingPoint` |
| `ES.BarberShop` | 1 | 1 | 1 | 1 `Converted` |

`TargetMissing` 不是“我们没写”，而是包要改的资产由框架运行时生成；纯 XNB 里连目标文件都不存在。
最典型的是 BushBloom 的 21 条贴图目标。

### 2.2 SpaceCore：163 条里都是什么

| 注册表 | 补丁 | 条目 | 包里在做什么 | 静态 XNB 的结果 |
|---|---:|---:|---|---|
| `TextureOverrides` | 95 | 95 | 给物品/家具做**动画帧覆盖**，如 `SourceTexture: ...PendantAnimation:0..3@9` | 只能取第一帧烘进底图；动画没有 |
| `ObjectExtensionData` | 25 | 158 | 分类名/颜色覆盖、`ConsumeForTriggerAction`、`UseForTriggerAction` 等 | 原版 `ObjectData` 没有这些字段；分类显示与触发器语义丢 |
| `WearableData` | 9 | 29 | 衣服/裤子“穿上给 buff” | 1.6 的 `ShirtData`/`PantsData` 没有 buff 字段 |
| `CookingRecipeOverrides` | 7 | 29 | 改配方成品/材料/展示 | 成品+材料可部分映射；覆盖文本/贴图不行 |
| `CropExtensionData` | 5 | 19 | 产量、经验、再生、掉落 | 只有 `RegrowDays` 等少数对得上 |
| `FurnitureExtensionData` | 5 | 22 | 家具扩展字段 | 原版没有对应字段 |
| `CraftingRecipeOverrides` | 5 | 24 | 制作配方覆盖 | 同烹饪：只能部分映射 |
| `NpcExtensionData` | 3 | 7 | NPC 扩展行为/数据 | 没有数据槽 |
| `Dungeons` | 2 | 4 | SpaceCore 运行时地牢 | 没有 |
| `ScheduleAnimationExtensionData` | 2 | 2 | 日程动画 | 没有 |
| `ShopExtensionData` | 1 | 1 | 商店扩展 | 没有 |
| `SpawnableDefinitions` | 1 | 40 | 可生成物定义 | 没有 |
| `SpawningGroups` | 1 | 6 | 生成组 | 没有 |
| `Guidebooks` | 1 | 1 | 指南书 | 没有 |

⚠ `TextureOverrides` 的 95 条不能简单说成“95 个物品丢了”：它们大多是**动画**，
底图通常由同包的普通 `Load`/`EditImage` 写出。丢的是**会动的部分**。
但 `ObjectExtensionData` 的 158 个条目里，分类名/颜色/触发器是玩家可见或影响玩法的。

### 2.3 自定义令牌：425 次，14 个命名空间

扫描 206 个 CP 包的 `content.json`（文本出现次数，不等于运行期执行次数）：

| 命名空间 | 次数 | 类型 | 能否离线解 |
|---|---:|---|---|
| `Rafseazz.RidgesideVillage` | 125 | 节日/配偶/安装日/性别等运行期状态 | 只有 `RSVNPCSAttendFestivals` 等少量 config 字段可解 |
| `mistyspring.GiEXredux.code` | 82 | 难度/排除鱼类/动物产物 | 部分：`DllConfigTokens` 按 config 白名单/唯一前缀解 |
| `TheMightyAmondee.CustomTokens` | 47 | 孩子年龄、周年日等 | 运行期，不可解（且本语料未安装该 DLL） |
| `PeacefulEnd.CustomCompanions` | 35 | 某同伴是否召唤/存在 | 运行期 |
| `spacechase0.JsonAssets` | 61 | JsonAssets 物品 ID | 运行期注册表；本语料没有 JsonAssets 内容包 |
| `Hana.GNMTokens` | 20 | 代词/称谓 | 运行期（且本语料未安装） |
| `DLX.Bundles` | 18 | 是否购买/购买天数 | 运行期 |
| `Sunken_Lace.PetsEnhancedCode` | 12 | 宠物种类/状态 | 运行期 |
| `KCC.SnS` | 11 | 头发、马名、口袋维度、特殊订单书 | 运行期 |
| `PeacefulEnd.FishingTrawler.NewHorizons` | 6 | 角色外观日 | 运行期（且未安装） |
| `SunberryTeam.SBVSMAPI` | 4 | 金太阳莓阶段 | 运行期 |
| `Ivy.OrnithologistsGuild` | 3 | 能力名 | 运行期 |
| `Spiderbuttons.CMCT` | 1 | 跨模组配置 | 运行期 |

**已处理的是“值恰好来自 config.json”的那一小类**：

- `ModConfigTokens`：按 `<UniqueID>/<字段>` 读目标 mod 的 `config.json`（RSV 的
  `RSVNPCSAttendFestivals` 等）；
- `DllConfigTokens`：只对带 `EntryDll` 的 mod 做窄规则（GiEX 的 `ExcludeFishes` /
  `ExcludeAnimalProduce` / `PortraitStyle` 等，含白名单和唯一前缀）。

其余令牌是**运行期状态**，静态转换无法知道答案。处理方式只能是：
保留字面量 → 被令牌闸拒绝；或按“默认块”冻结 → 条件丢失但内容出现。
两者都不是等价转换，报告里必须说清楚。

### 2.4 地图动作 / 事件命令 / 触发器 / 自定义地点类

这些不是“资产”，而是 DLL 注册的字符串扩展点。已有逐条调查
（[`2026-10-18-dll-registered-content-surfaces.md`](2026-10-18-dll-registered-content-surfaces.md)）：

- **地图动作**：产出树 207/1172 张地图含至少一个非原版动作，共 983 项；
  其中 795 项能指到已安装 DLL 的注册键。未知动作在原版里**静默不执行**。
- **事件命令**：360 个文件 / 2574 条脚本 / 165,778 条命令；自定义命令 20 个 / 220 次，
  其中 31 次影响流程或状态。未知命令被记 Error 后跳过。
- **事件前置条件**：Sunberry 的 22 个事件用 `!SunberryTeam.SBVSMAPI_IsPassiveFestivalToday`；
  纯 XNB 下未知前置条件返回 false 且 `!` 不生效 ⇒ **这 22 个事件永远不触发**。
- **TriggerActions**：681 条里 27 条挂自定义触发器类型，合计 64 条受影响；
  原版只有 `DayEnding` / `DayStarted` / `LocationChanged` / `Manual` 四种触发器。
- **自定义地点类**：`Data/Locations` 的 `CreateOnLoad.Type` 指向 DLL 里的类时，
  纯 XNB 下地点**直接建不出来**（不是“没有逻辑”，是构造抛异常）。全语料唯一命中是
  Cape Stardew 的 `Custom_CapeKeyMaze` / `…3` / `…4`（`CapeStardewCode.CustomLavaLogic`）。

这些损失即使地图、贴图、对话都已经烘进 XNB 也仍然存在。

### 2.5 DLL 在运行时写原版资产

对 **62 个已反编译 DLL** 做写入审计（`tools/dll-asset-audit.py`），命中的原版资产 22 个。
当前 221 个 DLL 中未全部反编译，所以这是**下界**。已安装模组里的主要项：

| 资产 | 谁在写 | 纯 XNB 后果 |
|---|---|---|
| `Data/Machines` | DaLion.Professions、ExtraMachineConfig | 机器规则/产出丢失 |
| `Data/CraftingRecipes` | BetterCrafting、DaLion.Professions | 制作配方丢失 |
| `Data/Locations` | DaLion.Professions | 地点定义/行为丢失 |
| `Data/Objects` | DaLion.Professions、ItemExtensions | 物品扩展/行为丢失 |
| `Data/BigCraftables` | DaLion.Professions、TrinketTinker | 可制作物/饰品数据丢失 |
| `Data/Trinkets` | TrinketTinker | 饰品数据丢失 |
| `Data/Mail` / `Data/mail` | Unlockable Bundles、MailFrameworkMod | 信件/附件丢失 |
| `LooseSprites/Cursors` | DaLion.Professions | 光标/UI 贴图变化丢失 |
| `Maps/SlimeHutch`、`Barn3`、`Coop3`、`SVE_PremiumBarn`、`SVE_PremiumCoop` | DaLion.Professions | 这些地图的运行时改动丢失 |
| `Maps/Woods`、`Maps/VFStatue` | Visible Fish | 雕像/贴图变化丢失 |
| `Data/PeacefulEnd.Parchment/Books` | Parchment | 自定义书数据（框架资产，非原版） |

⚠ 早期文档的「DLL 只写 7 个原版资产」是 69 个 DLL 时代的数；当前语料已经扩到 221 个 DLL，
上面的 22 个是新的静态下界，不能再拿 7 当结论。

### 2.6 存档状态

纯 XNB 不是“把已有存档原样读进来”：

- SpaceCore / DaLion / CookingSkill 的技能、职业、经验；
- Custom Companions 的同伴实体与关系；
- Unlockable Bundles 的购买记录/购买天数；
- TrinketTinker 的饰品、ItemExtensions 的机器状态、Parchment 的书；
- 各种自定义邮件/世界状态。

这些字段在纯原版里没有消费者。读旧存档时通常不会崩，但对应内容会消失或退化成默认值；
新存档也无法再获得它们。装机说明里应明确“建议新档”或“接受这些系统丢失”。

---

## 3. 221 个 DLL 按“失去的是什么”分组

不逐个列 221 个，只列对玩家有感知的组：

| 组 | 代表 | 失去的东西 |
|---|---|---|
| 内容框架 | SpaceCore、Json Assets、Fashion Sense、Alternative Textures、Custom Companions、Solid Foundations、Train Station、Bus Locations、More Grass、Bush Bloom、Custom Bush、Parchment、Unlockable Bundles、Trinket Tinker、Item Extensions、Extra Machine Config、Producer Framework、Livestock Bazaar、Calcifer、MEEP、MMAP、BETAS、CMCT | 框架注册表、运行时资产、UI、动作/令牌 |
| 大型内容包代码 | SVE Code、RidgesideVillage、East Scarp C#、Sunberry、Sword & Sorcery、CapeStardewCode、Downtown Zuzu DLL、GiEX、Visit Mount Vapius、Distant Lands | 自定义地点类、地图动作、事件命令、节日/商店/菜单逻辑、特殊状态 |
| 玩法系统 | DaLion Professions/Core、Better Crafting、Better Junimos、Horse Overhaul、Visible Fish | 职业/技能、制作 UI、祝尼魔行为、马匹、可见鱼 |
| 视觉/演出 | Dynamic Windows、DynamicShader、SDV-Radiance、Global God Rays、Indoor & Outdoor Lighting、Smooth Swapping、LunnaseasonPortraits | 光照、着色器、窗户、反射、动画、立绘切换 |
| UI/便利 | Lookup Anything、Data Layers、NPC Map Locations、Event Lookup、UI Info Suite、World Atlas、Shop Tabs、Convenient Inventory、Bigger Backpack、Wear More Rings | 查询/地图/事件/背包/戒指等界面与便利功能 |
| 工具/性能/存档 | Save Backup、Input Method Fix、Console Commands、TimeSpeed、StardewHack、Loading Optimizer 等 | 与内容无关；纯 XNB 下只是没有这些功能 |

> 这张表是“玩家会问‘我的 XX 哪去了’”的答案。**DLL 不是只提供几张贴图**；
> 221 个里很大一部分是 UI/行为/系统，XNB 的设计里没有它们的位置。

---

## 4. 哪些东西其实保住了

不要因为上面这张表就得出“纯 XNB 什么都保不住”：

- 206 个 CP 包 + 26 个 FTM 包里 authored 的**数据**大部分能落地：
  物品/大件/家具/作物/果树/动物/机器/配方/商店/对话/事件/日程/肖像/地图/图集/音频。
- FTM 的觅食与埋藏物已经翻成 `Data/Locations`；刷怪/刷矿/大件仍然没有落点
  （见 [`2026-10-10-known-losses.md`](2026-10-10-known-losses.md) §2.5）。
- CP 的 `Data/AudioChanges` 已经能注入 XACT（含循环 cue 的字节级支持）；
  但“进游戏真的循环”仍未实机确证。
- 季节图集、四季 BigCraftable、NPC 季节外观、翻译、ContentHashes、硬链接去重
  这些已有实现，不在本文的 DLL 损失范围内。

**判断标准**：内容是不是由 CP/FTM 的“数据”描述？
是 → 大多能落地；需要 DLL 在运行时读注册表/改资产/执行代码 → 不在 XNB 里。

---

## 5. 下一步优先级（按“可机械修复 + 玩家可见”）

1. **把 38 个不转包的账写进转换报告**（不是只列名字）：
   每个框架的包数、条目数、玩家可见后果。`convert-mods.py` 现在已按框架打印包数
   （`AT 17 / CC 10 / …`），但 `_not-converted.txt` 仍只有包名；下一步是把内容量
   （本节表格里的 381/1399、216、21/145…）和“玩家会看到什么”一起落盘。
2. **AT → 新增条目**：1399 个变体是当前语料最大的一块可恢复内容。
   先做 Decoration（精确）、Furniture（新家具）、Craftable（大件/栅栏）、Crop（新种子）。
   丢的是漆桶/剪刀 UI，不是外观。详见重新评估 §2。
3. **Json Assets → 原版数据表**：当前 0 个包，但 12 类里 11 类有落点（§3；Forge 除外）。
   遇到真实包时按 Object/Crop/FruitTree/BigCraftable/Hat/Weapon/Shirt/Pants/Boots/Fence 映射；
   `Forge` 没有原版表，单独报告。
4. **SF → `Data/Buildings`**：基础建筑 + 每个 Skin 一条独立建筑；`DrawLayers` 原版支持；
   灯光报告为丢失（§4.1）。
5. **CC → `Data/Pets`**：216 个 companion 落成宠物类型/品种；纹理重排；
   跟随/多只/检视对话降级（§4.2）。
6. **CCCB / MFM**：`Data/Bundles` 精确替换；`Data/Mail` + `Data/TriggerActions` 部分映射。
7. **SpaceCore 字段子集**：简单布尔、`RegrowDays`、配方覆盖、`TextureOverrides` 首帧；
   其余按损失报告。
8. **仍然只能近似/放弃的**：MoreGrass、BushBloom/CustomBush、Train/Bus 的站点 UI。
   它们没有原版数据表；不要为了覆盖率硬写语义不对的条目。

---

## 6. 复现口径

- 模组清单与框架计数：`D:\Steam\steamapps\common\Stardew Valley\Mods` 下
  `manifest.json` 的 `EntryDll` / `ContentPackFor` / `Dependencies`。
- 框架包内容计数：AT 的 `texture.json`（`Variations` / `ManualVariations`）、
  CC 的 `Companions/**/companion.json`、SF 的 `Buildings/**/building.json`、
  CCCB/MFM/BL/TS/BBM 的自身 JSON。
- 目标命名空间与判定：`D:\tmp\prof-fix26\*\report.json` 的 `patches[].target` /
  `verdict` / `entryCount`（231 个有补丁的包）。
- DLL 运行时写入：`python tools/dll-asset-audit.py D:\tmp\research-dll\decompiled`
  （62 个已反编译 DLL，结果为下界）。
- 动作/事件/触发器的逐条数字与源码依据：见
  [`2026-10-18-dll-registered-content-surfaces.md`](2026-10-18-dll-registered-content-surfaces.md)。

⚠ 本文的数字是**当前语料的快照**，不是“所有 Stardew 模组”的普遍结论；
换语料要重跑，不要把 38 / 230 / 425 当常量。
