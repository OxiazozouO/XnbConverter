using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// CP 的 <c>{{FirstValidFile: a, b}}</c>：取**第一个在包内存在的文件**。
/// 离线可定（文件集在一次转换里不变），但必须在 <c>{{TargetWithoutPath}}</c> 等字段令牌之后做。
/// 实测 RSV 的 <c>LooseSprites/RSVQuestBoard</c>：不解这个令牌 ⇒ 整条 Load 被判 TargetMissing，
/// 任务板 UI 直接不存在。
/// </summary>
public class FirstValidFileTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvfvf_" + Guid.NewGuid().ToString("N"));

    public FirstValidFileTests()
    {
        Directory.CreateDirectory(Path.Combine(_pack, "Assets", "LooseSprites"));
        File.WriteAllText(Path.Combine(_pack, "Assets", "LooseSprites", "Title_zh.png"), "x");
        File.WriteAllText(Path.Combine(_pack, "Assets", "LooseSprites", "Other_en.png"), "x");
    }

    public void Dispose() => Directory.Delete(_pack, recursive: true);

    [Fact]
    public void 取第一个存在的文件()
    {
        string text = "{{FirstValidFile: missing.png, Assets/LooseSprites/Title_zh.png, Other_en.png}}";

        Assert.Equal("Assets/LooseSprites/Title_zh.png",
            LoadApplier.ResolveFirstValidFile(text, _pack));
    }

    [Fact]
    public void 字段令牌先展开再判存在()
    {
        string expanded = LoadApplier.ExpandFieldTokens(
            "{{FirstValidFile: Assets/LooseSprites/{{TargetWithoutPath}}_zh.png, Other_en.png}}",
            "LooseSprites/Title");

        Assert.Equal("Assets/LooseSprites/Title_zh.png",
            LoadApplier.ResolveFirstValidFile(expanded, _pack));
    }

    [Fact]
    public void 一个都不存在时原样保留()
    {
        string text = "{{FirstValidFile: a.png, b.png}}";

        Assert.Equal(text, LoadApplier.ResolveFirstValidFile(text, _pack));
    }

    [Fact]
    public void 没有包根目录时不动()
    {
        string text = "{{FirstValidFile: Assets/LooseSprites/Title_zh.png}}";

        Assert.Equal(text, LoadApplier.ResolveFirstValidFile(text, null));
    }
}
