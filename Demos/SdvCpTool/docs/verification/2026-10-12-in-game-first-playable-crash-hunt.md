# 实机首跑（把产出装进游戏副本）抓到的三类问题

日期：2026-10-12
做法：`D:\Stardew.Valley.v1.6.15\Stardew Valley\` **整份复制**到 `D:\tmp\gv\`，把 `packed80`（prof80 的整树回包）
覆盖进 `Content/`，**原版 `Stardew Valley.exe` 启动**（不走 SMAPI —— 走了那 232 个 CP 包会重新施补丁，
就验不到我们的 XNB）；第二趟走 `StardewModdingAPI.exe` 并装了地图探针，拿 SMAPI 那份**更细的日志**定位。

> ⚠ 装机口径：**覆盖式**（见 [`../how-to-build-an-install.md`](../how-to-build-an-install.md) §4），
> 而且**最后必须重建 `ContentHashes.json`** —— 这条是本轮才补上的。

## ① 资产清单：装机不重建它，游戏一进建号菜单就崩（已修 + 已写进流程）

- 症状：标题画面 → 建号菜单时 `Unhandled exception. ContentLoadException: Could not load
  Mods/DaisyNiko.LavenderMeadows/icon asset!`（`CharacterCustomization.LoadFarmTypeData` → 枚举自定义农场）。
- 根因：游戏的 `LocalizedContentManager.DoesAssetExist` **不看文件系统**，查的是
  `<Content>/ContentHashes.json` 的键；而该文件是**没有 `.config` 边车的裸 `.json`** ⇒ 打包器静默跳过
  ⇒ 覆盖后副本里留的是**原版那份**（3,560 键）⇒ 我们新加的资产全不在清单里 ⇒ 加载必失败。
- 修法：新增 `tools/gen-content-hashes.py`，**按最终 Content 目录里实际存在的 `*.xnb`** 生成
  （值 = 文件字节的大写 MD5）。装进副本后：**15,362 条，缺 0、多 0、MD5 全符合** ✓；游戏随即过这一步。
- 哈希算法与游戏对齐的证据：拿**原版**那份清单（3,560 条）抽 300 条比 ⇒ **300/300 相符**。

## ② 27 个地点建不起来：地图引用了「带前导点」的图集名（已修，待复验）

SMAPI 日志把话说得很清楚：

```
Location 'Town' failed to load seasonal asset name 'Maps\.spring_outdoorsTileSheet' for tilesheet ID 'z_Landscape'
Couldn't create the 'Town' location. Is its data in Data/Locations invalid?
```

两条路径要分清（都在 `GameLocation.updateSeasonalTileSheets` 里）：

- 季节换名那条**只记日志不抛**（`catch { Game1.log.Error(...) }`）；
- **致命的是 `xTile.Map.LoadTileSheets` 抛** ⇒ 地图建不出来 ⇒ 地点没了 ⇒ 跨天时
  `RequireLocation("Town")` 抛 `KeyNotFoundException` ⇒ 整游戏崩。

建不起来的 **27 个**（含原版 `Town` / `Mountain` / `Forest` / `Woods` / `Desert` / `Sewer` /
`IslandWest` / `Caldera` 等）；致命的资产名只有 3 个：
`Maps\.spring_outdoorsTileSheet`(9 次)、`Maps\.paths`(5 次)、`Maps\spring_silo`(1 次)。

### 前导点是什么意思（两个方向都实测过）

| 包 | 它的 tmx 里写的 | 包里有没有那份图 | 结论 |
|---|---|---|---|
| `[CP] East Scarp` | `paths.png`（**不带点**） | **有**（`assets/Patches/paths.png`） | 不带点 ⇒ 先用**包内同级**那份 |
| `[CP] Integrated Minecarts` | `.spring_outdoorsTileSheet.png`（**带点**） | **没有** | 带点 ⇒ 用**游戏自己**那张（剥点后的 `Maps/spring_outdoorsTileSheet` 原版在） |

⚠ 纯 XNB 产出里**没人会剥这个点**（那是 SMAPI 运行时干的）⇒ 照原样写进 tbin，游戏就按
`Maps/.spring_outdoorsTileSheet.xnb` 找 ⇒ 必失败。文档原先把这批记成「上游数据前导点错名字」
—— **成因记反了**：名字是上游的写法，但**它本来有解**（剥点 = 游戏资产），我们没有剥。

修法（`Maps/LocalMapTilesheets`）：带点的引用 **剥点 + 去掉 `.png`、改写成原版那种裸名**
（`spring_outdoorsTileSheet`，原版 tilesheet 的 `ImageSource` 就是这个形状），并把地图重新落盘；
**带点的引用一律不去搬包内同名图**（包内那份通常只是给 Tiled 看的参考副本，搬过来会把原版盖掉）。
游戏侧的换季逻辑会把 `spring_X` 换成当季 ⇒ 原版四张 `*_outdoorsTileSheet` 都在 ⇒ 换季也正常。

## ③ 我自己的操作失误（记下来免得再犯）

在打包器**还在写** `packed80` 时就拷了安装、还起了游戏 ⇒ 报告里 9 个「游戏找不到的文件」
（`Maps/hime_*`、`Maps/z_*`）其实是**拷贝没完成**的假缺失 ✗。教训：**装机脚本要等打包器退出**，
或者装完再按 `ContentHashes.json` 的 `--check` 对一遍（`gen-content-hashes.py --check` 就是干这个的）。

## ④ 自动自检（`tools/GameSmoke/`）：把"人点一遍"变成可重复的一步

人工点太慢也不可复现，所以写了个 SMAPI mod 走**游戏自带的**跳过开场路径：

```
TitleMenu.createdNewCharacter(skipIntro: true)   ← 反编译：loadForNewGame() → eventsSeen.Add("60367")
                                                    → 放到家里床上 → Game1.NewDay(0f) 起床
