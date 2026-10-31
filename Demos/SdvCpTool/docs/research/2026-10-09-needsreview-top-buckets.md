# `NeedsReview` 最大的几个桶，逐条核过：都是**正确拒绝**

日期：2026-10-09

`NeedsReview` 是全语料最大的阻塞桶（1126 条 / 7048）。按理由聚合出前 18 桶，
逐条看了代表性实例，结论：**没有一处是该做没做的**。

## 1. 前三名（合计约 460 条）

| 条数 | 理由 | 逐条核过 |
|---|---|---|
| **164** | `资产 Data/Festivals/<id> 虽有键名编码的维度，但本条改的不是条目（TextOperations/Fields）` | ✓ **真的无处安放** |
| 37 | `Load 带条件时只能靠新增资产名区分，需要人工确认命名方案` | ✓ 资产创建表达不了「有条件地有」 |
| 21 + 20 + 21 | 条件里有**嵌套令牌** / **纯计算令牌**（自定义 mod 令牌）/ 值里含 `{{Baby1Gender}}` | ✓ `ChildGenders` 无 GSQ 等价物；自定义令牌静态算不出 |

### 1.1 `Data/Festivals/*` 那 164 条长什么样

```json
{ "Action": "EditData", "Target": "Data/Festivals/spring13",
  "When": { "HasSeenEvent": "6665293", "HasMod |contains=CPBoardingHouse": false },
  "TextOperations": [
    { "Operation": "Append", "Target": ["Entries", "Set-Up_additionalCharacters"],
      "Value": "MorrisTod 44 60 down", "Delimiter": "/" }, … ] }
```

往 `Set-Up_additionalCharacters` 这个 **`/` 分隔字符串**里追加一个角色摆位，条件是这个事件看过。

**条件真的没地方放**：那个位置的值是**字符串**，没有 `Condition` 字段可写；
`$query` 是**对话**机制，套在「角色摆位列表」上语义不对；
键名编码（`_yN`/`_spouse` 那一族）已经在
`docs/research/2026-10-06-key-encoded-variants.md` 里查过 —— 这一条改的不是条目，用不上。

⇒ 拒绝得对。**别因为它是最大的一桶就去动它。**

## 2. 其余桶

| 理由 | 结论 |
|---|---|
| `$query 内联不成立 —— 同一键上有 20 条带条件的补丁` | ✓ 拒绝是保守的：多条件变体没法合成一条 `$query` |
| `Data/NPCGiftTastes 的结构无法自动识别` | ✓ 那是**位置编码**的字符串（爱/喜欢/中立…），没有条件槽 |
| `schedules 键名编码了条件维度，但条件不被键名完全蕴含` | ✓ 同 `2026-10-06-key-encoded-variants.md` 的结论 |
| `Hearts 的值里嵌套了令牌` | ✓ 值侧嵌套令牌静态算不出（同 `2026-10-07` 那条「值里含代不掉的令牌就不写」）|

## 3. `NoLandingPoint`（判定 322 条）也过了一遍

⚠ 先厘清一个**口径**（差点当成 bug）：报告里 `patches[].verdict` 对**被集合发射覆盖**的补丁写
`Emitted`（`ReportWriter.cs:32-40` 有说明），而 `summary` 里它们仍记原判定（`NoLandingPoint`）。
两个数**都没错**、问的是两件事 —— 头条 `blocked` 用的是 `Total − Convertible − GroupEmitted`
（已扣掉那 224 条）。**别把 `summary[NoLandingPoint]` 当阻塞数**。

判定 322 条的构成：

