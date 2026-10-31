using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class VanillaAssetIndexTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvvai_" + Guid.NewGuid().ToString("N"));

    public VanillaAssetIndexTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteHashes(string json)
    {
        string path = Path.Combine(_dir, "ContentHashes.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void 识别原版资产名()
    {
        string p = WriteHashes("""
        { "Maps/Town.xnb": "aaa", "Data/Locations.xnb": "bbb" }
        """);

        var index = new VanillaAssetIndex(p);

        Assert.True(index.Contains("Maps/Town"));
        Assert.True(index.Contains("Data/Locations"));
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void 非原版资产名返回false()
    {
        string p = WriteHashes("""{ "Maps/Town.xnb": "aaa" }""");

        var index = new VanillaAssetIndex(p);

        Assert.False(index.Contains("Maps/Custom_Ridgeside_RSVCliff"));
        Assert.False(index.Contains("Data/Festivals/winter28"));
    }

    [Fact]
    public void 反斜杠与斜杠等价且大小写不敏感()
    {
        string p = WriteHashes("""{ "Maps/Town.xnb": "aaa" }""");

        var index = new VanillaAssetIndex(p);

        Assert.True(index.Contains("Maps\\Town"));
        Assert.True(index.Contains("maps/town"));
    }

    [Fact]
    public void 文件不存在时构造抛异常()
    {
        Assert.Throws<FileNotFoundException>(
            () => new VanillaAssetIndex(Path.Combine(_dir, "nope.json")));
    }
}
