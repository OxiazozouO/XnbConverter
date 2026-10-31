using XnbConverter.Tbin.Entity;
using SdvCpTool.Core.Maps;

namespace SdvCpTool.Core.Tracing;

/// <summary>指定地图格的全部静态信息：每层图块、动画帧、地图属性、原版差异。</summary>
public static class TileInspector
{
    public sealed record PropertyInfo(string Key, string Value);

    public sealed record FrameInfo(string Sheet, int Index, int[] SourceRect);

    public sealed record LayerTileInfo(
        string Layer, string Kind, string? Sheet, string? Image, int? Index,
        int[]? SourceRect, int? Interval, List<FrameInfo>? Frames, List<PropertyInfo> Properties);

    public sealed record Inspection(
        string Map, int X, int Y, int MapWidth, int MapHeight,
        List<LayerTileInfo> Layers, List<string> Warps, List<string> Actions, List<string> TouchActions,
        List<LayerTileInfo>? VanillaLayers);

    public static Inspection Inspect(MapRenderer renderer, string mapAssetName, int x, int y,
        MapRenderer? vanillaRenderer)
    {
        TBin10 map = renderer.LoadMap(mapAssetName)
                     ?? throw new FileNotFoundException($"找不到地图：{mapAssetName}");
        TBin10? baseMap = vanillaRenderer?.LoadMap(mapAssetName);
        return Inspect(renderer, map, mapAssetName, x, y, vanillaRenderer, baseMap);
    }

    public static Inspection Inspect(MapRenderer renderer, TBin10 map, string mapAssetName,
        int x, int y, MapRenderer? vanillaRenderer, TBin10? baseMap)
    {
        int mapW = map.Layers[0].LayerSize.X, mapH = map.Layers[0].LayerSize.Y;
        var layers = new List<LayerTileInfo>();
        var warps = new List<string>();
        var actions = new List<string>();
        var touch = new List<string>();
        Collect(renderer, map, x, y, layers, warps, actions, touch);

        List<LayerTileInfo>? vanilla = null;
        if (vanillaRenderer != null && baseMap != null)
        {
            vanilla = new List<LayerTileInfo>();
            Collect(vanillaRenderer, baseMap, x, y, vanilla, new List<string>(),
                new List<string>(), new List<string>());
        }
        return new Inspection(mapAssetName, x, y, mapW, mapH, layers, warps, actions, touch, vanilla);
    }

    private static void Collect(MapRenderer renderer, TBin10 map, int x, int y,
        List<LayerTileInfo> layers, List<string> warps, List<string> actions, List<string> touch)
    {
        foreach (Layer layer in map.Layers ?? new List<Layer>())
        {
            if (layer.Tiles == null) continue;
            if (x < 0 || y < 0 || x >= layer.LayerSize.X || y >= layer.LayerSize.Y) continue;
            int flat = y * layer.LayerSize.X + x;
            if (flat < 0 || flat >= layer.Tiles.Count) continue;
            BaseTile? tile = layer.Tiles[flat];
            if (tile == null) continue;

            var props = (tile.Properties ?? new List<Propertie>())
                .Select(p => new PropertyInfo(p.Key, p.Value?.ToString() ?? ""))
                .ToList();
            foreach (PropertyInfo p in props)
            {
                if (p.Key.Equals("Action", StringComparison.OrdinalIgnoreCase)) actions.Add($"{layer.Id}: {p.Value}");
                if (p.Key.Equals("TouchAction", StringComparison.OrdinalIgnoreCase)) touch.Add($"{layer.Id}: {p.Value}");
            }

            if (tile is AnimatedTile anim && anim.Frames is { Count: > 0 })
            {
                var frames = anim.Frames.Select(f => Frame(renderer, map, f)).ToList();
                layers.Add(new LayerTileInfo(layer.Id, "animated", null, null, null, null,
                    anim.FrameInterval, frames, props));
            }
            else if (tile is StaticTile stat)
            {
                FrameInfo frame = Frame(renderer, map, stat);
                layers.Add(new LayerTileInfo(layer.Id, "static", frame.Sheet, SheetImage(map, frame.Sheet),
                    frame.Index, frame.SourceRect, null, null, props));
            }
        }

        foreach (Propertie prop in map.Properties ?? new List<Propertie>())
        {
            if (!prop.Key.Equals("Warp", StringComparison.OrdinalIgnoreCase)
                && !prop.Key.Equals("NPCWarp", StringComparison.OrdinalIgnoreCase)) continue;
            string[] parts = (prop.Value?.ToString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 4 < parts.Length; i += 5)
            {
                if (int.TryParse(parts[i], out int wx) && int.TryParse(parts[i + 1], out int wy)
                    && wx == x && wy == y)
                    warps.Add($"{prop.Key}: {string.Join(' ', parts[i..(i + 5)])}");
            }
        }
    }

    private static string? SheetImage(TBin10 map, string sheetId) =>
        map.TileSheets?.FirstOrDefault(s => s.Id == sheetId)?.Image;

    private static FrameInfo Frame(MapRenderer renderer, TBin10 map, StaticTile tile)
    {
        int sheetWidth = renderer.GetSheetWidth(map, tile.TileSheet, out int tw, out int th);
        int across = sheetWidth > 0 && tw > 0 ? sheetWidth / tw : 0;
        int sx = across > 0 ? (tile.TileIndex % across) * tw : 0;
        int sy = across > 0 ? (tile.TileIndex / across) * th : 0;
        return new FrameInfo(tile.TileSheet, tile.TileIndex,
            across > 0 ? new[] { sx, sy, tw, th } : Array.Empty<int>());
    }
}
