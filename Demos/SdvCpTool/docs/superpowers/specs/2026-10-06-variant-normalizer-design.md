# 变体归一化器（Variant Normalizer）设计

日期：2026-10-06
**一句话**：把「同一意图的多种实现」自动收敛到原版的**最简形式**。

> 用户的原话：「**我想达到的效果就是自动演化到最简实现**，不要忘了你之前说的编译器思想。」

---

## 0. 编译器类比（为什么是这个架构）

| 编译器 | 本工具 |
|---|---|
| 源语言（自由度极高，同一计算有无数等价写法）| CP 的 5 个 Action + 令牌 + 条件（**同一意图 5–7 种写法**）|
| **IR** | **`变体意图 = (载体, 维度, 值集, 基对象)`** |
| 目标 ISA | 原版的**原生机制**（约 6 个可用于「连续格子」，见 §3）|
| **指令选择** | **发射表**：`(载体, 维度) → 最简形式` |
| 优化 | 常量折叠（已有）、**变体归一化（本设计）** |
| 可观测 | **熵 = 同一 IR 节点的实现数**；归一化后应下降 |

**「数据熵增」= 同一 IR 节点的实现数。归一化器做的就是把它们映射回同一个节点、再发射成同一个形式。**

---

## 1. 熵的实测（本设计的依据）

两个语料（RSV + Steam 96 项 32 个 CP 包，6538 条补丁）里，**带维度条件的补丁**：

| 维度 | 合计 | **实现写法数** | 分布（载体 → 条数）|
|---|---|---|---|
| **看过的剧情** | **1024** | **7** | 条目其它 309 / schedules 240 / 地图 236 / Festivals 键 166 / Load 31 / 精灵 42 |
| **季节** | **839** | **7** | schedules 276 / 地图 252 / 自己 Load 的资产 212 / 条目 41 / 精灵 55 |
| **婚姻** | **634** | **5** | Festivals 键 359 / 条目其它 149 / schedules 65 / 地图 47 / 精灵 14 |
| **年份** | **132** | **5** | 地图 57 / Festivals 键 45 / schedules 17 / 条目 9 |
| **好感度** | **104** | **5** | 条目 74 / Festivals 14 / 地图 9 / schedules 5 |

**同一个「季节」意图被 7 种载体实现。这就是熵。**

---

## 2. ★ IR：`变体意图`

```
VariantIntent {
    Base        : 被作用的对象（资产名 / 数据表 + 条目 / 图集 + 基址）
    Dimension   : 维度（season / marriage / year / seen_event / hearts / …）
    Values      : 维度的取值集合（4 季 / 配偶名列表 / 事件 ID / …）
                    每个值带各自的「表示」（资产名 / 条目值 / 图内矩形）
}
```

**判定一个补丁属于哪个 `VariantIntent`**，只需三问：

1. **它改的是什么对象？** → `Base`
2. **它按什么维度分叉？** → `Dimension`（从条件的令牌名判定）
3. **每个维度值对应什么？** → `Values`

**同一 `Base`、同一 `Dimension`、值集互补的多条补丁 → 同一个 IR 节点。**

---

## 3. ★ 发射表（本设计的核心资产）

**`(载体, 维度) → 最简形式`。这是唯一的事实来源，新增原生机制只改这张表。**

