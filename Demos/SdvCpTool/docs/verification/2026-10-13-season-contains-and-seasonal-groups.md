# 「季节怎么也丢」查到根：`Season |contains=` 的判据错了，顺带把四季图集组救回来

日期：2026-10-13
起因：业主问「季节怎么也丢」。前一轮只解释了**地图没有按季通道**那一半（
[`2026-10-13-map-tilesheet-reference-shape.md`](2026-10-13-map-tilesheet-reference-shape.md) §0、
[`../research/2026-10-13-runtime-conditioned-map-edits.md`](../research/2026-10-13-runtime-conditioned-map-edits.md)）。
这一轮把**能修的那一半**修了：不是政策问题，是**求值器读错了 CP 的写法**。

## 0. 判据（两条都是源码级，不是推测）

### 0.1 `Season |contains=A, B` 的语义

CP 的具名参数 `contains`（`ContentPatcher.Framework.Tokens/Token.cs:134-147` ⇒ `ApplyContains`）**不是**
"拿当前季节去比"，而是：

```csharp
bool value = invariantSet.Overlaps(parsed);
return InvariantSets.FromValue(value);      // ⇒ 令牌值折成 "true"/"false" **字符串**
```

再由 `Condition.IsMatch`（`Condition.cs:66`）拿它跟**声明值**求交集：

```csharp
IsMatch = token.GetValues(Input).Any(value => CurrentValues.Contains(value));
```

⇒ 所以 `"Season |contains=Summer, Fall, Winter": false` 的意思是
**「当前季节不在 {夏, 秋, 冬} 里」**（CP 官方文档 tokens.md 的例子 `"Year |contains=1": "false"`
注释就写着 `// NOT year 1`），**不是**「季节等于字面量 `false`」。
CP 还会校验：用 `contains` 时声明值必须是 true/false（`Token.cs:87-98` `TryValidateValues`），
否则整条补丁判失败。

### 0.2 游戏对**地图**没有按季通道（再次确认，这次是全库扫）

- `StardewValley.GameData.Locations/LocationData.cs` / `CreateLocationData.cs`：地图路径是**一个字符串**
  （`MapPath`），没有按季或按条件分叉的字段；
- 全库扫 `currentSeason + "…"`（`D:\sdv_src\StardewValley\*.cs`）：只出现在**节日**（`Data\Festivals\<季><日>`）、
  **邮件**（`Data\Mail` 的季节键）、**NPC 对话/日程**（`<季>_…`）、**图集**（`GameLocation.cs:16439`
  `GetSeasonalTilesheetName`）—— **没有一处是拼地图名的**。
- ⇒ 地图结构只能留一季（项目政策：春天）；图集有原生通道，所以**四季变体要整族落盘**（见 §2）。

## 1. bug ①：`Season` 的具名参数被丢了（400 处命中）

`Baker.EvalKey` 的 `"season"` 分支原来写 `EvalSeason(key.Value)` —— **`key.Arguments` 直接没传**：

```csharp
// 修前
case "season": return EvalSeason(key.Value);          // ← |contains= 被忽略
// 修后
case "season": return EvalSeason(key.Arguments, key.Value);
```

于是 `"Season": "Spring"` + `"Season |contains=Summer, Fall, Winter": false` 这种写法里，
第二个叶子被当成「"false" 是不是等于 "spring"」⇒ **假** ⇒ 整个 When 折叠为假 ⇒ 判
`NotApplicable`（"本就不适用"）⇒ 整条内容丢掉。全语料命中 **400 处**（`Season |contains=…` 作条件），
其中 **SVE 一家 365 处**（CommunityDay 的日程与台词，四位 NPC × 四季）。

修法：`EvalSeason(arguments, declaredValue)` 里先按 CP 语义把 `|contains=` 折成布尔，
再与声明值（必须是 true/false）比 —— **判据与配置令牌那条共用同一份实现**
（`ContainsValues` / `TryDeclaredBools`，`Baker.cs` 里一处定义、两处调用）。

测试（`BakerTests`）：SVE 那个写法成立、`|contains=Spring, Summer` + `true` 成立、
`+ false` 不成立、`Season: |contains=winter`（冒号写法）走同一条路、非布尔声明值判失败、
不认识的参数判失败；**1423 条全绿**（+3）。

## 2. bug ②：修好 ① 之后，**四季图集组被拆散**（树侧才看得见）

`SeasonalTilesheetPlanner` 挑候选时有一条过滤（`Program.cs` 里传的谓词）：
**只考虑"本来会被丢掉"的条件补丁**（`if (willWrite(v)) continue;`）—— 因为四季组是一条
**救援**通道：它把四季各一条的 `<asset>` 归一成 `<季>_<asset>` 四份文件 + 改地图里的图集指向
（走游戏原生的 `GetSeasonalTilesheetName`）。

