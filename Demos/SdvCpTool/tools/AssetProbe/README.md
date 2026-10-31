# AssetProbe —— 装机探针（SMAPI mod）

装机之后**唯一**能自动回答"每张地图到底建不建得起来"的东西。历史：`docs/how-to-build-an-install.md` §5
一直指着 `local.SdvCpTool.AssetProbe`，但那个工程在仓库里和全盘都**找不到**（空指）；
这份是 2026-10-11 重建的。

## 它做什么

对每一个地图资产：**真的 `new GameLocation(资产名, "Probe")` 建一遍，再碰一下它的 `.Map`**
—— 这一步走的是游戏**自己的**加载路径（`GameLocation.loadMap` → `getMapLoader()` →
`Game1.game1.xTileContent`，`GameLocation.cs:1941/1599`），顺带把图集也解析了
（比"能读成 Map"严格得多：图集缺一张就在这里炸）。最后打一行汇总 + 失败清单。

两个**对源码核过**的细节（都不是随手写的）：
- 地图在 1.6 里走 **`xTileContent`** 而不是 `Game1.content` ⇒ 探针**不自己** `Load<Map>`（用普通内容管理器读会得到**假失败**）；
- 地点名用固定值 `"Probe"`，**不用资产名**：构造里按名字判断 `isFarm`/`isStructure`
  （`name.Contains("Farm")` 之类，`GameLocation.cs:1536`），资产名带 `Farm` 的会被当成农场走另一条路。

⚠ **`Maps/` 底下不全是地图**（`spring_outdoorsTileSheet` 这类图集图片也在那儿）——
它们**不算失败**：探针先按 `Map` 试、失败再按 `Texture2D` 试，能当贴图读出来的记成「跳过」。
（老探针不区分，于是"失败 N 个"里混着一堆图集，得靠"拿 `.tbin` 数 vs `.png` 数对照"来判读；
现在这一步在探针里做掉了。）

## 怎么用

```bash
# 1) 编（要 .NET 6 目标包；SMAPI 4 只加载 net6.0 的程序集）
dotnet build -c Release tools/AssetProbe
# 换机器时指定游戏目录：dotnet build -c Release tools/AssetProbe -p:GamePath="D:\...\Stardew Valley"
#   ⚠ 要指**带游戏程序集**的那份（本机是 D:\Stardew.Valley.v1.6.15\Stardew Valley）；
#      本机 Steam 那份是"只有 SMAPI + Content + Mods"的便携布局，指着它会找不到引用。

# 2) 拷进 Mods（DLL + manifest 两个文件）
mkdir -p "<游戏>\Mods\AssetProbe"
cp tools/AssetProbe/bin/Release/net6.0/AssetProbe.dll tools/AssetProbe/manifest.json "<游戏>\Mods\AssetProbe\"

# 3) 进游戏 → **读一个存档** → SMAPI 控制台敲：
probe_maps
#   可选给清单：probe_maps "D:\tmp\maps.txt"（每行一个资产名，`#` 开头当注释）

# 自动化验收（不进控制台）：启动游戏前设环境变量，读档后自己跑一次
#   SDVCP_PROBE_AUTORUN=1          读档稳定后自动执行 probe_maps
#   SDVCP_PROBE_LIST=D:\tmp\list   可选，只测清单里的资产
```

⚠ **必须先进存档**：主菜单下内容管理器是空转的（老探针的教训：主菜单**不加载声库**，
"启动零报错"什么都证明不了）。没进存档时探针会先警告一句。

## 怎么读结果

```
—— 探针结果：PASS 376 / 贴图跳过 69 / **FAIL 0**（共 445 个）
```

- **FAIL 0** = 安装里每一张地图都建得起来；
- FAIL 非 0 = 逐条看 `FAIL <资产名> —— 异常`：常见的是图集缺失（`无法加载图集`）、
  资产本身不在产出里（`找不到资产`）。拿资产名回产出树里找那张 `.tbin`/那些图集。

## ⚠ 未确证

**这份探针没有在游戏里跑过**（本机跑不了：游戏那份 MonoGame 是 x86）。
能证明的是：它**能编译**（对着游戏与 SMAPI 的真实程序集）、逻辑与要检验的东西一一对应；
**不能**证明它在游戏里跑起来不报错。
