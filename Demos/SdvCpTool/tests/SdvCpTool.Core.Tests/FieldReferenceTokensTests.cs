using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

public class FieldReferenceTokensTests
{
    [Fact]
    public void 纯文本展开按目标()
    {
        Assert.Equal("assets/Characters/Monsters/FallenAdventurerFemale.png",
            FieldReferenceTokens.Expand("assets/Characters/Monsters/{{TargetWithoutPath}}.png",
                fromFile: null, target: "Characters/Monsters/FallenAdventurerFemale"));
    }

    /// <summary>
    /// ★ <c>{{FromFile}}</c> 展开出来的东西**自己还带令牌**时要再展开一遍。
    /// 实测 SVE 季节性服装的写法：FromFile 里是 <c>{{TargetWithoutPath}}</c>，
    /// When 里是 <c>HasFile:{{FromFile}}</c> —— 不套两层的话换出来还是令牌串，
    /// <c>File.Exists</c> 必然假，10 条 EditImage 全部判「不知道」。
    /// </summary>
    [Fact]
    public void FromFile自身带令牌时也要展开()
    {
        Assert.Equal("assets/Characters/Monsters/B.png",
            FieldReferenceTokens.Expand("{{FromFile}}",
                fromFile: "assets/Characters/Monsters/{{TargetWithoutPath}}.png",
                target: "Characters/Monsters/B"));
    }

    [Fact]
    public void 树展开按指定目标()
    {
        var patch = new SdvCpTool.Core.Cp.ChangePatch
        {
            Target = "Characters/A, Characters/B",
            FromFile = "assets/Characters/Monsters/{{TargetWithoutPath}}.png",
        };
        WhenNode when = WhenNode.All(WhenNode.Key("HasFile", "assets/Characters/Monsters/{{TargetWithoutPath}}.png", "true"));

        WhenNode expanded = FieldReferenceTokens.Expand(when, patch, "Characters/Monsters/B");
        WhenNode leaf = expanded.Descendants().First(n => n.Kind == WhenNodeKind.Key);

        Assert.Equal("assets/Characters/Monsters/B.png", leaf.Arguments);
    }

    /// <summary>
    /// ★ 真实形状：When 的键参数是 <c>{{FromFile}}</c>，而 FromFile **自己也带** <c>{{TargetWithoutPath}}</c>。
    /// 实测 SVE 季节性服装十条 EditImage 就是这个形状。
    /// </summary>
    [Fact]
    public void When用FromFile而FromFile自带令牌()
    {
        var patch = new SdvCpTool.Core.Cp.ChangePatch
        {
            Target = "Characters/Monsters/FallenAdventurerFemale, Characters/Monsters/FallenAdventurerMale",
            FromFile = "assets/Characters/Monsters/{{TargetWithoutPath}}.png",
        };
        WhenNode when = WhenNode.All(WhenNode.Key("HasFile", "{{FromFile}}", "true"));

        WhenNode expanded = FieldReferenceTokens.Expand(when, patch, "Characters/Monsters/FallenAdventurerFemale");
        WhenNode leaf = expanded.Descendants().First(n => n.Kind == WhenNodeKind.Key);

        Assert.Equal("assets/Characters/Monsters/FallenAdventurerFemale.png", leaf.Arguments);
    }
}