→ 逐张地点 Game1.getLocationFromName + Game1.warpFarmer（真传送）
→ Game1.NewDay(0f) 跨 3 天
→ 结果边跑边写 Mods/GameSmoke/smoke-result.txt
```

**装上 `packed81` 后的实机结果**（SMAPI 启动，全自动）：

```
# 进游戏了：第 1 天，玩家在 FarmHouse
# 巡 38 个地点 → 传送成功 31 / 建不起来 7
# 跨天第 1/2/3 次 —— 全部走完，游戏没崩
```

⇒ 第二节那 27 个地点的问题**在实机上消失了**：原先建不起来的 Town / Mountain / Forest / Woods /
Desert / Sewer / IslandWest / Caldera / Custom_* 现在都建得起来，"跨天 `RequireLocation("Town")` 崩"这条链断了。
地点从 **27 个 → 15 个**（游戏自己那份日志口径），找不到的资产名从 3 类（9+5+1 次）降到 4 个各 1~2 次。

同时补测到整树回包（`prof81`）：**成功 15381 / 失败 19**、`Destination array` 0 次、
`Wave Bank.xwb` 1 390 030 804 B —— 与上一段推算的 19 一致 ✓。

## ⑤ 剩下的 15 个地点：三种成因（只做得动前两种）

| 类 | 例子 | 处置 |
|---|---|---|
| 地图**根本没产出**（TMX 的 XML 层数据没解析） | `EastScarp_JacobBarn` | ★ 已修，见下 |
| 图**不在**地图同级目录（在包的别的子目录） | `spring_silo`（`Assets/Tilesheets/SiloVanilla/`） | 待办：查找规则只看地图同级 |
| `CreateOnLoad.Type` 指向 **DLL mod 的自定义地点类** | `Custom_CapeKeyMaze`（`CapeStardewCode.CustomLavaLogic`） | **修不了**（纯 XNB 安装里没有那个 DLL）⇒ 只报 |

### ★ TMX 的 XML 层数据（已修 + 定向验证）

症状：`EastScarp_JacobBarn` 的那条 `Load` **判了 `Converted` 却写失败**，note 是
`TMX 转换失败：不支持的图层数据编码 ''` —— 即 Tiled 的**默认 XML 写法**
（`<data><tile gid="13"/><tile/>…</data>`，没有 `encoding` 属性；空格子写成没有 gid 的 `<tile/>`），
而我们只认 `csv` 与 `base64`。⇒ 那张地图**产不出来** ⇒ 用它当 `CreateOnLoad.MapPath` 的地点建不起来。

修法（`TmxToTbin.DecodeData`）：补这一档；格子数必须与地图尺寸相符，不符（无限地图的 `<chunk>`）**明确拒绝**。

定向验证（只转 East Scarp 那几个包，同参数）：

| | `Maps/EastScarp_JacobBarn.*` |
|---|---|
| 修前 | **没有**（`prof81` 的产出树里查不到） |
| 修后 | `EastScarp_JacobBarn.tbin`（10 463 B）✓ |

