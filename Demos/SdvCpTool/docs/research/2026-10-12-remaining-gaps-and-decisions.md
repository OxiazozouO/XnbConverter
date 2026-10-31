# 当前剩下的缺口与要做取舍的地方（2026-10-12 收尾盘点）

口径：`prof70`（新语料 232 包，`total=32771`、`written=15180`、`writeFailed=201`、`blocked=4795`）。
下面每一条都标了**量**、**为什么现在没做**、**要做的取舍**。

## 一、可以继续做，但需要先定一个「命名方案」的（占 NeedsReview 的大头）

`NeedsReview` 共 **2538 条 / 70 包**，前几档：

| 档 | 条数 | 现在是 | 要做的取舍 |
|---|---|---|---|
| 新建资产（CP 让包凭空造资产） | 533 | 不写 | 我们会**照写**（`Load` 到新名字 ✓）；这 533 条是"名字由令牌拼出来、静态定不了"的 —— 要么按**列举**展开（像四季那样），要么继续不写 |
| 值里含**无法静态解析**的 CP 令牌（`{{Random}}` 之外还有 `{{Spouse}}` 一类） | 382 | 保留原样 + 报出来 | 要不要**确定化**（取第一个备选 / 当前季节…）？已经在 `Target`/`FromFile` 上开了口子（`{{Season}}` 冻结到春天），**值**上还没动 |
| `Load` 带条件（同一个资产名按条件换内容） | 309 | 不写 | 纯 XNB 里一个资产键只有一份内容 ⇒ 要物化就只能**改引用方**。四季那套能改（`<季>_X` + 地图 `ImageSource` ✓），**任意条件**这套改不动（引用方可能在任何包里） |
| 条件可由已装模组/包内文件**离线烘焙** | 173 | 不写 | 这一档看着"可烘焙"却没烘焙 —— 要看具体是哪一类挡住（可能是同一个 `When` 里混了判得了与判不了的叶子） |

⇒ 这四档都不是"再修一个 bug"，是**编码方案**的选择（写不写、怎么命名、怎么改引用方）。**建议先拿一档做样板**（我倾向 382 那档：确定化已有先例、改动只在值那条路上）。

## 二、需要你参与才能验的

- **实机探针**（`tools/AssetProbe`）：要**进存档**后在 SMAPI 控制台敲 `probe_maps` ——
  这一步得你来（我只做到「启动不报错」）。它回答的是"每张地图在游戏里到底建不建得起来"，
  是本项目**唯一的权威判据**（历史两次都靠它抓到真问题）。
- 8 条上游跳过的 mod（4 个 1.5 时代 DLL + 连带 CP + `资源数值背包` + 一个中文 DLL）
  与 13 个**旧版框架**（未被升级）—— 要修得换版本，属于你的决定。

## 三、确认是上游数据、我们不动的

- `Data/Characters`/`Objects`/`Weapons` 打不出包：`EndSlideShow` 位置写着 `true/false`，
  原版是**字符串枚举**（`"MainGroup"`，实测原版 0 个布尔）——`[CP] Lunna`、
  `内容拓展---热闹的下水道` 的源文件就是布尔。**CP 下同样会炸**。
- `[CP] Integrated Minecarts` 的 tmx 里图集名**字面**带前导点（`.paths.png`、`.DesertTiles.png`…），
  连带 8 张地图引用不到图集（9 种名字）。

## 三·四、这一档到底有多大：**新自检量出来的数**（2026-10-12）

新增 `TokenConditionAudit`（**只报告**，进报告 JSON 的 `tokenConditions` 与文本警告）之后，
全语料（`prof76`，232 包）实测：

| | 数 |
|---|---|
| 补丁的**条件键是包内令牌** | **973 / 12111**（带条件的补丁里 ≈8%） |
| **令牌块**的条件键是另一个令牌 | **99 / 2021** |
| 涉及包 | **41** |

最集中的包：`CapeStardew` 180、`Lurking in the Dark` 160、`SVE` 101、`Ridgeside Seasonal` 97、
`Remapping` 79、`Crystal Island` 63；最常被当条件的令牌名：
`EssenceComplete` 146、`IsDitRInstalled` 145、`IsOddYear` 126、`RMP.PRIORITIES.…` 70、`Compatibility` 50。

