using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// TMX（Tiled 的 XML）→ <see cref="TBin10"/>。
/// <para>
/// <b>为什么不引 <c>TMXTile.dll</c></b>（计划里原本建议先引它打通端到端）：
/// 它产出的是 <c>xTile.Map</c>，而我们的 tbin 写出器吃 <see cref="TBin10"/> ——
/// <b>引了也省不掉这层转换</b>，只是把「解析 XML」换成「遍历对象模型」。
/// 而 <c>XnbConverter</c> 自己的注释就写着 xTile.dll 这类程序集<b>加载会失败</b>。
/// 需要的 TMX 子集很小（正交、<c>infinite=0</c>、CSV 或 base64+zlib），
/// 且 zlib 与 base64 都在 BCL 里。
/// </para>
/// <para>
/// <b>★ 一条容易踩空的约定</b>：Stardew 的 tmx 把「图块属性」写成 Tiled <b>对象</b> ——
/// objectgroup 的 <c>name</c> 与图层同名，里面的对象 <c>name="TileData"</c>、按像素坐标画在格子上，
/// 属性要<b>折算到那一格的 tile 上</b>。当成无关数据丢掉就是丢属性
/// （实测 SVE 有 11704 个这种对象，带 <c>NPCBarrier</c>/<c>Type</c>/<c>Action</c> 之类）。
/// </para>
/// <para>
/// <b>翻转/旋转</b>（gid 高 3 位）：xTile 自己**不认**（tbin 里只有 index + blend mode），
/// 但 SMAPI 的 <c>SDisplayDevice</c> 会读 TMXTile 写在图块上的 <c>@Rotation</c>/<c>@Flip</c>
/// 属性并画出来 —— 纯 XNB 没有 SMAPI，所以这里把**变换参数记在图块上**
/// （<see cref="FlipProperty"/>），由 <see cref="FlipBaker"/> 在落盘时把变换**烘进图集**。
/// 映射公式照抄 <c>TMXTile.dll</c> 的 <c>GetRotationForFlippedTile</c>/<c>GetEffectForFlippedTile</c>。
/// </para>
/// <para>
/// <b>不做的事（宁可拒绝，不可猜）</b>：非正交、无限地图、group/imagelayer 等嵌套结构 ——
/// 一律失败并说明原因。
/// </para>
/// </summary>
public static class TmxToTbin
{
    private const byte StringType = 3;

    /// <summary>
    /// 翻转标记的图块属性名（落在 <c>StaticTile</c>/<c>AnimatedTile</c> 的 <c>Properties</c> 上）。
    /// 值 = <c>"&lt;rotation&gt;,&lt;effect&gt;"</c>（rotation ∈ {0,90,-90,180}；effect 是 XNA
    /// <c>SpriteEffects</c>：0=None、1=FlipHorizontally、2=FlipVertically）。
    /// <para>落盘前由 <see cref="FlipBaker"/> 消费并**删除**，最终 tbin 里不应残留。</para>
    /// </summary>
    public const string FlipProperty = "@SdvCpFlip";

    /// <summary>tile 的 gid 里翻转标志占高 3 位（Tiled 约定）。</summary>
    private const uint FlipH = 0x80000000, FlipV = 0x40000000, FlipD = 0x20000000;
    private const uint GidMask = 0x1FFFFFFF;

    /// <summary>
    /// 转一份 tmx。失败时 <paramref name="error"/> 说明原因，绝不返回半成品。
    /// <para>
    /// <paramref name="flippedTiles"/> = 带翻转/旋转标志的图块数。它们不再被丢弃：
    /// 变换参数写在图块的 <see cref="FlipProperty"/> 属性上，落盘前由 <see cref="FlipBaker"/>
    /// 烘进图集（纯 XNB 没有 SMAPI 的 <c>SDisplayDevice</c>，只能把结果画死）。
    /// </para>
    /// </summary>
    public static bool TryConvert(string tmxPath, out TBin10? map, out string? error,
        out int flippedTiles)
    {
        map = null;
        flippedTiles = 0;
        try
        {
            map = Convert(tmxPath, out flippedTiles);
            error = null;
            return true;
        }
        catch (NotSupportedException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = $"TMX 解析失败：{ex.Message}";
            return false;
        }
    }

