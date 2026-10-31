# 其它框架模组的「内容通道」盘点：除了 CP，还有谁在补地图

日期：2026-10-09

起因：用户指出「需要阅读其他框架模组的实现」「CP 模组就那么多」，
即：**剩下的地图整合缺口不在 CP 侧，得看别的框架**。本文是把 Mods 目录里
**所有**非 CP 内容通道逐个读完的结果。

---

## 1. 先把「有多少内容包、谁家的」数清楚

扫 `Mods/**/manifest.json` 的 `ContentPackFor`：

| 框架 | 包数 | 我们处理了吗 |
|---|---|---|
| `Pathoschild.ContentPatcher` | **32** | ✅ |
| `Esca.FarmTypeManager`（FTM）| **4** | ❌ 完全没碰 |
| `PeacefulEnd.CustomCompanions` | 1 | ❌ |
| `DIGUS.MailFrameworkMod` | 1 | ❌ |

⇒ 没处理的有 6 个包。**所以下一步必须先证明这 6 个包碰不碰地图。**

---

## 2. 逐个读完：这 6 个包**没有一个碰地图**

### FTM ×4（`lnh的步行去沙漠`、`lnh额外区域`、`SVE[FTM]`、`女巫沼泽扩展`）

4 个包的 `content.json` **顶层键完全一致**，只有两类：

```
ForageSpawnEnabled / LargeObjectSpawnEnabled / OreSpawnEnabled / MonsterSpawnEnabled
Forage_Spawn_Settings / Large_Object_Spawn_Settings / Ore_Spawn_Settings / Monster_Spawn_Settings
QuarryTileIndex / File_Conditions
```

- 字段是 `MapName`（SVE 那包 383 处）+ 坐标 + 物品定义 —— **在命名地图上刷觅食物/矿/怪/大物件**。
- 全 4 包文本里 `"Warp` / `MapEdits` / `MapPath` / `"Maps/` 命中 **均为 0**。
- 反向核 `FarmTypeManager.dll` 反编译（10384 行）：`Warps` / `MapEdits` **0 命中**，
  只有 `SpawnForage` / `SpawnOre` / `SpawnMonster` / `SpawnLargeObject` 这类**运行时**写入。

⇒ **FTM 是纯运行时生成**，既没有可静态化的地图改动，也不负责「把新区域接进旧地图」。

### CustomCompanions ×1、MailFrameworkMod ×1

运行时伙伴 / 邮件投递，与地图无关。

---

## 3. 框架里**确实存在**另外两条声明式地图补丁通道，但**本机没有消费者**

| 通道 | 长什么样 | 本机使用情况 |
|---|---|---|
| **SpaceCore `PatchContentEngine`** | 自己的内容语言，语句 `Entry(...)` / `Load(...)` / **`MapPatch(...)`**，字段 `Source` / `From` / `To` / `Mode`（↔ `PatchMapMode`），还能桥到 CP 令牌（`FuncCall.Function == "CP"`）| 全 Mods 目录搜 `MapPatch(` → **0 个文件** |
| **BirbCore `SEdit.Map`** | C# 特性驱动：`[SEdit.Map("Maps/Town")] string piece = "assets/piece.tmx";`，带 `…SourceArea`/`…TargetArea` 配套字段 | 本机没有模组用它（`PatchMap` 调用点只在 10/22/25/27/29，其中 22 就是 BirbCore 自己的实现）|

⇒ 这两条**能力是真的**（将来遇到用它们的包要单独处理），但**在当前语料里不产生任何缺口**。

---

## 4. DLL 侧地图操作总表（谁用哪种）

对 69 个反编译模组逐个 grep：

| 原语 | 用在哪些模组 |
|---|---|
| `PatchMap` / `AsMap()` | 10（可见鱼）、22（BirbCore）、25（SpaceCore）、27（CP）、29（可解锁的捆绑包）|
| `ApplyMapOverride` | 25（SpaceCore）、64（SVE）|
| **`AddWarps`** | **只有 27 = ContentPatcher 自己** |
| `locations.Add` | 13（更好的制作）、24（Kokoro）|
| `new GameLocation` | 13、21、25、30、58、62 |
| `RegisterTileAction` | 10、13、22、25、29、64 |
| `MapLoader` | 25（SpaceCore，Harmony 补丁 `GameLocation.getMapLoader` → 地下城）|
| `loadMap` | 01、03、25、29、64 |

**没有任何框架在**原版**地图上加传送点** —— `AddWarps` 只出现在 CP 自己的实现里。

---

## 5. 顺带纠正：`D:\tmp\gv2` 是**过期/残缺**的产出，上一轮拿它当基准是错的

我上一轮用 `D:\tmp\gv2`（那个装了 SMAPI 的实机目录）当「产出」分析，并据此怀疑
「169 张 `Maps/Custom_*` 只有 2 个在 `Data/Locations` 里登记」。**换成当前代码的全量链式产出后对不上**：

