# 隐式约定：名字即行为 与 索引的分组聚合

日期：2026-10-06
依据：`D:\sdv_src`（1.6 全量反编译，约 800 个 .cs）、`D:\sdv_gd`、`D:\sdv_tree`、两语料实测

> 用户判断：「**这游戏特别喜欢用群论来存储数据，而不是规整的结构**。」
> 本文件是那套「不规整的结构」的目录。

---

## 0. 一句话

语义不在结构化字段里，而在**名字的子串**、**索引的取模与除法**、**优先链的顺序**里。
**A 类（名字即行为）找到 6 个家族约 60 处；B 类（索引分组）找到 20+ 处 Z/N。**
其中**真正能用来「把 N 个变体塞进连续格子」的原生机制只有 6 个**。

---

## 1. ★ 核心机制：`name.Contains("Seasonal")` → 索引按季节取陪集

```csharp
// StardewValley/Object.cs:1940-1944
if (bigCraftable.Value && name.Contains("Seasonal"))
    base.ParentSheetIndex = ParentSheetIndex - ParentSheetIndex % 4 + location.GetSeasonIndex();
```

（另在 `Object.cs:1428/2918/7019/7083` 有同构调用。）

| 项 | 内容 |
|---|---|
| 触发 | **BigCraftable 且名字含子串 `Seasonal`**（大小写敏感）|
| 数学 | `index - index % 4` = **块的起点**（Z/4Z 的陪集），`+ seasonIndex` 取组内 |
| 作用对象 | `Data/BigCraftables` 的 `Texture` + `SpriteIndex` |
| 复位 | `ResetParentSheetIndex()`（`Item.cs:382-385`）= 取回 `SpriteIndex` 原值 |

> ⚠️ **它不分室内外**——`location.GetSeasonIndex()` 直接取该地点季节。
> 所以「室内不换图」这种语义**表达不了**。

---

## 2. B 类：**能「把 N 个变体塞进连续格子」的原生机制，只有这 6 个**

| # | 机制 | 公式 | N | 选择子 | 源码 |
|---|---|---|---|---|---|
| **B-1** | **BigCraftable 季节** | `idx - idx%4 + seasonIndex` | **4** | 季节 | `Object.cs:1940` |
| **B-2** | **建筑季节偏移** | `source.X += SeasonOffset.X * seasonIndex` | `SeasonOffset` 步长 | 季节 | `Building.cs:1785`；字段 `Data/Buildings.<id>.SeasonOffset` |
| **B-3** | **MapSeat 季节** | `value.X += value.Width * seasonIndex` | 座位图集宽 | 季节 | `MapSeat.cs:303` |
| **B-4** | **Bush** | `sourceRect.X = season*16*2 + tileSheetOffset*16` | 2 / 16 / 3 | 季节·结果·树龄 | `Bush.cs:130-192` |
| **B-5** | **FruitTree** | `(12 + season*3)*16` | **3** | 季节 | `FruitTree.cs:1014-1043` |
| **B-6** | **邻接掩码**（4 邻位）| `sprite = drawGuide[neighborMask & 0xF]` | **16** | 四邻连通位 | `HoeDirt.cs:1381`、`Flooring.cs:395`、`Fence.cs:268` |

**其中只有 4 个是「游戏自己按维度选」**：季节（B-1/B-2/B-3）、季节（B-4/B-5 但硬编码在 draw 里）、邻接掩码（B-6，但表是硬编码字典，**不可用于转换**）。

**其余 20+ 处 Z/N 的选择子是运行时时序**（机器帧偏移、品质星、裤子/头发/衬衫的图集分块…），转换价值低。

---

## 3. A 类：名字即行为（6 个家族）

### A-1 物品名子串 → 行为（**约 30 处，改名字会改玩法**）

