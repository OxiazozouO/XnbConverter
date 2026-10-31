# `EditMap` 写路径上**还剩**的两处与 CP 的偏差（当前语料里都不可见，**故意先不改**）

日期：2026-10-09

对着 CP `EditMapPatch.Edit`（`ContentPatcher.decompiled.cs:13860-13930`）逐段核完，
这两处不一致**没有改**。理由写在最后。

---

## 偏差一：`FromFile` 应用失败时，我们整条作废，CP 只警告后继续

CP 原文：

```csharp
if (AppliesMapPatch)
{
    Map source = base.ContentPack.ModContent.Load<Map>(base.FromAsset);
    if (!TryApplyMapPatch(source, val, out string error))
        WarnForPatch("map patch couldn't be applied: " + error);   // ← 只警告
}
if (AppliesTilePatches) { /* 逐块警告，继续 */ }
foreach (EditMapPatchProperty p in MapProperties) { … }            // ← 照做
if (AddWarps.Any()) { ApplyWarps(…); … }                          // ← 照做
for (…) TryApplyTextOperation(…)                                  // ← 照做
```

我们：`if (!merged.Success) return merged;` —— **整条作废**，
同一补丁里的 `MapTiles` / `MapProperties` / `AddWarps` / `TextOperations` 全丢。

**语料影响：0。** 含 `FromFile` + 其它字段的 `EditMap` 全语料只有 **7 条**
（6 条 `FromFile+MapProperties`、1 条 `FromFile+AddWarps`），
它们所在的三个包（lnh额外区域 / 社区中心扩建 / 鸟会）报告里 `writeFailed` **全为 0**，
即这些 `FromFile` 都应用成功了，没走到分叉。

## 偏差二：`FromFile` 指向的文件不存在时，CP 是「整条 `return`」，我们是 `Fail`

CP：

```csharp
if (AppliesMapPatch && !FromAssetExists())
{
    WarnForPatch($"the FromFile file '{base.FromAsset}' doesn't exist.");
    return;                     // ← 注意是 return，整条补丁什么都不做
}
```

我们：`ApplyFromFile` 返回 `Fail` → 报告里记一条**写入失败**。

**行为（写不写）两边一致**（都是什么都不做），差的是**报告口径**：
CP 静默警告，我们记成 `writeFailed`。所以这条本身不算错，
但会让「因为输入坏而没写」看起来像「我们写的时候出错」。

**语料影响：0。** 全语料带 `FromFile` 的 `EditMap` 共 **480 条**，
`FromFile` 指向的文件**找不到的 0 条**（`tools` 里那份扫描可见 `scan/fromfile_missing.py`）。

---

## 为什么先不改

两处都只在**输入本身就坏**（补丁块尺寸越界 / 来源文件缺失）时才分叉。要改得对，
得先把 `ApplyFromFile` 的失败**按种类分开**（"CP 会整条 return" vs "CP 会警告后继续"），
否则就会把「我们写的时候出错」和「输入坏所以没写」混成同一种静默通过 ——
**那正是本仓库反复点名的坑**（把判定不准伪装成写入出问题，反过来也一样糟）。

⇒ 记在这里，等真的遇到用这种写法的包再动。**两处都不是"该做没做"，是"故意留着 + 记着"。**

## 已经改掉的两处（对照）

同一天改掉的两处是**语义确实写歪**的，与上面两类不同：

| 偏差 | 语料影响 | 状态 |
|---|---|---|
| `TextOperations` 把整张属性表按字符串重写（CP 只动点名那一个）| 0（SVE 那几张被改过的地图恰好没有非字符串属性）| **已修**，见 `2026-10-09-textoperation-map-properties-rewrote-whole-table.md` |
| 五段改动的**顺序**与 CP 不一致（TextOperations 排到了最前）| 0（7 条多字段补丁没有一条同时含 TextOperations）| **已修**，测试 `MapProperties先于TextOperations生效` |
