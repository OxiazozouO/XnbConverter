using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// CP 内容包的<b>配置令牌</b>：<c>content.json</c> 的 <c>ConfigSchema</c> 字段名<b>就是令牌名</b>，
/// 值优先取包根目录的 <c>config.json</c>（SMAPI 约定：与 <c>manifest.json</c> 同层），
/// 缺失或为空时回退到 <c>ConfigSchema</c> 的 <c>Default</c>。
/// <para>
/// 依据 CP 反编译源码：
/// <list type="bullet">
/// <item><c>ScreenManager.cs:401-407</c> <c>AddConfigToken</c>：每个 <c>ConfigField</c> 注册成
/// <c>ImmutableValueProvider</c>，令牌名即 <c>ConfigSchema</c> 的字段名（<c>AddLocalToken</c>，
/// 属于内容包自己的本地令牌）；</item>
/// <item><c>ConfigFileHandler.cs</c> <c>LoadConfigSchema</c>：字段级的合法性检查
/// （名字含 <c>:</c>/<c>|</c>、旧格式缺 <c>AllowValues</c>、<c>AllowBlank:false</c> 又缺 <c>Default</c>、
/// <c>Default</c> 不在 <c>AllowValues</c> 里、<c>AllowMultiple:false</c> 却给了多个 <c>Default</c>）
/// 一律<b>丢弃该字段</b> —— 不注册令牌；</item>
/// <item><c>ConfigFileHandler.cs</c> <c>LoadConfigValues</c>：<c>config.json</c> 有非空值就用它，
/// 否则用 <c>Default</c>；值不合法（超 <c>AllowValues</c>、单值字段给了多个）时回退 <c>Default</c>；</item>
/// <item>逗号分隔按 <c>string.Split(',', RemoveEmptyEntries | TrimEntries)</c> 切
/// （<c>ModEntry.cs:403</c> <c>ParseCommaDelimitedField</c>），集合大小写不敏感。</item>
/// </list>
/// </para>
/// <para>
/// 本类只负责「字段名 → 值集合」的<b>取数</b>；<b>求值语义</b>（CP 的条件匹配）在
/// <see cref="SdvCpTool.Core.Merging.Baker"/> 里，<b>只此一处</b> —— 判定与写入走同一个实现。
/// </para>
/// </summary>
public sealed class ConfigSchemaTokens
{
    // 字段名（大小写不敏感）→ 当前值集合（已按 config.json / Default 的优先级解析好）。
    private readonly Dictionary<string, IReadOnlyList<string>> _fields = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>没有 <c>ConfigSchema</c> 时的空解析器 —— 行为与「不读 config」完全一致。</summary>
    public static ConfigSchemaTokens Empty { get; } = new();

    public bool IsEmpty => _fields.Count == 0;

    /// <summary>该名字是不是本包 <c>ConfigSchema</c> 里的字段（= 是不是配置令牌）。</summary>
    public bool Contains(string name) => _fields.ContainsKey(name.Trim());

    /// <summary>取某配置字段的当前值集合；不是配置字段 → <c>false</c>（不猜）。</summary>
    public bool TryGetValues(string name, out IReadOnlyList<string> values)
    {
        if (_fields.TryGetValue(name.Trim(), out IReadOnlyList<string>? found))
        {
            values = found;
            return true;
        }
        values = Array.Empty<string>();
        return false;
    }

    /// <summary>
    /// 读内容包根目录的 <c>content.json</c> 与 <c>config.json</c>。
    /// 文件缺失或解析失败 → 空解析器（不抛异常）：内容包本来可以没有 <c>ConfigSchema</c>。
    /// </summary>
    public static ConfigSchemaTokens Load(string packRootDirectory)
    {
        string contentJson = Path.Combine(packRootDirectory, "content.json");
        if (!File.Exists(contentJson)) return Empty;

        JObject root;
        try { root = JsoncReader.ParseFile(contentJson); }
        catch { return Empty; }

        return Load(root, packRootDirectory);
    }

