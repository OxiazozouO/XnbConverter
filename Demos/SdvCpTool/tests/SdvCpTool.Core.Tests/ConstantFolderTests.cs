using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 常量折叠（叶子级）的规则逐条测试。折叠**不是**布尔化简，是"把知道的那部分折成常量"。
/// 最要紧的一条：<c>evaluateLeaf</c> 返回 <c>null</c> 表示"不知道"，必须原样保留，绝不能被当成 <c>false</c>。
/// </summary>
public class ConstantFolderTests
{
    /// <summary>按 KeyName 查表的求值器：不在表里 = 求不了值（null）。</summary>
    private static Func<WhenNode, bool?> Eval(params (string Key, bool Value)[] known)
    {
        var map = known.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        return n => map.TryGetValue(n.KeyName ?? "", out bool v) ? v : null;
    }

    private static WhenNode Parse(string json) => WhenParser.Parse(JObject.Parse(json))!;

    private static WhenNode Fold(string json, Func<WhenNode, bool?> evaluate) =>
        ConstantFolder.Fold(Parse(json), evaluate);

    // ------------------------------------------------------------ Key

    [Fact]
    public void Key能求值时折成字面量_真()
    {
        WhenNode r = Fold("""{"HasMod": "Some.Mod"}""", Eval(("HasMod", true)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.True(r.LiteralValue);
    }

    [Fact]
    public void Key能求值时折成字面量_假()
    {
        WhenNode r = Fold("""{"HasMod": "Some.Mod"}""", Eval(("HasMod", false)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.False(r.LiteralValue);
    }

    [Fact]
    public void Key求不了值时原样保留()
    {
        WhenNode r = Fold("""{"Season": "spring"}""", Eval());

        Assert.Equal(WhenNodeKind.Key, r.Kind);
        Assert.Equal("Season", r.KeyName);
        Assert.Equal("spring", r.Value);
    }

    // ------------------------------------------------------------ Not

    [Fact]
    public void Not对字面量取反_真变假()
    {
        WhenNode r = Fold("""{"NOT": {"HasMod": "Some.Mod"}}""", Eval(("HasMod", true)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.False(r.LiteralValue);
    }

    [Fact]
    public void Not对字面量取反_假变真()
    {
        WhenNode r = Fold("""{"NOT": {"HasMod": "Some.Mod"}}""", Eval(("HasMod", false)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.True(r.LiteralValue);
    }

    [Fact]
    public void Not对求不了值的子节点保留()
    {
        WhenNode r = Fold("""{"NOT": {"Season": "spring"}}""", Eval());

        Assert.Equal(WhenNodeKind.Not, r.Kind);
        Assert.Equal(WhenNodeKind.Key, r.Children[0].Kind);
        Assert.Equal("Season", r.Children[0].KeyName);
    }

    // ------------------------------------------------------------ All

    [Fact]
    public void All里出现False短路为False()
    {
        // Season 求不了值，但 HasMod 已确定为假 —— 与假短路，整条为假。
        WhenNode r = Fold("""{"AND": [{"Season": "spring"}, {"HasMod": "X"}]}""",
            Eval(("HasMod", false)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.False(r.LiteralValue);
    }

    [Fact]
    public void All里丢掉True()
    {
        WhenNode r = Fold("""{"AND": [{"HasMod": "X"}, {"Season": "spring"}, {"Year": "1"}]}""",
            Eval(("HasMod", true)));

        Assert.Equal(WhenNodeKind.All, r.Kind);
        Assert.Equal(2, r.Children.Count);
        Assert.Equal("Season", r.Children[0].KeyName);
        Assert.Equal("Year", r.Children[1].KeyName);
    }

    [Fact]
    public void All全丢光为True()
    {
        WhenNode r = Fold("""{"AND": [{"HasMod": "X"}, {"HasMod": "Y"}]}""",
            Eval(("HasMod", true)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.True(r.LiteralValue);
    }

    [Fact]
    public void All只剩一个时提升为该子节点()
    {
        WhenNode r = Fold("""{"AND": [{"HasMod": "X"}, {"Season": "spring"}]}""",
            Eval(("HasMod", true)));

        Assert.Equal(WhenNodeKind.Key, r.Kind);
        Assert.Equal("Season", r.KeyName);
    }

    [Fact]
    public void All里求不了值的叶子原样保留()
    {
        WhenNode r = Fold("""{"AND": [{"HasMod": "X"}, {"Season": "spring"}]}""",
            Eval(("HasMod", true)));

        Assert.Equal("Season", r.KeyName);         // 未被当成假丢掉
        Assert.Equal("spring", r.Value);
    }

    // ------------------------------------------------------------ Any

    [Fact]
    public void Any里出现True短路为True()
    {
        WhenNode r = Fold("""{"OR": [{"Season": "spring"}, {"HasMod": "X"}]}""",
            Eval(("HasMod", true)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.True(r.LiteralValue);
    }

    [Fact]
    public void Any里丢掉False()
    {
        WhenNode r = Fold("""{"OR": [{"HasMod": "X"}, {"Season": "spring"}, {"Year": "1"}]}""",
            Eval(("HasMod", false)));

        Assert.Equal(WhenNodeKind.Any, r.Kind);
        Assert.Equal(2, r.Children.Count);
        Assert.Equal("Season", r.Children[0].KeyName);
        Assert.Equal("Year", r.Children[1].KeyName);
    }

    [Fact]
    public void Any全丢光为False()
    {
        WhenNode r = Fold("""{"OR": [{"HasMod": "X"}, {"HasMod": "Y"}]}""",
            Eval(("HasMod", false)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.False(r.LiteralValue);
    }

    [Fact]
    public void Any只剩一个时提升为该子节点()
    {
        WhenNode r = Fold("""{"OR": [{"HasMod": "X"}, {"Season": "spring"}]}""",
            Eval(("HasMod", false)));

        Assert.Equal(WhenNodeKind.Key, r.Kind);
        Assert.Equal("Season", r.KeyName);
    }

    // ------------------------------------------------------------ 最重要的约束：null != false

    [Fact]
    public void 求不了值的叶子绝不被当成假_All()
    {
        // 只有一个求不了值的叶子：折叠后必须仍是它本身，不能变成 Literal(false)。
        WhenNode r = Fold("""{"AND": [{"Season": "spring"}]}""", Eval());

        Assert.Equal(WhenNodeKind.Key, r.Kind);
        Assert.Equal("Season", r.KeyName);
    }

    [Fact]
    public void 求不了值的叶子绝不被当成假_Any()
    {
        WhenNode r = Fold("""{"OR": [{"Season": "spring"}]}""", Eval());

        Assert.Equal(WhenNodeKind.Key, r.Kind);
        Assert.NotEqual(WhenNodeKind.Literal, r.Kind);
    }

    [Fact]
    public void 真值的折叠不影响旁边求不了值的叶子()
    {
        // All([Season(不知道), HasMod(真)]) → 只折掉 HasMod，Season 原样留下；整条**不得**判成真。
        WhenNode r = Fold("""{"AND": [{"Season": "spring"}, {"HasMod": "X"}]}""",
            Eval(("HasMod", true)));

        Assert.Equal(WhenNodeKind.Key, r.Kind);
        Assert.Equal("Season", r.KeyName);
    }

    [Fact]
    public void 假值的折叠不影响旁边求不了值的叶子()
    {
        // Any([Season(不知道), HasMod(假)]) → 只折掉 HasMod，Season 原样留下；整条**不得**判成假。
        WhenNode r = Fold("""{"OR": [{"Season": "spring"}, {"HasMod": "X"}]}""",
            Eval(("HasMod", false)));

        Assert.Equal(WhenNodeKind.Key, r.Kind);
        Assert.Equal("Season", r.KeyName);
    }

    // ------------------------------------------------------------ 嵌套与纯函数

    [Fact]
    public void 嵌套_All里的Any全假使整条为假()
    {
        WhenNode r = Fold(
            """{"AND": [{"Season": "spring"}, {"OR": [{"HasMod": "X"}, {"HasMod": "Y"}]}]}""",
            Eval(("HasMod", false)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.False(r.LiteralValue);
    }

    [Fact]
    public void 嵌套_Any里的All全真使整条为真()
    {
        WhenNode r = Fold(
            """{"OR": [{"Season": "spring"}, {"AND": [{"HasMod": "X"}, {"HasMod": "Y"}]}]}""",
            Eval(("HasMod", true)));

        Assert.Equal(WhenNodeKind.Literal, r.Kind);
        Assert.True(r.LiteralValue);
    }

    [Fact]
    public void 折叠是纯函数_不改动原树()
    {
        WhenNode original = Parse("""{"HasMod": "X", "Season": "spring"}""");
        WhenNode child0 = original.Children[0];

        WhenNode folded = ConstantFolder.Fold(original, Eval(("HasMod", true)));

        Assert.Equal(WhenNodeKind.Key, original.Children[0].Kind);   // 原树的 HasMod 没被改成 Literal
        Assert.Same(child0, original.Children[0]);
        Assert.NotSame(original, folded);
        Assert.Equal("Season", folded.KeyName);
    }
}