| 维度 | 载体 | **最简形式** | 实测条数 | 可用 |
|---|---|---|---|---|
| **季节** | **精灵**（自己 Load 的资产）| **`Data/BigCraftables.Name` 加 `Seasonal` + `idx` 4 对齐** | **267** | ✅ |
| 季节 | 条目（`Data/Characters` 的 NPC）| `Appearance[]` 的 `Season` | 41 | ✅ |
| 季节 | 地图 tilesheet | **`spring_` 前缀**（**仅室外**，`GameLocation.cs:16411-16416` 已核）| 252 | ✅ |
| **季节** | **`Characters/schedules`** | **— 键链是优先列表，两个维度不可组合** | **276** | ❌ |
| **婚姻** | **`Data/Festivals` 的键** | **`<NPC>_spouse`**（`Event.cs:12609-12665`）| **359** | ✅ |
| 婚姻 | `Characters/schedules` | `marriage_<season>_<day>` 键 | 65 | ✅ |
| 婚姻 | 条目（NPC）| `Appearance[].Condition` = `PLAYER_NPC_RELATIONSHIP …` | 14 | ✅ |
| 婚姻 | 地图 | — | 47 | ❌ |
| **年份** | **`Data/Festivals` 的键** | **`_yN`**（`Event.cs:4417-4443`）| 45 | ✅ |
| 年份 | 地图 / schedules | — | 57 / 17 | ❌ |
| **看过的剧情** | 条目（对话宿主）| **`$query <GSQ>#真\|假`** | 466 | ⚠️ 见 §5 |
| **看过的剧情** | **地图** | 只有 `ConditionalDoor <GSQ>`（极窄）| **236** | ⚠️ 基本无 |
| 好感度 | 条目 | `$query` / `PLAYER_HEARTS` | 88 | ✅ |

**粗算：约 1300 / 2733（≈48%）有最简形式。**

---

## 4. 归一化流程

```
现有流水线（M1 判定 → M2 翻译 → M3 写入）产出 PatchVerdict 列表
            │
            ▼
   ★ 归一化 pass（新增，唯一入口 VariantNormalizer.Run）
      1. 按 (Base, Dimension) 分桶
      2. 桶内 ≥2 条、且值集覆盖维度全体 → 是一个 VariantIntent
      3. 查发射表
         ├─ 有最简形式 → 产出【一条】归一化结果，原 N 条标记为 Merged
         └─ 无         → 原样保留，理由写明「(载体, 维度) 无原生形式」
      4. 报告：熵下降量 = Σ(N − 1)
```

**关键约束（沿用既有教训）**：
- **判定与写入必须走同一个 `VariantNormalizer`**（Task 10b 的教训）。
- **值集必须「覆盖维度全体」才算完整意图**——只覆盖一半的不能归一（否则丢变体）。
- **无最简形式时如实报「无解」**，不得退回「没找到」。

---

## 5. 与现有任务的关系（不重写，而是收口）

**本设计不是新开一条流水线**，而是把已排的任务**收进一张表**：

| 已有任务 | 在本设计里的位置 |
|---|---|
| Task 10f（`_spouse` 键）| 发射表 `(Festivals 键, 婚姻)` 的**一个实现** |
| Task 18（`Seasonal` 名）| 发射表 `(精灵, 季节)` 的**一个实现** |
| Task 15d / 14a / 14b / 15 | **前置条件**（条件能翻译、旧值能取到）|
| Task 10g（`KeyEncodedVariant`）| 发射表的**载体识别**部分 |

**所以实现顺序**：先把 14a/14b（让条件能翻、旧值能取）做完 —— **归一化器才有输入**；
然后把已有实现（10f/18）改写成「查表发射」，最后补 `Seasonal` 之外的发射项。

---

## 6. 验收（**可量化，这是本设计的意义**）

1. **熵下降**：对两个语料跑归一化，报告
   - 归一化前：各维度的实现写法数（§1 的表）
   - 归一化后：**同一个 IR 节点的实现数应从 5–7 降到 1–2**
2. **覆盖**：报告「有最简形式 / 无最简形式」的条数比（当前实测 ≈ 48% / 52%）
3. **不静默丢失**：归一化后的产物必须与原 N 条**语义等价**（抽样逐条核对）
4. **无解要如实**：`(schedules, 季节)` 那 276 条必须报「键链不可组合」，不能报「没找到」

---

## 7. 明确不做的

- **不重写 M1/M2/M3**。归一化是**追加的一 pass**。
- **不追求 100% 覆盖**。实测 48% 有最简形式，**剩下 52% 如实报无解**。
- **不为凑数字放宽**。值集不完整的桶不归一。

---

## 8. 前端已实现 + 首次实测（2026-10-06）

前端工具：**`tools/variant-entropy.py`**（配 `tools/jsonc.py` 容错解析器）。
它读转换报告 → 按 `(维度, 载体)` 分桶 → 查发射表 → 报熵。

### 8.1 语料 B（32 个包，2181 条带维度条件的补丁）

