using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 把 <c>Target</c> 里带 <c>{{Season}}</c> 的补丁**展开成四季各一份**。
/// <para>
/// <b>依据</b>：CP 的 <c>Season</c> 是**单值**令牌 ——
/// <c>new ConditionTypeValueProvider(ConditionType.Season, save.GetSeason, allowedValues: {Spring, Summer, Fall, Winter})</c>
/// （<c>TokenManager.cs:148</c>），而 <c>TokenSaveReader.GetSeason()</c> 返回 <c>Game1.currentSeason</c>
/// （**小写** <c>spring</c>）。所以 <c>"Target": "Maps/{{season}}_town"</c> 在 CP 里
/// **每个季节解析成不同的资产**（`Maps/spring_town` / `summer_` / `fall_` / `winter_`），
/// 随季节切换重新应用。静态转换要复现的就是**四季都写一遍**。
/// </para>
/// <para>
/// 实测（SVE）：11 条 <c>EditImage</c> 的目标是 <c>Maps/{{season}}_town</c> /
/// <c>{{season}}_outdoorsTileSheet</c> / <c>{{season}}_Highlands_Background_*</c> ——
/// 那些 <c>Maps/spring_town.png</c> 一类资产**原版树里真有**（还带各语言变体），
/// 所以「四个都改」是**可以落地**的，只是先前判 <c>TargetContainsTokens</c> 整条不写。
/// </para>
/// <para>
/// ⚠ <b><c>When</c> 不动</b>：条件里的 <c>{{Season}}</c> 交给 GSQ 那条路（<c>SEASON spring</c>），
/// 在这里代掉它等于把一个条件**烤成常量**。
/// </para>
/// </summary>
public static class SeasonTargetExpansion
{
    /// <summary>四季的 CP 取值（小写，与 <c>Game1.currentSeason</c> 一致）。</summary>
    public static readonly string[] Seasons = { "spring", "summer", "fall", "winter" };

    /// <summary>该补丁的 <c>Target</c> 里有没有 <c>{{Season}}</c>（大小写不敏感）。</summary>
    public static bool Applies(ChangePatch patch) => HasSeasonToken(patch.Target);

    /// <summary>就地展开整份补丁列表；返回**展开出来的补丁数**（原补丁被替换掉）。</summary>
    public static int Apply(List<ChangePatch> patches)
    {
        int added = 0;
        for (int i = 0; i < patches.Count; i++)
        {
            ChangePatch patch = patches[i];
            if (!Applies(patch)) continue;

            var expanded = new List<ChangePatch>(Seasons.Length);
            foreach (string season in Seasons)
            {
                JObject raw = Substitute(patch.Raw, season);
                expanded.Add(new ChangePatch
                {
                    SourceFile = patch.SourceFile,
                    Index = patch.Index,
                    Action = raw["Action"]?.Value<string>() ?? patch.Action,
                    LogName = raw["LogName"]?.Value<string>(),
                    Target = raw["Target"]?.Value<string>(),
                    FromFile = raw["FromFile"]?.Value<string>(),
                    Update = raw["Update"]?.Value<string>(),
                    When = raw["When"] as JObject,
                    Raw = raw,
                    Priority = patch.Priority,
                });
            }

            patches.RemoveAt(i);
            patches.InsertRange(i, expanded);
            i += expanded.Count - 1;
            added += expanded.Count - 1;
        }
        return added;
    }

    private static bool HasSeasonToken(string? text) =>
        text != null
        && (text.Contains("{{Season}}", StringComparison.OrdinalIgnoreCase)
            || text.Contains("{{ Season }}", StringComparison.OrdinalIgnoreCase)
            || text.Contains("{{season}}", StringComparison.OrdinalIgnoreCase)
            || text.Contains("{{ season }}", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 返回一份**新**的 raw：把（<c>When</c> 之外的）<c>{{Season}}</c>/<c>{{season}}</c> 换成
    /// <paramref name="season"/>。<c>When</c> 整棵子树原样保留（见类说明）。
    /// </summary>
    public static JObject Substitute(JObject raw, string season)
    {
        var clone = (JObject)raw.DeepClone();
        Walk(clone);
        return clone;

        void Walk(JToken node)
        {
            switch (node)
            {
                case JObject obj:
                    foreach (JProperty p in obj.Properties().ToList())
                    {
                        // `When` 是**条件**：它里面的 `{{Season}}` 不是「这个资产叫什么」，
                        // 而是「当前是不是这个季节」——交给 GSQ，别在这里烤成常量。
                        if (p.Name.Equals("When", StringComparison.OrdinalIgnoreCase)) continue;

                        if (p.Value.Type == JTokenType.String)
                        {
                            string text = p.Value.Value<string>() ?? "";
                            string? done = Replace(text, season);
                            if (done != null) p.Value = done;
                        }
                        else
                        {
                            Walk(p.Value);
                        }
                    }
                    break;
                case JArray arr:
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].Type == JTokenType.String)
                        {
                            string? done = Replace(arr[i].Value<string>() ?? "", season);
                            if (done != null) arr[i] = done;
                        }
                        else
                        {
                            Walk(arr[i]);
                        }
                    }
                    break;
            }
        }
    }

    private static string? Replace(string text, string season)
    {
        if (!HasSeasonToken(text)) return null;
        return text
            .Replace("{{ Season }}", season, StringComparison.OrdinalIgnoreCase)
            .Replace("{{Season}}", season, StringComparison.OrdinalIgnoreCase)
            .Replace("{{ season }}", season, StringComparison.OrdinalIgnoreCase)
            .Replace("{{season}}", season, StringComparison.OrdinalIgnoreCase);
    }
}
