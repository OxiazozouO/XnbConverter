# 负结果：「包里物理存在的补丁数」比我们加载的多，**不是**缺口

日期：2026-10-09

## 1. 我量出来的东西（看着像大缺口）

拿「包里所有 `Changes` 数组的元素个数」比「报告里的 `total`」：

```
落差   包                                Changes 元素   我们加载
  456 内容拓展---野生动物 [CP]----------------        666      210
   65 扩展玩法---食谱技能 [CP]----------------        119       54
   59 地图资源---女巫沼泽扩展 [CP]--------------        120       61
   ...
合计 7196 / 6980（落差 216）
```

第一行尤其吓人：**666 条里我们只碰了 210 条**。

## 2. 查下来不是缺口，是**量错了**

那个包的 `content.json` 里没有顶层 `Include`，但 `Changes` 里有 12 条 **`Action: "Include"`**，
分叉在配置上：

```json
{ "Action": "Include", "FromFile": "assets/Map_Patch_High.json",
  "When": { "Enable mod?": "true", "Animal Abundance Level": "High",
            "HasMod |contains=FlashShifter.StardewValleyExpandedCP": false } }
{ "Action": "Include", "FromFile": "assets/Map_Patch_Low.json",
  "When": { …, "Animal Abundance Level": "Low",  … } }
{ "Action": "Include", "FromFile": "assets/Map_Patch_Vanilla.json",
  "When": { …, "Animal Abundance Level": "Vanilla", … } }
{ "Action": "Include", "FromFile": "assets/Map_Patch_SVE_Vanilla.json",
  "When": { …, "Animal Abundance Level": "Vanilla", "HasMod …": true } }
…
```

**这些分支是互斥的**：CP 按玩家的 `config.json` 只走其中一支。我们按**烘焙出来的默认配置**
选了 SVE + Vanilla 那一支，实际加载的正是它：

```
assets/Map_Patch_SVE_Vanilla.json   82
assets/Map_Patch_SVE_Custom.json    44
assets/Map_Patch_SVE_Extras.json    34
content.json                        31
assets/Map_Patch_Island_Vanilla.json 13
assets/Map_Patch_Island.json         6
```

其余四百多条是**没被选中的那些分支** —— CP 同样不会加载。
所以「物理数 − 加载数」这个量**根本不是缺口指标**，别再照它去挖。

## 3. 另一类落差倒是真的，而且跳得对

女巫沼泽扩展的 Include 里有一条 `"When": {"HasWalletItem |contains=MagicInk": true}` ——
**钱包物品是运行时的**，离线判不了 ⇒ 跳过。这类（运行时状态把关的 Include）确实是内容损失，
但它是**已知且正确**的拒绝，不是我们漏看。

## 4. 这一类到底丢多少（已量）

全语料 **175** 条 `Action: Include`，其中 **98** 条带 `When`。按 `When` 的键分类：

| 条件 | 条数 | 我们 |
|---|---|---|
| `HasFile:{{FromFile}}` | 22 | 烘焙 ✓ |
| `HasMod` | 21 | 烘焙 ✓ |
| 配置令牌（`Enable mod?` / `Animal Abundance Level` / `Allow Bonus Animals` / `*_Toggle`）| 45+ | 烘焙 ✓ |
| `HasWalletItem` | 4 | **判不了 ⇒ 不展开** |
| `HasSeenEvent: hostPlayer` | 4 | 同上 |
| `HasReadLetter` | 3 | 同上 |
| `Year` | 2 | 同上 |

这 **13 条** Include 若展开，会带来约 **74** 条补丁（女巫沼泽 51 / 聚宝盆制作 14 / 更逼真的对话 8 / 食谱技能 1）。

### 4.1 真能救回多少（已估）

