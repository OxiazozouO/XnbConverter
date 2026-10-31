namespace SdvCpTool.Core.Workspace;

/// <summary>已解包的资源树。把资产名解析成磁盘上的文件路径。</summary>
public sealed class ResourceTree
{
    private static readonly HashSet<string> IgnoredExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".config" };

    private readonly Dictionary<string, string> _byAssetName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 本次运行中<b>新建</b>的资产：资产名 →（产出树内的绝对路径，产出树内的相对路径）。
    /// <para>
    /// 判定（<c>PatchJudge</c>）与写入（写入落在 <see cref="OutputTree"/>）必须共用这一个存在性视图。
    /// 2026-10-06 的架构 bug：判定只查构造时的输入树、写入落在产出树，两棵树不打通，于是
    /// <c>Data/Extras/RSVMap.json</c> 里 idx1 的 <c>Load LooseSprites/RSVIcon</c> 刚写出
    /// （<c>written: true</c>），紧随其后的 <c>EditImage LooseSprites/RSVIcon</c> 就判
    /// <c>TargetMissing</c> —— 自己刚写的资产自己看不见。
    /// </para>
    /// </summary>
    private readonly Dictionary<string, (string Absolute, string Relative)> _added =
        new(StringComparer.OrdinalIgnoreCase);

    public string Root { get; }
    public IReadOnlyList<string> RelativeFiles { get; }

    public ResourceTree(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"资源树目录不存在：{root}");

        Root = Path.GetFullPath(root);

        if (SharedTreeIndex.TryGet(Root,
                out Dictionary<string, string> shared, out IReadOnlyList<string> sharedFiles,
                out _, out _))
        {
            _byAssetName = shared;
            RelativeFiles = sharedFiles;
            return;
        }

        var relative = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
            relative.Add(rel);

            string ext = Path.GetExtension(rel);
            if (IgnoredExtensions.Contains(ext)) continue;

            // 资产名 = 相对路径去掉扩展名，因此每个键至多被赋值一次，无需冲突处理
            string assetName = rel[..^ext.Length];
            _byAssetName[assetName] = rel;
        }

        relative.Sort(StringComparer.OrdinalIgnoreCase);
        RelativeFiles = relative;
        SharedTreeIndex.Reset(Root, relative);
    }

    /// <summary>CP 的资产名 → 资源树中的绝对路径。找不到返回 null。</summary>
    public string? Resolve(string assetName)
    {
        string normalized = assetName.Replace('\\', '/').Trim('/');

        // 本次运行新建的资产优先：它可能覆盖了输入树里的同名资产（写入落在产出树）。
        if (_added.TryGetValue(normalized, out (string Absolute, string Relative) added))
            return added.Absolute;

        return _byAssetName.TryGetValue(normalized, out string? rel)
            ? Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar))
            : null;
    }

    public string? ResolveRelative(string assetName)
    {
        string normalized = assetName.Replace('\\', '/').Trim('/');

        // 新建资产的相对路径相对<b>产出树</b>根（输入树里没有它）。
        if (_added.TryGetValue(normalized, out (string Absolute, string Relative) added))
            return added.Relative;

        return _byAssetName.TryGetValue(normalized, out string? rel) ? rel : null;
    }

    /// <summary>
    /// 把产出树本次新建的资产登记进判定视图 —— 判定侧「本次运行已写入的资产」的<b>唯一</b>来源。
    /// 直接复用写入侧已维护的 <see cref="OutputTree.CreatedFiles"/>，判定不另建一份清单，
    /// 避免两处再次走散（2026-10-06 架构 bug 的根因就是「两棵树各说各话」）。
    /// </summary>
    public void RegisterCreatedAssets(OutputTree output)
    {
        foreach (string rel in output.CreatedFiles)
        {
            string normalized = rel.Replace('\\', '/').TrimStart('/');
            if (normalized.Length == 0) continue;

            string ext = Path.GetExtension(normalized);
            if (IgnoredExtensions.Contains(ext)) continue;   // .config 不是资产本体

            string assetName = normalized[..^ext.Length];
            if (assetName.Length == 0) continue;

            _added[assetName] = (output.ToOutputPath(normalized), normalized);
        }
    }
}
