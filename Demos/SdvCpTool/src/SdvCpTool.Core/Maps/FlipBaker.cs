using System.Globalization;
using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 把 TMX 的**翻转/旋转**烘进图集：纯 XNB 没有 SMAPI 的 <c>SDisplayDevice</c>，
/// xTile 自己只按 <c>TileIndex</c> 取格子、不认任何翻转属性 ⇒ 只能把变换后的像素
/// **作为新的图块格**追加到图集里，再把地图上的 <c>TileIndex</c> 指过去。
///
/// <para>
/// <b>输入</b>：<see cref="TmxToTbin"/> 在带翻转位的图块上留下的
/// <see cref="TmxToTbin.FlipProperty"/>（值 <c>"rotation,effect"</c>，与 SMAPI 的
/// <c>@Rotation</c>/<c>@Flip</c> 同义）。
/// </para>
/// <para>
/// <b>为什么不改图集里的原格</b>：同一个图块索引可能被别的**不翻转**的格子引用，
/// 原地改会把那些格子也翻掉。追加新格是唯一安全的做法。
/// </para>
/// <para>
/// <b>季节图集</b>：游戏按文件名前缀 <c>spring_/summer_/fall_/winter_</c> 换图
/// （<c>GameLocation.GetSeasonalTilesheetName</c>），图块索引不变 ⇒ 新追加的索引必须在
/// <b>四个季节的图里都存在</b>。这里对同组的每份季节图都追加同一个索引
/// （内容各自变换自己那一格）。
/// </para>
/// <para>
/// <b>属性</b>：逐块属性（<c>@TileIndex@N@Passable</c> 等）按新索引**复制一份**，
/// 否则翻转过的墙/水会失去通行性判定。
/// </para>
/// </summary>
public sealed class FlipBaker
{
    private readonly ImageCache _images;

    /// <summary>同一组图集共用的追加位次（跨地图共享，避免同一变换重复追加）。</summary>
    private sealed class GroupState
    {
        public int NextIndex;
        public readonly Dictionary<(int Index, int Rotation, int Effect), int> Allocated = new();
    }

    private readonly Dictionary<string, GroupState> _groups = new(StringComparer.OrdinalIgnoreCase);

    public FlipBaker(ImageCache images) => _images = images;

    /// <summary>已烘焙的图块数（诊断用）。</summary>
    public int Baked { get; private set; }

    /// <summary>这一轮落盘时收集的说明（失败/跳过原因）。</summary>
    public List<string> Notes { get; } = new();

    /// <summary>地图里还有没有未处理的翻转标记（没有就完全不用碰图集）。</summary>
    public static bool HasMarkers(TBin10 map)
    {
        if (map.Layers == null) return false;
        foreach (Layer layer in map.Layers)
        {
            if (layer.Tiles == null) continue;
            foreach (BaseTile? tile in layer.Tiles)
            {
                if (tile?.Properties == null) continue;
                if (tile.Properties.Any(p => p.Key == TmxToTbin.FlipProperty)) return true;
            }
        }
        return false;
    }

    /// <summary>没有图片模型时至少别把内部标记写进 tbin（游戏不认这个键）。</summary>
    public static void StripMarkers(TBin10 map)
    {
        if (map.Layers == null) return;
        foreach (Layer layer in map.Layers)
        {
            if (layer.Tiles == null) continue;
            foreach (BaseTile? tile in layer.Tiles)
                tile?.Properties?.RemoveAll(p => p.Key == TmxToTbin.FlipProperty);
        }
    }

    /// <summary>
    /// 就地烘焙一张地图；返回本张图处理的翻转图块数。
    /// <paramref name="mapRel"/> 用于解析图集引用里的 <c>../</c>（相对地图文件目录）。
    /// </summary>
    public int Bake(TBin10 map, string mapRel = "")
    {
        int count = 0;
        if (map.Layers == null) return 0;
        foreach (Layer layer in map.Layers)
        {
            if (layer.Tiles == null) continue;
            foreach (BaseTile? tile in layer.Tiles)
            {
                if (tile?.Properties == null) continue;
                Propertie? marker = tile.Properties.FirstOrDefault(p => p.Key == TmxToTbin.FlipProperty);
                if (marker == null) continue;

                // 标记无论成败都要删掉 —— 它绝不能进最终 tbin（游戏不认这个键，留在那里只是垃圾）。
                tile.Properties.Remove(marker);

                if (!TryParse(marker.Value, out int rotation, out int effect))
                {
                    Notes.Add($"翻转标记无法解析：`{marker.Value}`（按不翻转保留）");
                    continue;
                }

                bool ok = tile switch
                {
                    AnimatedTile anim => BakeAnimated(map, anim, rotation, effect, mapRel),
                    StaticTile stat => BakeStatic(map, stat, rotation, effect, mapRel),
                    _ => false,
                };
                if (ok) count++;
            }
        }
        Baked += count;
        return count;
    }

