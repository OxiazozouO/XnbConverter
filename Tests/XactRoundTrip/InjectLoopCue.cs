using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;

namespace XactRoundTrip;

/// <summary>
/// 往 .xsb 里插一条**会循环**的 Cue，并单独写出。
///
/// <para>
/// 循环不能走「复杂 Cue + 变体类型 0」那条路（MonoGame 拿到的是不循环的实例），
/// 得照原版音乐的形态：**简单 Cue → 带 Clip 的 Sound → 类型 1 事件、LoopCount=255**。
/// 用法：<c>--inject-loop &lt;原始.xsb&gt; &lt;cue名&gt; &lt;trackIndex&gt; &lt;输出.xsb&gt;</c>
/// </para>
/// </summary>
public static class InjectLoopCue
{
    public static int Run(string input, string cueName, int trackIndex, string output)
    {
        SoundBank bank = SoundBankReader.Read(input);
        SoundBankView view = SoundBankView.From(bank);

        view.Sounds.Add(new SoundBankView.SoundView
        {
            FileOffset = 0,
            Cue = cueName,
            CategoryId = 2,          // Music（见 .xgs 的类别表顺序）
            Pitch = 0f,
        });
        view.Variations.Add(new SoundBankView.VariationView
        {
            FileOffset = 0,
            Cue = cueName,
            WaveBankIndex = 0,
            TrackIndex = (ushort)trackIndex,
        });
        view.LoopedCues.Add(cueName);
        view.ApplyTo(bank);

        byte[] rebuilt = SoundBankReader.Build(bank);
        File.WriteAllBytes(output, rebuilt);

        // 读回来核对：必须落在**简单 Cue** 上，而且带一条 LoopCount=255 的 Clip 事件
        SoundBank check = SoundBankReader.Read(output);
        SoundBank.SoundEntry? entry = check.SoundEntrys.FirstOrDefault(e => e.Name == cueName);
        XactSound? sound = check._sounds.TryGetValue(cueName, out XactSound[]? s) && s.Length > 0 ? s[0] : null;
        XactClip.WaveIndex? wave = sound?.SoundClips?.FirstOrDefault()?.WaveIndexs?.FirstOrDefault();

        bool ok = entry != null
                  && sound != null
                  && (sound.Flags & 1) != 0
                  && sound.NumClips == 1
                  && wave != null
                  && (wave.EventInfo & 0x1F) == 1
                  && wave.LoopCount == 255
                  && wave.TrackIndex == trackIndex;

        Console.WriteLine($"  插入循环 Cue「{cueName}」→ track {trackIndex}");
        Console.WriteLine($"    简单 Cue：{(entry == null ? "**没有** ✗" : $"Flags=0x{entry.Flags:X2} SoundOffset={entry.SoundOffset} ✓")}");
        Console.WriteLine($"    Sound：{(sound == null ? "**没有** ✗" : $"Flags=0x{sound.Flags:X2} NumClips={sound.NumClips}")}");
        Console.WriteLine($"    事件：{(wave == null ? "**没有** ✗" : $"类型={wave.EventInfo & 0x1F} LoopCount={wave.LoopCount} "
            + $"Bank={wave.WaveBankIndex} Track={wave.TrackIndex}")}");
        Console.WriteLine($"    总 Cue {bank.Header.NumTotalCues} → {check.Header.NumTotalCues}，"
                          + $"简单 Cue {bank.Header.NumSimpleCues}；文件 {new FileInfo(input).Length} → {rebuilt.Length} 字节"
                          + $"  {(ok ? "读回 ✓" : "读回 ✗")}");
        return ok ? 0 : 1;
    }
}
