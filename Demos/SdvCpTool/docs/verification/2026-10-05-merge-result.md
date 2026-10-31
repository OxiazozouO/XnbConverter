# M3 验收：合并产出结果

日期：2026-10-05
样本：Ridgeside Village 2.5.17（`[CP] Ridgeside Village`，235 个 content.json）
资源树：`D:\sdv_tree`（原版 Content 全量解包）
产出树：`D:\sdv_merged_final`
回包产出：`D:\sdv_packed`

## ⚠️ 先划清三层（本文件最重要的约定）

| 层 | 含义 | 本次证据 |
|---|---|---|
| **① 判定为可转换** | 设计上存在一条可用通道 | **910 条**（报告 `判定可合并`） |
| **② 已写入数据** | 数据文件真的落盘了 | **838 条**（`written: true`，文件已在树上核实） |
| **③ 能被游戏读到** | 三件套齐全 + 回包成功 + **实机可加载** | **未验证**。只有部分证据（见下） |

**本次只能确证到第②层。** 第③层没有任何实机验证 —— 我们既没有把产物接进游戏，也没有启动游戏读它。
回包成功与回环无损只是第③层的**必要不充分**证据，**不得**据此宣称"游戏能读到"。

---

## 一、实际命令

```bash
cd /d/SdvCpTool && rm -rf /d/sdv_merged_final
dotnet run --project src/SdvCpTool.Cli -- convert \
  --pack "D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods\Ridgeside Village 2.5.17-7286-2-5-17-1731446698\Ridgeside Village\[CP] Ridgeside Village" \
  --tree "D:\sdv_tree" \
  --content-hashes "D:\Stardew.Valley.v1.6.15\Stardew Valley\Content\ContentHashes.json" \
  --mods "D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods" \
  --out "D:\sdv_merged_final"
```

**退出码 1**（有不可合并的补丁，符合预期，非致命错误）。

> 计划里的输出目录是 `D:\sdv_merged`；本次按任务指示改用 `D:\sdv_merged_final`。其余参数与计划一致。

### 报告位置

未传 `--report`，报告落在 `<out>` 的同级目录：**`D:\[CP] Ridgeside Village.report.json`**（即盘符根目录）。
这是"报告写到资源树外"这一设计的副作用 —— 树外不等于"合理位置"，落在 `D:\` 根有点脏。不影响功能。

---

## 二、统计产出（与基线逐项对比）

| 指标 | 基线（Task 7d 后） | 本次 | 差异 |
|---|---|---|---|
| 补丁总数 | 1664 | **1664** | 0 |
| 判定可合并 | 910 | **910** | 0 |
| 无法合并 | — | **754** | 1664−910 |
| 实际写入 | 838 | **838** | 0 |
| 写入失败 | 33 | **33** | 0 |
| 未尝试写入 | — | **39** | = 910−838−33 |
| 新建文件 | 462 | **462** | 0 |
| ContentHashes 补入 key | — | **231** | — |

**与基线完全一致，无回归。**

按判定分类（合计 1664）：

```
Converted                     62
ConvertedNoCondition         179
NeedsReview                   43
CreatesNewAsset              626
NoLandingPoint               194
NonTranslatableCondition     152
NeedsRuntime                 140
LocalEdit                      2
TargetContainsTokens          21
TargetMissing                245
```

按通道分类：

```
None                        1598
Gsq                           26
Bake                          40
```

**实际落盘核对**（独立于工具报告，用文件系统 diff 得到）：

- 产出树**新增文件 463 个** = 231 个新资产的 `.json` + 231 个配套 `.config` + 根目录 `ContentHashes.json` 1 个。
- 工具报告的"新建文件 462"= 231×2，**不含** `ContentHashes.json`（它是单独写出的）。两个数不矛盾，只是口径不同。
- 231 个新资产**全部**有配套 `.config`（0 缺失）。

……

---

## 三、抽查结论（人工核对）

### 抽查 1：`Characters/Dialogue/Acorn`（多来源合并）

- **源**：`Assets/Dialogue/Acorn.json#0`（17 条 Entries）+ `Data/NPCData/ConversationTopics.json#57`（2 条 Entries），两条 `EditData` 都判 `CreatesNewAsset`、都 `written: true`。
- **产物**：`Characters/Dialogue/Acorn.json` 共 **21 个键**（17+2，无遗漏无重复）。
- **一致性**：逐键与源 `Entries` 对照，**值逐字相同**。但值是 `{{i18n:Acorn.Dialogue.spring_1}}`、`{{i18n:keahiprank.{{TargetWithoutPath}}}}`、`{{i18n:Acorn.Dialogue.random.{{Random: 1, 2, ...}}}}` 这类**未解析令牌**，原样写入。
- **config**：有，`Readers` = `DictionaryReader<String,String>`，与对话资产形状相符。✅
- **ContentHashes**：`Characters/Dialogue/Acorn.xnb` 已补上。✅

