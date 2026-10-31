using SdvCpTool.Core.Cp.Lexing;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// CP 词法器的移植（<c>ContentPatcher.Framework.Lexing/Lexer.cs</c>）。
/// <para>
/// <b>为什么要它</b>：本仓库对 <c>{{</c> 的结构写过**四份**手写扫描，三份的括号配对是错的，
/// 造成三次静默丢内容。结构只该有一处定义 —— CP 自己的词法器。这里的用例就是把它的
/// 边界条件钉住，免得以后有人又"顺手简化"回 <c>IndexOf</c>。
/// </para>
/// </summary>
public class CpLexerTests
{
    private static IReadOnlyList<ILexToken> Parse(string text)
    {
        Assert.True(CpLexer.Instance.TryParse(text, out IReadOnlyList<ILexToken> tokens), $"解析失败：{text}");
        return tokens;
    }

    private static LexTokenToken Only(string text)
    {
        IReadOnlyList<ILexToken> tokens = Parse(text);
        return Assert.IsType<LexTokenToken>(Assert.Single(tokens));
    }

    [Fact]
    public void 嵌套令牌解析成一层树而不是截断()
    {
        // 关键用例：`找第一个 }}` 会在这里截断（`|PlayerName={{PlayerName` 被当成令牌体）。
        LexTokenToken outer = Only("{{i18n:K |PlayerName={{PlayerName}}}}");

        Assert.Equal("i18n", outer.Name);
        Assert.NotNull(outer.InputArgs);
        Assert.Contains(outer.InputArgs!.Parts, p => p is LexTokenToken t && t.Name == "PlayerName");
    }

    [Fact]
    public void 具名参数与定位参数都保留在输入段里()
    {
        LexTokenToken token = Only("{{i18n:KEY |default=x}}");

        Assert.Equal("i18n", token.Name);
        string input = token.InputArgs!.ToString();
        Assert.StartsWith("KEY ", input);
        Assert.Contains("|default=x", input);
    }

    [Fact]
    public void 未闭合的令牌解析失败而不是抛出去()
    {
        // 判定侧要的是「解析不了」这个布尔；CP 那边是抛 LexFormatException。
        Assert.False(CpLexer.Instance.TryParse("{{i18n:K", out _));
        Assert.False(CpLexer.Instance.TryParse("{{i18n:K}} 后面的 }} 孤零零", out _));
    }

    [Fact]
    public void 单个花括号是字面量不是结构()
    {
        IReadOnlyList<ILexToken> tokens = Parse("a { b } c");
        Assert.Equal("a { b } c", string.Concat(tokens.Select(t => t.ToString())));
    }

    [Theory]
    [InlineData("{{i18n:K}}")]
    [InlineData("{{i18n:K |x={{Y}}}}")]
    [InlineData("前缀 {{A}}/{{B}} 后缀")]
    [InlineData("{{TargetWithoutPath}}")]
    public void 往返一致(string text)
    {
        // 重建串必须逐字节等于原文 —— 写入侧要按原文位置只换 i18n 那一段，
        // 余下字节原样保留；往返不一致就意味着「顺手展开」会改到别处。
        Assert.Equal(text, string.Concat(Parse(text).Select(t => t.ToString())));
    }

    [Theory]
    [InlineData("{{ i18n : K }}")]          // 令牌名与参数两侧的空白会被规范化掉
    [InlineData("{{i18n:K }}")]
    public void 往返会把花括号内的空白规范化(string text)
    {
        // ⚠ 这是**已知偏离**：CP 的 ParseBitQueue 会合并相邻字面量并在令牌输入边界修剪空白，
        //    于是重建串与原文不同。因此写入侧**不能**靠重建串来替换 —— 必须按原文偏移替换。
        //    这条用例把它钉住：哪天有人想「干脆重建整串」，这里先红。
        string rebuilt = string.Concat(Parse(text).Select(t => t.ToString()));
        Assert.NotEqual(text, rebuilt);
        Assert.Equal("{{i18n:K}}", rebuilt);
    }

    [Fact]
    public void 找配对结尾走同一份比特流()
    {
        string text = "{{i18n:K |x={{Y}}}}";

        Assert.Equal(17, CpLexer.Instance.FindTokenEnd(text, "{{i18n:".Length));   // 最外层
        Assert.Equal(15, CpLexer.Instance.FindTokenEnd(text, text.IndexOf("{{Y", StringComparison.Ordinal) + 2));
        Assert.Equal(-1, CpLexer.Instance.FindTokenEnd("{{i18n:K", "{{i18n:".Length));   // 未闭合
    }
}
