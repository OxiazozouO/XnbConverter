# 婚姻转换的九处修复：代码、单测与受控对比

日期：2026-10-18

## 0. 范围

承接 [`research/2026-10-18-marriage-conversion.md`](../research/2026-10-18-marriage-conversion.md)。
本轮解决"自定义 NPC 可婚但**在纯 XNB 里整条消失 / 婚后没有台词**"的六类断点：

| # | 修复 | 代码位置 |
|---|---|---|
| 1 | 关系状态令牌冻结（`CanBeRomanced: {{XRelationshipStatus}}` 且带婚房 ⇒ 布尔 `true`） | `Cp/ValueTokenSubstitutor.FreezeRomanceFlags` |
| 2 | `Include.FromFile` 里的配置令牌（`Data/{{SeasonalOutfits}}_NPCData.json`）在解析阶段就代 | `Cp/ContentPack.RootConfigSchema` + `ConfigTokenSubstitutor.ReplaceForPath` |
| 3 | 角色条目里带 CP 令牌的嵌套条件只删那一条变体；`Home.Location`/`Tile.X/Y` 的地图令牌允许有损确定化到默认块 | `Cp/ValueTokenSubstitutor.SanitizeCharacterConditions` / `ToleratesLossy` |
| 4 | 有损展开结果只剩 `{{i18n:…}}` 时放行（Hector 的 `{{TrueName}}`） | `Cp/ValueTokenSubstitutor.Expand` |
| 5 | 婚后对话资产名别名（`JadeMalic` ← `MarriageDialogueJade`，含 `.zh-CN` 变体与 `.config`） | `Assets/MarriageReadiness.FindDialogueAlias` + `Cli/Program.MarriageDialogueAliasFix` |
| 6 | SpaceCore `setDating`/`setEngaged` 删除；嵌套 i18n 目录递归读取 | `Assets/EventCommandFixes.RemoveStateCommands`；`Workspace/PackTranslations` |
| 7 | 角色 `Appearance`/`Home` 条件里的 CP 布尔折成合法 GSQ；跨包整条替换保留已有 `Appearance` | `Cp/ValueTokenSubstitutor.FoldBooleanConditions`；`Merging/EditDataMerger.ApplyEntries` |
| 8 | 同名的 `_Appearance<n>` 生成资产已存在时复用，不再让整组失败（否则只写行走图、立绘全丢） | `Merging/AppearanceEmitter.TryWriteVariant` |
| 9 | TMX 路径也执行「配偶房间共享图保留更大版本」；跨包整条替换时发射器把产出树里已有的 `Appearance` 一并带上 | `Merging/LoadApplier.LoadMapTmx`；`Merging/AppearanceEmitter.TryBuildCharacterPatch` |

配套的令牌内建：`Lowercase`/`Uppercase`（CP `LetterCaseValueProvider.cs:41-42`）；
`HasFlag`/`HasSeenEvent` 的 `|contains=` 形态在 i18n 键里冻结为 `false`（两种键都存在）。

## 1. 单测

`dotnet test -c Release`：**1525 条全绿**。新增覆盖：

- `ValueTokenSubstitutorTests`：带婚房冻结、无婚房不动、i18n-only 有损放行、
  角色条件变体删除、住所地图有损确定化、布尔条件折叠（含 `TargetField` 形态与 `ANY "…"` 引号内）。
- `ContentPackTests`：`Include.FromFile` 配置令牌（`config.json` 覆盖默认值）。
- `DynamicTokenResolverTests`：`Lowercase`/`Uppercase`；存档条件令牌冻结为 `false`。
- `TranslationApplierTests`：参数值是 `{{i18n:…}}` 时先翻译；译文自身嵌套 i18n 继续展开。
- `MarriageReadinessTests`：对话资产名别名（最长前缀、不抢别的 NPC、已有内容不覆盖）。
- `EventCommandFixesTests`：`setDating`/`setEngaged`（含 `quickQuestion` 分支）删除。
- `PackTranslationsTests`：目录布局 `i18n/Zh/*.json`、多文件合并、顶层与目录共存。
- `EditDataMergerTests`：`Data/Characters` 整条替换保留已有 `Appearance`；别的资产不保留。
- `AppearanceTests`：同名 `_Appearance<n>` 生成资产已存在时复用，立绘仍会写出；
  本包整条新建条目时，产出树里已有的 `Appearance` 也一并带上。
- `LoadApplierTests`：`spouseRoom` 的小 TMX 不覆盖已有大图（30×27 保住）。

## 2. 定点受控对比（Sword & Sorcery + Bear Family，同一批 4 包）

| 指标 | 修复前（mar3） | 修复后（mar8） |
|---|---|---|
| `Data/Characters` 里的 Mateo/Cirrus/Hector | 0 | 3（均 `CanBeRomanced=true` + 婚房） |
| Sigurd/Gunnar/Teren | 0 | 3（均 `CanBeRomanced=true` + 婚房） |
| `MarriageDialogue<NPC>` 条目 | 0 | 90 / 89 / 89；Mateo 96 / Cirrus 95 / Hector 98 |
| `(no translation:…)` 次数 | 3760 | 130（剩余键在 i18n 里确实不存在） |
| 事件里的 `setDating`/`setEngaged` | 有 | 0 |