    private static TBin10 Convert(string tmxPath, out int flippedTiles)
    {
        XElement root = XDocument.Load(tmxPath).Root
            ?? throw new NotSupportedException("tmx 内容为空");
        if (root.Name.LocalName != "map")
            throw new NotSupportedException($"根元素不是 map，而是 {root.Name.LocalName}");

        string orientation = Attr(root, "orientation") ?? "";
        if (orientation != "orthogonal")
            throw new NotSupportedException($"只支持正交地图（orientation=orthogonal），实际 `{orientation}`");
        if (Attr(root, "infinite") == "1")
            throw new NotSupportedException("不支持无限地图（infinite=1）");

        int width = Int(root, "width"), height = Int(root, "height");
        int tileW = Int(root, "tilewidth"), tileH = Int(root, "tileheight");
        if (width <= 0 || height <= 0 || tileW <= 0 || tileH <= 0)
            throw new NotSupportedException("地图尺寸或格尺寸非法");

        List<TilesetInfo> tilesets = ParseTilesets(root, tileW, tileH,
            Path.GetDirectoryName(Path.GetFullPath(tmxPath)) ?? ".");

        var map = new TBin10
        {
            Format = "tBIN10",
            Id = "Untitled",
            Description = "",
            Properties = ParseProperties(root.Element("properties")),
            TileSheets = tilesets.Select(t => t.Sheet).ToList(),
        };

        var layers = new List<Layer>();
        var byName = new Dictionary<string, Layer>(StringComparer.Ordinal);
        var flips = new int[1];
        foreach (XElement el in root.Elements("layer"))
        {
            Layer layer = BuildLayer(el, width, height, tileW, tileH, tilesets, flips);
            layers.Add(layer);
            byName[layer.Id] = layer;
        }
        flippedTiles = flips[0];

        // 图块属性：ObjectGroup 与图层**同名**，对象按像素坐标画在格子上。
        // ⚠ 名字对不上时**跳过**，不是拒绝整张图 —— 这一条照抄真实实现
        // `TMXTile.dll` 的 `LoadObjects`：`Layer layer = map.GetLayer(objectgroup.Name);
        // if (layer == null) continue;`。它**只做精确同名匹配**，没有别的回退，
        // 对不上的那组属性就是**不生效**（游戏里也如此）。
        // 曾经为此加了 `<图层>Spouse` 回退 —— 那会让我们**应用游戏不会应用**的属性，是偏的。
        foreach (XElement og in root.Elements("objectgroup"))
        {
            string name = Attr(og, "name") ?? "";
            if (!byName.TryGetValue(name, out Layer? target)) continue;
            ApplyTileData(target, og, width, height, tileW, tileH);
        }

        map.Layers = layers;
        return map;
    }

    // ────────────────────────────── 图集 ──────────────────────────────

    private sealed class TilesetInfo
    {
        public TileSheet Sheet = null!;
        public int FirstGid;
        public int TileCount;
        public Dictionary<int, List<Propertie>> TileProperties = new();
        public Dictionary<int, (int Interval, List<int> FrameIds)> Animations = new();
    }