```
[有]   有最简形式：       1373 / 2181（62%）
[无]   已确证无通用机制：  570 / 2181（26%）
[未定] 发射表未覆盖：      238 / 2181（10%）  ← 待补，不是「有」也不是「无」
```

**「未定」必须与「有」分开**——否则会把「我们还没查」算成「原版支持」，虚报约 5 个百分点。

### 8.2 按维度的载体分布（即「熵」）

| 维度 | 条数 | 载体数 |
|---|---|---|
| 看过的剧情 | 814 | **7** |
| 季节 | 810 | **6** |
| 婚姻 | 351 | **5** |
| 年份 | 109 | **5** |
| 好感度 | 97 | **5** |

**同一个意图最多被 7 种载体实现。**

### 8.3 ★ 一个必须分开的分类（本工具抓到）

**`Characters/schedules` 与 `Strings/schedules` 不是一回事**：

- `Characters/schedules/*` —— **纯键链**，无条件位
- `Strings/schedules/*` —— 经 `new Dialogue(...)` 解析，**是 `$query` 宿主**

**混为一类会判错落点。** 工具里已拆成两个载体。

### 8.4 ★ 精确量化 `$query` 的能力边界

**`季节 × 日程字符串` 225 条** —— 真实形态：

```json
// 四条互斥条件模拟一个季节维度
{ "Season": "Spring", "Season |contains=Summer, Fall, Winter": false }
{ "Season": "Summer", "Season |contains=Spring, Fall, Winter": false }
{ "Season": "Fall",   "Season |contains=Spring, Summer, Winter": false }
{ "Season": "Winter", "Season |contains=Spring, Summer, Fall":   false }
```

`Strings/schedules` 是 `$query` 宿主，**但 4 个分支超出 `$query` 的两分支上限**，
而嵌套 `$query` **坏**（`Dialogue.cs:646-652` 的 `Split('|')` 不带 limit，会丢尾段）。

> **结论：这一格 = 「有机制，但装不下 4 个分支」。**
> 不是「没找到」，也不是「原版没有」——是**表达能力的精确边界**。

### 8.5 下一步：把 238 条「未定」逐格定性

`未定` 现已集中在少数几格，逐格判「能补 / 只能烘焙 / 真装不下」即可。
**这是「一点点量化」的收敛点**——每填一格，`[有]` 的比例就往上走一步。

---

## 9. ★ 逐类型量化（2026-10-06，C# 版跑出的精确落点）

工具：`tools/variant-per-type.py`（把每个维度再按**实际资产类型**拆开）。

**未定格精确落点（246 条，占 9%）：**

| 未定格 | 条数 | 下一步 |
|---|---|---|
| 季节 × `Strings/schedules` | **228** | 靶向精算中 |
| 年份 × `Strings/schedules` | 9 | 同上 |
| 看过的剧情 × 其它 | 4 | 归类型 |
| 季节 × 其它 | 3 | 归类型 |
| 好感度 × 精灵 | 2 | 小 |

**五档汇总（2733 条）：有 66% / 无 24% / 未定 9%。**

### 9.1 ★ 熵压在哪些**类型**上（这才是「逐类型」的意义）

同一个维度散布在 **7–47 种资产类型**上，长尾就是熵：

| 维度 | 条数 | 类型数 | 压得最重的类型 |
|---|---|---|---|
| 看过的剧情 | 1024 | **30** | Maps 266 / `Characters/schedules` 229 / `Data/Festivals` 166 / **`Data/Events/*` 130** / Characters\|Portraits 109 |
| 季节 | 839 | **27** | Maps 258 / `Strings/schedules` 222 / Mods/\* 212 / Characters\|Portraits 64 |
| 婚姻 | 634 | **47** | `Data/Festivals` 359 / Characters\|Portraits 69 / `Characters/schedules` 48 / Maps 47 / **`Data/Events/*` ≈40** |
| 年份 | 132 | 7 | Maps 61 / `Data/Festivals` 45 |
| 好感度 | 104 | 11 | Characters\|Portraits 50 / `Data/Festivals` 14 / **`Data/Events/*` 13** |

### 9.2 ★ 一个整块未建模的类型：`Data/Events/*`（≈190 条）

