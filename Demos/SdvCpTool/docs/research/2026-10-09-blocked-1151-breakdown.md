# 1151 条「无法合并」:逐类原因与为它做过的努力

日期：2026-10-09
口径：`Total − Convertible − GroupEmitted`（即 `AnalysisReport.BlockedCount`）。

⚠ **别拿 `summary[Verdict.X]` 当阻塞数**：被集合发射覆盖的 224 条在 `summary` 里仍记**原判定**，
而 `patches[].verdict` 写 `Emitted`。上表是按后者数的 —— 所以这里的 `TargetMissing 262`
对应 `summary` 里的 267（差 5 条是 groupEmitted 的）。

数据源：当前代码全量跑一遍的 `report.json`（32 包）。

---

## 1. 分解

| 条数 | 原因 | verdict |
|---|---|---|
| **551** | 条件化的地图改动在原版没有通道（图块级只有 `ConditionalDoor`，且不适用于此） | `LocalEdit` |
| **262** | 资源树中找不到资产 | `TargetMissing` |
| **194** | 资产是图片 —— 引擎里按条件换图有机制，但本条不属于任何一种 | `NoLandingPoint` |
| **63** | 别的 mod 注册的自定义资产 | `NoLandingPoint` |
| **38** | 该位置不接受 `Condition`（原版在该位置不读条件字段） | `NoLandingPoint` |
| **21** | 定位不到条件落点条目（落点指着**数组本身**） | `NoLandingPoint` |
| 7 | 列表资产，元素里没有可挂条件的字段 | `NoLandingPoint` |
| 4 | Target 含令牌，静态解析不出资产名 | `TargetContainsTokens` |
| 4 | `Update=OnLocationChange`：要求在运行时额外时刻重算 | `NeedsRuntime` |
| 2 | 条件在 GSQ 里没有等价关键字 | `NonTranslatableCondition` |
| 5 | FromFile 含无法静态解析的令牌 | `TargetMissing` |

合计 1151。

---

## 2. 逐类：为什么拒绝 + 为它做了什么

### 551 条件化的地图改动 —— **原版没有通道**

**做了什么**：把游戏源码里**所有**可能承载条件的位置查了一遍 —— 10 个真位点 + 10 个
「看着像其实不是」的澄清（`docs/research/2026-10-06-map-condition-slots.md`）。
结论：地图里只认 **`ConditionalDoor`（图块 `Action`/`TouchAction`）** 与
**`BuildConditions`（地图属性）**，两者都收完整 GSQ —— 但**都改不了图块内容**
（一个只能开门/挡路，一个只能决定"这里能不能盖房子"）。
而这 551 条干的正是**改地形**（加路、加桥、加入口）。⇒ 机制上无处安放。

### 262 找不到资产

**做了什么**：逐条查过。
- **160 条不带条件** —— 真的缺资产，且多数是**别的 mod 提供的**：
  SVE 季节性服装要改 `Maps/VanillaCursors`、野生动物要改 `Maps/Custom_ForestWest`，
  这些都是 **SVE 本体**的资产，只装子包时不存在。CP 自己也作用不了不存在的目标。
- **107 条带条件** —— 试过「目标缺失时**先报条件**」，看着更准；受控对比后**回滚**：
  它会让 `convertible` 虚涨 323（`NeedsReview` 计入 convertible，而 `TargetMissing` 不计），
  且把 `LocalEdit`（"地图没有通道"）这种更根本的理由盖成「条件翻译不成 GSQ」这种更浅的。
  见 `2026-10-09-target-missing-condition-order-negative.md`。

### 194 图片

**做了什么**：**写了一套集合发射器**，不是"没做"——
- `SeasonalSpriteEmitter`（四季贴图）：产出报告里 `patchesCovered=208`、发射 52 组；
- `SeasonalTilesheetEmitter`（图集季节前缀）；
- `AppearanceEmitter`（NPC 立绘/行走图的 `Appearance[]`）。

剩下这 194 条是**三套发射器都 declined** 的：条件不是四季（多为 `LocationName` / 剧情），
例如女巫沼泽那 55 条 `LooseSprites/Cursors` —— 按**地点**换一张**全地点共用**图集的
一块区域，原版没有这个机制。

### 63 别的 mod 的自定义资产

**做了什么**：确认过两件事（`2026-10-08` 那条改动）：
1. 纯 XNB 场景里**没有那个 mod 的 C#** 去读它 —— 写进去也没人认；
2. 更要命的是**类型无从得知**：`.config` 只能从树里拿别的资产凑，而**凑出来的打不了包** ——
   实测 Cornucopia 往 `spacechase0.SpaceCore/ObjectExtensionData` 写对象，
   凑来的描述符说 `Dictionary<int,string>` ⇒ **回包直接失败 12 个资产**。

### 38 该位置不接受 Condition

**做了什么**：判据是**启发式**（它自己或其同级兄弟有 `Condition` 字段），
启发式就可能误杀 ⇒ 逐条去核游戏定义：`D:\sdv_gd\...\LocationData.cs` **逐字段列过**，
`DisplayName` / `ArtifactSpots` / `Fish` / `Forage` / `Music` / `CustomFields` …
**根本没有 `Condition`**（`ObjectData` 同理）。⇒ 判得对。

⚠ 但「判据是启发式」这件事记着：哪天遇到「类型**确实**有 `Condition` 字段、只是原版数据
没人用」的情况会误杀。根治要 `GameDataSchema` 能回答"这个类型有没有 `Condition`"，
而它现在只列**容器**字段。

### 21 数组落点 —— **唯一有方案的一类**

落点指着**数组本身**（如聚宝盆 `…OutputRules.<Id>.Triggers`、`Data/Shops` 的
`AnimalShop.Items`），而数组没有 `Condition`；被改的是数组**元素**。

**做了什么**：对着反编译 CP 源码把语义查清了（`ListKeyValueEditor.cs`）：
`SetEntry` 找不到就 `Data.Add(value)`、**追加不写 `Id`**；`ParseKey` 认 `#<下标>`；
`GetKey` 取元素的 `Id`。⇒ 自己追加的元素没有 `Id`，**只能按 `#下标`**，而下标要在合并后算。
方案、调用点、边界都写在 `2026-10-09-include-gap-is-not-a-gap.md` §4.3.1。

**为什么没做**：量是 **15 条**（不是最初估的 2 条），但要**判定与写入同步改**（本仓库铁律），
边界多（容器是对象时行为不能变）。权衡下来不值得现在动 —— 方案留着了。

### 4 Target 含令牌 / 4 OnLocationChange / 2 无 GSQ 等价 / 5 FromFile 含令牌

- **Target 含令牌**：资产名本身就是模板（`{{…}}`），静态解析不出。
- **`Update=OnLocationChange`**：要求运行时在**额外时刻**重算；写进原生 GSQ 落点的条件
  只在游戏**读取该资产时**求值，表达不了那些时刻。
- **条件无 GSQ 等价**：GSQ 无法返回集合或遍历实体。
- **FromFile 含令牌**：`assets/animals/Weasel_{{season}}.png` 一类 ——
  求值它们要么是运行时的，要么是自定义 mod 令牌。

---

## 3. 结论

**这 1151 条里没有一条是「该做没做」。** 每一类都查到了机制层（不是"没看"），
三份逐档核查文档把它们逐条过了一遍：
`2026-10-09-needsreview-top-buckets.md`、`2026-10-09-write-failed-eight-classified.md`、
`2026-10-09-include-gap-is-not-a-gap.md`。

**下一块能动的骨头只有 21 条那类（数组落点）** —— 方案齐备，判定不做。
