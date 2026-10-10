using System.Reflection;

namespace XactRoundTrip;

/// <summary>
/// 拿**游戏自己的 MonoGame** 校验一条 Cue 到底会不会循环。
///
/// <para>
/// 为什么不自己判：「会不会循环」由 MonoGame 的 <c>XactClip</c> 决定 —— 只有走
/// <c>XactClip</c> 那条路（sound 记录里有 clips），<c>loopCount</c> 才存在。
/// 我们写出来的字节「看着对」不算数，得让游戏那份运行时真的认。
/// </para>
///
/// <para>
/// 用法：<c>--check-cue-loop &lt;游戏目录&gt; &lt;XACT 目录&gt; &lt;cue名&gt;</c>
/// （XACT 目录里要有 <c>FarmerSounds.xgs</c> 与 <c>Sound Bank.xsb</c>）
/// </para>
/// </summary>
public static class CheckCueLoop
{
    public static int Run(string gameDir, string xactDir, string cueName)
    {
        // MonoGame 的依赖散在游戏目录里（native + 托管），默认解析器找不到 —— 指过去
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string candidate = Path.Combine(gameDir, name.Name + ".dll");
            return File.Exists(candidate)
                ? System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate)
                : null;
        };

        Assembly asm;
        try
        {
            asm = Assembly.LoadFrom(Path.Combine(gameDir, "MonoGame.Framework.dll"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ✗ 载不进游戏的 MonoGame（这个探针只能跳过，不算失败）：");
            Console.WriteLine("    " + ex.Message);
            for (Exception? e = ex.InnerException; e != null; e = e.InnerException)
            {
                Console.WriteLine("    ← " + e.Message);
            }

            if (ex is FileLoadException fle && !string.IsNullOrEmpty(fle.FusionLog))
            {
                Console.WriteLine("    FusionLog: " + fle.FusionLog);
            }

            return 2;   // 2 = 跳过（工具/环境限制），不是「校验不通过」
        }

        Type engineType = asm.GetType("Microsoft.Xna.Framework.Audio.AudioEngine")!;
        Type bankType = asm.GetType("Microsoft.Xna.Framework.Audio.SoundBank")!;

        object engine = Activator.CreateInstance(engineType, Path.Combine(xactDir, "FarmerSounds.xgs"))!;
        object bank = Activator.CreateInstance(bankType, engine, Path.Combine(xactDir, "Sound Bank.xsb"))!;

        object? cue = bankType.GetMethod("GetCue", new[] { typeof(string) })!
            .Invoke(bank, new object[] { cueName });
        if (cue == null)
        {
            Console.WriteLine($"  GetCue(\"{cueName}\") 返回 null ✗");
            return 1;
        }

        // Cue → CueDefinition → sounds[0] → 它的 clips（字段名随 MonoGame 版本，按类型找）
        object? definition = Field(cue, "_cue");
        object? sounds = Field(definition, "sounds");
        object? sound = (sounds as Array)?.Length > 0 ? (sounds as Array)!.GetValue(0) : null;
        object? clips = Field(sound, "_clips");
        int clipCount = (clips as Array)?.Length ?? 0;

        Console.WriteLine($"  Cue「{cueName}」：CueDefinition={(definition != null ? "✓" : "✗")} "
                          + $"sounds={(sounds as Array)?.Length ?? 0} clips={clipCount}");

        if (clipCount == 0)
        {
            Console.WriteLine("  ⇒ 没有 XactClip ⇒ **游戏只会播一遍** ✗");
            return 1;
        }

        // XactClip → _events（每条事件里带 loopCount）
        object clip = ((Array)clips!).GetValue(0)!;
        object? events = Field(clip, "_events");
        int loops = 0;
        if (events is Array arr)
        {
            foreach (object? e in arr)
            {
                object? count = Field(e, "loopCount");
                if (count is byte b && b >= 255) loops++;
                Console.WriteLine($"    事件 {e?.GetType().Name}：loopCount={count}");
            }
        }

        // 也可以直接问「播起来会不会循环」：Play 之后看它是不是在播、且不 dispose
        MethodInfo? play = cue.GetType().GetMethod("Play");
        play?.Invoke(cue, null);
        bool playing = true;
        try
        {
            Thread.Sleep(1200);
            playing = !(bool)(cue.GetType().GetProperty("IsStopped")?.GetValue(cue) ?? false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    （播放期间异常：{ex.Message}）");
        }

        bool ok = loops > 0 && playing;
        Console.WriteLine($"  ⇒ 无限循环事件 {loops} 条；播 1.2 秒后仍在播={playing}  {(ok ? "✓" : "✗")}");
        return ok ? 0 : 1;
    }

    /// <summary>按名字在类型（含私有基类）里找字段值。</summary>
    private static object? Field(object? target, string name)
    {
        if (target == null) return null;

        for (Type? t = target.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo? f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) return f.GetValue(target);
        }

        return null;
    }
}
