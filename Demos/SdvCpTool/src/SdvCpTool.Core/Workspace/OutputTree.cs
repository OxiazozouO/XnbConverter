using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Workspace;

/// <summary>产出文件落盘时的形态。</summary>
public enum WriteKind
{
    /// <summary>文本（<c>.json</c> / <c>.config</c> / <c>.yaml</c> …）。</summary>
    Text,

    /// <summary>内存里生成的二进制（<c>Load</c> 的 tbin 载荷等）。</summary>
    Bytes,

    /// <summary>整份照搬某个源文件（音频 ogg 等）。</summary>
    Copy,

    /// <summary>像素模型（<c>ImageCache</c>）—— 解码一次、改在内存、最后重编码一次。</summary>
    Image,

    /// <summary>地图模型（<c>MapCache</c>）—— 解析一次、改在内存、最后序列化一次。</summary>
    Map,
}

/// <summary>
/// 产出树：把输入树整树拷贝到输出目录，之后所有写入都落在输出目录。
/// 输入树<b>只读</b> —— 从不修改它。
/// </summary>
public sealed class OutputTree
{
    /// <summary>Windows 文件名里不允许的字符（<c>/</c>、<c>\</c> 是目录分隔符，已在校验前归一化）。</summary>
    private static readonly char[] IllegalNameChars = { '<', '>', ':', '"', '|', '?', '*' };

    /// <summary>Windows 保留设备名；这些名字（含 <c>NUL.txt</c> 这种带扩展名的形式）不能作为文件/目录名。</summary>
    private static readonly string[] ReservedDeviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>单个路径段的字符数上限（NTFS）。</summary>
    private const int MaxSegmentLength = 255;

    /// <summary>完整输出路径的字符数上限（经典 Win32 MAX_PATH）。</summary>
    private const int MaxFullPathLength = 259;

    public string InputRoot { get; }
    public string OutputRoot { get; }

    /// <summary>产出目录原本就非空 ⇒ 这次是**在已有树上续写**（没有重拷树）。</summary>
    public bool Append { get; }
    public IReadOnlyList<string> InputFiles { get; }

    /// <summary>延迟写：写入先进内存，直到 <see cref="Flush"/> 才落盘。</summary>
    /// <summary>是否处于延迟写模式。给缓存层用 —— 它据此决定「攒着一起写」还是「立刻写」。</summary>
    /// <summary>待落的文本（相对路径 → 内容）。</summary>
    /// <summary>
    /// 待落的文本（相对路径 → 内容）。
    /// <para>
    /// ⚠ **必须大小写不敏感**。产出文件系统是 Windows（见 <see cref="ToOutputPath"/>），
    /// <c>Characters/Dialogue/Andy.json</c> 与 <c>characters/dialogue/andy.json</c> 是**同一个文件**。
    /// 用序号比较会记成两个槽位，于是「谁能赢」由 <see cref="Flush"/> 的排序决定、而不是由
    /// **写入顺序**决定 —— 实测 SVE 的一条 <c>Load</c> 把同一份对话按三种大小写写了 10 次，
    /// 结果 137 键的那份被 4 键的那份盖掉（100 个文件对不上旧基线）。
    /// </para>
    /// </summary>
    private readonly Dictionary<string, string> _pendingText = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>待拷的文件（相对路径 → 源文件绝对路径）。只记**最终**来源，重复暂存时后者覆盖前者。</summary>
    private readonly Dictionary<string, string> _pendingCopy = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>待写的二进制（相对路径 → 内容）。</summary>
    private readonly Dictionary<string, byte[]> _pendingBytes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 大二进制（地图/音频）暂存到同卷临时文件，Flush 时 Move 到产出树。
    /// 延迟写语义不变，但字节不再在内存里挂到 Flush；实测 map 载荷单个 16–33 MB。
    /// </summary>
    private readonly Dictionary<string, string> _pendingMove = new(StringComparer.OrdinalIgnoreCase);
    // 溢写对两个阶段是相反的：Loads 多写临时文件很贵；Edits 若把 1 GB 地图留在
    // LOH，GC 会把 SVE 从 57 s 拖到 188 s。批量入口按阶段设置阈值。
    private readonly int _spillThreshold =
        int.TryParse(Environment.GetEnvironmentVariable("SDVCP_SPILL_MB"), out int spillMb)
            && spillMb > 0
            ? spillMb * 1024 * 1024
            : int.MaxValue;
    private string? _stageDir;