| 触发 | 行为 | 源码 |
|---|---|---|
| `name.Contains("Fence")` | `scale = (10, 0)` 摆成长条 | `Object.cs:916` |
| `name.Contains("SupplyCrate")` | 可重击破坏 | `Object.cs:1192` |
| `name.Contains("GreenRainWeeds")` | 10% 掉 Moss | `Object.cs:1465` |
| **`Name.ContainsIgnoreCase("weeds")` + `Category==-999`** | **`IsWeeds()` → 可被镰刀割** | `Object.cs:6116` |
| `Name == "Stone"` + `Category==-999` | `IsBreakableStone()` | `Object.cs:6081` |
| `Name.Contains("arecrow")` | `IsScarecrow()` | `Object.cs:4703` |
| `Name.StartsWith("Deluxe")` | 稻草人半径 **17**（否则 9）| `Object.cs:4712` |
| ContextTag `crow_scare_radius_N` | 半径 = **N（名字里带数字）** | `Object.cs:4712-4720` |
| `ItemId.StartsWith("SkillBook_")` | **ID 最后一个字符**当技能号加 250 经验 | `Object.cs:3388` |
| `Name.Contains("Mask")` | 遮住玩家脸 | `Hat.cs:87` |
| `name.Contains("TV")` | 摆放限制 = 0 | `Furniture.cs:125` |
| `Name.ContainsIgnoreCase("tea")` | 杯子画在桌下 | `Furniture.cs:317` |
| `!name.Contains("End Table")` | 是否算桌子 → 旋转与包围盒 | `Furniture.cs:1176` |
| **`InternalName` → context tag `item_<name>`** | **GSQ 可 `ITEM_CONTEXT_TAG` 命中** | `ItemContextTagManager.cs:102` |

> **⚠️ 转换时必须过一遍这份名单**：任何要改 `Data/Objects.Name` / `Data/BigCraftables.Name` /
> `Data/Monsters` 键 / `Data/Furniture` 第 2 字段的地方，**都会改玩法**。

### A-2 家具「类型名」→ 精灵尺寸

```csharp
// Furniture.cs:1722-1748
public static int getTypeNumberFromName(string typeName) {
    if (typeName.StartsWithIgnoreCase("bed")) return 15;
    return typeName.ToLower() switch { "chair"=>0, "bench"=>1, "couch"=>2, … _=>9 };
}
```

**触发**：`Data/Furniture` 行的**第 2 字段**（类型串）。
**行为**：决定 `spriteWidth/spriteHeight`（`Furniture.cs:1560-1634`，15 个 case）——
**同一个 `SpriteIndex`，类型名不同则取的像素框大小不同**。
**关键**：`Furniture.cs:244-251`——**只有当第 3 字段 == `"-1"` 时才用类型名推断**，否则用显式 `w h`。

### A-3 怪物名（= `Data/Monsters` 的键）→ 硬编码行为

`"Royal Serpent"`（段数/血量）、`"Wilderness Golem"`（掉落）、`"Tiger Slime"`（整套外观）、
`"Putrid Ghost"`、`"Magma Sparker"`… **名字就是分支键**。

### A-4 资产名后缀/前缀 → 换贴图

`_Beach`、`_dangerous`、`_dark`、`_animals`、`_Winter`、`_<season>`、
`spring_/summer_/fall_/winter_` 前缀、`x_WallsAndFloors_` 前缀。

**排查结论**：扫了全部 `DoesAssetExist`（28 处）、`+ "_"` 拼接、`StartsWith/EndsWith` 字面量（114 处），
**没有第 7 种后缀约定**。

### A-5 ★ **反射：字符串就是类名**

| 数据字段 | 源码 |
|---|---|
| `Data/Tools.<id>.ClassName` | `ToolDataDefinition.cs:125` `GetType("StardewValley.Tools." + toolData.ClassName)` |
| `Data/Trinkets.<id>.TrinketEffectClass` | `Trinket.cs:196` |
| `Data/Buildings.<id>.BuildingType` | `Building.cs:604` |
| `Data/Buildings.<id>.IndoorMapType` | `Building.cs:1884` |
| `Data/Machines.<id>.InteractMethod` | `Object.cs:4670` |

