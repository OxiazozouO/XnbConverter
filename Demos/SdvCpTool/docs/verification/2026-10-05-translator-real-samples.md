# M2 Task 6 验收：翻译器真实样本对账

> ⚠ **历史记录（命令已变）**：本文里的 `translate` / `--dry-run` / `--fast` / `--no-audit` /
> `--audit-cache` / `--append` 等开关**都已经删掉了**。现役只有一个命令：
> `SdvCpTool.Cli convert --pack <包> --tree <树> --out <输出> [--report] [--mods] [--content-hashes]`
> （"续写同一棵树"由 `--out` 是不是空目录**自动判断**，不再有开关）。照抄本文的命令会报错。

日期：2026-10-05
样本：Ridgeside Village 2.5.17（`[CP] Ridgeside Village`，235 个 content.json，1704 条补丁）
被验对象：`GsqTranslator` / `TokenTranslator`（`src/SdvCpTool.Core/Conditions/`）

Task 1–5b 的金标准样本都是手写的。这一步用真实 mod 数据验证，并首次给翻译器接上 CLI 出口
（在此之前 `src/` 里除自身外零引用，没有任何地方能跑到它）。

## 实际命令

```bash
cd /d/SdvCpTool
dotnet run --project src/SdvCpTool.Cli -- translate \
  --pack "D:\Stardew.Valley.v1.6.15\Stardew Valley\Mods\Ridgeside Village 2.5.17-7286-2-5-17-1731446698\Ridgeside Village\[CP] Ridgeside Village" \
  --report "D:\sdv_translate.json"
```

退出码 0，内容包解析零警告。

## 统计

| | 带 `When` 的补丁 | 成功 | 失败 | 成功率 |
|---|---|---|---|---|
| 修复前 | 856 | 525 | 331 | 61.3% |
| **修复后** | **856** | **530** | **326** | **61.9%** |

全部 1704 条补丁里 856 条带 `When`（50.2%），其余 848 条是无条件补丁，不在本任务范围。

⚠ 成败计数**不足以**说明修复量：修复后 530 条"成功"里，另有 **46 条的 GSQ 语义被纠正**
（`|contains=...: false` 曾翻反），成败数不变但输出完全不同 —— 见下面"实现缺陷"第 1 号。

## 失败分组（工具口径）

工具按"每条补丁**第一个**失败条件"归组 —— 一条补丁可能含多个翻不了的键，只报最先遇到的那个。

| 条数 | 原因 | 类别 |
|---|---|---|
| 143 | 自定义 mod 令牌 `X`，GSQ 无法静态求值 | **该失败** |
| 88 | 条件 `X` 依赖 mod 环境，GSQ 没有对应关键字（全部是 `HasMod`） | **该失败** |
| 33 | 条件 `X` 的值里嵌套了令牌，无法静态翻译 | **该失败** |
| 30 | 条件 `X` 不在 CP 内置令牌表里（内容包 DynamicTokens 自建或拼写有误） | **该失败**（已知 M2 缺口，见下） |
| 25 | 条件 `X` 是取值/计算类令牌，没有 GSQ 一对一等价物（`Query` 为主） | **该失败** |
| 5 | 条件 `X` 只能近似映射（`DayEvent` 3、`IsMainPlayer` 2） | **该失败**（刻意不自动翻译） |
| 1 | ``Weather`` 带了地点上下文 ``island`` | **该失败**（research §四.2） |
| 1 | 条件 `HasWalletItem` 在 GSQ 里没有等价关键字 | **该失败** |

**316 / 326 条失败的根因是"内容包侧的东西 GSQ 表达不了"**（自定义 mod 令牌、`HasMod`、
pack 自建的 DynamicTokens、`Query` 表达式、运行时才展开的 `{{...}}`），不是翻译器没实现。

## 失败补丁里各令牌的出现次数（按补丁去重）

