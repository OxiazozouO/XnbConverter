using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Frameworks;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// SpaceCore <c>CropExtensionData</c>：只有 RegrowDays 有原版单值槽位；
/// 多个不同 RegrowDays 必须报歧义，不能随便挑一个。
/// </summary>
public class SpaceCoreCropExtensionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spacecrop_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void WritePatch(string entries)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "data.json"), $$"""
            {
              "Changes": [
                { "Action": "EditData",
                  "Target": "spacechase0.SpaceCore/CropExtensionData",
                  "Entries": {{entries}} }
              ]
            }
            """);
    }

    [Fact]
    public void 唯一RegrowDays映射到Crops()
    {
        WritePatch("""
            { "{{ModID}}_seed": {
                "YieldOverrides": {
                  "4": { "RegrowDays": 7 },
                  "5": { "RegrowDays": 7 }
                }
            } }
            """);
        var crops = new JObject { ["test.mod_seed"] = new JObject { ["HarvestItemId"] = "Crop" } };
        var notes = new List<string>();

        int n = SpaceCoreCropExtensions.Apply(crops, _root, "test.mod", notes);

        Assert.Equal(1, n);
        Assert.Equal(7, ((JObject)crops["test.mod_seed"]!)["RegrowDays"]!.Value<int>());
        Assert.DoesNotContain(notes, x => x.Contains("多个值"));
    }

    [Fact]
    public void 多个不同RegrowDays报歧义且不改()
    {
        WritePatch("""
            { "{{ModID}}_seed": {
                "YieldOverrides": {
                  "4": { "RegrowDays": 3 },
                  "5": { "RegrowDays": 7 }
                }
            } }
            """);
        var crops = new JObject { ["test.mod_seed"] = new JObject() };
        var notes = new List<string>();

        Assert.Equal(0, SpaceCoreCropExtensions.Apply(crops, _root, "test.mod", notes));
        Assert.Null(((JObject)crops["test.mod_seed"]!)["RegrowDays"]);
        Assert.Contains(notes, x => x.Contains("多个值"));
    }

    [Fact]
    public void Entries里的Target数组不触发类型转换异常()
    {
        WritePatch("""
            { "{{ModID}}_seed": {
                "Target": [ "Data/map", "MapProperties" ],
                "RegrowDays": 7
            } }
            """);
        var notes = new List<string>();

        int n = SpaceCoreCropExtensions.Apply(new JObject(), _root, "test.mod", notes);

        Assert.Equal(0, n);
        Assert.Contains(notes, x => x.Contains("test.mod_seed"));
    }

    [Fact]
    public void 其它产出字段只报损失()
    {
        WritePatch("""
            { "{{ModID}}_seed": {
                "YieldOverrides": {
                  "4": { "RegrowDays": 5, "Drops": [ { "ItemId": "(O)X" } ] }
                },
                "ExperienceGained": 9
            } }
            """);
        var crops = new JObject { ["test.mod_seed"] = new JObject() };
        var notes = new List<string>();

        Assert.Equal(1, SpaceCoreCropExtensions.Apply(crops, _root, "test.mod", notes));
        Assert.Contains(notes, x => x.Contains("ExperienceGained"));
        Assert.Contains(notes, x => x.Contains("YieldOverrides"));
    }
}
