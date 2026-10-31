using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 地图 tbin 的**内存模型缓存** —— 「同一张地图只读一次、改动累积在内存、最后统一写回」。
///
/// <para>
/// **为什么要有它**：原来每个 <c>EditMap</c> 补丁都是
/// 「读整张 tbin → 改 → 写整张 tbin」。实测本语料 <c>EditMap</c> **802 条**打在
/// **122 个目标**上，其中 **80 个目标被多个补丁改**（覆盖 760 条）——
/// <c>Maps/Town</c> 被 **81 条**改、<c>Maps/Forest</c> **80 条**。
/// 也就是说这两张地图被**读写 81 / 80 次**，每次都要解析+重建整个 tbin 二进制。
/// </para>
///
/// <para>
/// 缓存之后：第一次碰某张地图时读进内存，之后所有补丁都改**同一个对象**，
/// 最后由 <see cref="Flush"/> 一次性写回。语义与 CP 一致 —— CP 也是在**同一棵内容树**
/// 上依次应用补丁，不是每步落盘。
/// </para>
///
/// <para>
/// ⚠ 这里只做「**合并读写**」，**不是**「算出最终差异、只写变化的那几个区域」——
/// 后者要把基线快照也留着做逐瓦片比对，是更远的一步。当前这一步就已经把
/// 80 个多补丁目标的解析+写盘次数从 N 次降到 1 次。
/// </para>
/// </summary>
public sealed class MapCache
{
    private readonly OutputTree _output;
    /// <summary>键是产出树相对路径 —— **大小写不敏感**（Windows 上大小写不同是同一个文件）。</summary>
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public MapCache(OutputTree output) => _output = output;

    /// <summary>
    /// 图片模型 —— 落盘前把 TMX 的翻转/旋转**烘进图集**（<see cref="FlipBaker"/>）要用。
    /// 为 <c>null</c> 时不烘焙（只用于不涉及图片的单测路径）。
    /// </summary>
    public ImageCache? Images { get; set; }

    /// <summary>
    /// 只保留翻转标记、不在本次落盘时烘焙。
    /// <para>
    /// 链式转换（多个包依次写同一棵树）里每个包都是独立进程：后面的包可能
    /// <c>Load</c> 一张更短的图集，把前面已经追加好的翻转格冲掉；反过来，后面的
    /// 烘焙又会从更小的图集重新分配索引，覆盖前面地图正在用的格子。
    /// 所以链式转换必须**等所有包跑完**，再用 <c>bake-flips</c> 统一烘一次。
    /// </para>
    /// </summary>
    public bool DeferFlips { get; set; }

    private FlipBaker? _flipBaker;

    /// <summary>本轮落盘烘焙的翻转图块数（诊断/报告用）。</summary>
    public int FlipBaked => _flipBaker?.Baked ?? 0;

    /// <summary>本轮落盘时归一了名字的自定义图层数（诊断/报告用）。</summary>
    public int LayersRenamed { get; private set; }

    /// <summary>地图属性里展开的 <c>{{i18n:…}}</c> 处数（诊断/报告用）。</summary>
    public int I18nTranslated { get; private set; }

    /// <summary>落盘前修正的图集引用数（指向了不存在的 <c>Maps/TileSheets/…</c> 之类）。</summary>
    public int TilesheetPathsRepaired { get; private set; }

    /// <summary>按真实图片尺寸重算的地图图集 <c>SheetSize</c> 数。</summary>
    public int TilesheetSizesRepaired { get; private set; }

    /// <summary>改写成 <c>PlayEvent</c> 的 Cape 宝珠图块动作数。</summary>
    public int CapeOrbActions { get; private set; }

    private readonly List<string> _capeOrbNotes = new();

    /// <summary>Cape 宝珠事件桥的跳过原因（找不到模板 / 事件 JSON 解析失败）。</summary>
    public IReadOnlyList<string> CapeOrbNotes => _capeOrbNotes;

    /// <summary>内容包翻译集（地图是单资产，按中文/默认译文冻结；null = 不处理）。</summary>
    public PackTranslations? Translations { get; set; }

    /// <summary>翻转烘焙过程中的说明（失败/跳过原因）。</summary>
    public IReadOnlyList<string> FlipNotes => _flipBaker?.Notes ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>已加载的地图数（诊断用）。</summary>
    public int LoadedCount => _entries.Count;

