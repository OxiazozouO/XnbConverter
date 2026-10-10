using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;

namespace XactRoundTrip;

/// <summary>
/// 往 .xsb 里插一条 Cue 并单独写出，供实机验证用（不碰 .xwb）。
/// 指向的是一个**已存在**的波形槽位，所以只验「游戏认不认这份声库」，不验音频内容。
///
/// 用法: XactRoundTrip --inject-cue &lt;原始.xsb&gt; &lt;cue名&gt; &lt;trackIndex&gt; &lt;输出.xsb&gt;
/// </summary>
public static class InjectCue
{
    public static int Run(string input, string cueName, int trackIndex, string output)
    {
        var bank = SoundBankReader.Read(input);
        var view = SoundBankView.From(bank);
        view.Sounds.Add(new SoundBankView.SoundView
        {
            FileOffset = 0,
            Cue = cueName,
            Pitch = 0f
        });
        view.Variations.Add(new SoundBankView.VariationView
        {
            FileOffset = 0,
            Cue = cueName,
            WaveBankIndex = 0,
            TrackIndex = (ushort)trackIndex
        });
        view.ApplyTo(bank);

        byte[] rebuilt = SoundBankReader.Build(bank);
        File.WriteAllBytes(output, rebuilt);

        // 立刻读回来确认能被解析
        var check = SoundBankReader.Read(output);
        var last = check.Cues[^1];
        bool ok = last.Name == cueName && last.CueVariations[0].TrackIndex == trackIndex;
        Console.WriteLine($"  插入 Cue「{cueName}」→ track {trackIndex}  " +
                          $"总 Cue {bank.Header.NumTotalCues} → {check.Header.NumTotalCues}  " +
                          $"{new FileInfo(input).Length} → {rebuilt.Length} 字节  {(ok ? "读回 ✓" : "读回 ✗")}");
        return ok ? 0 : 1;
    }
}
