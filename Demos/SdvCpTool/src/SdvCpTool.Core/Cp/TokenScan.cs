using SdvCpTool.Core.Cp.Lexing;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 「这段文本里还有没有代不掉的 <c>{{…}}</c> 令牌」—— 判定侧
/// （<see cref="ValueTokenSubstitutor.HasUnresolvedToken"/>）与写入侧
/// （<c>Merging.TranslationApplier</c>）共用。
/// <para>
/// <b>结构判定交给 CP 的词法器</b>（<see cref="CpLexer"/>），这里只负责**语义裁决**。
/// 缘由见 <see cref="CpLexer"/> 的说明：本仓库对 <c>{{</c> 的结构写过四份手写扫描，
/// 三份的括号配对是错的 —— <c>{{i18n:KEY |PlayerName={{PlayerName}}}}</c> 这种嵌套会被
/// 「找第一个 <c>}}</c>」截断，整段被当成长度只到内层令牌为止的东西。
/// 实测三次静默丢内容（13 处令牌落进产出等）都是它。
/// </para>
/// </summary>
public static class TokenScan
{
    /// <summary>字段引用令牌：由 <see cref="Conditions.FieldReferenceTokens"/> 展开，不算残渣。</summary>
    private static readonly string[] FieldTokens = { "Target", "TargetPathOnly", "TargetWithoutPath" };

    /// <summary>
    /// <paramref name="text"/> 里还有没有**没展开的** CP 令牌。
    /// <list type="bullet">
    /// <item><c>{{i18n:…}}</c> 放行，但**继续查它的键与参数**（<c>|x={{Y}}</c> 里的 <c>Y</c> 要报）；</item>
    /// <item>i18n **里面**的字段引用令牌放行（那是写入侧在键上展开的，不是残渣）；</item>
    /// <item>其余任何令牌都算未展开；<b>词法错误也算</b>（CP 解析不了的串，游戏更解析不了）。</item>
    /// </list>
    /// </summary>
    public static bool HasUnresolved(string text)
        => Scan(text, exemptFieldTokens: false);

    /// <summary>
    /// 找 <paramref name="contentStart"/> 处令牌配对的 <c>}}</c> 的下标 —— 交给词法器
    /// （<see cref="CpLexer.FindTokenEnd"/>），**不再自己数括号**。
    /// </summary>
    public static int FindTokenEnd(string text, int contentStart)
        => CpLexer.Instance.FindTokenEnd(text, contentStart);

    /// <summary>
    /// 列出**没展开的令牌名**（去重）—— 给报告用：拒绝一条补丁时要说清「卡在哪个令牌上」。
    /// <para>
    /// <b>为什么非列不可</b>：语料里 **112 条** 补丁被这道闸拦下，理由原先只有一句话，
    /// 看不出到底是 `{{Random}}`（值本身合法，只是每次不同）还是别的 mod 的 C# 令牌
    /// （根本不在我们能查的范围）。两者该不该放行是**完全不同的决定**，
    /// 而「要不要为它们整条不写」这笔账原先只能靠猜。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Unresolved(string text)
    {
        var names = new List<string>();
        Collect(text, exemptFieldTokens: false, names);
        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void Collect(string text, bool exemptFieldTokens, List<string> names)
    {
        CpLexer lexer = CpLexer.Instance;
        if (!lexer.MightContainTokens(text)) return;

        // 词法错误（未闭合等）⇒ 当成有残渣。CP 那边会直接抛格式异常；这里要的是名字。
        if (!lexer.TryParse(text, out IReadOnlyList<ILexToken> tokens))
        {
            names.Add("(词法错误)");
            return;
        }
        foreach (ILexToken token in tokens) CollectToken(token, exemptFieldTokens, names);
    }

    private static void CollectToken(ILexToken token, bool exemptFieldTokens, List<string> names)
    {
        if (token is not LexTokenToken tk) return;

        if (exemptFieldTokens && FieldTokens.Contains(tk.Name, StringComparer.OrdinalIgnoreCase)) return;

        if (!tk.Name.Equals("i18n", StringComparison.OrdinalIgnoreCase))
        {
            names.Add(tk.Name);
            return;
        }
        if (tk.InputArgs == null) return;
        foreach (ILexToken part in tk.InputArgs.Parts) CollectToken(part, exemptFieldTokens: true, names);
    }

    private static bool Scan(string text, bool exemptFieldTokens)
    {
        CpLexer lexer = CpLexer.Instance;
        if (!lexer.MightContainTokens(text)) return false;

        // 词法错误（未闭合等）⇒ 当成有残渣。CP 那边会直接抛格式异常；这里要的是一个布尔。
        if (!lexer.TryParse(text, out IReadOnlyList<ILexToken> tokens)) return true;

        return tokens.Any(t => IsUnresolved(t, exemptFieldTokens));
    }

    private static bool IsUnresolved(ILexToken token, bool exemptFieldTokens)
    {
        if (token is not LexTokenToken tk) return false;   // 字面量（含 `|` `:` 的独立字面量）不算

        if (exemptFieldTokens && FieldTokens.Contains(tk.Name, StringComparer.OrdinalIgnoreCase))
            return false;

        // `i18n` 由通道 B1（`TranslationApplier`）展开；键与具名参数里的内嵌令牌接着查 ——
        // 能代掉的在 `ValueTokenSubstitutor.Apply` 那一步已经代掉了，还留着的就是残渣。
        if (!tk.Name.Equals("i18n", StringComparison.OrdinalIgnoreCase)) return true;

        return tk.InputArgs != null
            && tk.InputArgs.Parts.Any(p => IsUnresolved(p, exemptFieldTokens: true));
    }
}
