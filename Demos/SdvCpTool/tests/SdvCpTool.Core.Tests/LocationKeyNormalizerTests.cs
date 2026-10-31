using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

public class LocationKeyNormalizerTests
{
    private static ChangePatch Patch(string json)
    {
        JObject raw = JObject.Parse(json);
        return new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            SourceFile = "content.json",
        };
    }

    /// <summary>FTM 写 `RailRoad`，树里的真名是 `Railroad`：必须归一到后者。</summary>
    [Fact]
    public void TargetField的地点名按已有键大小写归一()
    {
        var patch = Patch("""
            { "Action": "EditData", "Target": "Data/Locations",
              "TargetField": [ "RailRoad", "Forage" ], "Entries": {} }
            """);

        int n = LocationKeyNormalizer.Apply(new[] { patch }, JObject.Parse("""{ "Railroad": {} }"""));

        Assert.Equal(1, n);
        Assert.Equal("Railroad", patch.Raw["TargetField"]![0]!.Value<string>());
        Assert.Equal("Forage", patch.Raw["TargetField"]![1]!.Value<string>());
    }

    [Fact]
    public void 没有同名地点时不猜()
    {
        var patch = Patch("""
            { "Action": "EditData", "Target": "Data/Locations",
              "TargetField": [ "Custom_New", "Forage" ], "Entries": {} }
            """);

        Assert.Equal(0, LocationKeyNormalizer.Apply(new[] { patch }, JObject.Parse("""{ "Railroad": {} }""")));
        Assert.Equal("Custom_New", patch.Raw["TargetField"]![0]!.Value<string>());
    }
}
