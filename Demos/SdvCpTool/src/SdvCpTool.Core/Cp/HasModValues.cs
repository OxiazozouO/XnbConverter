using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// CP 的 <c>{{HasMod}}</c> 值令牌：值是**已装模组 id 集合**；
/// <c>|contains=A, B</c> 按 CP 的 <c>Token.ApplyContains</c> 是**任一命中**。
/// </summary>
public static class HasModValues
{
    /// <summary>展开一个值路径上的 <c>HasMod</c>；认不出的形态返回 <c>null</c>（原样保留）。</summary>
    public static string? Render(InstalledModIndex mods, string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return string.Join(", ", mods.UniqueIds);          // CP 多值令牌的文本形式

        if (!args.StartsWith("|contains=", StringComparison.OrdinalIgnoreCase)) return null;
        string want = args["|contains=".Length..].Trim();
        if (want.Length == 0) return null;

        // `A, B` 是集合；拿整串去查会永远为假（实测 Cape Stardew 的
        // `HasMod |contains=dreamy.AniProjectilesPoke,dreamy.Keyblades`）。
        string[] wants = want.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return wants.Any(id => mods.Contains(id)) ? "true" : "false";
    }
}
