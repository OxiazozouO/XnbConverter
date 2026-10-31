# 对着 CP 源码逐块核我们：CP 有什么、我们做了什么

日期：2026-10-08

## 0. 怎么拿到权威源码（这一步以前被当成「拿不到」）

```bash
cp "<Mods>/ContentPatcher/ContentPatcher.dll" D:\tmp\cp\
cd D:\tmp\cp
ilspycmd ContentPatcher.dll > all.cs        # 23648 行，按类型可搜
```

`ilspycmd` 这台机器上就装着（`~/.dotnet/tools`），DLL 就在 Mods 目录里。
**以后再遇到「CP 到底怎么做的」这类问题，先来这里查**，不要再拿印象或文档措辞顶。

---

## 1. `Action`：CP 一共 5 个（`PatchType` 枚举）

`Load` / `EditImage` / `EditData` / `EditMap` / `Include` —— **5 个我们都实现了**
（`Include` 在 `ContentPack` 展开阶段）。语料里 `UnsupportedAction` = 0 与之一致。

## 2. `PatchConfig` 的 20 个字段

| 字段 | 我们 | 备注 |
|---|---|---|
| `Action` `Target` `When` `Update` `FromFile` `Enabled` `Priority` | ✅ | `Priority` 已解析成整数（`ChangePatch.Priority`） |
| `Entries` `Fields` `TargetField` `TextOperations` `MoveEntries` | ✅ | 语义都已对着反编译源码核过 |
| `MapProperties` `AddWarps` `MapTiles` | ✅ | `MapTiles` 的 SetIndex/SetTilesheet/SetProperties/Remove 全支持 |
| `FromArea` `ToArea` `PatchMode` | ✅ | 图片：`Replace`/`Overlay`（与 `PatchImageMode` 枚举一致）；地图：`Overlay`/`ReplaceByLayer`/`Replace` |
| `LogName` | ✅（只做显示） | |
| **`TargetLocale`** | ❌ **未实现** | 只对某个语言生效。**语料 0 处** |

这一个**真缺口**，但暴露面是 0 —— 记在这里备查，不为它写推测性代码。

⚠ **订正（2026-10-10 复核）**：本表原先还列了一行 `AliasTokenNames`，**那放错了** ——
它在 `ContentConfig` 里（见下节），是**包**的字段不是**补丁**的字段。
按反编译的 `PatchConfig.cs` 逐个点过：**补丁字段一共 20 个**，除 `TargetLocale` 外全部 ✅。
这轮专门核过三个不常出现的：`Fields`（`PatchJudge` 与 `AppearanceEmitter` 在用）、
`TargetField`（`PatchJudge.cs:619` 显式处理）、`Enabled`（`ContentPack.cs` 有完整实现，
含 `Format >= 1.25.0` 的语义、语料只 3 处用）—— **都在**。

## 3. `ContentConfig` 的 6 个字段

`Format`（版本号，不参与语义）、`DynamicTokens` ✅、`CustomLocations` ✅、
`Changes` ✅、`ConfigSchema` ✅、`AliasTokenNames` ❌（同上）。

## 4. 令牌：CP 一共 **70** 个（`ConditionType` 枚举）

逐项对我们的归类 —— **70 个全部有归类，0 个落 `Unknown`**：

| 我们的归类 | 个数 | 说明 |
|---|---|---|
| `GsqTranslatable` | 34 | 每个都有对应的 `TokenTranslator` 分支（`TokenTranslator` 的翻译分支也是 34 个，两边对得上） |
| `GsqApproximate` | 3 | `DayEvent` / `IsMainPlayer` / `IsOutdoors` |
| `NoGsqEquivalent` | 7 | `DailyLuck` `HasActiveQuest` `PlayerName` `ChildNames` `ChildGenders` `HavingChild` `Pregnant` |
| `Bakeable` | 4 | `HasMod` `ModId` `HasFile` `FirstValidFile` |
| `Locale` | 1 | `I18n` |
| `PureComputation` | 21 | 取值类，无 GSQ 一对一 |

（34 + 3 + 7 + 4 + 1 + 21 = 70，与 CP 枚举逐一对齐。）

### 4.1 拿游戏源码的 **115 个 GSQ 关键字**复核了这 10 个「非可译」

游戏侧权威名单取自 `D:\sdv_src\StardewValley\GameStateQuery.cs`
（解析器是**方法**：`public static bool <KEY>(string[] query, GameStateQueryContext context)`，共 **115** 个）。

三个「近似」逐个读了解析器实现，**判定都站得住**：

| 令牌 | 最接近的 GSQ | 为什么仍算近似 |
|---|---|---|
| `IsMainPlayer` | `IS_HOST` | CP 判的是**玩家**（`player.IsMainPlayer`），GSQ 的 `IS_HOST` 是 **`Game1.IsMasterGame`**（本机是否主机）—— 分屏下不是一回事 |
| `IsOutdoors` | `LOCATION_IS_OUTDOORS` | CP 判的是**当前玩家所在**地点；GSQ 不带参数时用的是**上下文**地点 |
| `DayEvent` | `IS_FESTIVAL_DAY` | CP 给的是节日**名字**（`save.GetDayEvent`）；GSQ 那个只接受 `(locationContext, dayOffset)`，按**日期**判 |

七个「无等价物」也逐条对过 115 个关键字：GSQ 有 `PLAYER_HAS_CHILDREN`（**计数**）但没有孩子姓名/性别，
有 `PLAYER_LUCK_LEVEL`（食物加成等级）但没有日运小数，有 `PLAYER_SPECIAL_ORDER_ACTIVE` 但没有任务日志关键字。
**这一档与 `HasWalletItem` 那次翻案不同，是真没有。**

