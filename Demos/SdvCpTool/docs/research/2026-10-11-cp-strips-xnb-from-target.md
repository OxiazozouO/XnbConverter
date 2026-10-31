# CP 会把目标名结尾的 `.xnb` 剥掉 —— 我们原来没剥（已修，8 条）

日期：2026-10-11（晚）
起因：复核"报告里的判定能不能对得上"时，报告里出现了一条**看着就不对**的目标名：
`新建资产 `Data/Festivals/spring13.xnb` 带条件，但该资产类型的条目无法承载条件` ——
资源树里根本没有这个资产，因为**它不该带扩展名**。

---

## 判据（对 CP 源码核过）

`ContentPatcher.Framework/PatchLoader.cs:151-158`：

```csharp
private IAssetName ParseAssetName(string rawName)
{
    if (rawName.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
        rawName = rawName.Substring(0, rawName.Length - 4);   // ← 就这一步
    return ParseAssetNameImpl(rawName);
}
```

- 只对 **`Target`**（资产名）做；`FromFile` 是**真文件路径**，不剥；
- 逗号分隔多值是**先 `SplitPatches`、再逐条 `ParseAssetName`** ⇒ 要**逐段**剥；
- `ParseAssetNameImpl` 是注入的 SMAPI `ParseAssetName`（还会切语言后缀之类的归一）。

## 不剥的代价（实测）

全语料只有 **8 条**：包 01「佳能友好对话扩展」按年份写的节日对话，
`Data/Festivals/Y3.json` 里写成 `"Target":"Data/Festivals/spring13.xnb"`
（`LogName` 也写成 `Year 3 Festival Egg Festival - spring13.xnb`，是作者的笔误风格）。

原来的判定：既然树里查不到 `Data/Festivals/spring13.xnb` ⇒ 当**新建资产**走 `JudgeConditionalNewAsset`
⇒ 报告写「新建资产 `…spring13.xnb` 带条件，但该资产类型的条目无法承载条件」——
**对一个并不存在的资产谈落点**，而且真相是"改既有资产、条件是 `Year`"。

## 改法

`ContentPack.CollectUnits` 里对 `Target` 加一道 `StripXnbSuffix`（`ExpandModId` 之后）：

```csharp
Target = StripXnbSuffix(ExpandModId(raw["Target"]?.Value<string>())),
```

只在这一处（CP 也是加载期一次），逐段剥、只剥 `.xnb`、只在结尾、大小写不敏感；
不含 `.xnb` 的目标**原样返回**（连逗号重排都不做）。

## 同参数受控对比（改前 / 改后）

```
A = 改前（prof32，commit aca9365 的二进制）→ python tools/convert-mods.py D:\tmp\prof32
B = 改后（剥 .xnb）                     → python tools/convert-mods.py D:\tmp\prof33

报告：36 包里 **35 个逐项完全相同**；只有包 01 的 **8 条**变了 ——
      目标名 `Data/Festivals/<id>.xnb` → `Data/Festivals/<id>`，
      判定仍为 `NeedsReview`（都没写盘），但理由从
        「新建资产 `…<id>.xnb` 带条件，但该资产类型的条目无法承载条件」
      变成
        「资产 `Data/Festivals/<id>` 的条目键名编码了条件维度，但补丁条件不被键名完全蕴含，需人工确认落点」
产出树：13562 文件 **逐文件相同**（0 只在 A / 0 只在 B / 0 内容不同）
单测：**1342 通过 / 0 失败**（+1 条：`目标名结尾的xnb要剥掉`，含多值逐段剥与
      `FromFile` 不剥两条反例）
```

⇒ 这一改**没有多写一个字**，改的是"报告说的是不是真的"。判定不变是因为这类
（`Data/Festivals` + 只有 `Year`）本来就落在"容量之内但键名不被蕴含"那档 ——
与 `2026-10-11-remaining-buckets-…` §3 里那 5 条不带 `.xnb` 的同类补丁完全一致。

## 顺带记两条**没做**的（都量过，语料 0 条）

| CP 行为 | 我们 | 语料影响 |
|---|---|---|
| `FromFile` 没写扩展名、磁盘上却是 `<name>.xnb` 时**自动补 `.xnb`**（`Patch.cs:285-293` `NormalizeLocalAssetPath`）| 不补 ⇒ 判 `TargetMissing` | **0 条**（扫过全语料：没有"字面文件不存在、`.xnb` 存在"的 FromFile）|
| SMAPI `PathUtilities.NormalizeAssetName` 的完整归一（如 `Content/` 前缀、语言后缀）| 只做 `\`→`/` 与大小写不敏感（Windows 文件系统吸收）| 目标以 `Content/` 开头的 **0 条** ⚠ 该函数**实现体没在反编译产物里**（`StardewModdingAPI.Utilities.PathUtilities` 只是个转发壳）⇒ **未确证**它到底还做哪些归一 |
