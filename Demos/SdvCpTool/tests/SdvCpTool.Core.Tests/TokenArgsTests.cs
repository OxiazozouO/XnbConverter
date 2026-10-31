using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 令牌输入参数里那几个能离线折叠的算子（目前只有 <c>|contains=</c>）。
/// <para>
/// 口径**不是猜的**：`[CP] Seasonal Cute Characters` 的配置 <c>WinterClothing</c> 取
/// <c>IndoorAndOutdoor</c> 时定义了
/// <c>IndoorsWinterOutfitEnabled = {{WinterClothing |contains=IndoorAndOutdoor, IndoorOnly}}</c>
/// 与 <c>IndoorsWinterOutfitDisabled = {{WinterClothing |contains=OutdoorOnly}}</c> ——
/// 只有**任一命中**说得通（全都要有的话，选 <c>IndoorAndOutdoor</c> 两边都是假）。
/// </para>
/// </summary>
public class TokenArgsTests
{
    [Fact]
    public void 单值命中为真()
    {
        Assert.True(TokenArgs.TryFoldContains(new[] { "IndoorAndOutdoor" },
            "|contains=IndoorAndOutdoor", out string folded));
        Assert.Equal("true", folded);
    }

    [Fact]
    public void 多值时任一命中即为真()
    {
        Assert.True(TokenArgs.TryFoldContains(new[] { "IndoorAndOutdoor" },
            "|contains=IndoorAndOutdoor, IndoorOnly", out string folded));
        Assert.Equal("true", folded);
    }

    [Fact]
    public void 一个都没命中为假()
    {
        Assert.True(TokenArgs.TryFoldContains(new[] { "IndoorAndOutdoor" },
            "|contains=OutdoorOnly", out string folded));
        Assert.Equal("false", folded);
    }

    [Fact]
    public void 大小写不敏感()
    {
        Assert.True(TokenArgs.TryFoldContains(new[] { "IndoorAndOutdoor" },
            "|contains=indoorandoutdoor", out string folded));
        Assert.Equal("true", folded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("|contains=")]
    [InlineData(":input")]              // 别的算子不认 ⇒ 调用方原样保留令牌
    public void 认不出或参数为空时不折叠(string? args)
    {
        Assert.False(TokenArgs.TryFoldContains(new[] { "A" }, args, out _));
    }
}