**此前我们的表里完全没有它。** 实测：看过的剧情 130 + 婚姻 ≈40 + 好感度 13 + 年份 4。

`LandingKind` 里已有一档 `EntryKeyConditions`（「键形如 `<事件ID>/<前置条件>/…`」），
**但从没被真正利用过**。**这是当前最大的单块可回收面积。** 靶向精算中。

### 9.3 ★ 一个测量/实现坑：资产名大小写

工具第一版把 `Characters/schedules` / `characters/schedules` / `Characters/Schedules`
算成**三个不同类型**，虚增类型数。**资产名必须大小写不敏感地归一化**——
C# 侧 `VariantNormalizer.CarrierOf` 用的是 `OrdinalIgnoreCase`（已正确），
**这是测量工具的坑，不是产物的坑**，但值得记下来：**任何按资产名分桶的地方都要先归一化**。

### 9.4 ★ 两处自相矛盾（必须消解，否则表不可信）

同一格在 Python 表与 C# 表里**结论不同**：

| 格子 | `tools/variant-entropy.py` | `EmitterTable.cs` | 依据 |
|---|---|---|---|
| 看过的剧情 × 地图 | `None`（无） | `CapacityExceeded`（装不下） | 待精算 |
| 季节 × Characters/schedules | `None`（无） | `None` | 但 `KeyEncodedVariants` 又确证 `<season>_` 前缀是原生键 |

**第二行是真矛盾**：若季节性日程键原生成立，则 `(季节, 日程)` 应为**有**，
只是「季节与另一维度**同时**出现」时才无解。
**这说明发射表的粒度不够**——`(载体, 维度)` 装不下「仅当条件只含本维度时才成立」这个前提。

**因此下一版 `EmitPlan` 需要一个 `Precondition` 字段**：
「本格 `Native` 仅当条件**只含**本维度；含第二维度时落到组合格（当前组合格一律 `None`）」。
**这是在精算结果回来之前不动的设计结论**，先记下。

### 9.5 收敛路径

1. 精算 §8.5 的 5 个未定格 + §9.4 的 2 处矛盾 → **把 246 条未定消到最小**
2. 精算 `Data/Events/*` → **回收约 190 条**
3. 给 `EmitPlan` 加 `Precondition` → 让 `(载体, 维度)` 表能表达「单维度 vs 组合」
4. 每步重跑 §1 的表，报**熵下降量**

---

## 10. ★ 一次真实的收敛：SVE 季节灌木（208 → 52，压缩 75%）

工具：`tools/emitter-cells.py`（汇总报告里的发射表格子）。
> 原先是 `tools/variant-intents.py` —— 它**自己重新猜维度、还手抄了一份 EMITTER 表**，
> 与 C# 的 `EmitterTable` 是**同功能两处实现**，实测已经漂移（少抄了 `Data/Festivals` 的季节格、
> `Strings/schedules` 的「装不下」格、`Data/PassiveFestivals` 整格）。已删除，改为只读报告。

### 10.1 实测

SVE 在 **52 个 `Mods/*` 图集**上各写了 **4 条** `EditImage`，按季节各拷一个矩形：

```jsonc
{ "Action": "EditImage",
  "Target":   "Mods/FlashShifter.StardewValleyExpandedCP/Little Shrub",
  "FromFile": "assets/tilesheets/SeasonalJA.png",
  "When":     { "Season": "Spring", "IsOutdoors": true }, ... }
```

| 量 | 值 |
|---|---|
| 资产数 | **52** |
| 补丁数 | **208**（每资产恰好 4 条，四季齐备）|
| 四季来自**同一张** `FromFile` | **52 / 52**（全部）|
| **收敛后** | **52 个最简形式** |
| **压缩掉的补丁** | **156 条（75%）** |

**这是「N 个实现 → 1 个最简形式」的实证，且已经在真实语料上量出压缩比。**

### 10.2 ★ 但代价必须写清楚：只能标**近似**，不是无损

那 208 条补丁的**条件里还有别的东西**：

| 额外条件 | 出现次数 |
|---|---|
| `IsOutdoors: true` | **208 / 208**（全部）|
| `LocationContext \|contains=Island, Desert: false` | 53 |

