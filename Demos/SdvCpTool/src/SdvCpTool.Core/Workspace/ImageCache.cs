using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Workspace;

/// <summary>
/// 图片（PNG）的**内存模型缓存** —— 「同一张图只解码一次、改动累积在内存、最后统一写回」。
///
/// <para>
/// **为什么要有它**：原来每个 <c>EditImage</c> 都是
/// 「<c>Png.Read</c> 源图 + <c>Png.Read</c> 整张目标图 → 合成 → <c>Png.Write</c> 整张目标图」。
/// 实测本语料 <c>EditImage</c> **862 条**打在 **274 个目标**上，其中 **133 个目标被多条补丁改**
/// （覆盖 721 条）—— <c>LooseSprites/Cursors</c> 被 **107 条**改。也就是说这张图被
/// **解码 107 次、重编码 107 次**，而每次改动可能只是 x/y/w/h 的一个小矩形。
/// </para>
///
/// <para>
/// 缓存之后：第一次碰某张图时解码一次进内存，之后所有补丁都改**同一个像素缓冲区**，
/// 最后由 <see cref="Flush"/> 一次性重编码写回。语义与 CP 一致 —— CP 也是在
/// **同一棵内容树**上依次应用补丁，不是每步落盘。
/// </para>
///
/// <para>
/// ⚠ **不是**「只写变化的那几个区域」。PNG 的字节是按行紧密排布的（还有 zlib 流与滤波器），
/// 改一个像素也得整张重编码 —— 「定点写区域」在这层做不到。这里做到的定点是
/// **解码/重编码各一次**，把 107 次降到 1 次。
/// </para>
///
/// <para>
/// **约定**：产出树里的图片资产由本类**独家持有**。读走 <see cref="TryGetPixels"/>，
/// 写走 <see cref="SetPixels"/> / <see cref="NoteWholeFile"/>，
/// 不要再对产出树里的 <c>.png</c> 直接 <c>Png.Read</c> / <c>Png.Write</c> / <c>File.Exists</c>
/// —— 那样会看到**没被本类落盘**的旧内容。
/// </para>
/// </summary>
public sealed class ImageCache
{
    private readonly OutputTree _output;

    /// <summary>
    /// 键是产出树相对路径。**大小写不敏感** —— Windows 上 `LooseSprites/Cursors` 与
    /// `loosesprites/cursors` 是同一个文件，用序号比较会记成两个条目、写两次、内存里还会分叉。
    /// </summary>
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public ImageCache(OutputTree output) => _output = output;

    /// <summary>已加载的图片数（诊断用）。</summary>
    public int LoadedCount => _entries.Count;

    private sealed class Entry
    {
        /// <summary>解码后的 RGBA 像素；未解码时是 <c>null</c>。</summary>
        public byte[]? Pixels;
        public int Width;
        public int Height;

        /// <summary>整文件来源（<c>Load</c> 进来的包里那张 png）。按字节拷，不重编码。</summary>
        public string? WholeFileSource;

        /// <summary><see cref="Pixels"/> 里有未落盘的改动。</summary>
        public bool Dirty;

        /// <summary>被几条补丁改过（诊断用）。</summary>
        public int EditCount;
    }

    // ------------------------------------------------------------------ 源图解码缓存
    //
    // `EditImage` 的 `FromFile` 是**包里**的源图，与产出树里的目标图无关。
    // 同一个源图常被用在多个目标上（一张覆盖图贴到好几张图上），逐个补丁 `Png.Read` 就是
    // 「一个资源被读很多次」。这里按绝对路径缓存一次解码结果 —— 纯读、不改，语义零风险。

    private readonly Dictionary<string, (byte[] Pixels, int Width, int Height)> _sources =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>源图真正解码了几次（诊断用）。</summary>
    public int SourceDecodes { get; private set; }

    /// <summary>源图命中缓存几次（诊断用）。</summary>
    public int SourceHits { get; private set; }

