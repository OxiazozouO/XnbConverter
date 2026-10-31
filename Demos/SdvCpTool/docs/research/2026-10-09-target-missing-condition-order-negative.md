# 负结果：「目标缺失时先报条件」**看着更准，实际是错的**

日期：2026-10-09

## 1. 动机（看着很有道理）

判定链是「常量折叠 → 查目标存在」。能静态求值的叶子折得掉
（整条恒假 ⇒ `NotApplicable`，顺序是对的），但含**运行时**令牌的条件折不掉：

```json
{ "Action": "EditMap", "Target": "Maps/Farm_FrontierFarm",
  "FromFile": "assets/Maps/Critters/Owl.tmx",
  "When": { "FarmType": "FrontierFarm", "Time": "{{Range: 2000, 2600}}",
            "Season |contains=Winter": false, "Random: {{Range: 1, 20}}": 1 } }
```

`{{Range}}` / `FarmType` / 自定义 config 折不掉 ⇒ 掉进目标缺失分支 ⇒ 报
**`TargetMissing`**（"该资产不在提供的资源树中；若它是 mod 自带的，请把 mod 的资源也解包进来"）。

**这句话是误导**：`blocker != null` 的补丁**无论如何都不会写**（判定链后段一律降级人工），
与目标在不在**无关**。照它去解包 mod 资源，这条照样写不出来。

量化（`tools/targetmissing-with-condition.py`，新增）：

```
TargetMissing 中**带 When** 的: 107
TargetMissing 中**不带 When** 的: 160
```

⇒ 于是动手：在目标缺失分支里，`hasCondition && blocker != null` 时先报条件。

## 2. 受控对比（32 包，同参数）

| 项 | 改前 | 改后 |
|---|---|---|
| total | 7048 | 7048 |
| **convertible** | 5672 | **5995（+323）** |
| blocked | 1151 | 828（−323）|
| **written** | 3717 | **3717（±0）** |
| writeFailed | 8 | 8 |
| NeedsReview | 1129 | **1452（+323）** |
| TargetMissing | 267 | 210（−57）|
| LocalEdit | 551 | 496（−55）|
| NoLandingPoint | 548 | 337（−211）|

**`written` 一条没变** —— 只是把 323 条在判定档之间搬家。

## 3. 为什么它是错的

### 3.1 `convertible` 把 `NeedsReview` 算作「可合并」

`AnalysisReport.cs:82-87`：

```csharp
public int ConvertibleCount =>
    Summary[Verdict.Converted] + Summary[Verdict.ConvertedNoCondition]
    + Summary[Verdict.NeedsReview] + Summary[Verdict.CreatesNewAsset]
    + Summary[Verdict.NotApplicable];
```

⇒ 把补丁挪进 `NeedsReview` 会**直接让「判定可合并」虚涨 323**，而一条都没多写。
`TargetMissing` / `LocalEdit` / `NoLandingPoint` 则**不计入** convertible。

**教训**：判定分类的改动**不是零影响的** —— 它会动报告的头条数字。
「只改分类不改产出」是个**错觉**。

### 3.2 理由被**降级**了，不是被改准了

`EditMap` 那 55 条（`Maps/Custom_DesertRoad_04` 一类）：

- 原：`LocalEdit` —— 「条件化的地图改动在原版没有通道（图块级只有 `ConditionalDoor`，且不适用于此）」
- 新：「条件翻译不成 GSQ：条件 `Time` 的值里嵌套了令牌」

**原理由更根本**。`docs/research/2026-10-06-map-condition-slots.md` 已确证：地图里能承载条件表达式的位点只有
`ConditionalDoor` / `BuildConditions` 两个，**都改不了图块内容**。
⇒ 就算那条条件翻译得成 GSQ，**地图也没地方放**。新理由把「没有通道」说成了「条件不会翻」。

那 208 条 `Mods/FlashShifter.StardewValleyExpandedCP/*` 图片资产同理：
根本原因是「自定义资产 / 图片没有落点」（`NoLandingPoint`），换成「条件」也是变浅。

⇒ **判定链「从具体到一般」的顺序是对的**：目标存在性、落点机制（具体）优先于条件（一般）。
「先报条件」的直觉是反的。

## 4. 决定

**回滚**。`git checkout -- src/SdvCpTool.Core/Judging/PatchJudge.cs tests/SdvCpTool.Core.Tests/PatchJudgeTests.cs`，
工作区回到 `d6fa001`（`corpus_anl_msg` 用的就是那份代码，逐字节等同，无需重跑）。

`tools/targetmissing-with-condition.py` **保留** —— 它的量化（107/160）依然成立且有用，
只是结论从「该改」变成「**不该**改」。它的注释已改成记录这个负结果。

## 5. 这条记录**不覆盖**什么

只核了「改成先报条件」这一种做法。**没有**去试
「`TargetMissing` 保持原样、但在 reason 里**附一句**『它同时还带一个判不了的条件』」——
那既不搬动分类、又能让读者看到第二重原因。成本低，但**收益也低**（读者本来就会看到
这条补丁的 `When`），没有做。
