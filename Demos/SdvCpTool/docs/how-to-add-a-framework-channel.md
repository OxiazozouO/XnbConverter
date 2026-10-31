# 怎么给一个**非 CP 框架**加一条通道（照 FTM 那套做）

这份是**做法**，不是结论。结论看 `research/2026-10-11-non-cp-frameworks-what-can-bake.md`
（哪些框架值得做、哪些没有落点）。

FTM 那条通道（`Cp/FtmPackTranslator.cs`）是**唯一已经落地**的样板：4 个包、1000+ 条内容、
零下游改动。**将来遇到真实的服装框架包 / MFM 内容包，照这六步走。**

---

## 0. 先判"值不值得做"（别先写代码）

三问，任一为否就停：

1. **它的内容有没有原版的落点？**（读 `D:\sdv_gd` 的模型类 + `D:\sdv_src` 的消费代码，
   看"游戏会不会读这张表/这条字段"）—— 只看"框架怎么实现"不够，**要看游戏读什么**；
2. **本机有没有真实语料？**（只有框架自带的模板包 ⇒ 做完也**没有可验证的对象**，
   与服装那条一样：记录、等语料）；
3. **落不下去的那部分能不能如实报出来？**（"看得见，不是静默丢"是底线）

## 1. 认包：manifest 里的 `ContentPackFor.UniqueID`

```csharp
private static bool IsXxxPack(string packDirectory)   // 与 FtmPackTranslator.IsFtmPack 同形
```

**不要**靠目录名/文件名认（语料里的目录名带中文、方括号、序号）。

## 2. 翻译：**现场翻成 CP 形状的补丁集**，下游一行都不用改

这是这套做法的核心：框架自己的格式 → `{ "Action": "EditData", "Target": …, "Entries": {…} }`，
然后**全部交回既有流水线**（判定 / 写入 / 报告 / 自检都不用知道这个框架存在）。

```csharp
public static XxxImportResult? TryImport(string packDirectory)   // 不是这个框架的包 → null
```

翻的时候把"**落地**"与"**落不了**"分开记账：

- 能落的 → 补丁（FTM：`Forage`/`ArtifactSpots` 塞进 `Data/Locations`）；
- 落不了 / 只能用别的形态表达 → **写进 notes**（FTM：刷怪 246 个 area、宝箱、"一次性"改成邮寄）。
  ⚠ notes 里要**说清是"没写"还是"换了形态"** —— 后者必须在装机文档里也提一句。

## 3. 挂进 `ContentPack.Load`（在 CP 解析之前）

```csharp
FtmImportResult? ftm = FtmPackTranslator.TryImport(packDirectory);
if (ftm != null) { pack.FtmNotes = ftm.Notes; pack._units = pack.CollectUnits(contentJson, ftm.Content); }
```

⚠ 挂完记得看一眼**这一步之后的每一处**是否需要知道框架的存在 —— 理想是**不需要**（FTM 就是）。

## 4. notes 要进**报告**，不能只打 stdout

`report.Notes` 由 `pack.Warnings` + `pack.FtmNotes` + 本轮其它说明拼成。
"哪些内容拿不到"是只看报告的人最需要的信息 —— 只打终端等于没写（FTM 与 `CustomLocations`
都踩过这个）。

## 5. 字段覆盖：**把框架的真实键枚举出来，与自己读的逐个对**

这是**最容易漏**的一步（FTM 就漏了两个字段：`<段>SpawnEnabled` 与 `File_Conditions`，
语料里恰好不触发才没出事）：

```bash
# 把语料里这个框架所有包的键按层级数一遍（顶层 / 段级 / area 级都要数）
```
再对照 `src/` 里实际读的键 —— **差的那些要么补上、要么如实报出来**。
⚠ 别忘了**大小写**：CP 的补丁字段是大小写不敏感的（JSON.NET 反序列化，
见 `2026-10-11-patch-field-guard-and-case.md`），框架的字段同理要先核一遍。

## 6. 验收（**每一步都要受控对比**）

1. **写测试**：每条映射规则一条 + "不是这个框架的包 → `null`"一条 + 每类"落不了"各一条；
2. `dotnet test -c Release` 全绿；
3. **同参数跑全语料**（`python tools/convert-mods.py <新目录>`），然后：
   - **产出树逐字节比**（`compare-trees-by-hash.py` / `diff-trees.py`）—— 期望**只多该多的**；
   - **报告 notes 对比** —— 期望"少报"变"实际"；
   - **十行自检**（`verify-tree.py`）不退化；
4. 新产出的资产**定点回包**一次（`pack-check.py`）；
5. 记账：`research/` 一篇 + `known-losses` 一条 + `docs/README` 索引一行 + 装机文档里
   "要跟玩家说清楚"的那节（如果玩家能感知）。

---

## 附：别做的三件事

- **别在"落不了"上硬凑**：没有落点就是没有（FTM 刷怪试过"换地点类"四条路、
  地图属性全表、`TriggerActions` 动作表全文 —— 3 条独立证据都没有）。
  宁可**报出来**，也不要写一条"看着像、语义不对"的。
- **别碰我们自己的产物格式**去迁就某个框架（该改的是翻译层）。
- **别把"框架能实现"当成"游戏会读"**：判据永远是**游戏的消费代码**。