## 5. 具名输入参数：CP 一共 **3** 个（`InputArguments.ReservedArgKeys`）

`contains` / `valueAt` / `inputSeparator`。

| 参数 | 我们 | 语料 |
|---|---|---|
| `contains` | ✅ 完整实现（含 JSON 值是**取反开关**这条） | 2213 处 |
| `valueAt` | ❌ 未实现 | **6 处，全在 `ChildGenders` 上** —— 而它本就是 `NoEquivalent`，**可行动暴露面 0** |
| `inputSeparator` | ❌ 未实现 | 0 处 |

`valueAt` 的语义（`Token.ApplyValueAt`）：把令牌的**值集合**按下标取一个（支持负数从尾部取）。
要做的话不难，但语料里那 6 处换成什么写法都译不出来 —— 所以**先不做**，记着。

## 6. 几条已经用这份源码订正过的

| 结论 | 影响 |
|---|---|
| `Entries` 两个容器都**整值**（`ToObject` + `SetEntry`），逐字段合并只在 `Fields` | 订正数组分支的合并语义；追加不再补 `Id` |
| 字段名按**大小写不敏感**匹配（打包器同此） | 修 SVE 的 `ToolTips`/`Tooltips`，回包失败 1 → 0 |
| `MoveEntries` 的完整语义（含**先删后插要回挪下标**） | 补上整块被忽略的功能；语料 47 条 |
| `ListKeyValueEditor`：`#<整数>` 是下标、键匹配**大小写敏感**、`GetAssetKeyFunc` 取 `Id`/`ID` | 已与实现一致（核过，非改动） |

## 6.1 另外几处**核过是对上的**（不是缺口，别再重查）

对着反编译源码逐条核，结论「我们已经有」的：

| 面 | 依据 | 结论 |
|---|---|---|
| `ConfigSchema` 六条规则（名字含 `:`/`|` 丢弃；与内建条件同名时内建优先；旧格式 `<1.7` 的 `AllowValues` 必填且 `Default` 取第一个；`AllowBlank:false` 无 `Default` 丢弃；`Default` 越 `AllowValues` 丢弃；`AllowMultiple:false` 给多个 `Default` 丢弃） | `ConfigFileHandler.LoadConfigSchema` | `Cp/ConfigSchemaTokens.cs` 逐条实现 ✓ |
| 配置**取值**非法时回退 `Default`（不拒整包） | 同上 `LoadConfigValues` | 同 ✓ |
| `EditMap` 的 `MapProperties`：值为 `null` ⇒ **删属性**、否则设值 | `EditMapPatch` 13905-13913 | 同 ✓ |
| `EditMap` 的 `PatchMode` 三个值 + 缺省 `ReplaceByLayer` | `PatchMapMode` 枚举（下标 1） | 同 ✓ |
| `Enabled` 在现代格式（≥1.25）里只要不是字面 `true` 就**整条跳过** | `PatchLoader` | 同 ✓ |
| `#<整数>` 下标、键按 `Id`/`ID` **大小写敏感**匹配 | `ListKeyValueEditor` | 同 ✓ |

## 7. 格式迁移（CP 有 36 个，我们一个都没做）—— 查完，暴露面 0

CP 按包的 `Format` 从旧到新逐级套迁移。语料里的 `Format` 分布：

```
2.0.0 ×20   2.0 ×5   2.0.2 ×1   2.1.0 ×1   2.3.0 ×1
1.29.0 ×2   1.25.0 ×1   1.19.0 ×1
```

把 1.0～1.30 那 27 个迁移类逐个读了一遍，结论是**绝大多数只是「拒绝」**——
它们把「那个版本还不该出现的写法」判成错误（例：1.23 拒绝 `TextOperations` 的 `Fields` 目标
与 `valueAt` 参数；1.25 拒绝 `AnyPlayer` 玩家类型；1.21 拒绝 `CustomLocations`/`AddWarps`）。
真正的**内容改写**只有一处：

| 迁移 | 改写 |
|---|---|
| `Migration_1_20` | `{{Weather}}` → `{{Weather: Default}}`（补上默认地点上下文） |
| `Migration_1_21` | `Enabled` 是单个令牌时改写成 `When` 项（语料里 `Enabled` 0 处） |

而语料里 `Format < 1.20` 的包**只有一个**（身临其境的配偶 1.19.0），
社区中心扩建（1.25.0）只落在 1.26/1.28 两个**校验型**迁移上。
实测那两个包里 `{{Weather}}` **0 处** ⇒ 内容改写型迁移的**可行动暴露面 0**。

### 7.1 顺带查清：资产改名表（也用不上）

`Migration_2_0` 是**运行时资产改名**，五对：

```
Data/BigCraftablesInformation → Data/BigCraftables      Data/Blueprints          → Data/Buildings
Data/NPCDispositions          → Data/Characters         Data/ObjectContextTags   → Data/Objects
Data/ObjectInformation        → Data/Objects
```

语料里确实有 3 处引用旧名（SVE 的 `Magnus.json`/`CutContent.json`、谢恩的 `content.json`），
但那两个包声明的是 **2.3.0 / 2.0.0** —— 迁移**不适用**，CP 也不会改名，
而且那 3 处全在**已被跳过的 Include** 里（`When` 用 `HasSeenEvent` 之类离线判不了）。
所以「我们没做改名表」目前与 CP 一致，**不是缺口**。

## 8. 这份对照表**不覆盖**什么

- 只核到**字段与语义**这一层，没有做「把 CP 跑起来逐条比产出」那种动态验证；
- 语料是 32 个包，不是 CP 的全体用户；
- 标 ❌ 的三处（`AliasTokenNames`、`TargetLocale`、`valueAt`/`inputSeparator`）
  都是**已经知道缺什么、也知道暴露面**，不是「没查到」。
