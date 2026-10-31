# SdvCpTool 设计文档

日期：2026-10-05
状态：待实施

## 1. 背景与目标

星露谷的 Content Patcher（CP）是主流的内容模组框架，但它要求玩家安装 SMAPI。本项目要做一个 CLI 工具，**尽可能把 CP 模组转换成纯 XNB / 纯数据形式的修改**，让不装 SMAPI 的玩家也能用。

可行性来自 1.6 的原生能力：游戏内置了 GameStateQuery（GSQ）条件语言，且多个数据资产带 `Condition` 字段。因此 CP 的一部分能力可以直接落到原生数据里；剩下的部分本项目明确报出来，不假装能处理。

### 目标

给定「一个 CP 内容包」+「一份已解包的资源树」：

1. 逐个 patch 判定能否转换成原生形式
2. **能转的**：直接合并进资源树，输出到新目录
3. **不能转的**：记进问题报告（含原因与建议）

产物交给现有的 `XnbConverter.Cli pack` 回包，不需要新写打包逻辑。

### 非目标

- 不改动 `XnbConverter` 仓库的任何一行代码（详见 §9）
- 不实现 GSQ 求值器 —— 条件原样搬运，由游戏自己求值
- 不读取玩家存档
- 不做 GUI
- 不处理 `.xwb` / `.fx` / 字体等与 CP 无关的资产类型

## 2. 术语

| 术语 | 含义 |
|---|---|
| **内容包**（pack） | 一个含 `content.json` 的 CP 内容包目录 |
| **patch** | `content.json` 的 `Changes` 数组中的一条修改记录 |
| **资源树**（tree） | `XnbConverter.Cli unpack` 的产物：`Data/*.json`、`Maps/*.tbin`、`Characters/*.png` 等，每份资产配一个同名 `.config` |
| **条件落点** | 目标资产自身能承载条件的位置（entry 的 `Condition` 字段、entry key 的前置条件段、对话键等） |
| **GSQ** | GameStateQuery，1.6 的通用条件语言 |

## 3. 输入 / 输出契约

### CLI

```
SdvCpTool.Cli convert --pack <CP内容包目录> --tree <已解包资源树> --out <输出目录>
                      [--report <报告路径.json>] [--dry-run]
```

- `--pack`：含 `content.json` 的目录。一次只处理一个内容包；多个包按顺序反复调用，上一次的 `--out` 作为下一次的 `--tree`。
- `--tree`：已解包的资源树根目录。
- `--out`：输出目录，必须为空或不存在。**从不原地修改 `--tree`。**
- `--report`：默认为 `<out>/report.json`。
- `--dry-run`：只判定与产报告，不写资源树。

### 退出码

| 码 | 含义 |
|---|---|
| 0 | 全部 patch 成功合并 |
| 1 | 有未合并项，但资源树已产出 |
| 2 | 致命错误（内容包无法解析、资源树不存在等） |

### 输出目录布局

```
<out>/
  Data/…              合并后的 JSON（含未改动的资产的原样拷贝）
  Maps/…  Characters/… …
  <pack名>.report.json
```

**整个资源树被完整拷贝到 `<out>`，只有被 patch 命中的文件内容不同。** 这样 `<out>` 可以直接拿去做 `pack`，不需要人工拼装。

## 4. 架构

```
D:\SdvCpTool\
├─ SdvCpTool.sln
├─ src\
│  ├─ SdvCpTool.Core\          net8.0 类库
│  │   ├─ Cp\                  CP 内容包解析
│  │   ├─ Conditions\          条件解析与词汇表
│  │   ├─ Assets\              资产定位与条件落点判定
│  │   ├─ Merging\             合并实现
│  │   ├─ Reporting\           报告生成
│  │   └─ Workspace\           资源树读写与输出
│  └─ SdvCpTool.Cli\           net8.0 可执行
└─ tests\
   └─ SdvCpTool.Core.Tests\    xUnit
```

各单元的职责与边界：

