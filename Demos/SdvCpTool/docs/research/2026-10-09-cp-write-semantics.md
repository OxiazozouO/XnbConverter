# 对着 CP 源码改正的三组写入语义

日期：2026-10-09

反编译 CP 2.9.1（见 `2026-10-08-cp-source-audit.md` 第 0 节的取法）之后，
把**写入侧**三处「与 CP 不一样」的地方逐个订正。三处的共同点是：
我们原先判**整条失败**，而 CP 是**警告后继续**——代价不只是报告数字，
还会把**同一补丁里已经落好的改动一起丢掉**。

---

## 1. `TextOperations`：CP 有**两级**，我们原来只有一级

`PatchLoader.TryParseTextOperations`（解析期）与 `ApplyTextOperations`（运行期）
是两种完全不同的处理：

| 情形 | CP | 我们（原来） | 我们（现在） |
|---|---|---|---|
| `Operation` 缺失/非法 | `TrackSkip`：**整条补丁不创建** | 全落空才失败 | **整条补丁作废** ✓ |
| `Target` 少于两段 | 同上 | 同上 | 同上 ✓ |
| `RemoveDelimited` 缺 `Delimiter`/`Search` | 同上 | 同上 | 同上 ✓ |
| `ReplaceMode` 非法 | 同上 | 同上 | 同上 ✓ |
| 条目/字段在数据里找不到、类型不是字符串 | `WarnForOperation` 后继续，补丁仍算已应用 | 全落空 ⇒ 整条失败 | **只记 note** ✓ |

语料 11 条落在最后那一格，全是**跨包依赖**（条目 `Andy`/`Claire` 等由别的 mod 提供）。
判定侧给的是 `ConvertedNoCondition`（「无条件，直接合并」），写入侧却报失败 ——
正是本仓库点名要避免的「把判定不准伪装成写入出问题」。

### 顺带：`ReplaceDelimited` 不存在

`TextOperationType` 枚举只有 `Append` / `Prepend` / `RemoveDelimited`。
全程序集里 `ReplaceDelimited` **一处都没有**（既不是枚举值，也没有哪个迁移把它改名过来）。
本工具原先自己实现了它（语料 0 处）—— 等于把 CP 会整条丢掉的补丁应用出去，已删。

---

## 2. `Fields`：三处口径

| | 原来 | 现在（照 CP） |
|---|---|---|
| 条目不存在 / 下标越界 / 非数字键 | **整条失败** | 只跳过那一项、记 note |
| 目标是**列表** | `Fields 只能作用于对象` ⇒ 失败 | 按 `Id`/下标找元素再合并 |
| 值为 `null` | **删掉该字段** | **把该字段设成 `null`** |

第三条的依据：CP 对对象条目走 `Serializer.Populate(reader, entry)`，
JSON 里的 `null` 会把成员**赋成 null**（键还在）；
而 `Entries` 的 `null` 才是「删条目」（`RemoveEntry`）—— 两者不是一回事。

第二条的依据：`ApplyFields` 用的还是 `ParseKey`/`GetEntryType`/`GetEntry` 那一套，
只是编辑器换成了 `ListKeyValueEditor`。实测聚宝盆作物就有这么一条：
`TargetField: ["SeedShop","Items"]` + `Fields: { "(O)301": { "Condition": null } }`。

---

## 3. 字段引用令牌：两个「差一层」

这是**最隐蔽**的一处，两个缺陷叠在一起，修一个反而更糟。

### 3.1 `{{FromFile}}` 展开出来的东西自己还带令牌

SVE 季节性服装的惯用写法：

```json
"Target":   "Characters/Monsters/FallenAdventurerFemale, Characters/Monsters/FallenAdventurerMale",
"FromFile": "assets/Characters/Monsters/{{TargetWithoutPath}}.png",
"When":     { "HasFile:{{FromFile}}": true }
```

把**原始** `FromFile` 换进 `{{FromFile}}`，得到的还是 `…/{{TargetWithoutPath}}.png`，
`File.Exists` 必然假。**FromFile 自己也得先展开一遍**。

### 3.2 多目标补丁的令牌要**逐目标**求值

CP 把 `Target` 的逗号列表拆成**互相独立**的补丁，于是 `{{Target}}` 族与 `HasFile`
都是**每个目标各一次**。按整串展开，`{{TargetWithoutPath}}` 会拿**最后一个**目标名
拼出一个必然是假的路径。

⚠ **只修 3.1 不修 3.2 会更糟**：条件从「求不出来（响）」变成「折成假（不响）」——
补丁被判「本就不适用」**静默跳过**。折叠（判定侧 `PatchJudge`）与烘焙（写入侧
`Program.RunBake`）**两处都要逐目标**，只改一处就是这种不响地丢。

口径 = **至少一个目标成立**；有目标求不出来时仍返回「不知道」，绝不把不知道当假。
**不改补丁条数、不拆分补丁** —— 整体拆分今日已测过并回滚（内容 +1、头条吹大 1447，
见提交 `f4825c1`），别再走那条。

### 实测

SVE 季节性服装（同机同参数）：`writeFailed` **10 → 1**，
剩的那条是 `EditImage 目标 Maps/Stadium 不存在`（目标真的不存在，CP 也不会成功）。

---

## 4. 全语料受控对比（同机、同参数、32 包）

