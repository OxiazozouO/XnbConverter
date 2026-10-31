# 重新评估：把原版 XNB 当数据库，能不能把框架内容「加进去」

日期：2026-10-20
起因：用户指出上一版结论（`2026-10-20-pure-xnb-dll-losses.md`）把
「原版只有一个 `Texture` 字段」误当成「原版装不下这些内容」。正确的问法不是
「原版能不能表达运行时的‘替换/可选’」；而是
**「原版有没有一张能追加条目的数据表」**。按后者重新看，很多框架内容不是不能转，
只是要**换成新增条目**，而不是保留原来的运行时交互。

---

## 0. 结论先行

| 框架/内容 | 原版数据落点 | 增量做法 | 保真度 |
|---|---|---|---|
| **Alternative Textures · Decoration** | `Data/AdditionalWallpaperFlooring` | 直接把 AT 的 PNG 作为新墙纸/地板资产，追加 `{Id,Texture,IsFlooring,Count}` | **几乎精确**（AT 自己就是往这张表加） |
| **Alternative Textures · Furniture** | `Data/Furniture` | 每个变体追加一条家具；克隆原物品的类型/尺寸/价格，换 `Texture` + `SpriteIndex` | **高**（外观保留；变成新家具，不是漆桶替换） |
| **Alternative Textures · Craftable** | `Data/BigCraftables` / `Data/Fences` + `Data/Objects` + `Data/CraftingRecipes`/`Data/Shops` | 每个变体追加可制作物/栅栏条目和获取方式 | **中高**（栅栏贴图布局要逐包核） |
| **Alternative Textures · Crop** | `Data/Crops` + `Data/Objects` + `Data/Shops` | 每个变体追加一个种子/作物；复用原作物产物 | **中高**（外观保留；产物是同一个） |
| **Json Assets** | `Data/Objects`、`Crops`、`FruitTrees`、`GiantCrops`、`BigCraftables`、`Fences`、`Weapons`、`Boots`、`hats`、`Shirts`、`Pants`、`Furniture`、`CraftingRecipes`、`CookingRecipes`、`Shops`、`NPCGiftTastes`、`TailoringRecipes` | 基本是 1:1 数据映射；JA 的 `GetObjectInformation()` / `GetCropInformation()` 已经是原版数据形状 | **高**（少数自定义分类/额外贴图/锻造配方除外） |
| **Solid Foundations** | `Data/Buildings` | 基础建筑 + 每个 Skin 追加一条独立建筑；`DrawLayers` 原版有 | **高**（灯光没有原版字段） |
| **Custom Companions** | `Data/Pets` | 每个 companion 追加一个宠物类型/品种，映射走路/闲置动画 | **中**（宠物而不是跟随伙伴；同时只能有一只；检视对话丢失） |
| **Challenging CC Bundles** | `Data/Bundles` | 按首段名字替换已有捆绑包值（27/48 能对上原版） | **高**（选一套变体） |
| **Mail Framework** | `Data/Mail` + `Data/TriggerActions` | 正文/普通附件/条件 | **中高**（指定 Recipe、RandomChance、信纸不行） |
| **Bush Bloom / Custom Bush** | 没有原生灌木数据表 | 只能改地图图块放基础灌木；开花计划没有落点 | **低** |
| **More Grass** | 没有原生草类型表 | 只能替换全局草贴图，不能同时保留 52 种 | **低** |
| **Train Station / Bus Locations** | 没有原版「站点/目的地」数据表 | 可以加地图/传送点/动作；没有买票/选站 UI | **中低** |
| **SpaceCore 注册表** | 一半有原版字段，一半没有 | 见 §5 逐表映射 | **部分** |

**一句话**：Json Assets 和 Alternative Textures 这类「内容包」应该按**新增数据库行**来转；
上一版把它们标成「不能烘」是口径错了。真正不能加的是**运行时行为/UI/存档状态**那部分，
不是物品、家具、作物、建筑、宠物、邮件这些**数据**。

---

## 1. 判据：看原版有没有「可追加的数据表」

以 `D:\sdv_gd` 的模型 + `D:\sdv_src` 的消费代码为准：

- **可追加** = `Dictionary<string, XData>` / `List<XData>`，游戏按 ID 遍历；
  新条目不需要改原版条目，也不需要按编号挤图集。
- **不可追加** = 游戏在代码里写死类型/行为，或必须靠 Harmony 补丁改运行时对象。

这条判据下，原版 1.6 已经是一套相当完整的「数据库」：

