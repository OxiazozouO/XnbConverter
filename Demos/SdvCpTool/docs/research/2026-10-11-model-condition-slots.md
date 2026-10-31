# 条件落点那三条"定位不到"：真因是**条目被令牌门丢了**（顺手修了两个真 bug）

日期：2026-10-11（晚）
起因：`2026-10-11-remaining-buckets-…` 里量化「落点定位不到 / 不接受 Condition」那 **59 条**时，
盯上了 3 条看着像"我们保守过头"的（`Data/PassiveFestivals` 2 + `Data/SpecialOrders` 1 ——
模型类明明有 `Condition` 字段）。**查下来归因错了两轮**，记全过程。

---

## 1. 先纠正一个错判：3 条的真因是**值里含代不掉的令牌**

一开始我以为是"数据里没有先例 ⇒ 判据保守拒绝"，并据此加了「模型兜底」（§4）。
**但改完 A/B 报告一条没变**（36 包逐项相同）—— 追下去才看清：

| 补丁 | 值里的令牌 | 结果 |
|---|---|---|
| SVE `Data/PassiveFestivals` ×2（`…_JojaDay` / `…_CommunityDay`）| **`{{Season}}`** | 条目在**合并**时被丢 |
| 聚宝盆 `Data/SpecialOrders` ×1（`Cornucopia.ArtisanMachines_WaxBarrel`）| **`{{GeorgeSpecialOrderRoses}}`**（动态令牌）| 同上 |

`EditDataMerger` 的**逐条取舍**会把「值里含代不掉令牌」的条目**整条丢掉**
（`ValueTokenSubstitutor.SplitEntries`，见 `2026-10-10-token-gate-is-per-entry-not-per-patch.md`）——
**这是既定行为、也是对的**（写出去就是永不生效的条目）。
于是 `ConditionWriter` 定位时当然找不到它 ⇒ 判 `NoLandingPoint`，理由却是
「**定位不到条件落点条目**」——**真因（条目被丢）没说出来**，读报告的人会去找一个不存在的落点问题。

### 1.1 修法：理由带上合并的说明

`ConditionWriter.RefusalFor` 现在把**合并阶段的 notes 一并写进理由**：

```
定位不到条件落点条目：`FlashShifter.StardewValleyExpandedCP_JojaDay`（这条补丁的合并把条目丢了：
含代不掉令牌的条目 1 个已跳过（FlashShifter.StardewValleyExpandedCP_JojaDay））
```

测试：`定位不到时要带上合并的说明`（`ConditionWriterTests`）。
⇒ 这 3 条**判定不变**（确实落不下去，`{{Season}}` 不是"某一季"而是"当前季"，展开不了），
变的是**报告说的是不是真的**。

## 2. ★ 顺手逮到的真 bug（一）：**条目名里带点 ⇒ 一律定位不到**

`EditDataMerger.ApplyEntries` 用条目名的方式是**字面键**（`obj[prop.Name] = …`，CP 的字典编辑器
同样如此）；而 `ConditionWriter` 定位落点时却把路径**一律 `Split('.')` 下钻**
⇒ **名字里带点的条目永远找不到**。

这不是罕见写法：`FlashShifter.StardewValleyExpandedCP_JojaDay`、
`Cornucopia.ArtisanMachines_WaxBarrel`、`Aimon111.WitchSwampOverhaulCP_GoblinZic`
都是 `作者.模组_东西` 的常见命名。

修法：`ConditionWriter.ResolvePath` —— **先整串找，再按 `.` 分段下钻**
（只有 `TargetField` / 键路径那几种才需要下钻）。
测试：`条目名带点也能定位`。

**A/B 证据**（`prof39` → `prof42`，同参数全量 36 包）：只有 **1 条**判定理由变了 ——
包 25 的 `Aimon111.WitchSwampOverhaulCP_GoblinZic`：

```
改前：定位不到条件落点条目：`Aimon111.WitchSwampOverhaulCP_GoblinZic`   ← 假理由
改后：`Aimon111.WitchSwampOverhaulCP_GoblinZic` 这个位置不接受 Condition（原版在该位置不读条件字段）
```

