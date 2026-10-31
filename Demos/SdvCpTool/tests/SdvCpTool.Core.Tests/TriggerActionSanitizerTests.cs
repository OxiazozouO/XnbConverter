using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class TriggerActionSanitizerTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvta_s_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvta_o_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (string d in new[] { _src, _out })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    [Fact]
    public void 大写ID去重_单条Action也校验_自定义动作整条删除()
    {
        Directory.CreateDirectory(Path.Combine(_src, "Data"));
        File.WriteAllText(Path.Combine(_src, "Data", "TriggerActions.json"), """
        [
          { "ID": "CT.Dup", "Trigger": "LocationChanged", "Action": "AddConversationTopic A 1" },
          { "ID": "CT.Dup", "Trigger": "LocationChanged", "Action": "AddConversationTopic A 1" },
          { "Id": "GiEX.Tank", "Trigger": "DayStarted", "Action": "GiEX.AddFishTank GiEX_Aquarium 17 13 2322" },
          { "Id": "Good.Array", "Trigger": "DayStarted",
            "Actions": [ "AddMail Current Letter", "SomeMod.Custom" ] }
        ]
        """);
        File.WriteAllText(Path.Combine(_src, "Data", "TriggerActions.zh-CN.json"), """
        [
          { "ID": "CT.LocaleDup", "Trigger": "DayStarted", "Action": "AddMail Current Letter" },
          { "ID": "CT.LocaleDup", "Trigger": "DayStarted", "Action": "AddMail Current Letter" }
        ]
        """);

        var output = new OutputTree(_src, _out);
        int fixedCount = TriggerActionSanitizer.Sanitize(output);
        output.Flush();

        Assert.Equal(4, fixedCount);
        JArray after = JArray.Parse(File.ReadAllText(Path.Combine(_out, "Data", "TriggerActions.json")));
        Assert.Equal(2, after.Count);
        Assert.Single(after, e => e.Value<string>("ID") == "CT.Dup");
        JObject arrayEntry = Assert.Single(after.OfType<JObject>(),
            e => e.GetValue("Id", StringComparison.OrdinalIgnoreCase)!.ToString() == "Good.Array");
        Assert.Single((JArray)arrayEntry["Actions"]!);
        Assert.Equal("AddMail Current Letter", arrayEntry["Actions"]![0]!.ToString());

        JArray localized = JArray.Parse(File.ReadAllText(Path.Combine(_out, "Data", "TriggerActions.zh-CN.json")));
        Assert.Single(localized);
    }
}
