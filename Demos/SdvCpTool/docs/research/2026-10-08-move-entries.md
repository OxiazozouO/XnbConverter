# 补上 `MoveEntries`：此前整块被忽略

日期：2026-10-08

## 1. 缺口

CP 的 `EditData` 有四个字段，应用顺序是
**`Entries` → `Fields` → `TextOperations` → `MoveEntries`**（`EditDataPatch.ApplyEdits`）。
我们实现了前三步 —— **`MoveEntries` 一行都没做**，只在 `EditDataMerger` 里留了一句注释。

实测语料：**47 条** `MoveEntries`（6 个包），主要打在 `Data/Shops`：

| 资产 | 处数 |
|---|---|
| `Data/Shops` | 71 |
| `Data/Machines` | 19 |
| `Data/FishPondData` | 4 |

它们此前**全部是空操作**：mod 想把自己的条目插到某个原版条目旁边，结果条目被追加在末尾。

## 2. 语义（反编译 `ContentPatcher.dll` 2.9.1）

```csharp
// EditDataPatch.ApplyMoveEntries
if (!MoveRecords.Any()) return;
if (!editor.CanMoveEntries) { warn; return; }        // ← 只有列表能挪
foreach (record) {
    MoveResult r;
    if (record.ToPosition is Top or Bottom)
        r = editor.MoveEntry(key, record.ToPosition);
    else if (record.AfterID.IsMeaningful() || record.BeforeID.IsMeaningful())
        r = editor.MoveEntry(key, anchor, afterAnchor: record.AfterID.IsMeaningful());
    if (r != Success) warn;                          // ← 记警告后继续，不抛
}
```

```csharp
// ListKeyValueEditor.MoveEntry(key, anchorKey, afterAnchor)
if (index == anchorIndex) return AnchorIsMain;
int target = afterAnchor ? anchorIndex + 1 : anchorIndex;
if (index < anchorIndex) target--;                   // ← 先删后插，下标要回挪一位
Data.RemoveAt(index); Data.Insert(target, value);
```

要点四条，都写进了实现与测试：

1. **只对列表有效** —— `CanMoveEntries` 只有 `ListKeyValueEditor` 为真；打在对象上时 CP 记警告后整条跳过；
2. `Top`/`Bottom` → 删掉再插到 0 / 末尾；
3. 锚点移动**必须先删后插并回挪下标**，否则往后挪会差一格；
4. **逐条独立**：挪不动（条目/锚点不在、锚点是它自己）只记警告，不带走上层也不挡后面几条。

另外两条从语料里读出来的：

- 键与 `Entries` 同一套（`#<整数>` 是下标，其余按 `Id`/`ID`/字符串匹配）；
- 字段名**不区分大小写**：语料里 `Id`/`ID`、`AfterId`/`AfterID`、`BeforeId`/`BeforeID` 并存。

## 3. 受控对比（6 个用了 MoveEntries 的包，同机同参数，改动前后各一遍）

`written` / `writeFailed` **逐包不变** —— 这条只改顺序，不改「写不写」。

产出树按预期变动，抽查一条（「新武器」的 `Data/Shops.AdventureShop`）：

| | `GoldenKhopesh` | `(W)5` | `TheGravestone` |
|---|---|---|---|
| 改动前 | 下标 53（追加在末尾） | 14 | 下标 52 |
| 改动后 | **14**（`(W)5` 之前） | 15 | **16**（`(W)5` 之后） |

mod 原文就是 `BeforeId: "(W)5"` 与 `AfterId: "(W)5"` —— 一字不差。

三个小包定点回包仍然 0 失败（2/0、28/0、53/0）。

## 4. 已知缺口（暴露面 0，如实记）

`MoveEntries` **单独存在**（没有 `Entries`/`Fields`/`TargetField`）**且带真条件**时，
条件没法写进任何原生落点：写入器那条「算不出落点就当空操作」的近路会**不写盘**，
而报告仍记 `written` —— 也就是**静默丢掉那一条挪动**。

全语料只有 **1 条**「只有 MoveEntries 且带 `When`」的补丁（聚宝盆制作的 `keg.json`），
而它的 `When` 是 config 令牌、烘焙成真 ⇒ `gsq` 为空、根本不进那个分支，**实测暴露面 0**。

所以这次**不为它加机器** —— 真要为它写代码，最可能的写法是「无条件把条目挪了」，
而那与「绝不产出无条件版本」的规矩相反，反而更糟。留在这里备查。