| 单元 | 做什么 | 依赖 |
|---|---|---|
| `Cp.JsoncReader` | 解析 CP 的 JSONC（`//`、`/* */`、尾逗号） | 无 |
| `Cp.ContentPack` | 读 `content.json` + `manifest.json`，递归展开 `Include` | JsoncReader |
| `Cp.ChangePatch` | 一条 patch 的强类型模型 | 无 |
| `Conditions.WhenExpression` | patch 的 `When` → 条件树（AND/OR/NOT/键+参数） | 无 |
| `Conditions.ConditionVocabulary` | 内置条件键 → 能否用 GSQ 表达；识别自定义 mod token | 无 |
| `Assets.AssetLocator` | `Target` 资产名 → 资源树中的文件路径 | Workspace |
| `Assets.ConditionLanding` | 目标资产的条件落点判定（**核心**） | 内省 JSON 形状 |
| `Workspace.VanillaAssetIndex` | 读原版 `ContentHashes.json`，回答"这个资产名是不是原版的" | 无 |
| `Merging.*` | 按 Action 类型实施合并 | Core 的 XNB / TBin / Png |
| `Reporting.Report` | 汇总判定结果，产出 JSON + 终端摘要 | 无 |
| `Workspace.Tree` | 遍历/拷贝资源树，定位同名 `.config` | 无 |

## 5. 核心概念：原生通道

工具的全部判断力集中在**一个**问题：

> **给定「CP 的条件」和「该补丁想做的改动」，这个组合能不能用某条游戏原生通道实现？**

### 5.1 五条通道

判定**不是**「能不能翻成 GSQ」，而是「**这个（条件, 改动）组合有没有任何一条原生通道能表达**」。

| 通道 | 机制 | 适用 | 状态 |
|---|---|---|---|
| **A. GSQ + 数据落点** | 把 `When` 翻成 GSQ 字符串，写进目标资产的条件位置 | 绝大多数 `EditData`/`EditImage` | M2 已实现 |
| **B1. 语言后缀** | 把内容只写进 `<name>.<locale>.xnb` | 条件含 `I18n` 时；**任意资产** | M3 |
| **B2. 季节 tilesheet 名** | tilesheet 取名 `spring_X`，地图引用它 | 换贴图 | M3 |
| **B3. 昼夜瓦片** | `DayTiles`/`NightTiles` 地图属性 | 换瓦片索引 | M3 |
| **C. 离线烘焙** | 读 `Mods/` 与包内文件求值后写死 | `HasMod`/`ModId`/`HasFile`/`FirstValidFile` | M3 |

**没有条通道可用才是真·不可转换。**

#### 各通道的机制来源与约束（已由源码核实）

| 机制 | 出处 | 约束 |
|---|---|---|
| `<name>.<locale>.xnb` 约定 | **XNA/MonoGame**（`ContentManager.LoadLocalized`） | 星露谷**弃用了那套机制**（它跟 OS 语言），自己重写为 `LocalizedContentManager`（跟游戏内设置）。约定沿用 |
| 语言码映射 | 星露谷 `LocalizedContentManager.LanguageCodeString` | 短码→长码：`zh`→`zh-CN` 等 12 个 + `mod`。**CP 的 `i18n/*.json` 用的正是同一套短码** |
| 季节 tilesheet | 星露谷 `GameLocation.GetSeasonalTilesheetName:16439` | **仅户外/Summit**（`:16414` 对室内提前 return） |
| `DayTiles`/`NightTiles` | 星露谷 `GameLocation:5274/:3083` | **`DayTiles` 仅室内**（`addLightGlows` 对室外提前 return）；`NightTiles` 室内外皆可 |
| 离线烘焙的依据 | CP 把这些令牌注册成 **`ImmutableValueProvider`**（`TokenManager.cs:268,286`） | 对给定的 mod 集与内容包，值在一次会话内**不可变** —— 所以固化在语义上是正确的，不是将就 |
| 图块 `ConditionalDoor <GSQ>` | 星露谷 | 唯一图块级 GSQ，且**只此一个动词**（开门） |
| 地图 `BuildConditions <GSQ>` | 星露谷 | 唯一地图级 GSQ，仅建造许可 |
| 地图 `LocationContext` → `Data/LocationContexts` | 星露谷 | 间接（天气/音乐），不碰地图 |
| `@TileIndex@` 索引级属性 | **xTile**（第三方库） | 一次定义覆盖所有同索引格 |

