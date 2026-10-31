using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Tracing;
using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Cli;

/// <summary>
/// 截图反查：截图 → 地图/坐标 → 每层图块/动画 → 覆盖该格的 CP 补丁。
/// 设计见 docs/superpowers/specs/2026-10-25-screenshot-trace-design.md。
/// </summary>
internal static class TraceScreenshotCommand
{
    public static int Run(string[] args)
    {
        string? image = null, tree = null, mods = null, baseTree = null, outDir = null, cacheDirArg = null;
        string season = "spring";
        string? cropArg = null, mapArg = null, tileArg = null;
        int? scaleArg = null;
        bool rebuild = false;
        double minScore = 0.6;

        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "--image": image = Next(); break;
                case "--tree": tree = Next(); break;
                case "--mods": mods = Next(); break;
                case "--base": baseTree = Next(); break;
                case "--season": season = Next() ?? season; break;
                case "--crop": cropArg = Next(); break;
                case "--map": mapArg = Next(); break;
                case "--tile": tileArg = Next(); break;
                case "--scale":
                    if (int.TryParse(Next(), out int sc)) scaleArg = sc;
                    break;
                case "--out": outDir = Next(); break;
                case "--cache": cacheDirArg = Next(); break;
                case "--rebuild-cache": rebuild = true; break;
                case "--min-score":
                    double.TryParse(Next(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out minScore);
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    Console.Error.WriteLine($"未知参数：{a}\n{Usage}");
                    return 3;
            }
        }

        if (string.IsNullOrWhiteSpace(tree) || !Directory.Exists(tree))
        {
            Console.Error.WriteLine("缺少或找不到 --tree\n" + Usage);
            return 3;
        }
        if (image != null && !File.Exists(image))
        {
            Console.Error.WriteLine($"找不到截图：{image}");
            return 3;
        }
        (int tx, int ty)? forcedTile = ParseTile(tileArg);
        if (image == null && (mapArg == null || forcedTile == null))
        {
            Console.Error.WriteLine("没有截图时必须同时给 --map 和 --tile\n" + Usage);
            return 3;
        }

        outDir ??= Path.Combine(Directory.GetCurrentDirectory(), "trace-out");
        Directory.CreateDirectory(outDir);
        // 缓存不能跟着 --out 走：换一个输出目录就重建全树索引太浪费。
        string cacheDir = cacheDirArg ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SdvCpTool", "TraceCache");
        var renderer = new MapRenderer(tree, season);
        var vanilla = baseTree != null ? new MapRenderer(baseTree, season) : null;

        ScreenshotLocator.Result? located = null;
        ScreenshotLocator.Match? best = null;
        MapFingerprintIndex? index = null;
        // 已明确给了地图+坐标：不用再跑截图定位（也不建索引）。
        bool direct = forcedTile != null && mapArg != null;
        if (image != null && !direct)
        {
            index = MapFingerprintIndex.LoadOrBuild(tree, season, cacheDir, rebuild,
                line => Console.Error.WriteLine(line));
            var opts = new ScreenshotLocator.Options(
                CropX: 0, CropY: 0, CropW: 0, CropH: 0,
                MapFilter: mapArg, ForceScale: scaleArg, MinScore: minScore);
            if (ParseCrop(cropArg) is { } c)
                opts = opts with { CropX = c.X, CropY = c.Y, CropW = c.W, CropH = c.H };
            located = ScreenshotLocator.Locate(image, index, opts);
            best = located.Candidates.FirstOrDefault();
            if (best == null || located.Status != "matched" && forcedTile == null && mapArg == null)
            {
                WriteCandidateReport(outDir, located, image);
                Console.WriteLine($"未定案：{located.Status}，候选 {located.Candidates.Count} 个；已写 {outDir}");
                return 2;
            }
        }

        string mapName = NormalizeMap(mapArg ?? best?.Map
            ?? throw new InvalidOperationException("没有地图名"));
        int tileX = forcedTile?.Item1 ?? TargetTileX(located!, best!, index!, mapName);
        int tileY = forcedTile?.Item2 ?? TargetTileY(located!, best!, index!, mapName);