**原版的季节机制不区分室内/室外**（§3 已注：「仅室外已确证」是**地图 tilesheet** 那条，
而这里是 `Mods/*` 图集，室内外之分更无从表达）。

> **所以这一格的结论是：能收敛，但收敛后有语义损失 = 把「室外」这一维丢掉。**
> **必须标「近似」并统计受影响条数（208），不得标「无损」。**

### 10.3 这条案例教给发射表的三件事

1. **收敛率是可量化的**：75%（156/208）。每一格都能这样算出自己的数。
2. **「最简形式」常常装不下条件里的第二维度** —— 这正是 §9.4 说的 `Precondition` 缺口，
   现在有了**具体计数**（208 条带 `IsOutdoors`）来驱动它。
3. **「收敛」与「等价」是两件事**：`EmitPlan` 需要再一个字段区分
   **无损收敛** vs **近似收敛**，否则报告会把丢语义的转换算成满分。

### 10.4 结论（直接回答「到底能不能」）

> **能收敛，且已量化到具体数字**：
> 在这一类上 **208 条 → 52 个最简形式（−75%）**。
> 但**只有条数能覆盖的格子才算「有」**，且**带第二维度的必须降级为「近似」**。
> 余下的格子按 §9.5 逐格精算，**一格一格往上推**。

---

## 11. ★ 第二轮精算：五份普查 + 自我纠正（2026-10-06 晚）

五份新研究（`docs/research/2026-10-06-{schedules-season-capacity,map-condition-slots,events-key-preconditions,long-tail-assets,festival-map-dispatch}.md`）
推翻了本表**两格**，并逼出**第三个轴**。

### 11.1 被推翻的两格（都是我先前的判断）

| 格子 | 原判 | 实际 | 依据 |
|---|---|---|---|
| `条目键 × 季节` | ❌ 无 | ✅ **有**（`spring`/`spring_13`/`spring_Sun` 进键名） | `NPC.cs:5822,5861,5878` |
| `地图 × 季节` | ✅ 有 | ⚠️ **装不下**（语料 169 条全是 `SetProperties`） | `GameLocation.cs:16417` |

**两处的引用我都引错了**：`Game1.cs:8296` 是**邮件**键、`GameLocation.cs:16442` 是**地图 tilesheet 前缀** —— 都不是日程判定点。

### 11.2 ★ 第三个轴：资产类型

四个代理的实证共同指向：**同一 `(载体, 维度)` 在不同资产上最简形式不同**。

- `婚姻 × 条目键`：`Data/Festivals` 用 `<NPC>_spouse`，**`Data/Events` 用 `O <NPC>`**
- 两种 `schedules`：`Characters/schedules` 的季节进键名是 `Native`；
  `Strings/schedules` 是 `$query` 宿主而**只有两分支**（`Dialogue.cs:651` 的 `Split('|')` 不带 limit）→ `CapacityExceeded`
- 通用表里的「**条目键**」格**全部撤下** —— 留一条过宽的通用格，会让没查过的 `Data/mail` 被误判成「有原生形式」

**表结构**：`Lookup(载体, 维度, 资产类型)`，细分格优先，退回通用格。

### 11.3 新增：`Data/Events`（此前零覆盖）

事件条目键自带**前置条件段**（`<ID>/<条件>/…`），令牌 **102 个**（54 规范名 + 48 别名，`Preconditions.cs`）。
**通用 GSQ 入口是 `G`**（`Preconditions.cs:744-753`）——
⚠ **不是 `x`**：`x` 是 `SendMail` 伪事件（`:666-683`），发信并**恒返回 false**。
容量无上限（实测最深 27 段）。**四维并集约 181 条，全部可转。**

三处语义差必须标出：`O` **含订婚**、`Hearts` 是**点数**（1 心 = 250 点）、
年份**只有连续区间能表达**（`G YEAR <min> <max>`）。
> ⚠ 本节原写「`IsOddYear` 只能走 `G`」—— **2026-10-06 订正：这是错的**。
> GSQ 的 130 个关键字里**没有任何年代奇偶**（`YEAR` 只吃闭区间，`GameStateQuery.cs:411-422`），
> `y <n>`（n≠1）又是 `>= n` 不是相等（`Preconditions.cs:551-566`）。
> **奇偶年在原版任何机制里都表达不了**；`IsOddYear` 是 Ridgeside 内容包**自建的 DynamicToken**。

