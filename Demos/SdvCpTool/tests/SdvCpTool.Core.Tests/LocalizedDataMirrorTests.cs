using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 本地化 Data 是整份替换：base 有、locale 没有的键必须补回来。
/// </summary>
public class LocalizedDataMirrorTests
{
    [Fact]
    public void 字典补缺键且不覆盖本地化已有值()
    {
        var baseToken = JObject.Parse("""
        { "A": "base-a", "B": "base-b", "C": { "X": 1 } }
        """);
        var local = JObject.Parse("""
        { "A": "本地化-a" }
        """);

        Assert.Equal(2, LocalizedDataMirror.MirrorToken(baseToken, local));
        Assert.Equal("本地化-a", local["A"]!.Value<string>());
        Assert.Equal("base-b", local["B"]!.Value<string>());
        Assert.Equal(1, local["C"]!["X"]!.Value<int>());
        Assert.Equal(0, LocalizedDataMirror.MirrorToken(baseToken, local));
    }

    [Fact]
    public void 数组按Id补缺且不重复()
    {
        var baseToken = JArray.Parse("""
        [ { "Id": "A", "Value": "base" }, { "Id": "B", "Value": "base" } ]
        """);
        var local = JArray.Parse("""
        [ { "Id": "A", "Value": "local" } ]
        """);

        Assert.Equal(1, LocalizedDataMirror.MirrorToken(baseToken, local));
        Assert.Equal(2, local.Count);
        Assert.Equal("local", local[0]!["Value"]!.Value<string>());
        Assert.Equal("B", local[1]!["Id"]!.Value<string>());
        Assert.Equal(0, LocalizedDataMirror.MirrorToken(baseToken, local));
    }

    [Fact]
    public void 没有Id的数组元素不猜()
    {
        var baseToken = JArray.Parse("""[ "a", "b" ]""");
        var local = JArray.Parse("""[ "x" ]""");
        Assert.Equal(0, LocalizedDataMirror.MirrorToken(baseToken, local));
        Assert.Single(local);
    }

    [Fact]
    public void 嵌套对象和带Id数组也补缺但不覆盖译文()
    {
        var baseToken = JObject.Parse("""
        {
          "Location": {
            "DisplayName": "Base",
            "Fish": [ { "Id": "A", "Chance": 0.1 }, { "Id": "B", "Chance": 0.2 } ],
            "Nested": { "X": 1, "Y": 2 }
          }
        }
        """);
        var local = JObject.Parse("""
        {
          "Location": {
            "DisplayName": "本地名",
            "Fish": [ { "Id": "A", "Chance": 9 } ],
            "Nested": { "X": 7 }
          }
        }
        """);

        Assert.Equal(2, LocalizedDataMirror.MirrorToken(baseToken, local));
        Assert.Equal("本地名", local["Location"]!["DisplayName"]!.Value<string>());
        Assert.Equal(9, local["Location"]!["Fish"]![0]!["Chance"]!.Value<int>());
        Assert.Equal("B", local["Location"]!["Fish"]![1]!["Id"]!.Value<string>());
        Assert.Equal(7, local["Location"]!["Nested"]!["X"]!.Value<int>());
        Assert.Equal(2, local["Location"]!["Nested"]!["Y"]!.Value<int>());
        Assert.Equal(0, LocalizedDataMirror.MirrorToken(baseToken, local));
    }
}
