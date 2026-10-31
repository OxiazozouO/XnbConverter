using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 落盘前最后一道检查：**地图里的每个图集引用都必须真的能落到一张图上**。
/// <para>
/// 地图的 <c>TileSheet.Image</c> 是按**地图自己那层目录**解析的。上游 tmx/tbin 里常见
/// <c>../TileSheets/furniture.png</c>、<c>giex_island_extras</c> 这类写法，经过多次
/// 相对化/定基后可能被写成 <c>TileSheets/furniture</c>、<c>giex_island_extras</c>，
/// 游戏就会去找 <c>Maps/TileSheets/furniture.xnb</c>、<c>Maps/Patches/giex_island_extras.xnb</c>
/// —— 后者不存在 ⇒ <c>xTile.Map.LoadTileSheets</c> 抛异常 ⇒ 整个地点建不起来
/// （实机：ArchaeologyHouse / GiEX / Hurst / East Scarp 多张图）。
/// </para>
/// <para>
/// 这里不猜名字：只在**产出树里真实存在**的图里找，找到才改引用；找不到保持原样，
/// 由审计报出来。候选顺序是「绝对内容路径」→「Maps/ 下同路径」→「Maps/ 下同名文件」，
/// 都是上游/原版实际用过的形态。
/// </para>
/// </summary>
internal static class TilesheetPathRepair
{
    public static int Repair(TBin10 map, string mapRel, OutputTree output)
    {
        if (map.TileSheets == null || map.TileSheets.Count == 0) return 0;

        string mapDir = Path.GetDirectoryName(mapRel.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        int repaired = 0;

        foreach (TileSheet sheet in map.TileSheets)
        {
            string raw = (sheet.Image ?? "").Replace('\\', '/').Trim();
            if (raw.Length == 0) continue;

            string resolved = Resolve(mapDir, raw);
            if (ImageExists(output, resolved)) continue;

            string? hit = Find(output, raw, mapDir);
            if (hit == null || string.Equals(hit, resolved, StringComparison.OrdinalIgnoreCase))
                continue;

            sheet.Image = MapPatcher.Rebase(hit, mapRel, alreadyContent: true);
            repaired++;
        }

        return repaired;
    }

    /// <summary>按游戏的方式解析：图集引用相对**地图所在目录**；<c>..</c> 可出到内容根。</summary>
    private static string Resolve(string mapDir, string image)
    {
        string combined = image.StartsWith('/') ? image : mapDir + "/" + image;
        string normalized = NormalizeSegments(combined);
        return StripImageExtension(normalized);
    }

    /// <summary>找一张真实存在的图：绝对内容路径 → <c>Maps/</c> 下同路径 → <c>Maps/</c> 下同名。</summary>
    private static string? Find(OutputTree output, string raw, string mapDir)
    {
        string noExt = StripImageExtension(raw);
        string basename = Path.GetFileName(noExt.Replace('/', Path.DirectorySeparatorChar));

        var candidates = new List<string>
        {
            // `../TileSheets/furniture` / `TileSheets/furniture` → 内容根的 `TileSheets/furniture`
            StripLeadingParents(noExt),
            // `giex_island_extras` 在 `Maps/Patches/` 里被引用，实际图在 `Maps/`
            "Maps/" + noExt,
            // `spring_townInterior` 在 `Maps/7thAxis.LitD/` 里被引用，实际图在 `Maps/`
            "Maps/" + basename,
        };

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string key = NormalizeSegments(candidate);
            if (key.Length == 0) continue;
            if (ImageExists(output, key)) return key;
        }
        return null;
    }

    private static bool ImageExists(OutputTree output, string key)
    {
        if (key.Length == 0) return false;
        return output.Exists(key + ".png") || output.Exists(key + ".xnb");
    }

    private static string StripImageExtension(string path) =>
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;

    private static string StripLeadingParents(string path)
    {
        while (path.StartsWith("../", StringComparison.Ordinal)) path = path[3..];
        return path;
    }

    private static string NormalizeSegments(string path)
    {
        var parts = new List<string>();
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        return string.Join('/', parts);
    }
}
