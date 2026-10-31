using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// DLL 专有的事件前置条件 → 原版 GSQ（<see cref="EventPreconditions"/>）。
/// <para>
/// 实测来源（2026-10-18）：Sunberry Village 的 22 个事件 key 带
/// <c>!SunberryTeam.SBVSMAPI_IsPassiveFestivalToday</c>。原版对未知前置条件返回 false
/// 且**不处理 `!`**（`Event.cs:4197-4200`）⇒ 这些事件在纯 XNB 下永远不会触发。
/// 数据里的等效原版事件前置条件是 <c>!G IS_PASSIVE_FESTIVAL_TODAY skellady.SBVCP_TwilightFestival</c>
/// （`GameStateQuery.cs:384-392`，节日 ID 见 `Data/PassiveFestivals.json`）。
/// </para>
/// </summary>
public class EventPreconditionsTests
{
    private const string Old = "!SunberryTeam.SBVSMAPI_IsPassiveFestivalToday";
    private const string New = "!G IS_PASSIVE_FESTIVAL_TODAY skellady.SBVCP_TwilightFestival";

    [Fact]
    public void 事件key里的DLL前置条件换成原版GSQ()
    {
        var data = JObject.Parse($$"""
        {
          "Ripley_Monsters/f Clara.Ripley 500/t 900 1800/{{Old}}": "script",
          "skellady.SBVCP_20031471/t 900 1600/{{Old}}/!Weather rainy/f MiyoungSBV 1000": "script2"
        }
        """);

        Assert.Equal(2, EventPreconditions.Rewrite("Data/Events/Custom_SBV_Mines1", data));
        Assert.Contains(data.Properties(), p => p.Name.EndsWith(New, StringComparison.Ordinal));
        Assert.DoesNotContain(data.Properties(), p => p.Name.Contains("SunberryTeam", StringComparison.Ordinal));
    }

    [Fact]
    public void 非事件资产不动()
    {
        var data = JObject.Parse($$"""{ "{{Old}}": "x" }""");
        Assert.Equal(0, EventPreconditions.Rewrite("Data/Objects", data));
        Assert.Contains(data.Properties(), p => p.Name.Contains("SunberryTeam", StringComparison.Ordinal));
    }

    /// <summary>正文里的同名字符串不是前置条件，别改。</summary>
    [Fact]
    public void 事件正文里的同名字符串不动()
    {
        var data = JObject.Parse($$"""
        { "event/t 600 1200": "speak Clara \"{{Old}}\"" }
        """);

        Assert.Equal(0, EventPreconditions.Rewrite("Data/Events/Custom_SBV_Mines1", data));
        Assert.Equal($"speak Clara \"{Old}\"", data["event/t 600 1200"]!.Value<string>());
    }
}
