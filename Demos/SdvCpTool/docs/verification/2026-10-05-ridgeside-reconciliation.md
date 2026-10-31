# M1 验收：Ridgeside Village 对账记录

> ⚠ **历史记录（命令已变）**：本文里的 `translate` / `--dry-run` / `--fast` / `--no-audit` /
> `--audit-cache` / `--append` 等开关**都已经删掉了**。现役只有一个命令：
> `SdvCpTool.Cli convert --pack <包> --tree <树> --out <输出> [--report] [--mods] [--content-hashes]`
> （"续写同一棵树"由 `--out` 是不是空目录**自动判断**，不再有开关）。照抄本文的命令会报错。

日期：2026-10-05
样本：Ridgeside Village 2.5.17（`[CP] Ridgeside Village`，235 个 content.json）
资源树：`D:\sdv_tree`（原版 Content 全量解包，3552 个 .config）

## 实际命令

```bash
cd /d/SdvCpTool
dotnet run --project src/SdvCpTool.Cli -- convert \
  --pack "D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods\Ridgeside Village 2.5.17-7286-2-5-17-1731446698\Ridgeside Village\[CP] Ridgeside Village" \
  --tree "D:\sdv_tree" \
  --out  "D:\sdv_out" \
  --dry-run
```

退出码 0（无致命错误）。报告写入 `D:\sdv_out\report.json`。

## 工具输出

```
补丁总数：1704
  预计可合并：249
  无法合并：  1455

按判定分类：
  Converted                     63
  ConvertedNoCondition         184
  NeedsReview                    2
  NoLandingPoint               195
  NonTranslatableCondition     226
  NeedsRuntime                 140
  TargetMissing                894
```

内容包解析零警告，235 个文件全部解析成功。

## 与手工基线的对比

手工基线来自会话前期的独立 python 扫描（按条件形状分类，不检查目标是否存在）。

| 类别 | 工具 | 手工基线 | 差异 |
|---|---|---|---|
| **总数** | **1704** | **1622** | **+82** |
| 无条件 → `ConvertedNoCondition` | 184 | 767 | −583 |
| 条件在事件键 → `Converted` | 63 | 23 | +40 |
| 条件在对话键 → `NeedsReview` | 2 | 156 | −154 |
| 条件化局部改图 → `LocalEdit` | **0** | 108 | −108 |
| 目标无条件落点 → `NoLandingPoint` | 195 | 325 | −130 |
| 需 SMAPI 条件 → `NonTranslatableCondition` | 226 | 243 | −17 |
| 无对应 → `TargetMissing` | 894 | — | +894 |
| 无对应 → `NeedsRuntime` | 140 | — | +140 |

## 逐项追溯

### 1. 总数 +82：**工具更准，基线有解析失败**

手工基线的 python 扫描在 `Data/NPCData/ConversationTopics.json` 上解析失败（"Invalid control character"），整份文件被跳过。

该文件含 **恰好 82 条** `"Action"`。工具报告来自该文件的补丁数也是 **82**。

原因：该文件的对话值**跨行且换行未转义**。这是 CP 内容包的方言 —— CP 用宽松解析器，Newtonsoft 也能接受，而 python 的 `json` 严格拒绝。

**结论：1622 + 82 = 1704 ✓，差异完全解释，工具正确。**

### 2. `TargetMissing` 894 —— 工具引入的新类别，**但判定方向有误**

手工基线不检查目标是否存在于资源树，所以没有这个类别。工具把"目标不在树里"当作终局阻断。

用原版 `ContentHashes.json`（3560 个原版资产名）拆分这 894 条：

| 细分 | 数量 | 性质 |
|---|---|---|
| 原版资产名但树里没有 | **0** | — |
| 非原版资产名（mod 新增） | 822 | 见下 |
| `Target` 含 `{{` 令牌 | 21 | 静态不可解析 |
| `Target` 逗号多值 | 51 | **实现 bug** |

**零条是原版资产** —— 说明资源树完整无误，894 条全部指向 mod 自己新增的资产。

822 条的 Action 分布：

| Action | 数量 | 正确判定应为 |
|---|---|---|
| `EditData` | 625 | CP 对不存在的资产是**新建**它，可转换 |
| `EditMap` | 111 | mod 自带地图文件，确实不在原版树 —— `TargetMissing` 成立，但**理由文案误导** |
| `Load` | 81 | CP 会**新建**，可转换 |
| `EditImage` | 5 | 需 mod 自带的源贴图 |