⚠ **查过没有误报**：把每条 finding 的键拿回**对应包的 ConfigSchema** 里比对 ——
**0 条**"键同时也是配置字段"（那种会被 Baker 正常求值，不该算进来）。所以这个量是实打实的。

**自检这次改动本身是零副作用的**（同参数 `prof74 → prof76`）：232 包**只差 `tokenConditions`
一个新字段**，补丁级 **0 变好 / 0 变坏 / 0 新增 / 0 消失**，产出树**逐字节相同**
（只在 A 0 / 只在 B 0 / 内容不同 0）。

## 三·五、`|contains=` 折叠（**已修，+64 / 0 退化**）

`{{<配置字段> |contains=a, b}}` 原来被当成"带参数所以不认识"原样留着 —— 那个令牌的"值"
就是一串字面花括号（`[CP] Seasonal Cute Characters` 的 `IndoorsWinterOutfit*` 就是）。
`Cp/TokenArgs.TryFoldContains` 按**任一命中**折成 `true`/`false`（语义依据见那个文件的说明）。

全量同参数对比（`prof69` → `prof74`，232 包）：**变好 64 条 / 变坏 0 条**，
`total` 不变（32771）、`written` 15180 → **15244**、`writeFailed` 不变；
另有 8 条从 `NoLandingPoint` 变 `Emitted`（立绘集合发射现在认得出这些令牌了）。
定点回包（本轮变过的 37 个文件）：**18 / 9**，那 9 条**全是已知的上游问题**
（`Data/Characters` 的 `EndSlideShow` 写成布尔，见本文第三节）。

⚠ **顺带发现、暂不动的**：`HasMod |contains=a, b`（逗号列表）在语料里至少 8 个包用到
（Jasper 32 处、Bear Family 24、Integrated Minecarts 12、Ridgeside 12…）。
`InstalledModIndex` 是大小写不敏感的字典 ⇒ 单值口径下两条路等价，
但**逗号列表**的语义（应当是 CP 的"任一命中"）我们目前是"整串比较"（恒假）。
把它换成任一命中后（`prof73` = 新口径 / `prof74` = 旧口径，两轮的差别只有这一条）：
**变好 2 条 / 变坏 2 条 —— 净零**。变好的是 `Maps/panorama` 那条 `Load`（DaisyNiko）与 `Data/SpecialOrders` 那条 `EditData`（Sunberry），
变坏的是 `[CP] Seasonal Cute Characters` 的 2 条农民立绘（`written` → `TargetMissing`）。
⚠ **机制未查清**：两轮之间只差这一行，而它在语义上只作用于「**字符串里的** `{{HasMod …}}`」，
按说不该影响上面这几处（`When` 里的 `HasMod` 走 `Baker`，不经过这里）—— 也可能是
"任一命中"让**别的包**多写/少写了某个资产，经产出树波及。**没查清就不当结论**：
眼下净零 ⇒ **先保持旧口径**，等有把握时再单独改（语义上 any-of 才是对的）。

## 四、剩下 12 种「找不到图片的图集名」明细（**已全部归因，没有说不清的了**）

10 种是**上游数据**（9 种是 `[CP] Integrated Minecarts` 的 tmx 里图集名**字面带前导点**，
外加 `DN.SnS_Water` 那种"包里有图、但包从没发布过它"）；剩下 2 种各归一个已知桶：
`panorama` → 第一档（值里的令牌 / 令牌当条件），`spring_silo` → 第二档（条件 / 令牌驱动的 Load）。


