# 端到端回包验证（产出树 → XNB）

日期：2026-10-06
工具：`D:\XnbConverter\out_publish\XnbConverter.Cli.exe`（**只读调用，不改它**）
目的：验证「转化出的资源树能不能真的回包成 XNB」—— 这是本项目**产出可用性**的唯一硬判据。

---

## 0. 怎么跑（踩过的坑）

```
XnbConverter.Cli.exe "pack -c -i <解包目录> -o <打包目录>"
```

⚠ **输出目录必须事先存在**。`Program.Parse` 末尾有一段「输入输出在调用时都必须真实存在」：

```csharp
if (!File.Exists(outputPath) && !Directory.Exists(outputPath))
    return Error.Program_7 + OptionHelp("o");
```

不建目录时报的是「**错误命令-o [必填]输出的文件夹**」——**完全看不出是这条要求**。
（另外 `Parse` 按 `" -"`（空格+减号）切分选项，所以路径里不能出现「空格+减号」。）

---

## 1. 结果

| 范围 | 成功 | 失败 | 用时 |
|---|---|---|---|
| 整树（社区中心扩建，覆盖 tmx→tbin） | **3555** | **0** | 873 s |
| SVE 的四季产物子树（52 张 `64×32` 图 + 自带 `.config`） | **395** | **0** | 14 s |
| SVE 的 `Data/` | 901 | **4** | 7 s |

**四季发射器、tmx→tbin、键名归一化这些新写的产物全部回包成功。**

---

## 2. 那 4 条失败

逐资产核对产物目录确认：没有产出 `.xnb` 的正好是这四个。

| 资产 | 错误 | 归因 |
|---|---|---|
| `Data/Buildings` | `Error converting value "28, 116" to type 'Microsoft.Xna.Framework.Vector2'` | mod 数据 |
| `Data/FarmAnimals` | 同上（`"1.5, 1.75"`） | mod 数据 |
| `Data/Fences` | 同上（`"0, -20"`） | mod 数据 |
| `Data/WorldMap` | `Path 'Valley.MapAreas[7].ToolTips.Mines'` | mod 数据 |

### 2.1 前三条：`Vector2` 被写成字符串

SVE 的 `Data/Buildings` 里 `DrawLayers[].DrawPosition` 写成了 `"28, 116"`；
**原版写的是对象**（`{"X": 32.0, "Y": 96.0}`，实测 `Coop`/`Barn`）。

**⚠ 未确证**：SDV 自己**是否容忍**这种字符串写法，**没有证据**
（`DataLoader.cs` 里搜不到任何 `JsonConverter`，Newtonsoft 默认也不能从字符串转 `Vector2`）。
SVE 在游戏里能跑**不能**作为证据 —— `DrawPosition` 只是个绘制偏移，读不出来也只是建筑略微错位、没人会注意。
**因此不做「规范化成对象」的改写**（没确证就不猜）。

### 2.2 第四条：同一对象里仅大小写不同的键

`MapAreas[7]` 上同时有 `Tooltips`（模型字段）与 `ToolTips`（mod 把大小写写错）。
反序列化对属性名**大小写不敏感** ⇒ 两键撞到同一属性 ⇒ 类型不符（数组 vs 对象）直接抛异常。

**已加自检覆盖**：`Validating/KeyCaseConflictAudit.cs`，报告里逐条列出。
它排除「**键即数据**」的资产（`Data/Events/*` 是 `Map<string,string>`，
两个只差大小写的**事件键是合法的两个条目**），判据复用 `VariantNormalizer.AssetTypeOf`。

---

## 3. 各道自检的覆盖边界（**如实记录**）

| 失效类型 | 哪道自检覆盖 | 实测 |
|---|---|---|
| 容器形状不符（数组↔对象） | `GameDataValidator` | ✓（**已能下钻**，见 §7） |
| 引用不存在的贴图 / 精灵格越界 | `SpriteSheetValidator` | — |
| 同一模型对象内仅大小写不同的键 | `KeyCaseConflictAudit` | ✓ 抓到 `Data/WorldMap` |
| `Vector2`/`Vector3`/`Rectangle` 写成字符串（**一层**字段） | `GameDataValidator` 的 `FieldKind.Struct` | ✓ 抓到 `Data/FarmAnimals` 15 处、`Data/Fences` 2 处 |
| `Vector2` 写成字符串（**嵌套层**） | `GameDataValidator` 的 `FieldKind.Struct`（**§7 补上**） | ✓ 抓到 `Data/Buildings` 6 处 `DrawLayers[].DrawPosition` |

