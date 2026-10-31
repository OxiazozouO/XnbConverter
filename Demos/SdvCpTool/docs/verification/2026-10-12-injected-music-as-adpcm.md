# 注入音乐改存 **MS-ADPCM**：波形库从「产不出来」回到可发布

日期：2026-10-12
起因：[`2026-10-12-wave-bank-not-produced.md`](2026-10-12-wave-bank-not-produced.md)
（注入音乐是 PCM ⇒ 204 条 ≈2.6 GB ⇒ 撞三处 32 位上限 ⇒ `Wave Bank.xwb` 一个字节都产不出来）

> ⚠ 2026-10-25 补记：本文只核了**容器/元数据**；`MsAdpcm` 的块内布局当时是错的（编解码自洽但不符合规范），
> 以及新增 Sound 的 `VolumeFlag=0` 等于静音。见
> [`2026-10-25-adpcm-block-layout-fix.md`](2026-10-25-adpcm-block-layout-fix.md)。

## 改了什么

注入音乐不再把 `.ogg` 复制进树、让打包器解成 16 位 PCM，而是**我们自己**在转换期编成
**MS-ADPCM 的 RIFF wav**（新文件 `src/SdvCpTool.Core/Merging/AdpcmMusic.cs`），
波形清单里那条也按**原版 ADPCM 条目的形态**写全（长度、格式字、循环区）。

理由不是"体积好看"，而是**原版本来就是这么存的**：原版 `Wave Bank.xwb` 437 条里
**431 条是 MS-ADPCM**（2 声道 / ~44.1 kHz / 块对齐 140 / 4 位），只有 6 条是 PCM。
注入的那部分按 4:1 编回去 ⇒ 204 条约 650 MB，加原版 460 MB ⇒ **整库 ~1.1 GB**，
正是 2026-10-11 那次**打包器成功产出过**的尺寸（1 095 519 576 B）⇒ 链路本身是通的。

## 格式事实：全部对着**权威实现**核过，不是推的

| 事实 | 依据 |
|---|---|
| `Format` 字位布局：`wFormatTag` 0-1、`nChannels` 2-4、`nSamplesPerSec` 5-22、`wBlockAlign` 23-30、`wBitsPerSample` 31 | MonoGame `Audio/Xact/WaveBank.cs::DecodeFormat`（连位图注释都在），与 `XnbConverter` 的 `WaveBank.DecodeFormat` 一致 |
| 字段里的 `wBlockAlign` **不是字节数**，真块对齐 = `(字段 + 22) × 声道数` | MonoGame `Platform/Native/SoundEffect.Native.cs::PlatformInitializeXact`：`if (codec == Adpcm) blockAlignment = (blockAlignment + 22) * channels;`；原版 wav 的 `fmt` 正好是 `140 = (48 + 22) × 2` |
| 条目元数据 24 字节的顺序：`[+0]` 标志与时长、`[+4]` Format、`[+8]` 数据区相对偏移、`[+12]` 字节数、`[+16]` 循环起点、`[+20]` 循环长度 | MonoGame 同文件按这个顺序读（`EntryMetaDataElementSize` 逐段判）；打包器 `WaveBankWriter.GrowHeader` 写的也是这个顺序 ⇒ 我们追加的条目偏移/长度**一直是对的** |
| 循环两个字段的单位是**采样数**，且会真的被用 | MonoGame 把它们传进 `new SoundEffect(codec, audiodata, channels, rate, alignment, info.LoopStart, info.LoopLength)` |
| `[+0] = 采样数 × 16`（高 4 位恒 0）；`[+5] = 采样数`（整段循环） | 拿**游戏那份** `Wave Bank.xwb`（`D:\Stardew.Valley.v1.6.15\...\Content\XACT\Wave Bank.xwb`）**原始字节**逐条对：437 条的 `[+1] == 清单的 Format`、`[+3] == 清单的 Length` **全中**；`[+0] == 采样数×16` **437/437 全中**；`[+4] == 0` **437/437**；`[+5] == 采样数` **431/431**（ADPCM 那批） |

采样数怎么来的也对应上了：`每块采样数 = (块对齐 − 7·声道数)×2/声道数 + 2`，
`块对齐 = 70 × 声道数`（字段写 48）⇒ **每块 128 个采样，与声道数无关**，
原版 wav 的 `fmt` 里写的也是 `samplesPerBlock = 128` ✓。

我们的 wav 也照原版那条抄：`fmt `(50) + `fact`(4) + `data`(载荷)，`data` 的字节数**与清单里那条的长度相等**
⇒ 打包器 `Payload` 走 `LoadWaveForm().dataChunk.Data` 且长度相符 ⇒ **不触发重编码**，
把这串字节原样搬进库（与它搬原版那些 ADPCM 条目走同一条路）。

## 顺手补的一道闸：撞名改名

原版音乐波形就放在 `XACT/Wave Bank/Music/*.wav`（97 个）。以前我们写 `.ogg`，与原版的 `.wav`
天然错开；改成写 `.wav` 之后，**同名就会盖掉原版那份字节**，而原版条目记的长度/格式还指着老内容
⇒ 打包器看到长度不符会走重编码那条路（可能整份库打不出包）。
所以 `AudioChangesMerger.FreeName`：目标路径已被占（用 `OutputTree.Exists`，它认得未落盘的暂存）
就写 `X_2.wav`。

