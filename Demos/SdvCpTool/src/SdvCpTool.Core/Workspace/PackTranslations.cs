using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Workspace;

/// <summary>
/// 内容包的 SMAPI <c>i18n/</c> 翻译集，<c>default</c> 兜底。
/// <para>
/// **两种布局都要认**（SMAPI 的 <c>TranslationHelper</c> 会递归读 <c>i18n/</c>）：
/// <list type="bullet">
/// <item><c>i18n/zh.json</c> —— 语言 = 文件名；</item>
/// <item><c>i18n/zh/Dialogue.json</c>、<c>i18n/Zh/Events.json</c> —— 语言 = **第一层目录名**，
///   同语言下的多个文件**合并**（后读到的键覆盖先读到的）。</item>
/// </list>
/// 只认顶层文件是错的：实测 <c>[CP] Sword &amp; Sorcery</c>（<c>i18n/default/…</c>）与
/// <c>East Scarp NPCs</c>（<c>i18n/Default|Zh/…</c>）都只用目录布局，于是 2000+ 条文本退化成
/// <c>(no translation:key)</c>，Jade 的显示名也成了占位符。
/// </para>
/// <para>
/// 回退链：<c>&lt;长码&gt;</c> → <c>&lt;短码&gt;</c> → <c>default</c>（如 <c>pt-BR</c> → <c>pt</c> → <c>default</c>）。
/// 语言文件用的是星露谷的短码（<c>zh</c>），资产后缀用的是同一枚举的
/// <see cref="LanguageCodeString"/> 长码（<c>zh-CN</c>）。
/// </para>
/// </summary>
public sealed class PackTranslations
{
    /// <summary>兜底语言文件的名字；它产出的资产<b>不带</b>语言后缀。</summary>
    public const string DefaultLocale = "default";

    private readonly Dictionary<string, Dictionary<string, string>> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _variantLocales = new();

    private PackTranslations() { }

    /// <summary>是否有任何可用的翻译（<c>default.json</c> 也算）。</summary>
    public bool HasAny => _files.Count > 0;

    /// <summary>需要产出语言后缀变体的资产语言码（长码，如 <c>zh-CN</c>），去重且排序稳定。</summary>
    public IReadOnlyList<string> VariantLocales => _variantLocales;

    /// <summary>读内容包根目录下的 <c>i18n/</c>；目录不存在时返回空集。</summary>
    public static PackTranslations Load(string packRootDirectory)
    {
        var result = new PackTranslations();
        string dir = Path.Combine(packRootDirectory, "i18n");
        if (!Directory.Exists(dir)) return result;

        // 递归 + 排序：同语言目录下多文件合并时，覆盖顺序要确定（路径字典序）。
        List<string> files = Directory
            .EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string file in files)
        {
            string rel = Path.GetRelativePath(dir, file);
            int slash = rel.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            // 目录布局：语言 = 第一层目录名；顶层文件：语言 = 文件名。
            string name = slash >= 0 ? rel[..slash] : Path.GetFileNameWithoutExtension(file);
            JObject obj = JsoncReader.ParseFile(file);

            // SMAPI 的 i18n 键查找是大小写不敏感的：Translator.SetTranslations 反编译可见
            //   newobj Dictionary<string,string>(IDictionary, IEqualityComparer)
            //   call   StringComparer.get_OrdinalIgnoreCase
            // 实测 Ridgeside 亦然：`June.june_arrival.{{TargetWithoutPath}}` 会查 `June.june_arrival.Alissa`，
            // 而 i18n 里存的是小写 `June.june_arrival.alissa`。按 Ordinal 会误判成"键不存在"。
            if (!result._files.TryGetValue(name, out Dictionary<string, string>? values))
                result._files[name] = values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (JProperty prop in obj.Properties())
            {
                if (prop.Value.Type == JTokenType.String)
                    values[prop.Name] = prop.Value.Value<string>() ?? "";
            }
        }

        // 语言变体：除 default 外的语言文件 → 长码；短码与长码文件会落到同一个后缀，去重。
        foreach (string name in result._files.Keys)
        {
            if (name.Equals(DefaultLocale, StringComparison.OrdinalIgnoreCase)) continue;
            string? code = LanguageCodeString(name);
            if (string.IsNullOrEmpty(code)) continue;   // en / mod / 未知：不产后缀变体
            if (!result._variantLocales.Contains(code)) result._variantLocales.Add(code);
        }
        result._variantLocales.Sort(StringComparer.Ordinal);

        return result;
    }

    /// <summary>
    /// 取 <paramref name="key"/> 在 <paramref name="locale"/> 下的译文，走
    /// <c>长码 → 短码 → default</c> 回退链；都不存在返回 <c>null</c>。
    /// </summary>
    public string? GetValue(string key, string locale)
    {
        foreach (string candidate in FallbackChain(locale))
        {
            if (_files.TryGetValue(candidate, out Dictionary<string, string>? values)
                && values.TryGetValue(key, out string? value))
            {
                return value;
            }
        }
        return null;
    }

    private static IEnumerable<string> FallbackChain(string locale)
    {
        yield return locale;

        int dash = locale.IndexOf('-');
        if (dash > 0) yield return locale[..dash];   // zh-CN → zh

        if (!locale.Equals(DefaultLocale, StringComparison.OrdinalIgnoreCase))
            yield return DefaultLocale;
    }

    /// <summary>
    /// 语言短码 → 资产名长码，与游戏 <c>LocalizedContentManager.LanguageCodeString</c> 一致。
    /// 已经是长码（<c>pt-BR</c>）的原样返回；<c>en</c>/<c>mod</c>/未知返回 <c>null</c>（无后缀变体）。
    /// </summary>
    public static string? LanguageCodeString(string code) => code.ToLowerInvariant() switch
    {
        // ⚠ 大小写不敏感：目录布局的实测写法有 `i18n/Zh/…`、`i18n/Default/…`（East Scarp NPCs）。
        "ja" => "ja-JP",
        "ru" => "ru-RU",
        "zh" => "zh-CN",
        "pt" => "pt-BR",
        "es" => "es-ES",
        "de" => "de-DE",
        "th" => "th-TH",
        "fr" => "fr-FR",
        "ko" => "ko-KR",
        "it" => "it-IT",
        "tr" => "tr-TR",
        "hu" => "hu-HU",
        _ => IsRegionCode(code) ? code : null,
    };

    /// <summary>
    /// 是不是<b>区域码形状</b>（<c>de-DE</c>、<c>zh-CN</c>）。
    /// <para>
    /// 公开是因为**产出侧的自检也要用它**：语言变体资产名（<c>Data/Machines.zh-CN</c>）
    /// 与主资产同形，按模型核对形状时要能认出并去掉这一段后缀，否则变体**根本不会被检查**
    /// （实测代价：Cornucopia 的 <c>Data/Machines</c> 连同 8 个变体回包全失败，
    /// 而自检只报了基础资产那 8 处）。
    /// </para>
    /// </summary>
    public static bool IsRegionCode(string code) =>
        code.Length == 5 && char.IsAsciiLetter(code[0]) && char.IsAsciiLetter(code[1])
        && code[2] == '-' && char.IsAsciiLetter(code[3]) && char.IsAsciiLetter(code[4]);
}
