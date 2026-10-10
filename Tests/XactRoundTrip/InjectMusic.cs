using Newtonsoft.Json;
using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;
using XnbConverter.Xact.WaveBank.Writer;

namespace XactRoundTrip;

/// <summary>
/// 造一对「多了一首曲子」的 Wave Bank + Sound Bank，供实机验证：
/// ogg →（Core 里的 NVorbis 解成 PCM）→ 追加进 .xwb → 在 .xsb 里加一条指向它的 Cue。
///
/// 全程零外部依赖：不调 ffmpeg、不引 NuGet。
///
/// 新波形一律用 **PCM 16bit**：原版 437 条里本来就有 6 条 PCM，条目各带自己的
/// Format，混用是合法的；而且 PCM 的载荷就是 wav 的 data 块，长度直接等于
/// 清单里的 Length，走不到重编码那条路。
///
/// 用法: XactRoundTrip --inject-music &lt;原.xwb&gt; &lt;原.xsb&gt; &lt;ogg&gt; &lt;cue名&gt; &lt;输出目录&gt;
/// </summary>
public static class InjectMusic
{
    public static int Run(string xwbPath, string xsbPath, string oggPath, string cueName, string outDir)
    {
        string unpack = Path.Combine(outDir, "_unpack");
        Directory.CreateDirectory(unpack);

        // ---- 1. 解包 xwb，把 ogg 放进去、清单里加一条 ----
        //      ⚠ 这里**故意不解码** —— 生产侧（SdvCpTool）就是只做这两件事，
        //      解码留给打包器（WaveBankWriter 认出 .ogg 会自己解并回填 Length/Format）。
        WaveBank wb = WaveBankReader.Read(xwbPath);
        wb.OutputPath = Path.Combine(unpack, "Wave Bank");
        for (int i = 0; i < wb.Entries.Count; i++)
        {
            wb.Entries[i].FilePath = Path.Combine(unpack, "Wave Bank") + Path.DirectorySeparatorChar;
            wb.Entries[i].FileName ??= $"{i:x8}";
        }

        WaveBankReader.Save(wb);
        int newTrack = wb.Entries.Count;

        string oggDest = Path.Combine(unpack, "Wave Bank", "Music", Path.GetFileName(oggPath));
        Directory.CreateDirectory(Path.GetDirectoryName(oggDest));
        File.Copy(oggPath, oggDest, true);

        string manifest = wb.OutputPath + ".xwb.config";
        WaveBankManifest m = JsonConvert.DeserializeObject<WaveBankManifest>(File.ReadAllText(manifest));
        string manifestDir = Path.GetDirectoryName(Path.GetFullPath(manifest));
        m.Entries.Add(new WaveBankManifest.Entry
        {
            FileName = Path.GetRelativePath(manifestDir, Path.GetFullPath(oggDest)).Replace('/', '\\'),
            Offset = WaveBankWriter.AppendToEnd,
            Length = 0,          // 打包器解码后回填
            Format = 0,          // 同上
            FlagsAndDuration = 0,
            LoopOffset = 0,
            LoopLength = 0       // 0 ⇒ 打包器按整段循环
        });
        File.WriteAllText(manifest, JsonConvert.SerializeObject(m, Formatting.Indented));

        byte[] newXwb = WaveBankWriter.Build(manifest);
        string outXwb = Path.Combine(outDir, "Wave Bank.xwb");
        File.WriteAllBytes(outXwb, newXwb);
        Console.WriteLine($"  ogg 直接进库（打包器解码）：{Path.GetFileName(oggPath)}");

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
                       && checkWb.Entries[^1].PlayRegion.Length > 0;
        bool cueOk = checkSb.Cues[^1].Name == cueName
                     && checkSb.Cues[^1].CueVariations[0].TrackIndex == newTrack;
        Console.WriteLine($"  xwb 条目 {newTrack} → {checkWb.Entries.Count}，新条目载荷 {checkWb.Entries[^1].PlayRegion.Length}  {(entryOk ? "✓" : "✗")}");
        Console.WriteLine($"  xsb Cue「{cueName}」→ track {newTrack}  {(cueOk ? "✓" : "✗")}");
        Console.WriteLine($"  写出：Wave Bank.xwb {newXwb.Length} 字节、Sound Bank.xsb {newXsb.Length} 字节");

        return entryOk && cueOk ? 0 : 1;
    }
}
