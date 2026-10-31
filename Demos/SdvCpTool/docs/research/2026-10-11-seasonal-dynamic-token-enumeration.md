# 按季节分块的动态令牌：**枚举展开**（一条补丁 → 各季一条）

日期：2026-10-11

## 问题长什么样

SVE 季节性服装（`Poltergeister.SeasonalCuteSpritesSVE`，语料第 27 个包）每个 NPC 一条：

```jsonc
// content.json 的 DynamicTokens
{ "Name": "Aerobics", "Value": "Spring" },
{ "Name": "Aerobics", "Value": "Summer",        "When": { "Season": "Summer" } },
{ "Name": "Aerobics", "Value": "Fall",          "When": { "Season": "Fall" } },
{ "Name": "Aerobics", "Value": "Winter_Indoor", "When": { "Season": "Winter" } },
```

```jsonc
// assets/Code/Andy.json
{ "Action": "EditImage", "Target": "Characters/Andy",
  "FromFile": "assets/{{Target}}/{{TargetWithoutPath}}_{{Aerobics}}.png" }
```

`{{Target}}` / `{{TargetWithoutPath}}` 是 CP 内置的字段引用令牌（我们本来就能展开），
**唯独 `{{Aerobics}}` 是"最后一个条件匹配的块胜出"的动态令牌** —— 运行时才知道是哪一季 ⇒ 整条补丁被拒。
实测全语料**只有 9 条**这样的补丁，但它们盖着 **9 个 NPC + 一条 14 个 NPC 的多目标补丁**，
而且这个包**别的 NPC 的季节服装是已经落地的**（走四季/立绘发射器）⇒ 这 9 条是"同一件事漏了一角"。

## 做法：枚举展开（`Cp/TokenEnumerationExpander.cs`）

把这条补丁拆成 **N 条带互斥 `Season` 条件**的补丁：

```
Characters/Andy, When: {Season: Summer},        FromFile: …_Summer.png
Characters/Andy, When: {Season: Fall},          FromFile: …_Fall.png
Characters/Andy, When: {Season: Winter},        FromFile: …_Winter_Indoor.png
Characters/Andy, When: {Season: Spring},        FromFile: …_Spring.png      ← 默认块 = **补集**
```

拆完就走**现成的**四季/立绘发射器 —— 与同包里其它 NPC 完全同一条路（那条路已验证过）。

### ★ 五条前提（**任何一条不成立就整条不拆**，保持原判）

1. 条件块**只有一个键**且键名是 `Season`、值是四季之一 —— 这是**互斥**的唯一依据；
2. 各条件块的季节**两两不同**；
3. 无条件块**至多一块、且必须在所有条件块之前** —— CP 是「最后一个匹配块胜出」，
   无条件块排在后面就永远赢，拆开等于把语义**反过来**（有专门的测试钉这一条）；
4. 块的值**展得干净**（无未解析 / 无有损 / 无确定化 —— `{{Random}}`、`{{Spouse}}` 这种都不要）；
5. 一条补丁里**只有一个**这种令牌（两个说明按季节分的不止一个维度，不猜）。

另外：补丁自带的 `When` 与块的条件**同名同值** ⇒ 合并（仍是一条）；**同名不同值** ⇒ 那几条无解，
丢掉（例：补丁本身只在夏季生效时，只有"夏季那一块"活着）—— 这两条也各有测试。

## 受控对比（同参数，全语料 36 包）

| 项 | 改前 | 改后 |
|---|---|---|
| 判定总数 | 7259 | **7286**（+27 = 9 条拆成 36 条）|
| `TargetMissing` | 80 | **71**（−9：那 9 条不再被拒）|
| `Emitted`（集合发射覆盖的补丁）| 300 | **328**（+28）|
| `NoLandingPoint` | 323 | 331（+8：拆开后确实没落点的那几条，现在是**看得见**的）|
| `written` / `writeFailed` | 4059 / 9 | 4059 / 9（不动）|
| 包 27 的 `appearanceEmission` | 10 组 / 发射 7 / 覆盖 **28** | 19 组 / 发射 **14** / 覆盖 **56** |
| 产出文件 | 13506 | **13562**（+56）|

**产出树逐字节对比**（`prof19` vs `prof20`）：

```
只在改前：0
只在改后：56 = 7 个 NPC × 四季 × {.png, .config}
   Characters/{Andy,Claire,MorrisTod,Olivia,Sophia,Susan,Victor}_Appearance0..3.{png,config}
内容不同：2 = ContentHashes.json（我们的账本）+ Data/Characters.json（新写的外观条目）
```

