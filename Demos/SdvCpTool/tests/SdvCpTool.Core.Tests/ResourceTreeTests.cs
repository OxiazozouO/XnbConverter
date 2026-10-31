using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class ResourceTreeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvtree_" + Guid.NewGuid().ToString("N"));

    public ResourceTreeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Touch(string relative)
    {
        string full = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "{}");
    }

    [Fact]
    public void 解析json资产()
    {
        Touch("Data/Town.json");
        Touch("Data/Town.config");

        var tree = new ResourceTree(_dir);

        Assert.Equal(Path.Combine(_dir, "Data", "Town.json"), tree.Resolve("Data/Town"));
    }

    [Fact]
    public void 反斜杠与斜杠等价()
    {
        Touch("Data/Events/Town.json");

        var tree = new ResourceTree(_dir);

        Assert.NotNull(tree.Resolve("Data\\Events\\Town"));
    }

    [Fact]
    public void 解析tbin资产()
    {
        Touch("Maps/Town.tbin");

        var tree = new ResourceTree(_dir);

        Assert.EndsWith("Town.tbin", tree.Resolve("Maps/Town"));
    }

    [Fact]
    public void 本地化版本不会干扰基础资产()
    {
        Touch("Data/Events/Town.json");
        Touch("Data/Events/Town.zh-CN.json");

        var tree = new ResourceTree(_dir);

        Assert.DoesNotContain("zh-CN", tree.Resolve("Data/Events/Town")!);
    }

    [Fact]
    public void 不回退到config文件()
    {
        Touch("Data/Town.config");

        var tree = new ResourceTree(_dir);

        Assert.Null(tree.Resolve("Data/Town"));
    }

    [Fact]
    public void 找不到返回null()
    {
        var tree = new ResourceTree(_dir);

        Assert.Null(tree.Resolve("Data/NoSuchAsset"));
    }

    [Fact]
    public void 资产名大小写不敏感()
    {
        Touch("Data/Town.json");

        var tree = new ResourceTree(_dir);

        Assert.NotNull(tree.Resolve("data/town"));
    }

    [Fact]
    public void 目录不存在时抛异常()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new ResourceTree(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public void 相对路径集合使用正斜杠()
    {
        Touch("Data/Town.json");
        Touch("Maps/Town.tbin");

        var tree = new ResourceTree(_dir);

        Assert.Contains("Data/Town.json", tree.RelativeFiles);
        Assert.Contains("Maps/Town.tbin", tree.RelativeFiles);
    }
}
