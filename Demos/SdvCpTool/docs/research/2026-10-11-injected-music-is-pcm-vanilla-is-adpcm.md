# 注入的音乐是 **PCM**，而原版波形库基本都是 **MS-ADPCM** —— 这就是 1.2 GB 的来源

日期：2026-10-11

> **2026-10-12 追记：这条已经做了**（当时按「宁可少写，不可写错」记着不动）。
> 起因不是体积，是**阻断发布**：语料长到 204 条注入音乐后，PCM 的 ≈2.6 GB 直接撞上三处
> 32 位上限，`Wave Bank.xwb` 一个字节都产不出来。实现、格式依据与验收见
> [`../verification/2026-10-12-injected-music-as-adpcm.md`](../verification/2026-10-12-injected-music-as-adpcm.md)
> （整库 1.29 GB、整树回包里 `Destination array` 0 次）。**原文下面那份"为什么不动"里的
> 两处卡点，这一轮都解决了**：格式字的位布局对着 MonoGame 的 `DecodeFormat` 与 437 条原版条目核过；
> 剩下"进游戏听"那条仍然只能由人做。



整树回包后装下来的 `Content`：**544 MB → 1.2 GB**。差值几乎全在音频：

```
原版 Wave Bank.xwb   460 453 688 B
我们 Wave Bank.xwb 1 095 519 576 B      差 635 065 888 B（605 MiB）
```

## 两条各自成立的实测

**① 树里现有的 `.wav` 是 MS-ADPCM**（读 RIFF 头的 `wFormatTag`，扫 `XACT/Wave Bank/**` 全部 437 个）：

| wFormatTag | 条数 | 参数 |
|---|---|---|
| **2（MS-ADPCM）** | **431** | 2 声道 / ~44.1 kHz / blockAlign 140 / 4 bit |
| 1（PCM） | 6 | 2 声道 / 44100 / blockAlign 4 / 16 bit |

`Wave Bank(1.4)` 那份 **26/26 全是 ADPCM**。⇒ **游戏自己的波形库绝大部分就是 ADPCM**，
"引擎能不能播 ADPCM"不是一个假设 —— 它就是原版内容的主体。

**② 我们注入的 37 个 `.ogg` 被解成 16 位 PCM** ——
`XnbConverter.Core/Xact/WaveBank/Writer/WaveBankWriter.cs::DecodeOggEntries`：

```csharp
(int channels, int rate, byte[] pcm) = OggVorbis.ToPcm16(path);
entry.Length = pcm.Length;
entry.Format = (uint)(0 | (channels << 2) | (rate << 5) | (blockAlign << 23) | (1 << 31));  // 0 = PCM
```

57.9 MB 的 ogg（≈1 小时的音乐）解成 PCM 就是 ~635 MB —— **和上面那个差值对得上**。
⇒ **不是格式退化，是"把 ADPCM 那一档换成了 PCM"**。

## 量化：能省多少

MS-ADPCM 约 4:1。这 635 MB 若按 ADPCM 存，大约 **160 MB** ⇒ 整份产出从 1.2 GB 回到 **~700 MB**
（原版 544 MB + 注入的 ADPCM 音乐）。省掉的 475 MB 纯粹是"压缩格式没跟着走"。

## 为什么**记录而不动**

能做的基础都在：

- 编码器已有：`Xact/WaveBank/Codec/MsAdpcm.cs::Encode`，而且
  `Tests/XactRoundTrip/CodecCheck.cs` 已经做过「编码 → 解码 → 与原 PCM 比误差」的往返；
- 引擎侧有硬证据（见上：原版内容主体就是 ADPCM）。

卡在两处，而且都不是"再写两百行"那么简单：

1. **条目 `Format` 字段的 ADPCM 编码**。现在代码只写得出 PCM 那一种布局
   （`Entity/WaveBank.cs:89` 的 `DecodeFormat`：低 2 位 code / channels / rate / align / bit31），
   而 ADPCM 还牵涉 `AdpcmMiniWaveFormatBlockAlignConversionOffset = 22` 那个
   「mini 格式的 blockAlign 换算」（`Entity/WaveBank.cs:223`、`Reader/WaveFormReader.cs:9`）——
   这块是**读**的时候用的，**写**的方向没实现过，也没有逐位核过。
2. **没有实机听**（本机游戏那份 MonoGame 是 x86，探针跑不了）。ADPCM 条目写错的后果是
   **音乐变成噪音或者干脆不响** —— 比"体积大 475 MB"糟得多。

⇒ 按「宁可少写，不可写错」**这一轮不动**。真要做，验收标准先立好：

1. 按 XACT 的 `WAVEBANKMINIWAVEFORMAT` 逐位核对 ADPCM 条目的 `Format`（含 mini blockAlign 换算）；
2. 用 `Tests/XactRoundTrip` 把**新的**库解回来，逐条 `MsAdpcm.Decode` 与原 PCM 比 SNR；
3. **进游戏听**（这一条没法替代）。

⚠ 另一条**与体积无关**的独立上限：波形库清单 `XACT/Wave Bank.xwb.config` 的
`Entries[].Offset` / `Length` 与 `DataOffset` 都是 **32 位有符号**
（`Entity/WaveBankManifest.cs:24/31/50`）⇒ 库总长到 ~2 GB 就有写不下的风险
（本语料 1.096 GB，**未确证**真到那儿会怎样）。要长期注入更多音乐，这个 sidecar 得先加宽。
