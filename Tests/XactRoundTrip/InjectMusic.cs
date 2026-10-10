using System.Diagnostics;
using Newtonsoft.Json;
using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;
using XnbConverter.Xact.WaveBank.Writer;

namespace XactRoundTrip;

/// <summary>
/// 造一对「多了一首曲子」的 Wave Bank + Sound Bank，供实机验证：
/// ogg →（ffmpeg 转 PCM）→ 追加进 .xwb → 在 .xsb 里加一条指向它的 Cue。
///
/// 新波形一律用 **PCM 16bit**：原版 437 条里本来就有 6 条 PCM，条目各带自己的
/// Format，混用是合法的；而且 PCM 的载荷就是 wav 的 data 块，长度直接等于
/// 清单里的 Length，走不到重编码那条路。
///
/// 用法: XactRoundTrip --inject-music &lt;原.xwb&gt; &lt;原.xsb&gt; &lt;ogg&gt; &lt;cue名&gt; &lt;ffmpeg&gt; &lt;输出目录&gt;
/// </summary>
public static class InjectMusic
{
    public static int Run(string xwbPath, string xsbPath, string oggPath, string cueName,
        string ffmpeg, string outDir)
    {
        string unpack = Path.Combine(outDir, "_unpack");
        Directory.CreateDirectory(unpack);

        // ---- 1. ogg → PCM wav ----
        string wavRel = Path.Combine("Wave Bank", "Music", "injected.wav");
        string wavPath = Path.Combine(unpack, wavRel);
        Directory.CreateDirectory(Path.GetDirectoryName(wavPath));
        RunFfmpeg(ffmpeg, oggPath, wavPath);

        (int channels, int rate, int bits, int dataOffset, int dataLength) = ReadPcmWav(wavPath);
        Console.WriteLine($"  转码：{Path.GetFileName(oggPath)} → {channels}ch {rate}Hz {bits}bit  " +
                          $"载荷 {dataLength} 字节");

        // ---- 2. 解包 xwb，追加条目 ----
        WaveBank wb = WaveBankReader.Read(xwbPath);
        wb.OutputPath = Path.Combine(unpack, "Wave Bank");
        for (int i = 0; i < wb.Entries.Count; i++)
        {
            wb.Entries[i].FilePath = Path.Combine(unpack, "Wave Bank") + Path.DirectorySeparatorChar;
            wb.Entries[i].FileName ??= $"{i:x8}";
        }

        WaveBankReader.Save(wb);
        int newTrack = wb.Entries.Count;

        string manifest = wb.OutputPath + ".xwb.config";
        WaveBankManifest m = JsonConvert.DeserializeObject<WaveBankManifest>(File.ReadAllText(manifest));
        string manifestDir = Path.GetDirectoryName(Path.GetFullPath(manifest));
        int blockAlign = channels * (bits / 8);
        uint format = (uint)(0                       // PCM
                             | (channels << 2)
                             | (rate << 5)
                             | (blockAlign << 23)
                             | ((bits == 16 ? 1 : 0) << 31));
        m.Entries.Add(new WaveBankManifest.Entry
        {
            FileName = Path.GetRelativePath(manifestDir, Path.GetFullPath(wavPath)).Replace('/', '\\'),
            Offset = WaveBankWriter.AppendToEnd,
            Length = dataLength,
            FlagsAndDuration = 0,
            Format = format,
            LoopOffset = 0,
            LoopLength = (uint)dataLength      // 整段循环（音乐要的）
        });
        File.WriteAllText(manifest, JsonConvert.SerializeObject(m, Formatting.Indented));

        byte[] newXwb = WaveBankWriter.Build(manifest);
        string outXwb = Path.Combine(outDir, "Wave Bank.xwb");
        File.WriteAllBytes(outXwb, newXwb);

        // ---- 3. xsb 加 Cue，指向新条目 ----
        var sb = SoundBankReader.Read(xsbPath);
        var view = SoundBankView.From(sb);
        view.Sounds.Add(new SoundBankView.SoundView { FileOffset = 0, Cue = cueName, Pitch = 0f });
        view.Variations.Add(new SoundBankView.VariationView
        {
            FileOffset = 0,
            Cue = cueName,
            WaveBankIndex = 0,
            TrackIndex = (ushort)newTrack
        });
        view.ApplyTo(sb);
        byte[] newXsb = SoundBankReader.Build(sb);
        File.WriteAllBytes(Path.Combine(outDir, "Sound Bank.xsb"), newXsb);

        // ---- 4. 读回来确认 ----
        WaveBank checkWb = WaveBankReader.Read(outXwb);
        var checkSb = SoundBankReader.Read(Path.Combine(outDir, "Sound Bank.xsb"));
        bool entryOk = checkWb.Entries.Count == newTrack + 1
                       && checkWb.Entries[^1].PlayRegion.Length == dataLength;
        bool cueOk = checkSb.Cues[^1].Name == cueName
                     && checkSb.Cues[^1].CueVariations[0].TrackIndex == newTrack;
        Console.WriteLine($"  xwb 条目 {newTrack} → {checkWb.Entries.Count}，新条目载荷 {checkWb.Entries[^1].PlayRegion.Length}  {(entryOk ? "✓" : "✗")}");
        Console.WriteLine($"  xsb Cue「{cueName}」→ track {newTrack}  {(cueOk ? "✓" : "✗")}");
        Console.WriteLine($"  写出：Wave Bank.xwb {newXwb.Length} 字节、Sound Bank.xsb {newXsb.Length} 字节");

        return entryOk && cueOk ? 0 : 1;
    }

    private static void RunFfmpeg(string ffmpeg, string src, string dst)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (string a in new[] { "-y", "-loglevel", "error", "-i", src, "-ac", "2", "-ar", "44100",
                     "-c:a", "pcm_s16le", dst })
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi);
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg 失败（{p.ExitCode}）：{err}");
        }
    }

    /// <summary>只认 ffmpeg 刚写出来的那种标准 PCM wav：RIFF/WAVE + fmt + data。</summary>
    private static (int channels, int rate, int bits, int dataOffset, int dataLength) ReadPcmWav(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (BitConverter.ToInt32(b, 0) != 0x46464952 || BitConverter.ToInt32(b, 8) != 0x45564157)
        {
            throw new InvalidOperationException("不是 RIFF/WAVE。");
        }

        int pos = 12;
        int channels = 0;
        int rate = 0;
        int bits = 0;
        while (pos + 8 <= b.Length)
        {
            int id = BitConverter.ToInt32(b, pos);
            int size = BitConverter.ToInt32(b, pos + 4);
            if (id == 0x20746d66)                              // "fmt "
            {
                channels = BitConverter.ToInt16(b, pos + 10);
                rate = BitConverter.ToInt32(b, pos + 12);
                bits = BitConverter.ToInt16(b, pos + 22);
            }
            else if (id == 0x61746164)                         // "data"
            {
                if (channels == 0 || bits == 0)
                {
                    throw new InvalidOperationException("data 块出现在 fmt 块之前。");
                }

                return (channels, rate, bits, pos + 8, size);
            }

            pos += 8 + size + (size % 2);
        }

        throw new InvalidOperationException("没找到 data 块。");
    }
}
