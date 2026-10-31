# 全量链式产出的静态审计（四项检查，都带原版基准）

日期：2026-10-09

**为什么用链式**：语料普查是「一包一棵树」跑的，那样跨包的目标必然找不到。
真实用法是把所有包**依次转进同一棵树**。这份记录用的是
`tools/chain-packs.py` 把**全部 32 个 CP 包**按同一顺序跑完的最终树。

⚠ **参数必须一致**：这四项检查里有三项会拿"产出树"和"语料里的全部包"对照，
所以只能跑在全量树上。拿只跑了一两个包的树来对，会把别的包 `Load` 的资产
误报成缺口（本轮实测踩过两次）。

---

## 产出规模

| 项 | 实测 |
|---|---|
| 最终树 | `D:\tmp\chain\32_…\tree`（718 MB）|
| `Data/Locations` 地点 | **252** |
| `Maps/` 资产名 | **1593**（其中 `.tbin` 地图 **376** 张）|
| 地图里的 warp 条目 | **995**（只算 `Warp` 属性；**加上 `NPCWarp` 是 1990** —— 见下方订正）|


---

## 检查一：每条 `Warp` 的目标是否存在 —— **产出新增 0**

`tools/check-map-warps.py <产出树> D:\sdv_tree`

| | 产出 | 原版基准 |
|---|---|---|
| 地图张数 | 376 | 190 |
| warp 条数 | 995 | 786 |
| 查不到的 `toMap` | **3 个** | **3 个** |

查不到的 3 个是 `VolcanoEntrance`（`Island_N`、`Island_N_Trader`）与
`VolcanoDungeon0/9`（`Caldera`）—— **原版自己就没有**（火山那组是运行时生成的），
`Data/Locations` 里查不到属预期。**产出新增 0 个。**

> ⚠ **订正（2026-10-10）**：这里原来的"995 条 warp"**只算了 `Warp` 这一个地图属性**。
> `AddNpcWarps` 写的是**另一个属性 `NPCWarp`**，一开始没收进来 —— 补上之后：
> 产出 **1990** 条 / 原版 **1572** 条。**结论不变（产出新增 0）**，但那个总数当时报小了。
>
> 判 `toMap` 存在会认三样：`Data/Locations` 的键、各地点 `FormerLocationNames`
> 里列的老名字（CP `CustomLocations.MigrateLegacyNames` 写的就是这个字段 ——
> SVE 的 `ShearwaterBridge` 全靠它）、以及 `Maps/<名>`。

## 检查二：每个地点的 `CreateOnLoad.MapPath` 是否存在 —— **产出新增 0**

| | 产出 | 原版基准 |
|---|---|---|
| 有 `MapPath` 的地点 | 240 | 79 |
| 找不到地图的 | **0 种** | **0 种** |

## 检查三：季节性图集引用 —— **0 问题**

`tools/check-map-tilesheets.py`：产出查了 **495 处**带季节前缀的图集引用，
原版 147 处，两边**都是 0 问题**。

## 检查四：内容包 tmx 引用的 tileset 图片 —— **13 种 / 16 处在树里找不到**

> ⚠ **本节结论已订正（2026-10-09 晚）**：当时写「证据指向包侧」是**错的**。
> 根因是我们**漏搬了模组地图引用的「包内同级图集」**，是**我们的 bug**，
> 且会让**整张地图建不出来**（含原版地图 `Maps/Forest`、`Maps/Sewer`）。
> 已修，见 [`research/2026-10-09-mod-local-map-tilesheets.md`](../research/2026-10-09-mod-local-map-tilesheets.md)。
> 下面的追查过程保留，但**结论以那份为准**。


`tools/check-tmx-tilesheets.py`：全语料 `.tmx` 共 **1220 处** tileset 图片引用。

| 找不到的名字 | 处数 | 出处 |
|---|---|---|
| `binoculars` `perch` `spellnspeak` `stonebath` `woodenhopper` `woodenplatform` `plastictube` `seedhuller` `hummingbird` | 各 1 | 鸟会 `assets/map/OrnithologistsGuild.tmx` |
| `spring_exterior` | 2 | 鸟会 `Forest_OrnithologistsGuild.tmx` / `…__SVR3.tmx` |
| `winter_exterior` | 1 | 鸟会 `Forest-IceFestival_OrnithologistsGuild.tmx` |
| `spring_daisyextras` | 2 | lnh `WaterfallForest_N_NewArea.tmx`、鸟会 `…__SVR3.tmx` |
| `spirit_sewer` | 2 | 热闹的下水道 `Sewer_aimon111_mundane*.tmx` |

### 追查（鸟会那一组）

证据链：

1. 包把 `assets/map/Binoculars.png` **只** `Load` 到
   `Tilesheets/Ivy_OrnithologistsGuild_Binoculars`（给**物件**当贴图用，
   `usables.json:11`），**全包**（含 `Include` 的 `OrnithologistsGuild.json` /
   `patches.json` / `compatibility.json`）**没有一处** `Load Maps/Binoculars`；
2. `[CP]` 包自带的 `OrnithologistsGuild.CP.dll` 是**空桩**（反编译出来只有程序集属性，无类型）；
   主 DLL `Ivy.OrnithologistsGuild` 的 `AssetRequested` **只**处理
   `Mods/Ivy.OrnithologistsGuild/Nest` 一个名字；