| 数据表 | 能装什么 | 模型/消费方 |
|---|---|---|
| `Data/Objects` | 普通物品、种子、鱼、戒指、食物、药水 | `StardewValley.GameData.Objects/ObjectData.cs` |
| `Data/Crops` | 作物生长阶段、产物、再生、棚架 | `...Crops/CropData.cs` |
| `Data/FruitTrees` | 果树季节/果实/树贴图 | `...FruitTrees/FruitTreeData.cs` |
| `Data/GiantCrops` | 巨型作物 | `...GiantCrops/GiantCropData.cs` |
| `Data/BigCraftables` | 可放置大件 | `...BigCraftables/BigCraftableData.cs` |
| `Data/Fences` | 栅栏血量/贴图/工具 | `...Fences/FenceData.cs` |
| `Data/Furniture` | 家具（斜杠串） | `FurnitureDataDefinition.cs` |
| `Data/AdditionalWallpaperFlooring` | 新增墙纸/地板 | `StardewValley.GameData/ModWallpaperOrFlooring.cs` |
| `Data/Weapons` / `Boots` / `hats` / `Shirts` / `Pants` | 装备 | 各自的 `*DataDefinition.cs` |
| `Data/Buildings` | 建筑、皮肤、绘制层 | `...Buildings/BuildingData.cs` |
| `Data/Pets` | 宠物类型、品种、行为状态机 | `...Pets/PetData.cs`、`PetBreed.cs`、`PetBehavior.cs` |
| `Data/CraftingRecipes` / `CookingRecipes` | 配方 | `DataLoader.cs` |
| `Data/Shops` | 商店货架/条件/价格 | `DataLoader.Shops` |
| `Data/NPCGiftTastes` | NPC 礼物偏好 | `DataLoader.cs` |
| `Data/TailoringRecipes` | 裁缝配方 | `DataLoader.TailoringRecipes` |
| `Data/Bundles` | 社区中心捆绑包 | `DataLoader.Bundles` |
| `Data/Mail` + `Data/TriggerActions` | 邮件/触发动作 | `DataLoader.cs` |

> 注意：`Data/Furniture` 仍是**斜杠串**，不是类；但 `FurnitureDataDefinition` 明确读了
> 第 8/9/10 字段的 `SpriteIndex` / `Texture` / 布尔值，所以“换一张专属贴图”是原版支持的。
> 同理 `Data/hats`、`Data/Boots` 也是斜杠串，Json Assets 已经按这些格式生成。

---

## 2. Alternative Textures：按变体追加条目

AT 的 `texture.json` 只有：

```json
{ "ItemName": "Artist Bookcase", "Type": "Furniture",
  "TextureWidth": 32, "TextureHeight": 48, "Variations": 7 }
```

它没有价格、尺寸、配方；这些要从**基础物品**的现有数据克隆。当前语料里 AT 的 381 个
`texture.json` / 1399 个变体，分四类：

| Type | `texture.json` | 变体 | 做法 |
|---|---:|---:|---|
| Furniture | 251 | 多 | 克隆 `Data/Furniture[base]` 斜杠串，换 0/7/8/9 字段 |
| Craftable | 121 | 多 | 优先查 `Data/Fences`，否则 `Data/BigCraftables`；再补 `Data/Objects` + 配方/商店 |
| Crop | 5 | 4 左右 | 克隆 `Data/Crops[seed]`，换 `Texture`/`SpriteIndex`；新增种子 `Data/Objects`；种子加回原商店 |
| Decoration | 4 | 56/48… | **直接**追加 `Data/AdditionalWallpaperFlooring`，不需要克隆物品 |

### 2.1 Decoration：几乎零损失

AT 自己就是这么干的（反编译 `AlternativeTextures` 的 `OnAssetRequested`）：

```csharp
new ModWallpaperOrFlooring {
  Id = model.GetId(),
  Texture = "AlternativeTextures/Textures/" + model.GetTokenId(),
  IsFlooring = model.ItemName == "Floor",
  Count = model.GetVariations()
}
```

⇒ 静态转换只要把 PNG 复制进产出树，再往 `Data/AdditionalWallpaperFlooring` 追加同样一条。
原版 `Catalogue` 里的 `ALL_ITEMS (WP)` / `ALL_ITEMS (FL)` 会自动把新条目列出来。
**这是精确的，不是近似。**

### 2.2 Furniture：新家具，不是漆桶

⚠ AT 既支持单张 `texture.png`（纵向堆叠变体），也支持 `texture_1.png … texture_N.png`
（每个文件一个变体；实测 `[AT] Shyzie's Rugs` 是后者）。烘焙器两种都要读：
前者用 `SpriteIndex` 选变体，后者把每个文件各作为一张独立贴图。

`Data/Furniture` 的字段（从 `FurnitureDataDefinition` 与 `Furniture.cs` 读出）：

```
0 内部名 / 1 类型 / 2 精灵尺寸 / 3 包围盒 / 4 旋转数 / 5 价格 /
6 放置限制 / 7 显示名 / 8 SpriteIndex / 9 Texture / 10 布尔
```

做法：

