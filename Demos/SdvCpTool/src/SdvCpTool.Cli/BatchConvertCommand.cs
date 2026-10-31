using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Cli;

/// <summary>
/// C# 进程内批量转换：按 Content Patcher 的两趟语义，先跑所有包的 Load，
/// 再跑所有包的 Edit，共用一次进程和 Mods 索引。
/// <para>
/// Python 的全量脚本只是开发编排器；真正的大量进程启动成本不能留在产品路径里。
/// 这里直接复用 CLI 的 <c>convert</c> 入口（top-level Program 的 Main），
/// 不改变单包转换语义，只把进程边界移到整批之外。
/// </para>
/// </summary>
internal static class BatchConvertCommand
{
    private sealed record Pack(string Name, string Uid, string Path, List<string> Deps, bool Ftm);

    public static int Run(string[] args)
    {
        string? mods = null, tree = null, outRoot = null, hashes = null;
        bool bakeFlips = true, bakeFrameworks = false, dryRun = false, noAudit = false;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mods": if (++i < args.Length) mods = args[i]; break;
                case "--tree": if (++i < args.Length) tree = args[i]; break;
                case "--out": if (++i < args.Length) outRoot = args[i]; break;
                case "--content-hashes": if (++i < args.Length) hashes = args[i]; break;
                case "--no-bake-flips": bakeFlips = false; break;
                case "--bake-frameworks": bakeFrameworks = true; break;
                case "--dry-run": dryRun = true; break;
                case "--no-audit": noAudit = true; break;
            }
        }
        if (mods == null || tree == null || outRoot == null)
        {
            Console.Error.WriteLine(
                "用法：SdvCpTool.Cli convert-batch --mods <Mods> --tree <原版树> --out <共享树> " +
                "[--content-hashes <文件>] [--bake-frameworks] [--no-bake-flips] [--no-audit]");
            return 2;
        }
        if (!Directory.Exists(mods) || !Directory.Exists(tree))
        {
            Console.Error.WriteLine("convert-batch：--mods 或 --tree 不存在");
            return 2;
        }

        Directory.CreateDirectory(outRoot);
        List<Pack> packs = LoadOrderedPacks(mods);
        // 调优用：`SDVCP_ONLY_PACKS=Downtown Zuzu,Expanded` 只跑名字含这些子串的包
        // （包发现/排序仍照全量走，HasMod 等条件也按**完整** Mods 目录求值 —— 不会改变任务本身）。
        string? only = Environment.GetEnvironmentVariable("SDVCP_ONLY_PACKS");
        if (!string.IsNullOrWhiteSpace(only))
        {
            string[] keys = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            packs = packs.Where(p => keys.Any(k => p.Name.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        if (packs.Count == 0)
        {
            Console.Error.WriteLine("convert-batch：没有找到 CP/FTM 内容包");
            return 2;
        }

        if (dryRun)
        {
            for (int i = 0; i < packs.Count; i++) Console.WriteLine($"{i + 1}	{packs[i].Name}	{packs[i].Path}");
            return 0;
        }

        // 同一进程内共享产出树索引：每包不再重扫 17k 份文件。
        // 允许调用方预先设为 0 做 A/B（不覆盖已有值）。
        if (Environment.GetEnvironmentVariable("SDVCP_SHARED_TREE_INDEX") == null)
            Environment.SetEnvironmentVariable("SDVCP_SHARED_TREE_INDEX", "1");
        // 自检缓存同样只在进程内保留、退出时落盘一次；否则每包读+写 ~2 MB JSON。
        if (Environment.GetEnvironmentVariable("SDVCP_SHARED_AUDIT_CACHE") == null)
            Environment.SetEnvironmentVariable("SDVCP_SHARED_AUDIT_CACHE", "1");
        if (noAudit) Environment.SetEnvironmentVariable("SDVCP_SKIP_AUDITS", "1");
        MethodInfo main = typeof(BatchConvertCommand).Assembly.GetType("Program")!
            .GetMethod("<Main>$", BindingFlags.Static | BindingFlags.NonPublic)!;
        string modIndex = Path.Combine(outRoot, ".mod-index.json");
        // 报告放在 outRoot，产出树必须是 outRoot/tree：OutputTree 用“目录是否非空”
        // 判断 append；报告目录若和产出树同根，会让首包误判为续写、不复制原版树。
        string shared = Path.Combine(outRoot, "tree");
        var failed = new List<string>();
        var total = Stopwatch.StartNew();

        foreach ((string phase, string reportName) in new[] { ("loads", "report.loads.json"), ("edits", "report.json") })
        {
            // Loads 不溢写：1,252 张地图多写临时文件会把加载拖慢 ~78 s；
            // Edits 对 ≥2 MB 载荷溢写：否则活跃地图留在 LOH，SVE 编辑会从 ~57 s 涨到 ~188 s。
            Environment.SetEnvironmentVariable("SDVCP_SPILL_MB", phase == "edits" ? "2" : "0");
            Console.WriteLine($"=== 第 {(phase == "loads" ? 1 : 2)} 趟：{phase}（{packs.Count} 包）===");
            for (int i = 0; i < packs.Count; i++)
            {
                Pack pack = packs[i];
                string dir = Path.Combine(outRoot, $"{i + 1:00}_{SafeName(pack.Name, 26)}");
                Directory.CreateDirectory(dir);
                string report = Path.Combine(dir, reportName);
                string treeArg = phase == "loads" && i == 0 ? tree : shared;
                var cmd = new List<string>
                {
                    "convert", "--pack", pack.Path, "--tree", treeArg, "--out", shared,
                    "--mods", mods, "--report", report, "--pass", phase, "--defer-flips",
                    "--mod-index-cache", modIndex,
                };
                if (hashes != null && File.Exists(hashes)) cmd.AddRange(new[] { "--content-hashes", hashes });
                Stopwatch sw = Stopwatch.StartNew();
                int rc = Invoke(main, cmd.ToArray());
                Console.WriteLine($"[{phase} {i + 1}/{packs.Count}] {pack.Name} exit={rc} {sw.ElapsedMilliseconds}ms");
                if (rc != 0) failed.Add($"{pack.Name}（{phase}）");
            }
        }

        if (bakeFlips)
        {
            Console.WriteLine("=== bake-flips ===");
            int rc = FlipBakeCommand.Run(new[] { "bake-flips", "--tree", shared });
            if (rc != 0) failed.Add("bake-flips");
        }
        if (bakeFrameworks)
        {
            Console.WriteLine("=== bake-frameworks ===");
            int rc = FrameworkCommand.Run(new[] { "bake-frameworks", "--tree", shared, "--mods", mods });
            if (rc != 0) failed.Add("bake-frameworks");
        }

        total.Stop();
        // 墙钟受机器噪声影响可达 3 倍；CPU 时间才是单线程任务的可比口径。
        // 分配量与 Gen2 次数一起打出来：GC 的线程 CPU 会被算进各阶段的 Process.TotalProcessorTime，
        // 只看阶段数字会把「GC 抖动」误读成某个业务阶段变慢。
        double cpu = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
        double allocated = GC.GetTotalAllocatedBytes(precise: false) / (1024.0 * 1024 * 1024);
        Console.WriteLine($"convert-batch 完成：{packs.Count} 包 ×2 趟，{total.Elapsed.TotalSeconds:F1}s"
                          + $"，CPU {cpu:F1}s，分配 {allocated:F1} GB，Gen2 {GC.CollectionCount(2)} 次"
                          + (failed.Count == 0 ? "" : $"；非 0：{string.Join("、", failed.Take(20))}"));
        return failed.Count == 0 ? 0 : 1;
    }

    private static int Invoke(MethodInfo main, string[] args)
    {
        try { return (int)main.Invoke(null, new object[] { args })!; }
        catch (TargetInvocationException ex)
        {
            Exception inner = ex.InnerException ?? ex;
            Console.Error.WriteLine($"转换异常：{inner.GetType().Name}: {inner.Message}");
            return 2;
        }
    }

    private static List<Pack> LoadOrderedPacks(string mods)
    {
        var all = new List<Pack>();
        var cpUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string manifestPath in Directory.EnumerateFiles(mods, "manifest.json",
                     SearchOption.AllDirectories))
        {
            JObject m;
            try { m = JsoncReader.ParseFile(manifestPath); }
            catch { continue; }
            string? contentFor = (m["ContentPackFor"] as JObject)?.Value<string>("UniqueID");
            string fw = (contentFor ?? "").ToLowerInvariant();
            bool isCp = fw.StartsWith("pathoschild.contentpatcher", StringComparison.Ordinal);
            bool isFtm = fw == "esca.farmtypemanager";
            if (!isCp && !isFtm) continue;
            string path = Path.GetDirectoryName(manifestPath)!;
            string name = m.Value<string>("Name") ?? Path.GetFileName(path);
            string uid = m.Value<string>("UniqueID") ?? name;
            var deps = new List<string>();
            var required = new List<string>();
            foreach (JObject d in (m["Dependencies"] as JArray)?.OfType<JObject>()
                     ?? Enumerable.Empty<JObject>())
            {
                string? dep = d.Value<string>("UniqueID");
                if (dep == null) continue;
                deps.Add(dep);
                string? req = d.Value<string>("IsRequired");
                if (!string.Equals(req, "false", StringComparison.OrdinalIgnoreCase))
                    required.Add(dep);
            }
            all.Add(new Pack(name, uid, path, deps, isFtm));
            if (isCp) cpUids.Add(uid);
        }

        // FTM 包的隐式前置：同一父目录下的 CP 包（与 convert-mods.py 一致）。
        foreach (Pack ftm in all.Where(p => p.Ftm))
        {
            string parent = Path.GetDirectoryName(ftm.Path) ?? ftm.Path;
            foreach (Pack cp in all.Where(p => !p.Ftm &&
                         string.Equals(Path.GetDirectoryName(p.Path), parent,
                             StringComparison.OrdinalIgnoreCase)))
                if (!ftm.Deps.Contains(cp.Uid, StringComparer.Ordinal)) ftm.Deps.Add(cp.Uid);
        }

        var ftmUids = new HashSet<string>(all.Where(p => p.Ftm).Select(p => p.Uid),
            StringComparer.Ordinal);
        foreach (Pack p in all.Where(p => !p.Ftm))
            p.Deps.RemoveAll(d => ftmUids.Contains(d));

        var byUid = all.Where(p => p.Uid.Length > 0)
            .GroupBy(p => p.Uid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var ordered = new List<Pack>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        void Put(Pack p)
        {
            string key = p.Uid.Length > 0 ? p.Uid : p.Name;
            if (placed.Contains(key) || active.Contains(key)) return;
            active.Add(key);
            foreach (string dep in p.Deps.OrderBy(x => x, StringComparer.Ordinal))
                if (byUid.TryGetValue(dep, out Pack? d)) Put(d);
            active.Remove(key);
            if (placed.Add(key)) ordered.Add(p);
        }
        foreach (Pack p in all.OrderBy(p => p.Name.ToLowerInvariant(), StringComparer.Ordinal)
                     .ThenBy(p => p.Name, StringComparer.Ordinal)) Put(p);
        return ordered;
    }

    private static string SafeName(string name, int limit)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' '
            or '[' or ']' or '(' or ')' ? c : '_').ToArray();
        string s = new string(chars).Trim();
        if (s.Length > limit) s = s[..limit].TrimEnd();
        return s;
    }
}
