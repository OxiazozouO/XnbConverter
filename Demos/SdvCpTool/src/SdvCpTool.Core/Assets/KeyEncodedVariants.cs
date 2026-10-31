using System.Text.RegularExpressions;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// 「键名编码条件维度」的词表 —— 原版有一整族资产把条件写在键名里、由游戏按状态选键读取。
/// <para>
/// 只收<b>源码逐条确证过</b>的维度（每条注释给出依据行号）。<b>宁少勿多</b>：未确证的键形态一律不认 ——
/// 认错的代价是上层把"不知道"当成"知道"。命中只是"这个键看起来带了维度"，不代表条件一定能被蕴含：
/// 是否可转换由 <c>KeyEncodedVariantRewriter</c> 逐条判定。
/// </para>
/// </summary>
public static class KeyEncodedVariants
{
    /// <summary>SDV 的四季名（<c>Season</c> 枚举：spring/summer/fall/winter）。</summary>
    private static readonly string[] Seasons = { "spring", "summer", "fall", "winter" };

    /// <summary>`{key}_y{year}` 年份变体（Event.cs:4417-4448 TryGetFestivalDataForYear 的循环）。</summary>
    private static readonly Regex YearSuffix = new(@"_y\d+$", RegexOptions.Compiled);

    /// <summary>已确证的<b>后缀</b>维度（区分大小写 —— 原版读取点用的就是这些确切拼写）。</summary>
    private static readonly string[] Suffixes =
    {
        "_spouse", "_roommate",   // Event.cs:12609-12665（配偶/室友节日对话）
        "_Male", "_Female",       // FarmerTeam.cs:1315-1317 genderedKey
        "_dark",                  // Child.cs:109,113（婴儿/幼儿肤色）
        "_sleep",                 // NPC.cs:4559,5631（AnimationDescriptions 的睡眠动画键）
        "_Billed", "_NotBilled",  // Farmer.cs:5908、GameLocation.cs:10566（昏倒信件）
    };

    /// <summary>键名是否命中任一经确证的维度。</summary>
    public static bool Hits(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;

        if (LeadingSeason(key) != null) return true;                          // Game1.cs:8296-8302 / GameLocation.cs:16442
        if (key.Contains("_beach_", StringComparison.Ordinal)) return true;   // IslandSouth.cs:99-135（海滩活动动画键）
        if (YearSuffix.IsMatch(key)) return true;

        foreach (string suffix in Suffixes)
            if (key.EndsWith(suffix, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>键名若以 <c>&lt;season&gt;_</c> 开头则返回该季节名（小写），否则返回 <c>null</c>。</summary>
    public static string? LeadingSeason(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (string season in Seasons)
            if (key.StartsWith(season + "_", StringComparison.OrdinalIgnoreCase))
                return season;
        return null;
    }

    /// <summary>字符串是否为合法季节名（供转换规则校验条件值）。</summary>
    public static bool IsSeason(string value) =>
        Seasons.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
}
