using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class LocationTypeSanitizerTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvloc_s_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvloc_o_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (string d in new[] { _src, _out })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    [Fact]
    public void DLL类型退化成原版GameLocation_原版类型不动()
    {
        Directory.CreateDirectory(Path.Combine(_src, "Data"));
        File.WriteAllText(Path.Combine(_src, "Data", "Locations.json"), """
        {
          "Custom_CapeKeyMaze": { "CreateOnLoad": { "Type": "CapeStardewCode.CustomLavaLogic, CapeStardewCode" } },
          "Town": { "Type": "StardewValley.GameLocation" },
          "NoType": { }
        }
        """);

        var output = new OutputTree(_src, _out);
        int fixedCount = LocationTypeSanitizer.Sanitize(output);
        output.Flush();

        Assert.Equal(1, fixedCount);
        JObject after = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Locations.json")));
        Assert.Equal("StardewValley.GameLocation",
            after["Custom_CapeKeyMaze"]!["CreateOnLoad"]!.Value<string>("Type"));
        Assert.Equal("StardewValley.GameLocation", after["Town"]!.Value<string>("Type"));
        Assert.Null(after["NoType"]!["Type"]);
    }
}
