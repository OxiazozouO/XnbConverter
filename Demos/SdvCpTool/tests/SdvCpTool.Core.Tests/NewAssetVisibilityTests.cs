using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 2026-10-06 架构 bug：<c>Load</c> 写出的新资产对判定不可见。
/// <para>
/// 铁证：<c>Data/Extras/RSVMap.json</c> idx1 的 <c>Load LooseSprites/RSVIcon</c> → <c>written: true</c>，
/// 紧随其后的 idx2 <c>EditImage LooseSprites/RSVIcon</c> → <c>TargetMissing</c>。
/// 根因：判定查询的是构造时一次性建立的输入树，写入落在产出树 —— 两棵树，判定那棵运行中从不更新。
/// 修法：<see cref="ResourceTree.RegisterCreatedAssets"/> 把产出树的新建资产登记进判定视图，
/// 判定与写入共用这一个存在性视图，不各维护一份清单。
/// </para>
/// </summary>
public class NewAssetVisibilityTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvvis_src_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvvis_out_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvvis_pack_" + Guid.NewGuid().ToString("N"));

    public NewAssetVisibilityTests()
    {
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_pack);
    }

    private OutputTree? _tree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush()
    {
        _tree?.Flush();
    }

    public void Dispose()
    {
        Directory.Delete(_src, recursive: true);
        Directory.Delete(_pack, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void Tree(string relative, string content)
    {
        string full = Path.Combine(_src, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void PackFile(string relative, string content)
    {
        string full = Path.Combine(_pack, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static ChangePatch Patch(string json)
    {
        var raw = JObject.Parse(json);
        return new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            FromFile = raw["FromFile"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = "content.json",
        };
    }

    [Fact]
    public void 未登记时判定仍然报TargetMissing()
    {
        // 负例基线：不登记新建资产，判定只看输入树 —— 旧行为就是 TargetMissing。
        Tree("Data/Locations.json", "{}");

        PatchVerdict v = new PatchJudge(new ResourceTree(_src), _pack)
            .Judge(Patch("""{ "Action": "EditImage", "Target": "LooseSprites/Icon" }"""));

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
    }

    [Fact]
    public void Load新建的图片资产对EditImage判定可见()
    {
        // 复现 RSVMap 的形状：Load 出一张图，紧接着 EditImage 同一资产。
        Tree("Characters/Grandpa.png", "PNG");
        Tree("Characters/Grandpa.config", """{ "Readers": [ { "Type": "Texture2DReader" } ] }""");
        PackFile("icon.png", "PNG");

        var tree = new ResourceTree(_src);
        var output = _tree = new OutputTree(_src, _out);
        var images = new ImageCache(output);
        Assert.True(new ImageApplier(output, _pack, images).Apply("LooseSprites/Icon", "icon.png").Success);
        images.Flush();

        tree.RegisterCreatedAssets(output);

        PatchVerdict v = new PatchJudge(tree, _pack)
            .Judge(Patch("""{ "Action": "EditImage", "Target": "LooseSprites/Icon" }"""));

        Assert.NotEqual(Verdict.TargetMissing, v.Verdict);
        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void Load新建的数据资产对EditData判定可见()
    {
        Tree("Data/Locations.json", """{ "Town": {} }""");
        Tree("Data/Locations.config", "{}");
        PackFile("newdata.json", """{ "a": "b" }""");

        var tree = new ResourceTree(_src);
        var output = _tree = new OutputTree(_src, _out);
        Assert.True(new LoadApplier(output, _pack, images: new ImageCache(output)).Apply("Data/NewThing", "newdata.json").Success);

        tree.RegisterCreatedAssets(output);

        PatchVerdict v = new PatchJudge(tree, _pack).Judge(Patch(
            """{ "Action": "EditData", "Target": "Data/NewThing", "Entries": { "x": "y" } }"""));

        Assert.NotEqual(Verdict.TargetMissing, v.Verdict);
        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void 登记后输入树里的资产照旧可见()
    {
        Tree("Data/Town.json", "{}");

        var tree = new ResourceTree(_src);
        var output = _tree = new OutputTree(_src, _out);
        tree.RegisterCreatedAssets(output);

        Assert.NotNull(tree.Resolve("Data/Town"));
    }

    [Fact]
    public void config不会被当成资产()
    {
        Tree("Data/Town.json", "{}");

        var tree = new ResourceTree(_src);
        var output = _tree = new OutputTree(_src, _out);
        output.WriteText("Data/Town.config", "{}");
        output.CreatedFiles.Add("Data/Town.config");

        tree.RegisterCreatedAssets(output);

        // .config 不是资产本体，登记时跳过；同名资产名（Data/Town.config）不该可解析
        Assert.Null(tree.Resolve("Data/Town.config"));
    }
}
