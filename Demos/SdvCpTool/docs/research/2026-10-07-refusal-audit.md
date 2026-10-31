# 拒绝理由实地复核：头几大桶到底是不是真的「原版没通道」

日期：2026-10-07　　语料：32 个 CP 包 / 6743 条补丁（Task 49 之后那一轮）

**动机**：用户要求「别在认真考察之前就下『不可解析』的结论」。
本轮把**未写入**的补丁按理由聚合，取最大的几桶，**逐桶回到 1.6.15 的源码里核**。
结论先说：**头几桶都站得住**，没有发现错判；顺带挖到两条以前没记的机制（第 2、5 节）。

## 0. 当前分布（`tools/report-reasons.py D:\tmp\c10`）

```
   504  条件化的地图改动在原版没有通道（图块级只有 ConditionalDoor，且不适用于此）
   381  资产 X 的条目键名编码了条件维度，但补丁条件不被键名完全蕴含，需人工确认落点
   312  目标 X 不在资源树中，也不是原版资产；EditData 会新建它，需确认新资产名可达
   228  资源树中找不到资产 X
   220  ★ 已由集合发射产出（不是逐条写入）
   164  资产 X 虽有键名编码的维度，但本条改的不是条目（TextOperations/Fields），条件无处安放
   163  新建资产 X 带条件，但该资产类型的条目无法承载条件（落点 Unknown）
   155  资产 X 是图片 …… 本条没被任何可用机制归入
    93  值里含无法静态解析的 CP 令牌（Task 48/49 的闸）
    54  新建资产 X 带条件 ……（落点 DialogueKey）
    38  X：X 这个位置不接受 Condition（原版在该位置不读条件字段）
    35  Load 带条件时只能靠新增资产名区分，需要人工确认命名方案
    32  X 是别的 mod 注册的自定义资产（命名空间）
    23  条件含纯计算令牌，参数能否离线内联需人工确认
   22/21/20/16  对话键落点：$query 内联不成立（4 类）
    18  新建资产 X 带条件 ……（落点 KeyEncodedVariant）
    14  Target 含令牌
```

## 1. 504 条 · 条件化的地图改动

沿用前几轮的枚举结论（`DefaultActions` 26 个 action 无一触碰图块；
`GameLocation` 里 231 个 `case "…"` 字面量中与 tile/map/条件相关的只有 `ConditionalDoor`）。
本轮**没有**推翻它。

## 2. 381 条 · 「键名编码了维度但条件不被蕴含」——**占大头的是日程**

分布：`strings/schedules` 158 + `characters/schedules` 104 + `Characters/schedules` 61 + `Strings/schedules` 7
= **330/381（87%）**；其余是 `Data/Festivals` 38、`StringsFromCSFiles` 7、`Data/animationdescriptions` 5、`Data/Mail` 1。

**实地：`NPC.TryLoadSchedule()`（`NPC.cs:5755-5902`）的键查找顺序**（非已婚分支）：

```
GreenRain（仅 `绿色雨 && year==1`） → 被动节日 `<id>_<day>` / `<id>`
→ `<season>_<day>`            ← ★ 日程作者几乎都用这个键
→ `<day>_<好感档>` → `<day>` → `bus`（Pam）
→ 若下雨：`rain2`（50% 随机）→ `rain`     ← ★ 雨键在 season_day **之后**
→ `<season>_<星期>_<档>` → `<season>_<星期>` → `<星期>_<档>` → `<星期>`
→ `<season>` → `spring_<星期>` → `spring`
```

**结论**：日程键的维度只有 **季节 / 日 / 星期 / 好感档 / 被动节日 / 绿雨**，**没有 weather、没有 year、没有事件位**。
而且 `rain` / `rain2` 排在 `<season>_<day>` **后面** —— 只要 `<season>_<day>` 存在就永远轮不到雨键。
所以「同一天按天气二分」（语料里最常见的那一档：`When:{Weather:"rain, storm"}` 与 `{Weather:"sun, wind"}`
各写一条 `spring_14`）**用原版钥匙串表达不了**。⇒ **无通道**，拒绝对。

（`GreenRain` 是唯一的例外：它带 `year == 1` 的门槛，但那是硬编码在 C# 里的，不是数据能给的条件位。）

### 2.1 补记：日程**值**本身也是一台条件机（原先只查了键）