| 出现补丁数 | 令牌 | 说明 |
|---|---|---|
| 130 | `HasMod` | 依赖别的 mod 是否安装 |
| 129 | `Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals` | mod 自己 C# 注册的令牌 |
| 99 | `HasSeenEvent` | **本身可翻译**，是被同补丁里别的键拖累 |
| 67 | `Rafseazz.RidgesideVillage/ExpandedFestivalMaps` | 同上 |
| 37 | `Rafseazz.RidgesideVillage/EasyIntroduction` | 同上 |
| 34 | `Query` | CP 的表达式求值令牌 |
| 27 | `RandomNumber` | pack DynamicTokens（`{{Random: 1..10}}`，**本身随机**） |
| 26 | `Time` | 值里嵌 `{{Range: a, b}}` |
| 23 | `IsOddYear` | pack DynamicTokens（`{{Query: {{Year}} % 2 = 1}}`） |
| 17 | `Relationship` | 被同补丁里 `Query` 拖累 |
| 15 | `Weather` | 13 条可翻译，1 条带 `island` 上下文，1 条被 `Query` 拖累 |
| 10 | `Season` / 6 `DayOfWeek` / 5 `Day` / 5 `HasConversationTopic` / 4 `HasFlag` | 均被同补丁里别的键拖累 |
| 5 | `HasValue` | 取值类令牌 |
| 4 | `Hearts` | 值里嵌 `{{Range: a, b}}` |
| 3 | `DayEvent`、3 `EnableBetterBusStop`、2 `RepeatCableCarCutscene`、2 `IreneTraveling`、2 `Year`、2 `SeasonalRSVMap`、2 `IsMainPlayer`、1 `HasWalletItem`、1 `PoleAtBackwoods` | — |

按"阻塞令牌集"看，最常见的组合是 `{HasMod}`（44 条）与 `{自定义令牌}`（37 条）；
**24 条失败补丁的全部键都是可翻译令牌**，它们的失败纯粹来自值侧（23 条嵌 `{{`）或
`Weather: island`（1 条）。这 24 条是失败侧唯一"可能还有实现缺陷"的候选池，逐条核过：没有。

**但"失败侧没问题"不等于"成功侧是对的"** —— 复查成功样本的语义时抓到了下面第 1 号缺陷
（`|contains=...: false` 被翻反，46 条）。结论：对账不能只看成败计数，成功样本的输出也要按
CP 语义逐条复核。

## 实现缺陷（Step 3 判定 → 已修）

四个缺陷里，前两个让翻译器**该翻没翻**（多救回 5 条：525 → 530），第三个让报告**说谎**
（不改成败），第四个最严重 —— 它让 46 条"翻译成功"的补丁**语义翻反了**，而且是静默的。

### 1. `|contains=` 的声明值 `false` 被当成占位符（46 条语义翻反）⚠

真实数据里 `|contains=` 出现 **589 次**，其中 **126 处声明值是 `false`**，落在 **46 条最终翻译成功
的补丁**上。旧实现把 JSON 值当占位符直接丢掉，于是

```jsonc
{ "HasSeenEvent: |contains=75160249": false }   // CP: 玩家**没**看过这个事件
```

被翻成了 `PLAYER_HAS_SEEN_EVENT Current 75160249`（**看过**）—— 语义正好相反。

CP 侧证据（三处独立）：
- `Framework/Tokens/Token.cs:209` `ApplyContains` 返回 `InvariantSets.FromValue(found)`，即 `true`/`false`；
- `Framework/Conditions/Condition.cs:110` `IsMatch = token.GetValues(Input).Any(v => CurrentValues.Contains(v))`
  —— 声明值就是拿这个布尔去比，`false` 只在"没有交集"时匹配；
- `Framework/Tokens/Token.cs:112-124` `TryValidateValues`：用了 `contains` 时声明值**只能是 true/false**；
- 官方文档 `docs/author-guide/tokens.md` §contains 的示例写着 `"Year |contains=1": "false"`，
  注释就是 `// NOT year 1`。