    private static List<TilesetInfo> ParseTilesets(XElement root, int tileW, int tileH, string tmxDir)
    {
        var list = new List<TilesetInfo>();
        foreach (XElement tsRef in root.Elements("tileset"))
        {
            // ★ **外部 `.tsx`**：`<tileset firstgid="1" source="foo.tsx"/>` —— 定义在另一个文件里
            //   （相对 **tmx 所在的目录**解析）。实测语料 `Crystal Island` 等 16 条地图就是这么写的，
            //   以前一律拒 ⇒ **整张地图不写**。
            //   Tiled 的规矩：`firstgid` 在**引用处**、其余（name/tilewidth/image/tile…）在 tsx 里。
            XElement ts = tsRef;
            if (Attr(tsRef, "source") is string src && src.Length > 0)
            {
                string full = Path.GetFullPath(Path.Combine(tmxDir, src.Replace('\\', '/')));
                if (!File.Exists(full))
                    throw new NotSupportedException($"外部 tsx 不存在：`{src}`（相对 {tmxDir}）");
                XElement tsxRoot = XDocument.Load(full).Root
                    ?? throw new NotSupportedException($"外部 tsx 内容为空：`{src}`");
                if (tsxRoot.Name.LocalName != "tileset")
                    throw new NotSupportedException($"外部 tsx 的根元素不是 tileset，而是 {tsxRoot.Name.LocalName}：`{src}`");
                // 把引用处的 firstgid 借过来（tsx 自己没有这一项）
                ts = new XElement(tsxRoot);
                if (ts.Attribute("firstgid") == null && Attr(tsRef, "firstgid") is string fg)
                    ts.SetAttributeValue("firstgid", fg);
            }

            XElement image = ts.Element("image")
                ?? throw new NotSupportedException(ts.HasElements && ts.Elements("tile").Any(t => t.Element("image") != null)
                    ? "外部/内嵌 tileset 是**图集集合**（每格一张 <image>）—— tbin 的图集只有一张 sheet，表示不了"
                    : "只支持内嵌单个 <image> 的 tileset（不支持外部 tsx / 图集集合）");

            int tw = attrInt(ts, "tilewidth") ?? tileW;
            int th = attrInt(ts, "tileheight") ?? tileH;
            int imgW = Int(image, "width"), imgH = Int(image, "height");

            var info = new TilesetInfo
            {
                FirstGid = Int(ts, "firstgid"),
                TileCount = attrInt(ts, "tilecount") ?? 0,
                Sheet = new TileSheet
                {
                    // 实测（Island_House_Bin.tbin）：Id 是 tileset 的 **name**，Image 是 image 的 **source**。
                    Id = Attr(ts, "name") ?? "",
                    Description = "",
                    Image = Attr(image, "source") ?? "",
                    SheetSize = new IntVector2 { X = imgW / tw, Y = imgH / th },
                    TileSize = new IntVector2 { X = tw, Y = th },
                    Margin = new IntVector2 { X = 0, Y = 0 },
                    Spacing = new IntVector2 { X = 0, Y = 0 },
                    Properties = new List<Propertie>(),
                },
            };

            foreach (XElement tile in ts.Elements("tile"))
            {
                int id = Int(tile, "id");
                List<Propertie> props = ParseProperties(tile.Element("properties"));
                if (props.Count > 0) info.TileProperties[id] = props;

                XElement? anim = tile.Element("animation");
                if (anim != null)
                {
                    var frames = anim.Elements("frame")
                        .Select(f => (Interval: attrInt(f, "duration") ?? 0, TileId: Int(f, "tileid")))
                        .ToList();
                    if (frames.Count > 0)
                        info.Animations[id] = (frames[0].Interval, frames.Select(f => f.TileId).ToList());
                }
            }

            // ⚠ tileset 的**逐块属性**挂到**图集**上，键形如 `@TileIndex@<id>@<名>` ——
            // 这是真实实现的写法（`TMXTile.LoadTileSets`），也是原版的写法：
            // 实测原版 `Maps/Town.tbin` 里 `@TileIndex@…` 有 460 条、`@FirstGid` 0 条。
            // 游戏**先查** `tile.TileIndexProperties`（就是这套键），**没有再落** `tile.Properties`
            //（`GameLocation.cs:2784-2787`、`:2816-2820`）—— 挂在图块上会让**优先级反过来**：
            // 后续 `SetProperties` 覆盖本应被图集值遮住，却会生效。
            foreach ((int id, List<Propertie> props) in info.TileProperties)
                foreach (Propertie p in props)
                {
                    info.Sheet.Properties.Add(new Propertie
                    {
                        Key = $"@TileIndex@{id}@{p.Key}",
                        Type = p.Type,
                        Value = p.Value,
                    });
                }

            list.Add(info);
        }

        // ★ Tiled 允许两个 tileset **同名**（实测 `Cove_exit.tmx` 两个都叫
        //   `untitled tile sheet`，一个 25 列、一个 17 列）。tbin 的图块只靠 `'T'` 段里的
        //   **名字**找图集；同名会让 17 列那张的索引落到 25 列那张上（或者反过来），
        //   整张地图错位。这里给重名图集加 `_2`、`_3` 后缀，地图里的 'T' 段随之用新名。
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (TilesetInfo info in list)
        {
            string baseId = string.IsNullOrWhiteSpace(info.Sheet.Id) ? "tileset" : info.Sheet.Id;
            string id = baseId;
            for (int n = 2; !usedIds.Add(id); n++)
                id = $"{baseId}_{n}";
            info.Sheet.Id = id;
        }

        // **没有 tileset 是合法的** —— 只有对象、没有图块的地图（实测 RSV 有这种）。
        // 真有图块却找不到 tileset 时，Resolve() 会带着 gid 报错，不会静默放过。
        return list;
    }