        TileInspector.Inspection inspection = TileInspector.Inspect(renderer, mapName, tileX, tileY, vanilla);
        TBin10 map = renderer.LoadMap(mapName)
            ?? throw new FileNotFoundException($"找不到地图：{mapName}");

        var patches = mods != null && Directory.Exists(mods)
            ? PatchProvenance.Find(mods, mapName, tileX, tileY, line => Console.Error.WriteLine(line))
            : new List<PatchProvenance.PatchInfo>();

        string mapOut = mapName.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)
            ? mapName : "Maps/" + mapName;
        MapRenderer.RenderedMap? rendered = renderer.Render(mapOut);

        var report = BuildReport(image, tree, baseTree, season, located, best, mapOut, tileX, tileY,
            inspection, patches);
        File.WriteAllText(Path.Combine(outDir, "trace.json"),
            report.ToString(Formatting.Indented), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "trace.txt"), BuildText(located, best, mapOut, tileX, tileY,
            inspection, patches), new System.Text.UTF8Encoding(false));

        if (image != null && located != null && best != null)
            TraceImage.WriteAnnotated(image, Path.Combine(outDir, "annotated.png"),
                located, best, tileX, tileY);
        if (rendered != null)
            TraceImage.WriteMapContext(rendered, tileX, tileY, Path.Combine(outDir, "map-context.png"));
        TraceImage.WriteLayerCrops(Path.Combine(outDir, "layers"), renderer, map, inspection.Layers);

        Console.WriteLine($"{(located?.Status == "matched" ? "已匹配" : "已定位")}：{mapOut} ({tileX},{tileY})"
                          + (best != null ? $" score={best.Score:P0} scale={best.Scale}" : "")
                          + $" → {outDir}");
        return 0;
    }

    public const string Usage = """
        用法：
          SdvCpTool.Cli trace-screenshot --image <png> --tree <树> [--mods <Mods>] [--base <原版树>]
              [--season spring|summer|fall|winter] [--crop x,y,w,h]
              [--map <地图名>] [--tile x,y] [--scale 1|2|3|4] [--out <目录>]
              [--cache <目录>] [--rebuild-cache] [--min-score 0.6]

        产物：trace.json / trace.txt / annotated.png / map-context.png / layers/*.png
        """;

    private static (int X, int Y, int W, int H)? ParseCrop(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string[] p = value.Split(',');
        if (p.Length != 4) return null;
        return int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y)
               && int.TryParse(p[2], out int w) && int.TryParse(p[3], out int h)
            ? (x, y, w, h) : null;
    }

    private static (int, int)? ParseTile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string[] p = value.Split(',');
        return p.Length == 2 && int.TryParse(p[0].Trim(), out int x) && int.TryParse(p[1].Trim(), out int y)
            ? (x, y) : null;
    }

    private static string NormalizeMap(string name)
    {
        name = name.Replace('\\', '/').Trim('/');
        return name.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? name : "Maps/" + name;
    }

    private static int TargetTileX(ScreenshotLocator.Result result, ScreenshotLocator.Match match,
        MapFingerprintIndex index, string mapName)
    {
        if (result.RedBoxX is int rx && result.RedBoxW is int rw)
            return ScreenToTile(rx + rw / 2, result.CropX, match, index, mapName, xAxis: true);
        return match.TileX;
    }

    private static int TargetTileY(ScreenshotLocator.Result result, ScreenshotLocator.Match match,
        MapFingerprintIndex index, string mapName)
    {
        if (result.RedBoxY is int ry && result.RedBoxH is int rh)
            return ScreenToTile(ry + rh / 2, result.CropY, match, index, mapName, xAxis: false);
        return match.TileY;
    }

    private static int ScreenToTile(int screen, int cropOrigin, ScreenshotLocator.Match match,
        MapFingerprintIndex index, string mapName, bool xAxis)
    {
        int tilePx = TileFingerprint.Tile * match.Scale;
        int margin = tilePx * 2;
        int phase = xAxis ? match.PhaseX : match.PhaseY;
        int offset = xAxis ? match.OffsetX : match.OffsetY;
        int grid = (int)Math.Floor((screen - cropOrigin - margin - phase) / (double)tilePx);
        int value = offset + grid;
        MapFingerprintIndex.MapEntry? entry = index.Maps.FirstOrDefault(
            m => ("Maps/" + m.Name).Equals(mapName, StringComparison.OrdinalIgnoreCase)
                 || m.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
        int max = entry == null ? int.MaxValue : (xAxis ? entry.TilesWide : entry.TilesHigh) - 1;
        return Math.Clamp(value, 0, Math.Max(0, max));
    }

    private static JObject BuildReport(string? image, string tree, string? baseTree, string season,
        ScreenshotLocator.Result? located, ScreenshotLocator.Match? best, string mapName, int tx, int ty,
        TileInspector.Inspection inspection, List<PatchProvenance.PatchInfo> patches)
    {
        var layers = new JArray();
        foreach (TileInspector.LayerTileInfo l in inspection.Layers)
        {
            var item = new JObject
            {
                ["layer"] = l.Layer,
                ["kind"] = l.Kind,
                ["sheet"] = l.Sheet,
                ["image"] = l.Image,
                ["index"] = l.Index,
                ["sourceRect"] = l.SourceRect == null ? null : new JArray(l.SourceRect),
                ["interval"] = l.Interval,
                ["properties"] = new JArray(l.Properties.Select(p => new JObject { ["key"] = p.Key, ["value"] = p.Value })),
            };
            if (l.Frames != null)
                item["frames"] = new JArray(l.Frames.Select(f => new JObject
                {
                    ["sheet"] = f.Sheet,
                    ["index"] = f.Index,
                    ["sourceRect"] = new JArray(f.SourceRect),
                }));
            layers.Add(item);
        }

        var patchArray = new JArray();
        foreach (PatchProvenance.PatchInfo p in patches)
        {
            patchArray.Add(new JObject
            {
                ["pack"] = p.Pack,
                ["uniqueId"] = p.UniqueId,
                ["source"] = p.Source,
                ["index"] = p.Index,
                ["action"] = p.Action,
                ["target"] = p.Target,
                ["when"] = p.When == null ? null : JToken.Parse(p.When),
                ["fromFile"] = p.FromFile,
                ["toArea"] = p.ToArea == null ? null : new JArray(p.ToArea),
                ["mapTiles"] = new JArray(p.MapTiles),
                ["patchMode"] = p.PatchMode,
                ["update"] = p.Update,
                ["coversTile"] = p.CoversTile,
            });
        }

        var report = new JObject
        {
            ["schema"] = 1,
            ["status"] = located?.Status ?? "manual",
            ["confidence"] = located?.Confidence ?? 1.0,
            ["image"] = image,
            ["tree"] = Path.GetFullPath(tree),
            ["baseTree"] = baseTree == null ? null : Path.GetFullPath(baseTree),
            ["season"] = season,
            ["match"] = new JObject
            {
                ["map"] = mapName,
                ["tile"] = new JArray(tx, ty),
                ["mapSize"] = new JArray(inspection.MapWidth, inspection.MapHeight),
                ["scale"] = best?.Scale,
                ["phase"] = best == null ? null : new JArray(best.PhaseX, best.PhaseY),
                ["offset"] = best == null ? null : new JArray(best.OffsetX, best.OffsetY),
                ["score"] = best?.Score,
            },
            ["candidates"] = new JArray((located?.Candidates ?? Array.Empty<ScreenshotLocator.Match>())
                .Select(c => new JObject
                {
                    ["map"] = "Maps/" + c.Map,
                    ["tile"] = new JArray(c.TileX, c.TileY),
                    ["score"] = c.Score,
                    ["scale"] = c.Scale,
                })),
            ["redBox"] = located is { RedBoxW: not null }
                ? new JObject
                {
                    ["x"] = located.RedBoxX, ["y"] = located.RedBoxY,
                    ["w"] = located.RedBoxW, ["h"] = located.RedBoxH,
                }
                : null,
            ["redBoxes"] = located == null ? null : new JArray(
                located.RedBoxes.Select(b => new JObject
                {
                    ["x"] = b.X, ["y"] = b.Y, ["w"] = b.W, ["h"] = b.H,
                })),
            ["layers"] = layers,
            ["mapProperties"] = new JObject
            {
                ["warp"] = new JArray(inspection.Warps),
                ["action"] = new JArray(inspection.Actions),
                ["touchAction"] = new JArray(inspection.TouchActions),
            },
            ["vanilla"] = new JObject
            {
                ["available"] = inspection.VanillaLayers != null,
                ["layers"] = inspection.VanillaLayers == null ? null : new JArray(
                    inspection.VanillaLayers.Select(l => new JObject
                    {
                        ["layer"] = l.Layer,
                        ["kind"] = l.Kind,
                        ["sheet"] = l.Frames?.FirstOrDefault()?.Sheet ?? l.Sheet,
                        ["index"] = l.Frames?.FirstOrDefault()?.Index ?? l.Index,
                    })),
            },
            ["patches"] = patchArray,
        };
        return report;
    }

    private static string BuildText(ScreenshotLocator.Result? located, ScreenshotLocator.Match? best,
        string mapName, int tx, int ty, TileInspector.Inspection inspection,
        List<PatchProvenance.PatchInfo> patches)
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine($"map={mapName} tile=({tx},{ty}) mapSize={inspection.MapWidth}x{inspection.MapHeight}");
        if (located != null)
            s.AppendLine($"status={located.Status} confidence={located.Confidence:P0}"
                         + (best != null ? $" scale={best.Scale} score={best.Score:P0}" : "")
                         + (located.RedBoxW != null ? $" redBox=({located.RedBoxX},{located.RedBoxY},{located.RedBoxW},{located.RedBoxH})" : ""));
        s.AppendLine("layers:");
        foreach (TileInspector.LayerTileInfo l in inspection.Layers)
        {
            string tile = l.Kind == "animated"
                ? $"animated {l.Interval}ms [{string.Join(", ", l.Frames!.Select(f => $"{f.Sheet}@{f.Index}"))}]"
                : $"static {l.Sheet}@{l.Index} rect={string.Join(',', l.SourceRect ?? Array.Empty<int>())}";
            s.AppendLine($"  {l.Layer}: {tile}");
        }
        if (inspection.Warps.Count + inspection.Actions.Count + inspection.TouchActions.Count > 0)
        {
            s.AppendLine("properties:");
            foreach (string w in inspection.Warps) s.AppendLine("  warp " + w);
            foreach (string a in inspection.Actions) s.AppendLine("  action " + a);
            foreach (string t in inspection.TouchActions) s.AppendLine("  touch " + t);
        }
        s.AppendLine("patches:");
        if (patches.Count == 0) s.AppendLine("  (无 CP 补丁覆盖；来自地图底图 / Load)");
        foreach (PatchProvenance.PatchInfo p in patches)
            s.AppendLine($"  {(p.CoversTile ? "*" : " ")} [{p.Pack}] {p.Source}#{p.Index} {p.Action} "
                         + $"{p.Target} when={p.When ?? "-"} from={p.FromFile ?? "-"}");
        return s.ToString();
    }

    private static void WriteCandidateReport(string outDir, ScreenshotLocator.Result result, string image)
    {
        var report = new JObject
        {
            ["schema"] = 1,
            ["status"] = result.Status,
            ["confidence"] = result.Confidence,
            ["image"] = image,
            ["candidates"] = new JArray(result.Candidates.Select(c => new JObject
            {
                ["map"] = "Maps/" + c.Map,
                ["tile"] = new JArray(c.TileX, c.TileY),
                ["score"] = c.Score,
                ["scale"] = c.Scale,
                ["offset"] = new JArray(c.OffsetX, c.OffsetY),
            })),
        };
        File.WriteAllText(Path.Combine(outDir, "trace.json"),
            report.ToString(Formatting.Indented), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "trace.txt"),
            $"status={result.Status} confidence={result.Confidence:P0}\n"
            + string.Join("\n", result.Candidates.Select(c =>
                $"  Maps/{c.Map} tile=({c.TileX},{c.TileY}) score={c.Score:P0} scale={c.Scale}")),
            new System.Text.UTF8Encoding(false));
        if (result.Candidates.FirstOrDefault() is { } best)
            TraceImage.WriteAnnotated(image, Path.Combine(outDir, "annotated.png"), result, best, null, null);
    }
}
