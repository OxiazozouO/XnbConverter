# 两个实机 bug 的根因与修复：令牌的**具名参数**、Target 里**代不掉但可枚举**的令牌

日期：2026-10-15

两条都是**实机报错追到源头**的，不是清查出来的：

| 实机现象 | 根因 | 归因 |
|---|---|---|
| `NPC 'RaccoonSBV' failed to parse master schedule 'Mon'`；一批 NPC 对白把几句话粘在一起（`…$1++河豚！…`） | `{{Random: A ++ B \|inputSeparator=++}}`：我们**只按逗号切**、又不认具名参数，把"整串（含分隔符与参数本身）"当成**一个备选**写进产出 | **我们的** ⇒ 已修 |
| 「农场地图传送没合并进来」：玩家农场图上那条便捷通道**任何农场类型都没有** | `"Target": "Maps/{{Farm}}"`：令牌按**农场类型**分块（8 个原版农场 + 2 个自定义），我们静态代不掉 ⇒ 判 `TargetContainsTokens`、**整条不写** | **我们的** ⇒ 已修 |
| `Location 'EastScarp_Village' failed to load seasonal asset name 'Maps\spring_silo'` + 9 条 NPC 日程失败 | `[CP] Rodney O'Brien` 的 `FromFile` 字符串里**多打了一个逗号**（同名文件里 `SiloYri` 那份没有） ⇒ 那个 Load 找不到文件 | **包自己的** ⇒ 改了包 |

## 1. 令牌的具名参数（`DynamicTokenResolver`）

CP 的 `Random` 支持 `|inputSeparator=<分隔符>`（声明备选之间的分隔符，默认才是逗号）与 `|key=<名>`
（运行时"这次掷点用哪个键共享"）。我们原来：

- **不认分隔符** ⇒ `A ++ B |inputSeparator=++` 整个串当成一个备选（见上表）；
- 而**不认就拒**又太狠：`|key=` 对「确定化取第一个」**没有影响**，拒了会让
  `assets/animals/{{Random: Fox_Red, …, |key=foxes}}.png` 整条 Load 被闸挡住 ⇒ **4 个资产不写**。

**修法**：末尾的 `|名字=值` 可以有多个，**逐个剥**（剥的时候只看**花括号顶层**的 `|` ——
用 `LastIndexOf('|')` 会把内层令牌从中间截断成 `{{X`，词法器直接报
`Reached end of input, expected '}}'` ⇒ 那是**致命错误**，实测让 `[CP] Custom NPC Lani` 的
71 个资产一个都没写出来）；认 `inputSeparator`（照做）、认 `key`（忽略），**其余一律返回 false**（不猜）。

## 2. Target 里代不掉但**取值可枚举**的令牌（新增 `CandidateTargetExpansion`）

与仓库里已有的 `SeasonTargetExpansion` 逐字同构：**一个候选值 ⇔ 一个不同的资产名**，
而游戏一次只加载其中一个 ⇒ 「每个候选都写一份」等价于 CP 的「按当前存档写一份」。
区别只在于**四季能靠 GSQ 的 `SEASON` 表达、农场类型在 GSQ 里没有这一档**，
所以这条**不加条件**，靠"目标资产本身不同"区分。

**只在形状能证明互斥时才展开**：同一令牌里取**同键、取值两两不同且都是字面量**的一组
（2…12 个）；别的维度的块**不计入但记档**（实测那个令牌 10 块里 2 块是自定义农场，
键是 `Query: …`、取值本身还是令牌 —— 要求"全部合法"会把整个令牌否掉）。

### 2.1 自查逮到的一个**我自己引入**的缺陷（多目标重复写）

展开在第二个包也生效：`[CP] Ultimate Farm` 的 `Target: "Maps/UltimateFarm, Maps/{{file}}"`
**只有第二项含令牌**，而那条补丁用的是 `TextOperations: **Append**`（往 `MapProperties` 追加）：

| | `Maps/UltimateFarm.tbin` 里 `Front 61 6 510` |
|---|---|
| 展开前（补丁整条被丢） | **0 次** |
| 按候选展开（未按项拆） | **8 次** ✗ |
| 按项拆之后 | **1 次** ✓ |