### A-6 键名编码条件

日程优先链（`NPC.cs:5762-5899`，**15 级**）、`<key>_yN` 年轮（`Event.cs:4418`）、
邮件 `<season>_<day>_<year>`、`Data/ChairTiles` 的 `<tilesheet>/<x>/<y>`。

---

## 4. 量化：**同一需求，两种语料走了三条不同的路**

| 语料 | 季节换图怎么做 |
|---|---|
| **SVE** | **52 组 × 4 条 = 208 条条件 `EditImage` 手工模拟 B-1** |
| **RSV** | ① `{{season}}` token 驱动 `FromFile`；② **逗号拼接四季 Target**；③ `Data/Characters.Appearance[].Season` |

| 指标 | RSV | SVE | 整个 Mods |
|---|---|---|---|
| `EditImage` 总数 | 43 | 485 | 730 |
| 带 `When` | 16（37%）| 459（94.6%）| 596 |
| **`When` 第一键 = `Season`** | **0** | **264** | 266 |
| **「手工模拟季节分块」组数** | **0** | **52 组（208 条）** | 52 组 |

**SVE 那 208 条占它 `EditImage` 的 43%、整个语料 B `EditImage` 的 28.5%。**

**实测复核**（可复现）：52 个目标**全是 BigCraftable**，`SpriteIndex` **全是 0**（天然 4 的倍数），
4 帧在 `SeasonalJA.png` 同一行相邻（X=0/16/32/48，Y=192）——**正是 B-1 要的形状**。

> **只按字段名搜源码，会同时错过这三种实现。**

---

## 5. 可直接落地的检查清单

1. `Data/BigCraftables` 条目若同一 `Texture` 被 ≥2 条 `When.Season` 的 `EditImage` 命中 → **触发 B-1 转换**
2. `Data/Buildings` → 检查能否用 `SeasonOffset`（B-2）
3. `Maps/*` tilesheet 名带 `spring_/summer_/fall_/winter_` → **季节维免费**
4. `WorldMap` → `<asset>_<season>` 后缀免费（`MapRegion.cs:187`）
5. NPC 立绘/行走图的季节或地点变体 → `Data/Characters.<NPC>.Appearance[]`
6. 野树贴图季节变体 → `Data/WildTrees.<id>.Textures[].Season`
7. **`Mods/<自建资产>` 的季节变体 → 没有通用落点**（除非被某张有季节维的 `Data/*` 表引用）
8. **改名即改行为**：任何要改 `Name` 的地方都要过 §3 A-1 的名单

---

## 6. 我查过但没有的（搜索面）

| 搜了什么 | 结果 |
|---|---|
| `Data/Objects`（非 bigCraftable）的季节精灵 | **没有**——`Seasonal` 分支 5 处**全带** `bigCraftable.Value` |
| `summerobjects`/`fallobjects`/`winterobjects` | **不存在**，原版树只有 `Maps/springobjects` |
| `Data/Crops` 的季节精灵 | **没有**——季节只影响能不能种 |
| 「按品质换主贴图」的机制 | **没有**（只有 UI 星标）|
| 除 `%4` 外按季节取模 | **没有**——全库 `seasonIndex` 用于绘制处共 33 行，逐行看过 |
| 第 7 种资产名后缀 | **没有** |

---

## 7. 方法学备注

- **`D:\sdv_gd` 里的标识符被匿名化**（`SeasonOffset`→`Seasonn`、`IncubatorParentSheetOffset`→`IncubatorParentSheetn`）。
  **但 XML 注释完整**，且 **`D:\sdv_src` 的使用侧保留了真字段名**。
  → **字段名以 `sdv_src` 使用侧为准，字段语义以 `sdv_gd` 注释为准。**
- **源码搜索无果时，去看素材的实际布局**——「四季在连续 4 格」这种排布，
  只有在游戏按固定步长偏移时才讲得通。

