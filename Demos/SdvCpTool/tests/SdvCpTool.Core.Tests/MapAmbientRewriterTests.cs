using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Maps;

namespace SdvCpTool.Core.Tests;

public class MapAmbientRewriterTests
{
    [Fact]
    public void 白天写AmbientLight()
    {
        JObject raw = JObject.Parse("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "MapProperties": { "AmbientLight": "255 255 255" },
          "When": { "Time": "600 1900" } }
        """);

        JObject result = MapAmbientRewriter.Rewrite(raw, visibleAtDay: true)!;

        Assert.Equal("255 255 255", result["MapProperties"]!["AmbientLight"]!.Value<string>());
        Assert.Null(result["When"]);
        Assert.Null(result["MapProperties"]!["AmbientNightLight"]);
    }

    [Fact]
    public void 夜间写AmbientNightLight()
    {
        JObject raw = JObject.Parse("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "MapProperties": { "AmbientLight": "110 110 110" },
          "When": { "Time": "1900 2600" } }
        """);

        JObject result = MapAmbientRewriter.Rewrite(raw, visibleAtDay: false)!;

        Assert.Equal("110 110 110", result["MapProperties"]!["AmbientNightLight"]!.Value<string>());
        Assert.Null(result["MapProperties"]!["AmbientLight"]);
        Assert.Null(result["When"]);
    }
}
