# CP 运行时令牌的设计意图 与 原版替代路径

日期：2026-10-05
调研对象：`{{Random}}`、`{{FirstValidFile}}`、`HasSeenEvent`（含 `|contains=`）
源码依据：`D:\tmp\cp_decompile`（ContentPatcher 2.9.1 反编译，`ilspycmd -p`）、
`D:\sdv_src`（星露谷 1.6 完整反编译）、`D:\rsv_src`（RSV 反编译）、游戏 `Content\` 实物文件

---

## 0. 为什么要问「设计意图」而不是「能不能求值」

判断一个 CP 令牌「能不能转成纯 XNB」时，问「这个令牌能不能求值」会得出错误的结论，
因为它把三种性质完全不同的东西混成了一类。正确的问法是：

> **它是在编码「状态」，还是在编码「资源布局」，还是在编码「不确定性」？**

| 类别 | 替代路径 | 收录令牌 |
|---|---|---|
| **状态查询** | 原版 GameStateQuery（1.6 内建，115 个关键字） | `HasSeenEvent` |
| **资源布局** | 原版内容管线（`.<locale>` 后缀回退、`Data/Characters.Appearance`） | `FirstValidFile`、部分 `Random` |
| **不确定性** | 原版**没有**通用等价物 → 只能烘焙 | 任意字符串插值处的 `Random` |

---

## 1. `HasSeenEvent`（含 `|contains=`）

### 设计目的

**给一个「只能正向匹配、没有 NOT 运算符」的条件系统补上「成员测试 + 取反」能力。**

`Condition` 的实现是「令牌取值集合」与「JSON 值集合」求交集
（`ContentPatcher.Framework\Conditions\Condition.cs:77`）：

```csharp
IsMatch = token.GetValues(Input).Any(value => CurrentValues.Contains(value));
```

`HasSeenEvent` 是个**列表令牌**——它的取值集合就是玩家的 `eventsSeen` 集合
（`TokenManager.cs:166`）：

```csharp
new PerPlayerValueProvider(ConditionType.HasSeenEvent,
    (Farmer player) => (IEnumerable<string>)player.eventsSeen, save),