1. 在产出树的 `Data/Furniture` 里按 `ItemName` 找到基础条目；
2. 克隆整条斜杠串；
3. 覆盖 `0` 为唯一内部名、`7` 为变体名、`8` 为 `0`、`9` 为新贴图资产；
4. 若 AT 的 `TextureWidth/Height` 与基础条目的 `2` 不同，按 `宽/16 高/16` 写 `2`；
5. 追加到 `Data/Furniture`。

AT 的 PNG 是**纵向堆叠**的变体条；原版家具的源矩形公式是
`SpriteIndex*16 % texture.Width` / `SpriteIndex*16 / texture.Width*16`，
所以只要宽高都是 16 的倍数，可以直接复用原 PNG，用
`SpriteIndex = 变体号 × (TextureWidth/16) × (TextureHeight/16)` 选到对应变体，
不必切图；宽高不是 16 的倍数时改为**每个变体切一张 PNG**（此时 `SpriteIndex=0`）。
原版 `Furniture Catalogue` 的 `ALL_ITEMS (F)` 会自动列出来。

**代价**：它变成“新家具”，不是“同一件家具换外观”。玩家不能用漆桶切换；
但 1399 个外观都能作为独立家具拿到。这正是用户说的“加数据库”。

### 2.3 Craftable：大件 / 栅栏 / 配方

- **BigCraftable**：克隆 `Data/BigCraftables[base]`，换 `Name/DisplayName/Texture/SpriteIndex`；
  再克隆基础配方（`Data/CraftingRecipes`）或把基础商店条目复制一条到 `Data/Shops`。
- **Fence/Gate**：克隆 `Data/Objects[base]`（栅栏物品）和 `Data/Fences[base]`，
  新 `Data/Fences.Texture` 指向 AT 的 PNG；克隆 `Data/CraftingRecipes` 的输出 ID。
  ⚠ AT 的栅栏 PNG 是 48 宽、按连接形状排的紧凑布局；原版 `Fence` 的源矩形公式相同，
  但**每个包的布局要逐张核**（先做一包，比对四种连接/门）。
- **其它 Craftable**（Anvil、Auto-Grabber 等）：按 BigCraftable 处理。

### 2.4 Crop：新种子，复用产物

- AT 的 `ItemName` 是**种子 ID**（如 `QL_CallaLilySeeds`）；在 `Data/Crops` 找到基础作物；
- 克隆 `Data/Crops[seed]`，`Texture` 指向 AT 的 PNG，`SpriteIndex` 设为变体行号；
  AT 的作物 PNG 宽度通常就是 `128`（8 个 16px 阶段），高度按变体堆叠，和原版作物行格式一致；
- 新增种子 `Data/Objects`：克隆基础种子条目，换 `Name/DisplayName`，图标仍用基础种子；
- `HarvestItemId` 仍指向基础产物（**不新增产物**；外观是作物的，不是果实的）；
- 在 `Data/Shops` 里找到基础种子所在的货架，复制一条换成新种子 ID；找不到就报告“不可获得”。

**代价**：新增的是一种“同产物、不同外观”的作物；不是给原作物换皮。

### 2.5 实测（2026-10-20，C# `bake-frameworks` 命令）

拿 `D:/tmp/prof-fix26/tree` 作基础，直接跑了三类 AT 包（后来固化成 C# 的
`SdvCpTool.Cli bake-frameworks`）：

```
[AT] Shyzie's Rugs（10 个 texture_N.png）→ 10 条新 Data/Furniture 条目
[AT] Floors and Walls by Mila             → 56 条 Floor + 48 条 Wallpaper
                                            （Data/AdditionalWallpaperFlooring）
Quaint Living - Flower Garden Variety      → 20 条新种子/作物（5 个模型 × 各自变体数；
                                            Data/Crops 用 SpriteIndex=变体行号 + Data/Objects）
```

生成后的 `Data/Furniture.json`、`Data/AdditionalWallpaperFlooring.json`、
`Data/Crops.json`、`Data/Objects.json` 用打包器回包 **4/0**。
C# `FrameworkBaker` 现在会同时补 PNG `.config` 与 `ContentHashes` 键；
按工具实跑的口径，四类 AT 包（地毯/墙纸地板/作物/栅栏）共 243 个资产回包 **243/0**。
这条探针证明了“克隆基础条目 + 追加行”的路线不是纸上推演。

### 2.6 已落地的工具

```bash
SdvCpTool.Cli bake-frameworks --tree <产出树>/tree --mods <Mods> [--packs 子串…]
# 或在整包转换的最后一步：
python tools/convert-mods.py <产出根> --bake-frameworks
```

它只写**真正改过的 Data 文件**；重复跑是幂等的（第二次计数 0，不重复追加）。
当前实现 AT/SF/CC/CCCB/MFM；Json Assets 按 §3 继续扩展。

### 2.7 观赏内容去重开关（默认关闭）

家具/衣服这类纯观赏内容最容易出现“同一张图换色/几乎一样”的变体。
`bake-frameworks` 提供：