    /// <summary>
    /// 读一张**源图**（内容包里的 <c>.png</c>），同一个文件整个运行里只解码一次。
    /// 返回 <c>false</c> 表示解码失败，原因在 <paramref name="error"/>。
    /// </summary>
    public bool TryReadSource(string fullPath, out byte[] pixels, out int width, out int height,
        out string? error)
    {
        error = null;
        if (_sources.TryGetValue(fullPath, out var hit))
        {
            SourceHits++;
            (pixels, width, height) = (hit.Pixels, hit.Width, hit.Height);
            return true;
        }

        try
        {
            pixels = Png.Read(fullPath, out width, out height);
        }
        catch (Exception ex)
        {
            pixels = Array.Empty<byte>();
            width = 0;
            height = 0;
            error = $"PNG 读取失败（{fullPath}）：{ex.Message}";
            return false;
        }

        SourceDecodes++;
        _sources[fullPath] = (pixels, width, height);
        return true;
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    /// <summary>产出树里这张图**当前**存在吗（含本次运行还没落盘的改动）。</summary>
    public bool Exists(string relativePath)
    {
        string rel = Normalize(relativePath);
        if (_entries.TryGetValue(rel, out Entry? e) && (e.Pixels != null || e.WholeFileSource != null))
            return true;

        return _output.Exists(rel);
    }

    /// <summary>
    /// 记下「这张图的整份内容来自某个文件」—— <c>Load</c> 的语义。**不拷、不解码**，
    /// 到 <see cref="Flush"/> 才按字节拷过去（与原来一样不重编码）。
    /// <para>会**丢掉**这张图先前累积的像素改动 —— 整文件覆盖本来就该盖掉它们。</para>
    /// </summary>
    public void NoteWholeFile(string relativePath, string sourceFullPath)
    {
        string rel = Normalize(relativePath);

        // ★ 地图图集：后面的包 `Load` 一份更矮的版本时，绝不能把前面的高版本截断。
        //   链式转换里不同地图常引用同一个 `Maps/<图集>` 键，但作者各自带的是不同尺寸的副本。
        //   直接整文件覆盖会让前面地图的索引越界（黑洞），或把追加格冲掉。
        //   宽度相同 ⇒ 以高版本为底，把新版本像素覆在顶部，保留下半部分。
        if (rel.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)
            && TryComposeTaller(rel, sourceFullPath))
        {
            _output.DropPending(rel);
            _output.NoteWrite(rel, WriteKind.Image);
            return;
        }

        if (_entries.TryGetValue(rel, out Entry? previous)) ReleasePixels(previous);
        _entries[rel] = new Entry { WholeFileSource = sourceFullPath };
        // 产出树那边可能还挂着一条 StageCopy（老的调用路径），别再拷一遍把它盖回来
        _output.DropPending(rel);
        _output.NoteWrite(rel, WriteKind.Image);
    }

    /// <summary>把本缓存实际拥有的池化像素缓冲还回 <see cref="ByteBufferPool"/>。</summary>
    public void Release()
    {
        foreach (Entry entry in _entries.Values) ReleasePixels(entry);
        _entries.Clear();
        _sources.Clear();
    }

    private static void ReleasePixels(Entry entry)
    {
        if (entry.Pixels != null) ByteBufferPool.Return(entry.Pixels);
        entry.Pixels = null;
    }

    /// <summary>
    /// 目标已经有更高的同宽图集时，把新图的像素覆在顶部、保留旧图下半部分。
    /// 返回 <c>true</c> 表示已经生成合成像素（调用方不要再按整文件覆盖）。
    /// </summary>
    private bool TryComposeTaller(string rel, string sourceFullPath)
    {
        // 先只读 PNG 头拿尺寸：绝大多数图集都不需要合成（没有旧图 / 旧图不比新图高），
        // 而 `Png.Read` 会把整张 4K 图集解码成 RGBA —— Downtown Zuzu 一包 85 张地图
        // 的图集引用实测因此耗 148 s。尺寸判定与原来逐字一致后再解码。
        if (!SdvCpTool.Core.Validating.PngHeader.TryReadSize(sourceFullPath,
                out int newW, out int newH))
            return false;

        byte[]? oldPx = null;
        int oldW = 0, oldH = 0;
        string? oldPath = null;
        if (_entries.TryGetValue(rel, out Entry? entry) && entry.Pixels is not null)
        {
            oldPx = entry.Pixels;
            oldW = entry.Width;
            oldH = entry.Height;
        }
        else
        {
            oldPath = _output.ResolveReadablePath(rel);
            if (oldPath == null) return false;
            if (!SdvCpTool.Core.Validating.PngHeader.TryReadSize(oldPath, out oldW, out oldH))
                return false;
        }

        if (oldW != newW || oldH <= newH) return false;

        byte[] newPx;
        try { newPx = Png.Read(sourceFullPath, out _, out _); }
        catch { return false; }
        if (oldPx == null)
        {
            try { oldPx = Png.Read(oldPath!, out _, out _); }
            catch { return false; }
        }

        byte[] merged = ByteBufferPool.Rent(newW * oldH * 4);
        Array.Copy(oldPx, merged, oldPx.Length);
        Array.Copy(newPx, merged, newPx.Length);

        if (_entries.TryGetValue(rel, out Entry? replaced)) ReleasePixels(replaced);
        _entries[rel] = new Entry
        {
            Pixels = merged,
            Width = newW,
            Height = oldH,
            Dirty = true,
        };
        return true;
    }