    /// <summary>gid 落在哪个 tileset，及其在该图集内的局部下标。</summary>
    private static (TilesetInfo Info, int Local) Resolve(List<TilesetInfo> tilesets, uint gid)
    {
        TilesetInfo? hit = null;
        foreach (TilesetInfo ts in tilesets)
            if (gid >= (uint)ts.FirstGid && (hit == null || ts.FirstGid > hit.FirstGid))
                hit = ts;
        if (hit == null)
            throw new NotSupportedException($"gid {gid} 不在任何 tileset 的 firstgid 范围内");
        return (hit, (int)(gid - (uint)hit.FirstGid));
    }

    // ────────────────────────────── 图层 ──────────────────────────────

    private static Layer BuildLayer(XElement el, int width, int height, int tileW, int tileH,
        List<TilesetInfo> tilesets, int[] flips)
    {
        XElement data = el.Element("data")
            ?? throw new NotSupportedException($"图层 `{Attr(el, "name")}` 没有 <data>");
        uint[] gids = DecodeData(data, width, height);

        var layer = new Layer
        {
            Id = Attr(el, "name") ?? "",
            Visible = (byte)(Attr(el, "visible") == "0" ? 0 : 1),
            Description = "",
            LayerSize = new IntVector2 { X = width, Y = height },
            TileSize = new IntVector2 { X = tileW, Y = tileH },
            Properties = ParseProperties(el.Element("properties")),
            Tiles = new List<BaseTile>(),
            Index = new List<char>(),
            _sizeArr = new List<int>(),
            // 镜像读取侧：Read 先塞一个空串，之后每个 'T' 追加一个。
            _currTileSheet = new List<string> { "" },
        };

        string currentSheet = "";
        for (int y = 0; y < height; y++)
        {
            int x = 0;
            while (x < width)
            {
                uint raw = gids[y * width + x];

                if ((raw & GidMask) == 0)
                {
                    int run = 0;
                    while (x < width && (gids[y * width + x] & GidMask) == 0) { run++; x++; }
                    layer.Index.Add('N');
                    layer._sizeArr.Add(run);
                    for (int i = 0; i < run; i++) layer.Tiles.Add(null!);
                    continue;
                }

                // 翻转/旋转：把 TMXTile 的 (rotation, effect) 记在 图块 上，落盘时由
                // `FlipBaker` 把变换烘进图集（xTile 自己不会画；SMAPI 的 SDisplayDevice 才会画）。
                bool flipped = (raw & (FlipH | FlipV | FlipD)) != 0;
                if (flipped) flips[0]++;
                string? mark = flipped ? FlipMark(raw) : null;

                uint gid = raw & GidMask;
                (TilesetInfo info, int local) = Resolve(tilesets, gid);

                if (info.Animations.TryGetValue(local, out var anim))
                {
                    layer.Index.Add('A');
                    AnimatedTile animated = BuildAnimated(info, anim);
                    if (mark != null) animated.Properties.Add(Marker(mark));
                    layer.Tiles.Add(animated);
                }
                else
                {
                    if (!string.Equals(currentSheet, info.Sheet.Id, StringComparison.Ordinal))
                    {
                        layer.Index.Add('T');
                        layer._currTileSheet.Add(info.Sheet.Id);
                        currentSheet = info.Sheet.Id;
                    }
                    layer.Index.Add('S');
                    List<Propertie> props = CloneProps(info, local);
                    if (mark != null) props.Add(Marker(mark));
                    layer.Tiles.Add(new StaticTile
                    {
                        // TileSheet 在 tbin 里是**由 'T' 段隐含**的（StaticTileReader 不读它），
                        // 但显式填上让模型自描述 —— LayerRle.Rebuild 要靠它生成 'T' 段。
                        TileSheet = info.Sheet.Id,
                        TileIndex = local,
                        BlendMode = 0,
                        Properties = props,
                    });
                }
                x++;
            }
        }

        return layer;
    }

    private static Propertie Marker(string value) => new()
    {
        Key = FlipProperty,
        Type = StringType,
        Value = value,
    };