---

## 8. 家具与服装：**ID 就是格子号**（补，2026-10-06）

### 8.1 ⚠️ 纠正三处我先前的错误

| 我写过的 | 实际 |
|---|---|
| `Data/Furniture` 值含 `sourceRect`/`drawOffset` | **1.6 里没有这两个字段**（`FurnitureDataDefinition.cs` 全文核实）|
| `Data/ChairTiles` 是家具图集的坐标表 | 它是**地图座位表**（`GameLocation.cs:1851-1905` + `MapSeat.FromData`），与家具图集无关 |
| 「像俄罗斯方块一样堆叠」 | ✅ **成立，且已实证** |

### 8.2 ★ 核心：**数字 ID = 精灵格子索引**

```csharp
// FurnitureDataDefinition.cs:92-101  GetSpriteIndex
//   第 8 字段 > -1 → 用它；否则 int.TryParse(条目 ID)；再否则 -1
```

**家具 / 帽子 / 靴的数字 ID 就是图集里的格子号。** 家具 ID `0,3,6,9…` 稀疏
**不是随意编号，是因为各块宽不同、必须错开**。

**像素换算**（`Furniture.cs:1638-1642`）：

```csharp
new Rectangle(itemData.SpriteIndex * 16 % texture.Width,
              itemData.SpriteIndex * 16 / texture.Width * 16, spriteWidth * 16, spriteHeight * 16)
```

### 8.3 「俄罗斯方块」的实证

把 347 个（数字 ID、默认贴图的）家具按上面公式放回 `furniture.png`（512×1488 = 32×93 格）：

```
assigned blocks  = 347
cells covered>=1 = 1499 / 2976
OVERLAP cells    = 0        ← 零重叠
```

**零重叠 = 手工装箱。** 像素级复核：椅子起点 `x = 0, 48, 96…`（周期 48px = 3 格），与 ID `0,3,6` 吻合。

**块尺寸来自 `Data/Furniture` 第 2 字段**（`宽 高`，单位 16px 格）；为 `-1` 时按**类型名**取默认
（`Furniture.cs:1560-1636`：chair 1×2、bench 2×2、couch 3×2、long table 5×3、fireplace 2×5…）。

**游戏不做任何自动排版**——它假定「你给的格子号 + 尺寸」那块矩形**不与人重叠**，直接裁。
**排版是作者手工完成的。**

### 8.4 ★ 多状态家具的第 2 帧在**右邻**

```csharp
// Furniture.cs:1875 / 1971
sourceRect.X += sourceRect.Width * sourceIndexOffset;
```

灯（开/关）、窗（雨/晴）、TV、鱼缸用这个。**mod 必须预留 2× 宽度。**

### 8.5 服装：同一套公式，只是 N 不同

| 类 | 公式 | N |
|---|---|---|
| 衬衫 | `idx*8 % (W/2)` | W/16 = 16 件/行（**贴图左右对半：左衣服、右染料遮罩**）|
| 裤子 | `192*(idx % (W/192))` | W/192 = 10 件/行 |
| 帽子 | `idx*20 % W`（`+direction*20` 选朝向帧）| W/20 = 12 顶/行 |
| **家具** | `idx*16 % W` | W/16 格/行 |
| **BigCraftable 季节** | `idx - idx%4 + seasonIndex` | **4** |

> **前者 `index → (index%N, index/N)` 是拆包；后者 `index → index - index%N + 选择子` 是取陪集。同一套数学。**

**靴不在专用图集**——塞在 `Maps/springobjects` 里。

### 8.6 ★ 给转换器的硬要求

1. **原样搬运 `Data/Furniture` 的全部字段**（尤其第 2/8/9），**字符串 ID 的条目必须补第 8 字段**——
   否则 `int.TryParse(名字)` 得 `-1` → **生成报错物**。