#### 判定的形态

```
每条补丁 → 枚举所有可用通道
  通道 A  条件可翻 GSQ（ConditionVocabulary + GsqTranslator）且资产有条件落点
  通道 B1 条件含 I18n
  通道 B2 改动是换贴图且条件是季节
  通道 B3 改动是换瓦片且条件是时间
  通道 C  条件全是可烘焙令牌（HasMod/ModId/HasFile/FirstValidFile）
  ↓
  任一可用 → 可转换，verdict 记录「走了哪条通道」
  全不可用 → 不可转换，列出试过哪几条、各自为什么不行
```

**报告的口径必须跟着改**：不能再说"GSQ 翻不了 = 不可转换"。

> **警告**：当前 `Verdict` 枚举（`NonTranslatableCondition` / `NoLandingPoint`）是**通道 A 导向**的措辞，会把"通道 A 不行"误报成"不可转换"。M3 必须把判定改成通道相对，并逐条重跑对账。已实测的影响面见 §12 遗留项。

### 5.2 通道 A 的判定：条件落点

### 判定方式：内省，不靠硬编码表

读取目标资产在资源树中的实际 JSON，观察形状：

- **entry 有 `Condition` 字段** → 条件可原样放入该字段。适用于 `Data/Locations` 的 `Forage`/`Fish`/`ArtifactSpots`/`Music`、`Data/GiantCrops`、`Data/WildTrees`、`Data/Buildings`、`Data/FishPonds`、`Data/Machines`、`Data/LocationContexts`、`Data/TriggerActions`。
- **资产的 key 是 `<id>/<前置条件>/…` 格式**（`Data/Events/*`、`Data/Festivals/*` 的部分条目）→ 条件可翻译为 key 里的 `G <GSQ>` 段。
- **资产是 `Dictionary<string,string>` 且 key 是对话键**（`Characters/Dialogue/*`）→ 条件可用对话键的受限语法表达（季节/地点/星期/好感/topic），**不支持任意 GSQ**。
- **以上都不成立** → 无条件落点。

内省而非硬编码的原因：对 mod 新增的资产同样成立，且不会因为记错某个资产的字段而误判。判定失败（JSON 结构无法识别）时降级为 `NeedsReview`，不静默丢弃。

### 条件本身的可翻译性

`When` 里的键按**权威对照表**（`docs/research/2026-10-05-cp-to-gsq-token-mapping.md`）分类：

| 类别 | 例 | 判定 |
|---|---|---|
| `GsqTranslatable` | `Season`、`HasSeenEvent`、`HasFlag`、`Relationship`、`Hearts`、`Time`、`Weather`、`Day`、`Year`… | 可机械翻译 |
| `GsqApproximate` | `DayEvent`、`IsMainPlayer`、`IsOutdoors`（带 player key 时） | 能生成 GSQ 但语义有偏差 → `NeedsReview` |
| `NoGsqEquivalent` | `DailyLuck`、`HasActiveQuest`、`PlayerName`、`ChildNames`、`ChildGenders`、`HavingChild`、`Pregnant`、`HasWalletItem` | **GSQ 原理上表达不了** → 不可合并 |
| `Bakeable`（原 `EnvironmentDependent`） | `HasMod`、`ModId`、`HasFile`、`FirstValidFile` | **可烘焙**（通道 C）—— 读 `Mods/*/manifest.json` 与包内文件即可求值 |
| `Locale`（新增，原误归 `EnvironmentDependent`） | `I18n` | **可转**（通道 B1）—— 写进 `<name>.<长码>.xnb` |
| `PureComputation` | `Range`、`Round`、`Count`、`Query`、`Merge`、`Render`… | 参数全为字面量 → 离线内联；含令牌嵌套 → `NeedsReview` |
| `CustomModToken` | 形如 `作者.Mod名/令牌名` | 不可翻译 |
| `Unknown` | 认不出（含 pack 用 `DynamicTokens` 自建的名字） | `NeedsReview` |

**`NoGsqEquivalent` 的根因**：GSQ 的原生关键字全是"检查当前上下文/单玩家/单地点是否为真"的布尔解析器，
**没有返回集合或遍历实体的能力**。凡 CP 里"返回多值再做成员/计数/遍历"的令牌，原理上就翻译不了 ——
要覆盖只能给目标 mod 写自定义 GSQ 关键字（`GameStateQuery.Register`）。