`NPC.parseMasterScheduleImpl`（`NPC.cs:5447-...`）把日程值（`/` 分隔的命令串）当**程序**跑，
**头一条命令可以是条件**：

| 头命令 | 语义 |
|---|---|
| `GOTO <键>` / `GOTO season` / `GOTO no_schedule` | 整条日程改为去取另一个键（`season` 解析成当前季节）；`no_schedule` = 今天不排 |
| `NOT friendship <NPC> <好感> [<NPC> <好感> …]` | 任一列出的 NPC 好感 ≥ 门槛 ⇒ **改去取 `spring`**；否则跳过这一条 |
| `MAIL <邮件ID>` | `num = 1`（没收到该邮件）或 `2`（收到了）—— 于是 `MAIL <id>/<A>/<B>/<其余…>` 的两个分支分别是 `array[1]` 与 `array[2]`，**其余命令共享** |

**对本桶结论的影响**：这三条都**不是**通用条件位 —— `GOTO` 无条件，`NOT friendship` 只看好感，
`MAIL` 只看邮件，且只能二选一地分**头两条命令**。语料里这一桶的条件是
**`Weather`（rain/storm vs sun/wind）与 `Year`**，两条都落不到这三条上 ⇒ **拒绝仍然对**。
（顺带说明为什么「雨天键」也不行：`rain`/`rain2` 排在 `<season>_<day>` **之后**，
只要那个键在就永远轮不到；要让它轮到就得删掉 `<season>_<day>`，而那样晴天就落到
`<season>_<星期>`/`<season>`/`spring` 上 —— 那是**别的**日程，不是作者想要的晴天版本。）

## 3. 312 + 228 条 · 目标不在树里 / 不是原版资产

抽样：`Maps/Custom_ForestWest`(32)、`Maps/Farm_FrontierFarm`(16)、`Maps/Spring_z_extras`(15) …
按包：**SVE 季节性服装 96 + SVE 81 + 野生动物 63**。

实地：SVE 本体**确实**有 `{"Action":"Load","Target":"Maps/Custom_ForestWest","FromFile":"…ForestWest.tbin"}`
且**写成功了**（报告里 `Converted / written=True`）。季节性服装包**单独**转换时这些地图不存在 ——
它依赖 SVE 另一个包。⇒ 这是**跨包依赖**，逐包转换时无误。跨包链式转换先前量过（净 +1，见 plan）。

## 4. 220 / 155 条 · 图片

「已由集合发射产出」那 220 条**不是失败**，是信息（四季 BigCraftable 索引 / 图集季节前缀 / `Appearance[]`）。
剩下 155 条是**找不到消费方**的图片（`LooseSprites/Cursors` 之类）：像素/图块没有条件位。⇒ 无通道。

## 5. 164 条 · `Data/Festivals` 的 `TextOperations`

样本形态（SVE）：给 `Data/Festivals/spring13` 的 `Set-Up_additionalCharacters` **追加一个 NPC**，
条件是 `HasSeenEvent 6665293` / `Spouse: Lance` / `Roommate: Apples` / `GuntherUnlocked`。

**实地两点**：
1. 节日数据的字段是用 `Event.TryGetFestivalDataForYear(key, …)`（`Event.cs:4417-4447`）取的。
   它**只认 `{key}_y<n>` 这一族后缀**，而且是**循环**语义：
   `num = 最大的 n`，`取 (year % num == 0 ? num : year % num)`。
   ⇒ 能表达「周期」（2 个变体 = 奇偶年），**表达不了**「第 2 年起永远」。
   硬要用只能铺 `_y2 … _y30` 再回绕，**有边界**（第 31 年回到第 1 年的值）⇒ **不采用**，记在这里。
2. `Set-Up_additionalCharacters` 的值会过 `Event.ParseCommands`（`Event.cs:3307-3329`）——
   也就是 **gender block + `[Token]`**。但游戏的 27 个 token 里**没有**「是否见过某事件」/「配偶是谁」
   /「NPC 名」这类（`TokenParser.cs` 的 public static 解析器清单）。⇒ **无通道**，拒绝对。

## 6. 163 / 54 / 18 条 · 新建资产带条件（落点 Unknown / DialogueKey / KeyEncodedVariant）

新建资产要**条件化地存在**，而资产名是游戏按固定名字取的 —— 没有条件位。⇒ 无通道。