### 抽查 2：`Data/Festivals/winter28`（`Load` 打底 + `EditData` 叠加）

- **源**：`Load`（FromFile=`Data/Festivals/RidgesideFestivals/EmberNight.json`）+ `EditData`（`FestivalLocations/EmberOfResolutions.json` 等多条）。
- **产物**：顶层键 = EmberNight 的 4 个基础键（`name`/`conditions`/`set-up`/`mainEvent`）**全部存在**，另加 **116 个**来自 `EditData` 的键。
- **一致性**：打底键**零缺失**；`Load` 打底与 `EditData` 叠加都生效，产物**不是半成品**。✅
- **config**：有，`DictionaryReader<String,String>`。✅
- **ContentHashes**：`Data/Festivals/winter28.xnb` 已补上。✅
- ⚠️ **但**：这 116 个键里混入了 7 条**带 `When` 的条件补丁**（`HasSeenEvent` 等），它们被当成无条件写入 —— 见第五节发现 1。

### 抽查 3：`Characters/schedules/Acorn`（日程）

- **源**：`Assets/Schedules/Acorn.json` 两条 `EditData`：`#0` 无条件（`spring`=`2400 ...RSVHiddenWarp2...`），`#1` 带 `When: {HasSeenEvent...}`（`spring`=`0 ...RSVTheHike...`）。
- **产物**：`{"spring": "0 Custom_Ridgeside_RSVTheHike 44 61 2/..."}` —— **只有 #1 的值**，#0 被覆盖。
- **config**：有（字典形状，与 `Characters/schedules/*` 相符）。✅
- **ContentHashes**：键**补上了**，但大小写是 `Characters/Schedules/Acorn.xnb`（大写 S），而磁盘上的文件是 `Characters/schedules/Acorn.json`（小写 s）。⚠️ 见第五节发现 2。

### 抽查 4：`Data/Events/AdventureGuild`（新建事件资产）

- 资源树中没有 → 新建。产物为 1 个事件键（`75160075/f Freddie .../`），配套 `.config` 存在。✅

### 抽查 5：`Strings/schedules/Aguar`

- 新建，产物 `{"checkup": "{{i18n:checkup.{{TargetWithoutPath}}}}"}`，配套 `.config` 存在。✅ 值同样是未解析令牌。

### 抽查小结

- **内容与 CP 补丁一致**：抽查的 5 个资产，逐键值**逐字一致**（含未解析令牌）。✅
- **配套 `.config`**：231/231 齐全。✅（但个别 `.config` **形状不对** —— 见回包结果）
- **`ContentHashes.json` 补 key**：231 个全部补上。✅（但值为空串、60 个大小写不规范 —— 见第五节）

---

## 四、回包结果（Step 4）

### 全量回包

```bash
cd /d/XnbConverter/out_publish
./XnbConverter.Cli.exe pack -c -i "D:\sdv_merged_final" -o "D:\sdv_packed"
```

- **退出码 0**，耗时约 **1155 秒（19 分钟）**。
- 产出 **3783 个 `.xnb`，538 MB**。
- **2 个文件失败**（错误原文，来源工具日志）：

```
[ERROR] 文件名：D:\sdv_merged_final\Data\CustomNPCExclusions.config 无效的JSON文件格式:
  Could not convert string 'Kiwi' to dictionary key type 'System.Int32'.
  Create a TypeConverter to convert from the string to the key type object. Path 'Kiwi', line 2, position 9.
    at XnbConverter.XNB.Encode(String path)

[ERROR] 文件名：D:\sdv_merged_final\Data\ObjectContextTags.config 无效的JSON文件格式:
  Could not convert string 'Bladetail Sturgeon' to dictionary key type 'System.Int32'.
  Create a TypeConverter to convert from the string to the key type object. Path '['Bladetail Sturgeon']', line 2, position 23.
    at XnbConverter.XNB.Encode(String path)
```

