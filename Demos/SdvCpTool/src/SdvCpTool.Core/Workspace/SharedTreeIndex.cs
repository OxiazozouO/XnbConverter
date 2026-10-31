namespace SdvCpTool.Core.Workspace;

/// <summary>
/// 同一进程内连续转换多个包时，共享产出树的文件索引。
/// <para>
/// 每个包都会新建 <see cref="OutputTree"/> 与 <see cref="ResourceTree"/>；两者各自把
/// 17k 份文件递归枚举、建表。链式转换 232 包 ×2 趟就是 900 多次全树扫描，而上一包
/// 落盘后目录只多了几十个文件 —— 这是纯固定成本。这里只在批量入口
/// （<c>convert-batch</c>）打开，普通单包运行不受影响。
/// </para>
/// <para>
/// 索引由「首次建树/续写时扫描一次」+「每次 <see cref="OutputTree.Flush"/> 追加新路径」
/// 维护；转换过程不删文件，因此只需追加。
/// </para>
/// </summary>
internal static class SharedTreeIndex
{
    private static string? _root;
    private static Dictionary<string, string>? _byAsset;
    private static SortedSet<string>? _files;
    private static string[]? _snapshot;
    private static HashSet<string>? _set;
    private static Dictionary<string, List<string>>? _configs;

    public static bool Enabled =>
        Environment.GetEnvironmentVariable("SDVCP_SHARED_TREE_INDEX") == "1";

    public static bool TryGet(string root,
        out Dictionary<string, string> byAsset, out IReadOnlyList<string> files,
        out HashSet<string> set, out Dictionary<string, List<string>> configs)
    {
        if (Enabled && _root != null && _byAsset != null && _files != null
            && string.Equals(_root, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            byAsset = _byAsset;
            files = _snapshot ??= _files.ToArray();
            set = _set!;
            configs = _configs!;
            return true;
        }
        byAsset = null!;
        files = null!;
        set = null!;
        configs = null!;
        return false;
    }

    public static void Reset(string root, IEnumerable<string> relativeFiles)
    {
        if (!Enabled) return;
        _root = Path.GetFullPath(root);
        _byAsset = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        _set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _configs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _snapshot = null;
        foreach (string rel in relativeFiles) Add(rel);
        _snapshot = _files.ToArray();
    }

    public static void AddFiles(string root, IEnumerable<string> relativeFiles)
    {
        if (!Enabled || _root == null
            || !string.Equals(_root, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            return;
        bool changed = false;
        foreach (string rel in relativeFiles) changed |= Add(rel);
        if (changed) _snapshot = null;
    }

    private static bool Add(string relativePath)
    {
        string rel = relativePath.Replace('\\', '/');
        if (!_files!.Add(rel)) return false;
        _set!.Add(rel);
        string ext = Path.GetExtension(rel);
        if (!ext.Equals(".config", StringComparison.OrdinalIgnoreCase))
        {
            _byAsset![rel[..^ext.Length]] = rel;
            return true;
        }

        // `.config` 还要进「目录 → 候选」索引；`InputFiles` 是按 OrdinalIgnoreCase 排序的，
        // 新文件必须插到同样的位置，模板选择才与每包重建索引时逐字一致。
        var map = _configs!;
        map.TryAdd("", new List<string>());
        InsertSorted(map[""], rel);
        int slash = rel.IndexOf('/');
        while (slash >= 0)
        {
            string ancestor = rel[..slash];
            map.TryAdd(ancestor, new List<string>());
            InsertSorted(map[ancestor], rel);
            slash = rel.IndexOf('/', slash + 1);
        }
        return true;
    }

    private static void InsertSorted(List<string> list, string value)
    {
        int i = list.BinarySearch(value, StringComparer.OrdinalIgnoreCase);
        if (i >= 0) return;
        list.Insert(~i, value);
    }
}