## 7. 38 / 33 条 · `Data/Objects` 的条目级 `Condition`

工具的依据是全库实测：`Data/Objects` **只在 `GeodeDrops[]` 下有** `Condition`（全库 11 处），
条目级没有（`PostgresCondition` 的代价见 `ConditionPlacementAudit` 的注释）。本轮复核不推翻。

## 8. 23 条 · 「纯计算令牌」

拆开看是两类：
- **20 条**（孩子剧情）：`Query: '{{TheMightyAmondee.CustomTokens/Child:childindex=0|daysold}}' >= 16`
  —— **别的 mod（DLL）提供的令牌**，参数 `childindex/daysold` 的语义只在那份 DLL 里。
  即便读得出语义，它返回的是**玩家存档状态**（第 0 个孩子多少天大），GSQ 没有对应关键字
  （`PLAYER_HAS_CHILDREN` 只给个数）。⇒ 代不了。
- **3 条**（SVE）：`Query: {{SkillLevel:Mining}} >= 7` —— **这一条原版有等价物**：
  GSQ `PLAYER_MINING_LEVEL <player> <min> [max]`（`GameStateQuery.cs` 的 `PlayerSkillLevelImpl`：
  `num >= minLevel && num <= maxLevel`）。工具已有 `SkillLevel → PLAYER_<技能>_LEVEL` 的映射，
  缺的只是**把 `Query` 表达式里的比较运算拆成 min/max**。
  ⇒ **可做但只值 3 条**（全语料 `Query` 一共 23 条），**本轮没做**。

## 9. 未确证 / 边界

1. 上表**只核了头 8 桶（约 1900 条里的 1600+）**；剩下的零头（合计约 200 条）**没逐条核**。
2. 所有「无通道」的结论**都是静态读源码**得出的，**没有**装进游戏实测（没有可用的运行时环境）。
3. `Query` 表达式的通用翻译（不只是 `SkillLevel`）**没做**，也没估过全语料到底有多少种形状 ——
   本轮只看到 `>=` 一种。

## 10. 补记（2026-10-07 晚）：那道「代不掉的令牌」闸，现在**说得出卡在哪个令牌上**

原先理由只有一句话，看不出到底是 `{{Random}}`（值本身合法，只是每次不同）还是别的 mod 的
C# 令牌（根本不在我们能查的范围）—— 两者该不该放行是**完全不同的决定**。
现在把**残留令牌名**写进理由（`TokenScan.Unresolved` + `ValueTokenSubstitutor.UnresolvedTokens`）。
**受控对比 c20 → c21：全部计数 +0**（纯报告改动），于是 112 条按令牌名分档：

| 残留令牌 | 条数 | 能不能代 |
|---|---:|---|
| `Random` | 21 | 代不了（运行时随机） |
| **`HasMod`** | **13** | **能代**（已装模组索引在本机）→ 本轮做掉 |
| **`ModID` / `modID`** | **6** | **能代**（CP 令牌名**大小写不敏感**，我们只认精确的 `ModId`）→ 本轮做掉 |
| `Spouse` / `Baby1Gender` / `Baby2Gender` / `WhenSpouseIs*` | 19 | 代不了（玩家存档状态） |
| `PlayerName` / `ObjPro` | 3 | 代不了（运行时；`@` 那条通道见另一份文档） |
| 别的 mod 的令牌 / `InternalAssetKey` 等 | 其余 | 代不了 |

**两处都改掉之后（Task 55）**：c21 → c22 **已写入 3545 → 3564（+19）**、`NeedsReview` −19，
其余判定逐项相同。端到端核过：`{{ModID}}_RemoveConfigFlags` 在产出里成了
`Cornucopia.MoreCrops_RemoveConfigFlags`（该包 UniqueID 正是 `Cornucopia.MoreCrops`）。

两条依据都写在代码旁：
- `HasMod` 注册成 `ImmutableValueProvider("HasMod", installedMods)`（CP `TokenManager.cs:220`），
  带 `|contains=X` 时 `Token.GetValues` 把值集折成单个 `true`/`false`（`Token.cs:154-`）——
  实测用途是 SVE 在 `Data/WorldMap`/`Data/Characters` 的**值**里写游戏数据的 GSQ：
  `"Condition": "!{{HasMod |contains=flashshifter.GrandpasFarm}}"`；