3. 游戏侧：`xTile.Display.XnaDisplayDevice.LoadTileSheet` **直接**拿
   `tileSheet.ImageSource` 当资产名去加载（`xTile.dll` 反编译），
   而 `GameLocation` 对**没有目录**的 `ImageSource` 默认按 `Maps/` 解析
   （`gameloc.cs:1138-1143`：`Path.GetDirectoryName(...)` 为空时 `text = "Maps"`）；
4. 我们产出树里那张 tbin 存的就是 `Binoculars.png` / `Perch.png`
   —— **与输入一致**（不多、不少、不改名）。

⇒ **产出是忠实的；是包自己没有把这些图集发布成地图会去找的名字。**
**未确证**的是「实机上到底会不会报错」：`LoadTileSheet` 是**懒加载**
（`XnaDisplayDevice.DrawTile` 里 `if (!m_tileSheetTextures.ContainsKey(tile.TileSheet)) LoadTileSheet(...)`），
只有画到那块瓦片时才触发 —— 本轮没有实机验证这一点。

### 追查（另两组）

- `spring_daisyextras` 由 `DaisyNiko.SVR3` 提供，而该前置**没装**：
  引用它的两个补丁一个 `When` 是 `HasMod |contains=DaisyNiko.SVR3`，
  另一个（lnh 的）`When` 是包内配置项 `WaterfallForest_N_NewArea`。
  ⇒ 属于「引用了别的 mod 的图集」这一类，**条件门挡着**。
- `spirit_sewer`（实际写法 `image source="Spirit_Sewer.png"`）与鸟会那组同形。

---

## 附：那 3 条「写入失败」是**假失败**（独立复核）

SVE 的报告里 `attemptedWrite 2591 / written 2588 / writeFailed 3`，三条都是：

```
EditData  Characters/Dialogue/{Claire,Edmund,Martin}
  对话值内联 GSQ 失败：条目键 `Mon`/`Wed`/`summer_Tue2` 无法内联成 `$query`：
  取不到旧值（合并前的目标里没有这个键）
```

**为什么是假失败 —— 靠 CP 的排序推出来的**：

1. CP `PatchLoader.LoadPatches` 把 `Include` 进来的文件挂在**那条 Include 自己的 IndexPath** 下
   （`LoadPatches(RawContentPack, contentConfig.Changes, base.IndexPath, …)`，反编译 :14404）；
2. `PatchIndexComparer`（:14853）**按 IndexPath 字典序**比，不比 Priority；
3. SVE 的 `content.json` 里：`code/npcs/Claire.json` 在 **Include #79**，
   `Assets/CharacterFiles/Dialogue/Claire/Claire.json` 在 **Include #82**
   ⇒ `[0,79,*] < [0,82,*]` ⇒ **基础文件后apply，会盖掉条件补丁写的那几条**。
4. 实测产出里这些键**都在**，值就是基础文件那版：
   `Mon = "Let me know if I can do anything for you.$2"`、
   `summer_Sun = "I want to find something worth working toward...$0#…"` 等。

⇒ **CP 跑出来的最终值和我们产出的**一样**（基础版胜出）**，这几条补丁在 CP 下也不会有持久效果。
我们只是因为"取不到假分支的旧值"而**拒绝**了它 —— 拒绝时**如实记成写入失败**，
没有伪装成成功。与 `2026-10-09-write-failed-eight-classified.md` 里"3 条是假失败"的结论一致。

---

## 净结论

**地图/地点图的结构性检查全绿**：warp 目标、地点 MapPath、季节性图集三项
相对原版**都没有新增问题**。唯一未解析的是 **13 个 tileset 图片名**，
证据指向**包侧**（引用了自己没发布 / 别的 mod 没装的图集），不是我们的产出丢了东西。

⇒ 「新地图进不去」剩下的解释仍然只有一条：**带条件的 `EditMap`（贴地形块/入口）落不下来**
（见 `2026-10-09-why-new-maps-unreachable.md`），那是能力边界，不是产出损坏。

---

## 复跑（2026-10-10，音频注入改动之后）

这轮动了音频（`Data/AudioChanges` 改成并进 Wave Bank + Sound Bank、
`PatchJudge`/`WritePolicy`/`PatchApplier`/`Program` 四处接线），所以又跑了一次全量链式
确认那九项自检没被带坏。

**这次用的是 `tools/convert-mods.py`**（按各包 `manifest.json` 的 `Dependencies`
自动定序），比 `chain-packs.py` 手写顺序更接近真实安装 —— 32 个包全部跑完，
产出在**最后一个包**的 `tree/` 下（它是链式累积的终点）。

`python tools/verify-tree.py <最后那棵 tree> D:\sdv_tree`：

```
每条 Warp 的目标地点 / MapPath / 每张地图引用的图集图片 / 图块上的 Warp 动作 /
物品图集·精灵下标 / 季节性图集变体            → 新增 0
语料 tmx 的 tileset 图片                     → 新增 4
地图 Message 的文本                          → 新增 2
LocalizedText 引用                          → 新增 1
```

⇒ **与 2026-10-09 那次逐项一致**（6 项 0 + 那 3 项已知且非我们），**没有回归**。

⚠ 一处操作教训：`convert-mods.py` 的输出是 `<out>/<NN>_包名/tree`（**每包一棵**），
不是单一树 —— 要拿**最后一个**包的（链式的终点）去跑自检。
另外那个包名带中文和空格，从 Git Bash 传参给 Python 会被**截断+乱码**，
得在 Python 里自己 `glob` 出来（或在 Python 内 `runpy` 调用）。