**失败原因分析**：这两个都是**本工具新建的资产**。工具给它们复制的 `.config` 模板 `Readers` = `DictionaryReader<Int32,String>`（**整数键**），而实际数据是**字符串键**的字典（`{"Kiwi": "TownEvent TownQuest IslandEvent OtherEvent", ...}`）。回包器按 `.config` 的 `Readers` 编码，遇到字符串键无法转成 `Int32`，直接失败。

这正是计划里记录的**残余风险**的实物证据：

> `CopyConfig` 的形状匹配只到"主类型 + 首 entry 值类型"，**不解析 `.config` 里的 Reader 泛型**。

`Dictionary<Int32,String>` 与 `Dictionary<String,String>` 在工具的形状判据下**都是"对象 + 字符串值"**，无法区分，于是选错了模板。两个资产都**没有产出 `.xnb`**。

### 有界子集回包（本次额外补做）

为把"回包能否覆盖全部产出"这个问题**问到底**，另做一次只含 **231 个新建资产**（463 个文件）的子集回包：

- **退出码 0**，产出 **229 个 `.xnb`**，**同样那 2 个文件失败**。
- 即：**229/231 成功，2/231 失败（约 0.9%）**，失败集与全量回包一致。

**结论**：`已写入的数据绝大多数可以被回包器编码成 xnb`。第③层的必要证据之一（"回包成功"）**基本成立，但有 2 个已知反例**，且这两例的根因是**工具产出的 `.config` 形状错误**，不是回包器的问题。

### 回环验证（额外，用于支撑"编码无损"）

把回包出的 `.xnb` 再解包回 JSON，与产出树里的原始 JSON 对比：

| 资产 | 键数 | 值差异 |
|---|---|---|
| `Data/Festivals/winter28` | 120 / 120 | **0** |
| `Characters/Dialogue/Acorn` | 21 / 21 | **0** |

**两个样本回环无损**，说明对这两类资产，"JSON → xnb → JSON"没有信息损失。

> ⚠️ 回环无损**不等于**游戏实机可加载。它只排除"编码时丢字段/改类型"这一类问题，
> 排除不了"游戏按另一套 Reader/类型预期读取"或"资产名/路径对不上"的问题。
> **第③层仍然未验证。**

---

## 五、自查发现（计划外，供后续处理）

### 发现 1（重要）：新建资产上的条件被**静默丢弃**，条件补丁被当成无条件写入

**证据**：838 条已写入的补丁里，**248 条**的源 change 带 `When`：

| 判定 / 通道 | 条数 | 是否合理 |
|---|---|---|
| `Converted` + `Gsq` | 25 | ✅ 条件翻成了 GSQ 落点，合理 |
| `NeedsReview` + `Bake`（求值为真） | 2 | ✅ 烘焙成无条件，合理 |
| **`CreatesNewAsset` + `None`** | **221** | ⚠️ **条件被直接丢弃，无条件写入** |

**机制**（`src/SdvCpTool.Core/Judging/PatchJudge.cs`）：判定链在目标缺失时于 **L128–136** 直接
`return Verdict.CreatesNewAsset`，而条件的"blocker"降级只发生在**目标已存在**的两个落点分支里（后续代码）。
所以 `HasSeenEvent`、`Season` 这类**本可翻译成 GSQ 的**条件，在新建资产上**既不翻译、也不烘焙、也不标 NeedsReview**，
直接当不存在。

**实证**（抽查 3）：`Characters/schedules/Acorn` 的两条补丁，无条件版 `spring="2400 ...RSVHiddenWarp2..."`
和条件版 `spring="0 ...RSVTheHike..."`（`When: HasSeenEvent`），产物里**只剩条件版**。
对**没看过该事件**的存档，CP 会给 `2400...`，而本工具产出给 `0...` —— **行为与 CP 不符**。

**量化影响**：221/838 ≈ **26%** 的已写入补丁属于"条件被丢弃"。这不是计划里列出的已知限制。

### 发现 2：`ContentHashes.json` 的新 key 有三处问题

新增的 231 个 key：