**这 4 个打不出包的资产，两道自检合起来全部抓到**（`Buildings` / `FarmAnimals` / `Fences` / `WorldMap`）。

核实的依据（不是推断）：
- `Data/Buildings` 的**一层** Struct 字段（`AnimalDoor`/`DrawOffset`/`UpgradeSignTile`）**0 处违规** ——
  自检不报它是**对的**；
- 它的 **6 处** `DrawPosition` 都在 `DrawLayers[]` **元素内** ⇒ 曾够不着（§7 已补）。

⚠ **抓到不等于修**：这些都是 mod 自带数据，且「SDV 是否容忍」**未确证**（§2.1），
所以自检的产出**只进报告**，不改产物。

---

## 4. 结论

- 产出树**能被 XNB 打包器处理**，本次两处范围**零失败**。
- 有 4 个资产打不出 `.xnb`，**全部是 mod 自带数据**，不是本工具写坏的。
- 「回包」是唯一能抓到嵌套层标量类型问题的判据，但它一次要跑十几分钟（整树），
  **不该集成进每次转换** —— 而是作为**发布前的抽查**。

---

## 5. 补记：`Struct` 自检的实测覆盖（2026-10-06，同参数）

跑同一棵 SVE 产出树，`gameData` 自检的 `shapeMismatch` **3 → 17**：

| 资产 | 处数 | 字段 | 期望 → 实际 |
|---|---|---|---|
| `Data/FarmAnimals` | 15 | `BabyLeftRightPetHitboxTileSize` / `BabyUpDownPetHitboxTileSize` / `LeftRightPetHitboxTileSize` / `UpDownPetHitboxTileSize`（各 3 条）+ `StatToIncrementOnProduce`（Array） | `Struct` → `scalar` |
| `Data/Fences` | 2 | `HeldObjectDrawOffset` | `Struct` → `scalar` |

**与回包失败列表对照**：`FarmAnimals` ✓、`Fences` ✓ 被它抓到，
`WorldMap` 被 `KeyCaseConflictAudit` 抓到 —— **4 个里覆盖到 3 个**。

⚠ 上一版本文档写的「SVE 那 3 处都在嵌套层、抓不到」**是错的**：
`FarmAnimals`/`Fences` 的那些字段**就在一层**，抓得到；只有 `Data/Buildings` 的在嵌套层。

## 6. 可复用的抽查脚本

`tools/xnb-roundtrip-check.py <产出树目录>` —— 打一次包，并**逐资产**列出没有产出 `.xnb` 的。

★ 它**不建 junction**。上一轮我为路径在 `D:\XnbConverter` 下建过 junction（那是往受保护目录里写东西，
而且 `rm -rf` 一个 junction 会**跟着删掉目标内容**）。正确做法是**绝对路径**：
打包器的依赖按 **exe 所在目录**解析（.NET app base），与 cwd 无关，所以不需要把树搬过去。

脚本已实测：对 `D:\sdv_e2e_sve\Data` 报 `成功 901 / 失败 4`，并列出
`Buildings.xnb` / `FarmAnimals.xnb` / `Fences.xnb` / `WorldMap.xnb` 四个。

---

## 7. 补记：类型图，把嵌套层也覆盖上（2026-10-06）

### 7.1 改了什么

`GameDataSchema` 原本是**一张**「资产 → 一层字段」的表，**下钻不了**。
现在拆成**两张**（都由 `tools/gen-game-data-schema.py` 重新生成）：

| 表 | 内容 | 来源 |
|---|---|---|
| `AssetElement` | 资产名 → **元素类名** | `DataLoader.cs` 的 `Load<T>` 泛型实参 |
| `Types` | 类名 → 字段 → `FieldRef(容器种类, **元素类名**)` | `D:\sdv_gd` 的模型类 |

`GameDataValidator` 于是能沿类型图逐层下钻（上限 4 层，**有限深是防环用的**，
不是「只查这么深有意义」—— 生成器按 6 层收类，宽一层）。
只收「从资产元素 6 层内够得着」的类：**32 个资产 / 67 个类**（原先 32 个资产）。

### 7.2 受控对比（同一棵树，只改校验器）

`D:\sdv_e2e_sve`：`gameData.shapeMismatch` **17 → 23**。