CP 的机制是「条件成立才**加载** children」（`IncludePatch.TryApply` 只在 `IsReady` 时跑），
不是把条件并进去。但**效果**上等价于「children 各自带上那个条件」——
所以可以把 Include 内联、把它的 `When` **与子补丁自己的 `When` 相与**，
再走既有的条件翻译通道（`HasWalletItem` 就有原版 GSQ：钱包物品那个 power 的 `UnlockedCondition`）。

按「子补丁是 `EditData`/`EditImage` 且目标资产在**原版树里存在**」估：

```
能带条件落下：38 条
仍要拒（Load 创建资产 / 目标不存在）：36 条
```

（`Load` 没法带条件 —— 资产创建是「有或没有」，不是「有条件地有」。
女巫沼泽那 34 条里有 3 条 `Load` 在造 `Data/Events/{WitchSwamp,WitchHut,Blacksmith}`，
而这三个原版没有、正是靠那 3 条 Load 造出来的 ⇒ 依赖它们的 22 条 EditData 也跟着落不了。）

⇒ **这是一件有量、但需要改 Include 机制的事**（内联 + 条件相与 + 键冲突处理 + `Load` 单独拒）。
上界 38 条、约 0.55% 的补丁量。

### 4.2 做了（2026-10-09）

按上面的设计实现：判不了 ⇒ **内联**，把 Include 的 `When` 与子补丁自己的 `When` 相与；
带不动的两档（`Load`、键冲突）单独跳过并记原因。新增 `IncludeOutcome.Inlined`，
这一档**不算失败**（退出码不再因为它变 1）—— 条件能不能落地由每条子补丁自己的判定说了算。

受控对比（32 包，同参数，b21 → b23）：

| 项 | b21 | b23 |
|---|---|---|
| 补丁总数 | 6980 | **7048（+68）** |
| **已写入** | 3694 | **3717（+23）** |
| 写入失败 | 6 | 8（+2，见下）|

**+23 逐条核过是真内容**：女巫沼泽扩展 42 → 65，新建的
`Data/Events/{WitchSwamp,WitchHut,Blacksmith}` 共 12 个事件键，
**每条都带上了原生 `G PLAYER_HAS_MAIL Current HasMagicInk`** ——
内容在，条件由游戏自己按运行时判（这正是这条改动想要的效果）。

**+2 写入失败是如实拒绝，不是回归**：聚宝盆制作那两条的落点是**数组**
（`…OutputRules.<Id>.Triggers`）—— 数组本身没有 `Condition` 可写，
而新增的元素又没有 `Id`（同日按 CP 去掉了「追加时补 Id」）⇒ 无法逐条寻址。

（差 38 − 23 = 15：其中一部分是 #4.1 里说的「依赖被跳过的 `Load`」那类，
还有些落点构造不出来 —— 上界是估计，实测才是准的。）

### 4.3 顺带暴露的一个**落点模型**缺口（记为待办）

内联后聚宝盆制作多了 2 条写入失败：

```
Action=EditData  Target=Data/Machines
TargetField: ["(BC)Cornucopia_Extruder","OutputRules","Cornucopia_GlassNoodles","Triggers"]
Entries:     { "(O)Cornucopia_Cassava": { "RequiredItemId": … }, … }
```

落点路径落到 **`Triggers` 这个数组本身**，而数组没有 `Condition` 可写 ⇒ 拒。
**要落进去只能逐元素**（`MachineOutputTriggerRule` 既有 `Id` 也有 `Condition`），
但：

- 新增的元素**没有 `Id`**（同日按 CP 去掉了「追加时补 Id」）⇒ 按 `Id` 寻址不到；
- 按 **`#<下标>`** 寻址要在**合并之后**才知道下标 —— 而 `EntryPaths` 现在是在合并**前**算的。

⇒ 要修得让落点路径**在合并之后**推导（`RefusalFor` 已经先合并了，写入器那条路要先重排）。
**这是落点模型的设计改动，不是小补丁**；先记着。

#### 4.3.1 补记（2026-10-09 稍晚）：量比原先估的大，CP 语义已对着源码查清

