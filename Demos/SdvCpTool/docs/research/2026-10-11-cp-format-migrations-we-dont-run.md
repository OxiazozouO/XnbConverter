# CP 的格式迁移器：声明旧 `Format` 的包会被 CP **改名/改形状**，我们没实现（语料 0 条命中）

日期：2026-10-11（晚）
起因：追着 `.xnb` 那条（`2026-10-11-cp-strips-xnb-from-target.md`）去读 CP 的 `PatchLoader`，
看见加载链上还挂着一个 `Migrator` —— 顺着读下去发现：**包声明的 `Format` 决定了 CP 会不会改写它的补丁**。

---

## 1. CP 的规则（对源码核过）

`ContentPatcher.Framework.Migrations/AggregateMigration.cs:36`：

```csharp
Migrations = migrations.Where((IMigration m) => m.Version.IsNewerThan(version)).ToArray();
```

—— **比包的 `Format` 新的迁移统统会跑**。所以声明 `Format: 1.29.0` 的包会吃下 `1.30` + **`2.0`** + `2.1`…`2.3`。
（`Format` 不在迁移表里的会被直接拒收：`TryMigrateMainContent` 报
`unsupported format version …`。）

## 2. `Migration_2_0` 管什么（`Migration_2_0.cs:2637` 行，两张表）

| 资产名 | 迁移后 | 我们 |
|---|---|---|
| `Data/BigCraftablesInformation` | `Data/BigCraftables`（**改名**）| ✗ |
| `Data/Blueprints` | `Data/Buildings`（改名）| ✗ |
| `Data/NPCDispositions` | `Data/Characters`（改名）| ✗ |
| `Data/ObjectContextTags` | `Data/Objects`（改名）| ✗ |
| `Data/ObjectInformation` | `Data/Objects`（改名）| ✗ |
| `Data/Boots` / `Data/Crops` / `Data/Furniture` / `Data/Locations` / `Data/Weapons` | **名字不变、形状重写**（`RedirectTarget` 返回 `null`）| ✗ |

改名的那些，CP 是 `RedirectTarget` 换名 + `TryApplyEditPatch` / `TryApplyLoadPatch` 把
**1.6 之前"斜杠分隔字符串"** 的条目按 1.6 的模型重写（`MergeIntoNewFormat`）。

⇒ **不实现迁移器的后果**：照字面写会造出一个**游戏根本不读**的资产
（`Data/ObjectInformation.json` 在 1.6 里没人加载），或者写出**形状不对**的条目 ——
两者都是**静默写错**，而作者的意图是改 1.6 的那个资产。

## 3. 本语料量下来：**CP 包 0 条命中**（所以不实现迁移器）

36 个包里有 **4 个**声明旧 `Format`：

| 包 | Format |
|---|---|
| 03 人物对话---更逼真的对话 [CP] | 1.29.0 |
| 16 贴图美化---鱿鱼怪美化 | 1.29.0 |
| 19 人物对话---身临其境的配偶 | 1.19.0 |
| 26 地图资源---社区中心扩建 | 1.25.0 |

它们的补丁**一条都没碰**上面那 10 个资产名（03=35 / 16=2 / 19=122 / 26=14 条，目标是
对话、日程、地图、`UnlockableBundles/Bundles` 这类自定义资产）。

⚠ 别把**现代格式**的包也算进来：全语料打到这 10 个名字上的补丁有 **126 条**，拆开是
**83 条现代格式的 CP 包 + 43 条 FTM 包**（后者是 `Data/Locations`，那是**我们自己**合成的补丁、
`Format` 字段压根不存在）—— 没有一条来自上面那 4 个旧格式包。
83 条里绝大多数是 `Format 2.0/2.0.0/2.3.0` 的包写的 `Data/Locations` / `Data/Crops` / `Data/Weapons` /
`Data/Furniture` / `Data/Boots`：这些名字**在 1.6 里还在**，现代格式的包写它们**是对的**，CP 也不迁移。
⇒ 「打到这些名字上」**不能**当判据，**必须**连包的 `Format` 一起看。

## 4. 做了什么：**只报不修**

新增 `Cp/LegacyFormatMigration.cs`：`Format < 2.0.0` + 目标命中那 10 个名字 ⇒ 判
`NeedsReview` 并在理由里写清「CP 会把它迁移到哪个资产 / 形状为什么会不对 / 我们没实现迁移器 ⇒ 不写」。
判定放在 `PatchJudge.Judge` 的**最前面**（迁移改的是**目标资产名本身**，对后面每一条判据都成立）。
读不到 `Format`（`null`）时**什么都不说**（不知道就不猜 —— FTM 包的合成补丁走这条路）。

