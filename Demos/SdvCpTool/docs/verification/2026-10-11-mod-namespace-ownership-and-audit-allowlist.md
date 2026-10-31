# 复核「`Mods/` 目标」那条结论：数重算了、**判据也换了**、自检回到 0

日期：2026-10-11（晚）
起因：接着查「文档里的**结论性否定断言**能不能对得上报告数据」——上一轮用这个办法逮到
`2026-10-09-non-cp-framework-channels.md` §7.2 的错报，这一轮把同一节**从头重算**，
结果又逮到两层问题（一层是数，一层是**判据本身**）。

---

## 1. 数错了：手搓聚合少了 17 条、错配了 10 条

原文的表（`Mods/` 目标 654 条：自己 616 / 别人 38，"转了 623"）是用临时脚本聚合的，两处错：

| 错 | 是什么 |
|---|---|
| 少算 17 | 只数了带 `Mods/` 前缀的写法；`{{ModId}}/…`（不带前缀）解析出来是光秃秃的 `<UID>/…`，被漏掉 |
| 错配 10 | "别人"那格把**跨包写进 SVE 命名空间**的 10 条混了进来 —— 它们的命名空间是 SVE 的，但补丁来自包 27/28 |

重算（判据固化进 `tools/namespace-ownership.py`，命令见下）：

```
=== 36 包、由 tools/namespace-ownership.py 产出 ===
**自己**的 UniqueID：633 条
    Converted 414 / Emitted 208 / ConvertedNoCondition 6 / NoLandingPoint 4 / TargetMissing 1
**别人**的 UniqueID：81 条
    NoLandingPoint 63 / Converted 6 / NotApplicable 6 / ConvertedNoCondition 6
原版资产名：6572 条
⇒ 自定义命名空间共 714，写了 640（90%）
```

命令（可复现）：

```bash
python tools/namespace-ownership.py D:\tmp\prof32 --tree D:\tmp\prof32\tree
```

## 2. 判据错了（这层更要紧）：「归谁」≠「有没有消费方」

原结论默认「写在别人命名空间下 ⇒ 没有消费方 ⇒ 不写」。但**同一份产出里就有反例**：
10 条是**别的包**写进 SVE 命名空间的，我们照写，而且**是对的** —— 因为游戏会读它们：

| 补丁 | 目标 | 谁写的 | 消费方 |
|---|---|---|---|
| `EditImage` ×6 | `Mods/FlashShifter.StardewValleyExpandedCP/SVE_Portraits` | 包 27（SVE 季节性服装）| SVE 自己的 `Data/Characters` 立绘引用 |
| `Load` ×4 | `Mods/FlashShifter.StardewValleyExpandedCP/{spring,summer,fall,winter}_GrampletonFields` | 包 28（格兰普尔顿旷野）| SVE 地图的图集名 |

⇒ 判据应当是「**产出树里的数据资产有没有引用它**」，不是「它挂在谁的命名空间下」。
这一侧由 `tools/audit-texture-refs.py` 在管（`Texture` / `Portrait` / 图集名…）。

**"没写的那些到底丢没丢"** —— 新加的 `--tree` 那一步按 **JSON 解析后逐值**比（不按文本比：
JSON 源码里的路径是 `A\\B`，文本比会漏，我第一版就漏了、得到假的"0 命中"）：

```
== 没写的自定义命名空间目标：16 个资产名（产出树扫了 2762 个 JSON）
   ★ 被引用、且树里连文件都没有：0 个        ← 没有真丢
   ○ 被引用，但树的文件在：2 个
       Mods/FlashShifter.StardewValleyExpandedCP/Hedge Fence Tilesheet        （3 条 EditImage）
       Mods/FlashShifter.StardewValleyExpandedCP/Small Hardwood Fence Tilesheet（1 条）
```

那 2 个是**栅栏贴图带季节条件**：`Data/Fences.json` 的 `Texture` 指的正是它，**基图写了**；
原版没有"按季节换栅栏贴图"的通道 ⇒ 落不了的只是季节变体的那几条（`…-buckets…` §9 已记账）。

## 3. 自检第十行的唯一"多出"是 **SVE 自己的笔误**

`verify-tree.py` 第十项（数据资产里的贴图引用）原来报「产出比基准多出 1」。查清了：

- 出处：`code/Shops/SVE/AxelVendor2.json:13` 写的是
  `Mods\FlashShifter.StardewValleyExpandedCP_Aged_Blue_Moon_Wine\Axel_Vendor`
  —— 把**物品 id**（`…_Aged_Blue_Moon_Wine`）当成了路径的一段；同目录 `AxelVendor1.json:17`
  写的就是对的 `Mods\FlashShifter.StardewValleyExpandedCP\Axel_Vendor`；
- **没有任何补丁创建那个名字**（全 SVE 搜过：`Aged_Blue_Moon_Wine` 的 23 处是物品 id/GiftTaste，
  只有这一处当路径用）；
