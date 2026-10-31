using Newtonsoft.Json;

namespace SdvCpTool.Core.Reporting;

/// <summary>
/// 全树自检的**增量缓存**：同一棵树上，产出文件没变时结论当然也一样。
///
/// <para>
/// **为什么要有它**：链式跑语料时**每个包是一个独立进程**（`convert-mods.py` 逐包起进程），
/// 所以进程内的 <see cref="AuditParseCache"/> 帮不到跨包 —— 每个包仍要把整棵树的 JSON
/// 重新解析一遍。实测这一项占整轮 **60%**（3m28s → 1m22s）。
/// </para>
///
/// <para>
/// **新鲜度由调用方说了算**：产出阶段本来就知道**这一轮写了哪些文件**（`OutputTree.PlannedWrites`
/// 那份落盘账本），所以自检只要「重算这几份、其余沿用上次的结论」就够了 ——
/// 既不用把整棵树枚举一遍，也不用逐文件 stat。这比拿「大小 + 修改时间」猜更准：
/// 工具自己写过的文件必然重算，没写过的必然没变。
/// </para>
///
/// <para>
/// ⚠ 因此缓存与**那一轮的那棵树**绑定：文件头记了产出根与原版根，对不上就整份作废。
/// 别把缓存文件搬去另一棵树用。
/// </para>
/// </summary>
public sealed class AuditCache
{
    /// <summary>
    /// 一个产出文件的结论：它跳过了吗、检查了吗、发现什么没有。
    /// <para>
    /// <paramref name="Counts"/> 给「除了 findings 还有别的累计量」的自检用
    /// （如未展开令牌自检的 <c>IdentifierOnly</c> / <c>Harmful</c>）。
    /// </para>
    /// </summary>
    public sealed record Entry(bool Skipped, bool Checked, List<string[]> Findings, int[]? Counts = null)
    {
        /// <summary>「看过、但不算数」的那种结论（原版没有参照 / 描述符里没有 <c>Extension</c>）——
        /// 与「checked 却没有 findings」是两回事，各用各的，别混。</summary>
        public static Entry SkippedOnly => new(Skipped: true, Checked: false, new List<string[]>());
    }

    private sealed class Stored
    {
        public bool Skipped { get; set; }
        public bool Checked { get; set; }
        public List<string[]> Findings { get; set; } = new();
        public int[]? Counts { get; set; }
    }

    private sealed class File
    {
        public string? OutputRoot { get; set; }
        public string? InputRoot { get; set; }
        public Dictionary<string, Stored> Entries { get; set; } = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, Stored> _entries;

    /// <summary>看起来像没被写过、其实写过的文件（诊断用）：命中与未命中的计数。</summary>
    public int Reused;
    public int Recomputed;

    private AuditCache(Dictionary<string, Stored> entries) => _entries = entries;

    public static AuditCache Empty() => new(new Dictionary<string, Stored>(StringComparer.Ordinal));

    /// <summary>
    /// 载入缓存文件。路径为空、文件不存在、读坏了，或**记的树不是这一棵**，
    /// 都返回一个空缓存（重算一遍就是，不是错）。
    /// </summary>
    public static AuditCache Load(string? path, string outputRoot, string inputRoot)
    {
        if (string.IsNullOrEmpty(path)) return Empty();

        // 同进程批量转换：上一包刚 Put 进内存的结论就是最新的，不必每包重读 2 MB JSON。
        if (SharedEnabled)
        {
            string key = Path.GetFullPath(path);
            if (Shared.TryGetValue(key, out AuditCache? hit)) return hit;
            if (!System.IO.File.Exists(path)) return Empty();
            AuditCache loaded = LoadFromDisk(path, outputRoot, inputRoot);
            Shared[key] = loaded;
            return loaded;
        }

        if (!System.IO.File.Exists(path)) return Empty();
        return LoadFromDisk(path, outputRoot, inputRoot);
    }