修法：解析声明值的布尔，`false` 时给整条 query 加 GSQ 的 `!` 前缀
（`GameStateQuery.cs:2166` 解析 `!`）；声明值不是布尔时**直接失败**（CP 自己也会拒绝这种内容包，
不能替它猜）。回归护栏：`RealSampleTests` 的 `HasSeenEvent |contains=...: false`，
以及 `GsqTranslatorTests` 的 `具名参数contains` / `contains的声明值必须是布尔`。

复跑校验：46 条含 `contains=false` 的成功补丁**全部**带上了 `!`，取反关键字分布为
`!PLAYER_HAS_SEEN_EVENT` 20、`!PLAYER_NPC_RELATIONSHIP` 10、`!PLAYER_HAS_MAIL` 8、
`!DAY_OF_WEEK` 3、`!PLAYER_HAS_DIALOGUE_ANSWER` 2、`!PLAYER_FARMHOUSE_UPGRADE` 2、
`!PLAYER_LOCATION_NAME` 2、`!WEATHER` / `!SEASON` / `!DAY_OF_MONTH` 各 1。

### 2. 令牌名大小写敏感（4 条）

真实数据写的是 `FarmHouseUpgrade`（大写 H，4 处），研究的对照表写的是 `FarmhouseUpgrade`，
`TokenTranslator` 的 `switch` 按大小写敏感匹配 → 判成"没有已实现的 GSQ 翻译规则"。

CP 侧证据：`Enum.TryParse<ConditionType>(name, ignoreCase: true)`
（CP 源码 `Framework/ConfigFileHandler.cs:108`、`Framework/ScreenManager.cs:280,336`）——
CP 的条件名**大小写不敏感**。

修法：`switch (name.ToLowerInvariant())`，标签全部小写；`ConditionVocabulary.Classify` 本来就是
`OrdinalIgnoreCase`，与之一致。回归护栏：`RealSampleTests` 里的 `FarmHouseUpgrade |contains=1`。

### 3. `Relationship` 缺 `Friendly` 类型（1 条）

真实数据：`{"Relationship:Alissa": "Friendly"}` → 判成"关系类型 `Friendly` 没有 GSQ 对应值"。

CP 侧证据：Relationship 令牌直接返回 `friendship.Status.ToString()`
（CP 源码 `Framework/Tokens/ValueProviders/VillagerRelationshipValueProvider.cs:44`），
SDV 的 `FriendshipStatus` 枚举是 `Friendly, Dating, Engaged, Married, Divorced`；
GSQ 的 `PLAYER_NPC_RELATIONSHIP` 同样接受 `Friendly`（`GameStateQuery.cs:1527`）。

修法：`Relationship()` 增加 `friendly` → `Friendly` 分支。回归护栏：同一条真实样本。

### 4. 失败理由归因错误（不改成败，改报告）

`MapPlayer(args)` 原本在按令牌名分派**之前**执行，于是任何 args 槽不是玩家键的令牌都会报
"玩家参数 `X` 无法映射到 GSQ"。真实数据里 23 条 `Query`（args 是表达式）、1 条
`Weather: island`（args 是地点上下文）被这样误报 —— 成败是对的，**理由是错的**，
会直接把 Step 3 的分类带偏（"Query 翻不了"被读成"玩家键不认识"）。

修法：
- 先 `ConditionVocabulary.Classify(name) != GsqTranslatable` 就返回，未实现的令牌不再走到玩家键解析；
- 把所有失败理由按 `ConditionVocabulary` 的分类细化（无等价物 / 环境依赖 / 自定义令牌 /
  只能近似 / 取值计算 / 不在 CP 令牌表里），报告因此能直接按类聚合；
- 只对 args 槽**确实是玩家键**的令牌解析玩家键（新增 `NonPlayerArgTokens`）；
- `Weather` 带参数时按"地点上下文表达不了"明确失败，不再误报成玩家参数问题。

## 修不了的部分（诚实记录）