**修法**：多目标**按项拆** —— 不含令牌的项只出**一份**；内容里若也含该令牌，
"不含令牌"的那一份代不掉（写了也永远不生效）⇒ 不出那一份。

## 3. 包顺序：我们的 Kahn ≠ SMAPI 的"按名字遍历 + 依赖先行"

追「默认农场图上那条通道为什么还是没有」时逮到的**第三条、影响面最大的一条**：
`Maps/Farm` 上 `ConvenientRoad` 出现 **0 次**（另外 7 张农场图各 1 次）。

源因：`[CP] Grandpa's Farm` 有一条**无条件**的 `Load`（源文件第 135-139 行）：

```json
{ "Action": "Load", "Target": "Maps/Farm,Maps", "FromFile": "assets/Maps/GrandpasFarm.tbin" },
```

**整张替换 `Maps/Farm`** —— 谁后跑谁说了算：

| 顺序 | 结果 |
|---|---|
| 先 Load 换图、**后** Lnh 盖通道 | 通道**在** ✓ ← SMAPI |
| 先 Lnh 盖通道、**后** Load 换图 | 通道**没了** ✗ ← 我们（原来）|

- `Grandpa's Farm` 依赖 `Immersive Farm 2 Remastered`；
- **SMAPI**：按**名字**遍历、遇到没加载的依赖**先把依赖拉进来**（DFS）⇒ `Grandpa's Farm`（G）
  在名字序里先于 `Lnh's…`（L）⇒ 它**先**跑 ✓
- **我们**（`convert-mods.py:order`，Kahn：反复取"就绪的、名字最小的"）⇒ `Lnh's…` 早就绪就先落位
  ⇒ 它**先**跑 ✗

⇒ 修法：把 `order()` 改成 **SMAPI 那个算法**（按名字遍历 + 依赖先行，名字不区分大小写）。

| 包 | 改前位次 | 改后位次 |
|---|---|---|
| `Stardew Valley Expanded` | 45 | 14 |
| `Immersive Farm 2 Remastered` | 138 | 38 |
| `Grandpa's Farm` | 163 | **39** |
| `Lnh's Farms Convenient Road` | 139 | **116** |

⚠ 这条会**改动所有"两个包改同一个资产"处的胜负**（不只是农场图），所以第五轮→第六轮的
逐文件差异必须逐类看过 —— 判据见下。

## 4. 「条件落点」的两个新位置：去源码里找，而不是判"没有通道"

玩家反馈「农场上该有的传送没有」时，一开始的结论是"运行时条件没有通道 ⇒ 丢弃"。**这个结论是错的**
—— 去 `D:\sdv_src` 与 `D:\sdv_gd` 查过之后，找到两处**游戏本来就认**的落点：

### 4.1 `Data/Minecarts` 的**数组元素**自带 `Condition`（已修）

```csharp
// StardewValley/GameLocation.cs:10246 —— 逐条查每个目的地
foreach (MinecartDestinationData destination2 in network.Destinations)
    if (… || !GameStateQuery.CheckConditions(destination2.Condition, this)) continue;
// StardewValley.GameData.Minecarts/MinecartDestinationData.cs：public string Condition;
//                                  /MinecartNetworkData.cs：UnlockCondition / Destinations
```

而这类补丁的形状是「`TargetField` 指到**数组**、`Entries` 按键往里加条目」：

```json
{ "Action": "EditData", "Target": "Data/Minecarts", "TargetField": ["Default","Destinations"],
  "Entries": { "jibb.CustomCart1": { "Id": "jibb.CustomCart1", … } },
  "When": { "Enablejibb.CustomCart1": true } }
```

我们原来在 `ConditionWriter` 里遇到"落点是数组"就判「定位不到条件落点条目」⇒ 整条丢
（`[CP] Integrated Minecarts` 3 条 + `[CP] Meadowlands Minecarts` 2 条 = **矿车目的地整批没进产物**）。

**修法**：新增 `ArrayElementConditionSlots`（模型背书表，键 = 资产名 + `TargetField` 末段），
数组落点时把条件写进**本条补丁 `Entries` 那几个 `Id`** 的条目自己的 `Condition`：