**问题：625 + 81 = 706 条（占全部补丁的 41%）被判为"无法合并"，但它们恰恰是"新建资产"这一类 —— 而新建资产正是纯 XNB 最擅长的操作**（写一个新 `.json`/`.png` + 补 `ContentHashes.json` key）。判定方向错了。

### 3. 逗号多值 51 条：**确认是实现 bug**

CP 官方文档 `docs/author-guide/action-editdata.md:161` 原文：

> `Target` | The game asset name to replace **(or multiple comma-delimited asset names)** … This field supports tokens

实例：`"Target": "Maps/Farm, Maps/Farm_Island, Maps/Farm_FourCorners, ..."`。

`ResourceTree.Resolve` 把整串当成一个资产名 → 必然找不到。而这些农场地图**在原版树里是有的**（`D:\sdv_tree\Maps\Farm.tbin` 等），本可正常判定。

### 4. `Target` 含令牌 21 条：判定合理，**理由文案误导**

CP 文档同一句写明 `Target` **支持令牌**。实例：`{{PeacefulEnd.CustomCompanions/Companions:Rafseazz.RSVCC.Howdy}}`。

静态分析确实无法解析，但报告的"资源树中找不到资产"会让使用者以为是自己少放了文件。

### 5. 其余类别差异：**由 2 引起，不是独立问题**

条件化局部改图 108 → 0、条件在对话键 156 → 2、无条件 767 → 184，都是因为 `TargetMissing` 检查排在它们之前，把本该落到这些类别的补丁先截走了。一旦第 2 条修好，这些数字会回升。

## 结论

**判定表需要修正，且是结构性修正**（不是调参）：

1. `TargetMissing` 不该是 `EditData`/`Load` 的终局阻断。CP 语义下这两个动作对不存在的资产是**新建**。
2. `Target` 必须按 CP 语法拆分：先按逗号切多值，再对每个值判定；含 `{{` 令牌的值单独归类。
3. 需要区分"mod 新增资产"与"原版资产缺失"（可用原版 `ContentHashes.json` 作判据）。
4. 报告理由文案要区分"树里没有"与"静态不可解析"。

## 建议的 spec 改动

新增判定值（spec §6）：

| 新增值 | 含义 | 归属 |
|---|---|---|
| `CreatesNewAsset` | 目标不在树里，且 Action 是 `EditData`/`Load` —— CP 会新建该资产 | 可合并（需人工确认新资产名可达） |
| `TargetContainsTokens` | `Target` 含 `{{` 令牌，静态无法解析 | 需人工 |

`PatchJudge` 的判定链调整：

1. 解析 `Target` 时按 `,` 拆分；逐个 `Resolve`。
2. 任一值含 `{{` → `TargetContainsTokens`。
3. 全部解析不到时：
   - Action ∈ {`EditData`, `Load`} 且资产名**不在** `ContentHashes.json` → `CreatesNewAsset`
   - 否则 → `TargetMissing`（原义）
4. 部分解析到、部分没有 → 按"最差"的结论，理由里点名缺失的值。

需要 `ResourceTree` 增加对 `ContentHashes.json` 的读取（新单元 `Workspace.VanillaAssetIndex`）。

## 未修正项（M1 记录，留待 M2）

- 本记录**只诊断不改代码**。按计划 Task 11 的约定，先改 spec 与 plan，再决定何时实施。
- 已知限制（计划 Task 7 节）未变：`HasConditionFieldAnywhere` 假阳性风险、落点判定依赖树内样本、`Probe` 无缓存。

## Task 12 修正后复跑（2026-10-05）

判定表修正（Target 多值拆分、新建资产识别、令牌目标归类）后，用同一内容包与资源树复跑，
并新增 `--content-hashes` 指向原版 `ContentHashes.json`（3560 个原版资产名）：

```bash
cd /d/SdvCpTool
dotnet run --project src/SdvCpTool.Cli -- convert \
  --pack "D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods\Ridgeside Village 2.5.17-7286-2-5-17-1731446698\Ridgeside Village\[CP] Ridgeside Village" \
  --tree "D:\sdv_tree" \
  --content-hashes "D:\Stardew.Valley.v1.6.15\Stardew Valley\Content\ContentHashes.json" \
  --out  "D:\sdv_out2" \
  --dry-run
```

补丁总数仍为 1704，退出码 0。新分类计数：

```
补丁总数：1704
  预计可合并：875
  无法合并：  829

按判定分类：
  Converted                     63
  ConvertedNoCondition         184
  NeedsReview                    2
  CreatesNewAsset              626
  NoLandingPoint               195
  NonTranslatableCondition     226
  NeedsRuntime                 140
  LocalEdit                      2
  TargetContainsTokens          21
  TargetMissing                245
```

