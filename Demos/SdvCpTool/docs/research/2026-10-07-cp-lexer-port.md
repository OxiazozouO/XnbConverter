# 把 `{{…}}` 的结构判定交给 CP 自己的词法器（不再手写字符串匹配）

日期：2026-10-07　　代码：`Task 52`

## 1. 起因：同一件事写了四份，三份是错的

本仓库此前对「`{{…}}` 的结构」有**四份各自的手写扫描**：

| 位置 | 扫描方式 | 对错 |
|---|---|---|
| `Merging/TranslationApplier`（i18n 的键） | 配对（带深度计数） | ✅ |
| `Cp/ValueTokenSubstitutor.HasTokenText`（判定侧） | `IndexOf("}}")` 找第一个 | ❌ |
| `Cp/DynamicTokenResolver.Expand` | `IndexOf("}}")` 找第一个 | ❌ |
| `Cp/ContentPack.ResolveFromFileToken`（Include 的 When） | 精确串替换 | ✅（但只认 `{{FromFile}}`） |

错的那两份在**嵌套**上翻车：`{{i18n:KEY |PlayerName={{PlayerName}}}}` 的结尾落在**内层**令牌的
`}}` 上，于是整段被当成长度只到内层为止的东西。实测代价（都是**静默**的）：

1. 判定侧把它当「纯 i18n 令牌」整个放行 ⇒ 代不掉的 `{{PlayerName}}` 随译文写进产出（13 处）；
2. 动态令牌侧不递归进令牌的**输入参数** ⇒ `{{i18n:K |Season={{Season}}}}` 的实参永远代不掉。

## 2. 为什么该照搬 CP 而不是继续缝

结构只有一处权威定义：**CP 自己的词法器**
（`ContentPatcher.Framework.Lexing/Lexer.cs`，350 行；加 `LexTokens` 五个小类共约 500 行，自包含）。
它先把文本切成比特（`{{` / `}}` / `|` / `:` / 字面量），再递归解析成
`LexTokenLiteral` / `LexTokenToken{Name, InputArgs}` 的**树** ——
**嵌套、位置参数、具名参数都是它自带的能力**，不需要我们猜。

## 3. 移植了什么

`src/SdvCpTool.Core/Cp/Lexing/`：

- `LexTokens.cs` —— `LexBitType` / `LexTokenType` / `LexBit` / `ILexToken` /
  `LexTokenLiteral` / `LexTokenInput` / `LexTokenToken` / `LexFormatException`（同名同序）；
- `CpLexer.cs` —— `TokenizeString` / `ParseBits` / `ParseBitQueue` / `ExtractToken` /
  `ExtractInputArguments`，含 `ParseBitQueue` 那段「合并相邻字面量、在令牌输入边界修剪空白」的后处理
  （少了它，参数解析的边界就与 CP 不一致）。

**相对 CP 只加了三处，都写在代码注释里**：

1. `LexBit.Start` —— 原文偏移。CP 只需要树；本仓库有一处必须**按原文位置**替换
   （`TranslationApplier` 只换 `{{i18n:…}}` 那一段，其余字节原样保留）；
2. `LexTokenToken.Start/End` —— 同上，给「按位置替换」用；
3. `CpLexer.TryParse` —— 宽松入口。CP 遇到畸形直接抛；本仓库要拿词法器去**裁决**第三方包的数据，
   需要的是一个布尔，不是一个异常。

`SplitLexically` **未移植**（本仓库没有对应调用点）。

## 4. 换掉了什么、解开了什么

- `Cp/TokenScan` 的括号配对 → 交给词法器（`HasUnresolved` 只做**语义裁决**：
  谁算残渣、谁放行；`FindTokenEnd` 走同一份比特流）。**旧的手写配对实现删掉**。
- `Cp/DynamicTokenResolver.Expand` → 词法器 + **按原文位置替换**
  （不重建整串：重建会把 `{{i18n:K }}` 规范化成 `{{i18n:K}}`，而代换结果要写进产物）。
  于是**令牌输入参数里的内嵌令牌也会被代**，代不掉的则**不打散外层**
  （`{{i18n:K |PlayerName={{PlayerName}}}}` 原样保留，由自检报出来）。

## 5. 受控对比（32 包，同参数）

