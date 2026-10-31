using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// **「无条件落图 + 图块门禁」这条替代通道的判据**（<see cref="ConditionPersistence"/>）。
/// <para>
/// 实测来源（2026-10-16）：`[CP] Integrated Minecarts` 的 `Maps/Farm` 两条补丁
/// —— `When: { Enablejibb.IF2R: true, HasFlag ccBoilerRoom: true, FarmType: Standard,
/// HasMod immersivefarm2remastered: true }` 往标准农场放矿车（`MapTiles` +
/// `SetProperties.Action = MinecartTransport …`）。原来判 `LocalEdit` 整条丢 ⇒ 农场上的矿车没有。
/// </para>
/// <para>
/// 替代方案：改动**无条件落图**，条件由 <c>TouchAction = ConditionalDoor &lt;补丁条件的 GSQ&gt;</c> 承担
/// （`GameLocation.cs:3593` 说 `TouchAction` 只在 `Back` 层读；`:3907` 说条件为假就把玩家弹回、
/// 为真则放行 ⇒ 门禁里要写**条件本身**，不是它的取反）。
/// </para>
/// </summary>
public class ConditionPersistenceTests
{
    private static WhenNode Key(string name, string? args = null, string? value = null) =>
        WhenNode.Key(name, args, value);

    /// <summary>★ 配置键**不是**拦阻：离线是冻结值，只会产生一个变体（实测 `Enablejibb.IF2R`）。</summary>
    [Fact]
    public void 配置键与包内令牌不拦()
    {
        WhenNode when = WhenNode.All(
            Key("Enablejibb.IF2R", value: "true"),
            Key("HasFlag", "anyPlayer |contains=ccBoilerRoom", "true"),
            Key("FarmType", value: "Standard"),
            Key("HasMod", "|contains=flashshifter.immersivefarm2remastered", "true"));

        Assert.True(ConditionPersistence.AllLeavesStable(when));
    }

    /// <summary>★ 轮流型要拦：同一片区域会各写一份，无条件全写会互相覆盖。</summary>
    [Theory]
    [InlineData("Weather")]
    [InlineData("Time")]
    [InlineData("Season")]
    [InlineData("Day")]
    [InlineData("DayOfWeek")]
    [InlineData("Random")]
    public void 轮流型条件不落门禁(string key)
    {
        Assert.True(ConditionPersistence.IsVolatile(key));
        Assert.False(ConditionPersistence.AllLeavesStable(WhenNode.All(Key(key, value: "x"))));
    }

    [Fact]
    public void 无条件的地图改动不走这条路()
    {
        Assert.False(ConditionPersistence.AllLeavesStable(null));
        Assert.False(ConditionPersistence.AllLeavesStable(WhenNode.All(WhenNode.Literal(true))));
    }
}
