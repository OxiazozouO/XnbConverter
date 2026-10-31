# 修一处与 CP 不一致：地图属性上的 TextOperations 会把整张表按字符串重写

日期：2026-10-09
类别：**行为修正**（不是判定改动 —— 改前改后报告数字一模一样）

## 症状

`EditMap` 的 `TextOperations` 指向 `MapProperties` 时，我们原来是这么做的：

```csharp
var mirror = new JObject(map.Properties.Select(p => new JProperty(p.Key, Stringify(p.Value))));
MergeOutcome merged = TextOperationMerger.Apply(mirror, ops, assetName);
WriteBackAll(map, (JObject)merged.Data!);      // ← 把**整张属性表**写回
```

而 `WriteBackAll` 里每个属性都写成 `Type = StringType`（3）。tbin 的 `Propertie.Type`
是 0=bool / 1=int / 2=float / 3=string —— 也就是说：

- **别的属性也被一起重写**（哪怕文本操作没点它的名）；
- 它们的**类型被改成字符串**（1/2 → 3）。

## 权威实现怎么做

CP `EditMapPatch.TryApplyTextOperation(Map target, ITextOperation operation, …)`
（`ContentPatcher.decompiled.cs:14136`）：

```csharp
if (operation.Target.Length > 2) return Fail("…must only have one other segment…");
string name = operation.Target[1].Value;
string existing = Properties.TryGetValue(name, out var t) ? t : null;
Properties[name] = PropertyValue.op_Implicit(operation.Apply(existing));
```

⇒ **只动点名的那个属性**，别的属性一个字节都不碰。

## 修法

把 `WriteBackAll` 换成 `WriteBackNamed(map, merged, ops)` —— 只写回
`ops` 里每个 `Target[1]` 点到的键。原 `WriteBackAll` 随之删除（我的改动把它变成了孤儿）。

## 验证

1. **新测试**（`MapApplierTests`，2 条）：
   - `文本操作只改点名的属性其余原样` —— 先在输入树上造一个 `Type = 1` 的 `IntProp`，
     再对另一个属性跑 `Append`；断言 `IntProp` 的 `Type` 仍是 **1**、值仍是 `7`。
     **改之前这条是红的**（`Expected: 1, Actual: 3`），改之后绿。
   - `文本操作点名不存在的属性时按空串处理`（对齐 CP 的 `TryGetValue(…, ref "")`）。
   - 全量测试：**1194 通过 / 0 失败**（改前 1192）。
2. **端到端受控对比（同参数）**：用改前/改后两次 `chain-packs.py` 把 **SVE** 转进新树，
   逐文件比 md5 —— **10249 个文件，0 个不同**；报告里
   `total 4542 / written 2588 / writeFailed 3` 改前改后完全一致。

⇒ 这处不一致**在当前语料里不产生可见差异**（SVE 被 TextOperations 改过的那些地图
恰好没有非字符串属性），但它是实打实的语义偏差，遇上 `Type` 非字符串的地图就会写歪。

## 只剩一条已知偏差（未处理）

`WriteBackNamed` + 镜像仍会把**点名属性**的值写回成字符串类型 —— 这与 CP 一致
（CP 也是 `PropertyValue.op_Implicit(...)`），所以**不是**偏差。
