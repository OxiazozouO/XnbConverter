# 怎么从 Mods 目录做出一个能装的 XNB 产出

这份是**操作流程**（含一路踩过的坑）。判据与结论看 `research/`、验证记录看 `verification/`。

---

## 0. 前提

- 原始资源树：`D:\sdv_tree`（解包过的游戏 `Content`，**读取用，别改**）
- 打包器：`XnbConverter.Cli.exe`（本项目不做 XNB 编码，只产树）
- 内容包：`D:\Steam\steamapps\common\Stardew Valley\Mods`

## 1. 把内容包**依次**转进同一棵树

```bash
python tools/chain-packs.py D:\tmp\chain <包名1> <包名2> ...
```

- **必须链式**：语料普查是"一包一棵树"，那样跨包的目标必然找不到（B 包改 A 包的资产）。
  真实用法就是把所有包装在同一棵树上，和 CP 在同一个内容树上应用它们一致。
- `convert-mods.py` 用**同一棵产出树**（`<out>/tree`），按 `Dependencies` 定序，
  **两趟**：第 1 趟所有包只装 `Load`，第 2 趟再应用 `Edit*`（复刻 CP 的加载语义）。
  每个包目录只放报告：`report.json`（两趟合并）、`report.loads.json`、`report.edits.json`。
- 老工具 `chain-packs.py` 仍是一包一棵树、单趟；用的是**编译好的 Release exe**，
  改了代码要 `dotnet build -c Release src/SdvCpTool.Cli`。
- **要走全量（含跨包依赖）就用 `convert-mods.py`**：它按 `Dependencies` 自动定序、共用一棵树，
  而且**认识非 CP 的 FTM 包**（`ContentPackFor = Esca.FarmTypeManager`）—— 本机是 **CP 32 + FTM 4 = 36 包**。
- ★ **产出根会写一份 `_not-converted.txt`**（`convert-mods.py` 每轮刷新）：转进来哪几个内容包、
  本机有多少**代码模组**（DLL，纯 XNB 场景用不上）、有多少**别的框架的内容包**（没转、附 `ContentPackFor`）。
  这份账原来只在 stdout（会滚走），现在落成文件。
- ⚠ **"哪些内容拿不到"要看报告里的 `notes` 段**（2026-10-11 起）：FTM 的刷怪 246 个 area 落不了、
  哪个包没有 `config.json` 之类都在那儿；原先只打在终端上，回头只看报告就会漏。

## 2. 先在树上自检，别急着打包

```bash
python tools/verify-tree.py <产出树> D:\sdv_tree
```

十行检查（warp 目标 / 地点 MapPath / 地图图集图片 / 图块 Warp / 物品精灵下标 / 季节变体 /
语料 tmx 图集 / 地图 Message 文本 / `LocalizedText` 引用 / **数据资产里的贴图引用**）。
**必须带基准树** —— 多数检查的绝对数字本身说明不了问题（原版自己就有查不到的）。
退出码 0 = 没有"产出新增"的问题。

本周基线（2026-10-11，全语料 36 包那棵树）：**七行 0**，另三行是**已知且非我们**
（语料 tmx 图集 **4** / 地图 Message **2** / `LocalizedText` **1**），
外加新加的贴图引用那行 **1** —— 那一条已经查明是 **SVE 自己写错的死引用**（见 §6 末与验证记录）。

## 3. 打包

```bash
python tools/stage-packer.py                  # 先备好打包器（见下）
python tools/pack-tree.py <产出树>            # 产出默认落在 <产出树>_xnb
```

