using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 雨/昼夜条件地图叠加的原生近似：<c>DayTiles</c> / <c>NightTiles</c>。
/// <para>
/// CP 的 <c>EditMap</c> 在 <c>When: { Weather: Rain, Storm }</c>、
/// <c>When: { Time: "{{Range: 1900, 2500}}" }</c> 或纯白天 <c>Time</c> 下把一层图贴到地图上；
/// 纯 XNB 没有条件图层，但原版 <c>GameLocation.resetForPlayerEntry</c> 在
/// “夜晚或正在下雨”时会应用 <c>NightTiles</c>，白天晴天则应用 <c>DayTiles</c>。
/// 因此把叠加层的一侧写成透明、另一侧写成原图块，就能得到近似。
/// </para>
/// <para>
/// 只接受**纯叠加**：`FromFile` + 可选 `ToArea`/`FromArea`/`PatchMode`，
/// 且条件里只有不带地点上下文的 <c>Weather</c>（Rain/Storm/GreenRain）或
/// **纯夜间/纯白天**的 <c>Time</c> 区间。带 `MapTiles`、`MapProperties`、
/// `AddWarps` 等其它改动的补丁不走这里。
/// </para>
/// </summary>
public enum DayNightOverlayMode
{
    None,
    /// <summary>叠图只在夜晚/雨天显示（默认）。</summary>
    Night,
    /// <summary>叠图只在白天显示（用于纯白天 Time；夜间/雨天隐藏）。</summary>
    Day,
}

