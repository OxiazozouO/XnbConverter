# 修一个真 bug：一条坏条目让整条补丁不写（地图上的字符串显示成裸键）

日期：2026-10-10
类别：**行为修正（用户实机可见）**

## 症状（用户截图）

SVE 的蓝月酒庄地图上，本该是提示文字的地方显示成**裸键**：

```
Strings\StringsFromMaps:BlueMoonVineyard.7
```

## 根因

SVE 的 `code/Other/Strings.json` 用**一条** `EditData Strings/StringsFromMaps` 写了
**1037 个条目**，其中**只有 1 个**（`BlueMoonVineyard.7`）的值里带 `{{FarmName}}`
（玩家农场名，运行时才知道，代不掉）。

我们原来的规矩是「值里含代不掉的令牌 ⇒ **整条不写**」——
于是那 **1036 个好条目全丢**，地图的动作还在，字符串没了 ⇒ 显示裸键。

产出实测：`Strings/StringsFromMaps.json` 只有 **485 条**（全是原版的），
`BlueMoon*` **一个都没有**。

## 权威依据：CP 的粒度是**条目**

CP `EditDataPatch.ApplyRecords`（反编译 :13242+）对每一条记录是
`WarnForRecord(num, …)` 之后 `continue` —— **逐条**取舍，不是整条作废。
我们原来的粒度比 CP 粗得多。

## 修法

1. `ValueTokenSubstitutor.SplitEntries(Entries)`：把条目分成「写得出去」与
   「写不出去（**键或值**里含代不掉的令牌）」两组；
2. `EditDataMerger`：只丢写不出去的那几条，其余照写，并在 `writeNote` 里报出丢了哪些；
3. `WritePolicy.TokenGateBlocks`：只有**一个能写的条目都没有**时才整条拦；
   补丁里 `Entries` **之外**（`Fields`/`MoveEntries`/`TextOperations`）还有代不掉的令牌时，
   仍按老规矩整条不写 —— 那些地方没有逐条取舍的余地。

**顺带修掉一个潜伏 bug**：`ValueTokenSubstitutor.HasToken` **没有 `JValue` 分支** ——
传一个裸字符串进去**永远返回 false**。逐条取舍传的正是条目的值（一个 `JValue`），
少了这一支「坏条目」一个都认不出来（实测：第一版改动因此形同没做，测试直接红了）。

## 验证

**测试先行**（4 条新测试，改前红）：
`EditDataMergerTests.个别条目的值带代不掉的令牌时只丢那几条`、
`EditDataMergerTests.键带代不掉的令牌时也只丢那一条`、
`WritePolicyTests.个别条目带代不掉的令牌时只丢那几条`（含"全坏仍要拦"）；
全量测试 **1202 通过 / 0 失败**（改前 1198）。

**端到端（同参数，SVE 一个包）**：

| | 改前 | 改后 |
|---|---|---|
| `Strings/StringsFromMaps.json` 条目 | 485 | **1399** |
| 其中 `BlueMoon*` | **0** | **13**（含 `BlueMoonVineyard.7`）|
| 报告 `written` | 2588 | **2598**（+10）|

逐字节对比改前/改后两棵树：只有新树有 **4** 个文件
（两条补丁因此**新建**了资产：`Characters/Dialogue/MarriageDialogueLance`、
`Strings/schedules/Lance`）、内容不同 **9** 个（6 个 `MarriageDialogue*` +
`Data/Locations` + `Strings/StringsFromMaps` + `ContentHashes`），**只有旧树有 0**。

⇒ 用户截图那个症状修好了：地图上会显示真正的文字。
