using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Frameworks;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// SpaceCore <c>ObjectExtensionData</c> 的原版字段子集：
/// 只映射 `ObjectData` 真读的 CanBeTrashed / CanBeGivenAsGift / 进出货箱，其余只报丢失。
/// </summary>
public class SpaceCoreObjectExtensionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spaceobj_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void 映射可承载的布尔字段()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "content.json"), """
            {
              "Changes": [
                {
                  "Action": "EditData",
                  "Target": "spacechase0.SpaceCore/ObjectExtensionData",
                  "Entries": {
                    "{{ModID}}_shield": {
                      "CanBeTrashed": false,
                      "CanBeGivenAsGift": false,
                      "CanBeShipped": false,
                      "HideFromShippingCollection": true,
                      "MaxStackSizeOverride": 1,
                      "CategoryTextOverride": "Shield"
                    }
                  }
                }
              ]
            }
            """);
        var objects = new JObject
        {
            ["test.mod_shield"] = new JObject { ["Name"] = "Shield" },
        };
        var notes = new List<string>();

        int n = SpaceCoreObjectExtensions.Apply(objects, _root, "test.mod", notes);

        Assert.Equal(1, n);
        JObject item = (JObject)objects["test.mod_shield"]!;
        Assert.False(item.Value<bool>("CanBeTrashed"));
        Assert.False(item.Value<bool>("CanBeGivenAsGift"));
        Assert.True(item.Value<bool>("ExcludeFromShippingCollection"));
        Assert.Contains(notes, x => x.Contains("MaxStackSizeOverride"));
        Assert.Contains(notes, x => x.Contains("CategoryTextOverride"));
    }

    [Fact]
    public void 目标对象不存在只记原因不新建()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "data.json"), """
            {
              "Changes": [
                { "Action": "EditData",
                  "Target": "spacechase0.SpaceCore/ObjectExtensionData",
                  "Entries": { "{{ModID}}_missing": { "CanBeTrashed": false } } }
              ]
            }
            """);
        var objects = new JObject();
        var notes = new List<string>();

        Assert.Equal(0, SpaceCoreObjectExtensions.Apply(objects, _root, "test.mod", notes));
        Assert.Empty(objects.Properties());
        Assert.Contains(notes, x => x.Contains("test.mod_missing"));
    }

    [Fact]
    public void Entries里的Target数组不触发类型转换异常()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "content.json"), """
            {
              "Changes": [
                {
                  "Action": "EditData",
                  "Target": "spacechase0.SpaceCore/ObjectExtensionData",
                  "Entries": {
                    "{{ModID}}_x": {
                      "Target": [ "Data/map", "MapProperties" ],
                      "CanBeTrashed": false
                    }
                  }
                }
              ]
            }
            """);

        var notes = new List<string>();
        int n = SpaceCoreObjectExtensions.Apply(new JObject(), _root, "test.mod", notes);

        Assert.Equal(0, n);   // 目标对象不存在，但不应抛 InvalidCastException
        Assert.Contains(notes, x => x.Contains("test.mod_x"));
    }

    [Fact]
    public void 未展开令牌不猜()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "data.json"), """
            {
              "Changes": [
                { "Action": "EditData",
                  "Target": "spacechase0.SpaceCore/ObjectExtensionData",
                  "Entries": { "{{OtherToken}}_x": { "CanBeTrashed": false } } }
              ]
            }
            """);
        var notes = new List<string>();

        Assert.Equal(0, SpaceCoreObjectExtensions.Apply(new JObject(), _root, "test.mod", notes));
        Assert.Contains(notes, x => x.Contains("未展开令牌"));
    }
}
