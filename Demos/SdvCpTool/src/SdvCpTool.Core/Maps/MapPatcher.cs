using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Maps;

/// <summary>CP <c>PatchMode</c>（<c>StardewModdingAPI.dll</c> 的 <c>PatchMapMode</c>）。</summary>
public enum MapPatchMode
{
    /// <summary>只替换源区域里有图块的格子；目标原样保留源里为空的位置。</summary>
    Overlay,

    /// <summary>源地图里存在的图层，整层替换（源里为空也替换）。</summary>
    ReplaceByLayer,

    /// <summary>用源地图整个替换（源里没有的图层就地清空）。</summary>
    Replace,
}

/// <summary>
/// SMAPI 的 <c>IAssetDataForMap.PatchMap</c> / <c>ExtendMap</c> —— <b>逐段照抄</b>
/// （<c>StardewModdingAPI.dll</c> 反编译）。
/// <para>
/// <b>为什么必须照抄而不是自己实现</b>：CP 的 <c>EditMap</c> 带 <c>FromFile</c> 时
/// （<c>EditMapPatch.cs:216</c>）把合并交给这里。语义里有好几处只有读了实现才知道的细节 ——
/// 图集按 <b>Id + ImageSource</b> 双重匹配、不匹配就新建 <c>z_&lt;id&gt;</c>、
/// 图层按 Id 匹配、<c>Replace</c> 会把源里没有的图层<b>就地清空</b>。猜错任何一处都会写出坏地图。
/// </para>
/// <para>
/// 差异仅一处：SMAPI 用<b>反射</b>改 <c>xTile</c> 的私有字段，我们直接改 <see cref="Layer"/>
/// 的 <c>Tiles</c> 再 <see cref="LayerRle.Rebuild"/> —— tbin 的 RLE 是模型里的一等公民，
/// 不重建就会写出与模型对不上的二进制。
/// </para>
/// </summary>
public static class MapPatcher
{
    /// <summary>把每张图层撑到至少 <paramref name="minWidth"/> x <paramref name="minHeight"/>（补空格子）。</summary>
    public static void ExtendMap(TBin10 map, int minWidth, int minHeight)
    {
        foreach (Layer layer in map.Layers)
        {
            if (layer.LayerSize.X >= minWidth && layer.LayerSize.Y >= minHeight) continue;

            int w = Math.Max(minWidth, layer.LayerSize.X);
            int h = Math.Max(minHeight, layer.LayerSize.Y);
            var grown = new List<BaseTile>(w * h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    grown.Add(y < layer.LayerSize.Y && x < layer.LayerSize.X
                        ? layer.Tiles[y * layer.LayerSize.X + x]
                        : null!);

            layer.Tiles = grown;
            layer.LayerSize = new IntVector2 { X = w, Y = h };
            LayerRle.Rebuild(layer);
        }
    }

    /// <summary>
    /// 把 <paramref name="source"/> 的 <paramref name="sourceArea"/> 合进
    /// <paramref name="target"/> 的 <paramref name="targetArea"/>。
    /// 区域缺省时取源/目标的整幅范围（同 SMAPI）。
    /// </summary>
    /// <param name="targetAsset">
    /// **目标地图的资产名**（如 `Maps/Mines/19`）—— 用来把源地图的图集引用**按目标目录重新定基**
    /// （源地图的引用是相对 `Maps/` 的；目标地图的引用是相对**它自己那层目录**解析的）。
    /// 为 <c>null</c> 时不重定基（旧行为，给不关心这层的调用方）。
    /// </param>
    public static bool PatchMap(TBin10 target, TBin10 source,
        (int X, int Y, int W, int H)? sourceArea, (int X, int Y, int W, int H)? targetArea,
        MapPatchMode mode, out string? error, string? targetAsset = null)
    {
        error = null;

        (int sw, int sh) = MapArea(source);
        (int tw, int th) = MapArea(target);
        (int X, int Y, int W, int H) sa = sourceArea ?? (0, 0, sw, sh);
        (int X, int Y, int W, int H) ta = targetArea ?? (0, 0, Math.Min(sa.W, tw), Math.Min(sa.H, th));

        if (sa.X < 0 || sa.Y < 0 || sa.X + sa.W > sw || sa.Y + sa.H > sh)
        { error = $"源区域 ({sa.X},{sa.Y},{sa.W},{sa.H}) 超出源地图 {sw}x{sh}"; return false; }
        if (ta.X < 0 || ta.Y < 0 || ta.X + ta.W > tw || ta.Y + ta.H > th)
        { error = $"目标区域 ({ta.X},{ta.Y},{ta.W},{ta.H}) 超出目标地图 {tw}x{th}"; return false; }
        if (sa.W != ta.W || sa.H != ta.H)
        { error = $"源区域 {sa.W}x{sa.H} 与目标区域 {ta.W}x{ta.H} 尺寸不等"; return false; }

        // ── 图集：按 Id 找；找不到**或 ImageSource 不同**就新建一个 z_<id> ──
        // 键用**图集 Id 字符串**：瓦片里记的就是 Id（`StaticTile.TileSheet`），
        // 用对象做键还得反查一遍，凭空多一处能对不上的地方。
        var sheetMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TileSheet srcSheet in source.TileSheets ?? new List<TileSheet>())
        {
            target.TileSheets ??= new List<TileSheet>();
            TileSheet? dst = target.TileSheets.FirstOrDefault(s => s.Id == srcSheet.Id);
            if (dst == null
                || NormalizeTilesheet(dst.Image) != NormalizeTilesheet(srcSheet.Image))
            {
                string id = srcSheet.Id;
                if (!id.StartsWith("z_", StringComparison.OrdinalIgnoreCase)) id = "z_" + id;
                if (target.TileSheets.Any(s => s.Id == id))
                {
                    int suffix = 2;
                    while (target.TileSheets.Any(s => s.Id == $"{id}_{suffix}")) suffix++;
                    id = $"{id}_{suffix}";
                }
                dst = new TileSheet
                {
                    Id = id,
                    Description = srcSheet.Description ?? "",
                    // ⚠ 源地图的图集引用基准是**相对 `Maps/`**（`LocalMapTilesheets` 先跑一遍把它归一成
                    //   那个形态）—— 这里按这个基准相对**目标地图自己那层目录**写回去，**只定基一次**。
                    //   目标地图自己那些图集引用不经过这里（那是 `LocalMapTilesheets` 直接写的），
                    //   所以同一份引用不会被定基两次（实测踩过：`Maps/Mines/19` 里出现 `../../mine`）。
                    Image = Rebase(srcSheet.Image, targetAsset),
                    SheetSize = srcSheet.SheetSize,
                    TileSize = srcSheet.TileSize,
                    Margin = srcSheet.Margin,
                    Spacing = srcSheet.Spacing,
                    Properties = CopyProps(srcSheet.Properties),
                };
                target.TileSheets.Add(dst);
            }
            sheetMap[srcSheet.Id] = dst.Id;
        }

        // ── 图层：按 Id 匹配；源里有而目标没有的，新建 ──
        var layerMap = new Dictionary<Layer, Layer>();
        foreach (Layer srcLayer in source.Layers)
        {
            Layer? dst = target.Layers.FirstOrDefault(l => l.Id == srcLayer.Id);
            if (dst != null) layerMap[srcLayer] = dst;
        }
        var untouched = new HashSet<Layer>(target.Layers.Where(l => !layerMap.Values.Contains(l)));

        bool replace = mode == MapPatchMode.Replace;
        bool replaceByLayer = mode == MapPatchMode.ReplaceByLayer;
        var touched = new HashSet<Layer>();

        for (int dx = 0; dx < sa.W; dx++)
        {
            for (int dy = 0; dy < sa.H; dy++)
            {
                int sx = sa.X + dx, sy = sa.Y + dy;
                int tx = ta.X + dx, ty = ta.Y + dy;

                if (replace)
                {
                    foreach (Layer l in untouched)
                        SetTile(l, tx, ty, null!, touched);
                }

                foreach (Layer srcLayer in source.Layers)
                {
                    if (!layerMap.TryGetValue(srcLayer, out Layer? dstLayer))
                    {
                        // 目标没有这个图层 → 按目标首层尺寸新建（同 SMAPI `target.Layers[0].LayerSize`）。
                        IntVector2 size = target.Layers[0].LayerSize;
                        dstLayer = new Layer
                        {
                            Id = srcLayer.Id,
                            Visible = srcLayer.Visible,
                            Description = "",
                            LayerSize = new IntVector2 { X = size.X, Y = size.Y },
                            TileSize = srcLayer.TileSize,
                            Properties = CopyProps(srcLayer.Properties),
                            Tiles = Enumerable.Repeat<BaseTile>(null!, size.X * size.Y).ToList(),
                            Index = new List<char>(),
                            _sizeArr = new List<int>(),
                            _currTileSheet = new List<string> { "" },
                        };
                        target.Layers.Add(dstLayer);
                        layerMap[srcLayer] = dstLayer;
                    }
                    else
                    {
                        // SMAPI 在这里 `CopyFrom(layer.Properties)` —— 源图层的属性盖到目标图层上。
                        dstLayer.Properties = CopyProps(srcLayer.Properties);
                    }
                    touched.Add(dstLayer);

                    BaseTile? srcTile = srcLayer.Tiles[sy * srcLayer.LayerSize.X + sx];
                    BaseTile? newTile = srcTile == null
                        ? null
                        : CreateTile(srcTile, sheetMap);

                    // SMAPI：`tile2 != null || ReplaceByLayer || Replace` 才赋值 ——
                    // Overlay 模式下源里为空的位置**保留目标原样**。
                    if (newTile != null || replaceByLayer || replace)
                        SetTile(dstLayer, tx, ty, newTile!, touched);
                }
            }
        }

        foreach (Layer l in touched) LayerRle.Rebuild(l);
        return true;
    }