对比修正前（见本文件上方的诊断记录）：

| 类别 | 修正前 | 修正后 | 说明 |
|---|---|---|---|
| `CreatesNewAsset` | —（误并入 `TargetMissing`） | **626** | 全部为 `EditData`，原版清单交叉验证后确认非原版资产 |
| `TargetContainsTokens` | —（误并入 `TargetMissing`） | **21** | `Target` 含 `{{` 令牌，理由文案已改为"静态无法解析" |
| `TargetMissing` | **894** | **245** | 111 `EditMap` + 103 `Load` + 31 `EditImage` |
| `LocalEdit` | 0 | 2 | 原先被 `TargetMissing` 提前截走 |
| 预计可合并 | 249 | 875 | 主要是 626 条新建资产归入可合并 |

`TargetMissing` 中已无任何 `EditData`。含逗号 `Target` 的补丁共 72 条，现按多值拆分后分别落到
`Converted` / `TargetContainsTokens` / `NonTranslatableCondition` / `LocalEdit` / `NeedsRuntime` /
`CreatesNewAsset` 等各自的类别（不再整串误判）。

**与计划 Task 12 Step 12 预期数字的差异（如实记录）：**

- 计划预期 `TargetMissing` ≈ 116（`EditMap` 111 + `EditImage` 5），实际 245。差在 `Load` 103 仍归
  `TargetMissing`，且 `EditImage` 实为 31 而非 5。
- 计划预期 `CreatesNewAsset` ≈ 706，实际 626。差的 80 条为 `Load`：`PatchJudge` 中 `Load` 在到达
  多值/新建判定前就 `return JudgeLoad(...)`，所以 `createsNew` 条件里的 `|| Load` 分支不可达。
  这 103 条 `Load` 的 `FromFile` 全部含 CP 令牌（如 `{{TargetWithoutPath}}.png`、`{{Target}}.png`），
  静态无法解析内容，判定为 `TargetMissing`（理由"FromFile 不存在"）偏保守但不会写出错误结果。
  若要贴合 spec §6.1（`Action ∈ {EditData, Load}`）把 `Load` 也纳入 `CreatesNewAsset`，需另行处理
  `FromFile` 令牌问题，留待后续。

## 通道模型重跑（2026-10-05，Task 3）

判定表从「能不能翻成 GSQ」改为「有没有任何一条原生通道能表达」后复跑，同一内容包、资源树与
原版资产清单，报告写入 `D:\sdv_out5`：

```bash
cd /d/SdvCpTool
dotnet run --project src/SdvCpTool.Cli -- convert \
  --pack "D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods\Ridgeside Village 2.5.17-7286-2-5-17-1731446698\Ridgeside Village\[CP] Ridgeside Village" \
  --tree "D:\sdv_tree" \
  --content-hashes "D:\Stardew.Valley.v1.6.15\Stardew Valley\Content\ContentHashes.json" \
  --out "D:\sdv_out5" --dry-run
```

退出码 0，补丁总数仍为 1704。报告 JSON 的每条 patch 新增 `channel` 字段，终端摘要新增「按通道分类」。

### 改前 vs 改后

「改前」取本次改动前的口径（`Converted 62 / NeedsReview 3`，即通道判定接入前最后一次状态）。

| 类别 | 改前 | 改后 | 变化 |
|---|---|---|---|
| **补丁总数** | 1704 | 1704 | 0 |
| **预计可合并** | 875 | **921** | +46 |
| **无法合并** | 829 | **783** | −46 |
| `Converted` | 62 | 62 | 0 |
| `ConvertedNoCondition` | 184 | 184 | 0 |
| `NeedsReview` | 3 | **49** | **+46** |
| `CreatesNewAsset` | 626 | 626 | 0 |
| `NoLandingPoint` | 195 | 195 | 0 |
| `NonTranslatableCondition` | 226 | **180** | **−46** |
| `NeedsRuntime` | 140 | 140 | 0 |
| `LocalEdit` | 2 | 2 | 0 |
| `TargetContainsTokens` | 21 | 21 | 0 |
| `TargetMissing` | 245 | 245 | 0 |

逐条 diff 确认：全 1704 条里**只有 46 条**判定发生变化，全部是
`NonTranslatableCondition → NeedsReview`，且这 46 条的 `Channel` 均为 `Bake`。其余 1658 条判定不变。

### 新增的通道分布