```

`|contains=` 把它归约成布尔 `"true"`/`"false"`（`Tokens\Token.cs:125-170`，挂在**基类**上，
所以**所有令牌通用**，1.15 起被大批令牌采用）。于是 JSON 值成了**布尔门**：

| JSON 键 | JSON 值 | 语义 |
|---|---|---|
| `HasSeenEvent: \|contains=X` | `true` | **看过 X** |
| `HasSeenEvent: \|contains=X` | `false` | **没看过 X** ← 我们曾把这里做反 |
| `HasSeenEvent: \|contains=X,Y` | `false` | X、Y **都没**看过 |
| `HasSeenEvent: \|contains=`（空） | 任意 | 恒假 |

**JSON 值参与运算，不是占位符。** `Token.cs:78-97` 规定带 `contains` 时值若不是布尔，**加载即报错**。

### 原版替代：完全可以

| CP | GSQ |
|---|---|
| `HasSeenEvent: X` | `PLAYER_HAS_SEEN_EVENT Current X` |
| `HasSeenEvent: hostPlayer: X` | `PLAYER_HAS_SEEN_EVENT Host X` |
| `HasSeenEvent: anyPlayer: X` | `PLAYER_HAS_SEEN_EVENT Any X` |
| `HasSeenEvent: "X, Y"` | `PLAYER_HAS_SEEN_EVENT Current X Y` |
| `HasSeenEvent: \|contains=X: false` | `!PLAYER_HAS_SEEN_EVENT Current X` |
| `HasSeenEvent: \|contains=X,Y: false` | `!PLAYER_HAS_SEEN_EVENT Current X Y` |

`PLAYER_HAS_SEEN_EVENT` 签名（`StardewValley\GameStateQuery.cs:1209-1217`）：
**`<playerKey> <eventId>+`**，多个 eventId 空格分隔、语义是 **ANY**。playerKey **必须显式给**
（`ArgUtility.TryGet` 越界即报错，`allowBlank` 不等于可缺省），取值
`Any`/`All`/`Current`/`Target`/`Host`/数字 ID。

**唯一写不出来的**：把 `{{HasSeenEvent}}` 当**字符串值**插进文本（GSQ 只产布尔，产不出列表）。
`valueAt` 索引取值同理。

### 对我们的意义

我们之前那个 bug 的修法 `!PLAYER_HAS_SEEN_EVENT` **是对的**，已被本次源码复核确认。

---

## 2. `{{Random}}`

### 设计目的

**相关随机**（correlated randomness）——让多处本无关联的随机绑定到同一条随机线上，
并且**每天稳定、全体玩家一致**。

### 语义（源码逐行核对）

`ContentPatcher.Framework.Tokens.ValueProviders\RandomValueProvider.cs:31-42`：

```csharp
string text = input.GetRawArgumentValue("key") ?? input.TokenString.Path;
int seed = Utility.CreateRandomSeed(BaseSeed, Game1.hash.GetDeterministicHashCode(text), 0, 0, 0);
int num = new Random(seed).Next();
return InvariantSets.FromValue(input.PositionalArgs[num % input.PositionalArgs.Length]);
```

`RandomValueProvider.cs:50-66`：

```csharp
int daysSinceStart = SDate.Now().DaysSinceStart;
ulong uniqueIDForThisGame = Game1.uniqueIDForThisGame;
return Game1.hash.GetDeterministicHashCode(new int[2] { daysSinceStart, (int)(uniqueIDForThisGame % 2147483647) });
```

**结论（与直觉相反）**：

- **同一个存档 + 同一天 = 稳定常数。** 重启游戏、进出房间、换地点，**值都不变**。
- **每天重掷一次。** 随存档不同而不同。**所有玩家看到同一个值。**
- `key` **不是缓存**，是**种子输入**——把多处随机绑到一条线上。
- 没有 `key` 时默认用**该令牌所在位置的路径**（`TokenString.Path`）：同一字段里两个
  Random 会取到同一个值，不同字段各自独立。
- **唯一合法具名参数就是 `key`**；`value=`/`seed=`/`showkey=` 在 2.9.1 里**不存在**，
  传了会直接报错（`BaseValueProvider.cs:90-111` 白名单校验）。
- **不写存档**。`TokenSaveReader` 名字有误导性——它是 `Day/Season/Weather` 等令牌的只读带时效缓存，
  与 Random 无关。

### RSV 真实用例印证

`Data\DependencyData\CustomCompanionsTourists.json:251` 起：

```
Base 1&6: Body_{{Random: 1,2,3,4,5 |key=RSVTourist}}/Skin/Colour_{{Random: 1..10}}.png
Base 2&7: Body_{{Random: 2,3,4,5,1 |key=RSVTourist}}/Skin/Colour_{{Random: 3..10,1,2}}.png
... 共 5 条，Body 列表依次旋转 +1
```

**作者要的不是「随机」，是「保证各不相同」。** 5 条 patch 共享同一个 `key` → 拿到同一个下标 `num`，
但列表依次旋转 → 10 个游客恰好**各拿一个互不重复的 body**（一个排列）。同时所有图层
（body/skin/pants/shirt/hair/eyes）共享这个 key，**保证图层对齐**，不会「身子用 Body_3、裤子取 Body_1」。

### 原版替代

**① 条件类 → GSQ `SYNCED_CHOICE`**（`GameStateQuery.cs:1682`）

```
SYNCED_CHOICE <interval> <key> <min> <max> <允许值...>
```

底层 `Utility.TryCreateIntervalRandom(interval, key)`（`Utility.cs:1172-1198`）与 CP 的
`GenerateBaseSeed` **同构**：`key` 哈希进种子、`uniqueIDForThisGame` 是存档 ID、
`interval` ∈ `tick` / `day` / `season` / `year` 决定重掷时机。另有 `SYNCED_RANDOM`（概率版）与 `RANDOM`。

**② NPC 换装 → `Data/Characters` 的 `Appearance[]`（无损路径）**

`StardewValley.GameData.Characters\CharacterAppearanceData.cs` 全字段：
`Id` / `Condition`（**GSQ**）/ `Season` / `Indoors` / `Outdoors` / `Portrait` / `Sprite` /
`IsIslandAttire` / `Precedence` / `Weight`（默认 1）。

`NPC.cs:918-1011` `ChooseAppearance()`：种子是 `CreateDaySaveRandom(hash(NPC 名))` ——
**语义与 CP Random 几乎一模一样**（同存档同天稳定、每天重掷、跨玩家一致），
还额外支持**加权**与**优先级**。官方注释原话：*"the relative weight to use when randomly choosing one"*。

> 凡「给某个 NPC 每天换一张贴图」的 CP Random 用法，都能**无损**转成 `Appearance`。

**③ 任意资产路径 / 多图层分别随机再合成 → 无解，只能烘焙**

原版 `Appearance` 只能选**一张完整 `Sprite`**，且只对 `Data/Characters` 的 NPC 生效。
RSV 游客是 CustomCompanions 的 `Companion`（**不是 NPC**），且 CP Random 在这里生成的是
**任意资产路径**——原版没有任何数据字段能「按天生成资产路径」。

**烘焙的正确姿势**：不要随便取一个值。**按作者自己的旋转表固定成一组互不重复的组合**
（guest_i 取 Body_i、Colour_{2i-1}），把「**各不相同**」这个硬需求固化进 XNB。
损失从「每天不换装 + 所有游客撞衫」降到只剩「每天不换装」。

---

## 3. `{{FirstValidFile}}`

### 设计目的

**多候选取第一个存在的路径（回退链）。** CP 1.22 引入（`Migration_1_22.cs:13`）。
语义比「语言回退」更宽——RSV 的另一处用法是
`{{FirstValidFile: Assets/WeddingReception/NPCs/{{spouse}}.png, Assets/WeddingReception/z_RSVspousePic.png}}`
（按配偶选图，缺失则回退通用图）。

`FirstValidFileValueProvider.cs:35-57`：纯**文件存在性**判定，取第一个命中的；
空项跳过；绝对路径跳过；含 `../` **抛异常**；全部不存在 → 返回空集合 →
补丁标记未就绪并出诊断（**不报错崩**）。

### 关键陷阱：两套语言命名**不通用**

| | CP `{{Language}}` | 原版 `LanguageCodeString` |
|---|---|---|
| 取值 | **短码** `zh` / `ja` / `ru` | **长码** `zh-CN` / `ja-JP` / `ru-RU`（`en` → 空串）|
| 命名 | **下划线**：`RSVQuestBoard_zh.png` | **点**：`RSVQuestBoard.zh-CN.xnb` |
| 来源 | `TokenManager.cs:245-258` | `LocalizedContentManager.cs:446-465` |

RSV 实物文件名确实是 `_zh.png`（短码）。**照搬这个名字原版链找不到。**

### 原版本地化机制的精确行为

`LocalizedContentManager.Load<T>`（`LocalizedContentManager.cs:388-441`）—— 非英语时按序尝试：

1. `<assetName>.<LanguageCodeString>`（**点 + 长码**）
2. `<assetName>_international`
3. `<assetName>`（无后缀回退）

**对图片/纹理同样生效**（本次实物核实）：

```
Content\LooseSprites\Cursors.zh-CN.xnb        ← 存在
Content\LooseSprites\Billboard.zh-CN.xnb      ← 存在
Content\ 下 *.zh-CN.xnb 共 212 个
Content\ 下 *_international.xnb 共 4 个（全是纹理）
```

`Game1.cs:2383` `content.Load<Texture2D>("LooseSprites\\Cursors")` 就是走这条链的原版实证。

运行时真正执行的是 **SMAPI 的 `GameContentManager`**（`BaseContentManager.cs:131-162`），
链与上面一致。但 `ModContentManager.TryLocalizeKeys = false` —— **mod 内容包加载自己的文件时不做语言回退**。

### 为什么 CP 要自己造这个令牌

CP **有** `TargetLocale` 字段，但 2.1 起被 `Migration_2_1.cs:25-27` 判为错误；
且 `PatchManager.cs:342-345` 对 mod 资产默认 `skipLocalizedAssetByDefault = true`——
**CP 刻意不让 mod 资产参与原版 locale 链**。这正是作者绕道 `{{Language}}` + `FirstValidFile` 的原因。

### 结论

- **能替代「语言回退」这一用途**，但**必须改成原版命名**（`.zh-CN`，点 + 长码），
  且需要一个 loader 把 `LooseSprites/RSVQuestBoard.zh-CN` 这个**名字**注册成可加载资产。
- **推荐做法（最稳）**：**烘焙期静态解析**——转换时按目标语言跑一遍 `FirstValidFile` 的存在性逻辑，
  直接选定 `_zh` 或 `_en` 落成一个扁平资产。零运行时依赖。
  这与我们已有的「语言后缀产出」通道（通道 B1）天然契合。

### 对我们的意义

我们的 B1 通道产出的是 `<name>.zh-CN.json` / `.config`（点 + 长码）——
**命名已经是对的**，本次抽查 702 个文件无一用错短码。无需改动。

---

## 4. 对工具的三条直接影响

### 4.1 `TokenKind` 分类应改为按**语义类别**分

现在的分类是按**令牌名**分的。替代路径完全由**语义类别**（状态 / 资源布局 / 不确定性）决定，
按名字分会导致「同一类被拆开、不同类被合并」。

### 4.2 「一条叶子毒化整条」比「条件复杂」严重得多

对 10b 之后的真实报告实测（RSV，207 条条件失败）：

```
203 / 207 是纯对象（= AND），没有 OR、没有嵌套
叶子数：1 个 → 81 条   2 个 → 35 条   3 个 → 43 条   4 个 → 41 条   5 个 → 3 条
叶子令牌：自定义模组令牌 164 / HasSeenEvent 114 / HasMod 49 / Time 26 / IsOddYear 17 /
          HasFlag 17 / Weather 12 / Season 8 / Query 7 / LocationName 7 / ...