```bash
--dedup-cosmetics                 # 打开（默认阈值按原版校准）
--dedup-threshold 0               # dHash 汉明距离；0 = 只去完全相同的像素块
--dedup-color-tolerance 0         # 纯变色容差；0 = 关闭
```

**阈值以“刚好不去重原版”为准**。用 C# 自带的校准命令量原版家具：

```bash
SdvCpTool.Cli bake-frameworks --calibrate-dedup --tree D:/sdv_tree
# 原版家具 645 条，去完全相同后 642 条
# 最近 dHash 距离（mask IoU≥0.85）= 0，近邻 50940 对
# 最近纯变色 MAD（mask IoU≥0.95）= 0.29
# 推荐 --dedup-threshold 0
# 推荐纯变色容差 0
```

⇒ **默认阈值 0、纯变色容差 0**：只合并“像素完全相同”的重复，绝不会误伤原版
（实测全框架小树去掉 43 个，约 6.6 秒）。
要更激进（相似/纯变色）就显式调大：

```bash
--dedup-cosmetics --dedup-threshold 6 --dedup-color-tolerance 18 \
  --dedup-max-pixel-diff 1.0 --dedup-color-consistency 2.0
```

`--dedup-max-pixel-diff` 是给相似判断加的画面差上限（默认 1.0%）：dHash 通过后，
左右图 16×16 RGB 平均差超过它就不算重复。视觉抽查里 1.94% 的窗帘、6.35% 的书脊、
8.22% 的篱笆花色都会先被这个上限挡回去；0.34–0.75% 的近同图仍会去重。

`--dedup-color-consistency` 是给“纯变色”加的一致性上限（默认 **2.0%**）：
只有整张图按近似统一的 RGB 偏移变化才算换色；局部出现橙色花/不同书脊/不同窗帘时，
偏移方差变大，会被判成不同。另外代码里固定加了一道**单格最大偏移 ≤20%** 的兜底
（`CosmeticDeduper.PureRecolorMaxDeviation`）：平均一致性会把小面积变化稀释，
实测 Basic Window 2/3 只换窗帘时平均一致性 1.65%、单格却有 33%，只靠平均值仍会误杀。

相似/纯变色比较按**基础物品 + 季节**分组（`SeasonKey`）：春季绿篱与秋季枯篱即使只差颜色
也不再互相顶掉。但“像素完全相同”的精确去重仍是全树的（例如四季 Evergreen 栅栏用的是
同一张图，仍会只留一条）。

即使加了这些保护，**仍不能保证原版不被命中** —— 原版最近 dHash 距离就是 0，
任何 >0 的相似阈值都可能把原版里形状相近的家具算成一对，所以默认仍是 0/0。

判据：

1. 像素块完全相同 → 直接去重（默认唯一启用的一层）；
2. 同一基础物品 + 季节组内，16×16 灰度 + 8×8 dHash 汉明距离 **严格小于** 阈值、
   alpha 轮廓重合、RGB 画面差 ≤ `--dedup-max-pixel-diff` → 样式相近；
3. alpha 轮廓相同、去均值后的灰度差 ≤ 容差、RGB 偏移一致性 ≤ 2% 且单格偏移 ≤20% → 纯变色。

不比较不同基础物品或不同季节（避免“椅子”和“床”轮廓相似、或春季/秋季同款换色被误删）。
命中的变体**在写资产前**跳过，
不会留下孤儿 PNG。开销是逐对比较，实测激进参数下全框架小树约 **38 秒**，所以默认关闭；
第二次跑用产出根下的 `_frameworks-dedup.json` 跳过上轮已去掉的 ID（换阈值/容差/一致性会重新计算）。

### 2.8 运行时框架的静态近似（2026-10-20 追加）

剩下几个框架没有原版数据表，但**有可落到既有 XNB 的近似**。`bake-frameworks` 现在会顺带做：

| 框架 | 静态落点 | 保住了什么 | 丢了什么 |
|---|---|---|---|
| More Grass | `TerrainFeatures/grass.png` 的每季 3 个横向槽位 | 每季 3 张自定义草（从 13 张里等距取），直接进原版草图集 | 其余 10 张/季、位置白名单、冬季生长配置；且是**全局换草**，不是新增草类型 |
| Bus Locations | `Maps/BusStop` 的 `Buildings` 空位，复制售票机图块并加 `Action: Warp <x> <y> <地图>` + `Passable: T` | **每个目的地一台售票机，沿同一行排下去，两两间隔 1 格**（Desert 跳过：原版已有售票机）；不覆盖任何非 Back 图层，设施可穿行、不堵站台 | 票价、开车动画、选站 UI、修复巴士的前置条件 |
| Train Station | `Maps/Railroad` 的 `Buildings` 空位，复制箱子图块并加同样的 Warp + `Passable: T` | **每个 TrainStops 目的地一个箱子，沿同一行排下去，两两间隔 1 格**；不覆盖非 Back 图层 | 票价、条件、选站 UI、火车动画 |
| Bush Bloom | **无** | — | 原版 `Bush.inBloom()` 只认春 15–18 / 秋 8–11、`GetShakeOffItem()` 写死 (O)296/(O)410，没有自定义计划/物品/贴图的槽位 |