**量**：全语料「定位不到条件落点条目」写成两种字段 —— 判定阶段 `reason` **21 条**、
写入阶段 `writeNote` **2 条**。按落点路径聚合：

```
 11  (BC)*.OutputRules.*.Triggers      ← 聚宝盆机器的输出规则数组
  4  AnimalShop.Items / AnimalShop.Owners   ← Data/Shops 的数组
  6  其余（别的 mod 的自定义资产，正确拒绝）
```

⇒ 真正属这一类的是**约 15 条**，不是 2 条。

**CP 语义（对着反编译源码核过，`/d/tmp/cp_decompile`）**：

`ContentPatcher.Framework.Patches.EditData/ListKeyValueEditor.cs`：

```csharp
public override void SetEntry(object key, object value)
{
    if (TryGetEntry(key, out TValue _, out int index)) Data[index] = (TValue)value;
    else Data.Add((TValue)value);          // ← 追加**不写 Id**
}

public override object ParseKey(string key)
{
    string[] array = key.Split('#', 2);
    if (array.Length == 2 && string.IsNullOrWhiteSpace(array[0]) && int.TryParse(array[1], out var result))
        return result;                      // ← `#<下标>` 是官方寻址写法
    return key;
}

private string? GetKey(TValue entry) => … 元素的 `Id`（属性 `Id`/`ID`，退化到字段）
```

- ⇒ `Entries` 的 key 就是元素的 **`Id`**（这条我们本来是对的）；
- ⇒ **追加的元素不填 `Id`**（佐证我们「追加时不补 Id」的决定，`SetEntry` 就是这么写的）；
- ⇒ 于是**自己追加的元素按 `Id` 定位不到**（它的 `Id` 是空），**只能按 `#<下标>`**。

**实现方案（没做，留给以后）**：

1. `EntryPaths` 的 `TargetField` 分支现在只返回**容器路径**；要改成「容器 + 每个 `Entries` key」。
   但容器是数组时，key 是元素的 `Id`，**而下标只能合并后算** —— 两处的调用点
   （`ConditionWriter.RefusalFor` 第 88 行、`PatchApplier` 第 131 行）**都已经在合并之后**，
   所以把 data 传进 `EntryPaths`（或改在 `RefusalFor`/`Apply` 里展开）是可行的；
2. 定位顺序：先按 `Id`（`FindInArray` 已支持），**再**按 `#下标` —— 下标取
   「合并后数组里那个**合并前不存在**的元素」的位置（`SetEntry` 追加在**末尾**，顺序 = `Entries` 的顺序）；
3. **判定与写入必须共用同一个展开**（本仓库的铁律），否则又会出现「判定说能写、写入说不能」。

**为什么先不做**：这是落点模型的设计改动，判定/写入两处要同步改 + 边界（多个 key、
容器是对象时行为不能变）；收益 15 条。**权衡下来不值得现在动** —— 但如果以后要动，
上面的源码行号和方案是齐的。

⚠ 别顺手「把落点改到容器上」了事：数组**没有** `Condition`，`ConditionWriter.AcceptsCondition`
会把容器判成 `不接受 Condition` —— 那只是把「定位不到」换成另一种拒绝，一条也不会多写。

## 5. 要判断「有没有漏看内容」，该量什么

不是「物理数 − 加载数」，而是**逐条 Include 的决策**：

| 决策 | 是否缺口 |
|---|---|
| 条件烘焙为真 ⇒ 展开 | 否 |
| 同一组互斥分支里选中一支 | 否（CP 也只走一支）|
| 条件烘焙为假 ⇒ 不展开 | 否 |
| 条件**判不了** ⇒ 不展开 | **是**（内容损失，已知）|

最后一类才是该盯的。它的量在 CLI 的 stderr 里（`Include 求值失败、未展开，已跳过`），
**报告里没有** —— 要盯的话得把它搬进报告，那是另一件事。
