using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 判断一组 CP 条件是否依赖**运行时存档/世界状态**（`HasSeenEvent`/`HasFlag`/`Relationship`…）。
/// <para>
/// 这类条件在离线解析时会被冻结成某个值（通常 `false`），但**不等于"静态为假"**：
/// CP 在运行时可能为真。需要做"必然二选一"的确定化时（创建条目 / 条件 Load），
/// 靠它区分「真的不适用」与「只是现在不知道」。
/// </para>
/// <para>
/// 除了补丁自己的 `When` 键值，还检查**包内 DynamicToken** 的块值与块条件 ——
/// 实测 SVE 的 `ApplesHere = {{HasSeenEvent…}}` 这种中转。
/// </para>
/// </summary>
public static class RuntimeConditions
{
    private static readonly Regex Token = new(
        @"\{\{\s*(HasSeenEvent|HasFlag|Relationship|Roommate|Hearts|Friendship|Spouse|"
        + @"IsMainPlayer|DaysPlayed|DailyLuck|Weather|Time|DayOfWeek|Day|Year|Random|"
        + @"ChildGenders|LocationName|LocationContext|HasConversationTopic|PlayerName|FarmName)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool MayDependOnSave(JObject when, DynamicTokenResolver tokens)
    {
        foreach (JProperty property in when.Properties())
        {
            if (Token.IsMatch(property.Name + " " + property.Value)) return true;

            string name = DynamicTokenResolver.ConditionKeyName(property.Name);
            IReadOnlyList<(string Value, JObject? When)>? blocks = tokens.BlocksOf(name);
            if (blocks == null) continue;
            foreach ((string value, JObject? blockWhen) in blocks)
            {
                if (Token.IsMatch(value)) return true;
                if (blockWhen != null && Token.IsMatch(blockWhen.ToString())) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// **所有**子句都可能依赖运行时状态（才允许把 `When` 确定化成无条件）。
    /// 只要有一个子句是**已知静态**（`HasMod`/`HasFile`/配置字段/`Season`…），
    /// 它判假就是真的不适用 —— 混合条件不能拆（实测 `HasMod: 未装` + `HasSeenEvent`）。
    /// </summary>
    public static bool AllMayDependOnSave(JObject when, DynamicTokenResolver tokens)
    {
        foreach (JProperty property in when.Properties())
        {
            string name = DynamicTokenResolver.ConditionKeyName(property.Name);
            if (IsKnownStaticKey(name, tokens)) return false;
        }
        return true;
    }

    private static readonly HashSet<string> KnownStaticKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "HasMod", "ModId", "ModID", "HasFile", "FirstValidFile",
        "Season", "FarmType", "Language",
    };

    private static bool IsKnownStaticKey(string name, DynamicTokenResolver tokens)
    {
        if (KnownStaticKeys.Contains(name) || tokens.IsConfigToken(name)) return true;

        // 包内 DynamicToken：看它的块值与块条件里有没有运行时令牌；
        // 一块运行时都没有 ⇒ 视为静态可判（配置/HasMod 驱动）。
        IReadOnlyList<(string Value, JObject? When)>? blocks = tokens.BlocksOf(name);
        if (blocks == null) return false;   // 未知的外部令牌 ⇒ 当运行时（保守：宁可写）
        foreach ((string value, JObject? blockWhen) in blocks)
        {
            if (Token.IsMatch(value)) return false;
            if (blockWhen != null && Token.IsMatch(blockWhen.ToString())) return false;
        }
        return true;
    }
}