```

**CP 条件的布尔结构从来不复杂，原版 GSQ 的 115 个关键字覆盖了几乎所有叶子。**
真正的病是**全有或全无**。修法是**常量折叠 + 部分求值**（见计划 Task 10d），不是布尔化简。

### 4.3 GSQ 的能力被我们低估了

GSQ 共 **115 个关键字**（按 `DefaultResolvers` 的方法名反射注册，`GameStateQuery.cs:1958-1978`），
其中包括 `RANDOM` / `SYNCED_RANDOM` / `SYNCED_CHOICE` / `TRUE` / `FALSE` / `ANY` /
`PLAYER_HAS_MAIL` / `PLAYER_FRIENDSHIP_POINTS` / `DAY_OF_MONTH` / `YEAR` 等。

`TRUE` / `FALSE` 尤其有用——它们是常量折叠的天然落点。

---

## 5. 证据索引

**CP（`D:\tmp\cp_decompile`）**
- `ContentPatcher.Framework.Tokens.ValueProviders\RandomValueProvider.cs:15-66`
- `...\ValueProviders\FirstValidFileValueProvider.cs:15-57`
- `ContentPatcher.Framework\TokenManager.cs:166, 222, 238, 245-258`
- `ContentPatcher.Framework\Conditions\Condition.cs:54-85`
- `ContentPatcher.Framework\Conditions\TokenString.cs:289-290`
- `ContentPatcher.Framework.Tokens\Token.cs:78-97, 125-170`
- `ContentPatcher.Framework.Tokens\InputArguments.cs:12-18, 119-173`
- `ContentPatcher.Framework\ConfigModels\PatchConfig.cs:17, 26`
- `ContentPatcher.Framework\PatchManager.cs:342-345`
- `ContentPatcher.Framework.Migrations\Migration_1_4.cs:13` / `_1_15_Rewrites.cs:18-50, 74-83` / `_1_22.cs:13` / `_1_24.cs` / `_1_25.cs` / `_2_1.cs:25-27`

**星露谷（`D:\sdv_src`、`D:\sdv_gd`）**
- `StardewValley\GameStateQuery.cs:97-142, 253-256, 1209-1217, 1682, 1700, 1921-1931, 1958-1978, 2174, 2237`
- `StardewValley\Utility.cs:1161-1164, 1172-1198, 1206-1224`
- `StardewValley\LocalizedContentManager.cs:329-360, 388-441, 446-465`
- `StardewValley\NPC.cs:918-1011`
- `StardewValley\Farmer.cs:237`；`Netcode\NetHashSet.cs:39`
- `StardewValley.GameData.Characters\CharacterAppearanceData.cs`（全字段）
- `StardewValley\Game1.cs:378, 2383`；`StardewValley.Menus\Billboard.cs:155`

**SMAPI**：`BaseContentManager.cs:33, 131-162`；`ModContentManager.cs:67`；`AssetName.cs:43, 51-80`；`ContentCoordinator.cs:127, 471`

**实物**：`Content\LooseSprites\Cursors.zh-CN.xnb`、`Billboard.zh-CN.xnb`、`Maps\*_international.xnb`；
RSV `Assets\LooseSprites\RSVQuestBoard_{zh,en,pl,...}.png`；`D:\rsv_src\RidgesideVillage.decompiled.cs:786, 5038-5060`