修好 ① 之后，"春"那条变成**可直写**了（春天 ∈ {Spring} ✓）⇒ 被谓词挡在组外 ⇒
**凑不齐四季 ⇒ 整组不发射** ⇒ 实测树侧少 8 个文件
（`Maps/{spring,summer,fall}_Spring_z_extras.{png,config}` 等），只剩春天那份 ✗ ——
而四季组那条路才是**忠实**的（CP 里这张图就是按季换的）。

修法（两处，缺一不可）：

1. **四季组优先于直写**：分组时不再排除可直写的补丁（谓词恒 `false`）；
2. **被认领的补丁不再直写**：新增 `PatchVerdict.GroupOwned`（只标**真被组按季收走**的那些），
   写入侧的 `Writable` 认它。⚠ **不能**用现成的 `GroupEmitted` 挡 —— 那个是**报告口径**，
   同一张资产上的补丁都会被标上，**包括提供底图的无条件补丁**；实测按它挡的第一版（prof91）
   把底图也挡了 ⇒ **13 条真退化**（`Characters/Andy`/`Claire`/`Sandy`… 这些外观组的同 NPC 补丁）。

## 3. 受控对比（同参数，232 包全量）

```
python tools/convert-mods.py D:\tmp\prof89      ← 改前
python tools/convert-mods.py D:\tmp\prof92      ← 改后（① + ② 两处）
python tools/compare-runs.py        D:\tmp\prof89 D:\tmp\prof92
python tools/compare-trees-by-hash.py D:\tmp\prof89\tree D:\tmp\prof92\tree
```

| 侧 | 结果 |
|---|---|
| 报告 | 102 包相同 / 129 包有差异；`written` **15519 → 15583**、`writeFailed` 632 不变 |
| 判定 | 逐条配对：**变好 63**（`NotApplicable` ⇒ 应用）/ **"变坏" 10 条**（见下）|
| 产出树 | **只在 A：0**（无丢失）、只在 B：**24 个新文件**（四季图集多了 4 套）、内容不同 **142** |
| 142 个的构成 | `Strings/*.json` 75（SVE CommunityDay 台词）、`Maps/*.tbin` **40**（改指四季图集）、`Data/Events/Farm*.json` 12、`Characters/*.json` 10、图集 4 张、清单 1 |
| 测试 | **1423 条全绿** |
| 静态自检 | 11 项与 prof89 **一字不变**（含 **「季节性图集变体」0** —— 这一项正是会抓 §2 那种"四季变体丢了"的检查）|

⚠ 那「变坏 10 条」**不是丢失**，是**改打包**：同一张图上的"春"补丁从
`ConvertedNoCondition（直写）` 变成 `Emitted（并进四季组）`，
`compare-runs.py` 的 better/worse 只看 `written is True` ⇒ 把"不再直写"记成了变坏。
仲裁是**树侧**：`只在 A：0` + 只在 B 多出四季变体 ✓。这条口径局限值得记住
（`compare-runs.py` 头部本来就写着"报告只说明判定/写没写，不代表产出树相同"）。

## 4. 「季节到底丢了什么」—— 剩下的都**有理由**

把 232 包里**条件只有 `Season`** 的补丁（909 条）按目标分类，**改后**：

| | 条数 |
|---|---|
| 落地（`written`）| **207** |
| `NotApplicable` | **535**（改前按同一口径数是 **628** —— ① 冤枉掉的那批回来了；⚠ 改前的**落地数**没留下记录，别拿 207 去减）|

535 条的构成（逐条核过，**都是"季节确定化到春天"下的正确结论**，
理由串里写着 `Season = Winter` = 假 这种）：

| 目标 | 条数 | 是不是真丢 |
|---|---|---|
| `Mods/<命名空间>/…` | **195** | **丢了也无所谓**：那些资产由 DLL 框架（Remapping 之类）消费，纯 XNB 里没有消费者 |
| `strings/schedules/*`、`Characters/schedules/*` | **178** | ⚠ 不可表示：SVE 的四条 `CommunityDay_Lewis` 写的是**同一个键**、四季各一份台词 —— 静态树里一个键只能有一个值（**春天那份现在会落**，其余三季丢：一个键装不下四份）；日程键序（`NPC.TryLoadSchedule`：`<季>_<日>` → `<日>_N` → `<日>` → `<季>_<星期>` → `<星期>` → `<季>` → `spring_<星期>`）里**只有本来带季节维度的键**能承载它 |
| `Maps/*` | **38** | 地图没有按季通道（§0.2）⇒ 只能留春天（改前 72 ⇒ **春天那 34 条现在落了**）|
| 其它 | **124** | 逐条看：`Characters/Dialogue/*`（四季对话，同样"一键一值"）、`Data/Fish` 等条件条目 |