MoreGrass / 交通都做了**幂等**：草槽位像素不变就返回 0；地图里已有同一 `Action: Warp` 就跳过。
代价是这些是**修改既有资产**（草图集、两地图），不是新增数据行；`--packs` 过滤照常生效。
Bush Bloom 仍是纯运行时；把它的 `berry.png` 叠到原版灌木格又会在非原版花期显示、且物品写死，故不做。

⚠ 交通图块**不新增图层**：只在既有的 `Buildings` 层空位写一个图块。原版
`GameLocation.SortLayers()` 只绘制 Id 以 `Back`/`Buildings`/`Front`/`AlwaysFront` 开头、
后面为空或整数的图层；任意名字的自定义层即使加载了也不画（详见
[`../verification/2026-10-19-startup-layers-and-experience-gaps.md`](../verification/2026-10-19-startup-layers-and-experience-gaps.md) §2）。
所以这不是“超过多少层”的问题，而是**层名必须能归到四个前缀**。实测 BusStop 仍是 8 层，
Railroad 仍是 11 层（那 11 层是 CP 输入自带的，不是这里加的）。


---

## 3. Json Assets：本来就是原版数据的另一种写法

Json Assets 的 12 类内容里，**11 类有原版落点**；`ForgeRecipeData` 没有原版数据表。
它的 `Get*Information()` 方法
（反编译 `JsonAssets.Data/*.cs`）已经在生成原版格式的字符串/对象：

| JA 类型 | 原版落点 | JA 自己生成的形态 | 备注 |
|---|---|---|---|
| `ObjectData` | `Data/Objects` | `GetObjectInformation()` → `ObjectData` | 字段几乎 1:1 |
| `CropData` | `Data/Crops` + `Data/Objects`（种子/产物）+ `Data/GiantCrops` | `GetCropInformation()` / `GetGiantCropInformation()` | 季节/阶段/再生/产量/架/染色都有 |
| `FruitTreeData` | `Data/FruitTrees` + `Data/Objects`（树苗/果实） | `GetFruitTreeInformation()` | 季节/果实/贴图 |
| `BigCraftableData` | `Data/BigCraftables` | `GetBigCraftableInformation()` | 额外索引/动画贴图会丢 |
| `HatData` | `Data/hats` | 斜杠串（`GetHatInformation()`） | 20×20 帽子格 |
| `WeaponData` | `Data/Weapons` | `WeaponData` | 伤害/击退/暴击等 |
| `ShirtData` / `PantsData` | `Data/Shirts` / `Data/Pants` | 斜杠串 | 男/女变体可合成 256×32 |
| `BootsData` | `Data/Boots` | 斜杠串 | 防御/免疫/颜色 |
| `FenceData` | `Data/Fences` + `Data/Objects` | `FenceData` + 物品 | 血量/工具/贴图 |
| `TailoringRecipeData` | `Data/TailoringRecipes` | 配方对象 | 原版有列表 |
| `ForgeRecipeData` | **没有原版数据表** | `CustomForgeRecipe`（代码） | 只能报失 |

### 3.1 对象（最经典的一类）

`ObjectData.GetObjectInformation()` 直接生成原版 `ObjectData`：

- `Name/DisplayName/Description/Category/Price/Texture/SpriteIndex`
- `Edibility/IsDrink/Buffs`
- `ContextTags`（`CanBeGifted=false` → `not_giftable`）
- `ExcludeFromShippingCollection`（`HideFromShippingCollection`）
- `IsColored` → 原版 `ColorOverlayFromNextIndex = true` + 32×16 贴图（JA 的 `color.png` 拼在右侧）
- `Recipe` → `Data/CraftingRecipes` / `Data/CookingRecipes`（JA 的 `GetRecipeString()` 已经是原版斜杠串）
- `CanPurchase` / `PurchaseFrom` / `PurchasePrice` / `AdditionalPurchaseData` → `Data/Shops`
- `GiftTastes` → `Data/NPCGiftTastes`

**丢的**：`CategoryTextOverride` / `CategoryColorOverride`（原版分类名/颜色是代码常量）、
`MaxStackSizeOverride`（原版没有字段）、`TotemWarp`、`UseForTriggerAction` /
`ConsumeForTriggerAction`（原版 TriggerActions 没有 ItemUsed 触发器）。
这些是**对象上的扩展行为**，不是物品本身。

### 3.2 作物 / 果树 / 大件 / 装备