（`Data/Characters` 的条目**确实**没有 `Condition` 槽 ⇒ 该拒 ✓；改后理由是真的。）
其余 35 包逐项相同、产出树逐文件相同。

## 3. 顺手逮到的真 bug（二）：**"同一个根"的两项自检**

链式跑法 `--tree <同一棵> --out <同一棵> --append` 下，`conditionPlacement` / `outputShape`
是拿产出跟自己比 ⇒ `findings` 恒 0（**假 0**）。已改成报 `skippedSameTree: true` + 报告写"跳过"。
见 `2026-10-11-remaining-buckets-…` §0 与 `2026-10-09-self-audit-findings-reviewed.md` §6.3。

## 4. 那条"模型兜底"要不要留：**留**，但它在本语料 **0 命中**

`ConditionWriter.EntryElementConditionSlots`（两条，逐条核过模型、带出处）：

```csharp
["Data/PassiveFestivals"] = "PassiveFestivalData.Condition",   // PassiveFestivalData.cs:9
["Data/SpecialOrders"]    = "SpecialOrderData.Condition",      // SpecialOrderData.cs:28
```

它修的是**规则本身**：数据判据是「这个位置或它的同级**已有** `Condition`」——那要求**原版数据里
有人用过**；可有些资产的模型有字段、原版数据一个先例都没用 ⇒ 判据会**保守拒绝**。
兜底只在「表里有这个资产」**且**「条目的容器就是资产根」（`ReferenceEquals`）时生效
（`TargetField` 那种嵌套位置不适用 —— 表描述的是**资产自己的条目类型**）。

⚠ **本语料里它 0 命中**（那 3 条的真因是 §1 的令牌门）；留着是因为它把**规则**修对了，
将来遇到"模型有槽、数据无先例"的包不会再被误拒。加一条的规矩：**先核模型类、把出处写进注释**
（与 `Assets/ConditionCapacity.cs` 一个规矩）。

## 5. 同参数受控对比

### 5.1 `ResolvePath`（带点条目名）—— 全量 A/B：**1 条理由变了、树逐文件相同**

```
A = prof39（改前）  B = prof42（改后）     两轮都走 convert-mods.py 全量 36 包
⇒ 报告：35 包逐项相同，只有包 25 那 1 条理由变了（见 §2）
产出树：13562 文件逐文件相同
单测：1355 通过 / 0 失败
```

### 5.2 理由带上合并说明 —— 逐包验证（全量那轮**作废**了，教训见图下）

全量 A/B 那一轮（`prof43`）**不能用来下结论**：跑的同时我在往 `Mods` 里合并新语料 ⇒
它跑到一半看到的是**变了形状的语料**（38 个包、前两个是刚并进来的 `FadeHeim's East Scarp Seas` /
`UpgradeBigChest`）⇒ 两次对比的输入不同 ⇒ **作废**（这也是"跑批期间别动 Mods"的活例，
已记进 `tools/README.md`）。

改成**逐包验证**（这条改动只拼接理由字符串、不碰判定，逐包足够）：

```
python tools/run-packs.py D:\tmp\mis3 "大型扩展---SVE---"
⇒ `Data/PassiveFestivals`：定位不到条件落点条目：`FlashShifter..._JojaDay`
   （这条补丁的合并把条目丢了：含代不掉令牌的条目 1 个已跳过（FlashShifter..._JojaDay））
```

### 5.3 模型兜底表 —— **0 命中**（如实记）

`prof39` → `prof42` 的报告逐项比过：那 3 条**没变**（真因是 §1 的令牌门，不是判据保守）。
⇒ 这张表在本语料**没有命中**；留着是修**规则**（§4），不是修数据。

## 6. 没做 / 待办

- `Data/Machines` 那 2 条 `TargetField` 指到 `…Triggers` 的（元素类有 `Condition`）——
  要新增"列表元素落点"才救得回，量化后只值 ≤2 条 ⇒ 不做（见 §3.6 那条）。
- `{{Season}}` / `{{GeorgeSpecialOrderRoses}}` 这类**动态令牌**在值里 ⇒ 条目按既有规矩丢；
  要救得先回答"一个条目怎么表达四个季节"（现在答不了）⇒ 不动。
