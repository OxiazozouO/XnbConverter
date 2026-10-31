using XnbConverter.Audio;
using XnbConverter.Readers;
using XnbConverter.Xact.WaveBank.Codec;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 把注入的音乐编成**原版那一档格式**：MS-ADPCM 的 RIFF wav + 波形条目的元数据。
///
/// <para>
/// <b>为什么非做不可</b>：打包器的 ogg 路把注入音乐解成 **16 位 PCM**（`Format` 写死 PCM 那一档），
/// 204 条 ≈2.6 GB ⇒ 撞上三处 32 位上限，`Wave Bank.xwb` **根本产不出来**（见
/// `docs/verification/2026-10-12-wave-bank-not-produced.md`）。原版波形库 437 条里 **431 条本来就是
/// MS-ADPCM**（约 4:1）⇒ 编成 ADPCM 后整库回到 ~1.1 GB，正是 2026-10-11 那次打包器**成功产出过**的尺寸。
/// </para>
/// <para>
/// <b>格式细节都是照权威实现核过的</b>（不是推的）：
/// </para>
/// <list type="bullet">
/// <item><see cref="MiniFormat"/> 的位布局 = MonoGame `Audio/Xact/WaveBank.cs::DecodeFormat`
/// （`wFormatTag` 0-1 / `nChannels` 2-4 / `nSamplesPerSec` 5-22 / `wBlockAlign` 23-30 /
/// `wBitsPerSample` 31），也与 `XnbConverter` 的 `WaveBank.DecodeFormat` 一致
/// —— 且拿原版 437 条的 `Format` 字逐位复核过；</item>
/// <item>字段里那个 `wBlockAlign` 不是字节数而是 <c>块对齐/声道数 − 22</c>：
/// MonoGame `Platform/Native/SoundEffect.Native.cs::PlatformInitializeXact`
/// 里写着 <c>if (codec == Adpcm) blockAlignment = (blockAlignment + 22) * channels;</c>，
/// 与原版 wav 的 `fmt.BlockAlign = 140 = (48 + 22) × 2` 完全吻合；</item>
/// <item>条目元数据的 24 字节顺序（`[+0]` 标志与时长、`[+4]` Format、`[+8]` 数据区相对偏移、
/// `[+12]` 字节数、`[+16]` 循环起点、`[+20]` 循环长度）与 MonoGame 读的顺序一致，
/// 循环两个字段的单位是**采样数**（MonoGame 直接把它们传给 `SoundEffect` 当 loop 区）。</item>
/// </list>
/// </summary>
public static class AdpcmMusic
{
    /// <summary>写进 `wBlockAlign` 字段的值；真块对齐 = <c>(48 + 22) × 声道数</c>（单声道 70、立体声 140）。</summary>
    public const int AlignmentField = 48;

    /// <summary>这个块对齐下每块的采样数：<c>(70·ch − 7·ch)·2/ch + 2 = 128</c>（与声道数无关）。</summary>
    public const int SamplesPerBlock = 128;

    /// <summary>条目的 `Format`/`Length`/`FlagsAndDuration`/`LoopLength`，直接搬进波形库清单。</summary>
    public readonly record struct EntryShape(uint Format, int Length, uint FlagsAndDuration, int LoopLength);

    /// <summary>真块对齐（字节）。原版立体声是 140。</summary>
    public static int BlockAlign(int channels) => (AlignmentField + 22) * channels;

    /// <summary>
    /// WAVEBANKMINIWAVEFORMAT（波形库版本 ≥ 4 那一档），只支持 ADPCM。
    /// 原版 (ADPCM, 2 声道, 44100, 块对齐 140) 的字是 <c>0x1815888A</c>。
    /// </summary>
    public static uint MiniFormat(int channels, int rate) =>
        (uint)(2                                          // wFormatTag = ADPCM
               | ((channels & 7) << 2)
               | ((rate & 0x3FFFF) << 5)
               | ((AlignmentField & 0xFF) << 23)
               | (0 << 31));                               // wBitsPerSample：ADPCM 这一档原版就是 0