    private static bool TryParse(object? value, out int rotation, out int effect)
    {
        rotation = effect = 0;
        string text = value?.ToString() ?? "";
        string[] parts = text.Split(',');
        return parts.Length == 2
               && int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out rotation)
               && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out effect);
    }

    private bool BakeAnimated(TBin10 map, AnimatedTile tile, int rotation, int effect, string mapRel)
    {
        if (tile.Frames == null || tile.Frames.Count == 0) return false;
        bool all = true;
        foreach (StaticTile frame in tile.Frames)
            all &= BakeTile(map, frame, rotation, effect, mapRel, out _);
        return all;
    }

    private bool BakeStatic(TBin10 map, StaticTile tile, int rotation, int effect, string mapRel) =>
        BakeTile(map, tile, rotation, effect, mapRel, out _);

    private bool BakeTile(TBin10 map, StaticTile tile, int rotation, int effect, string mapRel,
        out string? failure)
    {
        failure = null;
        TileSheet? sheet = FindSheet(map, tile.TileSheet);
        if (sheet == null)
        {
            failure = $"找不到图集 `{tile.TileSheet}`";
            return Fail(failure);
        }

        int tileW = sheet.TileSize.X, tileH = sheet.TileSize.Y;
        if (tileW <= 0 || tileH <= 0) tileW = tileH = 16;
        if (tileW != tileH)
        {
            failure = $"图集 `{sheet.Id}` 的格尺寸不是正方形（{tileW}×{tileH}），旋转烘焙不支持";
            return Fail(failure);
        }

        string? asset = ResolveAsset(sheet.Image, mapRel);
        if (asset == null)
        {
            failure = $"图集 `{sheet.Id}` 的图片 `{sheet.Image}` 不在产出树里";
            return Fail(failure);
        }

        // 季节变体 + 每份的**语言变体**：中文存档加载的是 `spring_X.zh-CN`，
        // 新追加的索引必须在玩家实际会加载的那张图里也存在，否则索引越界（图块直接不画）。
        var variants = new List<string>();
        foreach (string season in SeasonalVariants(asset))
        {
            if (_images.Exists(season + ".png")) variants.Add(season);
            foreach (string suffix in LocaleVariants.Suffixes)
                if (_images.Exists(season + suffix + ".png")) variants.Add(season + suffix);
        }
        if (variants.Count == 0)
        {
            failure = $"图集 `{sheet.Id}` 的图片 `{asset}` 不在产出树里";
            return Fail(failure);
        }

        int index = tile.TileIndex;
        string group = GroupKey(asset);
        if (!_groups.TryGetValue(group, out GroupState? state))
        {
            state = new GroupState { NextIndex = variants.Max(v => CellCount(v, tileW, tileH)) };
            _groups[group] = state;
        }

        int newIndex;
        if (!state.Allocated.TryGetValue((index, rotation, effect), out newIndex))
        {
            newIndex = state.NextIndex++;

            // ★ 每个变体都要拿到**同一索引**的像素：中文存档加载 `.zh-CN`，只补基础图
            //   会让中文那张的索引越界 → 整块黑洞。某个变体缺这一格时，用其它变体
            //   的变换结果兜底（几何相同，最多季节色调略有差异），绝不能留空。
            var cells = new List<(string Asset, byte[]? Cell)>();
            byte[]? fallback = null;
            foreach (string variant in variants)
            {
                if (!_images.TryGetPixels(variant + ".png", out byte[] px, out int w, out int h,
                        out string? readError))
                {
                    Notes.Add($"`{variant}` 的翻转格烘焙失败：{readError}");
                    continue;
                }

                int columns = w / tileW, rows = h / tileH;
                if (index < 0 || index >= columns * rows)
                {
                    Notes.Add($"`{variant}` 的源格 {index} 越界（{columns}×{rows}），用其它变体的像素补齐");
                    cells.Add((variant, null));
                    continue;
                }

                byte[] cell = TransformCell(px, w, h, tileW, index, rotation, effect);
                fallback ??= cell;
                cells.Add((variant, cell));
            }

            if (fallback == null)
            {
                failure = "没有任何季节图能追加新格";
                return Fail(failure);
            }

            bool any = false;
            foreach ((string variant, byte[]? cell) in cells)
            {
                if (AppendCell(variant, newIndex, cell ?? fallback, tileW, tileH, out string? why))
                    any = true;
                else
                    Notes.Add($"`{variant}` 的翻转格烘焙失败：{why}");
            }
            if (!any)
            {
                failure = "没有任何季节图能追加新格";
                return Fail(failure);
            }
            state.Allocated[(index, rotation, effect)] = newIndex;
        }

        CopyIndexProperties(sheet, index, newIndex);
        tile.TileIndex = newIndex;
        return true;
    }

    private bool Fail(string reason)
    {
        Notes.Add(reason);
        return false;
    }

    private static TileSheet? FindSheet(TBin10 map, string id) =>
        map.TileSheets?.FirstOrDefault(s => s != null
            && string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>图集引用 → 产出树里的内容路径（不含 <c>.png</c>）。</summary>
    private string? ResolveAsset(string? image, string mapRel)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        string cleaned = image.Replace('\\', '/').Trim().TrimStart('/');

        // 图集引用按**地图文件所在目录**解析：`Maps/Mines/19` 的 `mine` ⇒ `Maps/Mines/mine`，
        // `Maps/Foo` 的 `../TileSheets/furniture` ⇒ `TileSheets/furniture`。
        string content;
        if (cleaned.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase))
            content = cleaned;
        else if (mapRel.Length > 0)
        {
            string mapDir = Path.GetDirectoryName(mapRel.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
            content = MapPatcher.NormalizeSegments(mapDir + "/" + cleaned);
        }
        else
            content = cleaned;

        string png = content.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? content : content + ".png";
        string[] candidates = mapRel.Length > 0 ? new[] { png } : new[] { png, "Maps/" + png };

        foreach (string candidate in candidates)
        {
            if (_images.Exists(candidate)) return candidate[..^4];
            // 基础资产不在、但玩家实际会加载的语言变体在 ⇒ 仍然可以烘焙。
            foreach (string suffix in LocaleVariants.Suffixes)
                if (_images.Exists(candidate[..^4] + suffix + ".png")) return candidate[..^4];
        }
        return null;
    }

    /// <summary>季节图组：同一张图集在四季里的四个文件（只返回存在的由调用方过滤）。</summary>
    private static IEnumerable<string> SeasonalVariants(string asset)
    {
        string fileName = asset[(asset.LastIndexOf('/') + 1)..];
        string? dir = asset.LastIndexOf('/') >= 0 ? asset[..asset.LastIndexOf('/')] : null;
        if (!IsSeasonal(fileName))
        {
            yield return asset;
            yield break;
        }

        string suffix = fileName[fileName.IndexOf('_')..];
        foreach (string season in new[] { "spring", "summer", "fall", "winter" })
            yield return dir == null ? season + suffix : dir + "/" + season + suffix;
    }

    private static bool IsSeasonal(string fileName) =>
        fileName.StartsWith("spring_", StringComparison.Ordinal)
        || fileName.StartsWith("summer_", StringComparison.Ordinal)
        || fileName.StartsWith("fall_", StringComparison.Ordinal)
        || fileName.StartsWith("winter_", StringComparison.Ordinal);

    /// <summary>四个季节图共用的键：把文件名前缀换成占位符，保证追加位次一致。</summary>
    private static string GroupKey(string asset)
    {
        string fileName = asset[(asset.LastIndexOf('/') + 1)..];
        if (!IsSeasonal(fileName)) return asset;
        string dir = asset.LastIndexOf('/') >= 0 ? asset[..(asset.LastIndexOf('/') + 1)] : "";
        return dir + "__season__" + fileName[fileName.IndexOf('_')..];
    }

    private int CellCount(string asset, int tileW, int tileH)
    {
        if (!_images.TryGetPixels(asset + ".png", out _, out int w, out int h, out _)) return 0;
        return (w / tileW) * (h / tileH);
    }

    private bool AppendCell(string asset, int newIndex, byte[] cell,
        int tileW, int tileH, out string? error)
    {
        error = null;
        string rel = asset + ".png";
        if (!_images.TryGetPixels(rel, out byte[] px, out int width, out int height, out string? readError))
        {
            error = readError;
            return false;
        }

        int columns = width / tileW;
        int newColumn = newIndex % columns, newRow = newIndex / columns;
        int needHeight = (newRow + 1) * tileH;
        if (needHeight > height)
        {
            byte[] grown = new byte[width * needHeight * 4];
            Array.Copy(px, grown, px.Length);
            px = grown;
            height = needHeight;
        }

        int dst = ((newRow * tileH) * width + newColumn * tileW) * 4;
        for (int y = 0; y < tileH; y++)
        {
            Array.Copy(cell, y * tileW * 4, px, dst + y * width * 4, tileW * 4);
        }

        _images.SetPixels(rel, px, width, height);
        return true;
    }

    /// <summary>
    /// 取一格出来做<b>与 SMAPI 的 <c>SDisplayDevice</c> 等价</b>的变换：
    /// 先按 <paramref name="effect"/>（XNA <c>SpriteEffects</c>）翻采样，再按
    /// <paramref name="rotation"/> 绕格中心旋转。
    /// </summary>
    private static byte[] TransformCell(byte[] pixels, int width, int height,
        int size, int index, int rotation, int effect)
    {
        int columns = width / size;
        int sx0 = (index % columns) * size;
        int sy0 = (index / columns) * size;
        var outCell = new byte[size * size * 4];

        for (int dy = 0; dy < size; dy++)
        {
            for (int dx = 0; dx < size; dx++)
            {
                // 以格中心为原点的 2 倍坐标（避免 16 格时出现 7.5 这样的半整数）。
                int cx = 2 * dx - (size - 1), cy = 2 * dy - (size - 1);
                int rx, ry;
                switch (rotation)
                {
                    case 90: rx = cy; ry = -cx; break;
                    case -90: rx = -cy; ry = cx; break;
                    case 180: rx = -cx; ry = -cy; break;
                    default: rx = cx; ry = cy; break;
                }
                if ((effect & 1) != 0) rx = -rx;   // SpriteEffects.FlipHorizontally
                if ((effect & 2) != 0) ry = -ry;   // SpriteEffects.FlipVertically

                int sx = (rx + (size - 1)) / 2;
                int sy = (ry + (size - 1)) / 2;
                int src = ((sy0 + sy) * width + (sx0 + sx)) * 4;
                int dst = (dy * size + dx) * 4;
                outCell[dst] = pixels[src];
                outCell[dst + 1] = pixels[src + 1];
                outCell[dst + 2] = pixels[src + 2];
                outCell[dst + 3] = pixels[src + 3];
            }
        }
        return outCell;
    }

    /// <summary>把原索引的逐块属性复制到新索引（否则翻转后的墙/水会失去通行性）。</summary>
    private static void CopyIndexProperties(TileSheet sheet, int from, int to)
    {
        if (sheet.Properties == null || from == to) return;
        string fromPrefix = "@TileIndex@" + from.ToString(CultureInfo.InvariantCulture) + "@";
        string toPrefix = "@TileIndex@" + to.ToString(CultureInfo.InvariantCulture) + "@";

        var existing = new HashSet<string>(
            sheet.Properties.Where(p => p.Key != null && p.Key.StartsWith(toPrefix, StringComparison.Ordinal))
                .Select(p => p.Key!),
            StringComparer.Ordinal);

        foreach (Propertie p in sheet.Properties.ToList())
        {
            if (p.Key == null || !p.Key.StartsWith(fromPrefix, StringComparison.Ordinal)) continue;
            string key = toPrefix + p.Key[fromPrefix.Length..];
            if (existing.Add(key))
                sheet.Properties.Add(new Propertie { Key = key, Type = p.Type, Value = p.Value });
        }
    }
}