```
按通道分类：
  None                        1632
  Gsq                           26
  Bake                          46
  LocaleSuffix                   0
  SeasonalTilesheet              0
  DayNightTiles                  0
```

判定 × 通道交叉表：

| 判定 | Channel | 数量 |
|---|---|---|
| `Converted` | `Gsq` | 26 |
| `Converted` | `None` | 36 |
| `NeedsReview` | `Bake` | 46 |
| `NeedsReview` | `None` | 3 |
| `ConvertedNoCondition` / `CreatesNewAsset` / `NoLandingPoint` / `NonTranslatableCondition` / `NeedsRuntime` / `LocalEdit` / `TargetContainsTokens` / `TargetMissing` | `None` | 其余 |

说明：

- `Channel.None` 用于**无条件**补丁（`ConvertedNoCondition` 184、无条件的 `Load` 36）以及所有未转换的补丁 ——
  这些确实「没有通道需要走」。无条件补丁记 `None` 与 Task 2 的单测预期一致。
- `Channel.Gsq` 26 = 落点判定为 `EntryConditionField` / `EntryKeyConditions` 且有条件的 `EditData`。
- `Channel.Bake` 46 = 41 条 `EditData` + 5 条 `EditMap`，其 `When` 含 `HasMod` / `HasFile` 等可烘焙令牌。
  注意这 5 条 `EditMap` 原本会被判 `LocalEdit`（"条件化地图改动没有通道"），现在正确识别为"烘焙成无条件后即可表达"。
- **`LocaleSuffix` / `SeasonalTilesheet` / `DayNightTiles` 均为 0** —— 本内容包里没有 `I18n` 条件，
  也没有只靠季节 tilesheet / 昼夜瓦片表达的条件。因此通道 B1 的判定路径本轮**未被真实数据触发**，
  其正确性只有单测覆盖。

### 与预期的重要出入（如实记录）

计划背景里推断 `NonTranslatableCondition` 的 **226 条**「现在应落到 `Channel.Bake` / `Channel.LocaleSuffix`」。
实测**并非如此**：

- 只有 **46 条**落到 `Channel.Bake`。
- **0 条**落到 `Channel.LocaleSuffix` —— 本包无 `I18n` 条件。
- 其余 **180 条**仍然是货真价实的不可转换：它们全部是 `CustomModToken`（`Rafseazz.RidgesideVillage/*`
  等 mod 用 C# 注册的令牌，如 `RSVNPCSAttendFestivals` 128 条、`EasyIntroduction` 37 条等），
  五条通道**都不适用**。旧口径把这 180 条和 46 条可烘焙的混在同一类别里，通道判定只是把这 46 条拆了出去。

因此"NTC 226 全部可转"是不成立的；通道模型的真实收益是 **+46 条判定为可转换**（875 → 921）。

### ⚠️ 「判定为可转换」不等于「本工具已能产出」

必须区分两件事：

| 说法 | 含义 |
|---|---|
| **判定为可转换** | 设计上存在一条原生通道能表达该补丁 |
| **本工具已能产出** | 该通道的产出（写出 XNB/资源树）实现已完成 |

当前实际状态：

- **通道 A（GSQ）**：判定已接入（26 条），但**写入尚未实现** —— 合并属于后续里程碑（本命令只有 `--dry-run`）。
- **通道 C（烘焙）**：**判定已接入（46 条），产出未实现**。判定目前只是说"该走烘焙、且未提供 `--mods` 时判 `NeedsReview`"，
  工具**不会**真的去读 `Mods/` 求值，更不会写出来。
- **通道 B1（语言后缀）**：判定路径已写好，但本包 0 条命中，且**产出未实现**。

所以本轮 `预计可合并 921` 提升的全部含义是：**这 46 条在判定上不再被误报为"不可转换"**，
不代表工具能把它们写出来。Ridgeside 全包目前仍是 **0 条真正产出**（`--dry-run`）。

### 与前一次运行产物 `/d/sdv_out2` 的细微差异

本文件上方 Task 12 的产物 `/d/sdv_out2` 记录为 `Converted 63 / NeedsReview 2`，而本次「改前」基线为
`Converted 62 / NeedsReview 3`。这 1 条的差异来自通道工作**之前**的提交（`3a83168`，为 `Data/Characters`
的一条含 `RandomNumber` 的补丁补上 `NoGsqEquivalent`/纯计算降级），**不属于本次 Task 3 的改动**。
本次任务自身只引起上述 46 条 `NonTranslatableCondition → NeedsReview`。