public static class WeatherMapOverlay
{
    public static DayNightOverlayMode Classify(ChangePatch patch, WhenNode? when,
        IReadOnlyList<WhenNode> keys)
    {
        if (!patch.Action.Equals("EditMap", StringComparison.OrdinalIgnoreCase))
            return DayNightOverlayMode.None;
        if (keys.Count == 0)
            return DayNightOverlayMode.None;

        bool hasFromFile = patch.Raw["FromFile"] != null;
        bool hasAmbient = IsAmbientProperties(patch.Raw["MapProperties"] as JObject);
        if (!hasFromFile && !hasAmbient)
            return DayNightOverlayMode.None;
        if (hasFromFile && hasAmbient)
            return DayNightOverlayMode.None;

        // `Not`/`Any` 会让“只看叶子值”的判定方向不可靠；这种组合不接管。
        if (when?.Descendants().Any(n => n.Kind is WhenNodeKind.Not or WhenNodeKind.Any) == true)
            return DayNightOverlayMode.None;

        // 只接管“纯叠加”（FromFile 或 AmbientLight 属性）补丁；其它字段走旧路径。
        string[] common = { "Action", "Target", "When", "Update", "LogName" };
        string[] fileOnly = { "FromFile", "FromArea", "ToArea", "PatchMode" };
        string[] ambientOnly = { "MapProperties" };
        foreach (JProperty p in patch.Raw.Properties())
        {
            bool ok = common.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
                      || (hasFromFile && fileOnly.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                      || (hasAmbient && ambientOnly.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
            if (!ok) return DayNightOverlayMode.None;
        }

        if (patch.Raw["PatchMode"]?.Value<string>() is string mode
            && mode.Equals("Replace", StringComparison.OrdinalIgnoreCase))
            return DayNightOverlayMode.None;

        bool hasRain = false, hasNight = false, hasDay = false;
        foreach (WhenNode key in keys)
        {
            if (IsRainWeather(key)) hasRain = true;
            else if (IsNightTime(key)) hasNight = true;
            else if (IsDayTime(key)) hasDay = true;
            else if (!IsSunWind(key)) return DayNightOverlayMode.None;
        }

        // 白天和夜间条件同时出现、或“白天 + 雨天”混合，原版两态无法同时满足。
        if (hasNight && hasDay) return DayNightOverlayMode.None;
        if (hasDay && hasRain) return DayNightOverlayMode.None;
        if (hasNight || hasRain) return DayNightOverlayMode.Night;
        if (hasDay) return DayNightOverlayMode.Day;   // 可带 Weather: Sun, Wind

        // 只有 Sun/Wind、没有 Time 时没有明确昼夜，走旧判定。
        return DayNightOverlayMode.None;
    }

    private static bool IsAmbientProperties(JObject? properties)
    {
        if (properties == null || !properties.HasValues) return false;
        foreach (JProperty p in properties.Properties())
        {
            if (!p.Name.Equals("AmbientLight", StringComparison.OrdinalIgnoreCase)
                && !p.Name.Equals("AmbientNightLight", StringComparison.OrdinalIgnoreCase))
                return false;
            if (p.Value.Type != JTokenType.String) return false;
        }
        return true;
    }

    private static bool IsRainWeather(WhenNode key)
    {
        if (!string.Equals(key.KeyName?.Trim(), "Weather", StringComparison.OrdinalIgnoreCase))
            return false;

        // CP 的 Weather 可带地点上下文（island/valley）；原版 Day/NightTiles 没有地点参数。
        if (!string.IsNullOrWhiteSpace(key.Arguments)
            && !key.Arguments.Trim().Equals("Here", StringComparison.OrdinalIgnoreCase))
            return false;

        string value = key.Value?.Trim() ?? "";
        if (value.Length == 0) return false;

        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        return parts.All(v => v.Equals("Rain", StringComparison.OrdinalIgnoreCase)
                              || v.Equals("Storm", StringComparison.OrdinalIgnoreCase)
                              // GreenRain 的 `LocationWeather.IsRaining` 也是 true，
                              // 因此同样会走 `switchOutNightTiles()`。
                              || v.Equals("GreenRain", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <c>Time</c> 区间是否是**纯夜间**。接受 CP 的 <c>{{Range: 1900, 2500}}</c>
    /// 与直接写出的 <c>1900 2500</c>/<c>1900, 2500</c>。
    /// </summary>
    private static bool IsNightTime(WhenNode key)
        => TryTimeRange(key, out int start, out int end) && start >= 1800 && end <= 2600;

    /// <summary>白天区间：起点早于 1800，终点不晚于 1900（例如 600–1900）。</summary>
    private static bool IsDayTime(WhenNode key)
        => TryTimeRange(key, out int start, out int end) && start < 1800 && end <= 1900;

    private static bool TryTimeRange(WhenNode key, out int start, out int end)
    {
        start = end = 0;
        if (!string.Equals(key.KeyName?.Trim(), "Time", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(key.Arguments)) return false;

        string raw = key.Value?.Trim() ?? "";
        if (raw.Length == 0) return false;

        // {{Range: 1900, 2500}}
        var m = System.Text.RegularExpressions.Regex.Match(raw,
            @"^\{\{\s*Range\s*:\s*(\d{1,4})\s*,\s*(\d{1,4})\s*\}\}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string a, b;
        if (m.Success)
        {
            a = m.Groups[1].Value;
            b = m.Groups[2].Value;
        }
        else
        {
            string[] parts = raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;
            a = parts[0];
            b = parts[1];
        }

        return int.TryParse(a, out start) && int.TryParse(b, out end) && end > start;
    }

    /// <summary>`Weather: Sun, Wind`：只用于给白天 Time 补一个“不是雨”的旁证。</summary>
    private static bool IsSunWind(WhenNode key)
    {
        if (!string.Equals(key.KeyName?.Trim(), "Weather", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(key.Arguments)
            && !key.Arguments.Trim().Equals("Here", StringComparison.OrdinalIgnoreCase))
            return false;

        string value = key.Value?.Trim() ?? "";
        if (value.Length == 0) return false;
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0
               && parts.All(v => v.Equals("Sun", StringComparison.OrdinalIgnoreCase)
                                 || v.Equals("Wind", StringComparison.OrdinalIgnoreCase));
    }
}
