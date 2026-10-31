// GSQ 语法实跑验证：把翻译器产出的 GameStateQuery 字符串喂给游戏自己的
// StardewValley.GameStateQuery.Parse，报告解析失败或解析出 0 条 query 的条目。
//
// 独立工具，有意不进 SdvCpTool.sln —— 它引用游戏程序集（Stardew Valley.dll），
// 依赖游戏安装目录，不能进 CI。
using System.Text;
using System.Text.Json;
using StardewValley;

Console.OutputEncoding = Encoding.UTF8;

string path = args.Length > 0 ? args[0] : @"D:\sdv_translate.json";
if (!File.Exists(path))
{
    Console.Error.WriteLine($"找不到输入文件：{path}");
    return 2;
}

var samples = new List<(string Gsq, string Source, string When)>();
if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
    {
        if (entry.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
            entry.TryGetProperty("gsq", out var gsq) && gsq.ValueKind == JsonValueKind.String)
        {
            samples.Add((
                gsq.GetString()!,
                entry.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "",
                entry.TryGetProperty("when", out var w) ? w.GetString() ?? "" : ""));
        }
    }
}
else
{
    // 纯文本模式：每行一条 GSQ 字符串（忽略空行与 # 注释）。
    foreach (var line in File.ReadLines(path))
    {
        var t = line.Trim();
        if (t.Length > 0 && !t.StartsWith('#'))
        {
            samples.Add((t, "(raw)", ""));
        }
    }
}

Console.WriteLine($"输入：{path}");
Console.WriteLine($"成功的 GSQ 条目：{samples.Count} 条（去重后 {samples.Select(x => x.Gsq).Distinct().Count()} 条）");
Console.WriteLine();

int pass = 0;
var failures = new List<(string Gsq, string Error, string Source)>();
var empty = new List<(string Gsq, string Source)>();
var thrown = new List<(string Gsq, string Ex, string Source)>();

foreach (var (gsq, source, _) in samples)
{
    try
    {
        var parsed = GameStateQuery.Parse(gsq);
        string? error = parsed.FirstOrDefault(p => p.Error != null).Error;
        if (error != null)
        {
            failures.Add((gsq, error, source));
        }
        else if (parsed.Length == 0)
        {
            empty.Add((gsq, source));
        }
        else
        {
            pass++;
        }
    }
    catch (Exception ex)
    {
        thrown.Add((gsq, ex.GetType().FullName + ": " + ex.Message, source));
    }
}

Console.WriteLine("================ 结果 ================");
Console.WriteLine($"通过（解析出 >=1 条 query 且无 Error）：{pass}");
Console.WriteLine($"解析报错（Error != null）           ：{failures.Count}");
Console.WriteLine($"解析出 0 条 query                  ：{empty.Count}");
Console.WriteLine($"调用抛异常                         ：{thrown.Count}");
Console.WriteLine();

if (failures.Count > 0)
{
    Console.WriteLine("---- 解析报错（去重）----");
    foreach (var (gsq, error, _) in failures.DistinctBy(x => x.Gsq))
        Console.WriteLine($"  GSQ : {gsq}\n  ERR : {error}\n");
}

if (empty.Count > 0)
{
    Console.WriteLine("---- 解析出 0 条 query（去重）----");
    foreach (var (gsq, _) in empty.DistinctBy(x => x.Gsq))
        Console.WriteLine($"  GSQ : {gsq}");
    Console.WriteLine();
}

if (thrown.Count > 0)
{
    Console.WriteLine("---- 调用抛异常（去重）----");
    foreach (var (gsq, ex, _) in thrown.DistinctBy(x => x.Gsq))
        Console.WriteLine($"  GSQ : {gsq}\n  EX  : {ex}\n");
}

return (failures.Count + empty.Count + thrown.Count) == 0 ? 0 : 1;
