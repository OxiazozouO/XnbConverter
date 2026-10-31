using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Maps;

/// <summary>把 tbin 地图按图层合成成 1× RGBA，供截图反查/图块检查使用。</summary>
public sealed class MapRenderer
{
    public const int TileSize = 16;

    private readonly string _tree;
    private readonly string _season;
    private readonly Dictionary<string, SheetPixels?> _sheets = new(StringComparer.OrdinalIgnoreCase);

    public MapRenderer(string treeRoot, string season = "spring")
    {
        _tree = Path.GetFullPath(treeRoot);
        _season = string.IsNullOrWhiteSpace(season) ? "spring" : season.Trim();
    }

    public sealed record RenderedMap(string AssetName, int Width, int Height, byte[] Rgba);

    private sealed record SheetPixels(byte[] Rgba, int Width, int Height, int TileWidth, int TileHeight);

    /// <summary>地图资产名 → tbin 绝对路径；没有返回 null。</summary>
    public string? MapPath(string assetName)
    {
        string name = assetName.Replace('\\', '/').Trim('/');
        if (name.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)) name = name[5..];
        string path = Path.Combine(_tree, "Maps", name + ".tbin");
        return File.Exists(path) ? path : null;
    }

    public RenderedMap? Render(string assetName)
    {
        string? path = MapPath(assetName);
        if (path == null) return null;
        TBin10 map;
        try { map = (TBin10)TBin10Reader.Create(File.ReadAllBytes(path)).Read(); }
        catch { return null; }
        return Render(map, assetName);
    }

    public RenderedMap Render(TBin10 map, string assetName)
    {
        int tilesWide = map.Layers[0].LayerSize.X;
        int tilesHigh = map.Layers[0].LayerSize.Y;
        int width = tilesWide * TileSize, height = tilesHigh * TileSize;
        byte[] canvas = new byte[width * height * 4];

        foreach (Layer layer in map.Layers ?? new List<Layer>())
        {
            if (layer.Tiles == null || layer.Visible == 0) continue;
            for (int i = 0; i < layer.Tiles.Count; i++)
            {
                BaseTile? tile = layer.Tiles[i];
                StaticTile? frame = tile switch
                {
                    StaticTile s => s,
                    AnimatedTile a when a.Frames is { Count: > 0 } => a.Frames[0],
                    _ => null,
                };
                if (frame == null) continue;
                int x = i % layer.LayerSize.X, y = i / layer.LayerSize.X;
                BlendTile(map, frame, canvas, width, height, x * TileSize, y * TileSize);
            }
        }
        return new RenderedMap(assetName, width, height, canvas);
    }

    /// <summary>把某层的某格单独画出来（不含其它层），供图层裁剪图使用。</summary>
    public byte[]? RenderTile(TBin10 map, StaticTile frame)
    {
        byte[] cell = new byte[TileSize * TileSize * 4];
        BlendTile(map, frame, cell, TileSize, TileSize, 0, 0);
        return cell;
    }

    public byte[]? RenderFrame(TBin10 map, string sheetId, int index) =>
        RenderTile(map, new StaticTile { TileSheet = sheetId, TileIndex = index });

    public TBin10? LoadMap(string assetName)
    {
        string? path = MapPath(assetName);
        if (path == null) return null;
        try { return (TBin10)TBin10Reader.Create(File.ReadAllBytes(path)).Read(); }
        catch { return null; }
    }

    public int GetSheetWidth(TBin10 map, string sheetId, out int tileWidth, out int tileHeight)
    {
        SheetPixels? sheet = GetSheet(map, sheetId);
        tileWidth = sheet?.TileWidth ?? TileSize;
        tileHeight = sheet?.TileHeight ?? TileSize;
        return sheet?.Width ?? 0;
    }

    private void BlendTile(TBin10 map, StaticTile frame, byte[] dst, int dstW, int dstH, int dx, int dy)
    {
        SheetPixels? sheet = GetSheet(map, frame.TileSheet);
        if (sheet == null || sheet.Width <= 0 || sheet.Height <= 0) return;
        int tw = sheet.TileWidth, th = sheet.TileHeight;
        if (tw <= 0 || th <= 0) tw = th = TileSize;
        int across = sheet.Width / tw;
        if (across <= 0) return;
        int sx = (frame.TileIndex % across) * tw;
        int sy = (frame.TileIndex / across) * th;
        if (sx < 0 || sy < 0 || sx + tw > sheet.Width || sy + th > sheet.Height) return;

        for (int y = 0; y < th; y++)
        {
            int ty = dy + y;
            if (ty < 0 || ty >= dstH) continue;
            for (int x = 0; x < tw; x++)
            {
                int tx = dx + x;
                if (tx < 0 || tx >= dstW) continue;
                int si = ((sy + y) * sheet.Width + sx + x) * 4;
                byte a = sheet.Rgba[si + 3];
                if (a == 0) continue;
                int di = (ty * dstW + tx) * 4;
                byte da = dst[di + 3];
                if (a == 255 || da == 0)
                {
                    dst[di] = sheet.Rgba[si];
                    dst[di + 1] = sheet.Rgba[si + 1];
                    dst[di + 2] = sheet.Rgba[si + 2];
                    dst[di + 3] = a;
                }
                else
                {
                    int outA = a + da * (255 - a) / 255;
                    for (int c = 0; c < 3; c++)
                        dst[di + c] = (byte)((sheet.Rgba[si + c] * a
                                              + dst[di + c] * da * (255 - a) / 255) / Math.Max(1, outA));
                    dst[di + 3] = (byte)outA;
                }
            }
        }
    }

    private SheetPixels? GetSheet(TBin10 map, string sheetId)
    {
        TileSheet? sheet = map.TileSheets?.FirstOrDefault(
            s => s != null && string.Equals(s.Id, sheetId, StringComparison.Ordinal));
        if (sheet == null || string.IsNullOrEmpty(sheet.Image)) return null;

        string image = sheet.Image.Replace('\\', '/').Trim('/');
        string? path = ResolveSheetPath(image);
        if (path == null) return null;
        if (_sheets.TryGetValue(path, out SheetPixels? cached)) return cached;

        int tw = sheet.TileSize.X > 0 ? sheet.TileSize.X : TileSize;
        int th = sheet.TileSize.Y > 0 ? sheet.TileSize.Y : TileSize;
        SheetPixels? result = null;
        try
        {
            byte[] px = Png.Read(path, out int w, out int h);
            result = new SheetPixels(px, w, h, tw, th);
        }
        catch
        {
            // 坏图/缺图：当作没有图集
        }
        _sheets[path] = result;
        return result;
    }

    private string? ResolveSheetPath(string image)
    {
        var candidates = new List<string>();
        void Add(string name)
        {
            candidates.Add(Path.Combine(_tree, "Maps", name + ".png"));
            candidates.Add(Path.Combine(_tree, "TileSheets", name + ".png"));
        }

        Add(image);
        if (!string.Equals(_season, "spring", StringComparison.OrdinalIgnoreCase))
        {
            int at = image.IndexOf("spring_", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) Add(image[..at] + _season + "_" + image[(at + "spring_".Length)..]);
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