- CP 的令牌表是 `InvariantDictionary`（大小写不敏感）⇒ `{{ModID}}` 就是 `{{ModId}}`。

## 11. 补记：「时段相关的 `MapProperties`」查过、**不成立**（免得以后重查）

504 条条件化地图改动里，唯一看着像有原版通道的一档是**改地图属性**（14 条），
典型是 SVE 改 `Maps/Custom_CastleVillageOutpost` 的 `AmbientLight`：

```
When {"Time": "{{Range: 600, 1700}}"}  → MapProperties {AmbientLight: "50 50 50"}
When {"Time": "{{Range: 1710, 1900}}"} → {AmbientLight: "90 90 90"}
When {"Time": "{{Range: 1910, 2100}}"} → {AmbientLight: "120 120 120"}
When {"Time": "{{Range: 2110, 2200}}"} → {AmbientLight: "145 145 145"}
```

**实地**：原版确实有时段相关的环境光通道，但只有**两个点** ——
`GameLocation.setUpLocationSpecificFlair`（`GameLocation.cs:6139-6155`）读 `AmbientLight`（白天）
与 `AmbientNightLight`（夜晚；缺省时夜晚 = 白天色），再由 `_updateAmbientLighting`（`:7154-7176`）
在 `getTrulyDarkTime` 前 **120 分钟**里做**线性插值**。

⇒ 表达得了「两点 + 平滑过渡」，**表达不了**作者要的**四段阶梯**（且阶梯的切换时刻也不同）。
把四个值折成两点是**近似**，属于改语义 —— **不采用**，如实记在这里。

（顺带核过：`Condition` 里的 `!` 与 `true`/`false` —— 见 §12。）

## 12. 补记：把「我们自己的发明」对着**真消费者**核一遍

产出里的条件字段不是摆设，游戏得真的读。两处核对：

1. **`Appearance[]`（NPC 立绘/换装）**：`NPC.ChooseAppearance`（`NPC.cs:918-1010`）读
   `Data/Characters` → `CharacterData.Appearance[]`，逐条看
   `Precedence`（**小的赢**，`> 当前最优` 的直接跳过）、`IsIslandAttire`、`Season`（可空=任意）、
   `Outdoors`/`Indoors`、**`Condition` → `GameStateQuery.CheckConditions`**、`Weight`（并列时**加权随机**）。
   ⇒ 我们发射器的目标格式**对得上**；而且 `AppearancePlanner` 早写下了关键那条：
   **`Precedence` 必须各不相同**，否则「全部并列 ⇒ 走加权随机」——CP 的语义是「后写的赢」，
   所以越靠后的补丁 `Precedence` 越小。
2. **GSQ 关键字大小写**：`GameStateQuery.QueryTypeLookup` 是
   `StringComparer.OrdinalIgnoreCase`（`GameStateQuery.cs:1955`），且原版数据里 **82 处** `!KEYWORD`
   写法 ⇒ 我们把 `!{{HasMod …}}` 代成的 `!false`/`!true` 是**合法 GSQ**（先前没验，现在验了）。

## 13. 补记：把「我们写出去的字段」也对着真消费者核一遍

上一轮核的是「CP 的输入模型我们读全没有」；这一轮核**输出侧**——凡是我们**自己造的**结构，
都要能在游戏源码里找到读它的那行：

| 我们写的 | 游戏在哪读 | 结论 |
|---|---|---|
| `Data/Locations` 的 `CreateOnLoad.MapPath`（Task 64 新加） | `Game1.cs:7363` `value.CreateOnLoad`、`:7390` `CreateGameLocation(key, CreateOnLoad)`；原版 `Data/Locations.json` 里 **91 处** | ✅ |
| 地点的 `FormerLocationNames` | 原版 `Data/Locations` 里就是字段（91 处） | ✅ |
| `Appearance[]` 的 `Precedence`/`Condition`/`Season`/`Outdoors`/`Indoors`/`Weight`/`IsIslandAttire` | `NPC.ChooseAppearance`（`NPC.cs:918-1010`） | ✅（§12） |
| 图集**季节前缀**改名 + 改地图 `TileSheet.ImageSource`（Task 30） | **`GameLocation.GetSeasonalTilesheetName`**（`GameLocation.cs:16440-16448`）：文件名以 `spring_`/`summer_`/`fall_`/`winter_` 开头时，把前缀换成**当前季节**再 `LoadTileSheet`（失败会**回退**原名） | ✅ |
| 四季 BigCraftable 的 `Name` 带 `Seasonal` + 4 连索引 | `Object.cs:1940-1943`（先前核过） | ✅ |