- 游戏侧有保护：立绘取不到 → 退化成"按名字找 NPC 立绘" → 再取不到返回 `null`
  （`ShopMenu.cs:800-813`）⇒ 商店不显示立绘、**不崩**；CP 场景下同一个字符串**原样**交给游戏
  ⇒ 两边行为一模一样。

⇒ 进了 `audit-texture-refs.py` 的 `UPSTREAM_DANGLING` 白名单（**要写清源码出处才准进**，
且**只在"上游笔误"这一栏单独列出来**，不从报表里消失）。自检第十行回到 **0**。

## 4. 顺手记两个"分析口径"的坑（都会静默给出错的数）

| 坑 | 表现 | 正确做法 |
|---|---|---|
| `summary` vs `patches[]` | `summary` 是在**三套发射器改写之前**算的（`AnalysisReport.cs:127-130`），`patches[]` 才是最终裁决（发射成功的已改成 `Emitted`）。实测 36 包：`NoLandingPoint` **1058** vs **741** | 一律数 `patches[].verdict` |
| `ContentHashes.json` | 产出树根目录那张表的**键**里有 `Mods/...`，但那是**我们自己**写的"资产存在性表"，**不是消费方** | 要数引用就数 `Data/*.json` 的**字段值** |

（这两条各自都让我先得到过一版错的数 —— 上面 §2 的"0 命中"和对着 §0 以为"文档错了"，
都是这么来的。已写进 `docs/research/2026-10-11-remaining-buckets-…` §0 与 `tools/README.md`。）

## 5. 代码侧：措辞按归属分（**判定不变**）

`PatchJudge` 原来只按"第一段带点 / 是不是 `Mods`"判，**分不出"自己"**：语料里 0 条命中
（这一档 63 条全是真的别人的命名空间），但一旦有包给**自己的**命名空间写条件化新建资产，
报告会说成"别的 mod 注册的自定义资产……纯 XNB 场景里没人读它"，**把自己的内容说成别人的**。

改动：把 `pack.UniqueId` 传进判定，`ModNamespaceReason` 按归属选措辞 —— 自己的那种只保留
"**类型无从得知 ⇒ 打不出来**"这条依据（Cornucopia 那次回包失败就是这条），并明说是**本包自己的内容**。
**判定不变**（两种都还是 `NoLandingPoint`、都不写盘）。

测试（`PatchJudgeTests`，+5 条）：自己的命名空间（含 `Mods/` 前缀与不带前缀两种写法、带条件与不带条件
两条路）走"本包自己的"措辞；别人的照旧；**不给 UID 时不乱认**（仍按"别人的"措辞）。

### 同参数受控对比（改前 / 改后）

同一份语料、同一棵树根（`D:\sdv_tree`）、同一个脚本、只换二进制：

```
A = 改前（git stash 回 HEAD 编的）→ python tools/convert-mods.py D:\tmp\prof31
B = 改后（把改动 pop 回来编）    → python tools/convert-mods.py D:\tmp\prof32

报告：36 个包里 35 个**逐项完全相同**，唯一的差异是 `tree` 字段（prof31\tree vs prof32\tree）
      —— `patches[]` 的 verdict/reason **一条都没变**（0 条补丁不同）
产出树：python tools/compare-trees-by-hash.py（16 进程）
      A 13562 个文件，B 13562 个文件
      只在 A：0    只在 B：0    内容不同：0    ⇒ **两棵树逐文件相同**
单测：1341 通过 / 0 失败（含新加的 5 条）
```

⇒ 这次改动**在语料上零影响**，与预期一致（这一档里本来就没有"写自己命名空间"的命中）；
它改的是"将来遇到时报告会不会说错话"。

⚠ 头一次对比用的是 `tools/run-packs.py`，**不是同参数** —— 它的树根是 `D:\sdv_tree`、
输出布局与 `convert-mods.py` 也不同（`report.tree` 字段、`conditionPlacement.checkedAssets` 2534 vs 2267）。
**换 harness 比出来的差异全是噪声**，这一轮改成两个都走 `convert-mods.py`。

> 追记（同日稍晚）：那两段自检的差异**根因已查明** —— 链式跑法是
> `--tree <同一棵> --out <同一棵> --append`，`checkedAssets` 数的是**整棵累积树**（比逐包大），
> 而且「产出 vs 原版」的形状对照**拿产出跟自己比** ⇒ findings 恒 0（**假 0**）。
> 现在那种情形会置 `skippedSameTree: true` 并写"跳过"。详见
> `2026-10-09-self-audit-findings-reviewed.md` §6.3。

## 6. 未确证 / 未复核

- `probe_maps` 实机（探针编得过、逻辑对着源码核过，**没在游戏里跑过**）；
- `2026-10-10-known-losses.md` §2「DLL 在原版资产上的写入只有 7 个资产」：依据的反编译目录
  （`D:\tmp` 下、不在 `clean-cache.js` 白名单）**已被清掉**，这一轮**没有复核**，重跑要重新反编译 69 个 DLL 模组。
