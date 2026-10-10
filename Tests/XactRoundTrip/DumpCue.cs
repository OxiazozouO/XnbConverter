using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;

namespace XactRoundTrip;

/// <summary>
/// 把某个 Cue 在 <c>.xsb</c> 里的真实结构打出来 —— 「怎么才能让它循环」这件事要照着实例抄，
/// 不能凭猜。用法：<c>--dump-cue &lt;Sound Bank.xsb&gt; &lt;Cue 名&gt;</c>
/// </summary>
public static class DumpCue
{
    public static int Run(string xsbPath, string cueName)
    {
        SoundBank sb = SoundBankReader.Read(xsbPath);

        bool simple = sb.SoundEntrys.Any(e => true) && sb._sounds.ContainsKey(cueName)
                      && sb.Cues.All(c => c.Name != cueName);

        Console.WriteLine($"Cue「{cueName}」");
        Console.WriteLine($"  形式：{(sb.Cues.Any(c => c.Name == cueName) ? "复杂 Cue" : "简单 Cue")}");
        Console.WriteLine($"  头部：NumSimpleCues={sb.Header.NumSimpleCues} NumComplexCues={sb.Header.NumComplexCues} "
                          + $"NumSounds={sb.Header.NumSounds} NumTotalCues={sb.Header.NumTotalCues}");

        Console.WriteLine($"  简单 Cue 前 3 个名字：{string.Join(" / ", sb.SoundEntrys.Take(3).Select(e => e.Name))}");
        SoundBank.SoundEntry? entry = sb.SoundEntrys.FirstOrDefault(e => e.Name == cueName);
        if (entry != null)
        {
            Console.WriteLine($"  SoundEntry：Flags=0x{entry.Flags:X2} SoundOffset={entry.SoundOffset} "
                              + $"FileOffset={entry.FileOffset}");
        }

        // 内联形式的复杂 Cue **没有名字**（Read 那段没填），只能按 SoundOffset 反查
        uint? soundAt = sb._sounds.TryGetValue(cueName, out XactSound[]? ss) && ss.Length > 0
            ? ss[0]?.FileOffset : null;
        SoundBank.SoundCue? complex = sb.Cues.FirstOrDefault(c => c.Name == cueName)
            ?? (soundAt.HasValue
                ? sb.Cues.FirstOrDefault(c => ((c.Flags >> 2) & 1) != 0 && c.SoundOffset == soundAt.Value)
                : null);
        if (complex != null)
        {
            Console.WriteLine($"  SoundCue.Flags=0x{complex.Flags:X2} 内联Sound={(complex.Flags >> 2) & 1} "
                              + $"NumEntries={complex.NumEntries} VariationFlags=0x{complex.VariationFlags:X2} "
                              + $"变体类型={(complex.VariationFlags >> 3) & 7}");
            foreach (SoundBank.SoundCue.CueVariation v in complex.CueVariations)
                Console.WriteLine($"    变体：WaveBankIndex={v.WaveBankIndex} TrackIndex={v.TrackIndex} "
                                  + $"Flags=0x{v.Flags:X8} SoundOffset={v.SoundOffset}");
        }

        {
            uint? at = sb._sounds.TryGetValue(cueName, out XactSound[]? probe) && probe.Length > 0
                ? probe[0]?.FileOffset : null;
            Console.WriteLine($"  Sound 记录偏移：{at}");
            foreach (SoundBank.SoundCue c in sb.Cues.Where(c => ((c.Flags >> 2) & 1) != 0).Take(4))
            {
                Console.WriteLine($"    内联复杂 Cue：Name={c.Name ?? "(无名)"} Flags=0x{c.Flags:X2} SoundOffset={c.SoundOffset}");
            }
        }

        if (!sb._sounds.TryGetValue(cueName, out XactSound[]? sounds))
        {
            Console.WriteLine("  （没有对应的 Sound 记录）");
            return 1;
        }

        foreach (XactSound s in sounds)
        {
            Console.WriteLine($"  Sound @{s.FileOffset}：Flags=0x{s.Flags:X2} CategoryId={s.CategoryId} "
                              + $"VolumeFlag=0x{s.VolumeFlag:X2} Pitch={s.Pitch} Priority={s.Priority} Filter={s.Filter}");
            if ((s.Flags & 1) == 0)
            {
                Console.WriteLine($"    直接引用波形：WaveBankIndex={s.WaveBankIndex} TrackIndex={s.TrackIndex}");
                continue;
            }

            Console.WriteLine($"    NumClips={s.NumClips}");
            foreach (XactClip? clip in s.SoundClips ?? Array.Empty<XactClip>())
            {
                if (clip == null) { Console.WriteLine("    （clip 为空）"); continue; }
                Console.WriteLine($"    Clip @{clip.ClipOffset}：VolumeDb={clip.VolumeDb} "
                                  + $"FilterQAndFlags=0x{clip.FilterQAndFlags:X4} FilterFrequency={clip.FilterFrequency} "
                                  + $"Events={clip.WaveIndexs?.Length ?? 0}");
                foreach (XactClip.WaveIndex w in clip.WaveIndexs ?? Array.Empty<XactClip.WaveIndex>())
                {
                    uint type = w.EventInfo & 0x1F;
                    Console.WriteLine($"      事件类型={type} EventInfo=0x{w.EventInfo:X8} EventFlags=0x{w.EventFlags:X2} "
                                      + $"LoopCount={w.LoopCount} WaveBankIndex={w.WaveBankIndex} TrackIndex={w.TrackIndex} "
                                      + $"NumTracks={w.NumTracks} RandomOffset={w.RandomOffset} "
                                      + $"PanAngle={w.PanAngle} PanArc={w.PanArc} "
                                      + $"未知字节={string.Join(",", w.unkn.Select(u => u[0]))}");
                }
            }
        }

        return 0;
    }
}
