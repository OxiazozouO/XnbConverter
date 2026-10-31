using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Workspace;

/// <summary>已装模组索引，读 <c>Mods/*/manifest.json</c> 的 UniqueID，并记住该 mod 的目录。</summary>
public sealed class InstalledModIndex
{
    // UniqueID → 含 manifest.json 的那层目录。config.json 按 SMAPI 约定就在同一层。
    private readonly Dictionary<string, string> _directories = new(StringComparer.OrdinalIgnoreCase);
    // UniqueID → 已解析的 config.json（null = 没有或解析失败），避免同一 mod 反复读盘。
    private readonly Dictionary<string, JObject?> _configs = new(StringComparer.OrdinalIgnoreCase);
    // 纯 XNB 运行时**真正还会存在**的 mod：内容包（ContentPackFor）和我们烘焙了内容的框架。
    // 其余带 EntryDll 的纯代码 mod 在无 SMAPI 的装机里不会运行 ⇒ `HasMod` 应为 false。
    private readonly HashSet<string> _runtime = new(StringComparer.OrdinalIgnoreCase);
    public int Count => _directories.Count;

    /// <summary>
    /// 内容会被 <c>bake-frameworks</c> 烘进 XNB 的框架 mod：它们的 DLL 虽然不在，
    /// 但内容已经在树里 ⇒ 运行时集合里仍算存在（用于空值删除保护）。
    /// 与 <c>FrameworkBaker</c> 的 UID 常量保持一致。
    /// </summary>
    private static readonly HashSet<string> BakedFrameworks = new(StringComparer.OrdinalIgnoreCase)
    {
        "peacefulend.alternativetextures",
        "digus.mailframeworkmod",
        "alja.cccb",
        "peacefulend.solidfoundations",
        "peacefulend.customcompanions",
        "epicbellyflop45.moregrass",
        "hootless.buslocations",
        "cherry.trainstation",
    };

    /// <summary>缓存文件里的那份索引 + 它是按什么键建的（键不符就重扫）。</summary>
    private sealed class DiskCache
    {
        public string Key = "";
        public Dictionary<string, string> Directories = new(StringComparer.OrdinalIgnoreCase);
        public List<string>? Runtime;
    }

    /// <param name="modsDirectory">游戏 Mods 目录。</param>
    /// <param name="cachePath">
    /// **可选的**磁盘缓存路径：给了就（键相符时）直接读上次那份索引。
    /// <para>
    /// <b>为什么要它</b>：递归扫整个 Mods 树（本机十万量级文件项）实测 **1.15 秒/次**；
    /// 链式跑批是"**每包一个进程**"⇒ 232 包白扫 232 次 ≈ **4.4 分钟**。SMAPI 自己每次启动也只扫一遍。
    /// 这是**工具链**要的东西（同一棵 Mods 树、连着转几十个包），所以做成**显式 opt-in**：
    /// 不传这个参数就是每次真扫，行为与以前**一模一样**。
    /// </para>
    /// <para>
    /// ⚠ <b>失效判据是"够用就好"</b>：键 = Mods 目录的 <c>LastWriteTimeUtc</c> + 一级目录数
    /// ⇒ **加/删 mod 会失效**（目录 mtime 变 ✓）；但**就地改某个 manifest 的 UniqueID**
    /// <b>不会</b>被察觉（那要逐文件 stat，等于又扫一遍）。工具链不会那样改，普通用户不传这个参数。
    /// </para>
    /// </param>
    public InstalledModIndex(string modsDirectory, string? cachePath = null)
    {
        if (!Directory.Exists(modsDirectory))
            throw new DirectoryNotFoundException($"Mods 目录不存在：{modsDirectory}");

        string key = CacheKey(modsDirectory);
        if (cachePath != null && TryLoad(cachePath, key))
            return;

        foreach (string manifest in Directory.EnumerateFiles(modsDirectory, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                JObject obj = JsoncReader.ParseFile(manifest);
                string? id = obj["UniqueID"]?.Value<string>();
                string? dir = Path.GetDirectoryName(manifest);
                if (string.IsNullOrWhiteSpace(id) || dir == null) continue;
                _directories[id] = dir;

                // 纯 XNB 装机没有 SMAPI：带 EntryDll 的纯代码 mod 不会加载。
                // 内容包（ContentPackFor）由我们转换；烘焙框架的内容由 bake-frameworks 写进树。
                bool hasDll = !string.IsNullOrWhiteSpace(obj["EntryDll"]?.Value<string>());
                bool isContentPack = obj["ContentPackFor"] != null;
                if (!hasDll || isContentPack || BakedFrameworks.Contains(id))
                    _runtime.Add(id);
            }
            catch
            {
                // 单个 manifest 坏了不影响其它
            }
        }

        if (cachePath != null) TrySave(cachePath, key);
    }