East Scarp（11 包定点）：Jade 显示名从 `(no translation:Name.Jade)` → `Jade`；
`MarriageDialogueJadeMalic` 从 0 条 → 79 条（含 `.zh-CN` 79 条）。

## 3. 全量 A/B（232 包，同参数）

基线 `prof-fix13` → 本轮 `prof-fix19`（同一 `convert-mods.py`、同一批包、同一顺序）。

### 3.1 判定分布

| 指标 | fix13 | fix19 | 差 |
|---|---|---|---|
| 补丁总数 | 32795 | 32813 | +18 |
| 写入 | 16123 | 16242 | +119 |
| 尝试写入 | 16754 | 16878 | +124 |
| 写失败 | 631 | 636 | +5 |
| 组发射 | 461 | 493 | +32 |
| Converted | 5785 | 5802 | +17 |
| ConvertedNoCondition | 9029 | 9100 | +71 |
| CreatesNewAsset | 1285 | 1299 | +14 |
| Emitted | 461 | 493 | +32 |
| NeedsReview | 2239 | 2131 | −108 |
| TargetMissing | 267 | 262 | −5 |
| NotApplicable | 10810 | 10807 | −3 |

`+5` 写失败全部来自 `Lurking in the Dark` 的 17 条 `Characters/schedules/SenS` 补丁：
它们以前在值令牌那一步就被挡住，现在能走到 Bake，卡在 `HasSeenEvent` 不可烘焙。
**产出树逐字节不变**（`SenS` 不在差异列表里）—— 是记账位置变了，不是内容退化。

### 3.2 树差异（逐文件 sha1）

| 项 | 数量 |
|---|---|
| 只在 fix13（丢失） | **0** |
| 只在 fix19（新增） | 154 |
| 内容不同 | 727 |

新增 154：82 个 `Characters/*`（对话/婚后对话/外观图）、42 个 `Data/*`、14 个 `Strings/*`、
6 个 `Portraits/*`、10 个 `Mods/mistyspring.GiEXredux/Textures/*`。

内容不同的 727：`Characters/Dialogue` 281、`Data/*` 214、`Data/Events` 179、`Strings` 39、
MEEP/RSV 8、`ContentHashes` 1、Eyvinder 外观图 3 —— **其余全是翻译改进**（嵌套 i18n 目录生效）。
**地图只有 2 个差异**：`Maps/Lemurkat_EastScarp_SpouseRoom.tbin`（12×9 → 30×27）和它的
图集 `Maps/LemurCustom.png`（随大图变大）；其它地图（含门禁）**逐字节相同**。

### 3.3 占位符

`(no translation:…)` 出现次数：**25653 → 2038**（fix13 → fix19）。
剩余的基本是 i18n 文件里确实没有的键（如 `miscellaneous.mapname.VioletMoonFestivalGrounds`、
`WaxBarrelMissingFuel`），不是解析失败。

### 3.4 婚姻资产

| 项 | 结果 |
|---|---|
| `CanBeRomanced: true` | **58**（fix13 38；新增 RSV 13 + S&S/Bear 6 + Sterling 布尔修正） |
| 缺 `Data/NPCGiftTastes` | 0 |
| 婚房 `MapAsset` 地图缺失 | 0 |
| 婚房矩形越界（25 张共享/独立图，逐张解析第一层尺寸） | **0** |
| 无婚房 | 12 个原版角色 + `Clara.Ripley`（原 mod 就没有；只打提示） |
| 无婚后对话 | 仅 `Clara.Ripley` |
| 六人婚后对话（base / zh） | Mateo 96/96、Cirrus 95/95、Hector 98/98、Sigurd 90/90、Gunnar 89/89、Teren 89/89 |
| JadeMalic | 79/79（从 `MarriageDialogueJade` 复制，含中文变体） |

### 3.5 残留标记

`Data/Events` 里 `setDating`/`setEngaged` **0**；`Maps/*.tbin` 里 `LoadMap` **0**；
`Maps/spouseRooms.tbin` 里 `ConditionalDoor` **0**；自定义 `OrderType` **0**。

## 4. 未确证 / 已知限制

- **实机未验**：送花束 → 订婚 → 婚礼 → 进农舍看婚房/配偶，本轮都没有跑。
- **Clara.Ripley**：原 mod 只有 `setDating ... false`（分手）而没有婚房/婚后对话；
  纯 XNB 造不出内容，保持可婚并打提示，不做臆造。
- **跨包优先级**：CP 全局按 `Priority` 排序，我们按包顺序。`Appearance` 已用
  「整条替换保留字段 / 发射器带上已有条目 / 同名生成资产复用」弥补；
  其它字段若被后包的整条 `Entries` 覆盖，仍可能不同（本轮语料未发现）。