### 11.4 ★ 熵的重新测量：把虚高挤出来

同一语料、同一口径，只换表：

| | 第一轮表 | 第二轮表 |
|---|---|---|
| 有最简形式 | 1805（66%）| **1526（55%）** |
| 已确证无 | 682（24%）| **1186（43%）** |
| 未定 | 246（9%）| **21（0.8%）** |

**「有」下降 279 条不是退步，是纠正**：`季节 × 地图` 那 258 条从「有」改判为「无」。
**第一轮的 66% 是虚的。** 这正是「未定必须独立存在」这条纪律的意义 ——
把「没查」算成「有」会虚报，把「查错了」算成「有」会**更**虚报。

### 11.5 已落地为代码（735 项测试）

| 修复 | 内容 |
|---|---|
| `EditData.Fields` | 分隔字符串条目的字段键是 **0 起下标**；原先按属性名处理会**把整条覆盖成 JSON 对象**（破坏性）|
| `SpouseKeyRewriter` | 认 `Spouse`/`Roommate` **值令牌**拼写（NPC 在值侧），此前只认 `Relationship` |
| `TokenTranslator` | `Spouse`/`Roommate` 的 GSQ 翻译（此前**恒失败**）；多 NPC 用 `ANY` 分组 |

**全量语料实测收益**（32 包 / 6538 条）：`Converted` 1483→1558（**+75**）、
`written` 3041→3145（**+104**）、`writeFailed` 396→367（**−29**）。

### 11.6 ★ 待办：表有两份，必然漂移

C# 的 `EmitterTable` 与 `tools/variant-entropy.py` 的 `EMITTER` **是两份手工同步的表** ——
本轮就漂移过（Python 侧还留着旧结论，重跑才发现）。
**下一步应把归一化 pass 接进 CLI 报告**，让 Python 前端退役，表只剩一份。

### 11.7 疑点已查清（2026-10-06 补充）

§10 那 208 条打在 `Mods/*` 图集上，而 `Seasonal` 机制认 `Data/BigCraftables` 的 Name ——
当时判定「两者对不上」。**现已查清：对得上。**

实测那条链路是：

```jsonc
{ "Action": "Load", "Priority": "Low",
  "Target": "Mods/FlashShifter.StardewValleyExpandedCP/Little Shrub",
  "FromFile": "assets/Items/BigCraftables/Little Shrub.png" }      // ← 从 BigCraftables 目录 Load
{ "Action": "EditImage", "Target": "Mods/…/Little Shrub",
  "FromFile": "assets/tilesheets/SeasonalJA.png",
  "When": { "Season": "Spring", "IsOutdoors": true }, … }          // ← 四季各一条
```

**它就是 BigCraftable 的贴图**（`Load` 自 `assets/Items/BigCraftables/`），
只是走了 CP 的 `Mods/<UniqueID>/` 虚拟资产命名空间，**从名字上看不出来**。

**机制本身（`Object.cs:1940-1943`，已逐字核对）**：

```csharp
if (bigCraftable.Value && name.Contains("Seasonal"))
{
    int num = base.ParentSheetIndex - base.ParentSheetIndex % 4;
    base.ParentSheetIndex = num + location.GetSeasonIndex();   // 春0 夏1 秋2 冬3
}
```

配套还有 `Object.cs:1428`（减回 4 的倍数）与 `:2918`（复位）。

**所以最简形式是**：四季图放 **4 个连续索引**（`4k..4k+3`），
并把 `Data/BigCraftables` 的 `Name` 加上 `Seasonal` —— 零条件。

⚠ **仍是「近似」不是「无损」**：`GetSeasonIndex()` **不看室内外**，
而 SVE 那 208 条**全带** `IsOutdoors: true` —— 这一维会丢。

⚠ **发射尚未实现**：需要①改 `Data/BigCraftables` 的 `Name`、②把 4 张变体画到连续索引上。


