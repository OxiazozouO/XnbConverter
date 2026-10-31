using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// **代价模组（DLL）暴露的 CP 命名空间令牌**里，值其实来自它自己 <c>config.json</c> 的那一小类。
/// <para>
/// 形态：<c>{{作者.模组/TokenName}}</c>（实测 `[CP] GiEX redux` 的
/// <c>{{mistyspring.GiEXredux.code/ExcludeFishes}}</c> / <c>ExcludeAnimalProduce</c>）。
/// 不代的话整条 `Data/Objects` 补丁被拒 ⇒ 28 个鱼 + 31 个动物产物定义消失，
/// 商店/机器引用悬空。
/// </para>
/// <para>
/// <b>只做收窄的规则</b>（不猜）：
/// <list type="bullet">
/// <item>模组目录里必须有 <c>manifest.json</c> 且带 <c>EntryDll</c>（排除纯 CP 内容包）；</item>
/// <item><c>config.json</c> 里有**同名**字段，或白名单映射，或**唯一前缀**匹配；</item>
/// <item>只接受顶层布尔/数字/短字符串（不含逗号、换行；字符串 ≤ 64 字符）；</item>
/// <item>布尔按 C# 插值的 <c>True</c>/<c>False</c> 输出（DLL 的令牌值就是这么来的）。</item>
/// </list>
/// 其余一律返回 <c>null</c>（保持未解析）。模糊或子串匹配**不做** —— 大多数命名空间令牌是
/// 运行时状态，同名 config 纯属巧合时"假成功"比拒绝更危险。
/// </para>
/// </summary>
public static class DllConfigTokens
{
    /// <summary>
    /// 令牌名与 config 字段名不同、但已核实一一对应的少数几个。
    /// **键必须带 mod ID**：别的模组同名字段不一定是同一个令牌（假成功比拒绝更危险）。
    /// </summary>
    private static readonly Dictionary<string, string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        // ExtraGIMapsRedux：`ExcludeFishes` 读 `ExcludeFromFishingCollection`。
        ["mistyspring.giexredux.code|ExcludeFishes"] = "ExcludeFromFishingCollection",
        // ExtraGIMapsRedux：`PortraitStyle` 读 `Portrait`（值为 Vanilla/Anime/SHIE/Auto）。
        ["mistyspring.giexredux.code|PortraitStyle"] = "Portrait",
    };

    public static string? Resolve(string tokenName, string? args, InstalledModIndex mods)
    {
        if (!string.IsNullOrWhiteSpace(args)) return null;   // 带输入参数的形态不猜

        int slash = tokenName.IndexOf('/');
        if (slash <= 0 || slash == tokenName.Length - 1) return null;
        string modId = tokenName[..slash].Trim();
        string field = tokenName[(slash + 1)..].Trim();
        if (field.Length == 0) return null;

        string? dir = mods.GetModDirectory(modId);
        if (dir == null || !HasEntryDll(dir)) return null;

        JObject? config = mods.GetModConfig(modId);
        if (config == null) return null;

        JToken? value = config[field];
        if (value == null && Whitelist.TryGetValue(modId + "|" + field, out string? mapped))
            value = config[mapped];
        if (value == null)
        {
            // 唯一前缀：`ExcludeAnimalProduce` → `ExcludeAnimalProduceFromShippingCollection`。
            if (field.Length < 6) return null;
            List<JProperty> matches = config.Properties()
                .Where(p => p.Name.StartsWith(field, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1) return null;
            value = matches[0].Value;
        }

        return Render(value);
    }

    private static string? Render(JToken value) => value switch
    {
        JValue { Type: JTokenType.Boolean } b => b.Value<bool>() ? "True" : "False",
        JValue { Type: JTokenType.Integer } i => Convert.ToString(i.Value<long>(),
            System.Globalization.CultureInfo.InvariantCulture),
        JValue { Type: JTokenType.Float } f => Convert.ToString(f.Value<double>(),
            System.Globalization.CultureInfo.InvariantCulture),
        JValue { Type: JTokenType.String } s => RenderString(s.Value<string>() ?? ""),
        _ => null,
    };

    private static string? RenderString(string text)
    {
        if (text.Length > 64) return null;
        if (text.Contains(',') || text.Contains('\n') || text.Contains('\r')) return null;
        return text;
    }

    /// <summary>mod 目录的 manifest 带 <c>EntryDll</c>（= 有代码，才可能注册命名空间令牌）。</summary>
    private static bool HasEntryDll(string dir)
    {
        string manifest = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifest)) return false;
        try
        {
            JObject root = JsoncReader.ParseFile(manifest);
            // 实测 `Fish Info` 写的是 `EntryDLL`（全大写 DLL）；SMAPI 能正常加载，
            // 说明 manifest 字段名按不区分大小写读。按字面量取会把它当成内容包。
            JProperty? entry = root.Properties().FirstOrDefault(
                p => p.Name.Equals("EntryDll", StringComparison.OrdinalIgnoreCase));
            return entry?.Value is JValue { Type: JTokenType.String } dll
                   && !string.IsNullOrWhiteSpace(dll.Value<string>());
        }
        catch
        {
            return false;
        }
    }
}
