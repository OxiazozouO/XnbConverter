using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

public class InitialStateConditionsTests
{
    [Fact]
    public void 新存档进度条件为假()
    {
        Assert.False(InitialStateConditions.Evaluate(WhenNode.Key("HasSeenEvent", null, "2000009")));
        Assert.False(InitialStateConditions.Evaluate(WhenNode.Key("HasFlag", null, "SomeFlag")));
        Assert.False(InitialStateConditions.Evaluate(WhenNode.Key("Spouse", null, "Aideen")));
        Assert.False(InitialStateConditions.Evaluate(WhenNode.Key("IsCommunityCenterComplete")));
    }

    [Fact]
    public void contains取反的进度条件初始为真()
    {
        Assert.True(InitialStateConditions.Evaluate(
            WhenNode.Key("HasSeenEvent", "|contains=2000009", "false")));
        Assert.True(InitialStateConditions.Evaluate(
            WhenNode.Key("HasFlag", "hostPlayer |contains=SomeFlag", "false")));
    }

    [Fact]
    public void 组合条件按初始状态求值()
    {
        var when = WhenNode.All(
            WhenNode.Key("FarmType", null, "Standard"),
            WhenNode.Key("HasSeenEvent", null, "2000009"));

        // FarmType 由静态求值器判真；事件初始为假 ⇒ 整条为假。
        Assert.False(InitialStateConditions.Evaluate(when,
            node => node.KeyName == "FarmType" ? true : null));

        var notSeen = new WhenNode
        {
            Kind = WhenNodeKind.Not,
            Children = { WhenNode.Key("HasSeenEvent", null, "2000009") },
        };
        Assert.True(InitialStateConditions.Evaluate(notSeen));
    }
}
