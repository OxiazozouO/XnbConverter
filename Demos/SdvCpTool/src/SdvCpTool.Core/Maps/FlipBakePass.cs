using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 全树翻转烘焙：**所有内容包都写完之后**再跑一次。
/// <para>
/// 链式转换里每个包是独立进程，图集可能被后面的包整张 <c>Load</c> 覆盖。
/// 若每个包各自烘焙，先烘的追加格会被后加载的短图集冲掉，后烘的索引又会从
/// 短图集的格数重新分配、覆盖前面地图正在用的格子 —— 实机表现就是
/// 某块地变成别的图块、透明格露出黑底。
/// </para>
/// <para>
/// 这里只处理**还带着翻转标记**的地图；每个包落盘时用 <c>DeferFlips</c> 保留标记。
/// </para>
/// </summary>
public static class FlipBakePass
{
    public static (int Maps, int Tiles) Run(string root)
    {
        string full = Path.GetFullPath(root);
        string mapsDir = Path.Combine(full, "Maps");
        if (!Directory.Exists(mapsDir))
            throw new DirectoryNotFoundException($"产出树里没有 Maps 目录：{mapsDir}");

        var output = new OutputTree(full, full);
        var images = new ImageCache(output);
        var baker = new FlipBaker(images);
        int mapCount = 0, tileCount = 0;
        var pending = new List<(string File, string Temp)>();
        bool prof = Environment.GetEnvironmentVariable("SDVCP_FLIP_PROFILE") == "1";
        var total = System.Diagnostics.Stopwatch.StartNew();

        // 解析 tbin 是这一步的绝对大头（本机全量 1252 张、单线程 20+ 分钟）。
        // reader 每次 Create 都是独立实例、没有共享静态状态 ⇒ 先并行解析并筛出带标记的地图，
        // 再串行烘焙（图集/追加格必须按确定顺序处理）。OrderBy 保住与串行完全一致的落盘顺序。
        string[] files = Directory.EnumerateFiles(mapsDir, "*.tbin", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        var marked = new System.Collections.Concurrent.ConcurrentBag<(string File, TBin10 Map)>();
        long parseStart = total.ElapsedMilliseconds;
        Parallel.ForEach(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
        }, file =>
        {
            try
            {
                var map = (TBin10)TBin10Reader.Create(File.ReadAllBytes(file)).Read();
                if (FlipBaker.HasMarkers(map)) marked.Add((file, map));
            }
            catch
            {
                // 坏图跳过，与串行时一致
            }
        });
        long parseMs = total.ElapsedMilliseconds - parseStart;

        long bakeMs = 0, repairMs = 0, saveMs = 0;
        foreach ((string file, TBin10 map) in marked.OrderBy(x => x.File, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(full, file).Replace('\\', '/');
            long t = total.ElapsedMilliseconds;
            tileCount += baker.Bake(map, rel);
            bakeMs += total.ElapsedMilliseconds - t;

            t = total.ElapsedMilliseconds;
            TilesheetSizeRepair.Repair(map, rel, images);
            repairMs += total.ElapsedMilliseconds - t;

            // ★ 地图先写临时文件：图集要等**所有**地图都烘完才统一写回。
            //   如果中途异常（路径解析等）导致图集没写成，绝不能把已经改了索引的地图留在地上
            //   —— 那会让地图指向不存在的追加格，变成实机黑洞。临时文件只在图片写成功后替换。
            string temp = file + ".flipbak";
            t = total.ElapsedMilliseconds;
            TBin10Reader writer = TBin10Reader.Create(Array.Empty<byte>());
            writer.Save(map, temp);
            saveMs += total.ElapsedMilliseconds - t;
            pending.Add((file, temp));
            mapCount++;
        }

        // 所有地图共用一个 ImageCache / FlipBaker：同一张图的追加位次跨图一致，
        // 最后一次性写回，避免每张地图都重复编码整张图集。
        long flushStart = total.ElapsedMilliseconds;
        images.Flush();
        long flushMs = total.ElapsedMilliseconds - flushStart;
        foreach ((string file, string temp) in pending)
            File.Move(temp, file, overwrite: true);
        if (prof)
        {
            Console.WriteLine($"[flip-prof] files={files.Length} marked={mapCount} total={total.ElapsedMilliseconds}ms "
                            + $"parse={parseMs}ms bake={bakeMs}ms repair={repairMs}ms save={saveMs}ms flush={flushMs}ms");
        }
        return (mapCount, tileCount);
    }
}
