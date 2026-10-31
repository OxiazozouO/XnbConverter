using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// 把**只有装了某个 DLL 才认识的事件前置条件**改写成原版 GSQ。
/// <para>
/// 原版对未知前置条件是「记 Warning 并返回 false」，而且**不处理 `!` 取反**
/// （`Event.cs:4197-4200`）⇒ 事件 key 里带 `!未知前置` 的事件**永远不会触发**。
/// 这比"命令被跳过"严重得多：命令跳过时事件还能走完，前置条件不成立则整条事件不出现。
/// </para>
/// <para>
/// 已确认的一类（2026-10-18 调查）：Sunberry Village 的 22 个事件用
/// `!SunberryTeam.SBVSMAPI_IsPassiveFestivalToday`（`SunberryVillage.dll:1744` 注册）。
/// 它的意思是"今天不是 Sunberry 的被动节日"；数据里 Sunberry 只有一个被动节日
/// `skellady.SBVCP_TwilightFestival`（`Data/PassiveFestivals.json`），
/// 原版事件前置条件的等价写法是 `!G IS_PASSIVE_FESTIVAL_TODAY skellady.SBVCP_TwilightFestival`
/// （`GameStateQuery.cs:384-392`）。纯 XNB 没有"被动节日"的 DLL 实现，但被动节日**数据**
/// 是原版自带的 —— 这个替换是语义等价的。
/// </para>
/// </summary>
public static class EventPreconditions
{
    private const string SunberryPassiveFestival = "!SunberryTeam.SBVSMAPI_IsPassiveFestivalToday";
    // ⚠ 事件前置条件里不能直接写 GSQ 关键字：要写成 `G <GSQ>`（`G` 是 `GameStateQuery` 的别名，
    // `Preconditions.cs:742-753`）。`!` 由 `CheckPrecondition` 在最外层处理（`:4192-4200`），
    // 所以 `!G IS_PASSIVE_FESTIVAL_TODAY …` 才是"今天不是这个被动节日"。
    private const string VanillaPassiveFestival =
        "!G IS_PASSIVE_FESTIVAL_TODAY skellady.SBVCP_TwilightFestival";

    /// <summary>
    /// 改写 <c>Data/Events</c> 资产里的事件 key。返回改写的 key 数。
    /// <para>只动 **key**（前置条件在 key 里）；事件正文里的同名字符串不是前置条件，别碰。</para>
    /// </summary>
    public static int Rewrite(string assetName, JToken? data)
    {
        if (data is not JObject root) return 0;
        if (!assetName.StartsWith("Data/Events", StringComparison.OrdinalIgnoreCase)) return 0;

        // `JProperty.Name` 是只读的 ⇒ 换名只能重建整层。先确认换完不会撞名，撞了宁可不动。
        int rewritten = root.Properties()
            .Count(p => p.Name.Contains(SunberryPassiveFestival, StringComparison.Ordinal));
        if (rewritten == 0) return 0;

        var renamed = root.Properties()
            .Select(p => new JProperty(
                p.Name.Replace(SunberryPassiveFestival, VanillaPassiveFestival, StringComparison.Ordinal),
                p.Value.DeepClone()))
            .ToList();
        if (renamed.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != renamed.Count)
            return 0;   // 换完撞名：宁可不动，别让 JObject 抛

        root.RemoveAll();
        foreach (JProperty property in renamed) root.Add(property);
        return rewritten;
    }
}
