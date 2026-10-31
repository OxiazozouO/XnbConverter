using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class ImageApplierTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvis_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvio_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvip_" + Guid.NewGuid().ToString("N"));

    private const string ImageConfig =
        """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0" } ] }""";

    private const string DataConfig =
        """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.String, mscorlib],[System.String, mscorlib]]" } ] }""";

    public ImageApplierTests()
    {
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_pack);
    }

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路，图片的落盘权又在 `ImageCache` 手里。</summary>
    private void Flush()
    {
        _images?.Flush();
        _tree?.Flush();
    }

    public void Dispose()
    {
        Directory.Delete(_src, recursive: true);
        Directory.Delete(_pack, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void Tree(string rel, string content)
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void TreeBytes(string rel, byte[] bytes)
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    private void PackBytes(string rel, byte[] bytes)
    {
        string full = Path.Combine(_pack, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    private OutputTree? _tree;
    private OutputTree OutTree() => _tree ??= new OutputTree(_src, _out);

    /// <summary>图片内存模型 —— 图片只在 `Images.Flush()` 时才落盘。</summary>
    private ImageCache? _images;
    private ImageCache Images => _images ??= new ImageCache(OutTree());

    private ImageApplier Build() => new(OutTree(), _pack, Images);

    /// <summary>
    /// ★ 图片 `Load` 要**镜像到已存在的语言变体**：中文存档加载的是 `asset.zh-CN`
    /// （`LocalizedContentManager` 先要带语言后缀的那份），只改基础资产玩家看不到。
    /// 实测（2026-10-19）：主界面 `Minigames/TitleButtons.zh-CN` 一直没被替换。
    /// </summary>
    [Fact]
    public void 图片Load镜像到已存在的语言变体()
    {
        Tree("Minigames/TitleButtons.config", ImageConfig);
        TreeBytes("Minigames/TitleButtons.png", new byte[] { 1, 1, 1 });
        Tree("Minigames/TitleButtons.zh-CN.config", ImageConfig);
        TreeBytes("Minigames/TitleButtons.zh-CN.png", new byte[] { 2, 2, 2 });

        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF };
        PackBytes("assets/title.png", png);

        var r = Build().Apply("Minigames/TitleButtons", "assets/title.png");
        Assert.True(r.Success, r.FailureReason);
        Images.Flush();
        Flush();

        Assert.Equal(png, File.ReadAllBytes(Path.Combine(_out, "Minigames", "TitleButtons.png")));
        Assert.Equal(png, File.ReadAllBytes(Path.Combine(_out, "Minigames", "TitleButtons.zh-CN.png")));
    }

    [Fact]
    public void 逐字节复制png并补一份图片config()
    {
        Tree("Characters/Abigail.config", ImageConfig);
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0x10, 0x7F, 0x00 };
        PackBytes("Assets/Spirits/RingSpirit1.png", png);

        var r = Build().Apply("Characters/RSVSpirits/RingSpirit1", "Assets/Spirits/RingSpirit1.png", "Characters/RSVSpirits/RingSpirit1");

        Assert.True(r.Success);
        Images.Flush();
        // 逐字节一致，绝不重新编码
        Flush();
        Assert.Equal(png, File.ReadAllBytes(Path.Combine(_out, "Characters", "RSVSpirits", "RingSpirit1.png")));
        // 配套 .config 从同类图片资产复制，Reader 必须是 Texture2DReader
        string config = File.ReadAllText(Path.Combine(_out, "Characters", "RSVSpirits", "RingSpirit1.config"));
        Assert.Contains("Texture2DReader", config);
    }

    /// <summary>
    /// ★ 图片**不许盖掉另一类资产**：XNB 的类型由 `.config` 说了算，盖成 `Texture2DReader` 之后
    /// 游戏会拿贴图当地图 / 数据用 —— 实测 `Maps/Coop` 就是这么坏的（进鸡舍就炸）。
    /// 宁可整条失败，也不静默写出坏树。
    /// </summary>
    [Fact]
    public void 目标处已有另一类资产时拒绝覆盖()
    {
        Tree("Maps/Coop.config", """{ "Readers": [ { "Type": "xTile.Pipeline.TideReader, xTile", "Version": 0 } ] }""");
        TreeBytes("Maps/Coop.tbin", new byte[] { 0x74, 0x42, 0x49, 0x4E, 0x31, 0x30 });
        Tree("Characters/Abigail.config", ImageConfig);         // 有可用的图片 .config 模板
        PackBytes("Assets/Foo.png", new byte[] { 1, 2, 3 });

        var r = Build().Apply("Maps/Coop", "Assets/Foo.png", "Maps/Coop");

        Assert.False(r.Success);
        Assert.Contains("非图片资产", r.FailureReason);
        Flush();
        Assert.False(File.Exists(Path.Combine(_out, "Maps", "Coop.png")));
        // 描述符**没被换成贴图那份**（产出树开局是输入树的拷贝，所以这份文件本来就在）
        Assert.Contains("TideReader", File.ReadAllText(Path.Combine(_out, "Maps", "Coop.config")));
    }

    [Fact]
    public void 目标已存在时只覆盖png不重复记新建()
    {
        Tree("Characters/Abigail.config", ImageConfig);
        TreeBytes("Characters/Abigail.png", new byte[] { 1, 2, 3 });
        PackBytes("Assets/New.png", new byte[] { 9, 8, 7 });

        var output = _tree = new OutputTree(_src, _out);
        var images = new ImageCache(output);
        var r = new ImageApplier(output, _pack, images).Apply("Characters/Abigail", "Assets/New.png", "Characters/Abigail");
        images.Flush();

        Assert.True(r.Success);
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(Path.Combine(_out, "Characters", "Abigail.png")));
        Assert.Empty(output.CreatedFiles);   // 覆盖已有资产不算新建，ContentHashes 不重复补 key
    }

    [Fact]
    public void 新建资产没有图片config模板时失败且不落盘()
    {
        // 树里只有数据资产的 .config（无 Texture2DReader），不能拿来给图片当模板
        Tree("Data/Locations.config", DataConfig);
        PackBytes("Assets/New.png", new byte[] { 1, 2, 3 });

        var r = Build().Apply("LooseSprites/NewSprite", "Assets/New.png", "LooseSprites/NewSprite");

        Assert.False(r.Success);
        Assert.Contains("Texture2DReader", r.FailureReason);
        Assert.False(File.Exists(Path.Combine(_out, "LooseSprites", "NewSprite.png")));
    }

    [Fact]
    public void FromFile不是png时失败()
    {
        Tree("Characters/Abigail.config", ImageConfig);
        PackBytes("Assets/Base.json", new byte[] { 1 });

        var r = Build().Apply("Characters/New", "Assets/Base.json", "Characters/New");

        Assert.False(r.Success);
        Assert.Contains("不是图片资产", r.FailureReason);
    }

    [Fact]
    public void 替换TargetWithoutPath字段引用令牌()
    {
        Tree("Characters/Abigail.config", ImageConfig);
        PackBytes("Assets/EventFiles/RSVSpirit1.png", new byte[] { 4, 5, 6 });

        // FromFile 相对内容包根：{{TargetWithoutPath}} 展开成 Target 最后一段 `RSVSpirit1`
        var r = Build().Apply("Characters/EventFiles/RSVSpirit1", "Assets/EventFiles/{{TargetWithoutPath}}.png",
            "Characters/EventFiles/RSVSpirit1");

        Assert.True(r.Success);
        Flush();
        Assert.True(File.Exists(Path.Combine(_out, "Characters", "EventFiles", "RSVSpirit1.png")));
    }

    [Fact]
    public void 含无法静态解析的令牌时失败()
    {
        Tree("Characters/Abigail.config", ImageConfig);

        var r = Build().Apply("Characters/New", "Assets/EventFiles/RSVSpirit{{Random:1,2}}.png", "Characters/New");

        Assert.False(r.Success);
        Assert.Contains("令牌", r.FailureReason);
    }

    [Fact]
    public void 找不到FromFile时失败()
    {
        Tree("Characters/Abigail.config", ImageConfig);

        var r = Build().Apply("Characters/New", "Assets/nope.png", "Characters/New");

        Assert.False(r.Success);
        Assert.Contains("找不到", r.FailureReason);
    }

    // ---------------------------------------------------------------- 坏路径降级（Task 16）

    [Fact]
    public void 目标资产名含星号时返回失败而不抛异常()
    {
        PackBytes("Assets/New.png", new byte[] { 1, 2, 3 });

        // SVE 的 `Portraits/**Martin**` 就是这一形态：图片 Load 也不得因坏路径抛异常
        var r = Build().Apply("Portraits/**Martin**", "Assets/New.png", "Portraits/**Martin**");

        Assert.False(r.Success);
        Assert.Contains("非法字符", r.FailureReason);
    }
}