    /// <summary>SMAPI <c>CreateTile</c>：静态照抄；动画逐帧换成目标图集的静态块，保留间隔。</summary>
    private static BaseTile? CreateTile(BaseTile srcTile, Dictionary<string, string> sheetMap)
    {
        // 源块记的是**图集 Id**；映射取目标图集 Id，映射里没有就沿用原值（同 SMAPI）。
        string Sheet(string id) => sheetMap.TryGetValue(id, out string? mapped) ? mapped : id;

        if (srcTile is StaticTile st)
        {
            return new StaticTile
            {
                TileSheet = Sheet(st.TileSheet),
                TileIndex = st.TileIndex,
                BlendMode = st.BlendMode,
                Properties = CopyProps(st.Properties),
            };
        }

        if (srcTile is AnimatedTile at)
        {
            var frames = new List<StaticTile>();
            foreach (StaticTile f in at.Frames)
            {
                frames.Add(new StaticTile
                {
                    TileSheet = Sheet(f.TileSheet),
                    TileIndex = f.TileIndex,
                    BlendMode = f.BlendMode,
                    Properties = CopyProps(f.Properties),
                });
            }
            AnimatedTile built = LayerRle.BuildAnimatedTile(at.FrameInterval, frames);
            built.Properties = CopyProps(at.Properties);
            return built;
        }

        return null;   // TBin10 的 BaseTile 只有上面两种，这里不可达
    }