2. **搬运 mod 自建的图集 PNG**，`texture` 字段映射成转换后的资产名。
3. **不要把 mod 图集合并进原版 `furniture.png`**——会破坏手工装箱，且干扰多帧的右邻约定。
4. 服装贴图必须满足取模约定（衬衫左右对半、帽子 4 朝向帧 × 12 列、靴在 `springobjects`）。

### 8.7 量化

| | RSV | 语料 B |
|---|---|---|
| 新增家具/服装条目 | **50** | **~104**（SVE 98 家具 + 2 靴 + 3 座位 + 1）|
| **改原版家具/服装数据** | **0** | **0** |
| 自建图集并手工排 spriteIndex | RSV / SVE / Cornucopia **都这么做**（实测零重叠）| |

**两语料没有一个 mod 改原版家具/服装数据**——所以这块是**「原样搬运」而非「合并」，风险低**。

---

## 9. 帧分布的约定（补，2026-10-06）

### 9.1 ⚠️ 纠正三处

| 我写过的 | 实际 |
|---|---|
| `Portraits/Abigail.png` = 8×10 个 16×32 | **是 2 列 × 5 行个 64×64**（`DialogueBox.cs:697` 用 64×64 切）。**我那个切法切出来的东西游戏从不读** |
| `Data/animationDescriptions` 的 `A/B/C` 是三个**朝向** | **是 `intro / 主循环 / outro`**（`NPC.cs:4668-4670`）。**朝向在这条链上不出现** |
| 列数写死 4 | NPC 是 `texW/16` **现算**；只有 Farmer 写死 `96`（6 列）|

### 9.2 帧号 → 矩形的公式（动态）

```csharp
// AnimatedSprite.cs:618-621
new Rectangle(frame * spriteWidth % textureWidth,
              frame * spriteWidth / textureWidth * spriteHeight, spriteWidth, spriteHeight)
// 列数 = textureWidth / spriteWidth，现算，不写死
```

### 9.3 ★ 生态的做法：**整图替换，不是按帧拼**

| 指标 | 语料 A（RSV）| 语料 B |
|---|---|---|
| `EditImage` → `Characters/`/`Portraits/` | 35 | 136 |
| **带 `FromArea` 的** | **0** | **0** |
| 带 `ToArea` 的 | 0 | 2 |

**`FromArea` 命中率 0/136** —— **不需要实现像素级帧编辑。**

**真正的落点**：`Data/Characters.<NPC>.Appearance[]`
（整图 `Sprite`/`Portrait` + GSQ `Condition` + `Season` + `Indoors`/`Outdoors` + `Precedence`/`Weight`）。

**SVE 已经在用这个原生方案**（`Characters/Alesia_Spring` 等由 `Appearance[].Condition: "SEASON spring"` 挂上）——
我们此前判它「无落点」是**假阳性**。

**真正无落点的**：只有**非 NPC 资产**（`Characters/Monsters/Royal Serpent`，语料 A 仅 1 条）。

### 9.4 三个新发现的隐式约定（建议单开任务）

1. **`TemporaryAnimatedSprite` 的跨行续帧**（`TemporaryAnimatedSprite.cs:1867-1886`）：
   `sourceRect.X >= Texture.Width` 就 `Y += Height` —— **帧在贴图里跨行续接**，极易漏。
2. **`Data/Festivals` 的演员表**（`Event.cs:3296-3299`）：
   ```csharp
   int key = tileIndex / 4;              // ★ 演员号
   int facingDirection = tileIndex % 4;  // ★ 朝向
   ```
   `Maps/characterSheet.png`（128×288 = 8×9）里**每个演员占连续 4 格**。
   **这可能是全游戏最干净的「索引分组聚合」样本。**
3. **`Data/FarmAnimals` 的尺寸校验**（`FarmAnimal.cs:2403-2414`）：
   引擎自己写出约定 —— **宽度必须恰好 `4 × SpriteWidth`**，高度 ≥ (5~7) × SpriteHeight。