| 阶段 | 对比 | 结果 |
|---|---|---|
| 移植 + 换掉判定侧的扫描 | c14 → c15 | **逐条补丁无任何变化**（口径完全一致） |
| 动态令牌改走词法器（含输入参数递归） | c15 → c16 | **已写入 3536 → 3542（+6）**；`NeedsReview` 1140 → 1134（−6）；其余判定逐项相同 |

那 6 条都在 `人物对话---身临其境 谢恩`（`Default Marriage Dialogue`、`Engagement Dialogue`、
`Marriage Extra Dialogue` 等）：原先卡在「值里含无法静态解析的 CP 令牌」，现在实参里的
包内令牌代掉了，5 条直接合并、1 条走 `$query` 内联 GSQ。

测试 **1115 通过**（新增 `CpLexerTests` 11 条，含**往返一致**与**已知偏离**两组：
花括号内相邻的空白会被规范化，所以写入侧不能靠「重建整串」来替换 —— 这条用例把偏离钉住）。

## 6. 收尾：参数切分也交给词法器，字段令牌展开并成一份

**（a）补上 `SplitLexically` 的移植**，用它替掉 `TranslationApplier` 里最后一段手写令牌处理
（`TopLevelPipe` + `TryReadParams` 里的 `Split('|')`）。
那个 `Split('|')` 是**真的错**：令牌输入里也有 `|`，裸切会切出一个**假参数**
（实测 SVE 的 Claire 婚姻对话切出过一个叫 `contains` 的假占位符，并据此把整条补丁判失败）。
`SplitLexically` 是 CP 里专为此写的：**切不动令牌内部**。

> **受控对比 c16 → c17：全部计数 +0，只有 1 条补丁的失败理由变准**
> —— 从「具名参数的值里含无法静态解析的令牌：`contains`（×2）」变成真正的拦路虎
> 「键含未支持的内嵌令牌：`Claire.funLeave.{{Season}}_{{Day}}`」（那是**随存档日期变**的键，
> 确实表达不了）。**结论没变，理由从假的变成真的。**

**（b）字段引用令牌的展开也并成一份**：原先 `LoadApplier.ExpandFieldTokens` 与
`TranslationApplier.ExpandFieldTokens` 各写了一遍同样的三条 `Replace`，现在都转发到
`Conditions.FieldReferenceTokens`（`LoadApplier` 保留同签名转发，因为已有 8 个调用点；
逐个改是纯 churn，重复的三条 `Replace` 才是真问题）。
**受控对比 c17 → c18：逐条补丁无任何变化。**

**剩下唯一没动的**：`Cp/ContentPack.ResolveFromFileToken` —— 对**原始 JSON**走键与值替换
`{{FromFile}}`（发生在「解析成树之前」，形状与词法器不同，且只处理 `{{FromFile}}`）。
它是对的，改它要另做受控对比。

## 7. 未确证

- 词法器的边界条件是用 CP **反编译**出来的源码移植的；`ParseBitQueue` 的后处理是按语义复刻的，
  **没有**拿 CP 本体做逐串对拍。现有证据是：`TokenScanTests` 的 15 条行为用例 + 语料 c14→c15 零变化。

## 8. 顺着往下清：还有哪些「土著实现」换掉了、哪些换不掉

按「在 CP/游戏里有没有**正规实现**可用」逐个过了一遍：

| 位置 | 正规实现 | 处置 |
|---|---|---|
| `Conditions/WhenParser.SplitKey`（手写「找第一个 `:` 或 `|`」） | `CpLexer.ParseBits(impliedBraces: true)` —— **正是 CP 解析条件键走的那条路** | **换掉**。c19 → c20 逐条无变化 |
| `Cp/ConfigTokenSubstitutor` 的正则 `\{\{\s*([^{}:|]+?)\s*\}\}` | 同上（词法器） | **换掉**。这是 `{{…}}` 的**第四份**手写匹配；c19 → c20 逐条无变化 |
| `Judging/PatchJudge` 的多目标规则（「一个缺就整条判死」） | CP 的 `PatchLoader.SplitPatches`（Target × FromFile **拆成互相独立的补丁**） | **按正规语义改**。c18 → c19：**已写入 +3**、`CreatesNewAsset` +3、`TargetMissing` −3 |
| `Merging/EditDataMerger` 的路径/编辑器 | CP 的 `KeyValueEditorFactory` + `BaseDataEditor` 一族 | **换不掉**：CP 那套是**按运行时类型反射**（`ModelKeyValueEditor` 用 `MemberInfo`）走的，我们只拿得到 JSON 文本，没有那些类型。语义已按 `EditDataPatch.cs` 逐条对齐（含「字符串条目按分隔符下标改」——那是先前修过的破坏性 bug） |
| `Merging/TmxToTbin` | xTile 的管线 | **换不掉**：xTile 不在依赖里 |
| `Cp/JsoncReader` | （无） | **保留**：Newtonsoft 不认尾逗号，CP 的 content.json 大量使用；它是逐字符扫描（不是正则），串内 `//` 处理正确 |
| `UnexpandedTokenAudit` 的 `{{…}}` 正则 | 词法器 | **保留**：它是**自检**，要的正是「连词法都不合法的花括号也要报出来」，词法器对畸形输入只会说不合法、点不出位置 |
| `Merging/DialogueQueryWriter` 的 `$query` 拼装 | 游戏的 `Dialogue` 状态机 | **已按状态机语义校验**：`#`/`|` 会撑破 `Split('#',2)` / `Split('|')`，拼装前就拒绝（`DialogueQueryWriter.cs:249-256`） |