多出来的 6 处**正好是** `Data/Buildings` 的 `DrawLayers[].DrawPosition`：

```
Data/Buildings  …_PremiumCoop.DrawLayers[0]/DrawPosition  Struct <- scalar   （Coop/Barn 各 3 处）
```

⇒ **那 4 个打不出包的资产，覆盖从 3/4 变成 4/4。**

### 7.3 假阳性核对（不是推断）

- 同一棵树：新报的 23 处**全部**是已知的三个族（`Buildings` 6 / `FarmAnimals` 15 / `Fences` 2），**没有一条新的意外**；
- 语料 B 的 **31 棵产出树**逐棵跑：29 棵 `0 处`，2 棵非零（见下）；
- 原版语料（`D:\e2e_out`，社区中心扩建）：`0 处`。

### 7.4 ★ 自检 + 回包联手抓到的一个**真 bug**：`TargetField` 建错了容器

**症状**：`Data/Characters.json` 里 **19 个 NPC** 的 `Appearance` 是**对象**（`{"<Id>": {...}}`），
而模型 `CharacterData.Appearance` 是 `List<CharacterAppearanceData>`。

**来源**：SVE 季节性服装的写法

```jsonc
{ "Action": "EditData", "Target": "Data/Characters",
  "TargetField": [ "Alesia", "Appearance" ],     // 落到子字段
  "Entries": { "{{ModId}}.AlesiaSpring": {...} } // 键 = Id
}
```

`Entries` 键当 `Id` 用、进去变成**列表元素**是 CP 的列表编辑语义。
**原版 NPC（`Marlon`/`Abigail`，字段已存在且是数组）走对**；
**非原版 NPC（`Alesia`…）字段不存在 ⇒ 我们的 `Descend` 一律 `new JObject()`**，于是建成了对象。

