using SdvCpTool.Core.Maps;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tracing;

/// <summary>反查结果的可视化产物：截图标注、地图上下文、逐层放大。</summary>
public static class TraceImage
{
    public static void WriteAnnotated(string imagePath, string outPath,
        ScreenshotLocator.Result result, ScreenshotLocator.Match? match, int? mapTileX, int? mapTileY)
    {
        byte[] px = Png.Read(imagePath, out int width, out int height);
        if (match != null && mapTileX is int tx && mapTileY is int ty)
        {
            int tilePx = TileFingerprint.Tile * match.Scale;
            int margin = tilePx * 2;
            int gx = tx - match.OffsetX, gy = ty - match.OffsetY;
            int sx = result.CropX + margin + match.PhaseX + gx * tilePx;
            int sy = result.CropY + margin + match.PhaseY + gy * tilePx;
            for (int x = result.CropX + margin + match.PhaseX;
                 x < result.CropX + result.CropW; x += tilePx)
                DrawLine(px, width, height, x, result.CropY, x, result.CropY + result.CropH, 40);
            for (int y = result.CropY + margin + match.PhaseY;
                 y < result.CropY + result.CropH; y += tilePx)
                DrawLine(px, width, height, result.CropX, y, result.CropX + result.CropW, y, 40);
            DrawRect(px, width, height, sx - 2, sy - 2, tilePx + 4, tilePx + 4, 255, 0, 0, 3);
        }
        Png.Write(outPath, px, width, height);
    }

    public static void WriteMapContext(MapRenderer.RenderedMap map, int tileX, int tileY, string outPath,
        int radius = 7, int scale = 4)
    {
        int x0 = Math.Max(0, (tileX - radius) * TileFingerprint.Tile);
        int y0 = Math.Max(0, (tileY - radius) * TileFingerprint.Tile);
        int x1 = Math.Min(map.Width, (tileX + radius + 1) * TileFingerprint.Tile);
        int y1 = Math.Min(map.Height, (tileY + radius + 1) * TileFingerprint.Tile);
        int cw = Math.Max(1, x1 - x0), ch = Math.Max(1, y1 - y0);
        byte[] dst = new byte[cw * scale * ch * scale * 4];
        for (int y = 0; y < ch * scale; y++)
        {
            int sy = y0 + y / scale;
            for (int x = 0; x < cw * scale; x++)
            {
                int sx = x0 + x / scale;
                int si = (sy * map.Width + sx) * 4;
                int di = (y * cw * scale + x) * 4;
                dst[di] = map.Rgba[si];
                dst[di + 1] = map.Rgba[si + 1];
                dst[di + 2] = map.Rgba[si + 2];
                dst[di + 3] = map.Rgba[si + 3];
            }
        }
        int w = cw * scale, h = ch * scale;
        for (int x = 0; x < w; x += TileFingerprint.Tile * scale)
            DrawLine(dst, w, h, x, 0, x, h, 55);
        for (int y = 0; y < h; y += TileFingerprint.Tile * scale)
            DrawLine(dst, w, h, 0, y, w, y, 55);
        int bx = (tileX * TileFingerprint.Tile - x0) * scale;
        int by = (tileY * TileFingerprint.Tile - y0) * scale;
        DrawRect(dst, w, h, bx - 1, by - 1, TileFingerprint.Tile * scale + 2,
            TileFingerprint.Tile * scale + 2, 255, 0, 0, 3);
        Png.Write(outPath, dst, w, h);
    }

    public static void WriteLayerCrops(string dir, MapRenderer renderer, TBin10 map,
        IReadOnlyList<TileInspector.LayerTileInfo> layers, int scale = 8)
    {
        Directory.CreateDirectory(dir);
        foreach (TileInspector.LayerTileInfo layer in layers)
        {
            var frames = layer.Frames ?? new List<TileInspector.FrameInfo>();
            if (frames.Count == 0 && layer.Sheet != null && layer.Index is int index)
                frames.Add(new TileInspector.FrameInfo(layer.Sheet, index, layer.SourceRect ?? Array.Empty<int>()));
            if (frames.Count == 0) continue;

            int cell = TileFingerprint.Tile * scale;
            int gap = 2;
            int width = frames.Count * cell + (frames.Count - 1) * gap;
            byte[] canvas = new byte[width * cell * 4];
            for (int f = 0; f < frames.Count; f++)
            {
                byte[]? tile = renderer.RenderFrame(map, frames[f].Sheet, frames[f].Index);
                if (tile == null) continue;
                int ox = f * (cell + gap);
                for (int y = 0; y < cell; y++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        int si = ((y / scale) * TileFingerprint.Tile + x / scale) * 4;
                        int di = (y * width + ox + x) * 4;
                        canvas[di] = tile[si];
                        canvas[di + 1] = tile[si + 1];
                        canvas[di + 2] = tile[si + 2];
                        canvas[di + 3] = tile[si + 3];
                    }
                }
            }
            string name = string.Join("_", layer.Layer.Split(Path.GetInvalidFileNameChars()));
            Png.Write(Path.Combine(dir, name + ".png"), canvas, width, cell);
        }
    }

    private static void DrawRect(byte[] px, int w, int h, int x, int y, int rw, int rh,
        byte r, byte g, byte b, int thickness)
    {
        for (int t = 0; t < thickness; t++)
        {
            DrawLine(px, w, h, x + t, y + t, x + rw - t, y + t, 255, r, g, b);
            DrawLine(px, w, h, x + t, y + rh - t, x + rw - t, y + rh - t, 255, r, g, b);
            DrawLine(px, w, h, x + t, y + t, x + t, y + rh - t, 255, r, g, b);
            DrawLine(px, w, h, x + rw - t, y + t, x + rw - t, y + rh - t, 255, r, g, b);
        }
    }

    private static void DrawLine(byte[] px, int w, int h, int x0, int y0, int x1, int y1, byte alpha,
        byte r = 255, byte g = 255, byte b = 255)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if (x0 >= 0 && y0 >= 0 && x0 < w && y0 < h)
            {
                int i = (y0 * w + x0) * 4;
                byte a = px[i + 3];
                px[i] = (byte)((r * alpha + px[i] * (255 - alpha)) / 255);
                px[i + 1] = (byte)((g * alpha + px[i + 1] * (255 - alpha)) / 255);
                px[i + 2] = (byte)((b * alpha + px[i + 2] * (255 - alpha)) / 255);
                px[i + 3] = (byte)Math.Max(a, alpha);
            }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
