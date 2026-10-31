using SdvCpTool.Core.Merging;
using XnbConverter.Audio;
using XnbConverter.Readers;
using XnbConverter.Xact.WaveBank.Codec;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 注入音乐编成 MS-ADPCM（`AdpcmMusic`）。
///
/// ⚠ 这里守的是「编出来的东西**结构**对」：格式字、块对齐、wav 能不能被打包器的读取器读回、
/// 解回来跟原波形差多少。**「进游戏好不好听」这一条单测守不了**，得有人去听。
/// </summary>
public sealed class AdpcmMusicTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "adpcm_" + Guid.NewGuid().ToString("N")[..8]);

    public AdpcmMusicTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* 清理失败无所谓 */ }
    }

    /// <summary>一段交错 16 位 PCM：两个声道各一条不同频率的正弦（比单音更接近真实音乐）。</summary>
    private static byte[] Pcm(int samples, int channels, int rate, double hz = 440)
    {
        var bytes = new byte[samples * channels * 2];
        for (int i = 0; i < samples; i++)
        {
            for (int c = 0; c < channels; c++)
            {
                double v = Math.Sin(2 * Math.PI * hz * (c + 1) * i / rate) * 20000;
                short s = (short)v;
                int at = (i * channels + c) * 2;
                bytes[at] = (byte)(s & 0xFF);
                bytes[at + 1] = (byte)((s >> 8) & 0xFF);
            }
        }

        return bytes;
    }

    /// <summary>
    /// ★ 格式字逐位对上**原版真值**：原版 437 条里最常见的那个字（78 条）是
    /// 0x1815888A = (ADPCM, 2 声道, 44100 Hz, 块对齐字段 48 ⇒ 真块对齐 140, 4 位)。
    /// 单声道那 6 条的形态是低 2 位 = 2、声道字段 = 1。
    /// </summary>
    [Fact]
    public void 格式字与原版条目逐位相同()
    {
        Assert.Equal(0x1815888Au, AdpcmMusic.MiniFormat(channels: 2, rate: 44100));
        Assert.Equal(0x18158886u, AdpcmMusic.MiniFormat(channels: 1, rate: 44100));

        // 位布局再拆开验一遍（MonoGame WaveBank.DecodeFormat 那套）
        uint f = AdpcmMusic.MiniFormat(channels: 2, rate: 24000);
        Assert.Equal(2u, f & 3);                       // wFormatTag = ADPCM
        Assert.Equal(2u, (f >> 2) & 7);                // nChannels
        Assert.Equal(24000u, (f >> 5) & 0x3FFFF);      // nSamplesPerSec
        Assert.Equal(48u, (f >> 23) & 0xFF);           // wBlockAlign（字段值，不是字节数）
        Assert.Equal(0u, (f >> 31) & 1);               // wBitsPerSample：这一档原版就是 0
        Assert.Equal(140, (48 + 22) * 2);              // 真块对齐：MonoGame 是 (字段 + 22) × 声道数
    }

    [Fact]
    public void 块对齐与每块采样数按原版那一档()
    {
        Assert.Equal(140, AdpcmMusic.BlockAlign(2));
        Assert.Equal(70, AdpcmMusic.BlockAlign(1));
        Assert.Equal(128, AdpcmMusic.SamplesPerBlock);
    }

    /// <summary>
    /// ★ 绝对布局回归。只用自己的解码器解自己的编码器，会把「块头整段错位、nibble 反了」也放过去
    /// —— 以前就是两边一起错，单测全绿、ffmpeg 一解全是 predictor 越界。
    /// 这里直接钉标准字节：块头按字段交错，采样1（较新）在 3·ch，采样2（较旧）在 5·ch。
    /// </summary>
    [Fact]
    public void 块头按字段交错而不是每声道七字节()
    {
        const int frames = 128, channels = 2;
        var pcm = new short[frames * channels];
        pcm[0] = 111;            // 第一帧声道 0
        pcm[1] = -222;           // 第一帧声道 1
        pcm[2] = 333;            // 第二帧声道 0（较新，写进采样1）
        pcm[3] = -444;           // 第二帧声道 1

        byte[] block = MsAdpcm.Encode(pcm, channels, blockAlign: 140, blockCount: 1, out _);
        Assert.InRange(block[0], 0, 6);
        Assert.InRange(block[1], 0, 6);
        Assert.Equal(333, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(block.AsSpan(6)));
        Assert.Equal(-444, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(block.AsSpan(8)));
        Assert.Equal(111, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(block.AsSpan(10)));
        Assert.Equal(-222, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(block.AsSpan(12)));
    }

    /// <summary>
    /// 立体声一个字节装同一帧：**高半字节 = 声道 0，低半字节 = 声道 1**。
    /// 造一个只有声道 0 在第三帧跳一下的波形，第一格必然是 0x10（旧实现写成 0x01）。
    /// </summary>
    [Fact]
    public void 立体声残差高半字节是声道零()
    {
        const int frames = 128, channels = 2;
        var pcm = new short[frames * channels];
        pcm[4] = 16;             // 第三帧声道 0；前两帧都是 0

        byte[] block = MsAdpcm.Encode(pcm, channels, blockAlign: 140, blockCount: 1, out _);
        Assert.Equal(0x10, block[14]);
    }

    /// <summary>
    /// 按标准布局手写一个块，解码结果必须逐采样对上。这个测试和编码器无关，
    /// 专门挡住「解码器跟着编码器一起错」。
    /// </summary>
    [Fact]
    public void 标准参考块能按规范解出()
    {
        var block = new byte[140];
        block[0] = 0;            // 预测器：两个声道都用 0
        block[1] = 0;
        block[2] = 16;           // 差分：声道 0
        block[4] = 16;           // 差分：声道 1
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(6), 1000);   // 采样1 声道 0
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(8), -1000);  // 采样1 声道 1
        // 采样2（10/12）都是 0
        block[14] = 0x12;        // 高半字节 1 = 声道 0，低半字节 2 = 声道 1

        short[] pcm = MsAdpcm.Decode(block, 0, block.Length, channels: 2, blockAlign: 140);
        Assert.Equal(new short[] { 0, 0, 1000, -1000, 1016, -968 }, pcm[..6]);
    }

    /// <summary>
    /// ★ 编出来的 wav **能被打包器的读取器读回**，且 `data` 块长度 == 我们写进清单的长度
    /// ⇒ 打包器那条路不会触发重编码，会把这串字节**原样搬进波形库**。
    /// </summary>
    [Fact]
    public void 编出来的wav能被打包器的读取器读回且长度与清单一致()
    {
        byte[] pcm = Pcm(samples: 3000, channels: 2, rate: 44100);
        Assert.True(AdpcmMusic.TryEncodeWav(pcm, 2, 44100, out byte[] wav, out var shape, out string? error), error);

        string path = Path.Combine(_dir, "x.wav");
        File.WriteAllBytes(path, wav);

        var reader = new WaveFormReader();
        reader.Init(new ReaderResolver { bufferReader = BufferReader.FormFile(path) });
        WaveForm form;
        try { form = reader.Load(); }
        finally { reader.Dispose(); }

        Assert.Equal(shape.Length, form.dataChunk.Data.Length);   // ← 打包器据此决定"不重编码"
        Assert.Equal(2, form.fmtChunk.NumChannels);
        Assert.Equal(44100u, form.fmtChunk.SampleRate);
        Assert.Equal(140, form.fmtChunk.BlockAlign);
        Assert.Equal(4, form.fmtChunk.BitsPerSample);
        Assert.Equal(90, wav.Length - shape.Length);              // 12 + fmt(8+50) + fact(8+4) + data(8)
        Assert.Equal(0, shape.Length % AdpcmMusic.BlockAlign(2));  // 整块
    }

    /// <summary>
    /// 采样数与循环长度按原版口径：整段循环、单位是**采样数**（原版每条 ADPCM 都这样），
    /// `FlagsAndDuration` = 采样数 × 16（原版 437 条逐条核过）。
    /// </summary>
    [Fact]
    public void 采样数与循环长度按原版口径()
    {
        Assert.True(AdpcmMusic.TryEncodeWav(Pcm(samples: 3000, channels: 2, rate: 44100), 2, 44100,
            out _, out var shape, out string? error), error);

        int blocks = 3000 / AdpcmMusic.SamplesPerBlock + 1;          // 24 块（末块补零）
        Assert.Equal(blocks, shape.Length / 140);
        Assert.Equal(blocks * AdpcmMusic.SamplesPerBlock, shape.LoopLength);
        Assert.Equal(shape.LoopLength * 16, (int)shape.FlagsAndDuration);
    }

    /// <summary>
    /// ★ 编码是有损的，但**不能离谱**：解回来与原波形比 SNR。位数交错写错、预测器序号越界、
    /// 块头三个 16 位字段顺序写反 —— 这几类错误都会把 SNR 打到个位数。
    /// （实测立体声正弦 ≈ 27 dB，这里留足余量取 15 dB；真实音乐大约 20~30 dB。）
    /// </summary>
    [Fact]
    public void 解码回来与原波形的误差在门槛内()
    {
        const int samples = 4000;
        byte[] pcm16 = Pcm(samples, channels: 2, rate: 44100);
        Assert.True(AdpcmMusic.TryEncodeWav(pcm16, 2, 44100, out _, out var shape, out string? error), error);

        // 取回载荷：从 wav 里切出来（就是打包器会搬进库的那串字节）
        Assert.True(AdpcmMusic.TryEncodeWav(pcm16, 2, 44100, out byte[] wav, out _, out _));
        int dataAt = wav.Length - shape.Length;
        byte[] payload = wav[dataAt..];

        short[] decoded = MsAdpcm.Decode(payload, 0, payload.Length, channels: 2, blockAlign: 140);
        Assert.Equal(shape.LoopLength * 2, decoded.Length);

        double signal = 0, noise = 0;
        for (int i = 0; i < samples * 2; i++)
        {
            short original = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
            double d = original - decoded[i];
            signal += (double)original * original;
            noise += d * d;
        }

        double snr = 10 * Math.Log10(signal / noise);
        Assert.True(snr > 15, $"SNR 只有 {snr:F1} dB —— 编解码有一条路写错了");
    }

    [Fact]
    public void 单声道也能编()
    {
        Assert.True(AdpcmMusic.TryEncodeWav(Pcm(samples: 500, channels: 1, rate: 22050), 1, 22050,
            out byte[] wav, out var shape, out string? error), error);

        Assert.Equal(70, AdpcmMusic.BlockAlign(1));
        Assert.Equal(AdpcmMusic.MiniFormat(1, 22050), shape.Format);
        Assert.Equal(0, shape.Length % 70);
        Assert.Equal(90 + shape.Length, wav.Length);
    }

    /// <summary>一小段 RIFF PCM wav（`fmt ` 16 字节 + `data`）。</summary>
    private static byte[] PcmWav(byte[] pcm, int channels, int rate, int bits = 16)
    {
        int blockAlign = channels * bits / 8;
        var wav = new byte[44 + pcm.Length];
        void Ascii(int at, string s) => System.Text.Encoding.ASCII.GetBytes(s).CopyTo(wav, at);
        void U32(int at, uint v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(at), v);
        void U16(int at, int v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(at), (ushort)v);
        Ascii(0, "RIFF"); U32(4, (uint)(wav.Length - 8)); Ascii(8, "WAVE");
        Ascii(12, "fmt "); U32(16, 16); U16(20, 1); U16(22, channels); U32(24, (uint)rate);
        U32(28, (uint)(rate * blockAlign)); U16(32, blockAlign); U16(34, bits);
        Ascii(36, "data"); U32(40, (uint)pcm.Length);
        pcm.CopyTo(wav, 44);
        return wav;
    }

    /// <summary>
    /// 源文件是 **16 位 PCM 的 wav** 也要能编 —— 语料里真有两处（`Kayyy_splash.wav`、
    /// `plane.wav`）。以前这两个是被原样拷进树、再让打包器走进重编码那条路的。
    /// 其它格式（这里是 8 位）**明确报错**，不猜着转。
    /// </summary>
    [Fact]
    public void wav源也能编_别的格式明确报错()
    {
        string ok = Path.Combine(_dir, "ok.wav");
        File.WriteAllBytes(ok, PcmWav(Pcm(samples: 1000, channels: 2, rate: 48000), 2, 48000));
        Assert.True(AdpcmMusic.TryEncodeWav(ok, out _, out var shape, out string? error), error);
        Assert.Equal(AdpcmMusic.MiniFormat(2, 48000), shape.Format);

        string bits8 = Path.Combine(_dir, "bits8.wav");
        File.WriteAllBytes(bits8, PcmWav(new byte[1000 * 2], 2, 44100, bits: 8));
        Assert.False(AdpcmMusic.TryEncodeWav(bits8, out _, out _, out string? error2));
        Assert.Contains("16 位 PCM", error2);

        Assert.False(AdpcmMusic.TryEncodeWav(Path.Combine(_dir, "x.mp3"), out _, out _, out string? error3));
        Assert.Contains("不认的音频格式", error3);
    }

    [Fact]
    public void WAV分块编码与整首编码逐字节一致()
    {
        byte[] pcm = Pcm(samples: 3000, channels: 2, rate: 44100);
        Assert.True(AdpcmMusic.TryEncodeWav(pcm, 2, 44100,
            out byte[] full, out var fullShape, out string? e1), e1);

        string path = Path.Combine(_dir, "blockwise.wav");
        File.WriteAllBytes(path, PcmWav(pcm, 2, 44100));
        Assert.True(AdpcmMusic.TryEncodeWav(path,
            out byte[] streamed, out var streamedShape, out string? e2), e2);

        Assert.Equal(full, streamed);
        Assert.Equal(fullShape, streamedShape);
    }

    /// <summary>
    /// AVX2 路径（4 块一组试算误差）必须与**逐块标量编码**逐字节一致。
    /// 覆盖正弦 / 白噪声 / 静音 / 满幅削波，单双声道，块数不是 4 的倍数，末尾块不满。
    /// </summary>
    [Theory]
    [InlineData(1, 4, 0, false)]
    [InlineData(2, 4, 0, false)]
    [InlineData(2, 7, 1, false)]
    [InlineData(1, 13, 2, false)]
    [InlineData(2, 40, 3, false)]
    [InlineData(2, 8, 1, true)]
    public void 整批编码与逐块标量一致(int channels, int blockCount, int kind, bool shortTail)
    {
        int spp = AdpcmMusic.SamplesPerBlock;
        int full = blockCount * spp * channels;
        // 末尾块不满：少 5 个采样，两种路径都要按「越界读 0」补齐。
        var pcm = new short[shortTail ? full - 5 : full];
        var rnd = new Random(1234 + (kind * 97) + (channels * 7) + blockCount);
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (short)(kind switch
            {
                0 => (int)(Math.Sin(i * 0.05) * 30000),
                1 => rnd.Next(-32768, 32768),
                2 => 0,
                _ => (i & 1) == 0 ? 32767 : -32768,
            });
        }

        int blockAlign = AdpcmMusic.BlockAlign(channels);
        byte[] vector = MsAdpcm.Encode(pcm, channels, blockAlign, blockCount, out _);

        var scalar = new byte[blockCount * blockAlign];
        var block = new short[spp * channels];
        for (int b = 0; b < blockCount; b++)
        {
            Array.Clear(block);
            int at = b * spp * channels;
            int n = Math.Min(block.Length, pcm.Length - at);
            if (n > 0) Array.Copy(pcm, at, block, 0, n);
            MsAdpcm.EncodeOneBlock(block, channels, blockAlign, scalar, b * blockAlign, out _);
        }

        Assert.Equal(scalar, vector);
    }

    [Fact]
    public void OGG分块编码与整首编码逐字节一致()
    {
        string? path = Environment.GetEnvironmentVariable("SDVCP_OGG_FIXTURE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var (channels, rate, pcm16) = OggVorbis.ToPcm16(path);
        Assert.True(AdpcmMusic.TryEncodeWav(pcm16, channels, rate,
            out byte[] full, out var fullShape, out string? e1), e1);
        Assert.True(AdpcmMusic.TryEncodeWav(path,
            out byte[] streamed, out var streamedShape, out string? e2), e2);
        Assert.Equal(full, streamed);
        Assert.Equal(fullShape, streamedShape);
    }

    [Fact]
    public void 空波形报错而不是写出个空wav()
    {
        Assert.False(AdpcmMusic.TryEncodeWav(Array.Empty<byte>(), 2, 44100,
            out byte[] wav, out _, out string? error));
        Assert.Contains("空的", error);
        Assert.Empty(wav);
    }
}