**两条判据都指向它**：
- 本工具的 `GameDataValidator`：`shapeMismatch = 19`，全是 `Appearance 期望 Array 实际 object`；
- **回包**（硬判据）：

  ```
  BEFORE  pack Data/Characters.json → 失败 1：
    Cannot deserialize the current JSON object (e.g. {"name":"value"}) into type
    'System.Collections.Generic.List`1[CharacterAppearanceData]' because the type requires a JSON array
    Path 'Alesia.Appearance['{{ModId}}.AlesiaSpring']'
  AFTER   → 成功 1，产出 Characters.xnb
  ```

**修法**：`TargetField` 的每一段该建什么容器，**按类型图推**
（`GameDataSchema.AssetElement` + `Types`，见 §7 的作业）—— 字段是 `Array` 就建 `JArray`，
其余仍建 `JObject`。模型不认得该资产时**保持原行为**（如实留缺口，不拿猜测覆盖）。

**受控对比**（同一个包、同参数）：

| | 修前 | 修后 |
|---|---|---|
| `total` / `convertible` / `blocked` / `written` / `writeFailed` | 205 / 108 / 97 / 55 / 1 | **完全相同** |
| `gameData.shapeMismatch` | **19** | **0** |

**影响面量化**（语料 B 全量扫描 `TargetField` + `Entries` 的落点）：

| 落点 | 条数 |
|---|---|
| **数组字段、且该容器在原版里不存在** ⇒ 老代码必然建成对象 | **506** |
| 数组字段、容器已存在 ⇒ 老代码本来就对 | 141 |

⚠ 上面「容器不存在」是拿**原版基础资产**判的；合并时更早的补丁可能已经把容器建出来了，
所以 **506 是上界**，不是确数。但另一侧只有 141，量级是清楚的。

### 7.4.1 全语料受控对比（32 个 CP 包，同参数）

| 检查 | 修前 | 修后 |
|---|---|---|
| 补丁 / 可合并 / 已写入 / 写入失败 | 6743 / 5537 / 3558 / 89 | **逐项相同** |
| 判定分布（Converted / NeedsReview / LocalEdit / …） | — | **逐项相同** |
| `gameData.shapeMismatch` | 53 | **32** |
| `conditionPlacement.misplaced` | 124 | **1** |
| `outputShape.arrayBecameObject` | 21 | **0** |
| `spriteSheet.missingTexture` / `outOfBounds` | 0 / 0 | 0 / 0 |
| `keyCase.fatalConflicts` | 2 | 2 |

**★ 一个 bug 同时造成了三处报错**（修完一起消失）：
`Appearance` 被写成对象 ⇒ ① 形状不符（`Appearance 期望 Array 实际 object`）；
② 路径多出一层 `*`，与原版 `/*/Appearance[]/Condition` 对不上，`ConditionPlacementAudit` 判成「落点游戏不读」
（**那 121 条本是假阳性**）；③ `OutputShapeAudit` 的 `arrayBecameObject` 21 条。
**只修「形状」这一处，三处一起清零** —— 印证了「一个根因、多个自检各自报警」。

剩下那 **1** 条 `misplaced` 是 `Data/WorldMap` 的 `MapAreas[].KnownCondition`：
**mod 自己把 `KnownCondition` 写错了层级**（正确位置在 `Tooltips[]/KnownCondition`），
属已知的 Task 26，不是本工具写的。

剩下 32 条 `shapeMismatch` **全部是 `Struct ← scalar`**（mod 把 `Vector2` 写成字符串）+
`FarmAnimals.StatToIncrementOnProduce`（`Array ← scalar`），**没有一条是本工具写出来的形状错**。

### 7.5 下钻**另抓到**一处新的（不在 SVE 里）

`内容拓展---聚宝盆_制作`（`Data/Machines`）**4 处**，在**第 3 层**：

```
(BC)Cornucopia_Alembic.LoadEffects[0].TemporarySprites[0]/PositionOffset  Struct <- scalar
```

实际写的是 `"PositionOffset": "0, -32"`（`MachineData → LoadEffects[] → TemporarySprites[] →
PositionOffset: Vector2`，链上每一环都取自 `D:\sdv_gd` 的模型类）。
`TemporaryAnimatedSprite` 这一族在总账里本就标着「**极易漏**」—— 下钻正好够到它。

⚠ 与 §2.1 同一个保留：**SDV 是否容忍这种写法未确证**，所以只报不改。


## 8. 补记（2026-10-07）：**小树回包**取代整树回包做定点核对

整树回包一次二十多分钟，而它真正要回答的问题往往只是「**这一批新写出来的文件**打不打得了」。
本轮起改用**小树**：只把新产生的文件（连 `.config`）拷进一个临时目录再 `pack`。

实测（Task 66/67 的产物）：

```
D:\tmp\mini_tree\Maps\spring_MovieTheater_TileSheet.{png,config}
D:\tmp\mini_tree\Maps\winter_MovieTheater_TileSheet.{png,config}
D:\tmp\mini_tree\Data\Locations.{json,config}
→ 成功: 3  失败: 0（3.4 秒）
```

顺带核了两件事：
1. 季节图集那四份 `.config` 与**原版图集**的 `.config` **逐字节相同**（sha1 `623eb743e8`）——
   季节变换靠的是文件名前缀（`GameLocation.GetSeasonalTilesheetName`），
   `.config` 该是 Texture2D 那一份，实测正确；
2. 四份 PNG **两两不同**（大小 29408~29462 B），确实是四个季节各自的图，不是同一张复制四遍。

## 9. 把「定点回包」固化成工具：`tools/pack-check.py`

**做法**：拿**输入树逐文件比**（先比大小、再比 sha1）挑出产出里**新增/改过**的文件，
连同名 `.config` 拷进小树再 `pack`。
（不能只靠报告的 `target`：`.config` 边车、发射器直接落盘的 `spring_<图集>.png` 都不在里面。）

**实测**（两个包，产出树保留着跑的）：

| 包 | 产出相对输入 | 小树 | 回包结果 | 用时 |
|---|---:|---:|---|---:|
| 人物对话---身临其境 谢恩 | 41 个文件 | 77 | **成功 38 / 失败 0** | 1.1 s |
| 大型扩展---SVE 季节性服装 | **575** 个文件 | 579 | **成功 289 / 失败 0** | **52.9 s** |

⇒ **我们写出去的每一个资产都打得了包**（289 个资产 + 286 份 `.config` = 575，正好对上）。
季节性服装那 575 个里就包含发射器直接落盘的四季图集与地图改指 ✓。

**唯一一条「缺 `.config`」是 `ContentHashes`** —— 那是**我们自己更新**的内容哈希清单，
不是给游戏读的资产，不带 `.config` 是对的（脚本照样报出来，不静默）。

**意义**：以后每次转换完都能拿**硬判据**（真的回包一次）收尾，而不用等到发布前跑二十多分钟的整树。
