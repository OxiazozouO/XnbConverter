using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 对一个 <c>When</c> 对象的求值判定。<c>Expand=null</c> = "判不了"（既不是真也不是假），
/// 绝不能退化成默认展开或默认跳过 —— 由调用方报失败。
/// </summary>
public sealed record IncludeDecision(bool? Expand, string? Reason = null)
{
    public static IncludeDecision Yes(string? reason = null) => new(true, reason);
    public static IncludeDecision No(string? reason = null) => new(false, reason);
    public static IncludeDecision Unknown(string reason) => new(null, reason);
}

/// <summary>带 <c>When</c> 的 Include 的求值结局。</summary>
public enum IncludeOutcome
{
    /// <summary>条件为真，已展开。</summary>
    Expanded,

    /// <summary>条件为假，该 Include 不适用（跳过，不是失败）。</summary>
    Skipped,

    /// <summary>条件无法求值 —— 未展开且报失败（既不能默认展开也不能默认跳过）。</summary>
    Unresolved,

    /// <summary>
    /// 条件**判不了但不丢**：把它与子补丁自己的 <c>When</c> 相与后内联。
    /// <para>
    /// 依据：CP 的机制是「条件成立才**加载** children」（<c>IncludePatch.TryApply</c> 只在
    /// <c>IsReady</c> 时跑），**效果**等价于「children 各自带上那个条件」。
    /// 直接丢掉会白丢内容 —— 而那些条件里有可译的（`HasWalletItem` 就有原版 GSQ：
    /// 钱包物品那个 power 的 `UnlockedCondition`），带上它反而能在游戏里**按时生效**。
    /// </para>
    /// </summary>
    Inlined
}

/// <summary>一个带 <c>When</c> 的 Include 的求值结果，供报告说明。</summary>
public sealed class IncludeResolution
{
    public string SourceFile = "";
    public int Index;
    public string? LogName;
    public string? FromFile;
    public JObject? When;
    public IncludeOutcome Outcome;
    public string? Reason;

    public string DisplayName =>
        LogName ?? FromFile ?? $"{Path.GetFileName(SourceFile)}#{Index}";
}

/// <summary>一个 CP 内容包：content.json 加所有 Include 展开后的 patch 列表。</summary>
public sealed class ContentPack
{
    public string RootPath { get; private init; } = "";
    public string Name { get; private set; } = "";
    public string UniqueId { get; private set; } = "";
    public List<ChangePatch> Patches { get; } = new();

    /// <summary>manifest 里声明的依赖 UniqueID（required + optional）。</summary>
    public List<string> DependencyIds { get; } = new();

    /// <summary>包根 <c>content.json</c> 的 <c>Format</c>（语义化版本）。用来判「现代格式」的那几条规矩。</summary>
    private Version? _formatVersion;

    /// <summary>这个包是不是**非 CP 的框架包**（FTM）—— 它的补丁是**翻译器合成**的，不是磁盘上的 `Changes`。</summary>
    public bool IsFtmPack { get; private set; }

    /// <summary>包声明的 `Format`（读不到就是 <c>null</c>；FTM 包的那份是翻译器**硬写的 `1.28.0`**）。</summary>
    public Version? FormatVersion => _formatVersion;

    /// <summary>
    /// **CP 的迁移器该按哪个 `Format` 跑**：CP 只管自己的包，FTM 包它压根不处理；而 FTM 那批补丁
    /// 是**我们自己**按 1.6 的形状合成的（`Data/Locations` 的 `Forage`/`ArtifactSpots`），
    /// 套"旧格式迁移"是**认错了人** —— 原文里就有这个坑：翻译器硬写的 `1.28.0` 早于 `2.0.0`，
    /// 于是 26 条 FTM 觅食被 `LegacyFormatMigration` 判成"要迁移"而不写（实测丢过 Data/Locations 的内容）。
    /// ⇒ 判定侧一律用这个属性，别直接用 <see cref="FormatVersion"/>。
    /// </summary>
    public Version? FormatForCpMigration => IsFtmPack ? null : _formatVersion;
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// **非 CP 框架包**（FTM）翻译时的说明：哪些落了、哪些落不了、哪些是有损的。
    /// 空 = 这个包不是框架包（或没什么要说的）。CLI 要把它们打进输出/报告 —— 用户得知道
    /// "哪些内容在这个方案里拿不到"（见 <c>FtmPackTranslator</c> 的注释）。
    /// </summary>
    public IReadOnlyList<string> FtmNotes { get; internal set; } = Array.Empty<string>();