    /// <summary>
    /// 源文件 → MS-ADPCM 的 RIFF wav（`fmt ` 50 字节 + `fact` 4 + `data` = 载荷），
    /// 并给出该条目的元数据。结构照原版那条 wav 抄（连 `fact` 的算法都一样），
    /// 目的是**打包器那条路原样搬运**：它只取 `data` 块、且长度与清单记录相等时不重编码
    /// （`WaveBankWriter.cs:88`）。
    ///
    /// <para>
    /// **两种源**：`.ogg`（NVorbis 解成 PCM）与**16 位 PCM 的 `.wav`**
    /// （模组确实会注入 wav —— 语料里 `Kayyy_splash.wav`、`plane.wav` 就是；
    /// 它们以前是被原样拷进树、然后让打包器走进重编码那条路的）。
    /// 别的格式（8 位/浮点/ADPCM 源）**明确报错**，不猜着转。
    /// </para>
    /// </summary>
    public static bool TryEncodeWav(string sourcePath, out byte[] wav, out EntryShape shape, out string? error)
    {
        wav = Array.Empty<byte>();
        shape = default;
        error = null;
        try
        {
            if (sourcePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                var reader = new WaveFormReader();
                reader.Init(new ReaderResolver { bufferReader = BufferReader.FormFile(sourcePath) });
                WaveForm form;
                try { form = reader.Load(); }
                finally { reader.Dispose(); }

                if (form.fmtChunk.FmtTag != FmtChunk.AudioFormats.Pcm || form.fmtChunk.BitsPerSample != 16)
                {
                    error = $"只认 16 位 PCM 的 wav（这个是 {form.fmtChunk.FmtTag} / {form.fmtChunk.BitsPerSample} 位）";
                    return false;
                }

                return TryEncodeWav(form.dataChunk.Data, form.fmtChunk.NumChannels,
                    (int)form.fmtChunk.SampleRate, out wav, out shape, out error);
            }

            if (!sourcePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                error = $"不认的音频格式（{Path.GetExtension(sourcePath)}）——只支持 .ogg 与 16 位 PCM 的 .wav";
                return false;
            }

            // 整首解码会为每首曲子分配几百 MB 的 `short[]`（LOH），全量下来把 GC 拖成大头；
            // 流式只保留一个 128 采样的块，音频编码耗时相同（DTZ 35 条：89 s vs 88 s），
            // 分配从 4.1 GB 降到 3.1 GB。产出逐字节一致（498 个 wav 比对 + 夹具单测）。
            return TryEncodeOggStreaming(sourcePath, out wav, out shape, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// PCM16 wav 的分块编码：不再把整份 PCM 转成 `short[]`，逐块从字节读数并复用块缓冲。
    /// 与 <see cref="TryEncodeWav(byte[], int, int, out byte[], out EntryShape, out string?)"/>
    /// 逐字节一致。
    /// </summary>
    private static bool TryEncodeWavPcmBlockwise(byte[] pcm16, int channels, int rate,
        out byte[] wav, out EntryShape shape, out string? error)
    {
        wav = Array.Empty<byte>();
        shape = default;
        error = null;
        try
        {
            if (channels <= 0 || rate <= 0 || pcm16.Length == 0)
            {
                error = $"波形是空的（{channels} 声道 / {rate} Hz / {pcm16.Length} 字节）";
                return false;
            }

            int blockAlign = BlockAlign(channels);
            int samples = pcm16.Length / (2 * channels);
            int blockCount = (samples + SamplesPerBlock - 1) / SamplesPerBlock;
            if (blockCount == 0)
            {
                error = "采样数不足一块";
                return false;
            }

            int blockSamples = SamplesPerBlock * channels;
            byte[] payload = new byte[blockCount * blockAlign];
            var block = new short[blockSamples];
            int seed = 0;
            for (int b = 0; b < blockCount; b++)
            {
                Array.Clear(block);
                int first = b * SamplesPerBlock;
                int count = Math.Min(SamplesPerBlock, samples - first);
                for (int j = 0; j < count; j++)
                {
                    int src = (first + j) * channels * 2;
                    int dst = j * channels;
                    for (int c = 0; c < channels; c++)
                        block[dst + c] = (short)(pcm16[src + c * 2] | (pcm16[src + c * 2 + 1] << 8));
                }

                MsAdpcm.EncodeOneBlock(block, channels, blockAlign, payload, b * blockAlign,
                    seed, out _, out seed);
            }

            int payloadLength = payload.Length;
            int totalSamples = blockCount * SamplesPerBlock;
            wav = BuildWav(payload, channels, rate, blockAlign, blockCount,
                totalSamples, payloadLength);
            shape = new EntryShape(
                Format: MiniFormat(channels, rate),
                Length: payloadLength,
                FlagsAndDuration: unchecked((uint)totalSamples * 16),
                LoopLength: totalSamples);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// OGG 分块解码 + 分块编码：只保留一个 128 采样的块，不再把整首解成 `short[]`。
    /// 每块的预测器选择与 <see cref="MsAdpcm.Encode"/> 完全一致。
    /// </summary>
    private static bool TryEncodeOggStreaming(string sourcePath,
        out byte[] wav, out EntryShape shape, out string? error)
    {
        wav = Array.Empty<byte>();
        shape = default;
        error = null;
        try
        {
            using var ogg = new NVorbis.VorbisReader(sourcePath) { ClipSamples = true };
            int channels = ogg.Channels;
            int rate = ogg.SampleRate;
            if (channels <= 0 || rate <= 0)
            {
                error = $"OGG 声道/采样率非法（{channels} / {rate}）";
                return false;
            }

            int blockAlign = BlockAlign(channels);
            int blockSamples = SamplesPerBlock * channels;
            var block = new short[blockSamples];
            var blockBytes = new byte[blockAlign];
            var blockScratch = new byte[blockAlign];
            var buffer = new float[channels * 4096];
            long estimate = ogg.TotalSamples > 0
                ? (ogg.TotalSamples + SamplesPerBlock - 1) / SamplesPerBlock * blockAlign
                : 0;
            using var payload = new MemoryStream(estimate > int.MaxValue ? 1 << 20 : (int)estimate);
            int filled = 0, blocks = 0, seed = 0;
            int read;
            while ((read = ogg.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    block[filled++] = (short)(buffer[i] * 32767f);
                    if (filled < blockSamples) continue;
                    // ⚠ `seed` 必须跨块带下去：上一块的预测器作为第一个候选，早停才压得低。
                    //   按 4 块一批调用 `MsAdpcm.Encode` 会把 seed 重置成 0，实测 DTZ 音频 86 s → 289 s。
                    MsAdpcm.EncodeOneBlock(block, channels, blockAlign, blockBytes, 0,
                        seed, blockScratch, out _, out seed);
                    payload.Write(blockBytes, 0, blockAlign);
                    blocks++;
                    Array.Clear(block);
                    filled = 0;
                }
            }

            if (filled > 0)
            {
                // block 剩余位置已经由上一轮 Array.Clear 归零，等价于整首编码的末块补零。
                MsAdpcm.EncodeOneBlock(block, channels, blockAlign, blockBytes, 0,
                    seed, blockScratch, out _, out seed);
                payload.Write(blockBytes, 0, blockAlign);
                blocks++;
            }

            if (blocks == 0)
            {
                error = "波形是空的（OGG 没有可解码采样）";
                return false;
            }

            int payloadLength = checked((int)payload.Length);
            int totalSamples = blocks * SamplesPerBlock;
            wav = BuildWav(payload.GetBuffer(), channels, rate, blockAlign, blocks,
                totalSamples, payloadLength);
            shape = new EntryShape(
                Format: MiniFormat(channels, rate),
                Length: payloadLength,
                FlagsAndDuration: unchecked((uint)totalSamples * 16),
                LoopLength: totalSamples);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 同一件事，但输入已经是 PCM（交错 16 位小端）—— 与上面那个只差一次 ogg 解码，
    /// 拆开是为了能拿合成波形直接单测（不必带一个 .ogg 夹具）。
    /// </summary>
    public static bool TryEncodeWav(byte[] pcm16, int channels, int rate,
        out byte[] wav, out EntryShape shape, out string? error)
    {
        wav = Array.Empty<byte>();
        shape = default;
        error = null;
        if (channels <= 0 || rate <= 0 || pcm16.Length == 0)
        {
            error = $"波形是空的（{channels} 声道 / {rate} Hz / {pcm16.Length} 字节）";
            return false;
        }

        int samples = pcm16.Length / (2 * channels);
        var pcm = new short[samples * channels];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
        return TryEncodeWav(pcm, channels, rate, out wav, out shape, out error);
    }

    /// <summary>已经解好的交错 16 位 PCM → MS-ADPCM wav（省掉 byte[] 中转）。</summary>
    public static bool TryEncodeWav(short[] pcm, int channels, int rate,
        out byte[] wav, out EntryShape shape, out string? error)
    {
        wav = Array.Empty<byte>();
        shape = default;
        error = null;
        try
        {
            if (channels <= 0 || rate <= 0 || pcm.Length == 0)
            {
                error = $"波形是空的（{channels} 声道 / {rate} Hz / {pcm.Length} 采样）";
                return false;
            }

            int blockAlign = BlockAlign(channels);
            int samples = pcm.Length / channels;
            int blockCount = (samples + SamplesPerBlock - 1) / SamplesPerBlock;
            if (blockCount == 0)
            {
                error = "采样数不足一块";
                return false;
            }

            byte[] payload = MsAdpcm.Encode(pcm, channels, blockAlign, blockCount, out _);
            int totalSamples = blockCount * SamplesPerBlock;

            wav = BuildWav(payload, channels, rate, blockAlign, blockCount, totalSamples);
            shape = new EntryShape(
                Format: MiniFormat(channels, rate),
                Length: payload.Length,
                // 原版 437 条的 [+0] 全都等于「采样数 × 16」（高 4 位恒 0，没有别的标志位）。
                // MonoGame 把这个字段标为 Unused，但别的 XACT 读取方不一定 —— 照原版写。
                FlagsAndDuration: unchecked((uint)totalSamples * 16),
                // 循环区单位是**采样数**，原版每条 ADPCM 都是「整段循环」（[+5] == [+0] >> 4）。
                LoopLength: totalSamples);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>拼 RIFF。字段算法照原版那条 wav：`byteRate = 块对齐 × (采样率 / 每块采样数)`（整除），`fact = 块数 × (每块采样数 − 2)`。</summary>
    private static byte[] BuildWav(byte[] payload, int channels, int rate, int blockAlign,
        int blockCount, int totalSamples, int? payloadLength = null)
    {
        const int fmtSize = 50;
        const int factSize = 4;
        int length = payloadLength ?? payload.Length;
        var wav = new byte[12 + 8 + fmtSize + 8 + factSize + 8 + length];
        int at = 0;

        void Ascii(string s) { foreach (char c in s) wav[at++] = (byte)c; }
        void U16(int v) { wav[at++] = (byte)(v & 0xFF); wav[at++] = (byte)((v >> 8) & 0xFF); }
        void U32(uint v) { for (int i = 0; i < 4; i++) wav[at++] = (byte)((v >> (8 * i)) & 0xFF); }

        Ascii("RIFF"); U32((uint)(wav.Length - 8)); Ascii("WAVE");

        Ascii("fmt "); U32(fmtSize);
        U16(2);                                    // wFormatTag：MS-ADPCM
        U16(channels);
        U32((uint)rate);
        U32((uint)(blockAlign * (rate / SamplesPerBlock)));   // 原版的算法（整除）
        U16(blockAlign);
        U16(4);                                    // 每采样 4 位
        U16(32);                                   // cbSize
        U16(SamplesPerBlock);
        U16(7);                                    // 系数对数
        foreach ((short c1, short c2) in Coefficients) { U16((ushort)c1); U16((ushort)c2); }

        Ascii("fact"); U32(factSize); U32((uint)(blockCount * (SamplesPerBlock - 2)));

        Ascii("data"); U32((uint)length);
        payload.AsSpan(0, length).CopyTo(wav.AsSpan(at));
        return wav;
    }

    /// <summary>MS-ADPCM 的标准系数表（与 <c>MsAdpcm</c> 里那两张表同一份）。</summary>
    private static readonly (short C1, short C2)[] Coefficients =
    {
        (256, 0), (512, -256), (0, 0), (192, 64), (240, 0), (460, -208), (392, -232),
    };
}