| | `gv2`（旧） | 全量链式（当前代码）|
|---|---|---|
| `Data/Locations` 地点数 | 93 | **219**（原版 91 + **128 `Custom_*`**）|
| `Maps/Town` 的 `Warp` 里有没有 SVE 追加的 `50 116 Custom_BlueMoonVineyard 56 49` | **没有**（378 字节，与原版逐字节相同）| **有**（415 字节）|

⇒ `gv2` 是**早期代码或部分包**跑出来的，**不能当基准**；
「169 张自定义地图只登记 2 个地点」是**读了一个过期产物**，不是 bug。

**教训（和上次那条一样，只是换了个形式）**：判断产出对不对，**先确认产出是哪次跑出来的**。
实机目录里躺着的那份很可能早就过期了。

---

## 6. 净结论

- **非 CP 的框架通道在本机不产生任何地图缺口**（FTM 纯刷怪；另两条通道无消费者）。
- **DLL 侧也没有别的加传送点的路子** —— `AddWarps` 只有 CP 用。
- ⇒ 「新地图没被整合」的原因仍然只剩那一条：**CP 的带条件 `EditMap` 落不下来**
  （见 `2026-10-09-why-new-maps-unreachable.md`），而不是某个框架在偷偷补地图。
- 要看产出对不对，**基准必须用当前代码重跑**（`tools/chain-packs.py`）。

---

## 7. 补两条**负结果**（2026-10-10，都是"扫过了、别重扫"）

### 7.1 语料里**没有**模组直接提供 XACT 文件

`Data/AudioChanges` 是唯一的音频注入通道 —— 查过整个 Mods：

- **没有**任何名为 `XACT` 的目录；
- **没有**散落的 `.xwb` / `.xsb` / `.xgs`。

⇒ 想加音频只能走 `Data/AudioChanges`（已改成并进 Wave Bank + Sound Bank，
见 `2026-10-10-why-injected-cues-dont-loop.md`）。
**别再去找"有没有模组直接塞 xwb"**。

### 7.2 目标写在 `Mods/` 下的补丁 —— ⚠ **本节原来的结论是错的（2026-10-11 订正）**

**原结论**（错误）：「这些目标形如 `Mods/<别人的 UniqueID>/…`，本质是跨模组通信，没有消费方 ⇒ 不转、别补」，
并列了一张表把 `Mods/FlashShifter.StardewValleyExpandedCP` 的 **587 条**记成"**别人的** mod id ⇒ 不转"。

**错在哪**：`FlashShifter.StardewValleyExpandedCP` **就是 SVE 自己的 UniqueID** —— 那是它写**自己**的
物品贴图（那张表没有拿目标的第二段去比**包自己的** UniqueID，于是把"自己"当成了"别人"）。
而这些资产**有确凿的消费方，就是游戏本体**：

```
Data/BigCraftables.json 里：  "Texture": "Mods\FlashShifter.StardewValleyExpandedCP\Adventurer Gear"
Data/*.json 里值以 `Mods/` 开头的**贴图类引用**共 **472 处**（`Texture` 445 + `Portrait` 27），
其中 **471 处解析得到**；唯一的例外是 SVE 自己的笔误（见下），不是我们弄丢的。
```

⚠ 一条**踩过的假证据**（免得下次又拿它当铁证）：产出树根目录的 `ContentHashes.json` 里也有
`Mods/...` 的键，但那是**我们自己**写进去的"资产存在性表"（游戏的 `DoesAssetExist` 只查这张表的键），
**不是消费方** —— 拿它当引用会得到一堆假阳性。要数就数 `Data/*.json` 里的**字段值**。

⇒ 照原结论做（"`Mods/` 目标一律不转"）会把 **SVE 的物品贴图整批丢掉**（东西在图鉴里、画不出来）。

**实测（2026-10-11 晚重做，36 包；判据与数字都由 `tools/namespace-ownership.py` 产出）**：

| 命名空间归谁 | 条数 | 写了 | 没写 |
|---|---|---|---|
| **包自己的 UniqueID**（`Mods/<自己的UID>/…` 与 `<自己的UID>/…`）| **633** | **628**（`Converted` 414 + `Emitted` 208 + `ConvertedNoCondition` 6）| 5（`NoLandingPoint` 4 + `TargetMissing` 1）|
| **别的 mod 的 UniqueID** | **81** | **12**（`Converted` 6 + `ConvertedNoCondition` 6）| 69（`NoLandingPoint` 63 + `NotApplicable` 6）|
| 合计 | **714** | **640（90%）** | 74 |

⚠ 上一版这张表（616 / 38 / 654 / "转了 623"）**也是错的**，两处：① 只数了带 `Mods/` 前缀的写法，
**漏了不带前缀的**（`{{ModId}}/…` 解析出来就是光秃秃的 UID），自己那格少算 17 条；② "别人"那格
把**跨包写进 SVE 命名空间**的 10 条混进了别的桶里，数成 38。**这两处都是"没拿工具跑、手搓聚合"的代价**，
所以现在固化成脚本了。

