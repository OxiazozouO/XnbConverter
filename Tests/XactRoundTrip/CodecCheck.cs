using XnbConverter.Xact.WaveBank.Codec;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;

namespace XactRoundTrip;

/// <summary>
/// 用真实波形库数据检验 MS-ADPCM 编解码。
///
/// 关键判据是「块头里的两个采样字段能否原样复现」：
/// 这两个字段存的是该块真实的头两个采样，解码时会按固定次序还原、
/// 重编码时又原样写回。若块头字段的先后顺序理解反了，还原出来的次序就会颠倒，
/// 写回去自然对不上。所以逐块比对这两个字段，就等价于验证了整个约定。
/// </summary>
public static class CodecCheck
{
    /// <summary>合成正弦自测：把编解码器单独拎出来验，排除重采样等因素。</summary>
    private static int SineSelfTest()
    {
        int num = SineCase(1, 38);
        return num + SineCase(2, 76);
    }

    /// <summary>单/双声道各测一遍（双声道两路用不同频率，可暴露交错顺序错误）。</summary>
    private static int SineCase(int channels, int blockAlign)
    {
        const int rate = 44100;
        int spb = MsAdpcm.SamplesPerBlock(blockAlign, channels);
        const int blocks = 200;
        int frames = spb * blocks;
        short[] sine = new short[frames * channels];
        for (int i = 0; i < frames; i++)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                sine[i * channels + ch] = (short)(16000 * Math.Sin(2 * Math.PI * (440.0 + 220 * ch) * i / rate));
            }
        }

        byte[] encoded = MsAdpcm.Encode(sine, channels, blockAlign, blocks, out double _);
        short[] decoded = MsAdpcm.Decode(encoded, 0, encoded.Length, channels, blockAlign);

        int peak = 0;
        double mse = 0;
        for (int i = 0; i < frames * channels; i++)
        {
            peak = Math.Max(peak, Math.Abs((int)decoded[i]));
            double d = sine[i] - decoded[i];
            mse += d * d;
        }

        mse /= frames;
        double psnr = mse <= 0 ? 99 : 10 * Math.Log10(32768.0 * 32768.0 / mse);
        Console.Write("    前 8 个采样 输入:");
        for (int i = 0; i < 8; i++)
        {
            Console.Write($" {sine[i]}");
        }

        Console.Write("  输出:");
        for (int i = 0; i < 8; i++)
        {
            Console.Write($" {decoded[i]}");
        }

        Console.Write("   第 60..63 输入:");
        for (int i = 60; i < 64; i++)
        {
            Console.Write($" {sine[i]}");
        }

        Console.Write("  输出:");
        for (int i = 60; i < 64; i++)
        {
            Console.Write($" {decoded[i]}");
        }

        Console.WriteLine();
        bool ok = peak < 20000 && psnr > 20;
        Console.WriteLine($"  {channels}ch 正弦自测: 输出峰值 {peak}, PSNR={psnr:F1}dB  {(ok ? "✓" : "✗")}");
        return ok ? 0 : 1;
    }

    public static int Run(string path)
    {
        int num = SineSelfTest();
        WaveBank waveBank = WaveBankReader.Read(path);
        int failed = 0;
        int checkedCount = 0;
        long minHeader = 100;
        double minPsnr = 999;
        HashSet<int> channelKinds = new HashSet<int>();
        for (int index = 0; index < waveBank.Entries.Count; index++)
        {
            WaveBank.WaveBankEntry entry = waveBank.Entries[index];
            entry.DecodeFormat(waveBank.Header.Version, out WaveBank.WaveBankFormats codec,
                out int channels, out int rate, out int align, out int bits);
            if (codec != WaveBank.WaveBankFormats.AdpcmMs)
            {
                Console.WriteLine($"  条目{index}: 跳过（{codec}）");
                continue;
            }

            int blockAlign = (align + 22) * channels;
            byte[] payload = entry.Data;
            int blockCount = payload.Length / blockAlign;
            int spb = MsAdpcm.SamplesPerBlock(blockAlign, channels);

            short[] pcm = MsAdpcm.Decode(payload, 0, payload.Length, channels, blockAlign);
            byte[] reencoded = MsAdpcm.Encode(pcm, channels, blockAlign, blockCount, out double err);
            short[] pcm2 = MsAdpcm.Decode(reencoded, 0, reencoded.Length, channels, blockAlign);

            double mse = 0;
            for (int i = 0; i < pcm.Length; i++)
            {
                double d = pcm[i] - pcm2[i];
                mse += d * d;
            }

            mse /= Math.Max(pcm.Length, 1);
            double psnr = mse <= 0 ? 99.0 : 10.0 * Math.Log10(32768.0 * 32768.0 / mse);

            // 逐块比对块头里的两个采样字段（每声道 +3 与 +5 各 2 字节）
            int same = 0;
            for (int b = 0; b < blockCount; b++)
            {
                bool ok = true;
                for (int ch = 0; ch < channels && ok; ch++)
                {
                    int h = b * blockAlign + 7 * ch;
                    ok = payload[h + 3] == reencoded[h + 3] && payload[h + 4] == reencoded[h + 4]
                         && payload[h + 5] == reencoded[h + 5] && payload[h + 6] == reencoded[h + 6];
                }

                if (ok)
                {
                    same++;
                }
            }

            double rate2 = 100.0 * same / Math.Max(blockCount, 1);
            bool ok2 = rate2 > 99.0 && psnr > 12.0;
            checkedCount++;
            channelKinds.Add(channels);
            minHeader = Math.Min(minHeader, (long)rate2);
            minPsnr = Math.Min(minPsnr, psnr);
            if (!ok2 || checkedCount <= 3)
            {
                Console.WriteLine($"  条目{index}: {channels}ch {rate}Hz align={blockAlign} spb={spb} 块={blockCount}  " +
                                  $"块头采样还原={rate2:F1}%  往返PSNR={psnr:F1}dB  {(ok2 ? "✓" : "✗")}");
            }

            if (!ok2)
            {
                failed++;
            }
        }

        Console.WriteLine($"  汇总: 检查 {checkedCount} 个 ADPCM 条目，声道数 {{{string.Join(",", channelKinds)}}}，" +
                          $"最差块头还原 {minHeader}%，最差往返 PSNR {minPsnr:F1}dB，失败 {failed}");
        return failed + num;
    }
}
