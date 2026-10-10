using XnbConverter.Readers;
using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;

namespace XactRoundTrip;

/// <summary>
/// Clip 的**字节级往返**：把原版某个 Cue 的 Clip 用 <see cref="XactClipReader.Write"/> 写回原位，
/// 与原文件逐字节比对。
///
/// <para>
/// 为什么要这么较真：要让注入的音乐循环，就得自己写一条「简单 Cue → 带 Clip 的 Sound」，
/// 而 Clip 的字节布局（事件的未知字节、角度/随机量的定标）光看反编译容易看漏一位。
/// 这条检验拿原版做参照物 —— 写不出一模一样的字节，就说明布局没吃透。
/// </para>
///
/// <para>用法：<c>--clip-roundtrip &lt;Sound Bank.xsb&gt; &lt;Cue 名&gt;</c></para>
/// </summary>
public static class ClipRoundTrip
{
    /// <summary>类型 1（单条播放）事件的字节数：EventInfo(4)+RandomOffset(2)+未知(1)+EventFlags(1)+Track(2)+Bank(1)+Loop(1)+Pan(2)+Arc(2)。</summary>
    private const int SingleTrackEventSize = 16;

    public static int Run(string xsbPath, string cueName)
    {
        SoundBank sb = SoundBankReader.Read(xsbPath);
        if (!sb._sounds.TryGetValue(cueName, out XactSound[]? sounds))
        {
            Console.WriteLine($"找不到 Cue「{cueName}」");
            return 1;
        }

        int checkedClips = 0;
        int bad = 0;
        foreach (XactSound sound in sounds)
        {
            foreach (XactClip? clip in sound.SoundClips ?? Array.Empty<XactClip>())
            {
                if (clip == null) continue;

                int events = clip.WaveIndexs?.Length ?? 0;
                int regionLen = 9 + 1 + events * SingleTrackEventSize;
                int start = (int)clip.ClipOffset - 9;

                byte[] buffer = (byte[])sb.OriginalBytes!.Clone();
                var writer = new BufferWriter(buffer);
                var reader = new XactClipReader();
                reader.Init(new ReaderResolver { bufferWriter = writer });
                writer.BytePosition = start;
                reader.Write(clip);

                bool same = true;
                for (int i = 0; i < regionLen; i++)
                {
                    if (buffer[start + i] != sb.OriginalBytes[start + i]) { same = false; break; }
                }

                Console.WriteLine($"  Clip @{clip.ClipOffset}（{events} 个事件、{regionLen} 字节）："
                                  + (same ? "逐字节相同 ✓" : "**写回去和原文件不一样 ✗**"));
                if (!same)
                {
                    Console.WriteLine($"    原：{Convert.ToHexString(sb.OriginalBytes, start, Math.Min(regionLen, 32))}");
                    Console.WriteLine($"    新：{Convert.ToHexString(buffer, start, Math.Min(regionLen, 32))}");
                    bad++;
                }

                checkedClips++;
            }
        }

        Console.WriteLine($"检查了 {checkedClips} 条 Clip，不一致 {bad} 条");
        return bad;
    }
}
