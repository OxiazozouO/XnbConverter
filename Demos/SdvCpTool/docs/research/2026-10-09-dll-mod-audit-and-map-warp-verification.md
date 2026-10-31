# DLL 模组全量反编译审计 + 地图 Warp 逐字节核对

日期：2026-10-09

起因：用户命令「**所有涉及 dll 的模组都需要反编译，以此理解 cp 模组**」，
以及对「1000 多判断是走错了」的指控。本文是照着做的结果，**含三条证伪**（其中一条是
我自己上一轮的说法）。

---

## 1. 做了什么：69 个带 DLL 的模组全量反编译

`D:\tmp\decompile_mods.py` → `D:\tmp\mods_src\<01..69>\`（每个含 `dll<N>\*.decompiled.cs` 与 `_MOD.txt`）。
再用 `D:\tmp\scan\dll_assets.py` 全量扫「DLL 对**原版**资产（`Data/…`、`Maps/…`、`LooseSprites/…` 等
顶层目录形状）的**写入**」——写入 = 同一行或前 3 行出现
`EditData / Edit( / EditImage / PatchMap / ApplyMapOverride / ReplaceWith / LoadFromModFile /
AddWarps / SetProperties / SetIndex / AsMap( / AsDictionary / AsImage / CopyFrom / InvalidateCache`。

### 结果：被 DLL 写入的原版资产**只有 7 个**

| 资产 | 模组 |
|---|---|
| `Data/Mail` | 可解锁的捆绑包 |
| `Data/mail` | 邮件框架模组 |
| `Data/CraftingRecipes` | 更好的制作 |
| `Data/Objects` | 项目扩展 |
| `Data/Shops` | 快乐家具设计 |
| `Maps/Woods` | 可见鱼（装了 SVE 时贴一个雕像）|
| `Maps/VFStatue` | 可见鱼 |

全都是**小体量 / 装饰性**的。**没有一个 DLL 在原版地图上新增区域或传送点。**

⚠ 判据说明：`IsEquivalentTo("X")` 之类的命中里，很多是**读**（如 `LooseSprites/Cursors` ×4
全部出现在 `OnAssetReady`/`Load<Texture2D>` 里，不是写）。上表只统计写入。

### 这条结论对「进不去」意味着什么

**它不解释「进不去」**：纯 XNB 场景里**根本没有 DLL**，所以 DLL 的运行时行为不是原因，
而是一条**能力边界**（少数小功能会丢，例如装了 SVE 时 `Maps/Woods` 上少一个雕像）。
我在上一轮把这条线索当成「用户指控的核心」，那是**过度解读**。

---

## 2. 纠正我自己上一轮的一个说法（第一处证伪）

上一轮我写过：「被拒的 417 条 `FromFile` 是贴地形块，**传送点只有 7–8 条**，大多已写」。

**这个说法不准。** 按「补丁体里含 `Warp` 字样」重扫 SVE：

| 机制 | 带 When（被拒） | 不带 When（已写） |
|---|---|---|
| `FromFile` | **31** | 5 |
| `MapProperties`（含 `Warp` 属性）| **9** | 5 |
| `MapTiles` | 3 | 0 |
| `AddWarps` | 1 | 0 |
| 其它（`TextOperations` 指向 MapProperties 等）| 4 | 2 |

合计 **60 条**含 Warp 的地图补丁，其中**被拒的远不止 7–8 条**。

### 但不能据此夸大 —— 第二处证伪（我先怀疑、后推翻的）

我原本的下一步假设是：「`FromFile` 补丁块自带的 **地图级 `Warp` 属性**会顺带写进去，
我们漏了」。**读了 CP 2.3.0 源码后不成立**：

- CP `EditMapPatch.TryApplyMapPatch`（`ContentPatcher.decompiled.cs:13960+`）只调
  `targetAsset.ExtendMap(...)` + `targetAsset.PatchMap(source, area, area2, PatchMode)`；
- SMAPI `IAssetDataForMap.PatchMap`（`StardewModdingAPI.decompiled.cs:27403+`）**只**复制
  **图集属性 / 图层属性 / 图块属性**，**从不碰地图级 `Properties`**。

⇒ 补丁块自带的 `Warp` **CP 自己也丢**。我们照抄 SMAPI 的 `MapPatcher`，行为一致。
`MapPatches/` 里 89 个「含 Warp」的块大多是残留（图块上的 `TouchAction Warp` 才算数）。

---

## 3. 第三处证伪：产出地图**没有**被我们写坏

实机产出（`D:\tmp\gv2`）里 `Maps/Backwoods` 的 `Warp` 属性看着像被写坏：

```
原版:  50 10 Mountain 0 13 50 11 … 50 16 Mountain 0 13 50 17 Mountain 0 13 …   (374 字节)
产出:  50 11 Mountain 0 13 50 12 … 50 16 Mountain 0 13 50 16 Mountain 0 13 …   (354 字节)
```

首条丢失 + 末条重复 —— 很像 off-by-one。**逐条核完，不是我们**：

1. **SVE 自己 `Load` 它**：`{"Action":"Load","Target":"Maps/Backwoods",
   "FromFile":"Assets/Maps/Locations/Backwoods.tbin"}`（无条件）。那份文件里的 `Warp`
   **就是 354 字节、`50 16` 重复**。我们逐字节照抄 ✓。
2. **XnbConverter 往返不背锅**：原版 `Backwoods.xnb` 解包→打包→再解包，`Warp` 仍是 374 字节原样。
3. **我们的地图写入路径不背锅**：最小夹具（单条无条件 `MapTiles` 补丁）跑 `convert`，
   `Warp` 374 字节原样。

⇒ 所以「产出与原版不同」的地方，**多数是 SVE 自己 `Load` 的版本**，不是我们改坏的。
（旧测试安装 `_451212886_0.txt` 里那条 `Failed loading asset 'Data\AdditionalFarms'` 是当时**未完成态**
的树，现已在 `gv2` 里确认存在 ✓。）

---

## 4. 一条待追的实机失败（唯一一条非夹具噪声）

探针（`AssetProbe`，在 `gv2` 上把每张 `Maps/*.xnb` 都建成地点）日志里，除
「把图集图片当地图」那类噪声外，只有一条真失败：

```
✗ Maps/Custom_Ivy_OrnithologistsGuild :: Could not find file 'D:\tmp\gv2\Content\Maps\Binoculars.xnb'
```

查到的对接不上：

- 鸟会的地图 `assets/map/OrnithologistsGuild.tmx` 写的是 `image source="Binoculars.png"`
  —— 地图图集按 **`Maps/`** 解析 ⇒ 要 `Maps/Binoculars`；
- 而 CP 包把它 `Load` 到 **`Tilesheets/Ivy_OrnithologistsGuild_Binoculars`**
  （`assets/object/usables.json`）—— 名字完全不同。

⇒ 这个地点**建不出来**。**未确证**：是我们漏了 CP 的某个「地图图集别名」机制，
还是原 mod 自身就依赖别的东西（它自带一个 `OrnithologistsGuild.CP.dll`，反编译里
`grep Tilesheet` **零命中**，所以不是它在改）。

> ⚠ **后续已查明（同日）**：是**我们**漏搬了 —— SMAPI 的 `ModContentManager.FixTilesheetPaths`
> 会**先按模组目录**解析地图图集（`LocalTilesheetExtensions = { ".png", ".xnb" }`），
> 纯 XNB 产出里没有那一层，所以那些包内同级图片必须一起搬进产出树。
> 已修，见 [`2026-10-09-mod-local-map-tilesheets.md`](2026-10-09-mod-local-map-tilesheets.md)。
> **「未确证」的答案就是"我们漏了"。**

---

## 5. `SelectedWarpMap` 追到了底（原先被我误判为「无处定义」）

SVE 的 `content.json` 里 21 处 `SelectedWarpMap`，我一度 grep 不到定义处就说它「无处定义」——
**错的**，它是 **DynamicToken**（4 条定义，后者按 `When` 覆盖前者）：

```json
{"Name":"SelectedWarpMap","Value":"Custom_GrandpasShedOutside"}
{"Name":"SelectedWarpMap","Value":"{{FarmMapAsset}}","When":{"WarpLocation":"default, custom"}}
{"Name":"SelectedWarpMap","Value":"{{FarmMapAsset}}","When":{"HasMod":"…GrandpasFarm","FarmType":"Standard"}}
{"Name":"SelectedWarpMap","Value":"{{FarmMapAsset}}","When":{"FarmType":"FrontierFarm"}}
```

⇒ 它等于 **`{{FarmMapAsset}}`** = 运行时才知道的「当前农场地图资产名」。
它被用在 **3 条 `Target: "Maps/{{SelectedWarpMap}}"`**（改玩家当前农场那张地图）——
这三条判 `TargetContainsTokens` 是**对的**（离线不知道玩家选的哪种农场）。

---

## 6. 结论与下一步

- **DLL 不是「进不去」的原因**（纯 XNB 里没有 DLL），它是一条能力边界。
- **产出地图与原版不同，主要是 SVE 自己 `Load` 的版本**，不是写坏。
- **真正还缺的仍是那一批带条件的 `EditMap`**（SVE：原版地图上 207 条 `LocalEdit`，
  其中含 Warp 的 14 条）。它们的条件依赖运行时状态（`HasSeenEvent`/`Time`/`Weather`/`Random`/
  `FarmType`），离线无论如何判不了 —— 结论与 `2026-10-09-why-new-maps-unreachable.md` 一致。
- **仍未确证**：「原地 TP」的**直接**原因。缺的输入是**具体哪张地图、哪个入口**。
  在此之前能做的：把产出树重新生成一次，做一遍**全量地图 Warp 目标存在性审计**
  （每条 `Warp` 的 `toMap` 是否真有对应地图/地点），这类静态审计能把
  §4 那种「地点建不出来」一次性全捞出来。
