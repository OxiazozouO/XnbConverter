using System.Diagnostics;
using XnbConverter;
using XnbConverter.Cli.Configurations;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;

// 单线程、逐阶段计时 + 分配统计的性能剖析宿主。
// 用法: XnbProf <unpack|pack> <输入目录> <输出目录> [工作目录]

// 波形库导出：XNB 之外的另一条路径，单独量分配。
// 用法: XnbProf xwb <xwb 文件> <输出目录>
if (args.Length >= 3 && args[0] == "xwb")
{
    ConsoleLogger.Build();
    string xwbPath = Path.GetFullPath(args[1]);
    string xwbDest = Path.GetFullPath(args[2]);
    List<(string, string)> xwbFiles = new() { (xwbPath, xwbDest) };
    xwbFiles.CreateDirectory();

    long w0 = GC.GetTotalAllocatedBytes(precise: true);
    Stopwatch wsw = Stopwatch.StartNew();
    XACT.Load(xwbFiles)?.Save();
    wsw.Stop();
    long wAlloc = GC.GetTotalAllocatedBytes(precise: true) - w0;
    Console.WriteLine($"xwb {Path.GetFileName(xwbPath)}  {wsw.Elapsed.TotalMilliseconds:F1} ms  分配 {wAlloc / 1048576.0:F1} MB");
    return;
}

if (args.Length < 3)
{
    Console.WriteLine("用法: XnbProf <unpack|pack> <输入目录> <输出目录> [工作目录]");
    Console.WriteLine("     XnbProf xwb <xwb 文件> <输出目录>");
    return;
}

string mode = args[0];
string input = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[2]);
string home = args.Length > 3 ? Path.GetFullPath(args[3]) : @"D:\XnbConverter";

Environment.CurrentDirectory = home;
ConsoleLogger.Build();
Directory.CreateDirectory(output);

List<string> files = Directory.EnumerateFiles(input, mode == "pack" ? "*.config" : "*.xnb",
    SearchOption.AllDirectories).OrderBy(p => p).ToList();
Console.WriteLine($"{(mode == "pack" ? "打包" : "解包")}  {files.Count} 个文件  输入 {input}");

// 先跑几个把 JIT 捂热，免得把编译时间算进第一阶段
foreach (string f in files.Take(Math.Min(10, files.Count)))
{
    _ = One(f, null);
}

Sink sink = new();
int failed = 0;

long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
long PeakHeap = 0;
Stopwatch wall = Stopwatch.StartNew();

foreach (string file in files)
{
    if (!One(file, sink))
    {
        failed++;
    }

    PeakHeap = Math.Max(PeakHeap, GC.GetTotalMemory(forceFullCollection: false));
}

wall.Stop();
long alloc = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;
double sec = wall.Elapsed.TotalSeconds;
int done = files.Count - failed;

Console.WriteLine();
sink.Parse.Report(done);
sink.Write.Report(done);
sink.Release.Report(done);
Console.WriteLine($"\n总计 {sec:F2} s   分配 {alloc / 1048576.0:F0} MB   失败 {failed}");
Console.WriteLine($"吞吐 {done / sec:F1} 文件/秒");

// 内存足迹：托管堆峰值（含池化但不含外部分配的数组）+ 进程峰值工作集。
// 分配量（GetTotalAllocatedBytes）对「借了又还的池缓冲」不敏感，要看内存得看这两个。
Process self = Process.GetCurrentProcess();
self.Refresh();
Console.WriteLine($"托管堆峰值 {PeakHeap / 1048576.0:F0} MB   进程峰值工作集 {self.PeakWorkingSet64 / 1048576.0:F0} MB");
Console.WriteLine($"GC gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}"
    + $" 暂停合计={GC.GetTotalPauseDuration().TotalMilliseconds:F0} ms"
    + $" 线程CPU={self.TotalProcessorTime.TotalSeconds:F1} s");

Console.WriteLine("\n按类型:");
foreach (KeyValuePair<string, Stage> kv in sink.ByKind.OrderByDescending(k => k.Value.Nanos))
{
    Console.WriteLine($"  {kv.Key,-12} {kv.Value.Nanos / 1e6,9:F1} ms   {kv.Value.Count,5} 个");
}

return;

bool One(string file, Sink? sink)
{
    // 输出一律落在 output 目录下，保持相对结构 —— 绝不能写回输入目录
    string rel = Path.GetRelativePath(input, file);
    string outPath = Path.Combine(output, Path.ChangeExtension(rel, mode == "pack" ? ".xnb" : ".config"));
    XNB? xnb = null;
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        long t0 = Stopwatch.GetTimestamp();
        long a0 = GC.GetTotalAllocatedBytes(precise: true);
        xnb = new XNB();
        if (mode == "pack")
        {
            xnb.ImportFiles(file);
        }
        else
        {
            xnb.Decode(file);
        }

        long t1 = Stopwatch.GetTimestamp();
        long a1 = GC.GetTotalAllocatedBytes(precise: true);
        string kind = xnb.XnbConfig?.Content?.Extension ?? "?";

        if (mode == "pack")
        {
            xnb.Encode(outPath);
        }
        else
        {
            xnb.ExportFiles(outPath);
            Texture2D.WaitAll(); // 导出的 PNG 编码丢给了线程池，等它落盘这段才算结束
        }

        long t2 = Stopwatch.GetTimestamp();
        long a2 = GC.GetTotalAllocatedBytes(precise: true);

        xnb.Dispose();
        xnb = null;
        long t3 = Stopwatch.GetTimestamp();

        sink?.Parse.Add((t0, t1), (a0, a1));
        sink?.Write.Add((t1, t2), (a1, a2));
        sink?.Release.Add((t2, t3), (a2, a2));
        sink?.ByKind0(kind).Add((t0, t2), (a0, a2));
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  失败 {Path.GetFileName(file)}: {ex.GetType().Name}: {ex.Message}");
        xnb?.Dispose();
        return false;
    }
}

internal sealed class Sink
{
    public readonly Stage Parse = new("解析/解压");
    public readonly Stage Write = new("写出/压缩");
    public readonly Stage Release = new("Dispose");
    public readonly Dictionary<string, Stage> ByKind = new();

    public Stage ByKind0(string kind)
    {
        if (!ByKind.TryGetValue(kind, out Stage? s))
        {
            ByKind[kind] = s = new Stage(kind);
        }

        return s;
    }
}

internal sealed class Stage(string name)
{
    private long _nanos;
    private long _bytes;

    public long Nanos => _nanos;
    public int Count { get; private set; }

    public void Add((long From, long To) span, (long From, long To) alloc)
    {
        _nanos += (span.To - span.From) * (1_000_000_000L / Stopwatch.Frequency);
        _bytes += alloc.To - alloc.From;
        Count++;
    }

    public void Report(int files)
    {
        Console.WriteLine($"  {name,-10} {_nanos / 1e6,9:F1} ms   分配 {_bytes / 1048576.0,7:F0} MB"
            + $"   {_nanos / 1e6 / Math.Max(files, 1),7:F3} ms/文件");
    }
}