## 验收（对应阻断文档里先立好的四条）

| 条 | 做法 | 结果 |
|---|---|---|
| ① `Format` 逐位 | 单测钉住原版最常见那个字 `0x1815888A`，并按位拆开复核；437 条原版条目对过 | ✅ |
| ② 往返 SNR | 单测：合成立体声正弦 → 编码 → 解码 → 与原 PCM 比（阈值 15 dB，实测 ~27 dB） | ✅ |
| ③ 整库回解 | 整树回包（`tools/pack-tree.py`）→ 从产出库里按条目偏移取回载荷，与我们树里 wav 的 `data` 块逐条比字节 | ✅ **204 条 / 886.5 MB 全等，不一致 0**（打包器没重编码） |
| ④ **进游戏听** | **我做不到，必须人来** | ⬜ 未做 |

⚠ 在 ④ 做之前，①②③ 只说明**结构对**，说明不了"好听/能播"。ADPCM 条目写错的后果是
**音乐变噪音或者干脆不响** —— 比体积问题糟得多。

### 整树回包（真正的发布闸门）

`python tools/pack-tree.py D:\tmp\prof79\tree --out D:\tmp\packed79`

| | 改前（PCM） | 改后（ADPCM） |
|---|---|---|
| `XACT/Wave Bank.xwb` | **没有**（`Destination array` 1 次） | **1 390 030 804 B（1.29 GB）** ✅ |
| `Destination array` | 1 次 | **0 次** |
| 汇总 | 成功 15374 / 失败 25 | 成功 15375 / 失败 25 |

- 失败那 25 条与音频无关，是那批**已知**的（`Data/Objects`/`Weapons` 里小数写进整数字段、
  `Data/Characters` 的 `Antiunn.EndSlideShow` 是布尔当枚举、`Mods/**` 两份缺 `.png`）——
  与 `prof74` 那次同一批，**没有新增**。
- **产出的库尺寸与记账逐字节吻合**：按打包器 `GrowHeader` 的算法（新元数据表起点 15532、
  4 字节对齐填充 10 B、旧数据区 460 443 052 B 加在末尾）推出来正好 **1 390 030 804 B，差 0**。
- 库里那 **204 条追加条目的元数据**（格式字/长度/循环长度/标志与时长）与我们的清单**完全一致** ✓。

## 本轮数字

| 项 | 数 |
|---|---|
| 追加波形条目 | **204 条**（与改动前同数：188 立体声 + 16 单声道） |
| 追加载荷合计 | **886.5 MB**（改动前按 PCM 算 ≈2.6 GB） |
| **整库预计**（原版 460.4 MB + 追加） | **1.29 GB**（改动前必然 >2 GB、一个字节都产不出） |
| 树里音频文件 | `Music/` 301 个 `.wav`（97 原版 + 204 我们的）；整树 **`.ogg` 残留 0** |

**结构自检**（204 条逐个文件、逐块查，0 问题）：RIFF/WAVE 头正常；`fmt` 的
`tag = 2(ADPCM)`、块对齐 = `(48+22)×声道数`、每块 128 采样、4 位；`Format` 字用独立复算的
位布局对得上；`data` 块长度与清单里那条**完全相等**；**每个块的块头**（预测器 ≤ 6、差分 ≥ 16）
合法。

**同参数对比**（`prof77` 改前 → `prof79` 改后，232 包）:

- **报告侧：231 包逐项零差异**（补丁级：变好 0 / 变坏 0 / 新增 0 / 消失 0）⇒ 这一改**不动任何判定**；
- **树侧**：整棵 3.1 万文件的树，差异**只有音频那三样** —— 198 个 `.ogg` 换成 202 个新 `.wav`
  （见下面撞名那 4 个）、2 个「源本来就是 wav」的文件**内容被换**（`Kayyy_splash.wav`、`plane.wav`）、
  以及 `XACT/Wave Bank.xwb.config` 本身。**别的文件一个字节都没变**。

## 顺带修掉的两件事（都是这轮改动暴露出来的）

1. **撞名静默覆盖（4 处）**：旧代码把源文件按原名拷进 `Music/`，而不同包会用同一个名字
   （语料里 `CastleVillageOutpostAmbience`、`CrystalCavern`、`DuskspireLaugh`、`SDV_Fireplace`
   各撞一次）⇒ 后来的**静默盖掉**前一个：204 条只落成 200 个文件，先那条 cue 放的是**别人**的音频。
   现在 `FreeName` 改名（`X_2.wav`），204 条各占一个文件。⚠ 这类"看起来成功了"的错最危险
   （报告上什么都不显）。
2. **源文件是 wav 的那 2 条**（`Kayyy_splash.wav`、`plane.wav`，都是 16 位 PCM）：
   旧代码原样拷进树，而清单里那条 `Length = 0 / Format = 0` ⇒ 打包器只能走**重编码**那条路
   （拿 0 声道/0 采样率去重编码，多半就是它抛错的地方）。现在这两种源都**自己编**，
   别的格式（8 位/浮点/ADPCM 源）明确报错不猜。