    // ------------------------------------------------------------------ 源地图解码缓存
    //
    // `EditMap` 的 `FromFile` 是**包里**的源地图。同一个源地图常被贴到多张地图上，
    // 逐个补丁 `FormFile` 就是「一个资源被读很多次」。
    // ⚠ 缓存的对象只**读**：`MapPatcher.PatchMap(target, source, …)` 只从 source 取图集/图层/瓦片
    //   并造新对象写进 target，从不改 source（`CopyProps` 也是复制）。
    //   例外只有一处：`LocalMapTilesheets.Copy` 的**图集改名**（落点撞上别的资产时改 `Image` 引用）。
    //   它只看树里有没有占位文件，同一份源地图反复用结果一致，留在缓存里不会打架。

    private readonly Dictionary<string, TBin10> _sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>源地图真正解码了几次（诊断用）。</summary>
    public int SourceDecodes { get; private set; }

    /// <summary>源地图命中缓存几次（诊断用）。</summary>
    public int SourceHits { get; private set; }

    /// <summary>
    /// 读一份**源地图**（同一个绝对路径只解一次）。已缓存则直接给既有那份。
    /// <paramref name="load"/> 只在没命中时调用 —— 由调用方决定按 `.tbin` 直读还是走 TMX 转换。
    /// </summary>
    public bool TryGetSource(string fullPath, Func<TBin10> load, out TBin10 map, out string? error)
    {
        error = null;
        if (_sources.TryGetValue(fullPath, out TBin10? hit))
        {
            SourceHits++;
            map = hit;
            return true;
        }

        try
        {
            map = load();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            map = null!;
            return false;
        }

        SourceDecodes++;
        _sources[fullPath] = map;
        return true;
    }

    // ------------------------------------------------------------------ 图集引用索引
    //
    // `SeasonalTilesheetEmitter` 每个组都要问一次「有没有地图把这张图集当 TileSheet 引用」，
    // 而每次问都要把产出树里**全部** `.tbin` 读+解析一遍。
    // 实测 2026-10-10 的语料：SVE 季节性服装那一包 52 个组里有 **11 个**真的扫到了
    // （3 个发射、8 个因「没人引用」被拒），也就是同一批 ~1000 份 tbin 被反复读了 11 遍。

    private Dictionary<string, List<(string Rel, TBin10 Map, string Original, TileSheet Sheet)>>? _tileSheetRefs;

    /// <summary>
    /// 图集引用索引：**归一后的图集名** → 引用它的地图（相对路径 + 地图对象 + 原名 + 那个 TileSheet）。
    /// 一次运行只扫一遍。
    /// <para>
    /// ⚠ 索引里**只留命中的**地图，扫描过程中读过就丢 —— 不然等于把全树地图常驻内存。
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, List<(string Rel, TBin10 Map, string Original, TileSheet Sheet)>>
        TileSheetRefIndex(OutputTree output, Func<string?, string> normalize)
    {
        if (_tileSheetRefs != null) return _tileSheetRefs;

        var index = new Dictionary<string, List<(string, TBin10, string, TileSheet)>>(StringComparer.OrdinalIgnoreCase);

        // 磁盘上的全部 .tbin + 缓存里有、磁盘上没有的那些（本包新建/改过的）
        var rels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(output.OutputRoot, "*.tbin", SearchOption.AllDirectories))
            rels.Add(Path.GetRelativePath(output.OutputRoot, path).Replace('\\', '/'));
        foreach ((string rel, _, _) in Loaded)
            rels.Add(rel);

        foreach (string rel in rels)
        {
            TBin10 map;
            if (TryGetLoaded(rel, out TBin10? cached))
            {
                map = cached!;
            }
            else
            {
                string full = output.ToOutputPath(rel);
                try { map = TBin10Reader.FormFile(full); }
                catch { continue; }   // 读不出来的不是地图（或坏文件），跳过，不因此失败
            }

            foreach (TileSheet sheet in map.TileSheets ?? new List<TileSheet>())
            {
                if (sheet.Image == null) continue;
                string key = normalize(sheet.Image);
                if (key.Length == 0) continue;
                if (!index.TryGetValue(key, out var hit))
                    index[key] = hit = new List<(string, TBin10, string, TileSheet)>();
                hit.Add((rel, map, sheet.Image, sheet));
            }
        }