条件树里只要出现一个不可翻译的键，整个 patch 判为不可合并。

> 注意边界：本工具**不实现 CP 的令牌求值引擎**。"纯计算"一类只处理字面量参数这种平凡情形；一旦出现令牌嵌套，一律降级为 `NeedsReview`，由报告交给人工判断。

## 6. 判定分类

每条 patch 归入且仅归入一类：

| 分类 | 含义 | 是否写入资源树 |
|---|---|---|
| `Converted` | 已合并 | 是 |
| `ConvertedNoCondition` | 无条件，直接合并 | 是 |
| `NeedsReview` | 可合并但语义可能有偏差（如对话键翻译、结构无法识别） | 是，并在报告中标注 |
| `CreatesNewAsset` | 目标资产不在资源树中，但 Action 是 `EditData`/`Load` —— CP 会**新建**该资产 | 是（需确认新资产名可达），并在报告中标注 |
| `NoLandingPoint` | 目标资产没有条件落点 | 否 |
| `NonTranslatableCondition` | 条件含 `HasMod` 或自定义 mod token | 否 |
| `NeedsRuntime` | 带 `Update` 字段，要求运行时重算 | 否 |
| `LocalEdit` | 条件化的局部改图（`EditMap` + `When`） | 否 |
| `TargetContainsTokens` | `Target` 含 `{{...}}` 令牌，静态无法解析 | 否 |
| `TargetMissing` | 目标资产不在提供的资源树中，且不是"新建"情形 | 否 |
| `UnsupportedAction` | 未实现的 Action 或字段 | 否 |

### 6.1 `Target` 的解析规则

CP 的 `Target` 字段支持**逗号分隔的多值**，也支持令牌。来源：CP 官方文档
`docs/author-guide/action-editdata.md:161` —— "The game asset name to replace
**(or multiple comma-delimited asset names)** … This field supports tokens"。

因此判定前必须：

1. 按 `,` 拆分，逐段 `Trim`。
2. 任一段含 `{{` → `TargetContainsTokens`（静态分析无法解析动态资产名）。
3. 逐段解析；**全部**命中资源树才算命中。
4. 全部落空时，用原版资产索引（`Content/ContentHashes.json`）区分：
   - Action 是 `EditData` 且资产名**不是**原版资产 → `CreatesNewAsset`
   - 否则 → `TargetMissing`
5. 部分命中、部分落空 → 取最保守的结论，理由里点名落空的那几段。

**`Load` 暂不在第 4 条之列（M1 已知缺口）**：`Load` 走独立分支，在 `FromFile` 存在性检查之后才轮到目标判定，
因此它无法参与多值/新建判定。实测中 mod 的 `Load` 普遍用 `{{TargetWithoutPath}}.png` 这类令牌拼 `FromFile`，
静态解析不了内容。要正确支持 `Load` 新建资产，需先实现 `Target` 系列令牌在 `FromFile` 里的替换 —— 见 §12 遗留项。

### 6.2 为什么 `CreatesNewAsset` 不是 `TargetMissing`

CP 的 `EditData` / `Load` 对**不存在的资产是新建它**，不是失败。被新建的资产只要名字能被游戏在某个代码路径上请求到，就是可达的（例如
`Data/Festivals/winter28` 会由 `Event.cs` 按 `"Data\\Festivals\\" + 节日id` 拼出）。

新建资产正是纯 XNB 最擅长的操作：写一个新 `.json`/`.png`/`.tbin`，再往
`ContentHashes.json` 补一行 key。把它判成"无法合并"是方向性错误。

`EditMap` / `EditImage` 落在不存在的资产上则不同 —— 它们**修改**既有资产，本身不提供内容。这类仍判 `TargetMissing`，但理由要说明"该资产是 mod 自带的，请把它的资源也解包进资源树"，而不是笼统的"找不到"。


## 7. 各 Action 的实现范围

### 7.1 `EditData`（占比最大）