**为什么只报不修**：迁移器逐字段上千行（斜杠串 → 1.6 对象模型），而本语料 **0 条命中** ——
换不来任何东西；但**必须看得见**，否则将来遇到就是"静默写错"。

### 4.1 ★ 这道守卫**第一版就误伤过**：FTM 包（**实测踩到，已修 + 加了回归测试**）

FTM 的补丁是**翻译器合成**的，而翻译器把译出来的 CP 形状根**硬写成 `Format: 1.28.0`**
（`FtmPackTranslator.cs:67`，早于 2.0.0），产物落点又**恰好**是 `Data/Locations`
（正在 `Migration_2_0` 的表里）⇒ 守卫把 **4 个 FTM 包的 43 条合成补丁**（26 条觅食 +
其余）判成"CP 要迁移它、我们没实现"，**一条都没写**：

```
产出树 Data/Locations.json：  919,992 B  →  700,094 B
  FarmCave.Forage          12 项 → 0 项
  Town.ArtifactSpots       49 项 → 5 项
```

**根因是我把"包声明的 Format"当成了"CP 会不会迁它"** —— 可 **CP 压根不处理 FTM 包**，
那些补丁是**我们自己**按 1.6 形状合成的，套"旧格式迁移"是认错了人。
修法：`ContentPack.FormatForCpMigration`（FTM 包恒 `null`，其余就是 `FormatVersion`），
判定侧一律用它；`FTM包的合成补丁不套旧格式迁移守卫` 这条测试钉住
（构造一个 FTM 包 → `IsFtmPack=true`、`FormatVersion=1.28.0`、`FormatForCpMigration=null`、
判定不许出现 `Migration_2_0`）。

⚠ **教训要留着**：这条误伤的发现**不是**来自报告对比 —— 报告当时被一段临时脚本的
字符串替换 bug 搞成了"自己跟自己比"（**假绿**：36 包"全同"），是**另一项逐字节树比**
把它兜住的。两根柱子（报告 / 产出树）缺一个，这次就会带着 220 KB 的静默丢失提交。
⇒ 对比脚本已固化成 `tools/compare-runs.py`，**一进来就先 assert 两个根不是同一个**。

## 5. 同参数受控对比

```
A = prof33（**加这道守卫之前**，含上一段的 .xnb 修复）
B = prof36（守卫 + FTM 排除）          —— 两轮都走 convert-mods.py 全量 36 包

报告：python tools/compare-runs.py D:\tmp\prof33 D:\tmp\prof36
      ⇒ 36 包**逐项相同**（0 条补丁不同）
产出树：python tools/compare-trees-by-hash.py D:\tmp\prof33\tree D:\tmp\prof36\tree
      ⇒ 13562 文件 **逐文件相同**（只在 A 0 / 只在 B 0 / 内容不同 0）
单测：**1346 通过 / 0 失败**（+4 条：旧格式包判 NeedsReview 并点出迁移目标、
      现代格式包写同名资产不拦、读不到 Format 不猜、**FTM 合成补丁不套守卫**）
```

⇒ 守卫在本语料上是**空操作**（CP 包 0 条命中），这正是要的结果：它只为"将来遇到"存在。

**顺带做了一次确定性复验**（同一份二进制连跑两轮，`prof34` vs `prof35`）：报告 36/36 相同、
产出树 13562 文件逐文件相同 ⇒ **现在的流水线仍是确定性的**（也反证 prof33→prof34 那 220 KB
的差异**全部**来自那次误伤，不是抖动）。

## 6. 第二道门槛：**令牌的版本门槛**（也做了，语料 0 条命中 + 一次**正面对照**）

`BaseMigration.TryMigrate(ref ILexToken)`（`BaseMigration.cs:32-41`）：

```csharp
if (AddedTokens != null && AddedTokens.Contains(lexTokenToken.Name))
{
    error = GetNounPhraseError("using token " + lexTokenToken.Name);
    return false;          // ← 整份 content.json 被拒
}
```

各 `Migration_*.cs` 里的 `AddedTokens` 就是"这个版本才有的令牌"： `{{Random}}`(1.10)、`{{Query}}`(1.13)、
`{{FromFile}}`(1.14)、`{{I18n}}`(1.18)、`{{Time}}`(1.19)、`{{Merge}}`/`{{Roommate}}`(1.24)、
`{{AbsoluteFilePath}}`/`{{InternalAssetKey}}`(1.25)、**`{{ModId}}`(2.0)**、`{{HasVisitedLocation}}`(2.2) …
⇒ **声明旧 `Format` 的包用了新令牌，CP 把整个文件拒掉**（作者那边这段内容根本不生效）。

