# 六道自检在 32 包上共报 31 条，逐类查清：没有一条是**我们**写错的

日期：2026-10-09

全语料跑完，把每道自检的非零项汇总（`tools/report-invariants.py` 另核了报告自身的自洽性，
32 份全过）。findings 分布：

```
spriteSheet         27
gameData             3
conditionPlacement   1
```

逐类查清如下。

## 1. `spriteSheet.missingTexture` 24 条 —— 贴图**不在产出树里**

例：`Data/Objects` 的 `Aimon111.AmanitaLover_Red_Mushroom_Caps` 指向
`Mods/Aimon111.AmanitaLover/Objects`。

**查过：全语料 32 个包里，没有任何一个包创建这张图集**
（`grep` 全部报告的 `Action=Load`，0 命中）。⇒ 它由**语料之外**的 mod 提供
（或由那个 mod 的 C# 直接注册资产）。纯 XNB 场景里没有它的 C# ⇒ 图集不存在 ⇒ 物件画不出来。

这是**如实报告**（自检的原话就是「游戏只记一条 Error 日志，物品画不出来（静默）」），
不是我们写错；要救得把那个 mod 也纳入转换。

## 2. `spriteSheet.badSheetWidth` 1 条 —— **mod 自己的 PNG 就不合规**

`Mods/FlashShifter.StardewValleyExpandedCP/Birch Water.png` 宽 15、格宽 16 ⇒ 列数计算会错位。

**核了源文件**：SVE 包里 `assets/Items/Objects/Birch Water.png` **本身就是 15×16**
（同目录的 `Birch Seed.png`/`Birch Syrup.png` 都是 16×16）。⇒ 我们是**逐字节照搬**的，
错在 mod 自己；CP 那边同样会错位。

## 3. `conditionPlacement.misplaced` 1 条 —— **mod 自己的字段**

`Data/WorldMap.json` 路径 `/*/*[]/KnownCondition`：`Valley.MapAreas[]` 里那个
`FlashShifter.StardewValleyExpandedCP_TownMap_EmeraldFarm` 元素上有
`KnownCondition: "LOCATION_ACCESSIBLE Railroad"`。

**核了原文**：SVE 的 `code/Locations/WorldMap.json` 里**就是这么写的** ⇒ 是**复制过来的 mod 数据**，
不是我们写的条件。而 `WorldMapAreaData`（`MapAreas[]` 的元素类）**没有 `KnownCondition` 字段**
（只有 `Tooltips[]` 的 `WorldMapTooltipData` 有）⇒ **游戏会忽略它**，这是 SVE 自己的 bug，
在 CP 下同样被忽略。

⚠ 这条自检**分不清「我们写的」与「照抄的」**，所以它的 finding 要**逐条看归因**再下结论 ——
这条就是唯一一处，且归因是 mod。

## 4. `gameData.shapeMismatch` 3 条 —— 已确证无害

SVE 的 `Data/FarmAnimals.StatToIncrementOnProduce` 写成空串（声明是 `List`）。
实测**打得出来**、往返变 `null`，见 `docs/research/2026-10-08-cp-xnb-value-dialect.md` §7.2。

## 5. 结论

**这一轮没有发现我们自己的产出缺陷。** 31 条里：
24 条是「贴图来自语料外的 mod」、1 条是 mod 自己的坏 PNG、1 条是 mod 自己的无效字段、
3 条是已确证无害的写法差异、2 条是同一类的重复计数。

（口径：本例只有这四类；`checkedAssets` 与 `skippedNoVanilla` 那些计数不是问题项。）

## 6. 追记（2026-10-11 晚复核时补的两条）

### 6.1 `_Beach` 那两条不是缺陷 —— 校验器的措辞已改

按当前产出重数时，`spriteSheet.findings` 里多出两条（SVE 的 `GuntherSilvian`）：

```
Data/Characters  GuntherSilvian  Characters/GuntherSilvian_Beach   Appearance[6].Sprite
Data/Characters  GuntherSilvian  Portraits/GuntherSilvian_Beach    Appearance[6].Portrait
```

