# 整树回包（新语料 232 包）：成功 15339 / 失败 29，12 个资产打不出包 —— 逐个分诊

日期：2026-10-12
输入树：`D:\tmp\prof66\tree`（新语料 232 包链式产出）；打包器：`D:\tmp\packer`（重建版）

```
成功: 15339   失败: 29   （29 = 12 个资产 + 它们的各语言 .config 变体）
打不出 .xnb 的资产：12 个
```

对照：上一轮整树回包（旧语料 36 包、13 506 个文件）是 **成功 6488 / 失败 0** ——
所以这 12 个全是**新语料带出来的**，逐个查过：

| # | 资产 | 判据（打包器的原话） | 归谁 |
|---|---|---|---|
| 1 | `ContentHashes.json` | 不是资产（我们自己的哈希清单） | **预期** |
| 2 | `Mods/jibb.minecarts/config.json` | 不是 XNB 资产 | **预期**：SMAPI 专属（DLL mod 的配置） |
| 3 | `Mods/skellady.SBVCP/SOEmojisData.json` | 同上 | **预期**：SMAPI 专属（别的 mod 的命名空间） |
| 4 | `RSV/RSVMapData.json` | JSON 里 `BloomingHill` 是对象，类型期望字符串 | **预期**：SMAPI 专属（RSV 的 DLL 注册资产） |
| 5 | `RSV/RSVWorldMapData.json` | 同上 | 同上 |
| 6 | `Data/Characters.json` | `EndSlideShow` 位置收到**布尔**，期望枚举 | **上游数据陈旧**：原版是 `"MainGroup"`（字符串枚举，实测原版 0 个布尔），而 `[CP] Lunna`、`内容拓展---热闹的下水道` 的源文件里写的是 `true/false`（1.5 时代形态）。**CP 下同样会炸**（游戏自己的反序列化也过不去） |
| 7 | `Data/Objects.json` | 同上（枚举位收到布尔） | 上游数据陈旧（同一类） |
| 8 | `Data/Weapons.json` | 同上 | 上游数据陈旧（同一类） |
| 9 | `Characters/Lunna_Shadowfolk.json` | `Could not find file '…Lunna_Shadowfolk.png'` | **我们的 bug** ⇒ 已修（见下） |
| 10 | `Portraits/Lunna_Shadowfolk.json` | 同上 | **我们的 bug** ⇒ 已修 |
| 11 | `Characters/Dianna_FallsCave.json` | 同上 | **我们的 bug** ⇒ 已修 |
| 12 | `Portraits/Dianna_FallsCave.json` | 同上 | **我们的 bug** ⇒ 已修（第 9-12 是同一个原因） |

## 我们的那一处（9-12）：`Load` 的 FromFile 是**贴图 `.xnb`**

`[CP] Lunna` 的 `assets/Data/NPCData.json`（include 文件）里：

```json
{ "Action": "Load", "Target": "Characters/Lunna_Shadowfolk",
  "FromFile": "assets/Events/Sprites/Lunna_Shadowfolk.xnb" }
```

`LoadApplier.DecodeXnb` 只认 **地图（tbin）** 与**数据（转 JSON）**两种载荷，贴图 `Texture2D`
落进了"数据"那条 ⇒ 产出是一份 `.json` + 一份**贴图 `.config`**、**图本身没有** ⇒ 打包器报
`Could not find file '…png'`。

修法：`DecodeXnb` **按载荷类型分流**（`Texture2D` ⇒ 图片资产：`<资产>.png` + 图片 `.config`，
与 `ImageApplier` 新建图片资产同口径，含"不许盖掉另一类资产"那道闸）；像素走
`ImageCache.SetPixels`（**不用** `Texture2D.SaveAsPng` 的异步队列 —— 落盘时序归 `ImageCache`）。
写之前验形状：`Data` 必须是逐像素 RGBA（`W×H×4`），不是就**失败**而不是写坏图。

单测：`FromFile是贴图XNB时落成图片资产`（夹具 `fixtures/Texture_2x2.xnb` 是打包器把一张
2×2 PNG 打出来的）—— 断言产出 `.png`（2×2、**像素逐点一致**）、不产出 `.json`、`.config`
是 `Texture2DReader`。

### 修后的定点验证（9-12）

`prof68 → prof69` 同参数全量：判定与 total/written/writeFailed **一字不差**（只改载荷形态）；
树里 `Portraits/Lunna_Shadowfolk.png`（5065 B）落地、同名 `.json` 消失。定点回包 4/0 ✓；
解包后与产出**逐像素相同**（`128×384` / `64×192` / `128×192`）。

