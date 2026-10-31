using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// <c>EditImage</c> 的判定：判定与写入必须用<b>同一个</b> (Target × FromFile) 展开逐个组合
/// 确认 <c>FromFile</c> 能静态解析且存在 —— 否则判定放行、写入必失败（Task 10b 的教训）。
/// 条件只有在<b>折叠后</b>无条件时才可转（图片层没有通用条件落点）。
/// </summary>
public class EditImageJudgeTests : IDisposable
{
    private readonly string _packDir = Path.Combine(Path.GetTempPath(), "sdveipk_" + Guid.NewGuid().ToString("N"));
    private readonly string _treeDir = Path.Combine(Path.GetTempPath(), "sdveitr_" + Guid.NewGuid().ToString("N"));

    public EditImageJudgeTests()
    {
        Directory.CreateDirectory(_packDir);
        Directory.CreateDirectory(_treeDir);
    }

    public void Dispose()
    {
        Directory.Delete(_packDir, recursive: true);
        Directory.Delete(_treeDir, recursive: true);
    }

    private void Tree(string relative, string content)
    {
        string full = Path.Combine(_treeDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void PackFile(string relative, string content)
    {
        string full = Path.Combine(_packDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private PatchVerdict Judge(string patchJson, Func<WhenNode, bool?>? evaluateLeaf = null)
    {
        var raw = JObject.Parse(patchJson);
        var patch = new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            FromFile = raw["FromFile"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = Path.Combine(_packDir, "content.json"),
        };
        return new PatchJudge(new ResourceTree(_treeDir), _packDir, null, evaluateLeaf).Judge(patch);
    }

    [Fact]
    public void 无令牌的无条件EditImage可转换()
    {
        Tree("Characters/Abigail.png", "PNG");
        PackFile("Assets/Abigail.png", "PNG");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Characters/Abigail", "FromFile": "Assets/Abigail.png" }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void FromFile含Random令牌时不转()
    {
        Tree("Characters/Abigail.png", "PNG");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Characters/Abigail",
          "FromFile": "Assets/Icon_{{Random: 1, 2}}.png" }
        """);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
        Assert.Contains("令牌", v.Reason);
    }

    [Fact]
    public void FromFile文件不存在时不转()
    {
        Tree("Characters/Abigail.png", "PNG");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Characters/Abigail", "FromFile": "Assets/nope.png" }
        """);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
        Assert.Contains("不存在", v.Reason);
    }

    [Fact]
    public void 逗号多目标里任一FromFile缺失即整条不转()
    {
        Tree("Characters/A1.png", "PNG");
        Tree("Characters/A2.png", "PNG");
        PackFile("Assets/A1.png", "PNG");   // 只有 A1 的源文件

        var v = Judge("""
        { "Action": "EditImage", "Target": "Characters/A1, Characters/A2",
          "FromFile": "Assets/{{TargetWithoutPath}}.png" }
        """);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
        Assert.Contains("A2", v.Reason);
    }

    [Fact]
    public void 字段引用令牌按目标展开后可转()
    {
        Tree("Characters/RSVIcon.png", "PNG");
        PackFile("Assets/LooseSprites/RSVIcon.png", "PNG");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Characters/RSVIcon",
          "FromFile": "Assets/LooseSprites/{{TargetWithoutPath}}.png" }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void 没有FromFile的无条件EditImage保持旧判定()
    {
        // 既存判定（与 NewAssetVisibilityTests / PatchJudgeTests）不为 FromFile 缺失而改变；
        // 真正的把关在写入阶段（EditImageApplier 会因缺源图失败）。
        Tree("Characters/Abigail.png", "PNG");

        var v = Judge("""{ "Action": "EditImage", "Target": "Characters/Abigail" }""");

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void 有条件的EditImage仍判NoLandingPoint()
    {
        // 图片层没有通用条件落点，条件无法表达 —— 不得因为本次改动而误转。
        Tree("Characters/Abigail.png", "PNG");
        PackFile("Assets/Abigail.png", "PNG");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Characters/Abigail", "FromFile": "Assets/Abigail.png",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
    }

    [Fact]
    public void 条件折叠为真后无条件可转()
    {
        Tree("Characters/Abigail.png", "PNG");
        PackFile("Assets/Abigail.png", "PNG");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Characters/Abigail", "FromFile": "Assets/Abigail.png",
          "When": { "HasMod": "Some.Mod" } }
        """, _ => true);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void 条件折叠为假时不适用()
    {
        Tree("Characters/Abigail.png", "PNG");
        PackFile("Assets/Abigail.png", "PNG");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Characters/Abigail", "FromFile": "Assets/Abigail.png",
          "When": { "HasMod": "Some.Mod" } }
        """, _ => false);

        Assert.Equal(Verdict.NotApplicable, v.Verdict);
    }

    [Fact]
    public void Mask模式报错而不是静默降级()
    {
        Tree("Characters/Abigail.png", "PNG");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Characters/Abigail", "PatchMode": "Mask" }
        """);

        Assert.Equal(Verdict.UnsupportedAction, v.Verdict);
        Assert.Contains("Mask", v.Reason);
    }
}
