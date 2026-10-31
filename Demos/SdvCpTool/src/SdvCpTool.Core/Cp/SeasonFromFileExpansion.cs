using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Normalizing;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 把 <c>FromFile</c> 里带 <c>{{Season}}</c> 的补丁**展开成四季各一份**：
/// 每一份把 <c>FromFile</c> 换成该季的取值，并往 <c>When</c> 里补一条 <c>Season = &lt;该季&gt;</c>。
/// <para>
/// <b>依据</b>：CP 的 <c>Season</c> 是**单值**令牌（<c>TokenManager.cs:148</c>，
/// 取值 = <c>Game1.currentSeason</c>，小写）。所以
/// <c>"FromFile": "assets/…/Cursors_{{season}}.png"</c> 的含义就是
/// 「**当季**时用这一张」—— 把它物化成「四季各一条、每条带 <c>Season</c> 条件」**语义等价**，
/// 而且换出来的条件正好能被既有的两套发射器接住
/// （<see cref="Normalizing.SeasonalTilesheetPlanner"/> 管地图引用的图集、
/// <see cref="Normalizing.AppearancePlanner"/> 管 NPC 的立绘/行走图）。
/// </para>
/// <para>
/// ⚠ <b>只在 <c>Target</c> 里没有季节令牌时才做</b>：Target 里有的话属于
/// <see cref="SeasonTargetExpansion"/> 的活（那一条是「四个资产各写一份」，不是「一个资产按季换图」）。
/// ⚠ <c>When</c> 里**已经有 <c>Season</c>** 时仍要展开，但只展开到它允许的季节，
/// 并把条件替换成该季的单值条件 —— 否则 <c>{{Season}}</c> 无法逐季固定，
/// 一条补丁还会被规划器算进多个季，互相覆盖。
/// </para>
/// </summary>
public static class SeasonFromFileExpansion
{
    private static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };

    /// <summary>就地展开整份补丁列表；返回**新增**的补丁数。</summary>
    public static int Apply(List<ChangePatch> patches)
    {
        int added = 0;
        for (int i = 0; i < patches.Count; i++)
        {
            ChangePatch patch = patches[i];
            if (!Applies(patch)) continue;

            // 如果原 When 已经限定了季节（`Season: "Spring, Summer"` 或
            // `Season |contains=Summer, Winter`），只展开到它允许的季节，并把条件
            // **替换成该季的单值条件** —— 否则一条补丁会被规划器算进多个季，
            // 同一季出现多份互相覆盖的图。
            IReadOnlyList<string>? allowed = patch.When != null && HasSeasonWhen(patch.When)
                ? SeasonToken.OfAll(patch.When, out _)
                : null;
            IEnumerable<string> seasons = allowed ?? Seasons;

            var expanded = new List<ChangePatch>(Seasons.Length);
            foreach (string season in seasons)
            {
                var raw = (JObject)patch.Raw.DeepClone();
                raw["FromFile"] = Substitute(raw["FromFile"]?.Value<string>(), season);

                // 补一条 `Season` 条件（CP 的运行时行为物化到数据里）；
                // 原有 Season 条件若有，换成当前季，避免多重限制。
                var when = raw["When"] as JObject ?? new JObject();
                if (ReferenceEquals(when, raw["When"]) is false) raw["When"] = when;
                foreach (JProperty p in when.Properties().Where(p => IsSeasonKey(p.Name)).ToList())
                    p.Remove();
                when["Season"] = season;

                expanded.Add(new ChangePatch
                {
                    SourceFile = patch.SourceFile,
                    Index = patch.Index,
                    Action = patch.Action,
                    LogName = patch.LogName,
                    Target = patch.Target,
                    FromFile = raw["FromFile"]?.Value<string>(),
                    Update = patch.Update,
                    When = when,
                    Raw = raw,
                    Priority = patch.Priority,
                });
            }

            patches.RemoveAt(i);
            patches.InsertRange(i, expanded);
            i += expanded.Count - 1;
            added += Math.Max(0, expanded.Count - 1);
        }
        return added;
    }

    /// <summary>这条补丁该不该按季节展开。</summary>
    public static bool Applies(ChangePatch patch)
    {
        if (!patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase)) return false;
        if (!HasSeason(patch.FromFile)) return false;
        if (HasSeason(patch.Target)) return false;                 // 归 `SeasonTargetExpansion`
        // 已有 Season 限制时仍要展开（FromFile 里的 {{Season}} 必须逐季固定），
        // 但只展开到它允许的季节；解析不了的保持原样，交给下游。
        if (patch.When != null && HasSeasonWhen(patch.When)
            && SeasonToken.OfAll(patch.When, out _) == null) return false;
        return true;
    }

    private static bool HasSeasonWhen(JObject when) =>
        when.Properties().Any(p => IsSeasonKey(p.Name));

    private static bool IsSeasonKey(string name)
    {
        int cut = name.IndexOfAny(new[] { ':', '|' });
        return (cut >= 0 ? name[..cut] : name).Trim()
            .Equals("Season", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSeason(string? text) =>
        text != null
        && (text.Contains("{{Season}}", StringComparison.OrdinalIgnoreCase)
            || text.Contains("{{ Season }}", StringComparison.OrdinalIgnoreCase)
            || text.Contains("{{season}}", StringComparison.OrdinalIgnoreCase)
            || text.Contains("{{ season }}", StringComparison.OrdinalIgnoreCase));

    private static string? Substitute(string? text, string season) => text == null
        ? null
        : text.Replace("{{ Season }}", season, StringComparison.OrdinalIgnoreCase)
              .Replace("{{Season}}", season, StringComparison.OrdinalIgnoreCase)
              .Replace("{{ season }}", season, StringComparison.OrdinalIgnoreCase)
              .Replace("{{season}}", season, StringComparison.OrdinalIgnoreCase);
}
