using System.Diagnostics;

namespace SdvCpTool.Core.Diagnostics;

/// <summary>
/// 临时分段计时（`SDVCP_PROFILE=1` 才开）。
/// <para>
/// **粗粒度（整段）用 CPU 时间** —— 开发机噪声大到同一份二进制墙钟能差 65%（135s vs 82s），
/// 整轮数字只能看 CPU 时间。
/// </para>
/// <para>
/// ⚠ **细粒度（单条补丁里的那几个动作）必须用高精度计时器**，别用
/// <see cref="Process.TotalProcessorTime"/>：它的粒度是系统时钟节拍（这台机器 15.6 ms），
/// 一次 3 ms 的合并会被记成 0 或 15 ms —— 一轮语料 2400 多次这种取样，
/// 归出来的「哪个动作贵」纯属噪声（2026-10-10 实测同一份二进制两次跑，
/// 同一个包同一项能差 60%）。走 <see cref="Now"/>/<see cref="AddSince"/>。
/// </para>
/// </summary>
public static class Prof
{
    public static bool On => Environment.GetEnvironmentVariable("SDVCP_PROFILE") != null;

    private static readonly Dictionary<string, (long Ms, long N)> Stats = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> AllocStats = new(StringComparer.Ordinal);
    private static readonly object StatsLock = new();

    /// <summary>高精度计时起点（<see cref="Stopwatch.GetTimestamp"/>）。</summary>
    public static long Now() => Stopwatch.GetTimestamp();

    /// <summary>分配量起点（<see cref="GC.GetTotalAllocatedBytes"/>）。</summary>
    public static long AllocNow() => GC.GetTotalAllocatedBytes(precise: false);

    /// <summary>同时记时长与分配量：GC 的线程 CPU 会被算进阶段耗时，只有分配量能指出真正的来源。</summary>
    public static void AddSinceAlloc(string key, long tickStart, long allocStart)
    {
        if (!On) return;
        AddSince(key, tickStart);
        long delta = GC.GetTotalAllocatedBytes(precise: false) - allocStart;
        lock (StatsLock)
            AllocStats[key] = AllocStats.GetValueOrDefault(key) + delta;
    }

    /// <summary>把「从 <paramref name="start"/> 到现在」记进 <paramref name="key"/>。</summary>
    public static void AddSince(string key, long start) =>
        Add(key, (long)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency));

    public static void Add(string key, long ms)
    {
        lock (StatsLock)
        {
            (long, long) s = Stats.GetValueOrDefault(key);
            Stats[key] = (s.Item1 + ms, s.Item2 + 1);
        }
    }

    public static void Dump()
    {
        // 只打前 30：`i18n资产[...]` 这类键按资产名累积，全量跑下来上万条，
        // 每包重打一遍既淹没有用信息、又把重定向输出拖成瓶颈。
        KeyValuePair<string, (long Ms, long N)>[] top;
        lock (StatsLock)
            top = Stats.OrderByDescending(k => k.Value.Ms).Take(30).ToArray();

        foreach (var kv in top)
        {
            long bytes;
            lock (StatsLock) bytes = AllocStats.GetValueOrDefault(kv.Key);
            Console.Error.WriteLine($"[prof2] {kv.Key,-26} {kv.Value.Ms,8} ms / {kv.Value.N,6} 次"
                                    + (bytes > 0 ? $" / {bytes / (1024 * 1024),7} MB" : ""));
        }
    }
}