**归因：可选变体，既不是我们的、也不是 mod 的缺陷。**
- 源包里 `assets/Characters/GuntherSilvian/` 只有 `_Spring/_Fall/_Winter/_Summer`，**没有 `_Beach`**
  ⇒ 这两个名字**语料里就没人提供**（`_Beach` 是海岛装的约定，通常由**别的**模组补）；
- 游戏对它们是 **`DoesAssetExist` 保护 + 回退**：`NPC.cs:1051-1066` ——
  先试 `Appearance[].Sprite/Portrait`，失败只打一条 `Warn`；`isWearingIslandAttire` 那支再试
  `<基名>_Beach`（`DoesAssetExist`）；最后落到 `TryLoadSprites("Characters/" + getTextureName())`
  ⇒ **NPC 照常画得出来**，只是穿常服/常图。

⇒ 校验器原来那条与"真缺图"共用一句「游戏只会记一条日志然后画不出人（静默）」——**说错了**。
现在按后缀分流：`_Beach` 的说清「可选变体、游戏会回退、**不是缺陷**」；真缺图的改说
「回退到缺省名（`NPC.cs:1020-1072`），缺省名也没图才是真画不出人」。
（与 `tools/audit-texture-refs.py` 里 `_Beach → 变体缺失（可接受）` 同一判据，两处现在一致。）
测试：`Beach变体缺失不当缺陷` / `真缺图措辞说回退`（`SpriteSheetValidatorTests`）。

### 6.2 ⚠ `spriteSheet.findings` **是按包累积的，别跨包相加**

每个包的 `report.json` 里那一段反映的是**那个包跑完时的整棵树**（链式转换：后面的包叠在前面产出上）
⇒ 同一条 finding 会在**后面每一个包**的报告里再出现一次。把 36 个包的
`spriteSheet.badSheetWidth` 相加会得到 **26**，很容易当成"从 1 涨到 26 的回归" ——
**实际全语料只有 3 条 finding**（1 条 SVE 自己的坏 PNG + 上面那 2 条 `_Beach`）。
要数就数**最后一个包**（或对整棵树单独跑一次 `SpriteSheetValidator.Run`），别求和。

### 6.3 ★★ 更要紧的一条：**链式跑法下 `conditionPlacement` / `outputShape` 是"没比"，不是"没问题"**

这两个自检的判据是「**产出** vs **原版那棵树里同一份资产**」的形状对照。而
`tools/convert-mods.py` 的链式跑法为了不每包重拷整棵树，是这么调的
（`convert-mods.py:180-192` 有原话）：

```
--tree <同一棵> --out <同一棵> --append
```

⇒ 自检里的「原版那份」读到的就是**刚写过的产出** ⇒ 形状一模一样 ⇒ **`findings` 恒为 0**。

实测对照（同一份 SVE）：

| 跑法 | `conditionPlacement` |
|---|---|
| 逐包对原版树（`tools/run-packs.py`，`--tree D:\sdv_tree --out <每包一棵>`）| `checked 2267`、**`misplaced 1`** ← 就是本文 §3 那条 |
| 链式（`convert-mods.py`）| `checked 2267`、`misplaced 0`（**假**）|

修法（2026-10-11 晚）：两个自检在**输入根 == 输出根**时整体**跳过**并置
`skippedSameTree: true`（`checkedAssets` 留在 0），报告里打一行
「条件落点自检：**跳过** —— 输入树与产出树是同一棵……」。
⇒ **0 从此不再冒充"干净"**；要看这两项就**逐包对原版树跑**（`run-packs.py`）。
测试：`输入树与产出树同一棵时报跳过`（`ConditionPlacementAuditTests` / `OutputShapeAuditTests`）。

⚠ 连带结论：**链式跑法（`prof*` 那批）报告里的 `conditionPlacement` / `outputShape` 两段一直不可信**
（历史轮次里读到的 0 都算"没比"）；其余几段（`gameData` / `spriteSheet` / `keyCase` /
`unexpandedTokens`）不跟输入树对照 ⇒ 不受影响。
