# 注入音乐「一卡一卡」：MS-ADPCM 块布局写错了

日期：2026-10-25

## 症状

注入的循环音乐能播、也会循环，但听感一顿一顿；原版音乐正常。

## 根因

`XnbConverter.Core/Xact/WaveBank/Codec/MsAdpcm.cs` 的块头和 nibble 顺序是**自洽但不符合 MS-ADPCM 规范**：

- 规范块头按**字段**交错：预测器（每声道 1B）→ 差分（每声道 2B）→ 采样1（每声道 2B）→ 采样2（每声道 2B）。
- 我们的实现按**声道**各写连续 7 字节。
- 规范里 4 位残差**高半字节在前**；立体声一个字节装同一帧的左右（高 = 声道 0）。
- 我们的实现低半字节在前，且左右反了。

编码器和解码器一起错，所以单测「编码→解码」始终能过；外部的解码器（ffmpeg / XAudio2）按规范读，块头立刻失真。

## 证据

对旧的 `NoraTheme.wav` 用 ffmpeg 解码：

```text
ERROR: block_predictor[1] = 33
ERROR: block_predictor[1] = 111
ERROR: block_predictor[1] = 173
...
0 decode errors 之前大量 packet 被丢弃
```

用旧解码器直接解**原版**波形库：

```text
ADPCM 预测器序号越界：247
```

即：不是「注入数据特别差」，而是编解码约定和整份原版库都不兼容。

## 修复

`MsAdpcm.Decode` / `EncodeBlock`：

- 块头读取/写入改为字段交错：预测器 `+j`、差分 `+ch+2j`、采样1 `+3ch+2j`、采样2 `+5ch+2j`。
- 残差先写/先读高半字节；立体声高半字节对应声道 0。

`Tests/XactRoundTrip/CodecCheck.cs` 的块头采样比对同步到新偏移。

## 第二个独立问题：新增 Sound 的 VolumeFlag = 0 等于静音

`AudioChangesMerger` 给新增 Sound 写的是 `VolumeFlag = 0`。XACT 这个字节不是 dB，而是 0..255 的曲线索引；
MonoGame `XactHelpers.ParseVolumeFromDecibels(0)` 解出来约 `1.6e-5`（≈ -96 dB），原版音乐用 180（≈ 1.004）。

循环音乐走「简单 Cue → 带 Clip 的 Sound」；`Cue._UpdateSoundParameters` 的最终音量是

```text
cue.Volume × category._volume × rpcVolume × sound.volume
```

`rpcVolume=1`、Music 类别约 1.004，所以 `sound.volume=0` 会把整首歌压到几乎听不见。
现已改为 180，和原版 `springtown` 等音乐一致。非循环的复杂 Cue 不经过这条 `XactSound` 记录，
所以以前只有**循环音乐**是哑的。

### 效果器链检查

用游戏自带的 MonoGame 反射加载声库/波形库，直接读播放实例（修复后）：

```text
[noraTheme]                         [springtown]
sound.volume=1.0040851               sound.volume=1.0040851
category=Music                       category=Music
useReverb=False                      useReverb=False
rpc=0                                rpc=0
Pitch=0                              Pitch=0
FilterEnabled=False                  FilterEnabled=False
LoopCount=255                        LoopCount=255
State=Playing                        State=Playing
instance.Volume=1.0123054            instance.Volume=1.0123054
```

没有 reverb send、没有滤波器、没有 RPC 曲线、没有随机音量/音高；唯一曾经作用在链上的是那个 0 音量。
Sound 记录里的 `Filter` 字段 MonoGame 读出来后直接丢弃（`XactSoundBankSound` 构造函数里 `ReadUInt16()`），
Clip 的 `FilterQAndFlags=0x5DC0` 最低位为 0 ⇒ `FilterEnabled=false`，和原版音乐相同。

### 运行时批量验证

用游戏自带的 MonoGame 逐条 `GetCue → Play → 读实例字段 → Stop`，211 条新增 cue：

- 211/211 进入 `State=Playing`，无异常。
- 157 条循环：`LoopCount=255`；8 种采样率/声道（8k/22.05k/24k/44.096k/44.1k/48k，单/双声道）
  各挑一条连播 4 秒仍在播。
- 54 条非循环：`LoopCount=0`，音量正常。
- 211/211：`useReverb=False`、`rpcCurves=0`、`Pitch=0`、`FilterEnabled=False`。
- 157 条循环最终实例音量 `1.0123054`，与原版 `springtown` 完全相同；54 条非循环 `1.0040851`。
- 211 条 cue → 变体的 `TrackIndex` 全部落在波形库内；`LoopLength` 全部等于
  `Length / 块对齐 × 128`，`FlagsAndDuration` 全部等于 `LoopLength × 16`。

### cue → 变体 → 轨道 → 打包载荷 的对齐验证

音频不走 XNB，mod 这边引用的是 **cue 名字符串**（地图 `Music`、`LocationContexts.Track`、事件 `playMusic` 等）；
XSB 内部再走 `cue → XactSound/clip 事件 → (WaveBankIndex, TrackIndex) → XWB 条目`。
逐条核对最终安装的 `Sound Bank.xsb` 和 `Wave Bank.xwb`：