    private static void SetTile(Layer layer, int x, int y, BaseTile tile, HashSet<Layer> touched)
    {
        if (x < 0 || y < 0 || x >= layer.LayerSize.X || y >= layer.LayerSize.Y) return;
        layer.Tiles[y * layer.LayerSize.X + x] = tile;
        touched.Add(layer);
    }

    private static List<Propertie> CopyProps(List<Propertie>? src) =>
        src == null
            ? new List<Propertie>()
            : src.Select(p => new Propertie { Key = p.Key, Type = p.Type, Value = p.Value }).ToList();

    private static (int W, int H) MapArea(TBin10 map)
    {
        int w = 0, h = 0;
        foreach (Layer l in map.Layers)
        {
            if (l.LayerSize.X > w) w = l.LayerSize.X;
            if (l.LayerSize.Y > h) h = l.LayerSize.Y;
        }
        return (w, h);
    }

    /// <summary>
    /// 把源地图的图集引用**按目标地图的目录重新定基**。
    /// <para>
    /// 为什么不照抄：地图的图集引用是相对**地图自己那层目录**解析的 ——
    /// `Maps/Mines/19` 里的 `Mines/mine` 会被游戏读成 `Maps/Mines/Mines/mine`（实测异常链里正是这个），
    /// 而 `../TileSheets/furniture` 才能到顶层的 `TileSheets/`。
    /// 源地图（包里的 tbin/tmx）的引用一律是**相对 `Maps/`** 的（`LocalMapTilesheets` 已经把包内同级图集
    /// 落成了 `Maps/&lt;名&gt;` 并改写过引用），所以这里把 `Maps/&lt;源引用&gt;` 归一再相对目标目录写回去。
    /// </summary>
    /// <param name="alreadyContent">
    /// <paramref name="image"/> 已经是**内容键**（`Maps/…` 或不带前缀的「相对 Maps/」内容路径，
    /// 例如 `TileSheets/furniture`）—— 这时**不能**再补 `Maps/`。调用方 `LocalMapTilesheets` 传进来的
    /// 就是内容键；`PatchMap` 传进来的是源地图里的原始引用（可能是 `Mines/mine`，也可能是
    /// `Maps/Mines/mine.png` 或 `../TileSheets/x`）。
    /// </param>
    public static string Rebase(string? image, string? targetAsset, bool alreadyContent = false)
    {
        string raw = (image ?? "").Replace(Path.DirectorySeparatorChar, '/').Trim();
        if (targetAsset == null || raw.Length == 0 || raw.StartsWith("/", StringComparison.Ordinal))
            return raw;

        // 源引用 → 内容路径：`Mines/mine` ⇒ `Maps/Mines/mine`（源引用默认相对 `Maps/`）；
        // `../TileSheets/x` ⇒ `TileSheets/x`（出 Maps 到顶层）；
        // ⚠ 已经是 `Maps/…` 的**不能再补一个**（包自己会写这种绝对一点的写法，实测 `[CP] East Scarp` 的
        //   `Maps/Mines/mine.png`）—— 补了会变成 `Maps/Maps/Mines/mine`，那张地图照样建不起来。
        string content = NormalizeSegments(
            alreadyContent || raw.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)
                ? raw
                : "Maps/" + raw);
        if (content.Length == 0) return raw;