- **9 种**是上面那类上游前导点（`Integrated Minecarts` 引入的）。
- **`panorama`（5 张地图）**：**已修**（第四版，`f7f5e95`）。`FromFile` 是
  `assets/Panorama/{{pan}}_day.png`，`{{pan}}` 的条件键是**另一个 DynamicToken**
  （`{"recolour": "Vanilla"}` / `{"recolour |contains=Vanilla": false}`）。
  病根见下面那串诊断的第 6 条：**护栏返回"判不了"会把令牌标 lossy** ⇒
  `ConfigTokenSubstitutor`「有损一律不代」⇒ 整条补丁的 FromFile 原样留着令牌 ⇒ 补丁被拒。
  改成返回 `false`（与老路径 `Baker` 对未知键的口径逐字一致）之后：
  - 全量同参数对比（`prof76 → prof77`）：**变好 3 条 / 变坏 0 条**；
  - 产出树差异**只有那 3 条**：`Maps/panorama.png` + `.config`（★ 让 5 张地图建不出来的图集终于进来）、
    `Data/SpecialOrders`（基础 + 3 语言）、`ContentHashes.json`；**只在 A 0 / 只在 B 2 / 内容不同 5**；
  - ⚠ **收益比缺口小得多**：自检量出的 973 条是**缺口规模**，而老行为把"未知键"当 **false**，
    在绝大多数情况下**恰好等于**正确判决 ⇒ 真正翻转的只有 3 条。这一版的真实价值是
    **语义正确**（令牌条件被真的求值，而不是一律当假）+ 给"值里的令牌"那一档铺路。
  **试过两版、都回退**（`4dbd4d3` 及带护栏的重做）：第一版 **+34 / −2**；第二版加了两道护栏
  （有损取值不当条件、值里还有未代令牌不当条件）后，单跑反而 **231/334 < 基线 233/334** ✗ ⇒ 回退。
  **两组都留了完整诊断，下一版直接照着做**：
  1. **前提是"把令牌的值完整渲染出来"，不是"比大小"**。实测
     `IndoorsWinterOutfitEnabled` 的值是字面 `{{WinterClothing |contains=IndoorAndOutdoor, IndoorOnly}}`
     —— **这一半已经修好了**（`Cp/TokenArgs.TryFoldContains` + 解析器那条分支，
     commit `134dce9`，语义"任一命中"）；另一半是**值里的内建令牌**（`SeasonToLoad` 的值就是
     内建 `{{Season}}`）—— 那要在**值**上做季节确定化，属下面第一档的取舍，没做。
  2. 判 `When` 时**不能把条目搬进新对象**（Json.NET 会把子节点从原父节点摘走，直接毁掉
     `block.When`）；没有令牌键时应当**原样把原对象交出去**（下游有按 When 对象做键的结构）。
  3. **有损**判出来的令牌（值落到默认块 / 用了 `{{Random}}` 备选）与**值里还有未代令牌**的，
     都不敢当条件 ⇒ 退回"判不了 ⇒ 视为不匹配 ⇒ 落到默认块"的老路。
  4. 判据要能被**红绿单测**钉住（构造 `SeasonalShoes → SeasonToLoad → {{Season}}` 那种链）。
  5. `|contains=` 的**算子语义**在条件键上也要处理（`{"recolour |contains=Vanilla": false}`）。
  6. **第三版**（带前三道闸重做）仍然 231/334 < 基线 233/334。第四版用**探针法**才逮到病根：
     **不是选错块，而是护栏返回"判不了"** ⇒ 调用方把令牌标 `Lossy` ⇒
     `ConfigTokenSubstitutor` 的「有损一律不代」把**整条代换**否掉 ⇒ FromFile 留着
     `{{SeasonalClothes}}` ⇒ 补丁被拒。老路径是把这个键交给 `Baker`，它把**不认识的键当作
     不成立**（false）⇒ 跳过且**不标 lossy**。⇒ 护栏要返回 **`false`**，不是 null。
     单测 `判不了的令牌条件不把令牌标成有损` 把这条钉住。
     **探针法**（同一个包、两版构建各插一个探针、把判决 diff 出来）比"想清楚再试"快得多，
     值得以后照用。
- `dn`（1 张，`SolomonMagicalCavern` 引用 `DN.SnS_Water`）与 `spring_silo`（1 张，`EastScarp_Village`）：
  两条都查清了，**都不是我们漏搬**：
  - `DN.SnS_Water.png` 在 `[CP] Sword & Sorcery` 的 `Assets/Tilesheets/Nature/` 里**确实存在**，
    但那个包**没有任何发布它的 `Load`**（全语料搜 `SnS_Water` 只命中一个无关的物品 ID）
    ⇒ 引用它的地图在 CP 下同样找不到图集，**上游写坏**。
  - `spring_silo.png` 在 `[CP] Rodney O'Brien` 的 `Assets/Tilesheets/SiloVanilla/` 里存在，
    那个包**是**用 `Load` 发布图集的 —— 但补丁写在 **include 文件**
    （`Data/Locations/Tilesheets.json`）里，⚠ **只搜 `content.json` 会漏**（本轮踩了两次）；
    而且它从不写成字面 `spring_silo` ⇒ 名字由令牌拼出来，属于上面第二档
    「**条件 / 令牌驱动的 Load**」，等那一档定方案时一起看。