- `CropData` 的季节、`Phases`、`RegrowthPhase`、`HarvestWithScythe`、`TrellisCrop`、
  `Bonus`、`Colors`、`SeedPurchase*` 都能落到 `CropData` + `Objects` + `Shops`；
  巨型作物落 `Data/GiantCrops`。
- `FruitTreeData` 的季节/果实/贴图/树苗购买落 `Data/FruitTrees` + `Objects` + `Shops`。
- `BigCraftableData` 的 `ReserveExtraIndexCount` 是给动画/状态用的多帧贴图；
  原版 `BigCraftableData` 只有一个 `SpriteIndex` ⇒ 只能保留第一帧，额外帧报告为丢失。
- `WeaponData`、`HatData`、`ShirtData`、`PantsData`、`BootsData` 都有对应原版表；
  JA 生成的字符串/对象可以直接写回。
- `FenceData` 落 `Data/Fences` + `Data/Objects` + 配方。

### 3.3 ID 与 CP 令牌

JA 的 ID 规则（`Extensions.FixIdJA`）是：

```
<包 UniqueID>_<Name 把 /&@#$%*{}[]\s\\ 换成 _>
```

例如 `spacechase0.JsonAssets/ObjectId:Sticky Rice Flour` 对应的物品 ID 是
`<packUniqueID>_Sticky_Rice_Flour`。静态烘焙时**沿用同一规则**，CP 里的
`{{spacechase0.JsonAssets/ObjectId:…}}` 就能在转换期解析成真实 ID；
否则那些条件/引用会继续悬空。

⚠ 当前语料里 **Json Assets 内容包是 0 个**（只有框架 DLL 和 CP 包里的令牌引用），
所以这一轮不会立刻多出 Json Assets 物品；但这条通道是完整可做的，
遇到真实包时应按本节映射，而不是再判 `NoLandingPoint`。

---

## 4. Solid Foundations / Custom Companions / 捆绑包 / 邮件

### 4.1 Solid Foundations：建筑皮肤可以拆成独立建筑

原版 `BuildingData` 比上一版估计的强得多（`D:\sdv_gd\...Buildings\BuildingData.cs`）：

- `Skins`（每个皮肤有 `Id/Name/Texture/Condition/Price/Materials`）
- `DrawLayers`（贴图、源矩形、绘制位置、背景/前景、帧动画 `FrameCount/FramesPerRow`）
- `BuildCondition`、`Builder`、`BuildCost/BuildDays/BuildMaterials`、`CollisionMap`、`SourceRect`、
  `SeasonOffset`、`HumanDoor`、`IndoorMap`、`MaxOccupants`…

SF 的 `building.json` 字段能对上大半。由于原版 `BuildingSkin` **没有**每皮肤的
`SourceRect/DrawLayers/SeasonOffset`，最稳的增量做法是：

> **每个 Skin 追加一条独立 `BuildingData`**，而不是塞进同一个建筑的 `Skins` 列表。
> 这样每个皮肤都有自己的 `Texture`、`SourceRect`、`DrawLayers`、价格和建造条件；
> 玩家在木匠菜单里看到的是多个建筑，而不是一个建筑的多个皮肤。

**丢的**：SF 的 `Lights`（原版 BuildingData 没有灯光字段；`DrawLayers` 只能画发光贴图，
不是真正的点光源）。`HideBaseTexture` 可以用透明底图近似。

### 4.2 Custom Companions：落成原版宠物类型/品种

原版 `Data/Pets` 不是只有猫狗：`PetData` 有 `Behaviors` 状态机、`Breeds`、
`AdoptionEvent*`、`MoveSpeed`、`GiftChance/Gifts`；`PetBreed` 有 `Texture`（128×256/288）、
`IconTexture`、`CanBeChosenAtStart/CanBeAdoptedFromMarnie`、`AdoptionPrice`。
`CharacterCustomization.GetPetTypesAndBreeds()` 会遍历**所有** `PetData` 和 `Breeds`。

因此 216 个 CC companion 可以按下面增量：

1. 每个 companion 建一个 `PetData`（或把多个 companion 放进同一 `PetData` 的 `Breeds`）；
2. `DisplayName` = companion 名；
3. 把 CC 的 `Up/Down/Left/RightAnimation` 帧区间映射成 `PetBehavior.Animation`；
4. `Texture` 重排成原版要求的 128×256/288（16/32px 帧）；
5. `InspectionDialogue` 没有原版宠物对话槽 ⇒ 报告丢失（或改成宠物礼物/邮件）。

**代价**：CC 的“跟着玩家走、可同时召唤多只、按路径巡逻、检视对话”变成
“可选的常驻宠物”；原版宠物一次只有一只。资产、动画、名字能保住，AI/多只不行。

### 4.3 CCCB：按名字替换 `Data/Bundles`

CCCB 源码（反编译 `ChallengingCommunityCenterBundles`）只做：

```csharp
if (CustomBundleData.ContainsKey(existingValue.Split('/')[0]))
    newBundleData[key] = CustomBundleData[existingValue.Split('/')[0]];
```

