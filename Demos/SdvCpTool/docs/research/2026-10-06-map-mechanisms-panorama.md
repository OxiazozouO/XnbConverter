# 星露谷 1.6 地图机制全景（四路彻查汇总）

日期：2026-10-06
依据：`D:\sdv_src`（1.6.15 全量反编译）、`D:\sdv_gd`、`D:\sdv_tree`、
游戏包 `xTile.dll` / `TMXTile.dll` / `StardewModdingAPI.dll`、两份语料实测

> 四路分工：**格式层**（XNB/tbin）、**属性层**（tbin 四层属性 + `Action` 取值）、
> **数据层**（`Data/*`）、**脚本层**（事件命令）。本文件是汇总。

---

## 0. 三句话结论

1. **地图的「条件」在原版是稀有的。** 全游戏只有 **3 个**能承载 GSQ 的地图落点（见 §2）。
2. **它们全都表达不了「图块内容按条件存在」**——只能表达「图块上的一个行为在条件成立时生效」。
3. **CP `EditMap` 的 5 类能力里，只有 `Remove`（图块置空）在脚本层有对应**；其余 4 类三层都没有。

---

## 1. 格式层：XNB 只能是 tbin，但这不是问题所在

- **XNB 的读取器写在 XNB 自己里面**（MonoGame `ContentTypeReaderManager.LoadAssetReaders`
  从流里读类型名 → `Type.GetType`）。**我们控制得了。**
- **但 `xTile.Pipeline` 下只有 `TideReader` 一个类型**，没有第二个能产出 `Map` 的 reader；
  且 `TideReader` 里 `FormatManager.Instance.BinaryFormat` 是**硬编码**的。
- **`.tbin` = XNB 载荷里的原始字节**（`XNB.cs:296-301` / `:564-569` 双向印证）。

### 旋转/翻转：**tbin 装得下，是渲染器不画**

| 环节 | 结论 |
|---|---|
| 旋转存哪 | xTile 的瓦片属性 `@Rotation`(int) / `@Flip`(int) |
| tbin 存得住吗 | **存得住**。`TbinFormat.StoreProperties` 不筛键不筛类型，原样全写 |
| 我们自己的读写 | `StaticTileReader` / `PropertieReader` 也全量往返 |
| **原版渲染** | **`XnaDisplayDevice.DrawImpl` 写死 `0f` 与 `SpriteEffects.None`** |
| SMAPI 渲染 | `SDisplayDevice` 读 `@Rotation`/`@Flip`（**与地图来源格式无关**） |

> **正确归因**：这是「**不装 SMAPI**」的固有边界，**不是 tbin 的边界**。
> 数据一直在 XNB 里；一旦环境有 SMAPI，旋转立刻生效。

**实测规模**：RSV 30/159 张 tmx 含翻转（2745 瓦片），其中 10 张含**旋转（529 瓦片）**；
语料 B 99/231 张（19306 瓦片），51 张含旋转（2413 瓦片）。
**其余 TMX 特性（`<chunk>`、外部 tsx、`<imagelayer>`、`tintcolor`、`parallaxorigin`、
对象 `rotation`、多边形/折线/文本）在两语料出现次数全部为 0**——
且它们的丢失是 **`TMXFormat` 压根没解析**，不是 tbin 装不下。

**建议：什么都不做**，让属性照常躺在 XNB 里。代价 0，数据无损。

---

## 2. ★ 全游戏只有 3 个能承载条件的地图落点

### ① 图块 `Action` / `TouchAction` = `ConditionalDoor <GSQ>`（**内联 GSQ**）

```csharp
// GameLocation.cs:9546（点击） / 3907（踩上）
case "ConditionalDoor":
    if (GameStateQuery.CheckConditions(ArgUtility.UnsplitQuoteAware(action, ' ', 1)))
        openDoor(tileLocation, playSound: true);          // 成立 → 开门
    else
        doesTileHaveProperty(..., "LockedDoorMessage", ...);   // 否则 → 按属性里的 key 弹提示
```

**参数就是 GSQ 全文。不是自定义语法。**

**实物证据**（字节级核实）：原版 `Maps/AdventureGuild.tbin` 里

