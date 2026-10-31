using SdvCpTool.Core.Workspace;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 地图落盘前用**图集图片的真实像素尺寸**重算 <see cref="TileSheet.SheetSize"/>。
/// <para>
/// CP 的 <c>EditImage</c> 会把图集换成更大的图片，但地图 tbin 里的 <c>SheetSize</c>
/// 是旧图集的格数；游戏按 <c>SheetSize</c> 判断可绘制范围，超出的 <c>TileIndex</c>
/// 直接不画 ⇒ 地图上出现整块黑洞/缺块（实测 `Custom_ForbiddenMaze` 的
/// `swamptiles`：元数据 8×16=128，图片实际 8×24=192，索引 128–184 全部丢）。
/// </para>
/// <para>
/// 只改尺寸，不改 <c>TileSize</c>/图片引用；读不到图片的保持原样。
/// </para>
/// </summary>
internal static class TilesheetSizeRepair
{
    public static int Repair(TBin10 map, string mapRel, ImageCache? images)
    {
        if (images == null || map.TileSheets == null) return 0;
        string mapDir = Path.GetDirectoryName(mapRel.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        int repaired = 0;

        foreach (TileSheet sheet in map.TileSheets)
        {
            string image = (sheet.Image ?? "").Replace('\\', '/').Trim();
            if (image.Length == 0) continue;
            if (image.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) image = image[..^4];

            string key = image.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)
                ? MapPatcher.NormalizeSegments(image)
                : MapPatcher.NormalizeSegments(mapDir + "/" + image);

            // ★ 必须走 ImageCache 的内存像素：翻转烘焙刚把新格追加进缓存、还没落盘，
            //   此时磁盘 PNG 还是旧尺寸。按磁盘算 SheetSize ⇒ 新索引被游戏丢弃（黑洞）。
            if (!images.TryGetSize(key + ".png", out int width, out int height)
                && !images.TryGetSize(key + ".png.png", out width, out height))
                continue;
            IntVector2? tile = sheet.TileSize;
            if (tile == null || tile.Value.X <= 0 || tile.Value.Y <= 0) continue;

            int sx = width / tile.Value.X;
            int sy = height / tile.Value.Y;
            if (sx <= 0 || sy <= 0) continue;
            IntVector2? old = sheet.SheetSize;
            if (old != null && old.Value.X == sx && old.Value.Y == sy) continue;

            sheet.SheetSize = new IntVector2 { X = sx, Y = sy };
            repaired++;
        }
        return repaired;
    }
}