    /// <summary>带 <c>When</c> 的 Include 的求值结果；<see cref="ResolveIncludes"/> 后填充。</summary>
    public List<IncludeResolution> IncludeResolutions { get; } = new();

    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>包根 `ConfigSchema` + `config.json`，供 Include 路径代换（懒加载一次）。</summary>
    private ConfigSchemaTokens? _rootConfigSchema;

    /// <summary>Include 的 `FromFile` 里动态令牌的展开器（`ResolveIncludes` 时由调用方接入）。</summary>
    private Func<string, string?>? _expandIncludeFromFile;

    /// <summary>是否把“路径含令牌”的 Include 也留到 `ResolveIncludes`（CLI 走这条）。</summary>
    private bool _deferIncludes;

    /// <summary>按原始顺序记录的单元：具体补丁，或待求值的条件化 Include。</summary>
    private List<Unit> _units = new();

    /// <summary>
    /// 读内容包。<paramref name="deferConditionalIncludes"/> 为 <c>true</c> 时，带 <c>When</c> 的
    /// Include <b>不在此时展开</b>，而是留待调用方拿到模组索引后调用 <see cref="ResolveIncludes"/> 决定；
    /// 为 <c>false</c>（缺省）时保持旧行为，无条件展开所有 Include。
    /// </summary>
    public static ContentPack Load(string packDirectory, bool deferConditionalIncludes = false,
        Func<string, bool?>? hasMod = null)
    {
        string contentJson = Path.Combine(packDirectory, "content.json");

        // ★ **非 CP 的框架包**：FTM 的 `content.json` 是另一套格式。先把它翻成 CP 形状的补丁集
        //   （觅食/埋藏物 → `Data/Locations`），后面**整条流水线原样复用** —— 判定、写入、报告
        //   都不必知道 FTM 的存在。换不到的段落由翻译器如实报进 `FtmNotes`。
        FtmImportResult? ftm = FtmPackTranslator.TryImport(packDirectory, hasMod);

        if (ftm == null && !File.Exists(contentJson))
            throw new FileNotFoundException("内容包根目录缺少 content.json", contentJson);

        var pack = new ContentPack
        {
            RootPath = Path.GetFullPath(packDirectory),
            _deferIncludes = deferConditionalIncludes,
        };
        pack.ReadManifest(packDirectory);
        if (ftm != null)
        {
            pack.IsFtmPack = true;
            pack.FtmNotes = ftm.Notes;
            pack._formatVersion = Version.TryParse(ftm.Content["Format"]?.Value<string>(), out Version? v) ? v : null;
            pack._units = pack.CollectUnits(contentJson, ftm.Content);
        }
        else
        {
            pack._formatVersion = ReadFormatVersion(contentJson);
            pack._units = pack.CollectUnits(contentJson);
        }

        if (deferConditionalIncludes)
            pack.Flatten(pack._units, evaluate: null, record: false);   // 先只落无条件补丁
        else
            pack.Flatten(pack._units, _ => IncludeDecision.Yes(), record: false);

        // 补丁字段守卫：出现我们不认识的键就报出来 —— **别静默按"它不存在"处理**。
        // 依据是 FTM 那次的教训（漏读了 `SpawnEnabled`/`File_Conditions` 两个字段，语料里恰好不触发）。
        PatchFieldGuard.Check(pack.Patches, pack.Warnings);

        // ★ **令牌的版本门槛**：声明旧 `Format` 的包用了它那个版本还没有的令牌 ⇒ CP 会把**整份
        //   content.json 拒掉**（`BaseMigration.cs:32-41`）。我们照转就是"作者那边根本不生效的内容"。
        //   包级警告（CP 的失败也是整份文件级的）。语料实测 0 条命中 —— 正面验证：`{{ModId}}`
        //   的使用者**全部**声明 `Format ≥ 2.0`（`ModId` 正是 `Migration_2_0` 的新增令牌）。
        if (!pack.IsFtmPack)
        {
            foreach (string warning in LegacyFormatMigration.FindForbiddenTokens(
                         JsoncReader.ParseFile(Path.Combine(pack.RootPath, "content.json")), pack._formatVersion))
            {
                pack.Warnings.Add(
                    $"令牌版本门槛：content.json 用了 {warning} —— CP 会**拒收整份文件**"
                    + "（`BaseMigration.cs:32-41`）；我们照转出来的内容在 CP 那边根本不生效，"
                    + "请当成「作者那边也是坏的」看待");
            }
        }

        return pack;
    }