⇒ **只多了该多的东西，别的一处没动**。自检十行与改前一致（七行 0 + tmx 4 / Message 2 /
`LocalizedText` 1 / 贴图引用 1）。单测 **1302 通过 / 0 失败**（新增 14 条）。

### 精确说：加进去的是**低优先级的兜底外观条目**（不是"把季节服装抢过来"）

写完之后核了游戏怎么在一堆命中的 `Appearance` 里挑一个（`NPC.ChooseAppearance`，
`NPC.cs:957-1010`）：**`Precedence` 小的赢**（扫到更小的就把候选表清空重来；并列才按 `Weight` 掷骰）。
我们发射的条目是 `Precedence` **1..4**，而这个包自己写的 `Poltergeister.*` 是 **−120 / −10000**
⇒ **包自己的那套永远优先**，我们那 4 条只在"包自己的条件不命中"时才兜底 ✓
（CP 侧这时候显示的是**基图** `Characters/<NPC>`，而包对基图的补丁恰好就是这条 `{{Aerobics}}` 的
季节图 ⇒ **我们兜的正是 CP 那个基图的位置**，语义对得上）。

⚠ 顺带确证一件事：**两套并存不是这次引入的** —— 改前就有 **9 个 NPC** 同时有我们的 `*_Appearance*`
与包自己的 `Poltergeister.*`（`MarlonFay / Morgan / Lance / Scarlett / Martin / Isaac / Alesia /
Jolyne / Jadu`），这次只是把同样的处理**扩到另外 7 个**（合计 16 个）。

**新增的 28 个资产单独打了包**（那 56 个文件 = 28 个资产 × `.png` + `.config`）：

```
成功: 28  失败: 0        ← 与其它立绘/外观资产同一种形状，打包器照吃
```

⚠ 整树回包那次（成功 6488 / 失败 0）是在**改动前**那棵树上跑的；这 28 个新资产是**定点**验的。
要一份含它们的完整回包数字，得再跑一次整树（约 48 分钟）。

## 剩下那 5 组为什么不发射（都记在报告的 `appearanceEmission` 里）

```
KrobusRaven / Magnus        不在 `Data/Characters` 里 —— Appearance[] 是该资产的字段，无处可写
Characters/Martin_Appearance0 / Scarlett_Appearance0   **产物名已被占用**（包里本来就有这套外观资产）
                            ⇒ 发射器不覆盖既有资产，整组不发射（安全侧）
Characters/Sandy            组里有一条 `HasMod` 条件：那是**可离线烘焙**的（通道 C），
                            而立绘发射器只认 GSQ 翻译 ⇒ 这一条是另一处的口子，不是本次引入的
```

⚠ 五个 NPC 里 `Martin` / `Scarlett` 的**季节外观本来就有**（包自带 `_Appearance0..4`），
所以这次没落到它们**不算损失**；`KrobusRaven` / `Magnus` 是结构上没有落点（前面几节同款结论）。

### 顺手排掉一个"看着能救"的：`HasMod` / `HasFile` 那几条**不是机会**

三套发射器的拒绝理由里有一批写着「`HasMod`/`HasFile` 装不下、无法静态求值」—— 看着像"其实可烘焙、
只是没接上通道 C"。**查完是空欢喜**：

- `HasMod |contains=Poltergeister.SeasonalCuteCharacters`：那个包**根本没装**
  （本机的 Poltergeister 系只有 `Poltergeister.SeasonalCuteSpritesSVE`）⇒ 条件为**假** ⇒
  CP 那边同样不会发射 ⇒ **拒绝是对的**；
- `HasFile:{{FromFile}}` 那几组的目标是 `Characters/GilSprite` 这类**精灵图**，
  它们既不满足"四季图集组要的是 `Data/BigCraftables` 的 `Texture`"，也没有地图把 `Characters/*`
  当 TileSheet 图集引用 ⇒ 就算把 `HasFile` 烘焙掉**也过不了发射器自己的前提**。

⇒ 那些理由的**措辞**可以更准（"条件为假/前提不满足"，而不是"无法静态求值"），但**没有可捞的内容**，
这一轮不动它们。

**全语料扫下来，这类"烘焙掉就能活"的只剩 1 组**：`Characters/Sandy` 的季节立绘 ——
它的条件写的是 `{"HasMod |contains=Poltergeister.SeasonalCuteCharacters": "false"}`，
而那个包**确实没装** ⇒ 条件为**真** ⇒ 4 条补丁本该能落（现在是 `NoLandingPoint`）。
做法清楚（在发射器**看 `When` 之前**先用 Baker 把能烘焙的叶子折掉），但**只值 4 条** ⇒
这一轮**不做**，留档。