    /// <summary>
    /// Tiled 的 H/V/D 三个翻转位 ⇒ TMXTile 的 <c>(@Rotation, @Flip)</c>。
    /// 公式<b>逐字照抄</b> <c>TMXTile.dll</c> 的 <c>GetRotationForFlippedTile</c> /
    /// <c>GetEffectForFlippedTile</c> —— SMAPI 的 <c>SDisplayDevice</c> 就是拿这两个值画的，
    /// 我们的烘焙要复现同一个结果。
    /// </summary>
    private static string FlipMark(uint raw)
    {
        bool h = (raw & FlipH) != 0, v = (raw & FlipV) != 0, d = (raw & FlipD) != 0;

        int effect;
        if (d)
        {
            if (!v && !h) effect = 2;
            else if (h && v) effect = 2;
            else if (h) effect = 0;
            else /* v */ effect = 0;
        }
        else if (h && v) effect = 0;
        else if (h) effect = 1;
        else if (v) effect = 2;
        else effect = 0;

        int rotation;
        if (d)
        {
            if (!v && !h) rotation = 90;
            else if (h && v) rotation = -90;
            else if (h) rotation = 90;
            else /* v */ rotation = -90;
        }
        else if (h && v) rotation = 180;
        else rotation = 0;

        return rotation.ToString(CultureInfo.InvariantCulture) + "," +
               effect.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 动画帧。RLE 段的构造走 <see cref="LayerRle.BuildAnimatedTile"/> ——
    /// <b>只有一处实现</b>：动画块自己的 <c>Index</c>/<c>_currTileSheet</c> 忘了建，
    /// 就会写出帧数为 0 的动画块（实测代价见那边的注释）。
    /// </summary>
    private static AnimatedTile BuildAnimated(TilesetInfo info, (int Interval, List<int> FrameIds) anim)
    {
        var frames = anim.FrameIds.Select(frameId => new StaticTile
        {
            TileSheet = info.Sheet.Id,
            TileIndex = frameId,
            BlendMode = 0,
            Properties = CloneProps(info, frameId),
        }).ToList();

        return LayerRle.BuildAnimatedTile(anim.Interval, frames);
    }

    /// <summary>
    /// 图块的初始属性：**空**。
    /// <para>
    /// tileset 的逐块属性不挂在这里 —— 它在<b>图集</b>上（<c>@TileIndex@…</c>，见 <see cref="ParseTilesets"/>）。
    /// 挂两处看着"更保险"，其实会让<b>优先级反过来</b>：游戏先查图集那套、没有再落图块这套，
    /// 两边都有时图块那套赢 —— 与真实实现不一致。
    /// </para>
    /// <para>objectgroup 的 <c>TileData</c> 属性仍然挂在图块上（那是另一回事，见 <see cref="ApplyTileData"/>）。</para>
    /// </summary>
    private static List<Propertie> CloneProps(TilesetInfo info, int local) => new();

    // ─────────────────────────── 图块数据（对象）───────────────────────────

    /// <summary>
    /// 把 objectgroup 里的 <c>TileData</c> 对象折算到图块上。
    /// 没有 tile 的格子<b>跳过</b> —— 属性在 tbin 里只能挂在图块上，空格子无处可挂。
    /// <para>
    /// ⚠ <b>对象按 <c>Width</c>/<c>Height</c> 覆盖一个矩形</b>，不是只落在左上那一格 ——
    /// 这一条照抄真实实现（<c>TMXTile.dll</c> 的 <c>LoadObjects</c>：
    /// <c>for (i = x; i &lt; x + w/16; i++) for (j = y; j &lt; y + h/16; j++)</c>）。
    /// 只落一格会让宽对象的属性**漏掉大半**。
    /// </para>
    /// </summary>
    private static void ApplyTileData(Layer layer, XElement objectGroup, int width, int height,
        int tileW, int tileH)
    {
        foreach (XElement obj in objectGroup.Elements("object"))
        {
            if (Attr(obj, "name") != "TileData") continue;

            List<Propertie> props = ParseProperties(obj.Element("properties"));
            if (props.Count == 0) continue;

            int x0 = (int)(Double(obj, "x") / tileW);
            int y0 = (int)(Double(obj, "y") / tileH);
            int wTiles = Math.Max(1, (int)(Double(obj, "width") / tileW));
            int hTiles = Math.Max(1, (int)(Double(obj, "height") / tileH));

            for (int cx = x0; cx < x0 + wTiles; cx++)
            {
                for (int cy = y0; cy < y0 + hTiles; cy++)
                {
                    if (cx < 0 || cy < 0 || cx >= width || cy >= height) continue;
                    if (layer.Tiles[cy * width + cx] is not BaseTile tile) continue;   // 空格子：无处可挂

                    tile.Properties ??= new List<Propertie>();
                    foreach (Propertie p in props)
                    {
                        tile.Properties.RemoveAll(x => x.Key == p.Key);
                        tile.Properties.Add(p);
                    }
                }
            }
        }
    }

    // ────────────────────────────── 杂项 ──────────────────────────────

    /// <summary>
    /// 图层数据。支持 <c>csv</c>、<c>base64</c>（可带 <c>zlib</c> 压缩）与 Tiled 的 **XML 写法**
    /// （没有 <c>encoding</c> 属性时的 <c>&lt;data&gt;&lt;tile gid="13"/&gt;…&lt;/data&gt;</c>）；其余一律拒绝。
    /// <para>
    /// ★ XML 那一档是 2026-10-12 补的：`[CP] East Scarp` 的 <c>JacobBarn.tmx</c> 就是这个写法，
    /// 缺了它那张地图**产不出来** ⇒ 用它的地点建不起来 ⇒ 实机日志里
    /// <c>EastScarp_JacobBarn</c> 一直报 <c>Couldn't create the … location</c>。
    /// </para>
    /// </summary>
    private static uint[] DecodeData(XElement data, int width, int height)
    {
        int expected = width * height;
        string encoding = Attr(data, "encoding") ?? "";
        string compression = Attr(data, "compression") ?? "";
        string text = data.Value;

        uint[] gids = new uint[expected];

        // XML 写法：每个格子一个 `<tile gid="…"/>`，**空格子写成没有 gid 的 `<tile/>`**。
        // 顺序就是格子顺序；数量必须与地图尺寸相符（无限地图的 `<chunk>` 不支持——那得另立模型）。
        if (encoding.Length == 0)
        {
            if (compression.Length != 0)
                throw new NotSupportedException($"XML 层数据不该带 compression={compression}");

            List<XElement> tiles = data.Elements("tile").ToList();
            if (tiles.Count != expected)
                throw new NotSupportedException(
                    $"XML 图块数 {tiles.Count} 与地图尺寸 {expected} 不符（无限地图的 `<chunk>` 写法不支持）");
            for (int i = 0; i < expected; i++)
            {
                gids[i] = Attr(tiles[i], "gid") is string raw && raw.Length > 0
                    ? uint.Parse(raw, CultureInfo.InvariantCulture)
                    : 0u;
            }
            return gids;
        }

        if (encoding == "csv")
        {
            if (compression.Length != 0)
                throw new NotSupportedException($"不支持 csv 带压缩（compression={compression}）");
            string[] parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != expected)
                throw new NotSupportedException($"csv 图块数 {parts.Length} 与地图尺寸 {expected} 不符");
            for (int i = 0; i < expected; i++)
                gids[i] = (uint)long.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            return gids;
        }

        if (encoding == "base64")
        {
            byte[] raw = System.Convert.FromBase64String(text.Trim());
            if (compression == "zlib")
            {
                using var input = new MemoryStream(raw);
                using var z = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                z.CopyTo(output);
                raw = output.ToArray();
            }
            else if (compression.Length != 0)
            {
                throw new NotSupportedException($"不支持的压缩方式 compression={compression}");
            }

            if (raw.Length != expected * 4)
                throw new NotSupportedException($"base64 解出 {raw.Length} 字节，与地图尺寸 {expected} 格不符");
            for (int i = 0; i < expected; i++)
                gids[i] = BitConverter.ToUInt32(raw, i * 4);
            return gids;
        }

        throw new NotSupportedException($"不支持的图层数据编码 `{encoding}`（只支持 csv、base64 与 XML 写法）");
    }

    private static List<Propertie> ParseProperties(XElement? container) =>
        container == null
            ? new List<Propertie>()
            : container.Elements("property")
                .Select(p => new Propertie
                {
                    Key = Attr(p, "name") ?? "",
                    Type = StringType,
                    Value = Attr(p, "value") ?? "",
                })
                .ToList();

    private static string? Attr(XElement el, string name) => el.Attribute(name)?.Value;

    private static int Int(XElement el, string name) => attrInt(el, name) ?? 0;

    private static int? attrInt(XElement el, string name)
    {
        string? v = el.Attribute(name)?.Value;
        return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)
            ? r : null;
    }

    private static double Double(XElement el, string name) =>
        double.TryParse(el.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double r)
            ? r : 0;
}