    /// <summary>
    /// 用给定求值器决定每个条件化 Include 是否展开。会重建 <see cref="Patches"/>（保持原始顺序），
    /// 并把每个 Include 的结局记进 <see cref="IncludeResolutions"/>。
    /// <para>
    /// 求值为真 → 展开；为假 → 跳过；<see cref="IncludeDecision.Expand"/>=<c>null</c> → <b>不展开并记为
    /// <see cref="IncludeOutcome.Unresolved"/></b>，绝不擅自猜真或猜假。
    /// </para>
    /// </summary>
    public IReadOnlyList<IncludeResolution> ResolveIncludes(Func<JObject, IncludeDecision> evaluate,
        Func<string, string?>? expandFromFile = null)
    {
        _expandIncludeFromFile = expandFromFile;
        Patches.Clear();
        IncludeResolutions.Clear();
        Flatten(_units, evaluate, record: true);
        return IncludeResolutions;
    }

    /// <summary>
    /// 把 <c>content.json</c> **根上**的 <c>CustomLocations</c> 展开成普通补丁。
    /// <para>
    /// <b>CP 的语义</b>（<c>ContentPatcher.Framework.Locations/CustomLocationManager.cs</c>）：
    /// <list type="number">
    /// <item>游戏按需加载 <c>Maps/&lt;Name&gt;</c> 时，内容取自包内的 <c>FromMapFile</c>
    ///   （<c>:184</c> 的 <c>e.LoadFrom(… ModContent.Load&lt;Map&gt;(FromMapFile) …)</c>，加载优先级取<b>最大</b>）
    ///   —— 等价于一条 <c>Load</c> 补丁；</item>
    /// <item>并向 <c>Data/Locations</c> 补一条
    ///   <c>{ "CreateOnLoad": { "MapPath": "Maps/&lt;Name&gt;" } }</c>（<c>:140-172</c>，编辑优先级 <c>-1000</c>）。</item>
    /// </list>
    /// <b>两条缺一不可</b>：只放地图，游戏不会创建这个地点；只登记，地图取不到。
    /// </para>
    /// <para>
    /// ⚠ <b>名字撞车必须跳过</b>：CP 在 <c>data.ContainsKey(name)</c> 时**只记警告、不覆盖**
    /// （<c>:152-160</c>）。照 <c>EditData</c> 的常规语义写下去会把原版那条地点数据**整个替换掉** ——
    /// 所以这里用 <paramref name="locationExists"/> 先问一句（由调用方拿资源树回答）。
    /// </para>
    /// </summary>
    /// <returns>真正展开的地点数。</returns>
    public int ExpandCustomLocations(Func<string, bool>? locationExists = null)
    {
        string contentJson = Path.Combine(RootPath, "content.json");
        if (!File.Exists(contentJson)) return 0;

        JObject root;
        try { root = JsoncReader.ParseFile(contentJson); }
        catch { return 0; }
        if (root["CustomLocations"] is not JArray locations) return 0;

        var entries = new JObject();
        int added = 0;
        foreach (JToken token in locations)
        {
            if (token is not JObject loc) continue;
            string? name = loc["Name"]?.Value<string>();
            string? fromMapFile = loc["FromMapFile"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(fromMapFile)) continue;

            if (locationExists != null && locationExists(name!))
            {
                Warnings.Add($"CustomLocations：`{name}` 已存在于 Data/Locations，跳过（CP 同此）");
                continue;
            }

            string mapPath = "Maps/" + name;

            var loadRaw = new JObject
            {
                ["Action"] = "Load",
                ["Target"] = mapPath,
                ["FromFile"] = fromMapFile,
                ["LogName"] = $"CustomLocations：{name}",
            };
            Patches.Add(new ChangePatch
            {
                SourceFile = "content.json (CustomLocations)",
                Action = "Load",
                LogName = loadRaw["LogName"]!.Value<string>(),
                Target = mapPath,
                FromFile = fromMapFile,
                Raw = loadRaw,
                Priority = int.MaxValue,      // `AssetLoadPriority.Exclusive`，与 CP 一致
            });

            var entry = new JObject { ["CreateOnLoad"] = new JObject { ["MapPath"] = mapPath } };
            if (loc["MigrateLegacyNames"] is JArray legacy) entry["FormerLocationNames"] = legacy.DeepClone();
            entries[name!] = entry;
            added++;
        }

        if (entries.Count > 0)
        {
            var dataRaw = new JObject
            {
                ["Action"] = "EditData",
                ["Target"] = "Data/Locations",
                ["Entries"] = entries,
                ["LogName"] = "CustomLocations 登记地点",
            };
            Patches.Add(new ChangePatch
            {
                SourceFile = "content.json (CustomLocations)",
                Action = "EditData",
                LogName = dataRaw["LogName"]!.Value<string>(),
                Target = "Data/Locations",
                Raw = dataRaw,
                Priority = -1000,             // 与 CP 的 `(AssetEditPriority)(-1000)` 一致
            });
        }

        return added;
    }

