using SdvCpTool.Core.Frameworks;

namespace SdvCpTool.Cli;

/// <summary>
/// <c>bake-frameworks</c>：在 CP/FTM 转换后的产出树上，把框架内容按原版数据表**新增行**。
/// <para>口径见 <c>docs/research/2026-10-20-additive-baking-reassessment.md</c>。</para>
/// </summary>
public static class FrameworkCommand
{
    public static int Run(string[] args)
    {
        string? tree = null;
        string? mods = null;
        string cccbVariant = "Pure_Vanilla";
        bool dedupCosmetics = false;
        int dedupThreshold = 0;
        double dedupColorTolerance = 0;
        double dedupMaxPixelDifference = 1.0;
        double dedupColorConsistency = 2.0;
        bool calibrateDedup = false;
        var packs = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tree": if (++i >= args.Length) return 2; tree = args[i]; break;
                case "--mods": if (++i >= args.Length) return 2; mods = args[i]; break;
                case "--cccb-variant": if (++i >= args.Length) return 2; cccbVariant = args[i]; break;
                case "--dedup-cosmetics": dedupCosmetics = true; break;
                case "--calibrate-dedup": calibrateDedup = true; break;
                case "--dedup-threshold":
                    if (++i >= args.Length || !int.TryParse(args[i], out dedupThreshold)) return 2;
                    break;
                case "--dedup-color-tolerance":
                    if (++i >= args.Length ||
                        !double.TryParse(args[i], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out dedupColorTolerance))
                        return 2;
                    break;
                case "--dedup-max-pixel-diff":
                    if (++i >= args.Length ||
                        !double.TryParse(args[i], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out dedupMaxPixelDifference))
                        return 2;
                    break;
                case "--dedup-color-consistency":
                    if (++i >= args.Length ||
                        !double.TryParse(args[i], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out dedupColorConsistency))
                        return 2;
                    break;
                case "--packs":
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        packs.Add(args[++i]);
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[i]}");
                    return 2;
            }
        }
        if (calibrateDedup)
        {
            if (tree == null) { Console.Error.WriteLine("--calibrate-dedup 需要 --tree <原版树>"); return 2; }
            try
            {
                var c = SdvCpTool.Core.Frameworks.CosmeticDeduper.CalibrateVanillaFurniture(tree);
                Console.WriteLine($"原版家具 {c.Entries} 条，去掉完全相同后 {c.ExactUnique} 条");
                Console.WriteLine($"最近 dHash 距离（mask IoU≥0.85）：{c.MinHamming}，近邻 {c.NearPairs} 对");
                Console.WriteLine($"最近纯变色 MAD（mask IoU≥0.95）：{c.MinPureMad:F2}");
                Console.WriteLine($"推荐 --dedup-threshold {c.RecommendedThreshold}");
                Console.WriteLine($"推荐纯变色容差 {c.RecommendedPureTolerance:F0}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"校准失败：{ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
        if (tree == null || mods == null)
        {
            Console.Error.WriteLine("用法：SdvCpTool.Cli bake-frameworks --tree <产出树> --mods <Mods> [--packs 子串…] [--cccb-variant Pure_Vanilla|RSV_SVE] [--dedup-cosmetics [--dedup-threshold 0] [--dedup-color-tolerance 0] [--dedup-max-pixel-diff 1.0] [--dedup-color-consistency 2.0]]");
            return 2;
        }

        try
        {
            FrameworkBaker.Result result = FrameworkBaker.Bake(tree, mods, packs, cccbVariant,
                dedupCosmetics, dedupThreshold, dedupColorTolerance,
                dedupMaxPixelDifference, dedupColorConsistency);
            foreach (FrameworkBaker.PackResult p in result.Packs)
            {
                Console.WriteLine(
                    $"  [{p.Framework}] {p.Name}: 家具 {p.Furniture} / 装饰 {p.Decoration} / " +
                    $"作物 {p.Crop} / 可制作 {p.Craftable} / 邮件 {p.Mail} / " +
                    $"捆绑包 {p.Bundles} / 建筑 {p.Buildings} / 宠物 {p.Pets} / " +
                    $"草地 {p.Grass} / 交通 {p.Transit} / 纹理覆盖 {p.Textures}");
                foreach (string note in (p.Notes ?? Array.Empty<string>()).Take(8))
                    Console.WriteLine($"    ⚠ {note}");
            }
            Console.WriteLine(
                $"合计：家具 {result.Furniture} / 装饰 {result.Decoration} / 作物 {result.Crop} / " +
                $"可制作 {result.Craftable} / 邮件 {result.Mail} / 捆绑包 {result.Bundles} / " +
                $"建筑 {result.Buildings} / 宠物 {result.Pets} / " +
                $"草地 {result.Grass} / 交通 {result.Transit} / 纹理覆盖 {result.Textures}" +
                (result.Deduped > 0 ? $" / 相似去重 {result.Deduped}" : ""));
            Console.WriteLine($"报告：{result.ReportPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"bake-frameworks 失败：{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
