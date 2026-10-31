using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

public class JsoncReaderTests
{
    [Fact]
    public void 解析纯JSON()
    {
        JObject o = JsoncReader.Parse("{\"a\": 1}");
        Assert.Equal(1, o["a"]!.Value<int>());
    }

    [Fact]
    public void 去掉行注释()
    {
        JObject o = JsoncReader.Parse("{ // 注释\n \"a\": 1 }");
        Assert.Equal(1, o["a"]!.Value<int>());
    }

    [Fact]
    public void 去掉块注释()
    {
        JObject o = JsoncReader.Parse("{ /* 注释 */ \"a\": 1 }");
        Assert.Equal(1, o["a"]!.Value<int>());
    }

    [Fact]
    public void 去掉尾逗号()
    {
        JObject o = JsoncReader.Parse("{\"a\": [1, 2, ], }");
        Assert.Equal(2, o["a"]!.Count());
    }

    [Fact]
    public void 字符串里的双斜杠不被当注释()
    {
        JObject o = JsoncReader.Parse("{\"url\": \"http://example.com\"}");
        Assert.Equal("http://example.com", o["url"]!.Value<string>());
    }

    [Fact]
    public void 字符串里的块注释标记不被去掉()
    {
        JObject o = JsoncReader.Parse("{\"a\": \"x /* keep */ y\"}");
        Assert.Equal("x /* keep */ y", o["a"]!.Value<string>());
    }

    [Fact]
    public void 转义引号不提前结束字符串()
    {
        JObject o = JsoncReader.Parse("{\"a\": \"he said \\\"hi\\\"\"}");
        Assert.Equal("he said \"hi\"", o["a"]!.Value<string>());
    }

    [Fact]
    public void 单引号不当作字符串定界符()
    {
        // JSONC 不支持单引号；这里确认实现不会误判
        JObject o = JsoncReader.Parse("{\"a\": \"it's\"}");
        Assert.Equal("it's", o["a"]!.Value<string>());
    }
}