不新增键，只替换已有捆绑包的值。实测每包 48 个定义，其中 **27 个名字**能对上原版
`Data/Bundles` 的 31 条；其余 21 个要别的包先提供同名 mod 捆绑包。
⇒ 选一套变体，按首段名字替换即可，**精确**；两包互斥，需要选一套。

### 4.4 MFM：邮件仍按 `Data/Mail` + `Data/TriggerActions`

3 封信的正文/标题/普通附件/条件大多能落；自定义信纸（`LetterBG`）、指定 `Recipe`、
`RandomChance` 需要近似或报告丢失。`%item` 附件、`Days/EventsSeen/Friendship/...` 条件
可翻成 `Data/Mail` 与 `Data/TriggerActions` 的 GSQ。

---

## 5. SpaceCore：一半能映射，一半是运行时

SpaceCore 的 31 个注册表不能一概而论。按 `SpaceCore.VanillaAssetExpansion/*.cs` 逐字段看：

| 注册表 | 原版落点 | 结论 |
|---|---|---|
| `ObjectExtensionData` | `Data/Objects` 的 `CanBeTrashed` / `CanBeShipped` / `CanBeGivenAsGift` / `Edibility` / `Buffs` / `ContextTags` | ✅ 2026-10-27 已实现 `CanBeTrashed` / `CanBeGivenAsGift` / `CanBeShipped` / `HideFromShippingCollection` 四个同义字段（见 [`verification/2026-10-27-spacecore-object-extension-fields.md`](../verification/2026-10-27-spacecore-object-extension-fields.md)）；`CategoryTextOverride/Color`、`MaxStackSizeOverride`、`TotemWarp`、`Use/ConsumeForTriggerAction`、逐 NPC 礼物白名单没有原版字段 |
| `CropExtensionData` | `Data/Crops` 的 `RegrowDays` 等 | ✅ 2026-10-27 已实现**唯一值** `RegrowDays` 映射（见 [`verification/2026-10-27-spacecore-crop-extension-regrowdays.md`](../verification/2026-10-27-spacecore-crop-extension-regrowdays.md)）；多个不同值报歧义；`Drops`/`ExperienceGained`/`NewPhase`/`IgnoreDefaultDrops` 仍丢失 |
| `CookingRecipeOverrides` / `CraftingRecipeOverrides` | `Data/CookingRecipes` / `CraftingRecipes` | 成品+材料+数量可映射；展示文本/贴图覆盖不行 |
| `FurnitureExtensionData` | `Data/Furniture` 的显示名/描述 | `DescriptionOverride` 可映射；`TileProperties`/`SeatLocations` 没有原版斜杠串字段 |
| `NpcExtensionData` | `Data/Characters` 的 `CanBeRomanced` 等 | `SeparateDatability` 可近似成 `CanBeRomanced`；`GiftEventTriggers`/`IgnoreMarriageSchedule` 没有直接字段 |
| `ShopExtensionData` | `Data/Shops` 的货架条目 | 物品可加到商店；自定义 Tab/图标没有原版字段 |
| `WearableData` | 无 | 1.6 `ShirtData/PantsData` 没有 buff 字段 ⇒ 丢失 |
| `TextureOverrides` | 目标物品/家具的原贴图 | 只能烘**首帧**；动画（`SourceTexture: ...:0..3@9`）丢失 |
| `Dungeons` / `Guidebooks` / `SpawnableDefinitions` / `SpawningGroups` / `ScheduleAnimationExtensionData` | 无 | 运行时系统，丢失 |

所以 SpaceCore 的 163 条补丁不是“全丢”：`ObjectExtensionData` 的简单布尔、`CropExtensionData`
的再生天数、配方覆盖等可以先映射；剩下的扩展字段/动画/地牢仍按损失报告。

---

## 6. 仍然真的很难/不能加的

这些不是因为“原版只有一个字段”，而是**原版没有这张表/这个消费方**：

1. **More Grass**：原版 `Grass` 只有 `grassType` + 全局 `TileSheets/grass`，没有“草类型注册表”；
   只能替换全局贴图，不能同时保留 52 种。
2. **Bush Bloom / Custom Bush**：`Bush` 是地图图块/代码生成的地形特征，没有 `Data/Bushes`；
   可以改地图放基础灌木，但不能表达“每天按计划开花/结果”的每种灌木数据。
3. **Train Station / Bus Locations**：没有原版“站点/目的地”表；可以加地图/传送点/动作，
   但没有买票/选站 UI。
4. **SpaceCore 的运行时部分**：技能/职业/地牢/指南书/生成组/动画/buff 衣服。
5. **Json Assets 的 `ForgeRecipeData`**：原版没有锻造配方数据表（代码里的 `CustomForgeRecipe`）。
6. **自定义分类文本/颜色、MaxStack、TotemWarp、ItemUsed 触发器**：没有原版字段。
7. **CC 的跟随 AI / 多只同时召唤 / 检视对话**：原版宠物状态机可近似走/停，但不是跟随伙伴。
8. **AT 的漆桶/剪刀 UI**：增量后是“新物品”，不是“给已有物品换外观”。