    /// <summary>
    /// 这条补丁是否被 <c>Enabled</c> 关掉。
    /// <para>
    /// 依据 CP <c>PatchLoader.cs:359-367</c>：
    /// <list type="bullet">
    /// <item><c>Format</c> <b>不早于</b> <c>1.25.0</c> 时，<c>Enabled</c> 只要不是能解析成
    ///   <c>true</c> 的字面量，整条补丁就被跳过（原文案：该字段已废弃，应当删除）；</item>
    /// <item>更早的格式走 <c>TryParseEnabled</c>（值可含令牌）—— 我们只认字面 <c>false</c>
    ///   （值含令牌时保持原样，与「不猜」一致）。</item>
    /// </list>
    /// </para>
    /// </summary>
    private bool IsDisabled(JObject raw, string action)
    {
        // `Include` 的 `Enabled` 由 CP 在更早的一步处理；这里只管普通补丁。
        if (action.Equals("Include", StringComparison.OrdinalIgnoreCase)) return false;

        string? enabled = raw["Enabled"]?.Value<string>();
        if (enabled == null) return false;

        bool literalTrue = bool.TryParse(enabled, out bool parsed) && parsed;
        if (literalTrue) return false;                       // 字面 `true` ⇒ 照常

        bool modern = _formatVersion == null || _formatVersion >= new Version(1, 25);
        if (modern) return true;                              // 现代格式：不是 `true` 一律跳过

        return bool.TryParse(enabled, out bool parsed2) && !parsed2;   // 旧格式：只认字面 `false`
    }

    /// <summary>读包根 <c>content.json</c> 的 <c>Format</c>（失败返回 <c>null</c>）。</summary>
    private static Version? ReadFormatVersion(string contentJson)
    {
        try
        {
            JObject root = JsoncReader.ParseFile(contentJson);
            string? text = root["Format"]?.Value<string>();
            return text != null && Version.TryParse(text, out Version? v) ? v : null;
        }
        catch { return null; }
    }

    private void ReadManifest(string packDirectory)
    {
        string manifest = Path.Combine(packDirectory, "manifest.json");
        if (!File.Exists(manifest)) return;
        try
        {
            JObject obj = JsoncReader.ParseFile(manifest);
            Name = obj["Name"]?.Value<string>() ?? "";
            UniqueId = obj["UniqueID"]?.Value<string>() ?? "";
            foreach (JToken dep in obj["Dependencies"] as JArray ?? new JArray())
            {
                string? id = dep["UniqueID"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(id)) DependencyIds.Add(id);
            }
        }
        catch (Exception ex)
        {
            Warnings.Add($"manifest.json 解析失败：{ex.Message}");
        }
    }

    // ---------------------------------------------------------------- 收集

    private abstract class Unit { }
    private sealed class PatchUnit : Unit { public ChangePatch Patch = null!; }
    private sealed class IncludeUnit : Unit
    {
        public JObject Raw = null!;
        public string SourceFile = "";
        public int Index;
        public JObject When = null!;
    }

    /// <summary>
    /// 把 Include 自己的 <c>When</c> 里的 <c>{{FromFile}}</c> 换成该 Include 的 <c>FromFile</c> 值。
    /// <para>
    /// CP 的约定：Include 的 <c>When</c> 里 <c>{{FromFile}}</c> 指的是**这条 Include 自己**的源文件，
    /// 惯用写法就是 <c>"HasFile: \"{{FromFile}}\""</c>（条件化的可选内容）。
    /// <b>不替换的话 <c>HasFile</c> 会拿字面量 <c>"{{FromFile}}"</c> 去 <c>File.Exists</c></b>，
    /// 恒为假 → 整条 Include 被判「条件为假」跳过。
    /// </para>
    /// <para>
    /// 实测代价：SVE 的「季节性服装」子包 21 条 Include **一条都没展开**，
    /// 整个包 0 条补丁进流水线（那 21 个被 Include 的文件里共 87 条含配置令牌的补丁）。
    /// </para>
    /// <para>键与字符串值都替换；嵌套对象里的也替换（<c>When</c> 允许多层）。</para>
    /// </summary>
    private static JObject ResolveFromFileToken(IncludeUnit include)
    {
        string fromFile = include.Raw["FromFile"]?.Value<string>() ?? "";
        var clone = (JObject)include.When.DeepClone();
        Substitute(clone, fromFile);

        static void Substitute(JToken token, string fromFile)
        {
            switch (token)
            {
                case JObject o:
                    // 键也要换：`HasFile:{{FromFile}}` 的占位符在**键**上。
                    foreach (JProperty p in o.Properties().ToList())
                    {
                        if (p.Name.Contains("{{FromFile}}", StringComparison.Ordinal))
                        {
                            string name = p.Name.Replace("{{FromFile}}", fromFile, StringComparison.Ordinal);
                            JToken value = p.Value;
                            p.Remove();
                            o[name] = value;
                        }
                        else
                        {
                            Substitute(p.Value, fromFile);
                        }
                    }
                    break;
                case JArray a:
                    foreach (JToken item in a) Substitute(item, fromFile);
                    break;
                case JValue { Type: JTokenType.String } v:
                    string text = v.Value<string>() ?? "";
                    if (text.Contains("{{FromFile}}", StringComparison.Ordinal))
                        v.Value = text.Replace("{{FromFile}}", fromFile, StringComparison.Ordinal);
                    break;
            }
        }

        return clone;
    }

