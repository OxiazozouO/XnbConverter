# 实机探针：图集修复前 / 后，安装里有多少张真地图能建起来

日期：2026-10-09

## 怎么测的

1. 把 `chain3`（改后）全量产出**覆盖式**装进游戏副本
   （先铺**原版** `Content` 3550 个 xnb，再把我们的 6404 个覆盖上去 → 6409 个）；
2. 用探针 mod（`local.SdvCpTool.AssetProbe`）跑一次 —— 它对 `Maps/` 下每个资产
   做 `new GameLocation(mapPath, name)`，建不起来的写进 SMAPI 日志；
3. **判读口径**（重要）：探针**不区分地图和贴图**，`Maps/` 下的图片资产它会当成地图试，
   必然报 `Unable to cast Texture2D to xTile.Map`。所以"失败 N 个"本身没意义 ——
   要用**把安装的 `Maps/` 解开、数 `.tbin` 与 `.png` 各多少**来对照。

⚠ **装机必须覆盖式**：替换式（只放我们的产出）会因打包器打不出
`Data/Buildings.xnb` 等 5 个资产而**启动就崩**（实测报
`Failed loading asset 'Data\Buildings'`）。见
`research/2026-10-09-five-data-assets-cannot-be-packed.md`。

> ⚠ **订正（2026-10-10 晚）**：那 5 个资产今天打得出来（四次独立复验写在那篇订正里），
> 所以「替换式会崩」这条**前提没了**。但**替换式没有重新实测过** ——
> 本文的实机结论（下面那张表）是在**覆盖式**安装下取得的，仍然有效。

## 结果

| | 安装 `Maps/*.xnb` | 其中真地图 `.tbin` | 其中图片 `.png` | 探针报「成功」 | 探针报「失败」 | **真地图失败数** |
|---|---|---|---|---|---|---|
| **改前**（旧安装）| 788 | 376 | 412 | 374 | 414 | **2** |
| **改后** | 813 | 376 | 437 | **376** | 437 | **0** |

- 改后：**成功 376 = 真地图 376**，**失败 437 = 图片 437** —— 一一对应，**没有一张真地图建不出来**。
  （两个数字来自**独立的两条路**：探针日志 vs. 我把安装的 `Maps/` 解开数文件。）
- 改前：`414 = 412 图片 + 2`，也就是**有 2 张真地图建不出来**。

## 那 2 张是哪两张

把改前那份安装的 `Maps/` 解开、用 `tools/check-map-tilesheet-assets.py` 查，
**引用了包内同级图集**（必然建不出来）的是：

- `Custom_Ivy_OrnithologistsGuild`（9 个图集：binoculars / perch / spellnspeak /
  stonebath / woodenhopper / woodenplatform / plastictube / seedhuller / hummingbird）
- `JasParty`（`spring_JasPartyTiles` 及其季节族）

⇒ 与"2 张"对得上。这两张正是
[`research/2026-10-09-mod-local-map-tilesheets.md`](../research/2026-10-09-mod-local-map-tilesheets.md)
修掉的那类。

## 补一道：直接在**装好的那份 Content** 上查图集

探针只看得到 `Maps/*.xnb` **顶层**（它的 `maps.txt` 是 `ls Maps/*.xnb` 生成的），
所以 `Maps/Mines/` 下那 69 张没被它覆盖。补一道静态检查，**对装好的 XNB 本身**：

```
把 gv2/Content/Maps 连子目录一起解开（904 个 xnb，含 Mines/）
  → 445 张真地图(.tbin) + 459 张图片(.png)
用 tools/check-map-tilesheet-assets.py 对这 445 张查：
  图集引用 1792 处；**找不到图片的 0 种**（原版基准也是 0）
```

⇒ **要装进游戏的那份产物本身**，每一张地图引用的每一个图集都找得到。

## 净结论

**「产出能被游戏真正读出来」这一层，第一次有了正面证据**：修完之后，
安装里 376 张真地图**全部**能被建出来（改前有 2 张不行）。
这条比之前的"回包成功 / 回环无损"强 —— 那两个只是**必要不充分**证据。

---

## 补记（2026-10-10）：探针**会真的走一遍地图**，日志里的两条新证据

翻 `SMAPI-latest.txt` 时发现探针不只是"把地图对象建起来" —— 它**创建存档并到处走**：

```
TRACE SMAPI] Context: after save creation, starting spring 1 Y1.
TRACE game] Warping to Farm / Farm / BusStop / Town / Beach
TRACE game] Warping to Custom_BlueMoonVineyard     ← 自定义地图，而且正是截图那张
```

**6 次传送，没有一条地图相关的报错** ⇒ 证据比"地图对象建得起来"更硬一层：
**游戏真的走进了自定义地图。**

唯一一条 `ERROR game`：

```
Failed loading texture Cornucopia.ArtisanMachines/Objects for item (O)Cornucopia_SeaSalt:
asset doesn't exist.
```

**未确证，且看起来不是"产出缺东西"**，三条理由：

1. 那个资产**在安装里存在**：`Content/Cornucopia.ArtisanMachines/Objects.xnb`
   （mtime 23:45:38，早于报错时间 23:57:28），解包出来是正常的 15,690 字节 PNG；
2. **同一张纹理还被另外 67 个物品引用**（`Cornucopia_Applesauce`/`Butter`/… 都在用），
   **只有 `SeaSalt` 这一个报错**；
3. 它是**单次**的（整份日志里就这一条）。

⇒ 记在这里待观察：如果以后能稳定复现，再按"瞬时/次序问题"还是"真缺"去查。
**不要**因为这一条就去改产出。