---

## 7. 建议的落地顺序

### 第 0 步：新增一个「烘焙框架」阶段（**已落地**）

不要硬塞进 `ContentPack.Load`（那里看不到最终产出树）。现在的形态：

```
CP/FTM 转换 → 产出树（Data/* + 贴图）
            → SdvCpTool.Cli bake-frameworks（读最终树 + 各框架包；追加条目/资产）
            → 打包
```

`convert-mods.py --bake-frameworks` 会自动接上这一步；也可以单独跑。
烘焙器输出 `_frameworks-report.json`，并同步 `ContentHashes` 与 PNG `.config`。

### 第 1 步：Alternative Textures（**已实现四类；C# 单测已补，待实机**）

1. Decoration：直接追加 `Data/AdditionalWallpaperFlooring` + 复制 PNG；
2. Furniture：按 `ItemName` 找基础条目，追加 `Data/Furniture`；
3. Craftable：BigCraftable / Fence 分支；
4. Crop：新增种子 + 作物 + 商店条目。

已用 `[AT] Low Hedge Fence`、`[AT] Shyzie's Rugs` / `Seasonal Rugs`、
`[AT] Floors and Walls by Mila`、`Quaint Living`、`[AT]Yomi's cute furniture`、
`Delaney's Energetic Craftables` 做过受控对比：地毯/墙纸/作物/栅栏/家具/大件全部回包
**失败 0**；重复跑第二次新增 **0**（幂等）。**还没进游戏实机验证**；
栅栏连接形状、家具目录显示要实机看。C# 回归
`tests/SdvCpTool.Core.Tests/FrameworkBakerTests.cs` 已覆盖新增行/反斜杠贴图路径/幂等。

### 第 2 步：Json Assets（未来/别的话语料）

按 §3 的 12 类映射（Forge 除外）；先做 Object + Crop + BigCraftable + Hat + Weapon 五类，
再补 Shirt/Pants/Boots/Fence/Tailoring。没有真实包时用合成包做端到端回包验证。

### 第 3 步：SF / CC / CCCB / MFM（**均已实现基础通道**）

- SF：基础建筑 + `Skins` → `Data/Buildings`；`SourceRect` 字符串归一为对象；
  `DrawLayers` 映射；灯光报失。**已实现**（2 包 21 建筑，回包 167/0）。
- CCCB：选一套替换 `Data/Bundles`。**已实现**（默认 `Pure_Vanilla`，27 条；回包 1/0）。
- MFM：`Data/Mail` + `Data/TriggerActions`（含 `zh-CN` 变体）。**已实现**（3 封；回包 5/0）。
- CC：每 companion 一条 `PetData`（`Breeds` + `Walk` 行为/动画）。**已实现基础通道**：
  小树（带 `Animals` 资源）落 **166** 个；`TileSheetPath` 不带扩展名，需依次试 `.png`/`.xnb`。
  原版 Pet 固定按 32×32 解释，CC 的 16×16/16×32 会报“可能显示错位”；检视对话/多只/跟随仍丢。

### 第 4 步：SpaceCore 字段映射

只做有原版字段的子集（`RegrowDays`、简单布尔、配方覆盖、首帧 TextureOverrides），
其余逐条报告。不要为了“覆盖率”硬写语义不对的字段。

---

## 8. 验收口径

- 每个新增条目的 ID 全局唯一，且不与基础条目冲突；
- 每条新增物品都有**获取方式**（目录/商店/配方/宠物选择），否则报告“不可获得”；
- 新贴图资产进 `ContentHashes`，并做一次 `pack-check.py` 回包；
- 受控对比：只多该多的条目/资产，原版条目逐字节不变；
- 报告必须区分 `exact` / `approximate` / `lost`，不能把“新增物品”说成“原物品换皮”。

---

## 9. 净结论

用户的方向是对的：**原版 1.6 本身就是一套可追加的数据库**。

- **Json Assets** 的 12 类内容里，11 类能直接写成原版 `Data/*` 行（Forge 除外）；
- **Alternative Textures** 的 1399 个变体，能变成 1399 个新增家具/可制作物/作物/墙纸；
- **Solid Foundations** 的 21 个建筑/145 个皮肤，能拆成独立建筑；
- **Custom Companions** 的 216 个同伴，能落成宠物类型/品种（AI 降级）；
- **CCCB/MFM** 有干净的 `Data/Bundles` / `Data/Mail` 落点。

上一版「不能烘」的结论只适用于**运行时交互/UI/状态**；对**内容资产**，
正确的做法就是用户说的：**把原版 XNB 当数据库，往表里加行。**