    /// <summary>
    /// 从<b>已解析</b>的 <c>content.json</c> 根对象构建（调用方已经解析过时用，避免重复读盘）。
    /// </summary>
    public static ConfigSchemaTokens Load(JObject contentRoot, string packRootDirectory)
    {
        if (contentRoot["ConfigSchema"] is not JObject schema) return Empty;

        var tokens = new ConfigSchemaTokens();

        // config.json 按 SMAPI 约定在包根（与 manifest.json 同层）。解析失败 → 当作没有，全部回退 Default。
        JObject? config = null;
        string configPath = Path.Combine(packRootDirectory, "config.json");
        if (File.Exists(configPath))
        {
            try { config = JsoncReader.ParseFile(configPath); }
            catch { config = null; }
        }

        // CP 的旧格式分支：Format < 1.7 时 AllowValues 必填、Default 缺省取 AllowValues 的第一个。
        // 现代内容包都是 2.x，这条分支实际不生效，但语义照抄以免对老包判错。
        bool oldFormat = IsOlderThan07(RawString(contentRoot["Format"]));

        foreach (JProperty prop in schema.Properties())
        {
            string name = prop.Name.Trim();

            // ConfigFileHandler.cs:76-83 —— 空名、含 `:` / `|` 的名字一律丢弃该字段。
            if (name.Length == 0) continue;
            if (name.Contains(':') || name.Contains('|')) continue;

            // ConfigFileHandler.cs:84-88 —— 与 CP 内建条件同名的字段被丢弃（保留字），不注册成令牌。
            // 必须在取数前丢掉：否则 `{"Time": "1200"}` 这种会被当成配置令牌求值，
            // 而 CP 里它仍然是内建的 `Time` 条件。
            if (ConditionVocabulary.Classify(name) != TokenKind.Unknown) continue;

            if (prop.Value is not JObject raw) continue;

            IReadOnlyList<string> allowValues = SplitList(RawString(raw["AllowValues"]));
            IReadOnlyList<string> defaults = SplitList(RawString(raw["Default"]));
            bool allowBlank = RawBool(raw["AllowBlank"]);
            bool allowMultiple = RawBool(raw["AllowMultiple"]);

            if (oldFormat)
            {
                if (allowValues.Count == 0) continue;                          // 旧格式必须给 AllowValues
                if (defaults.Count == 0 && !allowBlank) defaults = new[] { allowValues[0] };
            }
            if (!allowBlank && defaults.Count == 0) continue;                  // AllowBlank:false 必须给 Default
            if (allowValues.Count > 0 && defaults.Any(d => !InList(allowValues, d)))
                continue;                                                      // Default 必须落在 AllowValues 内
            if (!allowMultiple && defaults.Count > 1) continue;                // 单值字段不能有多个 Default

            // 取数优先级：config.json 有该键（且值非空，或字段允许空）→ 用它；否则用 Default。
            IReadOnlyList<string> values = defaults;
            string? fromConfig = FindConfigValue(config, name);
            if (fromConfig != null)
            {
                IReadOnlyList<string> parsed = SplitList(fromConfig);
                if (parsed.Count > 0 || allowBlank) values = parsed;
            }

            // 值合法性：CP 不因此拒绝整个包，而是回退 Default（ConfigFileHandler.cs:163-186）。
            if (!allowMultiple && values.Count > 1) values = defaults;
            if (allowValues.Count > 0 && values.Any(v => !InList(allowValues, v))) values = defaults;

            tokens._fields[name] = values;
        }

        return tokens;
    }

    /// <summary>config.json 里按字段名取值（大小写不敏感，与 CP 的 <c>InvariantDictionary</c> 一致）。</summary>
    private static string? FindConfigValue(JObject? config, string name)
    {
        if (config == null) return null;
        foreach (JProperty prop in config.Properties())
        {
            if (string.Equals(prop.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                // CP 的 config.json 是 Dictionary<string,string>；标量一律字符串化，对象/数组无法比较 → 当作没有。
                return RawString(prop.Value);
            }
        }
        return null;
    }

    /// <summary>取标量的字符串形式；对象/数组等非标量 → <c>null</c>（当作没给，不抛异常）。</summary>
    private static string? RawString(JToken? token) => token is JValue scalar ? scalar.ToString() : null;

    /// <summary>取布尔；非布尔标量按 <c>bool.TryParse</c> 解析，解析不了 → <c>false</c>（CP 的字段缺省值）。</summary>
    private static bool RawBool(JToken? token) =>
        token is JValue scalar && bool.TryParse(scalar.ToString(), out bool value) && value;

    /// <summary>CP 的逗号分隔字段解析：<c>RemoveEmptyEntries | TrimEntries</c>，去重（大小写不敏感），保序。</summary>
    private static IReadOnlyList<string> SplitList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool InList(IReadOnlyList<string> list, string value) =>
        list.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>CP 的 <c>formatVersion.IsOlderThan("1.7")</c>：主版本 &lt; 1，或 1.x 且 x &lt; 7。</summary>
    private static bool IsOlderThan07(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return false;   // CP 要求 Format 必填；缺省按现代格式处理
        string[] parts = format.Trim().Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out int major)) return false;
        if (major != 1) return major < 1;
        return parts.Length < 2 || !int.TryParse(parts[1], out int minor) || minor < 7;
    }
}