**这条线的方法论**（值得记）：先问「上游有没有正规实现」，有就**照搬**（连名字一起搬，便于对照）；
没有就先问「为什么没有」（要运行时类型？要不可用的依赖？），把理由写在代码旁边 ——
不要因为「看起来简单」就手写，同一件事写四份、三份是错的，代价在这一轮已经付过了。

## 9. 端到端核对：产出树整树翻一遍（不是只看自检）

报告里的 `unexpandedTokens` 自检是**按资产类型**走的（只认 `.json`）；为防「有内容绕过自检」，
新增 `tools/residual-token-scan.py`：**把产出树整个翻一遍**，数 `{{…}}`（排除 `{{i18n:…}}`）。

**实测（最小集 9 包，含 SVE，各约 2268~2540 个 json）**：每棵树残留**恰好 2 处**，
且**都在 `Strings/StringsFromCSFiles.ko-KR.json`** —— 那是**原版**韩文里的 `${{0}…`
（`$` 性别标记 + `{0}` 格式化占位符），输入树里本来就有。**产物里没有一处 CP 令牌。**

另外抽查了 `{{Random}}` 确定化的**落盘结果**：野生动物包 `Characters/DustSpirit_A_WA.png`
与源 `assets/special/DustSpirit_Normal.png` **逐字节相同**（= 第一个备选），`.config` 也一并生成 ✓。

## 10. 端到端核对：把季节令牌那条改动放进**链式**里跑

Task 66（`FromFile` 里的 `{{Season}}` 展开成四季）在**逐包单独转**的语料里只值 1 个目标，
因为季节变体要挂到 NPC/地图上时，那些资产多半是**别的包产出的**。所以在链式里复核：

```
SVE（--tree D:\sdv_tree）→ 大型扩展---SVE 季节性服装（--tree = SVE 的产出树）
```

**结果**（`appearanceEmission`）：
```
groups 10 / emitted 7 / rejected 3 / patchesCovered 28
已归一到原版 Appearance[]（4 个外观变体 → 4 份独立资产 + 1 处 Data/Characters 条目）
```
⇒ 那 12 条 `Characters/Alesia|Isaac|MarlonFay|Camilla_{{Season}}.png` **真的落成了**：
7 组 × (4 份外观资产 + 1 处 `Data/Characters` 条目) ✓，覆盖 28 条补丁。
（单独转时它们全是 `TargetMissing` —— 那些 NPC 由 SVE 那个包产出。）

**3 条被拒的理由逐条核过，都是真的**：
`KrobusRaven` / `Magnus` 「不在 `Data/Characters` 里」。**`Magnus` 看着可疑，实地查了**：
SVE 并没有建 `Magnus` 这个 NPC —— 它只是把**原版 Wizard 的 `DisplayName` 改成 "Magnus"**
（`code/NPCs/Magnus.json` 的 `EditData Data/Characters` → `Entries.DisplayName`），
NPC 的键仍是 `Wizard`。所以 `Characters/Magnus` 上**没有** `Data/Characters` 条目可挂 ✓
**拒得对**。（Appearance[] 是 **`Data/Characters` 那条目的字段**，没有条目就无处可写。）

**结论**：这条改动在**链式**用法下有效；`tools/chain-packs.py` 已是现成入口。