- ⚠ **只写那几个** —— 铺给全数组会把**原版条目**也变成有条件的（那会静默改变原版行为）；
- ⚠ 与单条目那条路的差别：**不做跨补丁 OR 累积**（`LandingState` 是"一个槽"的模型，数组是每个 Id 一个槽），
  语料里"两个包给同一个目的地 Id 加条件"目前没有实例；若出现，这里是 AND、CP 是 OR ⇒ 偏保守（可能少显示一条）。

**定点实测**（小树只放 `Data/Minecarts`，跑 `[CP] Integrated Minecarts`）：
`Default.Destinations` 57 条里 **46 条带 `Condition`**（改前只有无条件的那些），
一批 `NoLandingPoint` 翻成 `Converted`；剩下几条的理由换成**更准的**那句
「找不到本条补丁要写的条目 `X`」—— 因为**加那个条目的补丁被别的条件挡了**（级联，如实报出）。

### 4.2 农场图上**运行时条件**的 `EditMap`：仍在取舍（未改）

同一批调查里确认的形态（`[CP] Integrated Minecarts` `code/mapedits.json#65/#66`）：

```json
{ "Action": "EditMap", "Target": "Maps/Farm",
  "When": { "Enablejibb.IF2R": true, "HasFlag: anyPlayer |contains=ccBoilerRoom": true,
            "FarmType": "Standard", "HasMod |contains=flashshifter.immersivefarm2remastered": true } }
```

「标准农场上的矿车」——**目标本身就是 `Maps/Farm`**（只有玩标准农场时才会被加载）⇒
`FarmType` 这一维**由资产名承载**，理论上可以无条件发射；但 `HasFlag ccBoilerRoom` 是**运行时**状态，
无条件写会让"该晚点出现的"从第一天就出现。
⇒ 计划**先把矿车那个网络侧的口子按 4.1 补上**（`Data/Minecarts` 的 `UnlockCondition` 才是它真正的门），
地图侧的无条件化留作下一步，与「1011 条运行时条件地图改动」一起定口径。

## 5. 验证（全语料 + 实机）

| 手段 | 结果 |
|---|---|
| 单元测试 | **1455 条全绿**（本轮新增 11 条：令牌参数 5、候选展开 7 中的若干、数组落点 2、FTM `Years` 2）|
| 语料（232 包，逐轮同参数） | ① 令牌参数残渣 **14 → 0**；② `Maps/Farm*` 八张图各 1 条便捷通道；③ `Maps/UltimateFarm` 的 `TextOperations: Append` **8 次 → 1 次**；④ `Data/Minecarts` 带 `Condition` 的目的地 **52 → 71**（总数 95 → 114）|
| 对改前基线（`clean-cache` 前特意留下的指纹） | 消失 18 / 新增 6 / 内容不同 1519 —— 18 与 6 里 **6 对是同一文件的大小写变体**，余下 12 全是**发射器/本地化变体**，逐条归因到「两包同资产、胜负翻转」（如 `Characters/Eyvinder`：`East Scarp NPCs` 与 `Seasonal Cute Characters ES` 的先后变了）|
| 实机（`D:	mp\gv` 纯 XNB 装机） | `YEAR 2+ 2+` **72 → 0**；ERROR/WARN **241 → 192**；日程解析失败 1（`WrenSong`，运行时 `{{Pool}}` 那一档）、地点建不起来 3（CapeStardew 的 DLL 类地点）—— 都与基线表一致，**没有新类别**。巡图 **传送成功 35 / 建不起来 3**、注入音乐 6 条全播、跨天 3 次 |
| ⚠ 未跑完的 | 本轮的**逐张地图探针**（2499 张）在游戏被关掉时停在中途 ⇒ 这一轮的 `FAIL` 数**没有拿到**；上一轮（只差 `Data/Minecarts` 这一处改动）是 **PASS 1246 / 贴图跳过 1253 / FAIL 0** |

⚠ 本轮改过的三处**顺序/展开**类改动影响面比前几轮大（1519 个文件），归因到"两包同资产"之后**没有逐条看过每一个**；
`audit-overwrites.py` 给出的全语料"被后来的 `Load` 冲掉"是 **2708 处** —— 那一类**仍在**，只是"谁赢"现在与 SMAPI 一致。