    private static AuditCache LoadFromDisk(string path, string outputRoot, string inputRoot)
    {
        try
        {
            var file = JsonConvert.DeserializeObject<File>(System.IO.File.ReadAllText(path));
            if (file?.Entries == null
                || !string.Equals(file.OutputRoot, outputRoot, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(file.InputRoot, inputRoot, StringComparison.OrdinalIgnoreCase))
                return Empty();

            return new AuditCache(new Dictionary<string, Stored>(file.Entries, StringComparer.Ordinal));
        }
        catch
        {
            return Empty();
        }
    }

    public void Save(string? path, string outputRoot, string inputRoot)
    {
        if (string.IsNullOrEmpty(path)) return;

        // 同进程批量转换：内存里的这份就是权威，落盘推迟到进程退出一次完成。
        if (SharedEnabled)
        {
            string key = Path.GetFullPath(path);
            Shared[key] = this;
            Pending[key] = (outputRoot, inputRoot);
            RegisterExitFlush();
            return;
        }

        SaveToDisk(path, outputRoot, inputRoot);
    }

    private void SaveToDisk(string path, string outputRoot, string inputRoot)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(new File
        {
            OutputRoot = outputRoot,
            InputRoot = inputRoot,
            Entries = _entries,
        }));
    }

    private static bool SharedEnabled =>
        Environment.GetEnvironmentVariable("SDVCP_SHARED_AUDIT_CACHE") == "1";

    private static readonly Dictionary<string, AuditCache> Shared =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (string OutputRoot, string InputRoot)> Pending =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _exitRegistered;

    private static void RegisterExitFlush()
    {
        if (_exitRegistered) return;
        _exitRegistered = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach ((string path, (string outputRoot, string inputRoot)) in Pending)
            {
                try
                {
                    if (Shared.TryGetValue(path, out AuditCache? cache))
                        cache.SaveToDisk(path, outputRoot, inputRoot);
                }
                catch
                {
                    // 退出时写不进去就丢掉缓存，下一次重算；绝不能因此让进程非零退出。
                }
            }
        };
    }

    private static string Key(string kind, string rel) => kind + "|" + rel.Replace('\\', '/');

    /// <summary>
    /// 自检要重算的那些 json：**这一轮写过**的（落盘账本 <c>PlannedWrites</c> 就是它）。
    /// 按字典序返回，好让自检结论的顺序稳定。
    /// </summary>
    public static string[] JsonWrittenBy(Workspace.OutputTree output) =>
        output.PlannedWrites.Keys
            .Where(k => k.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// 取某个文件上次的结论（**不碰文件系统**）。
    /// <para>
    /// ⚠ 调用方要先自行判断「这份文件这一轮有没有被动过」—— 动过的必须重算，
    /// 不能问缓存（见 <see cref="JsonWrittenBy"/> 与类注释里的新鲜度说明）。
    /// </para>
    /// </summary>
    public bool TryGet(string kind, string rel, out Entry entry)
    {
        entry = null!;
        if (!_entries.TryGetValue(Key(kind, rel), out Stored? hit)) return false;

        Reused++;
        entry = new Entry(hit.Skipped, hit.Checked, hit.Findings, hit.Counts);
        return true;
    }

    public void Put(string kind, string rel, Entry entry)
    {
        Recomputed++;
        _entries[Key(kind, rel)] = new Stored
        {
            Skipped = entry.Skipped,
            Checked = entry.Checked,
            Findings = entry.Findings,
            Counts = entry.Counts,
        };
    }

    /// <summary>
    /// 上次的**全部**结论（去掉 <paramref name="kind"/> 前缀）。
    /// 自检拿它补上「这一轮没重算」的那些文件 —— 于是不必再枚举整棵树。
    /// </summary>
    public IEnumerable<(string Rel, Entry Entry)> Cached(string kind)
    {
        string prefix = kind + "|";
        foreach ((string key, Stored value) in _entries)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            yield return (key[prefix.Length..],
                new Entry(value.Skipped, value.Checked, value.Findings, value.Counts));
        }
    }
}
