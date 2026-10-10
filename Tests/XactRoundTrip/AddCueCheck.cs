using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;

namespace XactRoundTrip;

/// <summary>
/// 追加 Cue 的端到端检验：读 .xsb → 往 Cues 里挂一条 FileOffset=0 的新 Cue → 重建 →
/// 把重建结果**重新解包**，逐项比对。
///
/// 要证明四件事：
///   ① 重建结果仍是一份能被解包器读通的 .xsb；
///   ② 头部三处计数（复杂 Cue / 总 Cue / 名字表长度）按增量走；
///   ③ 新 Cue 的名字与它指向的 (WaveBankIndex, TrackIndex) 读得回来；
///   ④ 原文件那一段**除了头部那 5 个字段外**一个字节没动。
/// </summary>
public static class AddCueCheck
{
    public static int Run(string input)
    {
        string dir = Path.Combine("addcue", Path.GetFileNameWithoutExtension(input));
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }

        Directory.CreateDirectory(dir);

        byte[] original = File.ReadAllBytes(input);
        var bank = SoundBankReader.Read(input);

        int oldComplex = bank.Header.NumComplexCues;
        int oldTotal = bank.Header.NumTotalCues;
        int oldNameLen = bank.Header.CueNameTableLen;
        int oldCueCount = bank.Cues.Count;
        string[] oldNames = bank.Cues.Select(c => c.Name).ToArray();

        const string newName = "zzz_roundtrip_probe";

        var cue = new SoundBank.SoundCue
        {
            // FileOffset 保持 0 —— 这就是「新增」的标记
            Name = newName,
            Flags = 0,
            VariationFlags = 0,
            InstanceLimit = 0,
            FadeInSec = 0,
            FadeOutSec = 0,
            InstanceFlags = 0
        };
        cue.CueVariations.Add(new SoundBank.SoundCue.CueVariation
        {
            TrackIndex = 7,
            WaveBankIndex = 0,
            BWeightMin = 0,
            BWeightMax = 0
        });
        bank.Cues.Add(cue);

        byte[] rebuilt = SoundBankReader.Build(bank);
        string outPath = Path.Combine(dir, "rebuilt.xsb");
        File.WriteAllBytes(outPath, rebuilt);

        var bank2 = SoundBankReader.Read(outPath);   // 读不通就会抛

        bool countsOk = bank2.Header.NumComplexCues == oldComplex + 1
                        && bank2.Header.NumTotalCues == oldTotal + 1
                        && bank2.Header.CueNameTableLen > oldNameLen
                        && bank2.Cues.Count == oldCueCount + 1;

        var last = bank2.Cues[^1];
        bool cueOk = last.Name == newName
                     && last.CueVariations.Count == 1
                     && last.CueVariations[0].TrackIndex == 7
                     && last.CueVariations[0].WaveBankIndex == 0;

        bool keptOk = true;
        for (int i = 0; i < oldCueCount; i++)
        {
            if (bank2.Cues[i].Name != oldNames[i])
            {
                keptOk = false;
                Console.WriteLine($"    已有 Cue {i} 的名字变了：{oldNames[i]} → {bank2.Cues[i].Name}");
                break;
            }
        }

        // 原文件那一段只该在头部 5 个字段处不同
        int[] allowed = { 21, 22, 25, 26, 30, 31, 38, 39, 40, 41, 42, 43, 44, 45 };
        int unexpected = -1;
        for (int i = 0; i < Math.Min(original.Length, rebuilt.Length); i++)
        {
            if (original[i] != rebuilt[i] && Array.IndexOf(allowed, i) < 0)
            {
                unexpected = i;
                break;
            }
        }

        bool prefixOk = unexpected < 0;

        Console.WriteLine($"  复杂 Cue {oldComplex} → {bank2.Header.NumComplexCues}   " +
                          $"总 Cue {oldTotal} → {bank2.Header.NumTotalCues}   " +
                          $"名字表 {oldNameLen} → {bank2.Header.CueNameTableLen}  {(countsOk ? "✓" : "✗")}");
        Console.WriteLine($"  新 Cue「{last.Name}」→ (bank {last.CueVariations[0].WaveBankIndex}, track {last.CueVariations[0].TrackIndex})  {(cueOk ? "✓" : "✗")}   " +
                          $"已有 Cue 名不变 {(keptOk ? "✓" : "✗")}   " +
                          $"原段未被改动 {(prefixOk ? "✓" : $"✗ 首个意外差异 @{unexpected}")}   " +
                          $"文件 {original.Length} → {rebuilt.Length}");

        return countsOk && cueOk && keptOk && prefixOk ? 0 : 1;
    }
}