| 缺口 | 影响面 | 为什么修不了 |
|---|---|---|
| pack 自建 `DynamicTokens`（本工具不读，research §五 已记） | 52 条失败补丁含这类键；其中 26 条以 `RandomNumber` 为唯一阻塞项、2 条为 `IsOddYear` | 即使实现 DynamicTokens 解析也救不回多少：`RandomNumber` 的值是 `{{Random: 1..10}}`（**本身就是随机**，静态不可能有值）；`IsOddYear` 是 `{{Query: {{Year}} % 2 = 1}}`，GSQ 的 `YEAR` 只有闭区间，表达不了奇偶。只有 `IreneTraveling`（按 `Season`+`Day` 条件化的多个定义）有被静态展开的可能 |
| `HasMod` / 自定义 mod 令牌 | 130 / 191 条 | 语义就是"另一个 mod 在不在"，纯数据形态的产物天然无法判定 |
| 运行时令牌（`{{Range}}` 等 88 条含 `{{`） | 23 条以它为唯一阻塞项 | `Range` 运行时才取值 |
| `DayEvent` / `IsMainPlayer` 近似 | 5 条 | research §四 明确定调"不当精确映射"，宁可不翻 |
| `HasWalletItem` 的 `KeyToTheTown` 例外 | 1 条（真实值 `RustyKey`，不是 `KeyToTheTown`） | research §二 说只有 `KeyToTheTown` 可映射；本内容包没有这种样本，不按想象实现 |
| `HasProfession` 一律失败 | **0 条** | 刻意失败（缺职业名→ID 映射表）。**本内容包、以及本机全部已装 mod 的 `When` 里 0 次出现** —— 该刻意路径没有被真实数据触达 |

结论：**修完上面这些实现缺陷后，326 条失败里没有一条属于"本该翻却没翻"**；同时 530 条"成功"
里被翻反语义的 46 条也已纠正。重复一遍：成功率 61.9% 不是翻译器弱，而是这个内容包的重头戏
（自定义令牌 + HasMod）恰好是 GSQ 原理上做不到的那类。

## 固化到 CI 的 20 条真实样本

`tests/SdvCpTool.Core.Tests/RealSampleTests.cs`：12 条成功形态 + 8 条"该失败"形态，
每条 `When` 逐字抄自 Ridgeside 的真实文件，注释标了来源文件。

- 成功形态覆盖：`Season`、`Day`、`DayOfWeek`、`Weather`、`HasSeenEvent`（`|contains=...: false`
  的取反）、`HasFlag`、`HasDialogueAnswer`、`HasConversationTopic`、`LocationName`、`Relationship`
  （含 `Friendly` 与 `Married→Married Roommate`）、`FarmHouseUpgrade`；
- 该失败形态覆盖：自定义 mod 令牌、`HasMod`、值里嵌 `{{`、DynamicTokens、`Query`、
  近似映射、GSQ 无等价物、`Weather` 带地点上下文；
- 回归护栏直接对应上面修的实现缺陷（`FarmHouseUpgrade` 大小写、`Relationship` 的 `Friendly`、
  `|contains=` 取反）。

失败用例同时断言理由片段（如"只能近似映射"），把 Step 3 的分类也钉住，避免理由文案悄悄退化。
`|contains=` 的两种声明值另外在 `GsqTranslatorTests` 里补了金标准用例（含"声明值必须是布尔"）。

## 与计划的偏差

1. **多改了 `docs/research/2026-10-05-cp-to-gsq-token-mapping.md`**（新增 §六，并在 §一 加了指引）。
   计划 Task 6 的 Files 清单没列它，但 Step 5 明确写了"若发现 research 文档不足，补文档"——
   `|contains=` 取反、大小写不敏感、`Friendly` 这三条正是 research 没写、又直接导致缺陷的事实。
2. **改了 `tests/SdvCpTool.Core.Tests/GsqTranslatorTests.cs`**（Step 4 的 Files 清单只列了新建
   `RealSampleTests.cs`）。原有一条 `contains时忽略JSON值` 断言的是"JSON 值是占位符"——
   那正是本次判定的错误语义，必须改掉，并补上取反与"声明值必须是布尔"的金标准用例。