这个脚本把三件容易踩的事包进去了：**输出目录必须先存在**、**`cwd` 必须是打包器自己的目录**
（它按相对路径 `.config\custom dll\` 找模型程序集）、以及把 `[ERROR]` 行与「成功/失败」汇总读出来。
（它是"打整棵树"；只想验「这一批新写出来的文件打不打得了」用 `pack-check.py`，快得多。）
打包后会按内容哈希把相同产出文件改成**硬链接**（默认开启，`--no-dedupe` 关闭）：
重复的图片/JSON 仍保留各自资产名，只省磁盘；跨文件系统或链接失败时自动退回原样。

### 3.1 那「失败 N 条」是什么（2026-10-13 逐条核过）

最近一次整树回包（`prof96` 那棵树）：**成功 15428 / 失败 19**。19 条分三类：

| 类 | 条数 | 是什么 | 处置 |
|---|---|---|---|
| 一个坏值 ⇒ **整份资产**连语言变体一起报废 | 13 | `Data/Characters` 9（枚举字段上写了布尔）+ `Data/Weapons` 4（整数字段上写了 `0.5`）| ✅ **已修**：写入时**删掉那个字段并报出来**（见 [`verification/2026-10-13-unconvertible-values-are-dropped.md`](verification/2026-10-13-unconvertible-values-are-dropped.md)）⇒ 现在这 13 条消掉 |
| `RSV/*` 自定义资产 | 4 | RSV 的 DLL 自己读的 JSON（形状不是游戏模型）| **不用管**：DLL 从**模组目录**读它，不看 Content；纯 XNB 装机里没有那个 DLL ⇒ 谁都不读 |
| `Mods/<命名空间>/*` | 2 | 同上（DLL 命名空间的资产）| 同上 |

⇒ **实测（2026-10-13 重跑整树回包）：成功 15441 / 失败 6** —— 与"13 条已修、剩 6 条"的推算一致
（剩下 6 条的输入逐字节未变）。
⚠ 覆盖式装机下打不出的资产**退回原版** —— 所以这 13 条当时的实际后果是
`Data/Characters` 里 **221 个 mod 新增的 NPC 定义全体消失**（不崩不报，只有实机数 NPC 才看得出来）。

> ### ⚠ 打包器得用**配齐的那一份**（2026-10-11）
>
> `out_publish\XnbConverter.Cli.exe` 是 2026-10-05 的发布版，**早于**「波形库直接吃 `.ogg`」那几次提交。
> 拿它打整树**会把音频整段丢掉**：注入进去的 `.ogg` 报
> `无效的 WAV 格式：OggS，应为 RIFF`，`Wave Bank.xwb` **根本不生成**、
> `Sound Bank.xsb` 还是原版那 32062 字节（我们的 cue 全没了），
> **而汇总行仍然写着「成功 6488 失败 0」**。⇒ 之前记的「整树 6488/0」是**旧打包器的数**
> （`verification/`、`grand-total.md` 里的同一批数字都受这个影响）。
>
> 反过来，`bin\Release\net8.0` 那份**也不能直接用**：它是 framework-dependent 的，缺两样**运行期**的东西
> —— ① `xna-shim\MonoGame.Framework.dll`（Cli 的 csproj 有一条 `DropXnaShimFromOutputRoot` 把落到输出
> 根目录的替身程序集删掉，却没往 `xna-shim\` 子目录补，只有 Core 自己的输出里有）；
> ② `.config\custom dll\` 里三个数据 dll（打包器按 **cwd** 找这个目录，而 bin 下那份是它启动时自建的**空**目录）。
> 缺这两样时的表现**极具误导性**：它不报「缺 dll」，而是每一个 `.config` 都失败成
> `无效的JSON文件格式: The type initializer for 'XnbConverter.Readers.TypeReadHelper' threw an exception.`
> —— 实测整树 **成功 0 / 失败 6488**。
>
> ```bash
> python tools/stage-packer.py      # → D:\tmp\packer（顺带 dotnet build -c Release）
> ```
>
> 它是两个回包脚本 `--packer` 的默认值。已验：拿它打一小撮真资产（对象型 `Data/Locations` +
> TBin 地图 `Maps/JojaMart` + 贴图 `Characters/Marlon` + 整个 `XACT/`）**成功 3 / 失败 0**，
> 产出里 `Wave Bank.xwb` 在、`Sound Bank.xsb` 是 43010 字节（> 原版 32062，我们的 cue 进去了）。
> ⚠ 构建那步**必须从仓库外**跑 `dotnet build`（`global.json` 钉的 SDK 8.0.0 本机没装），脚本里已经这么做了。
>
> ### 整树实测（2026-10-11，重建的打包器，全语料 36 包那棵树）
>
> ```
> 成功: 6488  失败: 0        ← 一条 [ERROR] 都没有（旧打包器那次至少有一条 OggS）
> 产出：6492 个文件            ← 旧打包器是 6491，差的那 1 个就是 Wave Bank.xwb
> XACT/  FarmerSounds.xgs 1404 / Sound Bank.xsb 43010 / Wave Bank(1.4).xwb 37MB / Wave Bank.xwb 1.1GB
> ```
>
> - **输入 13506 个文件 → 产出 6492 个文件**，其中 **6488 个 `.xnb`** + `XACT/` 那 4 个按原样落的
>   （`.xgs`/`.xsb`/两个 `.xwb`）—— 树里 **6490 个 `.config` 一个不落**都对上了
>   （差的 2 个是波形库那两个**容器**，见 [验证记录](verification/2026-10-11-full-tree-pack-with-rebuilt-packer.md)）；
>   即**没有"以为打进去了、其实没打"的文件**；
> - ~~**耗时约 48 分钟**，大头是波形库~~ ⚠ **订正（2026-10-14，实测）**：**69 分 14 秒**
>   （成功 15441 / 失败 6、产出 1.6 GB），但**大头不是波形库** —— `XACT/`（1.36 GB 源 / 641 条）
>   单独打只花 **26 秒**（纯拷贝，清单 `Length` 之和 ÷ 磁盘大小 = **1.00**，635/641 条是 MS-ADPCM，
>   早已换掉 ogg 解 PCM 那条路）。**99% 的时间在其余 15435 个资产的逐资产开销**上：
>   实测占 **11–12 核 / 15 并发**、约 **0.5 核·秒/资产**，且**与载荷大小无关**
>   （1.3 MB 与 31.9 MB 两类各 200 个都是 30–33 秒）⇒ 是**每文件固定开销**，
>   见 [`verification/2026-10-14-pack-cost-is-per-asset.md`](verification/2026-10-14-pack-cost-is-per-asset.md)。
> - ⚠ **装下来 Content 会从 544 MB 涨到 1.2 GB**：波形库存**未压缩 PCM**（原版也是），
>   57.9 MB 的 `.ogg` 解出来就是 ~635 MB（原版 `Wave Bank.xwb` 460 MB → 我们 1 046 MB）。
>   **根因是"压缩格式没跟着走"**：树里现有的 437 个 `.wav` 里 **431 个是 MS-ADPCM**
>   （`Wave Bank(1.4)` 更是 26/26），而注入的 ogg 被解成 16 位 PCM
>   （`WaveBankWriter.DecodeOggEntries`）⇒ 按 ADPCM 存这 635 MB 只要 ~160 MB，整份产出能回到 ~700 MB。
>   编码器已有、引擎侧证据也足（原版内容主体就是 ADPCM），卡在**条目 `Format` 的 ADPCM 编码没实现/没逐位核过**，
>   而没实机听过 —— 写错的后果是音乐变噪音，比体积大糟得多 ⇒ 见
>   [`research/2026-10-11-injected-music-is-pcm-vanilla-is-adpcm.md`](research/2026-10-11-injected-music-is-pcm-vanilla-is-adpcm.md)。
> - ⚠ **注入的上限约 2 GB**（**未确证**真到那儿会怎样）：波形库清单
>   （`XACT/Wave Bank.xwb.config`）里 `Entries[].Offset` / `Length` 与 `DataOffset` 都是 **32 位有符号**
>   （`XnbConverter.Core/Xact/WaveBank/Entity/WaveBankManifest.cs:24/31/50`），
>   本语料 1.096 GB、还剩约 1 GB 余量；再多注入的音乐就有把清单写不下的风险。
>   要突破得先把这个 sidecar 的字段加宽（属于 XnbConverter 侧）。
> - `Sound Bank.xsb` **43010 字节**（原版 32062）⇒ 注入的 cue 确实进了;
> - ⚠ 旧记录里「整树 6488/0」与「整树 6472/16」**都是旧打包器的数**：前者数字一样但**少了 `Wave Bank.xwb`**、
>   `Sound Bank.xsb` 还是原版那份（等于**音频功能整体丢失**却报成功）；后者见上面那条订正（复现不了）。
> - 同一棵树的**定点往返**（解回来比字节）也做了：`.png` 与 `.config` 字节相同，
>   `.json` 只差"缺省字段物化 + `ItemID`→`ItemId`"，`.tbin` 只差 4 处 `.png` 被剥（-16 字节）。
>   完整记录见 [`verification/2026-10-11-full-tree-pack-with-rebuilt-packer.md`](verification/2026-10-11-full-tree-pack-with-rebuilt-packer.md)。

> ⚠ **订正（2026-10-11）**：本节原先写「必须把产出树拷到**纯 ASCII 路径**再打」。
> 今天实测**复现不了**：`D:	mp\中文树测试` 与 `D:	mp\树[CP]`（带方括号）两条路径都
> **打成 1/0**。⇒ 那条要求大概率是当年**传参方式**的产物（路径从脚本里按 GBK 打出来），
> 不是打包器本身限制。**未确证**：真正的触发条件没找到，只是两个反例。

- ⚠ **输出目录必须先存在**，而且**不能是 `<输入树>` 内部**；实测：
  - 只建父目录、让工具自己建 `<out>` → 正常；
  - **预先建好 `<out>` 本身** → 工具不拷树，只写 `ContentHashes.json`（踩过）；
  - 输出目录不存在 → 报"输出目录不存在"直接退出码 2。
- 整树 **69 分 14 秒**（2026-10-14 实测：31593 个源文件 / 1.9 GB → 15445 个产出 / 1.6 GB），
  **别设太短的超时**。⚠ 旧记录写的「28 分钟」「48 分钟」是**更小语料 + 旧打包器**的数；
  时间构成见上面 §3 那条订正 —— **不是波形库**，是逐资产固定开销。
- 全量实测：**成功 6472 / 失败 16** —— ⚠ **那 16 个失败今天复现不了**，
  见下面的订正。

> ### ⚠ 订正（2026-10-10 晚）：那 16 个失败**复现不了**
>
> 用**同一份打包器**重跑：`Data/{Buildings,FarmAnimals,Fences,Machines,Pets}`
> 单独打 **5/0**、原版 `Data` 整份 **851/0**、产出树 `Data` 整份（含语言变体）**982/0**、
> 整树定点回包（6461 个改过/新增的文件）**3486/0**。
> 那 5 份 `.config` 的 md5 与当时的记录一致。
>
> **⇒ "打不出 XNB ⇒ 必须覆盖式安装"这条依据不成立了。** 但：
> - ⚠ **整树（13k 文件那次）没有重跑**，上面 6472/16 这个数**未确证**还成不成立；
> - ⚠ **"替换式安装"没有重新实机验证过** —— 下面第 4 节那条要求是在
>   "有 16 个资产打不出来"的前提下写的，前提没了，但**实机结论没重测**（标未确证）。
> - ★ **一个真实前提**：打包器要在**它自己的目录**下跑（它按相对路径
>   `.\.config\custom dll\` 找模型程序集）。`tools/pack-check.py` 已经这么做了。
>   ⚠ 订正（同日）：光"在自己目录下跑"不够 —— **重建的那份 `bin\Release\net8.0` 还缺
>   `xna-shim\` 与 `.config\custom dll\` 里的数据 dll**，缺了会以「每个 `.config` 都报
>   JSON 格式不对」的样子全灭。用 `tools/stage-packer.py` 备好，见 §3。
>
> 证据：`research/2026-10-09-five-data-assets-cannot-be-packed.md`（顶部订正）。

## 4. 装机：**覆盖式**（这是既有实测的做法；"替换式行不行"未重新验证）

```bash
cp -r <原版 Content> <游戏副本>/Content      # 先铺原版
cp -r <我们的产出>/.  <游戏副本>/Content/     # 再覆盖
python tools/gen-content-hashes.py <游戏副本>/Content    # ★ 最后一步：按**实际文件**重建资产清单
```

### ★ 那最后一步**不能省**（2026-10-12 实机崩了两次才知道）

游戏判断「这个资产存在吗」**不看文件系统**，而是查 `<Content>/ContentHashes.json` 的**键**
（`LocalizedContentManager.DoesAssetExist`：`_manifest.Contains(名 + ".xnb")`；
`PlatformEnsureManifestInitialized` 读的就是这个文件）。而它是个**没有 `.config` 边车的裸 `.json`**
⇒ 打包器**静默跳过**（见 `research/2026-10-09-packer-silent-skips.md`）⇒ 覆盖之后副本里留的是**原版那份**
（3,560 键）⇒ 我们新加的资产**全都不在清单里**：

| 清单与实际不符 | 症状 |
|---|---|
| 键**缺**（我们新加的资产没进清单） | `ContentLoadException: Could not load <资产> asset!` —— 实测**建号菜单一列自定义农场就整游戏崩**（`Data/AdditionalFarms` 的 `IconTexture: Mods/DaisyNiko.LavenderMeadows/icon`） |
| 键**多**（列了盘上没有的文件） | `FileNotFoundException: Could not find file …xnb` —— 打包器跳过的那些资产还留在产出树的清单里 |

所以清单必须**从最终 Content 目录扫出来**（`tools/gen-content-hashes.py`：每个 `*.xnb` 一条，
值 = 文件字节的**大写 MD5**；拿原版 3,560 条对过，抽 300 条 **300/300** 相符），
**不能**用转换阶段在产出树上生成的那份（产出树 ≠ 打包产物 ≠ 最终安装）。

⚠ 第 3 节那条订正说过：当初要求覆盖式的理由是「有 16 个资产打不出来，只放我们的产出会
`Failed loading asset 'Data\Buildings'` 而**启动就崩**」，而那个理由今天不成立了 ——
但**替换式安装没有重新实机验证过**，所以这里照旧给覆盖式（它总是对的）。

订正（2026-10-11）：静态层面又多了两条支持"替换式也没缺口"的证据 ——
① 产出树里 **6490 个 `.config` 全部有对应的 `.xnb`**（差的 2 个是波形库容器，按原样落盘），
**没有静默跳过**；② 原版那 3552 个资产在产出树里**一个不缺**（产出是超集）。
⚠ 但**实机还是没验证过**（见 §5），所以结论维持"覆盖式"，只是理由更清楚了。
证据：[`verification/2026-10-11-full-tree-pack-with-rebuilt-packer.md`](verification/2026-10-11-full-tree-pack-with-rebuilt-packer.md)。

## 5. 实机验证

★ **首选是自检 mod `tools/GameSmoke/`**（2026-10-12 起）：进游戏**什么都不用做**，它自己
新建存档 → 跳过开场 → 起床 → 巡 38 个地点 → 播 6 条注入的音乐 → 跨 3 天 → **逐张地图探针**
（2495 张，真建 `GameLocation` 碰 `.Map`），结果边跑边写 `Mods/GameSmoke/smoke-result.txt`。
两个可选清单（放在 mod 目录里）：

| 文件 | 作用 |
|---|---|
| `probe-only.txt` | 只探这几个资产（每行一个资产名）—— 单图迭代用，一分钟一轮 |
| `season-sheets.txt` | 四季图集族（每行一个**完整资产名**，含季前缀，如 `Maps/spring_Spring_z_extras`）：用游戏自己的 `GetSeasonalTilesheetName` + `DoesAssetExist` 核四季四份都在不在 |

⚠ 验收口径看**总结行**：`地图探针 PASS/贴图跳过/**FAIL**`（要 0）、`四季图集族缺`（要 0）。
⚠ 装机目录里**别留别的 mod**（尤其 CP）：CP 会在运行时重放补丁，测的就不是我们的产出了。

老探针（`AssetProbe`，**源码就在 [`../tools/AssetProbe/`](../tools/AssetProbe/README.md)**）：
对每一个地图资产**真的建一个 `GameLocation` 并碰它的 `.Map`** —— 走游戏自己的加载路径
（`xTileContent`）、顺带解析图集（图集缺一张就在这里炸）。

```bash
dotnet build -c Release tools/AssetProbe
cp tools/AssetProbe/bin/Release/net6.0/AssetProbe.dll tools/AssetProbe/manifest.json "<游戏>\Mods\AssetProbe\"
# 进游戏 → **读一个存档** → SMAPI 控制台：probe_maps
```

- **必须先进存档**：主菜单下内容管理器是空转的（老探针的教训：主菜单**不加载声库**，
  "启动零报错"证明不了任何事）；没进存档时探针会先警告一句；
- **判读口径**：只看汇总那一行的 `FAIL` 数 ——
  `—— 探针结果：PASS 376 / 贴图跳过 69 / **FAIL 0**（共 445 个）`。
  `Maps/` 底下的**图集图片不算失败**（探针按 `Map` 试不成就按 `Texture2D` 试，记成"跳过"；
  老探针不区分，得靠"数 `.tbin` 与 `.png` 对照"来判读，现在这一步在探针里做掉了）；
- ⚠ **这份探针没有在游戏里跑过**（本机跑不了：游戏那份 MonoGame 是 x86）——
  能证明的是"能编译、逻辑对得上"，不能证明"跑起来不报错"。

> ⚠ **订正（2026-10-11）**：本节原先写「探针会创建存档并真的走几站地图 / 读 `maps.txt`」——
> 那个 `local.SdvCpTool.AssetProbe` **仓库里和全盘都找不到**（本节等于空指）。
> 现在是重建版：改成 SMAPI 控制台命令 `probe_maps`（不用建存档），
> 清单文件仍然可选（每行一个资产名），不给就自己扫 `Content\Maps\**\*.xnb`。

---

### 5.1 日志要**按类别过一遍**（自检 mod 看不到的东西都在这里）

```bash
python tools/triage-game-log.py          # 读 %APPDATA%\StardewValley\ErrorLogs\SMAPI-latest.txt
```

2026-10-13 就是靠这一步逮到两个真 bug：FTM 的 `Days` 区间被写成非法 GSQ（121 行、
那些觅食条目的条件整条失效）、`{{Query:}}` 只代了内层（10 行、整串传送被丢弃）。

**下面这些类别是已知且非我们的**（2026-10-13 那轮的实测计数）—— 以后每一类都应当落在这张表里，
**表外**的才要查：

| 类别 | 计数 | 是什么 |
|---|---|---|
| `Trigger action '…' unknown action/trigger 'Spiderbuttons.BETAS_*' / 'Pathoschild.ContentPatcher_*' / 'spacechase0.SpaceCore_*'` | 153 + 27 | **包自己**写的 `Data/TriggerActions` 在调**别的框架**的动作/触发器 ⇒ 纯 XNB 里没有那些框架（同"别的框架"那一档）|
| `Trigger action '…' has a duplicate ID` | 5 | 包自己写了两遍（字段拼写一处 `"Id"`、一处 `"ID"` —— 游戏大小写不敏感、看成两条）⇒ 游戏保留第一条；CP 同样 |
| `Failed parsing condition 'CAULDRON_CONFIG_VALUE …'` | 1 | 包**自带的自定义 GSQ**（由某个框架提供），原样写在包文件里被拷进 `Data/*` |
| `Couldn't create the '…' location` | 4 | 三个是 DLL 定义的地点类（报告里 `locationClass` 一节现在就报），一个是上游缺 `spring_silo` |
| `Probe (x, y) has unknown warp property '…'` | 1 | 原版**兼容告警**（包自带的自定义 warp 属性，游戏按 legacy 解析）|
| `Galaxy auth failure` | 1 | Steam 离线登录失败，与本项目无关 |

⚠ **别只看总数变没变**：这一轮里 `Failed parsing condition` 从 121 掉到 0、又冒出 1 条
（包自带的自定义 GSQ，那轮游戏恰好加载了那份资产）—— 每一条都要能**说清出处**。

## 6. 装机后要跟玩家说清楚的**能感知到的**损失

（工程口径的完整账在 [`research/2026-10-10-known-losses.md`](research/2026-10-10-known-losses.md) 与
[`research/2026-10-11-remaining-buckets-why-they-have-no-landing.md`](research/2026-10-11-remaining-buckets-why-they-have-no-landing.md)；
这里只列**进了游戏能察觉**的那几条 —— 纯 XNB 产出没有 SMAPI，条件与运行时行为**没有地方求值**。）

1. ★ **SVE 那些自定义地图里不会有怪** —— FTM 的刷怪 246 个 area 全落不了
   （原版室外刷怪写死在 `Farm.cs`、只作用于农场）。**这一条最容易被玩家当成 bug。**
2. **按条件变的地图结构改不了**（全语料 `EditMap` 里 1013 条带运行时条件）：事件/好感/天气/时间
   触发才出现的路、桥、入口一般不出现；`Season` 只按**春天那一季**求值（地图没有按季通道，
   游戏只有一套 tbin —— 四季并进来会互相打架）；原版地图里唯一的条件槽是 `ConditionalDoor`
   （只对**室内门**生效，且是全游戏唯一一个）。
   ⚠ **量过一遍（2026-10-13）**：这 1013 条对**原版地图的传送点造成 0 条可测损失**
   （`tools/lost-warps.py`：原版 259 张图里 33 张"少 warp"全是**包自带整张 `Load` 替换**、
   游戏里 CP 也一样；真缺口 0）；丢的主要是 **mod 自己地图上**的门/路/装饰 与牌子文字。
   取舍见 [`research/2026-10-13-runtime-conditioned-map-edits.md`](research/2026-10-13-runtime-conditioned-map-edits.md)。
3. **按条件换图**（194 条）：立绘/图集按季节或事件换的，只会留**一张**（能落地的那些已经落成四季文件名）。
4. **节日与日程的按条件替换**（节日 202 + 日程 332）：`Data/Festivals`、`Characters/schedules` 的键里
   只编码了「季节/几号/星期/年份循环」这几个维度，条件超出这些维度的替换不写。
   ⇒ 表现是**某个节日/某个 NPC 的某天仍是原版内容**。
5. **一次性刷出来的东西改成邮寄**（FTM 的 22 个 area → 84 封邮件）：**投递方式变了** ——
   原来是在地图上某个坐标出现，现在会以信件形式送到（收件时把物品一起给出）。
6. **音频**：SVE 的 `Data/AudioChanges` 已注入 XACT，但 ⚠ **"进游戏真的会循环"没实机听过**
   （见 `verification/2026-10-10-xact-looped-cues.md`）。
7. **别的框架注册表读不了**（SpaceCore / CustomBush / UnlockableBundles 那 5 个）：那些内容由框架的
   C# 消费，装了纯 XNB 产出就没有框架，**等于没有**。

⚠ 反过来：**能用报告里 `notes` 段核对的**（FTM 的宝箱/限次/认不出的写法、解析警告）都在那儿，
装机前扫一眼报告比记这份清单更靠谱。

---

## 踩过的坑（都出现过，别再踩）

1. **含中文的路径别用 `python -c` 传 argv** —— Windows 会按 ANSI 代码页解 argv，中文乱码。
   用 `runpy` 在进程内设 `sys.argv`（`tools/verify-tree.py` 就是这么做的），或写脚本文件。
2. **`os.walk` 而不是 `glob("**/*.tmx")`** —— 模组目录名带 `[CP]` 这种方括号，glob 会当字符类、
   **整包静默跳过**（实测引用数从 935 变 1220）。
3. **写正则别把反斜杠放进字符类** —— 各层转义会吃掉它；用排除式 `[^\]\s]+`。
   而且 **JSON 的反斜杠是双写的**：要**扫解析后的值**，扫原文会得到 `Strings//X` 这种假名字。
4. **tbin 里的字符串没有长度前缀** —— 抽出来会跟下一条串粘住（`IslandFarmHouseS`）。
   按形状匹配 + **从长到短试前缀**，否则原版自己就报假阳性。
5. **后台任务别用 `| tail -N`** —— 输出会被缓冲到结束才吐，看不到进度。
6. **判断产出对不对之前，先确认产出是哪次跑出来的** —— 实机目录里躺着的很可能早就过期
   （踩过：拿一棵过期树分析出"169 张自定义地图只登记 2 个地点"这种假结论）。
