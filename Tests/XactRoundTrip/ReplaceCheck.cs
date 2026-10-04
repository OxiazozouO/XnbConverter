using XnbConverter.Xact.WaveBank.Codec;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;
using XnbConverter.Xact.WaveBank.Writer;

namespace XactRoundTrip;

/// <summary>
/// 「换音频」端到端检验：解包 -> 用一段合成的 440Hz 正弦替换某个条目的 wav ->
/// 打包（自动重编码成等长载荷）-> 再解码，看音频是否真的是那段正弦。
/// </summary>
public static class ReplaceCheck
{
    public static int Run(string path)
    {
        byte[] original = File.ReadAllBytes(path);
        string outDir = "rt_replace";
        if (Directory.Exists(outDir))
        {
            Directory.Delete(outDir, true);
        }

        Directory.CreateDirectory(outDir);

        WaveBank waveBank = WaveBankReader.Read(path);
        waveBank.OutputPath = outDir;
        for (int i = 0; i < waveBank.Entries.Count; i++)
        {
            waveBank.Entries[i].FilePath = outDir + Path.DirectorySeparatorChar;
            waveBank.Entries[i].FileName ??= $"{i:x8}";
        }

        WaveBankReader.Save(waveBank);

        WaveBank.WaveBankEntry entry = waveBank.Entries[0];
        entry.DecodeFormat(waveBank.Header.Version, out WaveBank.WaveBankFormats codec, out int channels,
            out int rate, out int align, out int bits);
        int blockAlign = (align + 22) * channels;
        int blockCount = (int)entry.PlayRegion.Length / blockAlign;
        int samplesPerBlock = MsAdpcm.SamplesPerBlock(blockAlign, channels);
        int frames = blockCount * samplesPerBlock;

        // 合成 440Hz 正弦（用 48kHz，故意和目标 44.1k 左右不同，顺便检验重采样）
        const int sourceRate = 48000;
        const double toneHz = 440.0;
        int sourceFrames = sourceRate * 3;
        byte[] pcm = new byte[sourceFrames * channels * 2];
        for (int i = 0; i < sourceFrames; i++)
        {
            short v = (short)(16000 * Math.Sin(2 * Math.PI * toneHz * i / sourceRate));
            for (int ch = 0; ch < channels; ch++)
            {
                pcm[(i * channels + ch) * 2] = (byte)(v & 0xFF);
                pcm[(i * channels + ch) * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
        }

        string targetWav = Path.Combine(outDir, "00000000.wav");
        // 替换用的 wav 本身是 PCM（采样率刻意用 48k），由工具负责重采样并编码成目标的 ADPCM
        WaveFormReader.Save(pcm, targetWav, WaveBank.WaveBankFormats.Pcm, sourceRate, channels, 1, 0);
        Console.WriteLine($"  已用 3 秒 440Hz 正弦替换 {Path.GetFileName(targetWav)}（源 {sourceRate}Hz {pcm.Length} 字节，" +
                          $"原载荷 {entry.PlayRegion.Length} 字节）");

        string manifest = waveBank.OutputPath + ".xwb.config";
        byte[] rebuilt = WaveBankWriter.Build(manifest);

        Console.WriteLine($"  重打包大小 {rebuilt.Length}，与原文件 {(rebuilt.Length == original.Length ? "一致 ✓" : "不一致 ✗")}");

        // 只有被替换的那个条目区域应当变化
        int first = -1;
        int last = -1;
        int diffCount = 0;
        for (int i = 0; i < Math.Min(rebuilt.Length, original.Length); i++)
        {
            if (rebuilt[i] != original[i])
            {
                diffCount++;
                if (first < 0)
                {
                    first = i;
                }

                last = i;
            }
        }

        int entryStart = (int)entry.PlayRegion.Offset;
        int entryEnd = entryStart + (int)entry.PlayRegion.Length;
        bool contained = first >= entryStart && last < entryEnd;
        Console.WriteLine($"  改动 {diffCount} 字节，范围 [{first}..{last}]，条目区 [{entryStart}..{entryEnd})  " +
                          $"{(contained ? "全部落在目标条目内 ✓" : "越界 ✗")}");

        // 把替换后的文件重新读出来解码，验证音频确实是那段正弦
        string rebuiltPath = Path.Combine(outDir, Path.GetFileName(path));
        File.WriteAllBytes(rebuiltPath, rebuilt);
        WaveBank check = WaveBankReader.Read(rebuiltPath);
        check.Entries[0].DecodeFormat(check.Header.Version, out _, out int ch2, out int rate2, out _, out _);
        short[] decoded = MsAdpcm.Decode(check.Entries[0].Data, 0, check.Entries[0].Data.Length, ch2,
            blockAlign);

        // 过零点估频
        int crossings = 0;
        for (int i = 1; i < decoded.Length; i++)
        {
            if ((decoded[i - 1] < 0) != (decoded[i] < 0))
            {
                crossings++;
            }
        }

        double seconds = (double)decoded.Length / ch2 / rate2;
        double estimated = crossings / 2.0 / seconds;
        int peak = 0;
        foreach (short s in decoded)
        {
            peak = Math.Max(peak, Math.Abs((int)s));
        }

        bool ok = Math.Abs(estimated - toneHz) < 20 && peak > 10000;
        Console.WriteLine($"  解码结果：时长 {seconds:F2}s  过零点估频 {estimated:F1}Hz（目标 {toneHz}）  峰值 {peak}  " +
                          $"{(ok ? "确实是那段正弦 ✓" : "不对 ✗")}");
        return ok && contained && rebuilt.Length == original.Length ? 0 : 1;
    }
}
