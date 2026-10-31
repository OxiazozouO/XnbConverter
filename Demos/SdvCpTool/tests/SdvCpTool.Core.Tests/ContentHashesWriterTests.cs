using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class ContentHashesWriterTests
{
    [Fact]
    public void 补入新键()
    {
        var hashes = JObject.Parse("""{ "Maps/Town.xnb": "aaa" }""");

        ContentHashesWriter.AddKeys(hashes, new[] { "Data/Festivals/winter28.json" });

        Assert.True(hashes.ContainsKey("Data/Festivals/winter28.xnb"));
        Assert.Equal("aaa", hashes["Maps/Town.xnb"]!.Value<string>());
    }

    [Fact]
    public void 图片资产同样补键()
    {
        var hashes = JObject.Parse("""{ "Maps/Town.xnb": "aaa" }""");

        ContentHashesWriter.AddKeys(hashes, new[] { "Characters/RSVSpirits/RingSpirit1.png" });

        Assert.True(hashes.ContainsKey("Characters/RSVSpirits/RingSpirit1.xnb"));
        Assert.Equal("", hashes["Characters/RSVSpirits/RingSpirit1.xnb"]!.Value<string>());
    }

    /// <summary>地图源文件 `.tbin` 回包后也是 `.xnb`；漏掉会导致新建地图没有 ContentHashes 键。</summary>
    [Fact]
    public void 地图资产同样补键()
    {
        var hashes = JObject.Parse("""{ "Maps/Town.xnb": "aaa" }""");

        ContentHashesWriter.AddKeys(hashes, new[] { "Maps/Custom_LavenderMeadows_Spa.tbin" });

        Assert.True(hashes.ContainsKey("Maps/Custom_LavenderMeadows_Spa.xnb"));
    }

    [Fact]
    public void 已存在的键不重复添加()
    {
        var hashes = JObject.Parse("""{ "Maps/Town.xnb": "aaa" }""");

        ContentHashesWriter.AddKeys(hashes, new[] { "Maps/Town.json" });

        Assert.Single(hashes);
        Assert.Equal("aaa", hashes["Maps/Town.xnb"]!.Value<string>());   // 原值不变
    }

    /// <summary>已存在的键仅大小写不同也算同一个资产，不能加出第二条。</summary>
    [Fact]
    public void 大小写不同视为已有键()
    {
        var hashes = JObject.Parse("""{ "Characters/Schedules/Nora.xnb": "aaa" }""");

        ContentHashesWriter.AddKeys(hashes, new[] { "Characters/schedules/Nora.json" });

        Assert.Single(hashes);
    }

    [Fact]
    public void 非xnb文件被忽略()
    {
        var hashes = JObject.Parse("""{ "Maps/Town.xnb": "aaa" }""");

        ContentHashesWriter.AddKeys(hashes, new[] { "Data/Some.config" });

        Assert.Single(hashes);
    }

    [Fact]
    public void 值为空串()
    {
        // 键的存在性才是判据；值（MD5）填什么不影响 DoesAssetExist
        var hashes = new JObject();

        ContentHashesWriter.AddKeys(hashes, new[] { "Data/New.json" });

        Assert.Equal("", hashes["Data/New.xnb"]!.Value<string>());
    }
}
