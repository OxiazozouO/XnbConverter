using SdvCpTool.Core.Maps;

namespace SdvCpTool.Cli;

internal static class FlipBakeCommand
{
    public static int Run(string[] args)
    {
        string? tree = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--tree" && i + 1 < args.Length) tree = args[++i];
        }
        if (string.IsNullOrWhiteSpace(tree) || !Directory.Exists(tree))
        {
            Console.Error.WriteLine("用法：SdvCpTool.Cli bake-flips --tree <已转换的产出树>");
            return 2;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        (int maps, int tiles) = FlipBakePass.Run(tree);
        Console.WriteLine($"翻转/旋转烘焙：{tiles} 个图块，涉及 {maps} 张地图（{sw.ElapsedMilliseconds}ms）");

        // 多层叠出来的硬画树/灌木/石头：按图层栈合成识别，删除所有非地面层，
        // 在底部中心补 Paths 指令。和翻转烘焙一起作为全树最后一道静态化。
        sw.Restart();
        (int hardMaps, int hardInstances) = HardDrawnResourceRepair.Run(tree,
            log: line => Console.WriteLine("  " + line));
        Console.WriteLine($"硬画资源转 Paths：{hardInstances} 个实例，涉及 {hardMaps} 张地图"
                          + $"（{sw.ElapsedMilliseconds}ms）");
        return 0;
    }
}