- 支持的字段：`Entries`（增/改条目）、`Fields`（改嵌套字段）、`MoveEntries`（调整顺序）、`TextOperations`（字符串替换）
- 合并方式：把 patch 的改动应用到目标 JSON 上
- 有条件时：按 §5 判定落点，把条件写进 entry

### 7.2 `Load`

- 目标资产**已存在**于资源树 → 等同于整份替换
- 目标资产**不存在** → 需要新建文件 + 从一个同类 `.config` 复制模板（沿用 README 里描述的手工流程）
- `FromFile` 的 `.json`/`.png`/`.tbin` 按扩展名分发

### 7.3 `EditImage`

- `PatchMode: Replace` 整图替换 → 直接覆盖 `.png`
- `PatchMode: Overlay` / 指定区域 → 用 `XnbConverter.Core` 的 `Utilities/Png` 做区域合成
- 无条件时始终可合并；有条件时判 `LocalEdit`

### 7.4 `EditMap`

- 只处理**无条件**的地图和图块属性改动，通过 `XnbConverter.Core` 的 `Tbin` 读写
- 有条件时判 `LocalEdit`（原版没有对应通道）

### 7.5 `Include`

- 不是实际修改，在解析阶段展开

## 8. 处理流程

```
1. 解析内容包 → patch 列表（Include 已展开）
2. 建输出目录，整树拷贝
3. 对每条 patch：
   a. 解析 When → 条件树；若含不可翻译键 → NonTranslatableCondition，跳过
   b. 若带 Update 字段 → NeedsRuntime，跳过
   c. AssetLocator 定位目标；找不到 → TargetMissing，跳过
   d. ConditionLanding 判落点；无条件落点且带条件 → NoLandingPoint，跳过
   e. 按 Action 分发到对应 Merger
   f. 合并成功 → Converted / NeedsReview；失败 → 记录原因，跳过
4. 产出报告
5. 按退出码返回
```

**关键约束：任何一步失败都只跳过该条 patch，不中断整个流程。** 工具的价值在于"尽可能多地转"，因此单条失败必须是局部的。

## 9. 依赖与隔离

### 引用 `XnbConverter.Core`

新项目**引用** `..\XnbConverter\XnbConverter.Core\XnbConverter.Core.csproj`，用于：

- `XNB` 门面（`Decode` / `ExportFiles` / `ImportFiles` / `Encode`）与 `XnbObject`（`.config` DTO）
- `Tbin`（`TBin10Reader.FormFile` / `Save`）—— 地图读写
- `Utilities/Png`（`PngReader` / `PngWriter`）—— 图片合成

**`XnbConverter` 仓库一行都不改。** 这是硬约束：该仓库已发布，且已有完整的功能边界。

### 运行时配置

`XnbConverter.Core` 通过 `RootPath` 解析 `.config/custom dll/` 下的游戏 DLL。处理带类型的 Data 资产（如 `Data/Locations`，其 reader 是 `StardewValley.GameData.Locations.LocationData`）需要 `StardewValley.GameData.dll`。

新工具在启动时通过继承 `XnbConverter.Configurations.RootPath` 建立自己的根路径（与 `XnbConverter.Cli` 的 `ConsoleRootPath` 同构），指向自己的 `.config/`。首次运行时若缺少必需 DLL，报明确错误并提示放置位置，不静默降级。

### 其他依赖

只加 `Newtonsoft.Json`（与 Core 一致）。不引入 ImageSharp 等重型库。

## 10. 错误处理

| 场景 | 行为 |
|---|---|
| `content.json` 不存在或语法错误 | 致命错误，退出码 2，打印出错位置 |
| `Include` 的 `FromFile` 不存在 | 警告，跳过该 include，继续 |
| JSON 结构无法识别 | 该 patch 记 `NeedsReview`，打印警告 |
| 单条 patch 合并异常 | 捕获，记入报告，继续下一条 |
| 输出目录已存在且非空 | 致命错误，退出码 2（不覆盖用户数据） |

**不吞异常。** 每条失败的 patch 在报告里都有可读的原因和定位信息（内容包文件 + patch 的 `LogName` 或序号）。

## 11. 测试与验收

### 单元测试