⇒ 一句话：**有原生按季通道的（图集 / 节日 / 邮件 / 日程的季节键）都落；
没有通道的（地图结构、单个数据键）留一季或丢** —— 而这一轮之前，
连"有通道的"也有一批被 ① 冤枉掉了。

⚠ 另一条**已被证伪**的猜想（记下来免得重做）：以为"日程的键自带季节 ⇒ 季节条件冗余 ⇒ 可以无条件写"。
抽到 `marriage_Mon` 这类**不带季节**的键就打脸了（`TryLoadSchedule` 里 `marriage_<星期>` 是会读的）
⇒ 只有 `summer_13` 这种**键本身就带那一季**的才冗余。全语料按这条能安全改写的只有 **8 条**（+2 条本就冗余）
⇒ 不值得为它加一套改写规则。

## 5. 实机验收

这批动了 **40 张地图对图集的指向**（改指 `<季>_<名>`）+ **4 套新的四季图集文件**，
正是以前出过 `FAIL` 的那一类 ⇒ 必须进游戏探。

装机方式：**只重打 `Maps/` 子树**（2495 个资产，回包 **成功 2495 / 失败 0**）后覆盖到
`D:\tmp\gv\Content`（上一轮验收用的那份），再 `gen-content-hashes.py` 重建清单
（**15 465 条 == 盘上 15 465 个 `.xnb`**，逐键核过 `spring_MovieTheater_TileSheet` /
`winter_VanillaCursors` 这些新资产都在）。
⚠ 这份复本**只覆盖探针会走的东西**（`Maps/` + 地点数据 + 音频）：本批另外改动的
`Strings/*.json`（SVE CommunityDay 台词）、`Data/Events/Farm*.json`、`Characters/*.json`
（日程/对话）共约 102 个文件**没有**进这份复本 —— 探针不检查它们（它建的是**地点与地图**），
所以验收结论不受影响；要一份"完全等于当前产出树"的装机得重打整树（含音频，约 48 分钟）。

不重打整树是因为**单文件打包与整树打包字节等价**已经证过
（`Maps/Mines/19`：mini-pack 与整树 packed88 的 md5 相同），而且这批的非地图差异
是 `Strings/*.json` 与 `Data/Events/*.json`（不影响地图加载）。

| 探针 | 结果 |
|---|---|
| 单图（改指的 8 张：`MovieTheater` / `Custom_DTZ_JojaHQ` / `Custom_DTZ_ApartmentLobby` / `Custom_Highway` / `Koda_Sub` / `MadDog.HashtagBearFam_Cellar` / `Custom_LK_TheaterBreakroom` / `Custom_LASV_BiankaDate`） | **PASS 8 / FAIL 0** |
| 全量 2495 张 | PASS **1245** / 贴图跳过 1249 / **FAIL 1** |
| 巡图 / 音乐 / 跨天 | 34 成功·4 失败 / 6 条 / 3 次（与上一轮一致） |

⇒ 唯一剩下的 `FAIL` 仍是 `Maps/EastScarp_Village` ← 上游缺 `Maps/spring_silo`
（没有任何包提供这个资产，补不出来）。**这一批没有引入新的失败**。

### 四季族**能不能被游戏按季取到**：这次也做了实机判据

「地图建得起来」只证明**当季**那一份在。换季能不能取到是另一条 —— 自检 mod 新增
`season-sheets.txt`（每行一个**完整资产名**，含季前缀）：对每一族用**游戏自己的两个 API** 核
`GetSeasonalTilesheetName(name, 季)`（换季规则）+ `DoesAssetExist`（查 `ContentHashes.json` 的键，
游戏判"存在"看的就是它，不看文件系统）：

```
# 四季图集族 `Maps/spring_Spring_z_extras`：四季四份都在（游戏按季取得到）
# 四季图集族 `Maps/spring_MovieTheater_TileSheet`：四季四份都在
# 四季图集族 `Maps/spring_MovieTheaterJoja_TileSheet`：四季四份都在
# 四季图集族 `Maps/spring_VanillaCursors`：四季四份都在
✗ 四季图集族 `Maps/spring_Nope_Missing_Sheet` 缺：spring → …、summer → …、fall → …、winter → …
```

最后那行是**对照组**（故意写一个不存在的族）：它报缺 ⇒ 这条检查**灵敏**，不是恒真。

⚠ 写这条检查时自己踩了一个假阴性：一开始只给**基名**（`Spring_z_extras`），
`DoesAssetExist` 拿它去查 `spring_Spring_z_extras.xnb`，而游戏实际请求的是
**`Maps/spring_Spring_z_extras`**（地图的图集引用相对地图那层目录解析）⇒ 盘上有文件也报缺。
所以清单文件里必须写**游戏会请求的那个完整名字**。
