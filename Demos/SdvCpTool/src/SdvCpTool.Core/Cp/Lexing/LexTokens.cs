namespace SdvCpTool.Core.Cp.Lexing;

/// <summary>
/// CP 词法器的比特类型。**照抄**
/// <c>ContentPatcher.Framework.Lexing.LexTokens</c>（同名同序），免得看代码时对不上。
/// </summary>
public enum LexBitType
{
    Literal,
    StartToken,
    EndToken,
    PositionalInputArgSeparator,
    NamedInputArgSeparator,
}

public enum LexTokenType
{
    Literal,
    Token,
    TokenInput,
}

/// <summary>
/// 词法比特：一段原文，或一个结构字符（<c>{{</c>/<c>}}</c>/<c>|</c>/<c>:</c>）。
/// <para>
/// <b><c>Start</c> 是 CP 那边没有的</b>：CP 只把比特流解析成树，不需要回到原文的位置；
/// 而本仓库有一处必须**按原文位置**替换（<c>TranslationApplier</c> 只换 <c>{{i18n:…}}</c> 那一段，
/// 其余字节必须原样保留）。记下偏移，就不必再手写一遍括号配对。
/// </para>
/// </summary>
public readonly record struct LexBit(LexBitType Type, string Text, int Start = 0);

public interface ILexToken
{
    LexTokenType Type { get; }
}

public sealed class LexTokenLiteral : ILexToken
{
    public LexTokenType Type => LexTokenType.Literal;

    /// <summary>原文片段。<c>set</c> 是给 <see cref="CpLexer"/> 的后处理用的（合并相邻字面量、修剪空白）。</summary>
    public string Text { get; set; } = "";

    public LexTokenLiteral(string? text) => Text = text ?? "";

    public override string ToString() => Text;
}

/// <summary>令牌的输入参数段（<c>:</c> 或 <c>|</c> 之后到令牌结束之前的那一串）。</summary>
public sealed class LexTokenInput : ILexToken
{
    public LexTokenType Type => LexTokenType.TokenInput;
    public ILexToken[] Parts { get; }

    public LexTokenInput(ILexToken[] parts) => Parts = parts;

    public override string ToString() => string.Concat(Parts.Select(p => p.ToString()));
}

/// <summary>一个令牌：名字 + 可选的输入参数（里面可以再嵌令牌）。</summary>
public sealed class LexTokenToken : ILexToken
{
    public LexTokenType Type => LexTokenType.Token;
    public string Name { get; }
    public LexTokenInput? InputArgs { get; }

    /// <summary>真 = 该令牌不是由 <c>{{ }}</c> 包起来的（CP 的「隐含花括号」解析模式）。</summary>
    public bool ImpliedBraces { get; }

    /// <summary>
    /// 该令牌在**原文**里的起止下标（<c>[Start, End)</c>）。
    /// <para>
    /// CP 那边没有这两个字段 —— 它只需要树。本仓库要「**按原文位置替换**」：
    /// 展开令牌时把 <c>text[Start..End]</c> 换成值，其余字节原样保留。
    /// 靠它就不必重建整串（重建会把花括号内的空白规范化掉，见 <c>CpLexerTests.往返会把花括号内的空白规范化</c>）。
    /// </para>
    /// </summary>
    public int Start { get; init; } = -1;
    public int End { get; init; } = -1;

    public LexTokenToken(string name, LexTokenInput? inputArgs, bool impliedBraces)
    {
        Name = name;
        InputArgs = inputArgs;
        ImpliedBraces = impliedBraces;
    }

    /// <summary>命名空间令牌 <c>作者.模组名/令牌名</c> 的命名空间部分；不是则为 <c>null</c>。</summary>
    public string? GetProviderModId()
    {
        string[] parts = Name.Split('/', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? parts[0] : null;
    }

    public bool HasInputArgs() => InputArgs != null && InputArgs.Parts.Length != 0;

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        if (!ImpliedBraces) sb.Append("{{");
        sb.Append(Name);
        if (InputArgs != null)
        {
            if (!InputArgs.ToString().StartsWith('|')) sb.Append(':');
            sb.Append(InputArgs);
        }
        if (!ImpliedBraces) sb.Append("}}");
        return sb.ToString();
    }
}

/// <summary>词法格式错误 —— 与 CP 的 <c>LexFormatException</c> 对应。</summary>
public sealed class LexFormatException : Exception
{
    public LexFormatException(string message) : base(message) { }
    public LexFormatException(string message, Exception inner) : base(message, inner) { }
}
