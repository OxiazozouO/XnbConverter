# `{{Query:…}}` 只代了内层 ⇒ 残留写进产物 ⇒ 整串传送被游戏丢弃

日期：2026-10-13
怎么发现的：扫整份实机 SMAPI 日志（自检 mod 只覆盖它自己检查的那几项），
`Failed parsing warp '26 35 Farm {{Query:58}} {{Query:15 + 2}} 27 35 …' for location
'Custom_ConvenientRoad'. Warps must have five fields … but got a non-numeric value` **10 行**。

## 0. 那串花括号怎么来的

`Lnh's Farms Convenient Road` 的 `content.json`：**ConfigSchema + DynamicTokens** 拼出来的 ——

```json
"ConfigSchema": { "Map_Coordinates_X": { "Default": 58 }, "Map_Coordinates_Y": { "Default": 15 } },
"DynamicTokens": [ { "Name": "Coordinates_Y", "Value": "{{Query:{{Map_Coordinates_Y}} + 2}}" } ],
… "AddWarps": "26 35 Farm {{Coordinates_X}} {{Coordinates_Y}} …"
```

CP 会把 `{{Query:15 + 2}}` 算成 `17`。我们**把内层代了、外层没算**：
`DynamicTokenResolver.TryResolveBuiltin` 里只认 `Random` / `Range`，`Query` 直接放行 ⇒
文本留下来的是 `{{Query:58}}` / `{{Query:15 + 2}}` ⇒ 整条 `Warp` 里出现非数字
⇒ 游戏**丢弃那一整串传送**（不是只丢一条）。

## 1. 修法

新 `LiteralQueryEvaluator`（`Cp/`）：**只认纯字面量算式** —— 整数、`+ - * / %`、括号、一元负号。
`DynamicTokenResolver` 里加一条：**先把输入里的内层令牌代掉，再对结果求值**
（与 `Random` 那条路同一做法；内层代换**不单独记替换** —— 外层代成功时整段会被一个数字换掉，
内层偏移就失效了）。

⚠ **代完仍带令牌/字母就绝不求值**（`{{Query:{{Day}} + 1}}`、`{{Query:abc}}`、`{{Query:5 > 3}}`、
除零…）—— 那是运行时语义，猜不得；原样留着并由 `UnexpandedTokenAudit` 报出来。
这条纪律与 `ValueTokenSubstitutor` 的"代不掉就留着"一致。

## 2. 验证

测试 +3（`DynamicTokenResolverTests`）：字面量求值（`58` / `15 + 2` / 整数除法 / 负号）、
**先代内层再算**（`{{Query:{{Map_Coordinates_Y}} + 2}}` ⇒ `17`）、不认的就别代。**1431 条全绿**。

受控对比（同参数 232 包，prof95 → prof96）：

| 侧 | 结果 |
|---|---|
| 判定 | 4 包有差异，**变好 1 / 变坏 0** —— SVE 一条 `EditData data/events/Farmhouse` 从 `NeedsReview`（"值里含无法静态解析的令牌 `{{Query}}`"⇒不写）变成 `Converted` ⇒ **那条事件现在落地了** |
| 产出树 | 31593 个文件里 **9 个不同**：`Maps/Custom_ConvenientRoad.tbin`（warp 变成数字）+ `Data/Events/FarmHouse*.json`（7 个语言变体）；无新增/无消失 |
| 残留令牌 | 新 warp 已是 `26 35 Farm 58 17 27 35 Farm 58 17 …`；全树残留只剩框架令牌（`{{DaoFestivalPositionsToken}}` 等）与**取值依赖运行时**的包内令牌（`{{FestivalYear}}`，本就该不猜）|

实机（把改动的 13 个 `.xnb` 单文件打包后装机 —— 单文件与整树打包字节等价已证过）：

| | 改前 | 改后 |
|---|---|---|
| `Failed parsing warp` | **10** 行 | **0** 行 |
| 全量探针 | PASS 1245 / 贴图跳过 1249 / FAIL 1 | 同左（唯一失败仍是上游缺 `spring_silo`）|

⚠ 同一轮日志里出现 1 行 `Failed parsing condition 'CAULDRON_CONFIG_VALUE FaerieGarden True':
'CAULDRON_CONFIG_VALUE' isn't a known query or alias.` —— **不是这次改动引起的**：
那是包**自带的自定义 GSQ**（原样写在 `[CP] Lumisteria Mt Vapius\assets\items\FruitTrees.json` 里，
被拷进 `Data/FruitTrees.json`），而 `Data/FruitTrees.json` 在这三轮里**逐字节没变**；
它出现与否只取决于这一轮游戏**有没有加载那份资产**（加载时机差异）。