```
37 00 00 00 | "ConditionalDoor PLAYER_STAT Current monstersKilled 1000"
11 00 00 00 | "LockedDoorMessage..."
```

**条件已经躺在可直接烘成 XNB 的格式里了。** SVE 也在 `.tbin` 里发同样的东西。

### ② 地图属性 `BuildConditions` = GSQ

```csharp
// GameLocation.cs:2469-2470 —— 只被 IsBuildableLocation() 读
string mapProperty = getMapProperty("BuildConditions");
if (string.IsNullOrEmpty(mapProperty) || GameStateQuery.CheckConditions(mapProperty, this))
    return true;
```

RSV 的 `Custom_Ridgeside_SummitFarm.tmx` 在用：
`value="PLAYER_HAS_SEEN_EVENT Host 75160263"`。

### ③ 图块 `Action` 的**隐式**条件（**不可参数化**）

`LockedDoorWarp`（时间窗 + NPC 友谊）、`Door`（2 心）、`OpenShop`（时间窗 + 方位）、
`MagicWarp`（mail 标志）、`NightTiles`（硬编码 `pamHouseUpgrade`）。
**这些条件写死在 C# 里，不是数据能填的。**

### 其余全是**隐式或没有**

- **层属性（`Layer.Properties`）是纯元数据**——**游戏从不读它的任何键**
- `Data/Festivals/*.conditions` = 自有语法 `<地点>/<起> <止>`，只 2 个自由度

---

## 3. 数据层：**只有一个整图换的槽**

| 机制 | 能否承载条件 | 语料用量 |
|---|---|---|
| **`Data/Locations.<loc>.CreateOnLoad.MapPath`** | CP 可用 `When` 展开成多条地点 | **新建 162 个地点**（RSV 86 + B 76）|
| `LocationData` 本身 | **没有任何 `Condition` 字段** | — |
| `LocationMusicData.Condition` / `Fish/Forage/ArtifactSpots[].Condition` | GSQ | 多 |
| `Data/Buildings.TileProperties` + `Skins[].Condition`(GSQ) | **能往图块注入属性，且优先级高于地图自身**（`GameLocation.cs:13149` 先遍历建筑） | **0** |
| `Data/AdditionalRenovations`（地图属性） | **原生「mail 标志 → 叠加/还原图」** | **0**，且只在 FarmHouse |
| `Data/ChairTiles` | 「零改图给图块加行为」的范例 | RSV 1 |
| `Data/TriggerActions` | **26 个动作，无一改地图** | — |
| `PassiveFestivals.MapReplacements` | **模型注释自证**：*"Despite the field name, this swaps the **full locations**, not the location's map asset."* | 2 条，**都是退化用法** |

**季节是免费的**：季节维度**只存在于 tilesheet**（`spring_` 前缀），地图文件没有季节后缀。
四套 tilesheet 齐全即自动切换，**地图 tbin 只写一份**。

---

## 4. 脚本层：**140 个事件命令，只有 3 个改地图**

| 命令 | 参数 | 语义 |
|---|---|---|
| `RemoveTile <x> <y> <layer>` | — | 图层某格置空 |
| `ChangeMapTile <layer> <x> <y> <index>` | 注意 **layer 在前** | **只改 `TileIndex`**，同 tilesheet、**保留原属性**、**空位报错** |
| `ChangeToTemporaryMap <mapName> [pan]` | — | 新建临时地点，原图不动 |

**其余「像改地图的」全是临时对象**（`addObject` / `addProp` / `addLantern` / `addTemporaryActor` /
`makeInvisible` / `removeObject` / `removeSprite`）——**事件结束即消失**。

### 三条硬限制

1. **无 `SetTileProperty` 命令**（命令区 grep 命中 0）。`GameLocation.setTileProperty` 是 C# 方法，
   **事件层完全够不到** —— 也就是说 **`ConditionalDoor <GSQ>` 那种内联条件，事件脚本写不出来**。
2. **无 `AddWarps` / 无 `MapProperties` 命令**。
3. **改动只在内存**：`SaveGame.cs` / `GameLocation.cs` 里 **0 处**把 map 写回磁盘；换存档/重开即丢。
   且事件是**一次性**的（`eventsSeen`），不是每次进图求值。