## 第 13 项：XACT 循环 cue 重名（**已修**）

打包器还有一条不在上面 12 个里的 `[ERROR]`：
`新增的循环 Cue「PIRATE_THEME」与已有的 Cue 重名` ⇒ **整份 `Sound Bank.xsb` 打不出包**（音乐全丢）。

查下来是**我们**的注入不幂等：`AudioChangesMerger` 是**逐条补丁**被调用的，而
`EditData Data/AudioChanges` 常有多条（实测 `Crystal Island` 8 条、East Scarp 多条），
第二次进来时同一 cue 已在声库里 ⇒ 又加一遍。改后实测：

| | `Sounds` 条数 | 循环 cue 里重名的 |
|---|---|---|
| 改前（链式 `prof69`） | 685 | `PIRATE_THEME`×2、`PIRATE_THEME(muffled)`×2、`FallInEastScarp`×2、`SummerInTheScarp`×2、`WinterInEastScarp`×2、`EastScarpSpringTheme`×2… |
| 改后（单跑 East Scarp，修复版） | 470 | **0** |

（两条命令、同一份语料；`Sounds` 条数不同是"单跑 vs 链式"的差异，不是本次改动。）

## 第 2-5 项（SMAPI 专属资产）：**试过一版，已回退**

想法是：`Mods/…`、`RSV/…` 这类是 **SMAPI 专属**资产（DLL mod 自己注册的，纯 XNB 场景没人读），
我们给它们补 `.config` 之后打包器就按**我们猜的类型**去打包、然后失败 ⇒ 干脆不补。

**实测是反的**（对 `prof70` 树整树回包，与 `prof66` 逐项对照）：

| | 打不出包的资产 |
|---|---|
| `prof66`（**补** `.config`） | 12 个：`ContentHashes`、`Data/{Characters,Objects,Weapons}`、`Mods/jibb…`、`Mods/skellady…`、`RSV/RSVMapData`、`RSV/RSVWorldMapData`、**Lunna 那 4 个** |
| `prof70`（**不补**） | 11 个：Lunna 那 4 个**没了**（那是另一处修复），但**多了 3 个** —— `MEEP/FarmAnimals/SpawnData`、`UnlockableBundles/BundleThemes`、`UnlockableBundles/Bundles` |

⇒ **不补 `.config` 并不能让打包器跳过**：它照样尝试，只是没了类型信息才失败；而上面那 3 个
**带着**我们猜的 `.config` 时**恰恰打得出包**（形状正好猜对）。所以这一版是净害 ⇒ **回退**
（`d3fa804` 已 revert）。要真解决，得在**打包器侧**认 SMAPI 专属命名空间，或者拿
`--content-hashes` 判"这是不是原版资产"。

## 全量对照（prof69 → prof70，同参数 232 包）

- 报告：`total / written / writeFailed` **一字不差**（32771 / 15180 / 201），
  逐条核过**变好 0 / 变坏 0 / 判定变化 0**；
- 逐字节树：**只在 A 15 个**（就是上面那 15 份 `.config`）+ **8 个重复的
  `XACT/Wave Bank/Music/*.ogg`**（音频去重后不再重复拷），内容不同 2 个
  （`XACT/Sound Bank.xsb.json`、`Wave Bank.xwb.config` —— 去掉重复项）；
- 两棵树都**没有悬空引用**：波形清单里每条都能在树里找到文件、
  `Music/` 下每个文件都被清单引用（去重只去掉了重复，没有去成"引用不存在的文件"）。

## 附：跑批里"某包报告没写出、退出码 2"是什么

2026-10-12 有 4 次遇到过（`Seasonal Floorings`、`[CP] Do Eun's Female Body`、
`Juliet, Jessie and Lodging House`、`pigeon_mail`，每次的包不一样）。查过：

- 退出码 2 的含义是"**参数错误**"，单跑那个包复现时 CLI 说的是
  **「给定的内容包目录缺少 content.json」**；
- 再查：`D:\Steam\…\Mods\pigeon_mail` **整个目录已经不存在了**（它在上一轮的产出里还有）

⇒ 是**环境性的**：跑批期间**有别的进程在动 `Mods/` 目录**（上一轮合并语料时就见过
Vortex 的 `__folder_managed_by_vortex` 标记），包目录中途消失/改名 ⇒
CLI 拿到一个没有 `content.json` 的路径 ⇒ 退出码 2、报告写不出。
**不是我们的 bug**，但会让"同参数两轮对比"出现 `只在 A：N` 的假差异 ⇒
对比时先确认 Mods 目录没被动过（runner 现在会把**报告没写出**和子进程的 stderr 打出来）。
