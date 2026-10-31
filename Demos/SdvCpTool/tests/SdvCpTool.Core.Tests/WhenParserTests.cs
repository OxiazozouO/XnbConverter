using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

public class WhenParserTests
{
    [Fact]
    public void 无When返回null()
    {
        Assert.Null(WhenParser.Parse(null));
    }

    [Fact]
    public void 单个键()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"Season\": \"spring\"}"))!;
        Assert.Equal(WhenNodeKind.All, root.Kind);
        var key = Assert.Single(root.Children);
        Assert.Equal(WhenNodeKind.Key, key.Kind);
        Assert.Equal("Season", key.KeyName);
        Assert.Null(key.Arguments);
    }

    [Fact]
    public void 多个键在同一个对象里表示AND()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"Season\": \"spring\", \"Year\": \"1\"}"))!;
        Assert.Equal(2, root.Children.Count);
        Assert.All(root.Children, c => Assert.Equal(WhenNodeKind.Key, c.Kind));
    }

    private static WhenNode ParseKey(string rawKey)
    {
        var when = new JObject { [rawKey] = "x" };
        return Assert.Single(WhenParser.Parse(when)!.Children);
    }

    // 用例取自真实 CP 内容包（Ridgeside Village 等）的实际写法
    [Theory]
    [InlineData("HasMod |contains=Foo.Bar", "HasMod", "|contains=Foo.Bar")]
    [InlineData("HasSeenEvent: |contains=75160120", "HasSeenEvent", "|contains=75160120")]
    [InlineData("HasDialogueAnswer|contains=751600641", "HasDialogueAnswer", "|contains=751600641")]
    [InlineData("Query: {{Day}} < 15", "Query", "{{Day}} < 15")]
    [InlineData("Season", "Season", null)]
    [InlineData("Hearts:Abigail", "Hearts", "Abigail")]
    // ★ 参数里嵌了令牌 —— 交给词法器之后连 **参数内部的 `|`/`:`** 也不会被误当分隔符
    [InlineData("HasValue:{{TargetWithoutPath}}", "HasValue", "{{TargetWithoutPath}}")]
    [InlineData("HasValue:{{HasMod|contains=Foo}}", "HasValue", "{{HasMod|contains=Foo}}")]
    // 词法失败（键名里有孤立的 `{{`）⇒ 整串当键名，不抛异常
    [InlineData("{{Target}}", "{{Target}}", null)]
    public void 键名与参数的拆分(string rawKey, string expectedName, string? expectedArgs)
    {
        var key = ParseKey(rawKey);
        Assert.Equal(expectedName, key.KeyName);
        Assert.Equal(expectedArgs, key.Arguments);
    }

    [Fact]
    public void AND数组()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"AND\": [{\"Season\": \"spring\"}, {\"Year\": \"1\"}]}"))!;
        var all = Assert.Single(root.Children);
        Assert.Equal(WhenNodeKind.All, all.Kind);
        Assert.Equal(2, all.Children.Count);
    }

    [Fact]
    public void OR数组()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"OR\": [{\"Season\": \"spring\"}, {\"Season\": \"fall\"}]}"))!;
        var any = Assert.Single(root.Children);
        Assert.Equal(WhenNodeKind.Any, any.Kind);
        Assert.Equal(2, any.Children.Count);
    }

    [Fact]
    public void NOT对象()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"NOT\": {\"Season\": \"winter\"}}"))!;
        var not = Assert.Single(root.Children);
        Assert.Equal(WhenNodeKind.Not, not.Kind);
        Assert.Equal("Season", Assert.Single(not.Children).KeyName);
    }

    [Fact]
    public void 嵌套组合()
    {
        var json = "{\"AND\": [{\"Season\": \"spring\"}, {\"OR\": [{\"Year\": \"1\"}, {\"Year\": \"2\"}]}]}";
        var root = WhenParser.Parse(JObject.Parse(json))!;
        var all = root.Children[0];
        Assert.Equal(WhenNodeKind.Any, all.Children[1].Kind);
    }

    [Fact]
    public void Descendants遍历所有键节点()
    {
        var json = "{\"AND\": [{\"Season\": \"spring\"}, {\"NOT\": {\"Year\": \"1\"}}]}";
        var root = WhenParser.Parse(JObject.Parse(json))!;
        var names = root.Descendants().Where(n => n.Kind == WhenNodeKind.Key).Select(n => n.KeyName).ToList();
        Assert.Equal(new[] { "Season", "Year" }, names);
    }

    [Fact]
    public void 键节点携带原始值()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"Season\": \"spring\"}"))!;
        Assert.Equal("spring", Assert.Single(root.Children).Value);
    }

    [Fact]
    public void 带参数时值与参数分开()
    {
        var raw = WhenParser.Parse(JObject.Parse("{\"HasSeenEvent: hostPlayer\": \"123\"}"))!;
        var key = Assert.Single(raw.Children);
        Assert.Equal("HasSeenEvent", key.KeyName);
        Assert.Equal("hostPlayer", key.Arguments);
        Assert.Equal("123", key.Value);
    }

    [Fact]
    public void 组合节点的值不参与()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"AND\": [{\"Season\": \"spring\"}]}"))!;
        Assert.Null(root.Children[0].Value);
    }

    [Fact]
    public void 非字符串值转成字符串()
    {
        var root = WhenParser.Parse(JObject.Parse("{\"HasSeenEvent\": 75160255}"))!;
        Assert.Equal("75160255", Assert.Single(root.Children).Value);
    }
}