    /// <summary>
    /// 待落的 **JSON**（相对路径 → 已解析的 token）。
    /// <para>
    /// 与 <see cref="_pendingText"/> 的区别：这里存的是**还没序列化**的对象。
    /// 大资产（<c>Data/Objects</c> 一类 1 MB 上下）在一轮里会被几十条补丁反复改，
    /// 每条都 <c>ToString</c> 一遍纯属浪费 —— 攒到最后 <see cref="Flush"/> 序列化一次。
    /// 中途有人要读文本就走 <see cref="_pendingJsonText"/>（懒序列化，读完留着给 Flush 复用）。
    /// </para>
    /// </summary>
    private readonly Dictionary<string, JToken> _pendingJson = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>上面那些 token 的序列化结果（懒生成，Flush 复用）。</summary>
    private readonly Dictionary<string, string> _pendingJsonText = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 待落的文本，但**连内容都等到 <see cref="Flush"/> 才生成**（相对路径 → 生成函数）。
    /// <para>
    /// 给「同一份文档按语言展开成多份」那种写用：变体文档与基础文档**结构完全相同**、
    /// 只差那几个 <c>{{i18n:…}}</c> 节点的值。大资产（<c>Data/Objects</c> 三万多个节点）
    /// 在一轮里会被几十条补丁反复改，每条都把 9 个语言各序列化一遍 —— 实测
    /// `聚宝盆 制作` 一包 720 次序列化里，**89% 是白做的**（同一个资产被写了 39 次，
    /// 只有最后一次落盘）。这里和 <see cref="_pendingJson"/> 同一个道理：攒到最后。
    /// </para>
    /// <para>
    /// ⚠ 生成函数**必须**把共享文档改回原样再返回（见 <c>TranslationApplier</c> 的用法）：
    /// 基础资产与各语言变体共用同一份 token，函数返回时留下的状态会决定基础资产的内容。
    /// </para>
    /// </summary>
    private readonly Dictionary<string, Func<string>> _pendingLazyText = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 同 <see cref="_pendingLazyText"/>，但生成的是 <see cref="JToken"/>：语言变体被
    /// 下一包读取时可以直接拿对象，不必先序列化整份大文档再解析回来。
    /// </summary>
    private readonly Dictionary<string, Func<JToken>> _pendingLazyJson = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 每条产出路径**第一次**被暂存时用的写法（保留大小写）。
    /// <para>
    /// 产出文件系统是 Windows：**往已存在的文件里写不会改名字**，所以目录项的名字由
    /// **第一次写它的那次**决定。四种暂存形态分开存，落盘又分四个批次 ——
    /// 不显式记下「第一次是谁写的」，名字就会被**落盘批次顺序**决定（那是纯巧合）。
    /// 实测：SVE 先 <c>Load Data/Events/JojaMart</c>（大写）、后有几十条
    /// <c>EditData data/events/jojamart</c>（小写），产出树里该是大写。
    /// </para>
    /// </summary>
    private readonly Dictionary<string, string> _displayPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>记下这条路径第一次被写时的写法（只记第一次，后来的不覆盖）。</summary>
    private void NotePath(string rel) => _displayPath.TryAdd(rel, rel);

    /// <summary>落盘时该用的路径写法（没记过就用传入的）。</summary>
    private string DisplayPath(string rel) =>
        _displayPath.TryGetValue(rel, out string? first) ? first : rel;

    /// <summary>已落盘的路径，按落盘顺序（诊断/测试用）。</summary>
    public IReadOnlyList<string> FlushedFiles => _flushed;
    private readonly List<string> _flushed = new();

    /// <summary>
    /// 本次运行**要落盘**的产出文件汇总（相对路径 → 形态）。
    /// <para>
    /// 所有写入方（补丁、发射器、两个内存模型）都往这里登记，**落盘之前**就能拿到完整清单 ——
    /// 这就是「先对要操作的文件做汇总、再执行」里那份汇总。
    /// </para>
    /// <para>
    /// ⚠ 它只是**账本**，不参与任何判定，也不决定执行顺序（执行顺序由各层自己的
    /// <c>Flush()</c> 按字典序定）。拿它回答「这一轮到底动了哪些文件、各是什么形态」。
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, WriteKind> PlannedWrites => _planned;
    private readonly Dictionary<string, WriteKind> _planned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 暂存字典的锁：同一包的补丁现在可以**按目标资产并行**写（不同资产互不相干），
    /// 所有暂存/读取都要经过它。同一资产的多条补丁由调度分到同一条 lane，仍然串行。
    /// </summary>
    private readonly object _sync = new();

    /// <summary>登记一个产出文件及其形态（同一个路径后来换形态就改写这条）。</summary>
    public void NoteWrite(string relativePath, WriteKind kind)
    {
        lock (_sync) _planned[Normalize(relativePath)] = kind;
    }

    /// <summary>`using var g = Enter();` 形式的锁；<see cref="Monitor"/> 可重入，方法间嵌套调用安全。</summary>
    private readonly struct LockScope : IDisposable
    {
        private readonly object _gate;
        public LockScope(object gate) { _gate = gate; Monitor.Enter(gate); }
        public void Dispose() => Monitor.Exit(_gate);
    }

    private LockScope Enter() => new(_sync);

