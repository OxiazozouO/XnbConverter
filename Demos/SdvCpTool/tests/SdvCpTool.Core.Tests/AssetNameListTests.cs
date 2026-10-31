using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

public class AssetNameListTests
{
    [Fact]
    public void 普通逗号分隔()
    {
        Assert.Equal(new[] { "a", "b", "c" }, AssetNameList.Split("a, b ,c"));
    }

    [Fact]
    public void 令牌里的逗号不是分隔符()
    {
        // 这是真实回归：Include FromFile: Data/Festivals/Y{{Random:null, 3}}.json
        // 曾被朴素 Split(',') 拦腰切成两段，两个都不存在，整条 Include 贡献 0 条补丁。
        Assert.Equal(new[] { "Data/Festivals/Y{{Random:null, 3}}.json" },
            AssetNameList.Split("Data/Festivals/Y{{Random:null, 3}}.json"));
    }

    [Fact]
    public void 令牌内逗号与真分隔符并存()
    {
        Assert.Equal(new[] { "Data/A", "Data/B{{Random:1, 2}}", "Data/C" },
            AssetNameList.Split("Data/A, Data/B{{Random:1, 2}}, Data/C"));
    }

    [Fact]
    public void 多层嵌套令牌()
    {
        Assert.Equal(new[] { "x{{a{{1,2}},3}}y" }, AssetNameList.Split("x{{a{{1,2}},3}}y"));
    }

    [Fact]
    public void 空段丢弃()
    {
        Assert.Equal(new[] { "a", "b" }, AssetNameList.Split("a,,  ,b"));
    }

    [Fact]
    public void 空与空白输入返回空表()
    {
        Assert.Empty(AssetNameList.Split(null));
        Assert.Empty(AssetNameList.Split(""));
        Assert.Empty(AssetNameList.Split("   "));
        Assert.Empty(AssetNameList.Split(","));
    }

    [Fact]
    public void 不配对的左括号退化为整段而不是抛异常()
    {
        // {{ 多于 }}：进入令牌后的逗号不再当分隔符，剩下的全算一段，不能崩。
        // 注意 `a,` 那个逗号在深度 0，是**真**分隔符，照切。
        Assert.Equal(new[] { "a", "b{{unclosed" }, AssetNameList.Split("a, b{{unclosed"));
    }

    [Fact]
    public void 单括号不是令牌边界()
    {
        Assert.Equal(new[] { "a{b", "c}d" }, AssetNameList.Split("a{b, c}d"));
    }
}
