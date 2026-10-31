using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// **服装这条通道**：`Data/Shirts`（同构的还有 `Data/Pants` / `Data/HairData` / `Data/hats`）
/// 在 1.6 是数据化的 —— 条目自带 <c>Texture</c> + <c>SpriteIndex</c>，也就是
/// 「一条数据 + 一张专属贴图」是**原版原生支持**的，不需要任何框架运行时。
/// <para>
/// 为什么单独钉一条：**当前 32 包语料里服装一条都没有**（报告与 <c>Mods/</c> 全零命中），
/// 而这条通道恰恰是「将来遇到服装框架包」时唯一现成的落点（见
/// <c>docs/research/2026-10-11-non-cp-frameworks-what-can-bake.md</c>）。
/// 端到端（转 → 打包 → 解包读回）已经手工跑过一遍，这里只钉**转**这一侧：
/// 两件事必须同时成立，少一件游戏就取不到图 ——
/// ① `Data/Shirts` 多出那条、且 <c>Texture</c> 指向新资产名；
/// ② 新贴图落进产出树，**并且带上 <c>Texture2DReader</c> 的 <c>.config</c>**
///    （没有它打包器会**静默跳过**那张图，见 `2026-10-09-packer-silent-skips.md`）。
/// </para>
/// </summary>
public class ClothingChannelTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvcl_s_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvcl_p_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvcl_o_" + Guid.NewGuid().ToString("N"));

    private OutputTree? _tree;
    private ImageCache? _images;

    public ClothingChannelTests() => Directory.CreateDirectory(_src);

    public void Dispose()
    {
        foreach (string d in new[] { _src, _pack, _out })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private void Tree(string rel, string content)
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void Pack(string rel, byte[] content)
    {
        string full = Path.Combine(_pack, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
    }

    [Fact]
    public void 新增衬衫条目_带专属贴图_两件事都落进产出树()
    {
        // 原版树里要有两样模板：服装数据资产本身，以及**同一目录**的一张图片 .config
        Tree("Data/Shirts.json", """{ "1000": { "Name": "Classic Overalls" } }""");
        Tree("Data/Shirts.config",
            """{ "Readers": [ { "Type": "StardewValley.GameData.Shirts.ShirtData, StardewValley.GameData", "Version": 0 } ] }""");
        Tree("Characters/Farmer/shirts.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");

        // 贴图逐字节搬运、不重新编码 ⇒ 内容是什么无所谓，这里就用几个字节
        Pack("assets/TestShirt.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 });

        var output = _tree = new OutputTree(_src, _out);
        _images = new ImageCache(output);

        // 图片落在图片那条路上（生产里由 `WritePolicy.IsImageLoad` 分流，见 `Program.ApplyOneCore`）
        var images = new ImageApplier(output, _pack, _images);
        MergeOutcome loaded = images.Apply("Characters/Farmer/testsuit", "assets/TestShirt.png");
        Assert.True(loaded.Success, loaded.FailureReason);

        var applier = new PatchApplier(output);
        MergeOutcome edited = applier.ApplyEditData("Data/Shirts", """
            { "Entries": { "SdvCpTool_TestSuit": {
                "Name": "Test Suit", "DisplayName": "合成衬衫", "Description": "通道验证用",
                "Price": 10, "Texture": "Characters/Farmer/testsuit", "SpriteIndex": 0,
                "CanBeDyed": true, "HasSleeves": true } } }
            """);
        Assert.True(edited.Success, edited.FailureReason);

        _images.Flush();
        output.Flush();

        // ① 数据条目：新键 + Texture 指过去，原版那条不动
        JObject shirts = JObject.Parse(
            File.ReadAllText(Path.Combine(_out, "Data", "Shirts.json")));
        JToken suit = shirts["SdvCpTool_TestSuit"]!;
        Assert.Equal("Characters/Farmer/testsuit", suit["Texture"]!.Value<string>());
        Assert.Equal(0, suit["SpriteIndex"]!.Value<int>());
        Assert.Equal("Classic Overalls", shirts["1000"]!["Name"]!.Value<string>());

        // ② 贴图 + 它的 .config（少了 .config，打包器会**静默跳过**这张图）
        string png = Path.Combine(_out, "Characters", "Farmer", "testsuit.png");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 }, File.ReadAllBytes(png));
        Assert.Equal(
            File.ReadAllText(Path.Combine(_src, "Characters", "Farmer", "shirts.config")),
            File.ReadAllText(Path.Combine(_out, "Characters", "Farmer", "testsuit.config")));
    }
}