1. **值全部是空串**（`"Characters/Dialogue/Acorn.xnb": ""`）。
   代码注释（`ContentHashesWriter.cs`）称游戏的 `DoesAssetExist` 只查**键**、不查值，故留空。
   **这一断言未经实机验证** —— 如果游戏某处用到值（如内容校验），空值会出问题。
2. **60 个 key 的大小写与磁盘文件不一致**：key 写作 `Characters/Schedules/Acorn.xnb`（大写 S，
   沿用了 CP 补丁 `Target` 的写法），而磁盘文件是 `Characters/schedules/Acorn.json`，
   原版 `ContentHashes.json` 也一律用小写 `Characters/schedules/`。
   若游戏的资产名查找是**序数大小写敏感**的，这 60 条 key 可能查不到。需实机确认游戏的比较方式。
3. 只对 `.json` 结尾的创建文件补 key（`.config` 被跳过，符合预期）。

### 发现 3：同一次运行里"写入失败"出现两个数

- 写入循环打印：`已写入补丁：838 条（写入失败 30 条）`
- 汇总打印：`写入失败：33`

原因：循环的 `failed` 只统计**进入写入循环后**失败的 30 条；另有 **3 条通道 C 烘焙求值失败**
（2×`HasSeenEvent` + 1×`Season`，非可烘焙令牌）在 `RunBake` 阶段就被标 `Written=false`，
没进循环，被汇总计入。**两个数都在数"写入失败"，标签却相同**，容易误读。

---

## 六、如实记录：做不到的部分

### 写入失败的 33 条（各自原因）

| 类别 | 条数 | 说明 |
|---|---|---|
| `Load` 的 `FromFile` 是 **`.png`** | 27 | 图片层，本任务只处理 `.json`（如 `Portraits/Philip.png`） |
| `Load` 的 `FromFile` 是 **`.tmx`** | 2 | 地图层，同上 |
| 通道 C **烘焙求值失败** | 3 | `HasSeenEvent`×2、`Season`×1 —— 不是可烘焙令牌 |
| **GSQ 翻译失败** | 1 | `Weather` 带 `island` 地点上下文，GSQ 的 `WEATHER` 首参表达不了 |

### 不可转换的 754 条（报告分类）

`NoLandingPoint 194` / `NonTranslatableCondition 152` / `NeedsRuntime 140` / `TargetMissing 245` /
`TargetContainsTokens 21` / `LocalEdit 2`。

### 明确的未实现 / 未验证项

- **`CopyConfig` 形状匹配不解析 Reader 泛型** —— 残余风险，本次已由 2 个回包失败**坐实**（占 231 新资产的 0.9%）。
- **值里的 `{{i18n:...}}` / `{{TargetWithoutPath}}` / `{{Random:...}}` 令牌原样写入、未解析** —— 抽查可见。
- **通道 B1/B2/B3**（语言后缀 / 季节 tilesheet / 昼夜瓦片）**未实现**。
- **地图 / 图片层**（`.tbn` / `.png` / `.tmx`）**未处理** —— 直接体现在 29 条写入失败上。
- **第③层"游戏能读到"完全未验证**：没有实机加载，没有进游戏。三件套齐全 + 回包成功 + 回环无损都只是**必要不充分**证据。
- 发现 1（新建资产条件丢弃）**未修**，只记录。

---

## 七、构建与测试

```bash
cd /d/SdvCpTool && dotnet build --no-incremental
```
```
已成功生成。
    0 个警告
    0 个错误
```

```bash
cd /d/SdvCpTool && dotnet test
```
```
已通过! - 失败:     0，通过:   383，已跳过:     0，总计:   383，持续时间: 1 s - SdvCpTool.Core.Tests.dll (net8.0)
```

---

## 八、结论

1. **产出量**：1664 补丁 → 910 判定可合并 → **838 已写入**（462 个新文件，231 个新资产，全部有 `.config`）。
   与 Task 7d 基线**逐项一致，无回归**。
2. **数据落盘可靠**：抽查 5 个新资产的**内容与 CP 补丁逐字一致**；`Load` + `EditData` 组合正确；三件套齐全。
3. **回包基本可行**：全量与有界子集回包**均以退出码 0 完成**，231 个新资产里 **229 个成功、2 个失败**；
   回环验证 2 个样本**无损**。