| 条数 | 理由 | 结论 |
|---|---|---|
| **约 100** | `资产 X 是图片 —— 引擎里按条件换图有机制（四季 BigCraftable 索引 / 图集文件季节前缀 / NPC 的 Appearance[]），但本条不属于任何一种；需要人工确认命名方案` | ✓ 三套发射器**都试过并 declined**（前置条件不满足），不是没做 |
| 约 60（实为 **63**） | `别的 mod 注册的自定义资产`（`sophie.ConversationTopics` 32 / `Mods/Bouhm.*` 19 / `furyx639.CustomBush` 5 / `spacechase0.SpaceCore` 4 …） | ✓ 纯 XNB 场景没人读它（见 `2026-10-08` 那条改动）。⚠ "是不是别人的命名空间"**不等于**"有没有消费方" —— 口径与逐条实测见 `2026-10-09-non-cp-framework-channels.md` §7.2（自己的命名空间措辞已分开） |
| 约 20 | `列表资产，元素里没有任何可挂条件的字段`（如 `Data/MoviesReactions`） | ✓ 模型里确实没有 |
| 其余（约 140） | 长尾 | 未逐条核 → **见 3.1，现在抽查过了** |

### 3.1 长尾抽查（2026-10-09 补）

按 `reason` 前 46 字符聚合，`NoLandingPoint` 实际有 **118 个桶 / 323 条**
（`summary` 里的 548 是没扣掉发射覆盖的宽口径）。逐一看过前 60 桶：

| 桶 | 条数 | 结论 |
|---|---|---|
| `LooseSprites/Cursors` 是图片 | 55 | ✓ 条件是 `LocationName: WitchSwamp` 一类 —— 按**地点**换一张**全局共享图集**的一块区域，原版没有这个机制（`Cursors` 全地点共用） |
| `sophie.ConversationTopics` | 32 | ✓ 自定义资产 |
| `Tilesheets/Craftables` / `Maps/MenuTiles` / `Characters/SlimeActor*` / `LooseSprites/{Map,Fence1,daybg,nightbg}` / `Characters/{Sandy,Claire}` … | 各 4–9 | ✓ 同图片类，条件不是四季（多为地点/剧情），三套发射器前置条件不满足 |
| `Mods/Bouhm.NPC*` / `furyx639.CustomBush/Data` / `spacechase0.SpaceCore/*` | 各 1–8 | ✓ 自定义资产 |
| `Data/MoviesReactions` | 6 | ✓ 列表资产，元素无可挂条件字段 |
| **「`X` 这个位置不接受 Condition」** | **5** | 见下，**逐条核过是真拒绝** |
| `定位不到条件落点条目`（数组落点） | 15 | ← 另一件事，见 `2026-10-09-include-gap-is-not-a-gap.md` §4.3.1 |

#### 「不接受 Condition」那 5 条是真的（差点当成 bug）

判据在 `ConditionWriter.AcceptsCondition`：**它自己已有 `Condition`，或它的同级兄弟有** ——
是**启发式**，不是 schema 驱动（`Data/Locations` 的 `Forage[].Condition` 与 `Data/Objects` 的
`GeodeDrops[].Condition` 形状一样，资产级扫描分不开）。启发式就可能误杀，所以去核了：

```
3 条  `Data/Objects`：`426` / `424` 这个位置不接受 Condition
1 条  `Data/Locations`：`JojaMart` 这个位置不接受 Condition
```

查游戏定义（`D:\sdv_gd\StardewValley.GameData.Locations\LocationData.cs`，逐字段列过）：
**`LocationData` 里根本没有 `Condition` 字段**（`DisplayName` / `ArtifactSpots` / `Fish` / `Forage` /
`Music` / `CustomFields` … 都不是）。`ObjectData` 同理。
⇒「原版在该位置不读条件字段」**判得对**，这 5 条落不进去。

⚠ 但**判据是启发式这件事本身**值得记着：哪天遇到「某类型**确实**有 `Condition` 字段、
只是原版数据里恰好没人用」的情况，这一档就会误杀。要根治得让 `GameDataSchema` 能回答
「这个类型有没有 `Condition`」—— 它现在只列**容器**字段（数组/映射），答不了。

## 4. 这条记录**不覆盖**什么

只核到「理由站不站得住」这一层，**没有**去核每条补丁的**语义**是否本该有别的落点。
`Data/Festivals` 那 164 条占 `NeedsReview` 的 15%，值得再想一次 ——
但要动它得先有一个能表达「字符串里追加的东西带条件」的机制，现在没有。