**顺带否证一个看着像通道的东西**：`GameLocation.ApplyMapOverride(override_map, override_key, …)`
（`:1663`）能在运行期叠加整张地图，但它是**给游戏自己的 C# 调用的 API**，
**没有数据侧入口** ⇒ 不能当通道用 ✓（这条记下来免得以后重查）。

**没变的东西**：本轮**没有代码改动** —— 核的都是已经实现、先前没逐行验过的机制。

## 14. `TargetField` 落在**数组**上时：`Entries` 通、`Fields` 不通（量过了，只有 1 条）

CP 的 `TargetField` 是把 `Entries`/`Fields`/`MoveEntries` 指到**子树**上
（`["SeedShop", "Items"]` = `Data/Shops` → `SeedShop` → `Items`）。子树的**根可以是数组**，
而 CP 对数组用的编辑器是 `ListKeyValueEditor`（`Framework/Patches.EditData/`）：
键的解析是 **`#<下标>` 走位置、其它走元素的 `Id` 字段**（`ParseKey` / `GetAssetKeyFunc`），
`CanMoveEntries = true`。

**语料用量**（32 包）：

| 字段 | 条数 |
|---|---:|
| `Entries`（+`TargetField`） | **699** |
| `MoveEntries`（+`TargetField`） | 33 |
| `Fields`（+`TargetField`） | **1** |

**实测结论**：`Entries` 落在数组上**我们是通的**（`新武器` 那条 `Data/Shops/AdventureShop/Items`
判 `ConvertedNoCondition` 且 `written=true` ✓）；**只有 `Fields` 落数组这一条不通**
（报「Fields 只能作用于对象」），因为 `Fields` 那条路径还没有数组分支。
⇒ **不为 1 条改代码**，但把 CP 的键语义（`#下标` / `Id`）记在这里，
以后要补的时候照着 `ListKeyValueEditor` 写就行。

## 15. **链式**（真实用法）下的资产级缺口：390，逐类都已核过

`tools/convert-mods.py` 按依赖定序把 32 包转进**同一棵树**（顺序：SVE 第 11 位，所有依赖它的在其后）。
用 `tools/asset-coverage.py` 量：

```
涉及到的 (包, 资产) 对：3943
  已写到：        3541
  由集合发射覆盖：66
  缺口：          390          ← 逐包单独转时是 451
```

缺口前几类（**都已在 §1-§14 里逐条核过**，不是新账）：

| 条数 | 类别 | 结论 |
|---:|---|---|
| 99 | `烘焙求值失败：HasFile 的路径里还有未展开的令牌` | SVE 季节性服装的多目标 + `{{TargetWithoutPath}}`。**量过两次**（单独转 +1 资产、链式 +0），**换不来内容** |
| 37 | 四季图集组未发射 | 目标多半**不是图集**（`LooseSprites/*`），或组里混了没给 `Season` 的成员 ✓ |
| 35 | 值里含代不掉的令牌 | 运行时值（`Random` 之外的存档状态）✓ |
| 31 | 条件化的地图改动 | 图块级无通道（§1）✓ |
| 24 | 键名编码维度的其余部分 | 日程/节日的 `HasSeenEvent`/`Weather`（§2）✓ |
| 21 | 新建资产带条件·落点 Unknown | **逐条看了**：写的是 `Mon` 这类**星期键** + `Season` 条件 ⇒ 键不蕴含条件、也没有条目级 `Condition` ⇒ **拒得对**（"键即条件"的重写在更早一步已经跑过，能转的早转了）✓ |
| 13 | 资源树中找不到资产 | 链式已把这一档从 73 降到 13（剩下的都是别的 mod 的 DLL 资产）✓ |
| 10+9+8+7 | Load 带条件 / FromFile 令牌 / 计算令牌 | 见 §8、§10 ✓ |

**⇒ 这一档没有「未考察的洞」了**：剩下的每一条都能指到上文的某一节。
以后要再往上走，得**新开通道**（例如把 `HasFile` 的逐目标求值 + 多目标拆分一起做，代价是补丁条数不可比），
而不是继续在既有理由里找漏网的。
