# 修一个真 bug：`Target`/`FromFile` 里**代换之后才浮出来**的内建 `{{Season}}` 没人管

日期：2026-10-12
类别：**行为修正（高影响）** —— 包内图集进不了产出树 ⇒ **引用它的地图建不出来**

## 一条走过弯路的路（记在这里免得再走）

第一版想接进**已有的四季图集机制**（`SeasonalTilesheetPlanner` + `SeasonalTilesheetEmitter`：
写 `spring_X/…` 四份、把引用地图的 `ImageSource` 指到 `spring_X`）。全量实测**净负**：

- 那条机制要求「**发射时**引用它的地图已经在树里」——引用方是**别的包**的、跑在本包之后
  ⇒ 永远凑不齐 ⇒ 组被拒 ⇒ **缺失图集一种都没少**（14 → 14）；
- 而我把 `Load` 也塞进季节展开（`SeasonFromFileExpansion`），把 Crystal Island 那两条
  **本来代得掉** 的 `Load` 拆成了四季条件 `Load` ⇒ `written` **−2**。

⇒ **回退**（`git checkout`），改走下面这条更小、也不猜的路。

## 真因

`[CP] HxW Tilesheets` 的 FromFile 是：

```
"FromFile": "Assets/{{Coloring}}/{{SeasonWithDefault}}/hime_outdoorplants.png"
```

`SeasonWithDefault` 是**包内 DynamicToken**，它的值里带 **CP 内建的 `{{Season}}`**。
于是链路是：

1. `SeasonFromFileExpansion`（判定之前）看到的是 `{{SeasonWithDefault}}` —— **不是**它认的
   `{{Season}}` ⇒ 不展开（这一步没做错，它只该认四季那个令牌）；
2. `ConfigTokenSubstitutor`（本轮新增的 DynamicTokens 代换）把包内令牌代掉，**浮出**
   `Assets/Vanilla/{{Season}}/hime_outdoorplants.png`；
3. 没有第三步 —— 内建 `{{Season}}`（= 当前季节、单值）没人管 ⇒
   下游「`FromFile` 含无法静态解析的令牌」⇒ **整条不写** ⇒ `Maps/hime_outdoorplants`
   不存在 ⇒ **引用它的 7 张地图建不出来**。

## 修法：**确定化到春天**，并如实报出来

`ConfigTokenSubstitutor.Substitute` 最后加一步 `DeterminizeSeason`：

- 只认**不带输入参数**的 `{{Season}}`（走词法器，不用正则碰花括号）；
- 只在本包**没有自己的** `Season` 令牌时做（那是包内令牌，轮不到我们猜）；
- 只动 `Target`/`FromFile` —— 值里的 `{{Season}}` 继续由 `UnexpandedTokenAudit` 报出来；
- 写进那条补丁的 `writeNote`：**「`{{Season}}` 确定化：一律取春天那份」**。

为什么是「冻结」而不是「四季各一份」：纯 XNB 里**一个资产键只有一份内容**，而引用方在
**别的包里**（跑在我们之后），我们既改不了它们的 `ImageSource`、也不能把它们引用的
裸名换掉 ⇒ 四季那套只能用在「引用方已经在树里」的场合（四季机制本身照旧为
`EditImage` 服务）。冻结一季是**可见的近似**，比"地图直接建不出来"严格更好。

## 验证（prof66 → prof68，同参数全量 232 包）

- 定点（`[CP] HxW Tilesheets` 单包）：`written` 17 → **24**，
  `Maps/hime_outdoorplants.png`（110 KB）与 `hime_outdoorfurniture_Front.png` 落地，
  两处 `writeNote` 写明确定化。
- 全量（`prof66` → `prof68`，232 包同参数）：`total` 不变（32771 —— 没有把补丁拆多）、
  `written` **15144 → 15180（+36）**、`writeFailed` **208 → 201（−7）**；
  逐条核过：**变好 36 条**（`TargetMissing → Converted` 29、`ConvertedNoCondition → Converted` 7）、
  **变坏 0 条**；另有 21 条「两轮都没写、判定更精确」（目标资产现在存在了 ⇒ 从 `TargetMissing`
  变成「条件换图需要机制」）。
- 图集盘点：找不到图片的图集名 **14 → 12 种**（少的是 `hime_outdoorplants`、
  `hime_outdoorfurniture_front` 这一档）；「落点被地图占着」0 处。
- ⚠ 中途走过一轮弯路（接四季图集机制）已回退，`prof67` 是那轮的产物、**不要拿它当基准**；
  基准是 `prof66`（回退后、本修复前）。