**正面对照（这条比"0 条命中"更有说服力）**：全语料 `{{ModId}}` 的使用者 **8 个包、1037 处**，
**全部**声明 `Format ≥ 2.0`（2.0/2.0.0/2.0.2/2.1.0/2.3.0）—— 而 `ModId` 正是 `Migration_2_0` 的新增令牌。
⇒ 这套门槛在真实语料上**是被遵守的**；我们按它报出"用早了"的包，不会是误报。

实现：`LegacyFormatMigration.FindForbiddenTokens`（令牌表照抄各 `Migration_*.cs` 的 `AddedTokens`）
+ 加载期**包级警告**（CP 的失败本来就是整份文件级的，所以不改任何补丁的判定）：
「content.json 用了 `{{X}}`（CP x.y 才有，本包声明 `Format …`）—— CP 会**拒收整份文件**」
（走 `pack.Warnings` → 报告 `notes` 的「解析警告：…」+ 控制台，与字段守卫同一条路 ✓）。

同参数 A/B（`prof36` → `prof37`，只多了这道门槛）：报告 36 包**逐项相同**、
产出树 13562 文件**逐文件相同**、单测 **1348 通过 / 0 失败**（+2 条：旧格式包用新令牌要报、
版本够新时不报）⇒ 与 §5 一样是**空操作**。

## 7. 顺带记一条：CP 还会**拒收**旧包里的过时用法

`Migration_1_25` 之类会**直接报错拒掉**某些用法（例：`AnyPlayer` 玩家类型、`OnTimeChange` 更新频率）
—— 但**只对早于该版本的包**。实测语料里唯一用 `AnyPlayer` 的是 **SVE（`Format 2.3.0`）**，
`HasSeenEvent: AnyPlayer|contains=…`（`Data/events/farm`）—— 现代格式下**合法**，
`PerPlayerValueProvider.cs:142` 仍在处理 `PlayerType.AnyPlayer` ⇒ 我们照常转换**没错**。
（`InternalAssetKey` 那处是包 07，`Format 2.0.0` ≥ 1.25 也合法。）

**30 个迁移里只有 `Migration_2_0` 是"资产级"的**，其余按机制分三类（逐个文件看过）：

| 类 | 例子 | 我们的处置 |
|---|---|---|
| **资产级改写**（改目标名/条目形状）| 只有 `Migration_2_0`（`IEditAssetMigrator`）| 已加守卫（§4）|
| **令牌门槛** | 各文件的 `AddedTokens` | 已加包级警告（§6）|
| **拒收用法**（`TryMigrate` / `TryMigrateMainContent` 返回 false ⇒ **整份文件被拒**）| `AnyPlayer`/`OnTimeChange`（1.25）、`Include`（1.16）、`PatchMode` 用在 `EditMap`（1.19）、`CustomLocations`（1.21）、`Format` 带补丁号（1.25）、`ConfigSchema.Section`（1.26）、`TargetField`/`AddWarps`（1.21/1.25）… | **不拦、只记账**（下表逐个量过，全 0 命中）|

**这一档逐个量过（扫全语料 36 包的源码 JSON；结论：命中的都是"格式够新、本来就合法"的包）**：

| 用法 | 语料里谁在用 | 够新吗 | 结论 |
|---|---|---|---|
| `OnTimeChange` | SVE(2.3.0) 7 处、包 20/25(2.0.0) 15+4 处 | ✓（1.19 的迁移只拒**早于 1.19** 的包）| 合法；`TokenManager.cs:42` 现代 CP 仍在用这个 update rate |
| `Include` | 包 01/04/06/08/09/11/12/19/20/21/22/25/27/29 | ✓（只拒**早于 1.16** 的；语料最旧 1.19）| 合法 |
| `PatchMode` | SVE 284 处等 11 个包 | ✓（1.19 只拒"`PatchMode` 用在 `EditMap`"）| 这些都是贴图补丁，合法 |
| `AddWarps` | SVE 1、包 20/23/24/25 | ✓（1.21 只拒**早于 1.21** 的）| 合法（`AddWarps` 本来就只有 CP 用）|
| `CustomLocations` | 包 23/24（`Format 2.0`）| ✓（1.21 只拒**早于 1.21** 的）| **CP 现代版仍然完整实现它**（`CustomLocationManager` 还在、`ScreenManager.cs:359` 照常注册）⇒ 我们展开它是对的 |
| `ConfigSchema.Section` / `Format` 带补丁号 | 无人用（`2.0.2` 那种补丁号都出现在 ≥1.25 的包上）| — | 无 |

⇒ 这一档**只记账不拦**：语料里没有一条落在"格式不够新"上，而拦下来会让"其实能转"的包白丢内容。