        _tileSheetRefs = index;
        return index;
    }

    /// <summary>已加载的地图（相对路径 → 模型）。给「要扫全部地图」的调用方用（找图集引用）。</summary>
    public IEnumerable<(string RelativePath, TBin10Reader Reader, TBin10 Map)> Loaded =>
        _entries.Select(kv => (kv.Key, kv.Value.Reader, kv.Value.Map));

    private sealed class Entry
    {
        /// <summary>
        /// 落盘用的写入器。⚠ <c>TBin10Reader.Save</c> **不会重置</c>BufferWriter.BytePosition</c> ——
        /// 同一个实例第二次 Save 会把整份文档追加到上一次后面（实测两次 Flush 写出 1564 字节、
        /// 两份 tBIN10，读回只看到第一份）。所以每次写成功后就换成一个干净的写入器。
        /// </summary>
        public required TBin10Reader Reader { get; set; }
        public required TBin10 Map { get; init; }
        public int AppliedCount;

        /// <summary>
        /// 这份模型**被改过**、要写回。只读过（补丁最后失败 / 只是想看看）的不写 ——
        /// 把没改过的地图重新序列化一遍会写出一份与原文件不逐字节相同的 tbin。
        /// </summary>
        public bool Dirty;

        /// <summary>
        /// 落盘时用的相对路径（保留原大小写）。
        /// <para>
        /// 缓存键是大小写不敏感的（Windows 上大小写不同是同一个文件），键里留的是**首次**
        /// 出现的大小写。而磁盘目录项的名字跟随**第一次创建它的那次写** —— Windows 上往已存在的
        /// 文件里写**不会**改名字。实测 SVE：先 <c>Load Maps/Beach-Nightmarket</c>、
        /// 后 <c>EditMap Maps/Beach-NightMarket</c>，目录项是前者（小写 m）。
        /// </para>
        /// <para>
        /// 所以本条目**还没落盘**时（本次运行里刚 <c>Load</c> 进来、暂存里那份就是它的第一次写），
        /// 名字要沿用暂存里的写法 —— 见 <see cref="TryGetModel"/>。
        /// </para>
        /// </summary>
        public string Rel = "";

        /// <summary>
        /// 这张图是**本次运行里被 <c>Load</c> 整份换掉**的。
        /// <para>
        /// 原来 <c>Load</c> 一份 <c>.tmx</c> 走的是 <c>File.Move(tmp, dest, overwrite: true)</c> ——
        /// 那个 API **会把目录项改成目标的大小写**；而 <see cref="TBin10Reader.Save"/> 用的
        /// <c>File.WriteAllBytes</c> 只是往已存在的文件里写，**不会**改名字。凑巧原版树里
        /// 已有 <c>Maps/Beach-NightMarket.tbin</c>（大写 M），而 SVE 的 <c>Load</c> 写的是小写 m：
        /// 老行为把目录项改成了小写，统一到内存模型之后就没改。这里补上这一步。
        /// </para>
        /// </summary>
        public bool ReplacedByLoad;
    }

    /// <summary>这张地图在产出树里**当前**存在吗（含本次运行改过、还没落盘的）。</summary>
    public bool Exists(string relativePath) =>
        _entries.ContainsKey(relativePath) || _output.Exists(relativePath);

    /// <summary>
    /// 忘掉这张地图的模型。给「<c>Load</c> 把整张地图换掉了」用 —— 缓存里若还留着旧模型，
    /// <see cref="Flush"/> 会把它写回去、把刚 <c>Load</c> 进来的那份冲掉（实测先 <c>EditMap</c> 后 <c>Load</c> 就会）。
    /// </summary>
    public void Forget(string relativePath) => _entries.Remove(relativePath);

    /// <summary>缓存里有这张地图的模型吗（有就直接用，别去读盘上那份旧的）。</summary>
    public bool TryGetLoaded(string relativePath, out TBin10? map)
    {
        map = null;
        if (!_entries.TryGetValue(relativePath, out Entry? hit))
            return false;

        map = hit.Map;
        return true;
    }

    /// <summary>
    /// 接管一张**由本工具生成**的地图（<c>Load</c> 的 TMX 转换产物）：放进缓存、不立即落盘，
    /// 由 <see cref="Flush"/> 统一写。
    /// <para>
    /// 为什么要接管：否则 <c>Load</c> 直接写盘，而缓存里若已有这张图的旧模型
    /// （先 <c>EditMap</c> 后 <c>Load</c> 的顺序），<see cref="Flush"/> 会把旧模型写回去、把 <c>Load</c> 冲掉。
    /// </para>
    /// </summary>
    public void Set(string relativePath, TBin10Reader reader, TBin10 map,
        bool replacedByLoad = false)
    {
        _entries[relativePath] = new Entry
        {
            Reader = reader, Map = map, AppliedCount = 1, Dirty = true, Rel = relativePath,
            ReplacedByLoad = replacedByLoad,
        };
        _output.DropPending(relativePath);
        _output.NoteWrite(relativePath, WriteKind.Map);
    }

    /// <summary>
    /// 取某张地图的内存模型（没有就从 <paramref name="bytes"/> 解出来），
    /// 返回 <c>null</c> 表示解析失败。<paramref name="error"/> 带上失败原因，调用方照原样报出去。
    /// <para>
    /// 收**字节**而不是文件路径：地图可能是本次运行里刚 <c>Load</c> 进来的，
    /// 那会儿还在 <see cref="OutputTree"/> 的暂存里、磁盘上没有（延迟写）。
    /// </para>
    /// </summary>
    public (TBin10Reader Reader, TBin10 Map)? Get(string relativePath, byte[] bytes, out string? error,
        string? displayPath = null)
    {
        error = null;
        if (_entries.TryGetValue(relativePath, out Entry? hit))
        {
            hit.AppliedCount++;
            return (hit.Reader, hit.Map);
        }

        try
        {
            TBin10Reader reader = TBin10Reader.Create(bytes);
            var map = (TBin10)reader.Read();
            _entries[relativePath] = new Entry
            {
                Reader = reader, Map = map, AppliedCount = 1,
                // 本次运行里刚 Load 进来时，暂存里那份就是它的「第一次写」——名字跟它走
                Rel = displayPath ?? relativePath,
            };
            return (reader, map);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// 取这张地图的内存模型：**已有就直接给**，没有才用 <paramref name="readBytes"/> 去读。
    /// <para>
    /// ⚠ 必须先看缓存再看 <paramref name="readBytes"/>：第一遍改成功之后暂存已被撤掉
    /// （落盘权归缓存），这时同一张图的第二条补丁再去读盘会读到「还没有的那份」，
    /// 于是误报「地图 tbin 不在资源树里」。实测 SVE 的 `Maps/Custom_EnchantedGrove` 就是这么丢的。
    /// </para>
    /// </summary>
    public bool TryGetModel(string relativePath, Func<byte[]?> readBytes,
        out TBin10Reader reader, out TBin10 map, out string? error)
    {
        if (_entries.TryGetValue(relativePath, out Entry? hit))
        {
            hit.AppliedCount++;
            (reader, map) = (hit.Reader, hit.Map);
            error = null;
            return true;
        }

        byte[]? bytes = readBytes();
        if (bytes == null)
        {
            reader = null!;
            map = null!;
            error = $"地图 tbin 不在资源树里：{relativePath}";
            return false;
        }

        var got = Get(relativePath, bytes, out string? parseError, _output.PendingName(relativePath));
        if (got == null)
        {
            reader = null!;
            map = null!;
            error = $"读取地图 tbin 失败：{parseError}";
            return false;
        }

        (reader, map) = got.Value;
        error = null;
        return true;
    }

    /// <summary>
    /// 告诉缓存「这张地图被改过，最终由我落盘」—— 撤掉产出树那边的暂存。
    /// <para>
    /// 必须在**改成功之后**才调：失败时留着暂存，产出树那边写的是刚 <c>Load</c> 进来的原始字节，
    /// 正是我们要的结果。
    /// </para>
    /// </summary>
    public void MarkModified(string relativePath)
    {
        if (_entries.TryGetValue(relativePath, out Entry? hit)) hit.Dirty = true;
        _output.DropPending(relativePath);
        _output.NoteWrite(relativePath, WriteKind.Map);
    }

    /// <summary>
    /// 把所有加载过的地图写回产出树。**一次运行结束前调用一次**。
    /// 返回真正写了的相对路径（按字典序），供报告/测试核对。
    /// </summary>
    public List<string> Flush()
    {
        var written = new List<string>(_entries.Count);

        // ★ 翻转烘焙必须是**串行**的：它要改图集像素（`ImageCache` 不是线程安全的），
        //   同一张图集的追加位次也要跨地图保持一致。放在并行写回之前一次做完。
        foreach (string rel in _entries.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            Entry e = _entries[rel];
            if (!e.Dirty) continue;

            // 地图/图块属性里的 i18n 令牌：CP 写入时代掉，我们原先原样落进 tbin。
            // 放在图层名归一/翻转烘焙**之前**，这样被复制的图块属性也已经是译文。
            if (Translations != null)
                I18nTranslated += MapApplier.TranslateMap(e.Map, Translations);

            // 自定义图层名归一（`Back+1`/`Always Front`/`Draw=…`）——原版按 `锚点+整数` 决定画不画，
            // 非整数后缀的层整层不渲染（业主报的云雾层）。改名仍然在**补丁全部应用之后**做。
            LayersRenamed += LayerNameNormalizer.Normalize(e.Map);

            // 图集图片可能被 EditImage 换成更大的尺寸；tbin 里的 SheetSize 还是旧格数，
            // 超出的 TileIndex 会被原版直接丢掉（整块黑洞）。ImageCache 不是线程安全的，
            // 必须在下面的并行写回**之前**串行做。
            TilesheetSizesRepaired += TilesheetSizeRepair.Repair(e.Map, e.Rel.Length > 0 ? e.Rel : rel, Images);

            // 链式转换：把烘焙留到所有包跑完后的 bake-flips 统一做（见 DeferFlips 说明）。
            if (DeferFlips) continue;
            if (!FlipBaker.HasMarkers(e.Map)) continue;
            if (Images == null)
            {
                FlipBaker.StripMarkers(e.Map);
                continue;
            }
            _flipBaker ??= new FlipBaker(Images);
            _flipBaker.Bake(e.Map, e.Rel.Length > 0 ? e.Rel : rel);

            // ⚠ **翻转烘焙会把新格追加到图集末尾**，必须再按新图片尺寸重算 SheetSize；
            // 否则游戏仍按旧格数判断越界，新增的翻转格全被丢弃（实机黑色小方块）。
            TilesheetSizesRepaired += TilesheetSizeRepair.Repair(e.Map, e.Rel.Length > 0 ? e.Rel : rel, Images);
        }

        // Cape 宝珠的 TouchAction 需要先有 Data/Events 条目，才能改成原版 `PlayEvent`。
        // 这是语法层的纯计划 + 一次资源写入：先写事件数据，成功后才改地图属性。
        var capeMaps = _entries
            .Where(kv => kv.Value.Dirty)
            .Select(kv => (MapRel: kv.Value.Rel.Length > 0 ? kv.Value.Rel : kv.Key, Map: kv.Value.Map))
            .ToList();
        CapeOrbActions = CapeOrbBridge.RewriteAndGenerate(_output, capeMaps, _capeOrbNotes);

        // 每张地图一个独立文件 —— 并行序列化 + 写。账本按排序后的顺序登记。
        int tilesheetsRepaired = 0;
        System.Threading.Tasks.Parallel.ForEach(
            _entries.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), rel =>
            {
                Entry e = _entries[rel];
                if (!e.Dirty) return;        // 只读过、没改过的不写
                string dest = _output.ToOutputPath(e.Rel.Length > 0 ? e.Rel : rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                // `Load` 换掉过的：名字要按 Load 的写法落，而 WriteAllBytes 改不动已存在文件的名字
                // （Windows 上大小写不同是同一个文件）—— 先删掉再写。
                if (e.ReplacedByLoad && File.Exists(dest)) File.Delete(dest);
                string mapRel = e.Rel.Length > 0 ? e.Rel : rel;
                // 纯 XNB 装机没有 TMXL 那个注册 `LoadMap` 的 DLL ⇒ 落盘前统一改成原版 `Warp`。
                // 图集引用也在这里按**地图自己的目录**归一（`../TileSheets/furniture` 不能被剥成
                // `TileSheets/furniture`，否则游戏去找 `Maps/TileSheets/…`）。
                MapTileActions.Rewrite(e.Map, mapRel);
                // 最后一道：图集引用必须落到真实存在的图上。找不到的保持原样，由审计报出来。
                System.Threading.Interlocked.Add(ref tilesheetsRepaired,
                    TilesheetPathRepair.Repair(e.Map, mapRel, _output));
                e.Reader.Save(e.Map, dest);
                // Save 不重置 BytePosition：同一个写入器再 Save 一次会追加出第二份 tBIN10。
                // 换掉它，下次 Flush 才能从 0 开始；同时清 Dirty，没再改过的图不重复写。
                e.Reader = TBin10Reader.Create(Array.Empty<byte>());
                e.Dirty = false;
                lock (written) written.Add(rel);
            });

        TilesheetPathsRepaired = tilesheetsRepaired;
        written.Sort(StringComparer.Ordinal);
        return written;
    }

    /// <summary>
    /// 落盘后释放地图对象图/源缓存。地图只在转换阶段需要；自检和下一包都从磁盘读。
    /// </summary>
    public void Release()
    {
        _entries.Clear();
        _sources.Clear();
        _tileSheetRefs = null;
    }
}