3. **失败用例拆成两个 `[Theory]`**（`真实样本翻译成功` / `真实样本该失败`）。计划给的模板只有
   "whenJson → expectedGsq" 一种形状，装不下"该失败"的样本；用 null 哨兵更隐晦，故拆开。
4. **`translate --help`**：计划没提，但任务的验证清单要求该命令"能正常输出"，于是在
   `Program.cs` 顶部加了 `--help`/`-h` 分支（`convert --help` 顺带也有了）。
5. 计划的样本 JSON 里失败条目带 `reason`、成功条目带 `gsq`；实际实现给**每条**都写了
   `ok` 字段，成功才有 `gsq`、失败才有 `reason`，与计划示例一致。
6. **本任务只新增 20 条 CI 用例，但任务书预期总数是 210 + 20 = 230；实际是 234。**
   多出的 4 条在 `GsqTranslatorTests.cs`：`|contains=` 取反的金标准（3 条 InlineData）
   与"声明值必须是布尔"（1 条）。它们是修第 1 号缺陷时必须补的护栏，不属于"真实样本"，
   所以没有塞进 `RealSampleTests.cs` 充数。

## 未变的东西

- `convert` 主流程与 `PatchJudge` 未被触碰（`PatchJudge` 用的是 `WhenParser` +
  `ConditionVocabulary`，不经过 `GsqTranslator`）；复跑同一内容包仍为 1704 条补丁、退出码 0。
- M2 唯一新增的 CLI 表面就是 `translate`，M3 才把翻译器接进 `convert` 主流程。

---

# M2 Task 7 验收：GSQ 语法实跑验证

日期：2026-10-05
被验对象：`translate` 导出的 530 条成功译文（`D:\sdv_translate.json`）
验证器：`tests/GsqSyntaxCheck/`（独立控制台工程，**有意不进 `SdvCpTool.sln`**）

Task 6 的对账证明"译文语义对"，但都停留在字符串比对层面。这一步把译文喂给**游戏自己的**
`StardewValley.GameStateQuery.Parse`，用游戏代码证明这些字符串是它真能解析的。

## 结论

**实跑跑通了，没有降级，也没有为跑通做任何 hack。**

| 指标 | 数量 |
|---|---|
| 输入的成功条目 | 530（去重后 207 条不同字符串） |
| 解析通过（>=1 条 query 且无 `Error`） | **530** |
| 解析报错（`Error != null`） | 0 |
| 解析出 0 条 query | 0 |
| 调用抛异常 | 0 |

退出码 0。

## 怎么跑的

```bash
cd /d/SdvCpTool
dotnet build tests/GsqSyntaxCheck/GsqSyntaxCheck.csproj
dotnet tests/GsqSyntaxCheck/bin/Debug/net8.0/GsqSyntaxCheck.dll "D:\sdv_translate.json"
```

`GsqSyntaxCheck.csproj` 只声明了一个 `<Reference>`（`HintPath` 指向
`D:\Stardew.Valley.v1.6.15\Stardew Valley\Stardew Valley.dll`，`Private=true`）。
**没有**写 `AssemblyResolve`、**没有**反射绕过、**没有**改游戏目录下的任何文件。

一个反直觉的点：输出目录里除了 `Stardew Valley.dll`，还自动出现了
`MonoGame.Framework.dll`、`xTile.dll`、`Lidgren.Network.dll`、`StardewValley.GameData.dll`、
`SkiaSharp.dll` 等一整组游戏依赖。这不是我们手动加的 —— MSBuild 的
`ResolveAssemblyReference` 会读取被引用程序集的元数据，并在 `HintPath` 同级目录里
自动探测、复制它的依赖闭包。所以"依赖加载不通"这个计划里预留的风险**没有发生**：
MSBuild 用标准机制替我们解好了。程序集目标框架是 `net6.0`，在 `net8.0` 宿主里加载正常。