        // 目标目录（`Maps/Mines/19` ⇒ `Maps/Mines`；`Maps/Town` ⇒ `Maps`）
        string targetDir = NormalizeSegments(targetAsset.Replace(Path.DirectorySeparatorChar, '/').Trim());
        int slash = targetDir.LastIndexOf('/');
        targetDir = slash < 0 ? "" : targetDir[..slash];

        // 相对化：从 targetDir 走到 content
        string[] want = content.Split('/');
        string[] from = targetDir.Length == 0 ? Array.Empty<string>() : targetDir.Split('/');
        int common = 0;
        while (common < want.Length - 1 && common < from.Length && want[common] == from[common]) common++;

        var parts = new List<string>();
        for (int i = common; i < from.Length; i++) parts.Add("..");
        parts.AddRange(want.Skip(common));
        return string.Join("/", parts);
    }

    /// <summary>
    /// 把**绝对内容路径**写成「**相对 `Maps/`**」的形态 —— 源地图（包里的 tbin/tmx）里图集引用的基准，
    /// 也正是 <see cref="Rebase"/>（<c>alreadyContent: false</c>）期待的输入形态。
    /// <para>
    /// ⚠ 这个形态**幂等**：`Maps/Mines/mine` ⇒ `Mines/mine` ⇒ 再算一遍还是 `Mines/mine`
    /// （归一时会先补回 `Maps/` 再解析回原样）。源地图对象会被 `MapCache` 缓存、被多条补丁**反复**
    /// 拿去用，写进去的形态必须幂等 —— 否则每条补丁都会在同一个引用上再加层
    /// （实测 `Mines/mine.png` 被 4 条补丁逐次写成 `mine` ⇒ `../mine` ⇒ `../../mine`，
    /// 最后指向内容根、那张地图建不起来）。
    /// </para>
    /// </summary>
    public static string MapsRelative(string contentKey)
    {
        string content = NormalizeSegments(contentKey);
        if (content.Length == 0 || string.Equals(content, "Maps", StringComparison.OrdinalIgnoreCase))
            return "";
        return content.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? content[5..] : "../" + content;
    }

    /// <summary>把路径段里的 <c>.</c>/<c>..</c> 归一掉（`Maps/../X` ⇒ `X`）。</summary>
    public static string NormalizeSegments(string path)
    {
        var stack = new List<string>();
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(part);
        }
        return string.Join("/", stack);
    }

    /// <summary>照抄 SMAPI 的 <c>NormalizeTilesheetPathForComparison</c>。</summary>
    private static string NormalizeTilesheet(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string p = path.Trim().Replace(Path.DirectorySeparatorChar, '/');
        if (p.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)) p = p[5..];
        if (p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) p = p[..^4];
        return p;
    }
}