4. **但第③层未验证**：能否被**游戏**读到，没有实机证据，**不得**宣称"已可用"。
5. **新发现一个计划外缺陷**：新建资产上的 `When` 被静默丢弃（221 条），产出内容对条件不成立的存档会与 CP 不符。
6. **两个 `.config` 形状错误的资产**坐实了计划已记录的 Reader 泛型残余风险。

---

## 九、上述缺陷的修复结果（Task 8b / 8c / 8d）

### Task 8b：新建资产的条件不再被静默丢弃 —— 已修

**那 221 条的构成**（按目标前缀）：

| 前缀 | 条数 | 能否挂条件 |
|---|---|---|
| `Characters/Schedules` | 83 | ❌ 键是季节名 |
| `Data/Festivals` | 80 | ❌ 字符串字典，无 `Condition` 字段 |
| `Characters/Dialogue` | 47 | ❌ |
| `Strings/schedules` | 5 | ❌ |
| `Data/CustomNPCExclusions` | 4 | ❌ |
| `Data/Events/Custom_Ridgeside_ShiroHouse` | 2 | ✅ 键形如 `id/前置条件` |

**修法**：在 `CreatesNewAsset` 分支前插条件分流 —— 能挂的（2 条）走 `Channel.Gsq` 真挂；挂不上的（219 条）判 `NeedsReview` 并**给出原因、不写**。

**代价明确**：`written` **838 → 619（−219）**。**这是正确的** —— 宁可少写，也不能产出"条件没了"的假资产。

**验收证据**：`Characters/schedules/Acorn` 修复前是无条件写出条件版内容，修复后留下的是**真正的无条件补丁**（`idx 0`），符合 CP 语义。

> ⚠️ 记账问题：`ConvertibleCount` 把 `NeedsReview` 也算作"可合并"，所以"910 可合并"这个数字**掩盖了"其中 219 条本次不写"**。后续应区分口径。

### Task 8c：`.config` 模板匹配解析 Reader 泛型 —— 已修

**修前**：子集回包 `成功 0 / 失败 2` —— 两条 `Could not convert string 'Kiwi' to dictionary key type 'System.Int32'`。

**修后**：`成功 2 / 失败 0`，且**往返校验无损**（unpack 回来的 JSON 与写出数据完全相等）。

**做法**：解析候选 `.config` 的 `Readers[0].Type` 泛型实参（丢弃程序集限定名），与从被写数据推断的 `[键类型, 值类型]` 逐位比对；明确不一致的候选被排除（泛型分 −1），无泛型信息的退到原逻辑。

**A/B 回归验证**：`git stash` 后跑基线，与修复后全树 diff —— **只有 2 个文件不同，正是那两个 `.config`**，其余零差异。

**数据佐证**：游戏树里 `DictionaryReader` 泛型分布 `String,String` 2191 份、`Int32,String` 25 份 —— **键类型**是这里的判别关键。

### Task 8d：`ContentHashes` key 大小写 —— 核实为**无影响，不改**

60 个新 key 写成 `Characters/Schedules/`（大写 S），磁盘与原版清单是小写 `schedules`。

**核实依据**（源码）：

```csharp
// LocalizedContentManager.cs:143
_manifest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // ← 忽略大小写
// :359
return _manifest.Contains(item);
```

**`OrdinalIgnoreCase`，所以大小写不影响 `DoesAssetExist`。** 记录在案，不做无谓改动。

---

## 十、M3 最终状态

```
补丁总数      1664
判定可转换     910   （口径注意：含 219 条本次不写）
已写入数据     619
写入失败        33
新建资产       231   （.json + .config 各 231，ContentHashes key 各 1）
回包           全量 3779 xnb / 子集 2/2 成功
测试           393 全绿
构建           dotnet build --no-incremental → 0 警告 0 错误
```

**仍然未验证的（不许宣称已可用）**：

- **第③层"能被游戏读到"** —— 三件套齐全 + 回包成功 + 回环无损都只是**必要不充分**证据，**没有实机加载**
- `ContentHashes` 新 key 的值一律为空串（源码证实游戏只查键，但未实机）
- 值里的 `{{i18n:...}}` 等令牌**原样写入未解析**
- 通道 B1/B2/B3（语言后缀 / 季节 tilesheet / 昼夜瓦片）**未实现**
- 地图/图片层（`.tbin`/`.png`/`.tmx`）**未处理**
- 783 条不可转换（`TargetMissing` 245 / `NoLandingPoint` 194 / `NeedsRuntime` 140 等）