**语料用量**：RSV `changeToTemporaryMap` 75 / `changeMapTile` 33 / `removeTile` 2；
语料 B 19 / 29 / 14。**RSV 那 33 条本来就依赖 `eventsSeen`/好感/时间，无法烘进 tbin**——
反过来，语料 B 那 43 条是 CP 里的**静态改动，应该烘进 tbin，不该转成事件**。

**`G <GSQ>` 前置条件**（`Preconditions.cs:744-753`）可挂在事件键上，是**事件侧唯一的 GSQ 入口**。

---

## 5. 对 CP `EditMap` 的最终判定

| CP `EditMap` 能力 | tbin 属性层 | 数据层 | 脚本层 |
|---|---|---|---|
| `MapProperties` | ⚠️ 只有 `BuildConditions` 收 GSQ | ❌ | ❌ 无命令 |
| `AddWarps` / `AddNpcWarps` | ❌ 无条件位 | ❌ | ❌ 无命令 |
| `MapTiles[].SetProperties` | ⚠️ **仅当**设的是 `Action`/`TouchAction` 且语义匹配 `ConditionalDoor` | ⚠️ 建筑 `TileProperties`（用量 0） | ❌ 无命令 |
| `MapTiles[].Remove` | ✅ | —— | ✅ `RemoveTile` |
| `MapTiles[].SetTilesheet` + `SetIndex` | ✅ 可写 | —— | ⚠️ 只能同 tilesheet |
| `FromFile` + `PatchMode: Replace`（整图） | ✅ 可写 | ✅ `CreateOnLoad.MapPath` | ⚠️ 临时地点 |

**结论：`EditMap` 的「图块内容按条件存在」这一类，三层都表达不了。**
这不是我们没找到——是**原版就没有这个概念**。

---

## 5.5 日程键：维度**不可组合**（一条已验证的「确实做不到」）

语料 B 里约 40 条 `Characters/schedules/<NPC>` 被判
「条目键名编码了条件维度，但补丁条件不被键名完全蕴含」。**逐条看过后，这个拒绝是对的。**

真实形态：

```json
{ "Target": "Characters/schedules/Alex", "When": {"Weather": "rain, storm"},
  "Entries": { "spring_14": "…雨天行程…", "marriage_spring_14": "…" } },
{ "Target": "Characters/schedules/Alex", "When": {"Weather": "sun, wind"},
  "Entries": { "spring_14": "…晴天行程…", "marriage_spring_14": "…" } }
```

**同一批键，按天气给两套值。**

原版的日程键链（`NPC.cs:5765-5903` `TryLoadSchedule`）是一条**优先级列表**：

```
GreenRain → marriage_<festival> → marriage_<season>_<day> → marriage_<dow>
        → <season>_<day> → <day>_<friendship> → <day>
        → rain → rain2 → …
```

**`<season>_<day>`（`spring_14`）排在 `rain` 之前。**

所以「**某一天 _且_ 下雨**」这个**组合**在原版键空间里表达不了——
日期维度先命中，天气维度永远轮不到。**原版有天气维度，但两个维度不可组合**
（是优先链，不是笛卡尔积）。

> **结论：这类保持 `NeedsReview` 是正确的，不是我们的能力缺口。**
> 要近似的话只能选一套烘死（丢失另一套），**不值得**。

---

## 6. 一个反复出现的教训

本次四路调研里，「我以为没有 → 实际有」出现了 **5 次**：

| 我的判断 | 实际 |
|---|---|
| `Data/Festivals` 无条件位 | `_spouse` / `_yN` 键 |
| `strings/StringsFromCSFiles` 无条件位 | `$query` 内联 |
| 图片层无条件位 | `Data/Characters.<NPC>.Appearance[].Condition` |
| `Trigger` 地图属性 → `TriggerActions` | 不存在（我猜错） |
| **「RSV 用 C# 注册的自定义动作表达不了」** | **`RSVTeleport`（10 条里 5 条）就是原生 `Warp`** |

**判断一个东西能不能转，要问「它的实现做了什么、原版有没有等价机制」，
而不是「它叫什么、属于谁」。**