    /// <summary>
    /// 取这张图当前的像素（必要时解码一次）。返回 <c>false</c> 表示拿不到 ——
    /// 目标既不在缓存里、磁盘上也没有，或解码失败，原因在 <paramref name="error"/>。
    /// </summary>
    public bool TryGetPixels(string relativePath, out byte[] pixels, out int width, out int height,
        out string? error)
    {
        pixels = Array.Empty<byte>();
        width = 0;
        height = 0;
        error = null;

        string rel = Normalize(relativePath);
        if (!_entries.TryGetValue(rel, out Entry? entry))
            entry = new Entry();

        if (entry.Pixels is not null)
        {
            pixels = entry.Pixels;
            width = entry.Width;
            height = entry.Height;
            return true;
        }

        // 没解码过 ⇒ 找当前的可读来源：整文件来源（Load 的）优先于磁盘。
        string? readable = entry.WholeFileSource ?? _output.ResolveReadablePath(rel);
        if (readable == null)
        {
            error = $"产出树里没有这张图：{rel}";
            return false;
        }

        try
        {
            pixels = Png.Read(readable, out width, out height);
        }
        catch (Exception ex)
        {
            error = $"PNG 读取失败（{rel}）：{ex.Message}";
            return false;
        }

        entry.Pixels = pixels;
        entry.Width = width;
        entry.Height = height;
        _entries[rel] = entry;
        return true;
    }

    /// <summary>
    /// 取这张图**当前**的像素尺寸（只读，不解码的路径尽量复用源图缓存）。
    /// <para>
    /// ⚠ 与 <see cref="TryReadSource"/> 的区别：这里先看内存里的像素改动。翻转烘焙把新格
    /// 追加到内存后还没 <see cref="Flush"/> 时，磁盘上的 PNG 仍是旧尺寸；按磁盘算
    /// <c>SheetSize</c> 会把新追加的索引判成越界（实机整块黑洞）。
    /// </para>
    /// </summary>
    public bool TryGetSize(string relativePath, out int width, out int height)
    {
        width = height = 0;
        string rel = Normalize(relativePath);
        if (_entries.TryGetValue(rel, out Entry? entry) && entry.Pixels is not null)
        {
            width = entry.Width;
            height = entry.Height;
            return true;
        }

        string? readable = entry?.WholeFileSource ?? _output.ResolveReadablePath(rel);
        if (readable == null) return false;
        // 只为尺寸：读 PNG 头即可，不要为了问宽高把整张 4K 图集解码成 RGBA。
        if (SdvCpTool.Core.Validating.PngHeader.TryReadSize(readable, out width, out height))
            return true;
        return TryReadSource(readable, out _, out width, out height, out _);
    }

    /// <summary>改完像素后写回缓存，标记为待落盘。</summary>
    public void SetPixels(string relativePath, byte[] pixels, int width, int height)
    {
        string rel = Normalize(relativePath);
        if (!_entries.TryGetValue(rel, out Entry? entry))
        {
            entry = new Entry();
            _entries[rel] = entry;
        }

        if (!ReferenceEquals(entry.Pixels, pixels)) ReleasePixels(entry);
        entry.Pixels = pixels;
        entry.Width = width;
        entry.Height = height;
        entry.Dirty = true;
        entry.EditCount++;

        _output.DropPending(rel);   // 本类接管这张图的落盘，产出树别再拷
        _output.NoteWrite(rel, WriteKind.Image);
    }

    /// <summary>
    /// 把所有碰过的图片写回产出树。**一次运行结束前调用一次**。
    /// 返回真正写了的相对路径（按字典序），供报告/测试核对。
    /// </summary>
    public List<string> Flush()
    {
        var written = new List<string>(_entries.Count);

        string[] rels = _entries.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        // 每张图一个独立文件，重编码也各算各的 —— 并行写。账本按排序后的顺序登记。
        try
        {
            System.Threading.Tasks.Parallel.ForEach(rels, rel =>
            {
                Entry e = _entries[rel];
                // 只读进缓存、没有改动的图片不落盘；别为它们建目录/算输出路径。
                if (e.Dirty && e.Pixels != null)
                {
                    string dest = _output.ToOutputPath(rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    Png.Write(dest, e.Pixels, e.Width, e.Height);
                    lock (written) written.Add(rel);
                }
                else if (e.WholeFileSource != null)
                {
                    string dest = _output.ToOutputPath(rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(e.WholeFileSource, dest, overwrite: true);
                    lock (written) written.Add(rel);
                }
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            Exception inner = ex.InnerExceptions[0];
            throw new IOException($"图片写回失败：{inner.Message}", inner);
        }

        written.Sort(StringComparer.Ordinal);
        return written;
    }
}
