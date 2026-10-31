using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// 原版有几族资产，**条件容量是封闭的、逐条查清过的**（键空间是写死的候选表，值里也只有有限的几种条件写法）。
/// 对这几族，判定可以**敢说"确证无处安放"** —— 而不是含糊地"需人工确认落点"。
///
/// <para>
/// <b>判据（每族都给了逐条依据）</b>：
/// <list type="bullet">
/// <item><b>日程</b>（<c>Characters/schedules/*</c>、<c>strings/schedules/*</c>）：
///   游戏依次去试的键是**写死的一串**（<c>NPC.checkSchedule</c>，约 <c>5800-5890</c>）：
///   <c>marriage_&lt;季节&gt;_&lt;几号&gt;</c> / <c>marriageJob</c> / <c>marriage_&lt;星期&gt;</c> /
///   <c>&lt;季节&gt;_&lt;几号&gt;</c> / <c>&lt;星期&gt;_&lt;n&gt;</c> / <c>&lt;星期&gt;</c> / <c>bus</c> /
///   <c>rain2</c> / <c>rain</c> / <c>&lt;季节&gt;_&lt;星期&gt;_&lt;n&gt;</c> / <c>&lt;季节&gt;_&lt;星期&gt;</c> /
///   <c>&lt;季节&gt;</c> / <c>spring_&lt;星期&gt;</c> / <c>spring</c>
///   ⇒ 维度只有 <b>{已婚, 季节, 几号, 星期, 周序号}</b>；
///   值（日程脚本）里能挂条件的只有三种写法（<c>NPC.parseMasterScheduleImpl</c>）：
///   <c>GOTO</c> / <c>NOT friendship</c> / 开头的 <c>MAIL &lt;邮件id&gt;</c> ⇒ 只有"有没有那封信"这一个布尔维度。
/// </item>
/// <item><b>节日</b>（<c>Data/Festivals/*</c>）：条目是**字段→字符串**，字段名里唯一的条件维度是
///   <c>_y&lt;年&gt;</c> 变体（<c>Event.TryGetFestivalDataForYear</c>，<c>Event.cs:4417-4448</c>：
///   <c>Game1.year % num</c> 选键）；配偶那一维另有 <c>&lt;NPC&gt;_spouse</c> 键（<c>Event.cs:12609-12665</c>，
///   由 <c>SpouseKeyRewriter</c> 单管）。除此之外**没有别的地方放条件**。
/// </item>
/// </list>
/// </para>
///
/// <para>
/// <b>怎么用（这是本类唯一定义的口径）</b>：补丁的 <c>When</c> 里只要有**一个合取项**不在这族的容量清单里，
/// 就能**确证**这条补丁无处安放（合取要求每一项都成立，而这一项没有任何地方能表达）⇒ 判
/// <c>NoLandingPoint</c>。**全都在容量内但仍不被键名蕴含**时**什么都不说**（交回原来的
/// <c>NeedsReview</c>）—— 那种情况可能是"有落点只是我们没实现"（日程的 <c>MAIL</c>/<c>marriage_</c> 那几种），
/// 一口咬定"没有落点"就是**说错**。
/// </para>
/// </summary>
public static class ConditionCapacity
{
    /// <summary>容量内**可能**表达得出来的条件键（CP 写法的基名）。不在里面的合取项 ⇒ 确证无处安放。</summary>
    private static readonly string[] ScheduleExpressible =
    {
        // 键空间那五个维度（已婚=marriage_ 前缀；季节/几号/星期/周序号）
        "Season", "Day", "DayOfWeek", "Spouse", "Relationship",
        // 值里的 MAIL 槽：有没有某封邮件 / 某个 world state
        "HasMail", "HasFlag",
    };

    private static readonly string[] FestivalExpressible =
    {
        // `_y<年>` 变体（循环）+ `<NPC>_spouse` 键
        "Year", "IsOddYear", "Spouse", "Relationship", "Season",
    };

    /// <summary>
    /// 这条补丁**确证**无处安放吗？是则给出理由（含该族的容量清单与**卡住的那一项**）。
    /// 不是这几族、或条件没被证伪，返回 <c>false</c>（调用方保持原判）。
    /// </summary>
    public static bool ProvablyUnlandable(string assetName, JObject? when, out string? reason)
    {
        reason = null;
        if (when == null || !when.HasValues) return false;

        string normalized = assetName.Replace('\\', '/').Trim('/');
        bool isSchedule = normalized.StartsWith("Characters/schedules/", StringComparison.OrdinalIgnoreCase)
                          || normalized.StartsWith("Strings/schedules/", StringComparison.OrdinalIgnoreCase);
        bool isFestival = normalized.StartsWith("Data/Festivals/", StringComparison.OrdinalIgnoreCase);
        if (!isSchedule && !isFestival) return false;

        string[] expressible = isSchedule ? ScheduleExpressible : FestivalExpressible;
        foreach (JProperty p in when.Properties())
        {
            string baseName = p.Name.Split('|')[0].Trim().Split(':')[0].Trim();
            if (expressible.Contains(baseName, StringComparer.OrdinalIgnoreCase)) continue;

            reason = isSchedule
                ? $"日程的**条件容量是封闭的**（`NPC.checkSchedule` 的候选键写死："
                  + "`marriage_<季节>_<几号>` / `marriageJob` / `marriage_<星期>` / `<季节>_<几号>` / "
                  + "`<星期>_<n>` / `<星期>` / `bus` / `rain2` / `rain` / `<季节>_<星期>_<n>` / "
                  + "`<季节>_<星期>` / `<季节>` / `spring_<星期>` / `spring` —— 维度只有"
                  + "**{已婚, 季节, 几号, 星期, 周序号}**；值里能挂条件的只有 `GOTO` / `NOT friendship` / "
                  + "开头的 `MAIL <邮件id>`），而这条补丁带了 `" + p.Name + "` ⇒ **确证无处安放**"
                : $"节日数据（`Data/Festivals/*`）的字段是**字符串字典**，字段名里唯一的条件维度是 "
                  + "`_y<年>`（`Event.cs:4417-4448` 的 `Game1.year % num` 循环；配偶那一维是 `<NPC>_spouse`，"
                  + "另有通道处理），而这条补丁带了 `" + p.Name + "` ⇒ **确证无处安放**";
            return true;
        }

        return false;   // 全在容量内：可能是"有落点但没实现"，**不替它下结论**
    }
}