程序默认读 `D:\sdv_translate.json`；若传的是非 `.json` 文件，则按"每行一条 GSQ"的纯文本模式读
（用于下面的人工反例）。退出码：全部通过 0，否则 1。

## 这个验证证明了什么、没证明什么

`GameStateQuery.Parse` 的职责是**把原始串切成 query 并确认每个 query 键有解析器**（含别名、
大小写不敏感、`!` 取反前缀、按逗号切顶层、按引号感知切参数）。所以"530 条全过"意味着：

**证明了** ——
- 530 条译文里每个 query 键都被游戏认识（含 11 个键全部命中 `DefaultResolvers` 的 115 个
  默认键/别名表），没有拼错的键、没有用错别名；
- 17 条 `!` 取反串（Task 6 修掉的 `|contains=...: false` 缺陷产生的正是这一类）游戏解析器
  接受，取反位被正确识别；
- 45 条逗号多 query 串被正确切成多段，没有哪一段因拼接问题变成空段或粘成一坨。

**没证明**（诚实边界）——
- **参数个数与参数取值**：`Parse` 不校验 arity 和参数合法性，那发生在 resolver 求值阶段、
  需要真实 `GameStateQueryContext`（要一个跑起来的 `Game1` 世界）。所以"过 Parse"**不等于**
  "语义/参数一定对"。参数级正确性目前只由 Task 1–6 的金标准单测与 Task 6 的人工对账背书。
- **引号感知切分**：本数据集 530 条译文里**没有一条**含引号值（207 条不同串里 `"` 出现 0 次），
  所以 quote-aware 分支没被真实数据覆盖到。

## 验证器有牙齿（负对照）

"全过"必须有反例证明检查器不是恒真。用一份故意写坏的纯文本探针跑一遍：

```
输入 8 条：
THIS_IS_NOT_A_QUERY                       → ERR: 'THIS_IS_NOT_A_QUERY' isn't a known query or alias
SEASON Spring, BOGUS_KEY x                → ERR: 'BOGUS_KEY' isn't a known query or alias
WEATHER Here Sun Wind                     → 通过
!WEATHER Here Sun Wind                    → 通过（取反位识别）
SEASON                                    → 通过（无参数也能过 Parse，见上面"没证明"）
PLAYER_HAS_MAIL                           → 通过（同上）
season spring                             → 通过（键名大小写不敏感）
SEASON Spring, WEATHER Here Sun Wind      → 通过（多 query）
```

结果：通过 6、解析报错 2、0 条 0、抛异常 0，退出码 1（非零即失败）。检查器能抓未知键，
且大小写/取反/多 query 三种合法形态都不会被误报。

## 与计划的偏差

1. **喂的是 530 条，不是计划 Step 3 说的"Task 6 里那 20 条"。** 530 条是那 20 条的严格超集
   （20 条是手工挑的样本，530 条是全量成功译文），覆盖面更大、更省事，故全量跑。
2. **`dotnet new console -f net8.0` 不支持**：机器默认 SDK 是 10.0.300，其 console 模板只提供
   `net10.0`/`net7.0`。于是用默认模板建工程，再把 `csproj` 的 `TargetFramework` 手改成
   `net8.0`（与仓库其余工程一致，也被根目录 `Directory.Build.props` 约束）。产物仍是计划要求的
   独立 `net8.0` 控制台工程。目录名：计划写 `Tests/`（大写 T），但仓库已有小写 `tests/`，Windows
   文件系统大小写不敏感，实际落在既有的 `tests/` 下，git 记录的路径是 `tests/GsqSyntaxCheck/`。
3. **csproj 里只有一条 `<Reference>`，但输出目录带出了整组游戏依赖 DLL**：见上文，是 MSBuild
   自动解依赖，不是我们加的，也不算 hack。计划预判的"MonoGame/xTile 加载不通"因此没触发，
   白名单降级方案**没用到**。
4. 计划的 Step 4 说"把结果并入对账文档"——本文件即那份对账文档，故结果追加在此，未另建文件。
