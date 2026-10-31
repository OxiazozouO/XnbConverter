using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// <c>{{…}}</c> 扫描的**唯一**实现（判定侧与写入侧共用）。
/// <para>
/// <b>为什么值得单独立测</b>：两处曾各写一份「找第一个 <c>}}</c>」——
/// <c>TranslationApplier</c> 那份是**配对**的（对），<c>ValueTokenSubstitutor</c> 那份不是（错）。
/// 于是 <c>{{i18n:KEY |PlayerName={{PlayerName}}}}</c> 被判成「一个 i18n 令牌」整个放行，
/// 代不掉的 <c>{{PlayerName}}</c> 随译文写进 <c>Data/Events</c>（语料 13 处）。
/// 同一件事只留一份实现，这里把边界钉死。
/// </para>
/// </summary>
public class TokenScanTests
{
    [Theory]
    [InlineData("{{i18n:K}}", false)]
    [InlineData("{{i18n:K |default=x}}", false)]
    [InlineData("{{i18n:K |PlayerName={{PlayerName}}}}", true)]        // ★ 内嵌在**参数值**里
    [InlineData("{{i18n:ICS.X.{{Random: 1,2}}}}", true)]               // 内嵌在**键**里，代不掉
    [InlineData("{{i18n:{{TargetWithoutPath}}.Greeting}}", false)]     // 内嵌字段引用令牌：写入侧会展开
    [InlineData("{{i18n:{{Target}}.Greeting}}", false)]
    [InlineData("{{i18n:{{TargetPathOnly}}.Greeting}}", false)]
    [InlineData("{{i18n:K |a={{i18n:J |b={{X}}}}}}", true)]            // 套两层
    [InlineData("{{PlayerName}}", true)]
    [InlineData("{{Random: 1,2}}", true)]
    [InlineData("{{i18n:K}} 之后还有 {{Spouse}}", true)]               // 第一个放行，第二个要报
    [InlineData("没有令牌", false)]
    [InlineData("{{i18n:K", true)]                                     // 未闭合
    [InlineData("{{i18n:{{Target}} , {{Random: 1,2}}}}", true)]        // 放行的那一个不掩盖另一个
    public void 判定未展开令牌(string text, bool expected)
    {
        Assert.Equal(expected, TokenScan.HasUnresolved(text));
    }

    [Fact]
    public void 找配对的结尾而不是第一个()
    {
        //              0123456789...
        string text = "{{i18n:K |x={{Y}}}}";

        Assert.Equal(17, TokenScan.FindTokenEnd(text, "{{i18n:".Length));   // 最外层那个 }} 的起点
        Assert.Equal(15, TokenScan.FindTokenEnd(text, text.IndexOf("{{Y") + 2));   // 内层那个
    }
}