    /// <summary>缓存键：Mods 目录的 mtime（UTC ticks）+ 一级目录数。见构造函数的失效说明。</summary>
    private static string CacheKey(string modsDirectory)
    {
        long ticks = Directory.GetLastWriteTimeUtc(modsDirectory).Ticks;
        int top = Directory.EnumerateDirectories(modsDirectory).Count();
        return $"{ticks}-{top}";
    }

    private bool TryLoad(string cachePath, string key)
    {
        try
        {
            if (!File.Exists(cachePath)) return false;
            DiskCache? cache = JsoncReader.ParseFile(cachePath).ToObject<DiskCache>();
            if (cache == null || !string.Equals(cache.Key, key, StringComparison.Ordinal)) return false;
            if (cache.Runtime == null) return false;   // 旧缓存没有运行时集合：重扫一遍

            foreach ((string id, string dir) in cache.Directories)
                if (!string.IsNullOrWhiteSpace(id)) _directories[id] = dir;
            foreach (string id in cache.Runtime)
                if (!string.IsNullOrWhiteSpace(id)) _runtime.Add(id);
            return _directories.Count > 0;
        }
        catch
        {
            return false;   // 缓存坏了就当没有：重扫一遍即可
        }
    }

    private void TrySave(string cachePath, string key)
    {
        try
        {
            var cache = new DiskCache { Key = key };
            foreach ((string id, string dir) in _directories) cache.Directories[id] = dir;
            File.WriteAllText(cachePath, new JObject
            {
                ["Key"] = cache.Key,
                ["Directories"] = JObject.FromObject(cache.Directories),
                ["Runtime"] = new JArray(_runtime.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            }.ToString());
        }
        catch
        {
            // 写不进去不影响本次运行（下次再扫）
        }
    }

    /// <summary>模组**装没装**（含纯代码 mod）。配置读取等用这个。</summary>
    public bool Contains(string uniqueId) => _directories.ContainsKey(uniqueId);

    /// <summary>
    /// **运行时这个 mod 还在不在**：纯 XNB 装机没有 SMAPI ⇒ 带 <c>EntryDll</c> 的纯代码 mod
    /// 不会加载；内容包和已烘焙框架算存在（见 <see cref="BakedFrameworks"/>）。
    /// <para>
    /// ⚠ 只用于「依赖未烘焙框架时保留原版条目」（空值删除保护）。**不要拿它替换 <c>HasMod</c>** ——
    /// 实测会让 FTM/Include 的整块内容直接不展开（16 条 Data/Locations、14 条对话等消失）。
    /// </para>
    /// </summary>
    public bool ContainsRuntime(string uniqueId) => _runtime.Contains(uniqueId);

    /// <summary>已装模组的 UniqueID 集合（含纯代码 mod；配置/诊断用）。</summary>
    public IReadOnlyCollection<string> UniqueIds => _directories.Keys;

    /// <summary>纯 XNB 运行时仍存在的 UniqueID 集合（诊断/删除保护用）。</summary>
    public IReadOnlyCollection<string> RuntimeUniqueIds => _runtime;

    /// <summary>某已装 mod 的目录（含 manifest.json 的那层）；没装该 UniqueID → <c>null</c>。</summary>
    public string? GetModDirectory(string uniqueId) =>
        _directories.TryGetValue(uniqueId, out string? dir) ? dir : null;

    /// <summary>
    /// 读某已装 mod 的配置：mod 目录下有真实 <c>config.json</c> 就用它，没有（或解析失败）→ <c>null</c>
    /// （求不了值，相关条件判 NeedsReview）。结果带缓存。
    /// </summary>
    public JObject? GetModConfig(string uniqueId)
    {
        if (_configs.TryGetValue(uniqueId, out JObject? cached)) return cached;

        JObject? config = null;
        string? dir = GetModDirectory(uniqueId);
        if (dir != null)
        {
            string path = Path.Combine(dir, "config.json");
            if (File.Exists(path))
            {
                try { config = JsoncReader.ParseFile(path); }
                catch { config = null; }
            }
        }

        _configs[uniqueId] = config;
        return config;
    }

}