- 211 条注入 cue、219 个波形变体的 `WaveBankIndex` 全为 0，`TrackIndex` 全部落在 `[0,656)`。
- 219 条 `(TrackIndex → XWB entry data)` 的 SHA-256 与产出树对应 wav 的 `data` 块**逐条相等**。
- 157 条循环 cue 在二进制声库里都能查到，`LoopCount=255`、`CategoryId` 与 JSON 意图一致、`VolumeFlag=180`。
- 运行时 211/211 能 `GetCue → Play`（见上一节）。

### 多 FilePaths：完整随机变体

CP 的 `FilePaths` 有多条时（同一个 cue 的多个随机变体），现在的处理是：

- 每个文件都编成 ADPCM、追加进 Wave Bank；
- 同一个 cue 在 `Variations` 里写多行，打包时合成**一个类型 6 事件**：
  `NumTracks = N`、每轨一个 `(TrackIndex, WaveBankIndex, weight)`、
  `MoreFlags = 2`（Random）、`LoopCount` 按 `Looped` 写 255/0；
- MonoGame `PlayWaveEvent` 每次 Play 随机挑一条；`Cue.VariantIndex` 可观测到选中下标。

MonoGame 的选择是 `Random.Next(totalWeight + 1)`，不是标准权重区间；全 1 权重会让第一个变体
拿到 `2/(N+1)`。所以写成 `[1, 2, 2, …]`（总权重 `2N−1`、结果数 `2N`），每个变体刚好分到 2 个结果。
实测 60 次：两条变体约 29/31，三条 21/13/26，不再偏向第一条。

之前缺失的 7 条已全部补进：

| cue | 变体数 | 引用位置 |
|---|---|---|
| `ZuzuCity1` | 3 | `LocationContexts`（DefaultMusic + 白天 Track） |
| `ZuzuCityNight` | 2 | `LocationContexts`（夜间 Track） |
| `ZuzuResort` | 2 | `LocationContexts.DefaultMusic` |
| `JojaHQ1` | 2 | 多张 `Custom_DTZ_Joja*` 地图 `Music` |
| `kristoffOffice` | 2 | `Custom_DTZ_JojaCEO*` 地图 `Music` / 事件 |
| `HotCave` | 2 | `Custom_DTZ_OasisCaveLava` 地图 `Music` |
| `GiEX.LairMusic` | 2 | `GiEX_ExtinctLair` 地图 `Music` / `Data/Locations.MusicDefault` |

补完后 `LocationContexts` 已无缺失 cue；`Data/Locations` 只剩 `none`（特殊值）和
SVE 自己引用了却没定义的 `…_ZuzuCity_Crowd_Ambience`。

### 已知遗留：非循环 cue 的 Category

非循环注入 cue 走「复杂 Cue + 变体类型 0」，MonoGame 直接构造 `XactSoundBankSound`，
不读我们写的 Sound 记录，所以运行时 `categoryID=0`（Global）、`volume=1`（不是我们解析的 3/Sound）。
它不产生效果器，也不会卡；只是游戏里的 Music/Sound/Ambient 音量滑条不会作用到这些音效。
要把它们也归到正确类别，需要把非循环 cue 改成带 Sound 记录的形态，这是另一项结构改动。

## 验证

- 原版 `Wave Bank.xwb`：431 条 ADPCM 全部解码成功，块头采样还原 100%，最差往返 PSNR 34.1 dB。
- 修复后重编 219 条注入波形（从模组源 ogg/wav 重编，不做二次有损转码）；ffmpeg 独立解码全部 316 条波形，0 错误。
- 重打包后波形库 **650 条 ADPCM** 全部通过；219 个变体的载荷 SHA-256 与产出树 wav 的 `data` 块逐条相等。
- 运行时 211/211 cue 能播；7 条多轨 cue 的 `VariantIndex` 分布已消除首条偏置。
- 新增回归单测：块头字段交错、高半字节声道 0、手写标准参考块逐采样解码、新增 Sound 的 `VolumeFlag=180`、多 FilePaths 生成多行 Variation。
- 完整单测：1651 通过，0 失败。

## 产物与回退

- 最终波形库：`D:\tmp\full-v11\packed-multifix2\Wave Bank.xwb`（1,483,593,724 B，SHA-256 `eec6bd39…`），已同步到产出树与游戏。
- 最终声库：`D:\tmp\full-v11\packed-multifix2\Sound Bank.xsb`（52,527 B，SHA-256 `546d671f…`），已同步到产出树与游戏。
- 修复前波形库备份：`D:\tmp\wave-bank-before-adpcm-fix.xwb`、`D:\tmp\wave-bank-before-multifix.xwb`。
- 修复前注入 wav 备份：`D:\tmp\full-v11\audio-before-codec-fix`。
- 修复前声库备份：`D:\tmp\sound-bank-before-volume-installed.xsb`、`D:\tmp\sound-bank-before-volume.xsb`、`D:\tmp\sound-bank-before-multifix.xsb`。
- 多文件注入前的两份清单：`D:\tmp\multi-variant-backup`。