| 项 | 改动前（b17） | 改动后（b20） | 差 |
|---|---|---|---|
| 补丁合计 | 6980 | 6980 | 0 |
| 可合并 | 5629 | 5618 | **−11** |
| 阻塞 | 1127 | 1138 | +11 |
| **已写入** | 3679 | **3692** | +13 |
| **写入失败** | **32** | **8** | **−24** |

### 4.1 ⚠ `已写入 +13` 里**只有 1 条是真内容**

逐条查了这 13 条新写的补丁：**12 条是「写了但一个字段都没落」**
（`TextOperations`/`Fields` 的目标条目由**别的 mod** 提供，本包单独转换时资产里没有 ——
跨包依赖）。真内容只多 **1** 条（聚宝盆作物的 `Fields` 打在列表上那条，130 → 131）。

这正是今天早些时候那条教训（提交 `f4825c1`：拆分多目标「内容 +1、头条吹大 1447」）的同一类：
**头条数字与内容不是一回事，必须分开报**。

### 4.2 `可合并 −11 / 阻塞 +11` 是**更准**，不是更差

`NeedsReview 1115 → 1106`、`TargetMissing 258 → 267`。逐条核过（SVE 季节性服装那 10 条）：

```
EditImage Characters/Aaron, Characters/Brianna, …      verdict=TargetMissing
EditImage Characters/Monsters/FallenAdventurerFemale,…  verdict=TargetMissing
```

条件原先**求不出来**（`HasFile` 里还是令牌）⇒ 报「等三通道/需人看」；
现在条件**求出来了**，落点检查才真正跑起来 ⇒ 报「目标资产不存在」。
后者才是真原因（Aaron/GilSprite/FallenAdventurer 都是别的 mod 加的 NPC）。

### 4.3 剩下 8 条写入失败（逐条看过，都不是「我们写坏了」）

| 条数 | 原因 |
|---|---|
| 4 | `对话值内联 GSQ 失败`：条目是**别的 mod 的 NPC**，取不到合并前的旧值 ⇒ 拒绝（不写错） |
| 2 | `MapTiles` 位置越界 / 空位置上没给 `SetTilesheet`+`SetIndex` |
| 1 | `EditImage Maps/Stadium` 目标不存在（CP 也不会成功） |
| 1 | `FromFile` 含 `{{MessyCleanToken}}` —— 查过了：它是**DynamicToken**，值 `{{Random: Messy, …, Clean}}`、条件 `Hearts:Shane` 在 0–8 —— 随机 + 好感度，运行时才知道 ⇒ 拒绝得对 |

## 5. 第四处：`MapTiles` 也是逐块独立

同一模式又出现一次。CP `EditMapPatch` 的 `AppliesTilePatches` 分支：

```csharp
foreach (EditMapPatchTile tilePatch in MapTiles) {
    n++;
    if (!TryApplyTile(data, tilePatch, out string error))
        WarnForPatch($"MapTiles > entry {n} couldn't be applied: {error}");   // ← 只是警告
}
```

它**不抛也不作废补丁**，同一补丁里的 `MapProperties`/`AddWarps`/`TextOperations` 照做。
我们原先逐块 `Fail`。同样分两级：

| 情形 | CP | 我们（现在） |
|---|---|---|
| `Layer`/`Position`/`SetIndex`/`Remove` 自身解析不了 | 解析期 `TrackSkip` ⇒ 整条不创建 | 仍判 `Fail` ✓ |
| 图层不存在、位置越界、图集不存在、空位上没给全 `SetTilesheet`+`SetIndex` | 运行期 `WarnForPatch` | **只记 note，继续下一块** ✓ |

受控对比（同机、同包，改动前后各一遍，并**核对了两轮二进制的哈希确实不同**）：

| 包 | 改动前 | 改动后 |
|---|---|---|
| 鸟类观察协会 | failed 1 / written 47 | **failed 0 / written 48** |
| 女巫沼泽扩展 | 42 / 0 | 42 / 0（未受影响） |

⚠ 但鸟类观察协会那包的 `Maps/Forest-IceFestival.tbin` 的 sha1 **改动前后完全相同** ——
那条补丁只有那一个落不下去的块（CP 那边同样什么都不改）。
⇒ 这一处是**记账变更，不是内容**，如实记。

## 6. 第五处：`AddWarps` 逐条独立

同一模式。CP `ApplyWarps` 把校验不过的收进 `errors` 字典，调用处
`foreach (errors) WarnForPatch(...)`，而**合法的那些照样写进 `Warp`/`NPCWarp` 属性**
（`if (list.Any())`）。我们原先一条非法就整条 `Fail`，把合法的也丢了。

受控对比（5 个用到 `AddWarps` 的包，改动前后各一遍，**并核对了两轮二进制的哈希确实不同**）：
written / writeFailed **逐包不变**、产出树**逐文件零差异**。
⇒ 语料里没有非法 warp，这一处在**本语料上暴露面 0**；
但它的失败模式原本是**响的**（报「格式非法」），响过与否一看就知道，所以「没响过」= 确实没有。

## 7. 这五处的共同教训

**「宁可拒绝」不是免费的。** 判整条失败时，代价不只是报告里多个数字：

1. 同一补丁里**已经落好的改动被一起丢掉**（`Entries` 落好了、`Fields` 判失败 ⇒ 全没）；
2. 判定与写入**口径分叉**，报告把「判定不准」显示成「写入出问题」；
3. 修一半（只修展开不修折叠）会把「响的错」变成「不响的丢」。

一律改为 CP 的「逐项警告后继续」之后，三种代价同时消失 —— 而且**不会写出错数据**：
该跳的项照样跳，只是不再连坐。