    /// <param name="append">
    /// <c>true</c> = 输出目录已存在时**不报错、也不重新拷树**，直接在它上面续写。
    /// 给「多个包依次进同一棵树」用：链式的正确形态是**在同一棵树上依次应用补丁**，
    /// 第 2 个包起没必要把原版树再拷一遍（600 MB 上下，纯白做工）。
    /// <para>
    /// ⚠ 默认 <c>false</c>（输出目录必须为空）是为了**不覆盖用户数据**，别改这个默认值。
    /// </para>
    /// </param>
    /// <param name="deferWrites">
    /// <c>true</c> = 写入暂存在内存里，直到 <see cref="Flush"/> 才落盘。**默认 false**（立即写）。
    /// <para>
    /// 为什么要它：<see cref="ResourceTree"/>（判定读）读的是**磁盘**。若判定与写共用一棵
    /// 产出树、且写是立即的，那么**同一次运行里先写的会被后判的看到** —— 实测这让
    /// 13 个 `Data/Events/*` 与「判定读输入树、写另一棵」的旧行为不一致。暂存后
    /// <see cref="ReadTextOrNull"/> 看得到 pending（同一运行内后续补丁要能读前面写的，
    /// 与旧行为一致），而 <see cref="ResourceTree"/> 只看盘、看不到 pending —— 两边都对。
    /// </para>
    /// </param>
    public OutputTree(string inputRoot, string outputRoot)
    {
        if (!Directory.Exists(inputRoot))
            throw new DirectoryNotFoundException($"输入树不存在：{inputRoot}");

        InputRoot = Path.GetFullPath(inputRoot);
        OutputRoot = Path.GetFullPath(outputRoot);

        // 续写不续写**由目录状态决定**：非空 = 在它上面接着写，空/不存在 = 先整树拷一份。
        // （没有开关：以前是 `--append`，忘了带就会重拷一遍 600 MB，带错了又会盖掉别人的目录。）
        Append = Directory.Exists(OutputRoot) && Directory.EnumerateFileSystemEntries(OutputRoot).Any();

        List<string> files;
        if (SharedTreeIndex.TryGet(OutputRoot, out _,
                out IReadOnlyList<string> sharedFiles,
                out HashSet<string> sharedSet,
                out Dictionary<string, List<string>> sharedConfigs))
        {
            // 同一进程的链式转换：上一包落盘后目录只多了几十个文件，不必每包重扫整棵树。
            files = new List<string>(sharedFiles);
            _inputFileSet = sharedSet;
            _configsByDir = sharedConfigs;
        }
        else if (Append)
        {
            // 续写：不拷树，只把**已有**的文件登记成 InputFiles（调用方会拿它做 ContentHashes 之类的对照）
            files = new List<string>();
            foreach (string file in Directory.EnumerateFiles(OutputRoot, "*", SearchOption.AllDirectories))
                files.Add(Path.GetRelativePath(OutputRoot, file).Replace('\\', '/'));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            SharedTreeIndex.Reset(OutputRoot, files);
        }
        else
        {
            files = new List<string>();
            foreach (string file in Directory.EnumerateFiles(InputRoot, "*", SearchOption.AllDirectories))
                files.Add(Path.GetRelativePath(InputRoot, file).Replace('\\', '/'));

            // 整树拷贝**并行**：一万多个文件、600 MB 上下，串行拷是这一包里最贵的一步。
            // 目标路径两两不同，互不相干。
            System.Threading.Tasks.Parallel.ForEach(files, rel =>
            {
                string dest = ToOutputPath(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(Path.Combine(InputRoot, rel), dest, overwrite: true);
            });
            files.Sort(StringComparer.OrdinalIgnoreCase);
            SharedTreeIndex.Reset(OutputRoot, files);
        }

        InputFiles = files;
    }

    /// <summary>
    /// 把相对路径映射到输出目录下的绝对路径，拒绝越界。
    /// <para>
    /// ⚠ **大小写不做归一，靠 Windows 文件系统吸收**。语料里同一资产会以不同大小写出现
    /// （SVE 的 `code/Festivals/` 有 580 种小写 Target：`characters/Dialogue/*`、
    /// `data/festivals/*`、`strings/schedules/*`）。在 Windows 上 `characters/X` 与
    /// `Characters/X` 是同一个文件，所以「先写小写、后写大写」自然变成覆盖/合并，
    /// 顺序与 CP 一致 —— 实测 SVE 产出树里只有 `Characters`/`Data`/`Strings` 各一份。
    /// **换到大小写敏感的文件系统（Linux）就会出一模一样的两份**，那时得先归一化。
    /// </para>
    /// </summary>
    public string ToOutputPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        string full = Path.GetFullPath(Path.Combine(OutputRoot, normalized));

        // 防 `..` 逃逸
        if (!full.StartsWith(OutputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, OutputRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"相对路径越界：{relativePath}", nameof(relativePath));
        }
        return full;
    }

