namespace SdvCpTool.Core.Cp;

/// <summary>
/// 令牌**输入参数**里那几个能离线折叠的 CP 算子。目前只有 <c>|contains=</c>。
///
/// <para>
/// <b>依据（实测反推，不是猜）</b>：<c>[CP] Seasonal Cute Characters</c> 的配置
/// <c>WinterClothing</c> 取值 <c>IndoorAndOutdoor</c> 时同时定义了
/// <c>IndoorsWinterOutfitEnabled = {{WinterClothing |contains=IndoorAndOutdoor, IndoorOnly}}</c> 与
/// <c>IndoorsWinterOutfitDisabled = {{WinterClothing |contains=OutdoorOnly}}</c> ——
/// 只有**任一命中**（any-of）说得通：选 <c>IndoorAndOutdoor</c> ⇒ 启用为真、禁用为假；
/// 要是"全都要有"（all-of），选了 <c>IndoorAndOutdoor</c> 反而两边都为假（既不启用也不禁用），
/// 那不是作者的意思。
/// </para>
/// <para>
/// 与既有 <c>HasMod |contains=X</c> 的实现同一条口径（那里的值是"已装模组 id 集合"，
/// 命中即 <c>true</c>）。大小写不敏感（配置取值在 CP 里就是大小写不敏感的）。
/// </para>
/// </summary>
public static class TokenArgs
{
    /// <summary>
    /// 把 <c>|contains=a, b</c> 折成 <c>true</c>/<c>false</c>。
    /// <paramref name="args"/> 不是这个算子（或参数为空）时返回 <c>false</c> —— 调用方**原样保留**令牌。
    /// </summary>
    public static bool TryFoldContains(IReadOnlyList<string> values, string? args, out string folded)
    {
        folded = "";
        if (string.IsNullOrWhiteSpace(args)) return false;
        if (!args.StartsWith("|contains=", StringComparison.OrdinalIgnoreCase)) return false;

        string want = args["|contains=".Length..].Trim();
        if (want.Length == 0) return false;

        bool any = want.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
            .Any(w => values.Any(v => string.Equals(v, w, StringComparison.OrdinalIgnoreCase)));
        folded = any ? "true" : "false";
        return true;
    }
}