**怎么分自己/别人**：拿目标的命名空间（`Mods/<X>/…` 或 `<X>/…` 里的 `X`）比**本包 manifest 的
`UniqueID`（`Mods/{{ModId}}/…` 这种写成令牌的，解析后就是自己的 id）。

**代码侧同步（2026-10-11 晚）**：`PatchJudge` 原来只按"第一段带点 / 是不是 `Mods`"判，
**分不出"自己"** —— 语料里 0 条命中（这一档 63 条全是真的别人的命名空间），但一旦有包
给**自己的**命名空间写条件化新建资产，报告就会说成"别的 mod 注册的自定义资产……纯 XNB 场景里
没人读它"，**把自己的内容说成别人的**。现在把 `pack.UniqueId` 传进判定，**措辞按归属分**
（自己的那种只保留"类型无从得知 ⇒ 打不了包"这条依据，并明说是**本包自己的内容、真损失**），
**判定不变**（两种都还是 `NoLandingPoint`、都不写）。改前/改后同参数跑全量 36 包：产出树与报告
**逐字节相同**（因为这一档本来就没有"自己"的命中），单测 1340 通过。

### ★★ 更要紧的一条：**"归谁"不是"有没有消费方"**（这才是原错报的病根）

原表真正错的地方不是数错了，是**判据拿错了**：它默认"写在别人命名空间下 ⇒ 没有消费方 ⇒ 不写"。
实测**反例就在同一份产出里** —— **10 条是别的包写进 SVE 命名空间的，我们照写，而且是对的**：

| 补丁 | 目标 | 谁写的 | 消费方 |
|---|---|---|---|
| `EditImage` ×6 | `Mods/FlashShifter.StardewValleyExpandedCP/SVE_Portraits` | 包 27（SVE 季节性服装）| SVE 自己的 `Data/Characters` 立绘引用 |
| `Load` ×4 | `Mods/FlashShifter.StardewValleyExpandedCP/{spring,summer,fall,winter}_GrampletonFields` | 包 28（格兰普尔顿旷野）| SVE 地图的图集名 |

⇒ 正确的判据是「**产出树里的数据资产有没有引用它**」（`Texture` / 图集名 / 立绘名 —— 这一类由
`tools/audit-texture-refs.py` 在管），**不是**「它挂在谁的命名空间下」。命名空间只回答"**这条补丁是不是在给别的模组定制**"，
不回答"游戏读不读它"。

**"没写的那些到底丢没丢东西"也量了**（`--tree` 那一步，按 JSON 解析后逐值比，不按文本比）：

```
python tools/namespace-ownership.py D:\tmp\prof32 --tree D:\tmp\prof32\tree
  == 没写的自定义命名空间目标：16 个资产名（产出树扫了 2762 个 JSON）
     ★ 被引用、且树里连文件都没有：0 个        ← 没有真丢
     ○ 被引用，但树的文件在：2 个
         Mods/FlashShifter.StardewValleyExpandedCP/Hedge Fence Tilesheet        （3 条 EditImage）
         Mods/FlashShifter.StardewValleyExpandedCP/Small Hardwood Fence Tilesheet（1 条）
```

那 2 个是**栅栏贴图带季节条件**（`Data/Fences.json` 的 `Texture` 指的是它，基图写了；
原版没有"按季节换栅栏贴图"的通道 ⇒ 只有季节变体那几条落不了，见
[`2026-10-11-remaining-buckets-why-they-have-no-landing.md`](2026-10-11-remaining-buckets-why-they-have-no-landing.md) §9）。

### 顺带逮到的一条：`Shops.json` 那唯一一条"找不到"是 **SVE 自己的笔误**

`code/Shops/SVE/AxelVendor2.json:13` 写的是 `Mods\FlashShifter.StardewValleyExpandedCP_Aged_Blue_Moon_Wine\Axel_Vendor`
—— 把**物品 id**（`…_Aged_Blue_Moon_Wine`）当成了路径的一段（同目录 `AxelVendor1.json:17` 写的就是对的
`Mods\FlashShifter.StardewValleyExpandedCP\Axel_Vendor`），而**没有任何补丁创建那个名字**。
游戏侧有保护：立绘取不到就退化成"按名字找 NPC 立绘"，再取不到返回 `null`
（`ShopMenu.cs:800-813`）⇒ 商店不显示立绘、**不崩**；CP 场景下同一个字符串**原样**交给游戏 ⇒
两边行为一模一样。所以它进了 `tools/audit-texture-refs.py` 的 `UPSTREAM_DANGLING` 白名单
（要写清源码出处才准进），自检第十行因此还是 **0**（"产出比基准多出来的找不到"）。

**别再拿"目标在 `Mods/` 下"或"命名空间不是我自己的"当不转的理由** —— 要拿**引用**说话。


