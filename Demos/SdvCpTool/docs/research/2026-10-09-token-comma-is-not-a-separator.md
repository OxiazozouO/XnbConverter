# 令牌内部的逗号**不是**分隔符

日期：2026-10-09

**触发**：语料里「佳能友好对话扩展」（mod id 2544）的 `content.json` 第 191 行

```json
{ "Action": "Include",
  "FromFile": "Data/Festivals/Y{{Random:null, 3}}.json",
  "When": { "HasFile:{{FromFile}}": true, "Year |contains= 1, 2, 3, 4": false } }
```

跑出来的 stderr 是**两条**警告：

```
Include 目标不存在：Data/Festivals/Y{{Random:null
Include 目标不存在：3}}.json
```

一条 `FromFile` 被切成了两条路径 —— 两条都不存在。包目录里只有 `Data/Festivals/Y3.json`。

## 根因

CP 是**先分词、再取值**（`CpLexer`）：`{{Random:null, 3}}` 是**一个**令牌，
它的实参里本来就带逗号。而我们对 `Target` / `FromFile` 的「逗号分隔多值」
用的是朴素 `Split(',')` —— 在**分词之前**就切了，把一个令牌拦腰切断。

## 修法

新增 `src/SdvCpTool.Core/Cp/AssetNameList.cs`：按 `{{` / `}}` 的**配对深度**切，
只有深度为 0 的逗号才是分隔符。不配对时（`{{` 多于 `}}`）退化为「进入令牌后剩下的全算一段」，
不抛异常。

改动的拆分点一共 **5 处**（原先漏了一处，见下）：

| 文件 | 位置 | 切的是什么 |
|---|---|---|
| `Cp/ContentPack.cs` | Include 的 `FromFile` | 包内 JSON 文件列表 |
| `Judging/PatchJudge.cs` | `EvaluateLeafOverTargets` | 每个目标各求值一次 |
| `Cli/Program.cs` | `TargetNames` | 资产名归一路径 |
| `Cli/Program.cs` | `BakeOverTargets` | 条件按目标 OR |
| `Merging/EditImageApplier.cs` | `SplitList`（`Target`/`FromFile` 笛卡尔积） | **原先漏掉的一处** |

★ 第 5 处是回扫 `grep -rn "Split(',')"` 才发现的：`EditImageApplier.ExpandJobs` 里
`patch.Target` / `patch.FromFile` 就是资产名列表，同样是资产名的拆分点。
**先改的 4 处是照着记忆改的，漏了它** —— 教训是「有几处」这个数必须 grep 出来，不能凭印象。

## 影响面：全语料**只有 1 处**

`tools/token-comma-scan.py`（新增）扫遍 32 个包的**所有** `*.json`
（含被 Include 的、字段名大小写不敏感、`Target` 支持字符串与字符串数组两种写法），
统计「逗号落在 `{{…}}` 内」的 `Target`/`FromFile`：

```
合计 1 处（分布在 1 个文件里）
人物对话---佳能友好对话扩展---2544   content.json  FromFile = Data/Festivals/Y{{Random:null, 3}}.json
```

⚠ **订正**：我先前记的是「17 处」。那是**全文任意位置**含 `{{…,…}}` 的字符串数
（多数在 `Update`/`Entries` 的值里）；限定到 `Target`/`FromFile` 后只有 **1 处**。

## 受控对比：**逐条补丁无任何变化**

同参数（同一份 `--tree`/`--mods`/`--content-hashes`，只有 exe 不同）跑两轮全量 32 包：

- 基线 exe 从 `git worktree add /d/wt-base HEAD --detach` 单独编译；
- 新版 exe 是工作区（含 5 处改动）。

`python tools/corpus-diff.py D:\tmp\corpus_anl_base D:\tmp\corpus_anl_new`：

```
total 7048→7048   convertible 5672→5672   blocked 1151→1151
written 3717→3717   writeFailed 8→8   attemptedWrite 3725→3725
判定 11 类全部 +0
逐条补丁无任何变化。
```

2544 包单跑的产出树也**逐字节**一致：7595 个文件，0 个只在一边、0 个内容不同。

**为什么测不出变化**：这条 Include 后面还挡着 `{{Random:…}}` —— 那是**运行时随机**，
离线求不了值，路径终究展开不出来（工具的 `Include 令牌展开 0 个`）。所以这条 Include
在修前修后都贡献 0 条补丁。修复消除的是**错误的拆分**，不是产出差异。

## 直接验证（语料测不出来，就单看 stderr）

```
基线：Include 目标不存在：Data/Festivals/Y{{Random:null
      Include 目标不存在：3}}.json
新版：Include 目标不存在：Data/Festivals/Y{{Random:null, 3}}.json
```

两条变一条，且路径完整 —— 拆分不再发生。

## 测试

- `tests/.../AssetNameListTests.cs`（新增，8 条）：普通逗号、令牌内逗号、
  令牌内逗号与真分隔符并存、多层嵌套、空段丢弃、空/空白输入、
  不配对退化不抛异常、单括号不是边界。
- `tests/.../EditImageApplierTests.令牌内的逗号不是分隔符`：第 5 处拆分点的回归。

## 已知未改（不影响产出）

`Normalizing/VariantNormalizer.AssetTypeOf`：

```csharp
string t = (target ?? "").Split(',')[0].Trim().Replace('\\', '/');
```

这里取 `[0]` 只是为了判断**资产类型前缀**（`Data/Festivals` 这类），
前缀永远在第一个逗号之前，所以 token 内逗号**不影响**它的结论。
它只进统计归类，不进产出 —— 按「外科手术式改动」原则**不动**。