    /// <summary>
    /// 读一个 content.json（或 Include 目标），把内容<b>按原始顺序</b>拆成单元列表。
    /// 无 <c>When</c> 的 Include 立即递归展开；带 <c>When</c> 的记成 <see cref="IncludeUnit"/>，延迟求值。
    /// </summary>
    /// <summary>
    /// 把 <c>Target</c>/<c>FromFile</c> 里的 <c>{{ModId}}</c> 换成**本包的 UniqueID**。
    /// <para>
    /// <b>为什么必须先换</b>：它是 CP 注册的 <c>ImmutableValueProvider</c>（值就是包的 UniqueID），
    /// **静态可求值**；而 <c>Target</c> 里只要还剩 <c>{{</c>，整条补丁就被判「资产名含令牌」而拒。
    /// 官方文档推荐的正是这个写法（给自定义资产加命名空间），实测语料里 8 个包共 **1037 处**。
    /// </para>
    /// <para>
    /// 值侧的 <c>{{ModId}}</c> 由 <see cref="ValueTokenSubstitutor"/> 展开（那边早就支持了），
    /// 这里只补 <b>「名字」字段</b>这一处 —— 两个地方问的是同一件事，取值也必须是同一个。
    /// </para>
    /// </summary>
    private string? ExpandModId(string? text) =>
        string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal)
            ? text
            : text.Replace("{{ModId}}", UniqueId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 目标资产名**结尾的 <c>.xnb</c> 要剥掉** —— CP 就是这么做的：
    /// <c>PatchLoader.ParseAssetName</c>（<c>PatchLoader.cs:151-158</c>）拿到原始名先
    /// <c>EndsWith(".xnb", OrdinalIgnoreCase)</c>，是就砍掉最后 4 个字符再解析。
    /// <para>
    /// <b>不剥的代价</b>（实测语料 8 条，包 01「佳能友好对话扩展」的按年份节日对话）：
    /// <c>"Target": "Data/Festivals/spring13.xnb"</c> 在资源树里**查不到**（树里是
    /// <c>spring13.json</c>）⇒ 被判成「**新建**资产 <c>Data/Festivals/spring13.xnb</c>」，
    /// 报告里说的落点、理由全是对一个**并不存在的资产**说的。CP 那边命中的是**既有**资产。
    /// </para>
    /// <para>
    /// ⚠ 只对 <c>Target</c>（资产名）做，<c>FromFile</c>（文件路径）**不能碰** —— 那是真路径。
    /// 逗号分隔多值要**逐段**剥（CP 是先 <c>SplitPatches</c> 再逐条 <c>ParseAssetName</c>）。
    /// </para>
    /// </summary>
    private static string? StripXnbSuffix(string? target)
    {
        if (string.IsNullOrEmpty(target) || !target.Contains(".xnb", StringComparison.OrdinalIgnoreCase))
            return target;

        IReadOnlyList<string> parts = AssetNameList.Split(target);
        if (parts.Count == 0) return target;
        return string.Join(',', parts.Select(p =>
            p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p));
    }

    private List<Unit> CollectUnits(string filePath, JObject? preParsedRoot = null)
    {
        var units = new List<Unit>();
        string full = Path.GetFullPath(filePath);
        if (!_visited.Add(full)) return units;  // 已在处理中或处理过 → 防环

        JObject root;
        try
        {
            // FTM 包：根节点是**现场翻出来的**那份，不是磁盘上的 content.json（那是另一套格式）
            root = preParsedRoot ?? JsoncReader.ParseFile(full);
        }
        catch (Exception ex)
        {
            Warnings.Add($"解析失败 {full}：{ex.Message}");
            return units;
        }

        if (root["Changes"] is not JArray changes) return units;

        for (int i = 0; i < changes.Count; i++)
        {
            if (changes[i] is not JObject raw) continue;

            // ⚠ **字段名先规范化**：CP 是把补丁反序列化进 `PatchConfig` 的（JSON.NET 大小写不敏感），
            //   所以 `"when"` / `"Patchmode"` 在 CP 那边照常生效。我们按名字直取 —— 不规范化就会
            //   把它们当成"没写"（实测语料里有：`Patchmode: "Overlay"` 会被当成默认的 `Replace`）。
            PatchFieldGuard.Normalize(raw);

            string action = raw["Action"]?.Value<string>() ?? "";

            // CP 的 `Enabled`：现代格式（≥ 1.25.0）里它**已废弃**，只要不是字面 `true`，
            // CP 就把这条补丁**整条跳过**（`PatchLoader.cs:359-364`，理由是
            // "the Enabled field is obsolete and should be removed"；`warn: false`，连警告都不打）。
            // 不读它的后果是**把作者禁用的补丁写进产物**。
            if (IsDisabled(raw, action))
            {
                Warnings.Add($"{Path.GetFileName(full)}#{i}：`Enabled` 不是 `true`（现代格式里该字段已废弃），"
                             + "按 CP 的口径整条跳过");
                continue;
            }

            if (string.Equals(action, "Include", StringComparison.OrdinalIgnoreCase))
            {
                JObject? when = raw["When"] as JObject;
                // 路径里含令牌（配置/动态）⇒ 也留到 `ResolveIncludes`：Load 阶段还没有
                // 动态令牌求值器，先展开只会报“目标不存在”并把这条 Include 丢掉。
                // 空 `When` 让它在 `ResolveIncludes` 里按“无条件”求值。
                bool tokenPath = (raw["FromFile"]?.Value<string>() ?? "")
                    .Contains("{{", StringComparison.Ordinal);
                if ((when != null && when.HasValues) || (tokenPath && _deferIncludes))
                {
                    units.Add(new IncludeUnit
                    {
                        Raw = raw, SourceFile = full, Index = i,
                        When = when != null && when.HasValues ? when : new JObject(),
                    });
                }
                else
                {
                    units.AddRange(ExpandIncludeUnits(raw, full));
                }
                continue;
            }

            units.Add(new PatchUnit
            {
                Patch = new ChangePatch
                {
                    SourceFile = full,
                    Index = i,
                    Action = action,
                    LogName = raw["LogName"]?.Value<string>(),
                    Target = StripXnbSuffix(ExpandModId(raw["Target"]?.Value<string>())),
                    FromFile = ExpandModId(raw["FromFile"]?.Value<string>()),
                    Update = raw["Update"]?.Value<string>(),
                    When = raw["When"] as JObject,
                    Raw = raw,
                    Priority = ParsePriority(raw["Priority"]?.Value<string>(), action, full)
                }
            });
        }

        return units;
    }

    /// <summary>包根的配置令牌表（`ConfigSchema` + `config.json`）；读不到时为空表。</summary>
    private ConfigSchemaTokens RootConfigSchema()
    {
        if (_rootConfigSchema != null) return _rootConfigSchema;
        try
        {
            _rootConfigSchema = ConfigSchemaTokens.Load(
                JsoncReader.ParseFile(Path.Combine(RootPath, "content.json")), RootPath);
        }
        catch
        {
            _rootConfigSchema = ConfigSchemaTokens.Empty;
        }
        return _rootConfigSchema;
    }

    /// <summary>展开一个（无 When 或已判真的）Include 的所有目标文件。</summary>
    private List<Unit> ExpandIncludeUnits(JObject include, string includingFile)
    {
        var units = new List<Unit>();
        string? fromFile = include["FromFile"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(fromFile))
        {
            Warnings.Add($"{includingFile}：Include 缺少 FromFile");
            return units;
        }

        // FromFile 相对**内容包根目录**，不是当前文件所在目录
        foreach (string part in AssetNameList.Split(fromFile))
        {
            // 配置令牌要在这里就代：Include 的文件列表在**解析阶段**就定下来，
            // 那时补丁级 `ConfigTokenSubstitutor.Apply` 还没跑。不代的话整个 Include 会被当成
            // "目标不存在"跳过 —— 实测 Bear Family 的 `Data/{{SeasonalOutfits}}_NPCData.json`
            // 让 Sigurd/Gunnar/Teren 三个 NPC 整条从 `Data/Characters` 消失。
            string trimmed = ConfigTokenSubstitutor.ReplaceForPath(part.Trim(), RootConfigSchema());
            if (trimmed.Length == 0) continue;

            // 路径里的**动态令牌**（实测 Rose and the Alchemist：
            // `Data/Locations/{{Animal_Shop_Owner}}_Animals.json`，值由 HasMod 决定）。
            // 第一次 Flatten（Load 时）还没有求值器，代不掉；`ResolveIncludes` 会重建
            // Patches，这次带着展开器重试。代不掉就保留原文，照旧报“目标不存在”。
            if (trimmed.Contains("{{", StringComparison.Ordinal) && _expandIncludeFromFile != null)
            {
                string? expanded = _expandIncludeFromFile(trimmed);
                if (!string.IsNullOrEmpty(expanded)) trimmed = expanded;
            }

            string target = Path.GetFullPath(Path.Combine(RootPath, trimmed));
            if (!File.Exists(target))
            {
                Warnings.Add($"Include 目标不存在：{trimmed}");
                continue;
            }
            // 必须共享同一个 visited 集合。每层新建集合会让成环的 Include 无限递归。
            units.AddRange(CollectUnits(target));
        }
        return units;
    }

    /// <summary>
    /// 把单元列表扁平化进 <see cref="Patches"/>，保持原始顺序。
    /// <paramref name="evaluate"/> 为 <c>null</c> 表示"求值器还没到"——条件化 Include 暂不展开，也不报结果。
    /// </summary>
    private void Flatten(List<Unit> units, Func<JObject, IncludeDecision>? evaluate, bool record)
    {
        foreach (Unit unit in units)
        {
            if (unit is PatchUnit patchUnit)
            {
                Patches.Add(patchUnit.Patch);
                continue;
            }

            var include = (IncludeUnit)unit;
            if (evaluate == null) continue;  // 未决，等 ResolveIncludes

            // 空 `When` = 无条件。**路径含令牌的 Include** 会被留到这里等令牌展开器，
            // 它没有条件；把空对象交给求值器会被（CLI 那只）当成“判不了”并内联，
            // 内联后的 `Load` 又因“资产创建不能有条件”全部跳过。
            IncludeDecision decision = include.When == null || !include.When.HasValues
                ? IncludeDecision.Yes("无条件")
                : evaluate(ResolveFromFileToken(include));

            if (decision.Expand == null)
            {
                // ★ **判不了 ≠ 只能丢**：把 Include 的 `When` 与子补丁自己的 `When` 相与后内联。
                //   丢掉会白丢内容，而这些条件里有可译的（`HasWalletItem` 就有原版 GSQ）。
                //   ⚠ 带不动的两档单独跳过并记原因，绝不写成无条件版本：
                //     · `Load` 是**创建资产**（有或没有），没法「有条件地有」；
                //     · 子补丁自己的 `When` 与 Include 的**键冲突**（两边同一键不同值）——
                //       相与要算集合交集，那是猜，不干。
                if (record) Record(include, IncludeOutcome.Inlined, decision.Reason);
                InlineConditional(include, ResolveFromFileToken(include), evaluate, record);
                continue;
            }

            if (!decision.Expand.Value)
            {
                if (record) Record(include, IncludeOutcome.Skipped, decision.Reason);
                continue;
            }

            if (record) Record(include, IncludeOutcome.Expanded, decision.Reason);
            // 在当前位置展开，children 里若还有条件化 Include 会被同一求值器继续处理
            Flatten(ExpandIncludeUnits(include.Raw, include.SourceFile), evaluate, record);
        }
    }

    /// <summary>
    /// 把条件化 Include 的 children 内联进来，<paramref name="condition"/> 与每个子补丁的
    /// <c>When</c> 相与。带不动的那两类单独跳过（见调用处说明）。
    /// </summary>
    private void InlineConditional(IncludeUnit include, JObject condition,
        Func<JObject, IncludeDecision> evaluate, bool record)
    {
        foreach (Unit unit in ExpandIncludeUnits(include.Raw, include.SourceFile))
        {
            if (unit is IncludeUnit nested)
            {
                // 嵌套 Include：先把父条件并进它的 When，再走同一套判定 ——
                // 它也判不了的话会继续内联，条件层层相与。
                if (MergeWhen(nested.When, condition) is string nestedConflict)
                {
                    Warnings.Add($"{Path.GetFileName(nested.SourceFile)}#{nested.Index}："
                                 + $"嵌套 Include 的 When 与父 Include 冲突（{nestedConflict}），已跳过");
                    continue;
                }
                Flatten(new List<Unit> { nested }, evaluate, record);
                continue;
            }

            var patchUnit = (PatchUnit)unit;
            if (patchUnit.Patch.IsLoad)
            {
                // `Load` 是**创建资产**：资产「有或没有」，表达不了「有条件地有」。
                Warnings.Add($"{Path.GetFileName(patchUnit.Patch.SourceFile)}#{patchUnit.Patch.Index}"
                             + $"（{patchUnit.Patch.Target}）：条件化 Include 里的 `Load` 无法带条件（资产创建不能有条件），已跳过");
                continue;
            }

            if (MergeWhen(patchUnit.Patch.Raw, condition) is string conflict)
            {
                Warnings.Add($"{Path.GetFileName(patchUnit.Patch.SourceFile)}#{patchUnit.Patch.Index}"
                             + $"（{patchUnit.Patch.Target}）：子补丁自己的 When 与 Include 的冲突（{conflict}），已跳过");
                continue;
            }
            patchUnit.Patch.When = patchUnit.Patch.Raw["When"] as JObject;
            Patches.Add(patchUnit.Patch);
        }
    }

    /// <summary>
    /// 把 <paramref name="condition"/> 相与进 <paramref name="target"/> 的 <c>When</c>。
    /// 冲突（同一键、不同值）时返回原因、**不改动** —— 算键的交集是猜，不干。
    /// </summary>
    private static string? MergeWhen(JObject target, JObject condition)
    {
        JObject? own = target["When"] as JObject;
        var merged = (JObject)condition.DeepClone();
        if (own != null)
        {
            foreach (JProperty prop in own.Properties())
            {
                if (merged[prop.Name] is JToken existing && !JToken.DeepEquals(existing, prop.Value))
                    return $"`{prop.Name}` 在两边取值不同";
                merged[prop.Name] = prop.Value.DeepClone();
            }
        }
        target["When"] = merged;
        return null;
    }

    private void Record(IncludeUnit include, IncludeOutcome outcome, string? reason)
    {
        IncludeResolutions.Add(new IncludeResolution
        {
            SourceFile = include.SourceFile,
            Index = include.Index,
            LogName = include.Raw["LogName"]?.Value<string>(),
            FromFile = include.Raw["FromFile"]?.Value<string>(),
            When = include.When,
            Outcome = outcome,
            Reason = reason
        });
    }

    // ---------------------------------------------------------------- Priority

    // CP 的 Priority 取值来自 SMAPI 的资产优先级枚举（数值经反编译 StardewModdingAPI.dll 核实）：
    //   EditData/EditImage/EditMap → AssetEditPriority: Early=-1000, Default=0, Late=1000
    //   Load                       → AssetLoadPriority: Low=-1000, Medium=0, High=1000, Exclusive=int.MaxValue
    // 另支持偏移写法，如 "Default + 2"、"Late - 10"。大小写不敏感；纯数字不是合法 Priority。
    private static readonly Dictionary<string, int> EditPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Early"] = -1000,
        ["Default"] = 0,
        ["Late"] = 1000
    };

    private static readonly Dictionary<string, int> LoadPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Low"] = -1000,
        ["Medium"] = 0,
        ["High"] = 1000,
        ["Exclusive"] = int.MaxValue
    };

    /// <summary>解析 CP 的 <c>Priority</c> 字段为整数；非法值记警告并按缺省处理。</summary>
    private int ParsePriority(string? raw, string action, string sourceFile)
    {
        bool isLoad = action.Equals("Load", StringComparison.OrdinalIgnoreCase);
        Dictionary<string, int> values = isLoad ? LoadPriorities : EditPriorities;
        int fallback = isLoad ? int.MaxValue : 0;  // Load 缺省 Exclusive；Edit* 缺省 Default

        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        string text = raw.Trim();
        int splitAt = text.IndexOfAny(['+', '-']);
        string name = (splitAt < 0 ? text : text[..splitAt]).Trim();
        if (values.TryGetValue(name, out int value))
        {
            if (splitAt < 0) return value;

            string offset = text[splitAt..].Replace(" ", "");
            if (int.TryParse(offset, out int delta)) return value + delta;
        }

        Warnings.Add(
            $"{Path.GetFileName(sourceFile)}：Priority '{raw}' 不是合法的 {action} 优先级，按缺省值处理");
        return fallback;
    }
}