    /// <summary>
    /// 校验相对路径能否作为<b>输出目标</b>。合法返回 <c>null</c>，非法返回可读原因。
    /// <para>
    /// 判的是 Windows 会拒绝的形态：非法字符 <c>&lt; &gt; : " | ? *</c>、保留设备名
    /// （<c>CON</c>/<c>NUL</c>/<c>COM1</c>…，含带扩展名的形式）、空路径段、以点或空格结尾的段、
    /// 段或整条路径超长。只做静态判定，<b>不碰文件系统</b>。
    /// </para>
    /// <para>
    /// 写入前先判一次，是为了把「坏路径」降级为该条补丁写入失败 —— 而不是等
    /// <see cref="Directory.CreateDirectory(string)"/> / <see cref="File.WriteAllText(string,string)"/>
    /// 抛出异常，把整次转换带走（实测：SVE 的 <c>Characters/Dialogue/**Martin**</c>）。
    /// </para>
    /// </summary>
    public string? ValidatePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "路径为空";

        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0)
            return "路径为空";

        if (normalized.Any(c => c < 32))
            return "路径含控制字符";

        foreach (char c in IllegalNameChars)
        {
            if (normalized.Contains(c))
                return $"路径含非法字符 `{c}`";
        }

        foreach (string segment in normalized.Split('/'))
        {
            if (segment.Length == 0)
                return "路径含空的目录段";

            if (segment is "." or "..")
                return $"路径含相对目录段 `{segment}`";

            int dot = segment.IndexOf('.');
            string bare = dot < 0 ? segment : segment[..dot];
            if (ReservedDeviceNames.Contains(bare, StringComparer.OrdinalIgnoreCase))
                return $"路径段 `{segment}` 是 Windows 保留设备名";

            if (segment[^1] is '.' or ' ')
                return $"路径段 `{segment}` 以点或空格结尾（Windows 会截断，写出的名字与预期不符）";

            if (segment.Length > MaxSegmentLength)
                return $"路径段超过 {MaxSegmentLength} 字符";
        }

        // 上面已排除 `..`，所以这里是落盘时完整路径的长度
        if (OutputRoot.Length + 1 + normalized.Length > MaxFullPathLength)
            return $"完整输出路径超过 {MaxFullPathLength} 字符";

        return null;
    }

    /// <summary>相对路径的规范形式（暂存字典的键用它，避免 `a\\b` 与 `a/b` 记成两条）。</summary>
    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    // ------------------------------------------------------------------ `.config` 候选特征缓存
    //
    // `AssetCreator` 每给一个新资产选 `.config` 模板，都要把**整个目录**的候选比一遍：
    // 形状分要读同名 `.json`、泛型分要读 `.config`。`Data/` 有 263 份候选
    // （含 1.2 MB 的 `Shops.json`，总量 29 MB），本语料新建 `Data/*` 资产上千个 ——
    // 不缓存就是把这几百份文件反复读、反复解析。
    //
    // 特征只取决于**文件内容**，所以任何一次写入都让相关条目作废（见 InvalidateConfigFacts）。

    /// <summary>候选 <c>.config</c> 的只读特征（见 <see cref="GetConfigFacts"/>）。</summary>
    public readonly record struct ConfigFacts(
        string? ReaderType, JTokenType? JsonRootType, JTokenType? JsonFirstValueType);

    private readonly Dictionary<string, ConfigFacts> _configFacts = new(StringComparer.OrdinalIgnoreCase);

    // 链式批量转换里每个包都新建 OutputTree；同一份 `.config`/同名 JSON 的特征只取决于内容，
    // 跨包复用可以从「每包重读几百份配置与 Data JSON」降到「只在文件被改写时重读」。
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ConfigFacts>
        SharedConfigFacts = new(StringComparer.OrdinalIgnoreCase);

    private static string SharedFactKey(string root, string rel) => root + "\n" + rel;

    /// <summary>
    /// 一份 <c>.config</c> 的只读特征：根 Reader 的类型串（<c>Readers[0].Type</c>），
    /// 以及**同名 `.json`** 的根形状与首 entry 值类型。
    /// <para>同一次运行里同一个路径只读+解析一次；该路径或它的同名 `.json` 被写过就作废重算。</para>
    /// </summary>
    public ConfigFacts GetConfigFacts(string configRelativePath)
    {
        string rel = Normalize(configRelativePath);
        lock (_sync)
            if (_configFacts.TryGetValue(rel, out ConfigFacts hit)) return hit;

        bool shared = SharedTreeIndex.Enabled;
        string key = SharedFactKey(OutputRoot, rel);
        if (shared && SharedConfigFacts.TryGetValue(key, out ConfigFacts cached))
        {
            lock (_sync) _configFacts[rel] = cached;
            return cached;
        }

        ConfigFacts facts = ComputeConfigFacts(rel);
        // 并行时两个线程可能同时算同一份（结果相同），只保证字典写入安全。
        lock (_sync) _configFacts[rel] = facts;
        if (shared) SharedConfigFacts[key] = facts;
        return facts;
    }

    private ConfigFacts ComputeConfigFacts(string configRel)
    {
        string? readerType = null;
        string? configJson = ReadTextOrNull(configRel);
        if (configJson != null)
        {
            try { readerType = (JObject.Parse(configJson)["Readers"] as JArray)?[0]?["Type"]?.Value<string>(); }
            catch { readerType = null; }
        }

        JTokenType? rootType = null;
        JTokenType? firstType = null;
        if (configRel.EndsWith(".config", StringComparison.OrdinalIgnoreCase))
        {
            string jsonRel = configRel[..^".config".Length] + ".json";
            string? json = ReadTextOrNull(jsonRel);
            if (json != null)
            {
                try
                {
                    JToken token = JToken.Parse(json);
                    rootType = token.Type;
                    firstType = token switch
                    {
                        JObject obj => obj.Properties().FirstOrDefault()?.Value?.Type,
                        JArray arr => arr.FirstOrDefault()?.Type,
                        _ => null,
                    };
                }
                catch { rootType = null; firstType = null; }
            }
        }

        return new ConfigFacts(readerType, rootType, firstType);
    }

    /// <summary>该路径（或它的同名 <c>.config</c> / <c>.json</c>）内容变了 ⇒ 特征缓存作废。</summary>
    private void InvalidateConfigFacts(string relativePath)
    {
        if (_configFacts.Count == 0) return;

        string rel = Normalize(relativePath);
        _configFacts.Remove(rel);
        if (SharedTreeIndex.Enabled) SharedConfigFacts.TryRemove(SharedFactKey(OutputRoot, rel), out _);

        if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string configRel = rel[..^".json".Length] + ".config";
            _configFacts.Remove(configRel);
            if (SharedTreeIndex.Enabled)
                SharedConfigFacts.TryRemove(SharedFactKey(OutputRoot, configRel), out _);
        }
    }

    public void WriteText(string relativePath, string content)
    {
        using var _gate = Enter();
        _ = ToOutputPath(relativePath);   // `..` 逃逸仍抛 ArgumentException（既有契约）

        string? invalid = ValidatePath(relativePath);
        if (invalid != null)
            throw new IOException($"输出路径非法：{invalid}");

        string rel = Normalize(relativePath);
        // 同一个产出路径只允许有一种暂存形态：后来居上，其余全丢掉。
        // ⚠ 少了 `_pendingJson` 这一句，`Flush` 里 JSON 排在文本**后面**写，
        //    会把后设的文本盖回去（实测表现：资产变成 `{}`）。
        _pendingCopy.Remove(rel);
        RemovePendingMove(rel);
        RemovePendingBytes(rel);
        _pendingJson.Remove(rel);
        _pendingJsonText.Remove(rel);
        _pendingLazyText.Remove(rel);
        _pendingLazyJson.Remove(rel);
        _pendingText[rel] = content;
        InvalidateConfigFacts(rel);
        NoteWrite(rel, WriteKind.Text);
    }

    /// <summary>
    /// 预定「这个产出文件最终来自某个源文件」——**只记来源**，真正拷贝留到 <see cref="Flush"/>。
    /// 图片、音频、`Load` 的二进制都走它：同一个目标被多个补丁 Load 时，最后那个来源胜出
    /// （CP 的语义），中间那些源文件**一次都不用拷**。
    /// <para>⚠ 非延迟模式（默认）下就是立即 <see cref="File.Copy(string,string,bool)"/>。</para>
    /// </summary>
    public void StageCopy(string relativePath, string sourceFullPath)
    {
        using var _gate = Enter();
        _ = ToOutputPath(relativePath);   // `..` 逃逸仍抛 ArgumentException（既有契约）
        string? invalid = ValidatePath(relativePath);
        if (invalid != null)
            throw new IOException($"输出路径非法：{invalid}");

        string rel = Normalize(relativePath);
        _pendingText.Remove(rel);
        RemovePendingBytes(rel);
        _pendingJson.Remove(rel);
        _pendingJsonText.Remove(rel);
        _pendingLazyText.Remove(rel);
        _pendingLazyJson.Remove(rel);
        NotePath(rel);
        _pendingCopy[rel] = sourceFullPath;
        InvalidateConfigFacts(rel);
        NoteWrite(rel, WriteKind.Copy);
    }

    /// <summary>
    /// 预定写入一份 **JSON**（已解析的对象）。落盘时序列化一次；中途读文本会懒序列化并留用。
    /// </summary>
    public void StageJson(string relativePath, JToken data)
    {
        using var _gate = Enter();
        _ = ToOutputPath(relativePath);   // `..` 逃逸仍抛 ArgumentException（既有契约）
        string? invalid = ValidatePath(relativePath);
        if (invalid != null)
            throw new IOException($"输出路径非法：{invalid}");

        string rel = Normalize(relativePath);
        _pendingText.Remove(rel);
        RemovePendingBytes(rel);
        _pendingCopy.Remove(rel);
        RemovePendingMove(rel);
        _pendingLazyText.Remove(rel);
        _pendingLazyJson.Remove(rel);
        NotePath(rel);
        _pendingJson[rel] = data;
        _pendingJsonText.Remove(rel);      // 内容变了，序列化结果作废
        InvalidateConfigFacts(rel);
        NoteWrite(rel, WriteKind.Text);
    }

    /// <summary><see cref="StageJson"/> 的<b>不抛异常</b>版本。</summary>
    public bool TryStageJson(string relativePath, JToken data, out string? error)
    {
        string? invalid = ValidatePath(relativePath);
        if (invalid != null)
        {
            error = $"输出路径非法：{invalid}";
            return false;
        }

        try
        {
            StageJson(relativePath, data);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>预定写入一段二进制（内存里已有的内容，如 `Load` 的 tbin 载荷）。</summary>
    /// <summary>撤掉某路径的待落盘字节；来自池的缓冲立即归还。</summary>
    private void RemovePendingBytes(string rel)
    {
        if (_pendingBytes.Remove(rel, out byte[]? old)) ByteBufferPool.Return(old);
    }

    private void RemovePendingMove(string rel)
    {
        if (!_pendingMove.Remove(rel, out string? temp)) return;
        try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 临时文件下次再清 */ }
    }

    private string StageDirectory()
    {
        if (_stageDir != null) return _stageDir;
        string parent = Path.GetDirectoryName(OutputRoot.TrimEnd(Path.DirectorySeparatorChar))
                        ?? Path.GetTempPath();
        _stageDir = Path.Combine(parent, "." + Path.GetFileName(OutputRoot.TrimEnd(
            Path.DirectorySeparatorChar)) + ".stage");
        Directory.CreateDirectory(_stageDir);
        return _stageDir;
    }

    public void StageBytes(string relativePath, byte[] content)
    {
        using var _gate = Enter();
        _ = ToOutputPath(relativePath);   // `..` 逃逸仍抛 ArgumentException（既有契约）
        string? invalid = ValidatePath(relativePath);
        if (invalid != null)
            throw new IOException($"输出路径非法：{invalid}");

        string rel = Normalize(relativePath);
        _pendingText.Remove(rel);
        _pendingCopy.Remove(rel);
        RemovePendingMove(rel);
        _pendingJson.Remove(rel);
        _pendingJsonText.Remove(rel);
        _pendingLazyText.Remove(rel);
        _pendingLazyJson.Remove(rel);
        RemovePendingBytes(rel);
        RemovePendingMove(rel);
        NotePath(rel);

        if (content.Length >= _spillThreshold)
        {
            string temp = Path.Combine(StageDirectory(), Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(temp, content);
            }
            catch
            {
                try { File.Delete(temp); } catch { }
                throw;
            }
            ByteBufferPool.Return(content);
            _pendingMove[rel] = temp;
        }
        else
        {
            _pendingBytes[rel] = content;
        }

        InvalidateConfigFacts(rel);
        NoteWrite(rel, WriteKind.Bytes);
    }

    /// <summary>
    /// 预定写入一段**到落盘时才生成**的文本（<see cref="_pendingLazyText"/> 的公开入口）。
    /// 与 <see cref="StageJson"/> 同一路：同一个路径后来的写入照常覆盖它、连生成函数一起丢掉 ——
    /// 于是「同一份资产被几十条补丁反复改」时，只有**最后那一次**真的生成内容。
    /// <para>
    /// ⚠ <paramref name="produce"/> 会在 <see cref="Flush"/> 里被调用**一次**（中途有人读这条路径也一样，只调一次）。
    /// </para>
    /// </summary>
    public void StageLazyText(string relativePath, Func<string> produce)
    {
        using var _gate = Enter();
        _ = ToOutputPath(relativePath);   // `..` 逃逸仍抛 ArgumentException（既有契约）
        string? invalid = ValidatePath(relativePath);
        if (invalid != null)
            throw new IOException($"输出路径非法：{invalid}");

        string rel = Normalize(relativePath);
        _pendingText.Remove(rel);
        RemovePendingBytes(rel);
        _pendingCopy.Remove(rel);
        RemovePendingMove(rel);
        _pendingJson.Remove(rel);
        _pendingJsonText.Remove(rel);
        NotePath(rel);
        _pendingLazyJson.Remove(rel);
        _pendingLazyText[rel] = produce;
        InvalidateConfigFacts(rel);
        NoteWrite(rel, WriteKind.Text);
    }

    /// <summary>
    /// <see cref="StageLazyText"/> 的<b>不抛异常</b>版本（单条补丁的写入一律走带 <c>Try</c> 的那份）。
    /// </summary>
    /// <summary>
    /// 预定一段**到落盘时才生成**的 JSON。与 <see cref="StageLazyText"/> 的区别是：
    /// 同一次运行里后续补丁能直接读到 <see cref="JToken"/>，不必先序列化再解析。
    /// </summary>
    public bool TryStageLazyJson(string relativePath, Func<JToken> produce, out string? error)
    {
        string? invalid = ValidatePath(relativePath);
        if (invalid != null)
        {
            error = $"输出路径非法：{invalid}";
            return false;
        }

        try
        {
            using var _gate = Enter();
            _ = ToOutputPath(relativePath);
            string rel = Normalize(relativePath);
            _pendingText.Remove(rel);
            RemovePendingBytes(rel);
            _pendingCopy.Remove(rel);
            RemovePendingMove(rel);
            _pendingJson.Remove(rel);
            _pendingJsonText.Remove(rel);
            _pendingLazyText.Remove(rel);
            NotePath(rel);
            _pendingLazyJson[rel] = produce;
            InvalidateConfigFacts(rel);
            NoteWrite(rel, WriteKind.Text);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryStageLazyText(string relativePath, Func<string> produce, out string? error)
    {
        string? invalid = ValidatePath(relativePath);
        if (invalid != null)
        {
            error = $"输出路径非法：{invalid}";
            return false;
        }

        try
        {
            StageLazyText(relativePath, produce);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 把这条路径上的懒文本生成出来、转成普通暂存文本（读之前必须先做）。
    /// <para>生成函数可能克隆整份大文档 —— **在锁外跑**，否则并行 lane 会被它整段堵住。</para>
    /// </summary>
    private void MaterializeLazyText(string rel)
    {
        Func<string>? produce;
        lock (_sync)
        {
            if (!_pendingLazyText.TryGetValue(rel, out produce)) return;
            _pendingLazyText.Remove(rel);
            _pendingLazyJson.Remove(rel);
        }

        string text = produce();
        lock (_sync) _pendingText[rel] = text;
    }

    private void MaterializeLazyJson(string rel)
    {
        Func<JToken>? produce;
        lock (_sync)
        {
            if (!_pendingLazyJson.TryGetValue(rel, out produce)) return;
            _pendingLazyJson.Remove(rel);
        }

        JToken token = produce();
        lock (_sync)
        {
            _pendingJson[rel] = token;
            _pendingJsonText.Remove(rel);
        }
    }

    /// <summary>
    /// 把所有暂存的内容一次落盘：**文本 → 二进制 → 拷贝**（顺序固定，便于复现）。
    /// 非延迟模式下没有暂存，调用它是空操作。
    /// </summary>
    public void Flush()
    {
        // 懒文本先**全部**生成出来再进写批次：生成函数会改共享文档（见 `_pendingLazyText`），
        // 必须排在写之前的串行段落里，不能跟 `WriteBatch` 的并行写混在一起。
        if (_pendingLazyText.Count > 0 || _pendingLazyJson.Count > 0)
        {
            long _t0 = Diagnostics.Prof.Now();
            foreach (string rel in _pendingLazyJson.Keys.ToArray())
                MaterializeLazyJson(rel);
            foreach (string rel in _pendingLazyText.Keys.ToArray())
                MaterializeLazyText(rel);
            if (Diagnostics.Prof.On) Diagnostics.Prof.AddSince("落盘.变体物化", _t0);
        }

        // 四个批次各自**并行写**：目标路径两两不同（暂存字典是大小写不敏感的，不会撞名），
        // 写文件本身也互不相干。`_flushed` 按**排序后的顺序**登记，与串行时完全一致 ——
        // 只有写动作并行，账本顺序不变，复现性不受影响。
        WriteBatch(_pendingText.Keys, rel =>
        {
            string full = ToOutputPath(DisplayPath(rel));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, _pendingText[rel]);
        });

        // 待落的 JSON：**到这里才序列化**（中途被读过就复用那份）。
        // 没被读过的直接流式写到文件，不再先 ToString 成几十 MB 的字符串再 UTF-8 编码。
        WriteBatch(_pendingJson.Keys, rel =>
        {
            string full = ToOutputPath(DisplayPath(rel));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (_pendingJsonText.TryGetValue(rel, out string? cached))
            {
                File.WriteAllText(full, cached);
                return;
            }

            JsonFile.Write(full, _pendingJson[rel]);
        });

        WriteBatch(_pendingBytes.Keys, rel =>
        {
            string full = ToOutputPath(DisplayPath(rel));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, _pendingBytes[rel]);
        });

        WriteBatch(_pendingCopy.Keys, rel =>
        {
            string full = ToOutputPath(DisplayPath(rel));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.Copy(_pendingCopy[rel], full, overwrite: true);
        });

        // 大二进制在 StageBytes 时已经写到同卷临时文件；这里只做 Move，不再从内存写。
        if (_pendingMove.Count > 0)
        {
            string[] moves = _pendingMove.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            foreach (string rel in moves)
            {
                string full = ToOutputPath(DisplayPath(rel));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.Move(_pendingMove[rel], full, overwrite: true);
            }
            _flushed.AddRange(moves);
            _pendingMove.Clear();
        }

        SharedTreeIndex.AddFiles(OutputRoot, _flushed);

        // ★ 落盘完成后，最大的暂存内容（tbin/png 字节）不再需要。原来这些引用一直挂到
        //   `OutputTree` 被回收：一个包的 staged 字节实测单个数组就有 33 MB、总量近 1 GB，
        //   会在自检阶段继续活着，把 LOH 顶住、逼出更多 Gen2。
        //   文本/JSON 暂存保留：`ReadTextOrNull` 的既有契约允许 Flush 后继续读到本轮内容
        //   （`CapeOrbBridge` 的补扫测试就依赖它）。字节没有同样的读取契约。
        foreach (byte[] bytes in _pendingBytes.Values) ByteBufferPool.Return(bytes);
        _pendingBytes.Clear();
        _pendingCopy.Clear();
        if (_stageDir != null)
        {
            try { if (Directory.Exists(_stageDir)) Directory.Delete(_stageDir, recursive: true); }
            catch { /* 临时目录下次再清 */ }
            _stageDir = null;
        }
    }

    /// <summary>按字典序把一批互不相干的目标**并行**写出去，账本按同样顺序登记。</summary>
    private void WriteBatch(IEnumerable<string> keys, Action<string> write)
    {
        string[] rels = keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (rels.Length == 0) return;

        try
        {
            System.Threading.Tasks.Parallel.ForEach(rels, write);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            // 并行写失败要还原成**单条**异常的形态：调用方只看得到第一处失败的具体原因
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }

        _flushed.AddRange(rels);
    }

    /// <summary>
    /// <see cref="WriteText"/> 的<b>不抛异常</b>版本：路径非法或写入失败时返回 <c>false</c> 并给出原因。
    /// <para>
    /// 单条补丁的写入一律走这里 —— 一条坏路径只该让<b>那一条</b>失败，不该把整次转换带走。
    /// </para>
    /// </summary>
    public bool TryWriteText(string relativePath, string content, out string? error)
    {
        // 先做静态校验：坏路径的缘由稳定可读，且完全不依赖文件系统 API 的报错措辞
        string? invalid = ValidatePath(relativePath);
        if (invalid != null)
        {
            error = $"输出路径非法：{invalid}";
            return false;
        }

        try
        {
            WriteText(relativePath, content);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 读产出树里的文本。**延迟写模式下先看 pending** —— 同一次运行里，补丁 B 必须能读到
    /// 补丁 A 刚写的内容（旧行为是立即落盘，所以天然能读到）。
    /// <para>
    /// ⚠ 与之相对，<see cref="ResourceTree"/>（判定读）**只读磁盘**、看不到 pending ——
    /// 这与旧行为一致：判定不该被同一次运行里后来的写入影响。
    /// </para>
    /// </summary>
    public string? ReadTextOrNull(string relativePath)
    {
        string relKey = Normalize(relativePath);
        string full;
        // 锁只保护暂存字典；读盘放在锁外（并行的不同资产不该在磁盘 I/O 上互相排队）。
        lock (_sync)
        {
            if (_pendingText.TryGetValue(relKey, out string? pending)) return pending;

            if (_pendingLazyJson.ContainsKey(relKey)) MaterializeLazyJson(relKey);

            if (_pendingLazyText.ContainsKey(relKey))
            {
                MaterializeLazyText(relKey);
                return _pendingText[relKey];
            }

            if (_pendingJson.TryGetValue(relKey, out JToken? token))
            {
                // 懒序列化一次，结果留着给 Flush 复用
                if (!_pendingJsonText.TryGetValue(relKey, out string? text))
                    _pendingJsonText[relKey] = text = token.ToString(Formatting.Indented);
                return text;
            }

            full = ToOutputPath(relativePath);
        }

        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    /// <summary>
    /// 读 JSON；优先返回内存里的对象（含懒生成的变体），**不经过字符串序列化**。
    /// 给「同一份大文档被几十条补丁反复读写」的路径用。
    /// </summary>
    public JToken? ReadJsonOrNull(string relativePath)
    {
        string rel = Normalize(relativePath);
        lock (_sync)
        {
            if (_pendingLazyJson.ContainsKey(rel)) MaterializeLazyJson(rel);
            if (_pendingJson.TryGetValue(rel, out JToken? token)) return token;
        }

        string? text = ReadTextOrNull(rel);
        if (text == null) return null;
        try { return JToken.Parse(text); }
        catch { return null; }
    }

    public bool Exists(string relativePath)
    {
        string full;
        lock (_sync)
        {
            string rel = Normalize(relativePath);
            if (_pendingText.ContainsKey(rel) || _pendingBytes.ContainsKey(rel) || _pendingCopy.ContainsKey(rel)
                || _pendingMove.ContainsKey(rel)
                || _pendingJson.ContainsKey(rel) || _pendingLazyText.ContainsKey(rel)
                || _pendingLazyJson.ContainsKey(rel))
                return true;
            full = ToOutputPath(relativePath);
        }

        return File.Exists(full);
    }

    /// <summary>
    /// 产出树里这个路径**当前**的可读来源（绝对路径）：暂存拷贝的**源文件**优先，其次磁盘。
    /// 都不在则 <c>null</c>。
    /// <para>
    /// 给只能用「路径」读的调用方用（<c>Png.Read</c> / <c>TBin10Reader.FormFile</c> 这类）。
    /// 直接拿 <see cref="ToOutputPath"/> 去读是**错的** —— 延迟写模式下磁盘上还是旧内容。
    /// </para>
    /// <para>
    /// ⚠ 只认暂存的**拷贝**。暂存的文本/二进制是从内存里生成的，磁盘上没有对应文件，
    /// 那种情况请走 <see cref="ReadTextOrNull"/>。
    /// </para>
    /// </summary>
    public string? ResolveReadablePath(string relativePath)
    {
        string rel = Normalize(relativePath);
        lock (_sync)
        {
            if (_pendingCopy.TryGetValue(rel, out string? source))
                return source;
            if (_pendingMove.TryGetValue(rel, out string? spilled))
                return spilled;
        }

        string full = ToOutputPath(rel);
        return File.Exists(full) ? full : null;
    }

    /// <summary>
    /// 读产出树里的二进制。**延迟写模式下先看暂存**（二进制 / 拷贝的源文件 / 文本按 UTF-8 编码），
    /// 再看磁盘。都不是则 <c>null</c>。
    /// <para>
    /// 给「必须在内存里拿到字节」的调用方用（地图 tbin 的解码）。与
    /// <see cref="ResolveReadablePath"/> 的区别：那个只给**路径**、认不了暂存的字节。
    /// </para>
    /// </summary>
    public byte[]? ReadBytesOrNull(string relativePath)
    {
        string rel = Normalize(relativePath);
        lock (_sync)
        {
            if (_pendingBytes.TryGetValue(rel, out byte[]? bytes)) return bytes;
            if (_pendingMove.TryGetValue(rel, out string? spilled)) return File.ReadAllBytes(spilled);
            if (_pendingCopy.TryGetValue(rel, out string? source)) return File.ReadAllBytes(source);
            if (_pendingText.TryGetValue(rel, out string? text)) return System.Text.Encoding.UTF8.GetBytes(text);
            if (_pendingJson.ContainsKey(rel) || _pendingLazyText.ContainsKey(rel)
                || _pendingLazyJson.ContainsKey(rel))
                return System.Text.Encoding.UTF8.GetBytes(ReadTextOrNull(rel)!);
        }

        string full = ToOutputPath(rel);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    /// <summary>
    /// 输入树里有这个文件吗。**O(1)**。
    /// <para>
    /// ⚠ 别写成 <c>InputFiles.Contains(rel, StringComparer.OrdinalIgnoreCase)</c> ——
    /// <see cref="InputFiles"/> 是<b>有序列表</b>，那个重载是<b>线性扫描</b>。
    /// 实测教训：<c>LoadApplier.FindMapConfig</c> 外层遍历 <see cref="InputFiles"/>、内层又
    /// <c>Contains</c> 一次，13506 条 × 数千个 config 的 O(N²) 把 SVE 的 Load 阶段拖到 49 秒。
    /// </para>
    /// </summary>
    public bool HasInputFile(string relativePath) =>
        (_inputFileSet ??= new HashSet<string>(InputFiles, StringComparer.OrdinalIgnoreCase))
        .Contains(Normalize(relativePath));

    private HashSet<string>? _inputFileSet;

    /// <summary>
    /// 按**祖先目录**索引的 <c>.config</c> 路径（键是不带尾斜杠的目录，根级用空串；顺序同
    /// <see cref="InputFiles"/>）。
    /// <para>
    /// <c>AssetCreator</c> 每给一个新资产选一次模板，就要按前缀把 <see cref="InputFiles"/>
    /// 扫一遍 —— 而且「自下而上的每一级祖先目录」各扫一遍。实测 SVE 新建上千个资产，
    /// 这一项就是纯浪费。建成索引后每次只扫**命中那一档**。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ConfigsUnder(string dir)
    {
        using var _gate = Enter();
        if (_configsByDir == null)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in InputFiles)
            {
                if (!file.EndsWith(".config", StringComparison.OrdinalIgnoreCase)) continue;

                map.TryAdd("", new List<string>());
                // 根级那份收下所有 .config（`StartsWith("")` 就是全部）
                map[""].Add(file);

                int slash = file.IndexOf('/');
                while (slash >= 0)
                {
                    string ancestor = file[..slash];
                    map.TryAdd(ancestor, new List<string>());
                    map[ancestor].Add(file);
                    slash = file.IndexOf('/', slash + 1);
                }
            }

            _configsByDir = map;
        }

        string key = dir.Replace('\\', '/').Trim('/');
        return _configsByDir.TryGetValue(key, out List<string>? hit) ? hit : Array.Empty<string>();
    }

    private Dictionary<string, List<string>>? _configsByDir;

    /// <summary>目录前缀 → 该前缀下第一份图片 `.config`（含 null 结果）。InputFiles 固定时结果固定。</summary>
    private readonly Dictionary<string, string?> _imageConfigByPrefix =
        new(StringComparer.OrdinalIgnoreCase);

    public string? ImageConfigUnder(string prefix, Func<string?> find)
    {
        using var _gate = Enter();
        string key = prefix.Replace('\\', '/').Trim('/');
        if (_imageConfigByPrefix.TryGetValue(key, out string? hit)) return hit;
        string? found = find();
        _imageConfigByPrefix[key] = found;
        return found;
    }

    /// <summary>
    /// 这条路径在暂存里**首次出现时的写法**（保留大小写）；没有暂存则 <c>null</c>。
    /// <para>
    /// 给「接管落盘权」的缓存层用：Windows 上往**已存在**的文件里写不会改名字，
    /// 所以磁盘目录项的大小写由**第一次创建它的那次写**决定。本次运行里刚 <c>Load</c> 进来、
    /// 还没落盘的文件，暂存里那份就是它的「第一次写」—— 落盘的名字得跟它走。
    /// </para>
    /// </summary>
    public string? PendingName(string relativePath)
    {
        lock (_sync)
            return _displayPath.TryGetValue(Normalize(relativePath), out string? first) ? first : null;
    }

    /// <summary>
    /// 撤掉这个路径上的暂存（三种暂存都撤）。给「接管了某个产出文件落盘权」的缓存层用
    /// （<see cref="ImageCache"/> / <see cref="Maps.MapCache"/>）：它们会自己写，
    /// 产出树再拷一遍会把手改的结果盖回去。
    /// </summary>
    public void DropPending(string relativePath)
    {
        using var _gate = Enter();
        string rel = Normalize(relativePath);
        _pendingText.Remove(rel);
        RemovePendingBytes(rel);
        _pendingCopy.Remove(rel);
        RemovePendingMove(rel);
        _pendingJson.Remove(rel);
        _pendingJsonText.Remove(rel);
        _pendingLazyText.Remove(rel);
        _pendingLazyJson.Remove(rel);
        _displayPath.Remove(rel);   // 落盘权交给内存模型了，名字由它们自己记
        InvalidateConfigFacts(rel);
        _planned.Remove(rel);   // 产出树不再写它了（落盘权已交给内存模型）
    }

    /// <summary>
    /// 把新文件加入产出树，并记录到 InputFiles 之外的清单。
    /// <para>并行写时会被多个线程同时 <c>Add</c>/<c>Contains</c>，所以内部自带锁。</para>
    /// </summary>
    public CreatedFileSet CreatedFiles { get; } = new();

    /// <summary>线程安全的「本次新建的文件」集合；接口保持 Add/Contains/Count/枚举。</summary>
    public sealed class CreatedFileSet : IEnumerable<string>
    {
        private readonly HashSet<string> _set = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public void Add(string relativePath) { lock (_lock) _set.Add(relativePath); }
        public bool Contains(string relativePath) { lock (_lock) return _set.Contains(relativePath); }
        public int Count { get { lock (_lock) return _set.Count; } }

        public IEnumerator<string> GetEnumerator()
        {
            lock (_lock) return _set.ToList().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