- `JsoncReader`：注释、尾逗号、字符串内的 `//`、转义
- `ContentPack`：`Include` 递归展开、相对路径以 pack 根为基准
- `ConditionVocabulary`：内置键识别、自定义 token 识别（`作者.Mod/令牌`）
- `ConditionLanding`：四类落点的正反例
- 各 `Merger`：给定输入 JSON + patch，断言输出 JSON

### 端到端验收

以 **Ridgeside Village 2.5.17** 为黄金样本（237 个 `content.json`，1622 条 patch）。

已有一份独立来源的手工分类统计作为对照基准：

```
无条件                 767
条件在事件键           23
条件在对话键           156
条件化局部改图         108
目标无条件落点         325
需 SMAPI 条件          243
```

**验收标准**：工具输出的分类分布与上表在可解释范围内吻合；每一处偏差都能追溯到具体原因并说明是工具判断更准还是手工统计有误。

附加验收：把 `<out>` 跑一遍 `XnbConverter.Cli pack`，确认能成功回包。

#### 首次对账结果（2026-10-05）

实际跑完的结论见 `docs/verification/2026-10-05-ridgeside-reconciliation.md`：

- **补丁总数 1704**（手工基线 1622）。差异 +82 全部来自一份手工基线解析失败的 CP 方言文件（对话值跨行未转义，Newtonsoft 接受、Python 严格拒绝）。**工具正确。**
- **发现三处判定缺陷**，其中一处影响 41% 的补丁（`EditData`/`Load` 新建资产被误判为 `TargetMissing`）。本 spec 的 §6.1 / §6.2 即依此修订。
- 对账也顺带确认了资源树完整无误：`TargetMissing` 中**零条**是原版资产。

## 12. 里程碑

| # | 内容 | 完成标志 |
|---|---|---|
| M1 | 内容包解析 + 条件解析 + 报告（不合并） | 对 Ridgeside 产出完整判定报告，与基准分布对账通过 |
| M2 | `EditData` 合并（JSON 资产） | Ridgeside 的 JSON 类 patch 全部合并或明确报因；产物能 pack 成功 |
| M3 | `EditMap` + `EditImage` 合并 | 地图与贴图类 patch 处理完毕，产物能 pack 并进游戏 |
| M4 | `Load` 新建资产 + `.config` 模板生成 | 新增资产类 patch 处理完毕 |

M1 是关键验证点：它不写任何合并逻辑，却能证明"判定表"是对的。

**实施按里程碑逐个推进，每个里程碑单独出一份实施计划。** 本 spec 描述的是完整设计；第一份计划只覆盖 M1。M1 的验收结果可能反过来修正本 spec 中关于落点判定的假设，此时先改 spec，再进入 M2。

### M1 遗留项（进入 M2 之前处理）

| # | 遗留项 | 影响 | 说明 |
|---|---|---|---|
| 1 | **`Load` 不参与新建资产判定** | 实测 103 条 `Load` 被判 `TargetMissing` | `Load` 走独立分支，且在 `FromFile` 存在性检查之后才判目标。这 103 条的 `FromFile` 全部含令牌（`{{TargetWithoutPath}}.png` 等）。修法：实现 `Target`/`TargetWithoutPath`/`TargetPathOnly` 在 `FromFile` 里的替换，然后把 `Load` 也纳入 `CreatesNewAsset`。保守方向（不会写出错误产物），但损失转换率。 |
| 2 | **`CreatesNewAsset` 未经实机验证** | 626 条以此为结论 | 该判定假设"新建的资产名能被游戏请求到"。对 `Data/Festivals/winter28` 这类可由 C# 拼接的名字成立，但未逐条验证。**M2 真正写文件之前，必须至少实机验证一个 `CreatesNewAsset` 的产物能进游戏。** |
| 3 | **原版资产索引依赖 `ContentHashes.json`** | 未提供时判定放宽 | 解包树不含该文件（解包器不复制非 xnb）。未提供时 CLI 警告并放宽为 `CreatesNewAsset`，此时原版资产缺失会被误判。 |
| 4 | 计划里的预测数字不可靠 | — | Task 12 的 Step 12 写的预期（706 / 116）与实际（626 / 245）不符。**该步的"预期数字"是估计值，不是断言**；实际值以对账记录为准。 |
