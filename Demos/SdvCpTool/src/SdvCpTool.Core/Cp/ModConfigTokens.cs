using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// CP 的**跨模组配置令牌**：<c>{{&lt;UniqueID&gt;/&lt;configField&gt;}} [|contains=…]</c>。
/// <para>
/// 实测来源（2026-10-18）：RSV 的 <c>Data/Characters</c> 里
/// <c>"WinterStarParticipant": "{{Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals |contains=true}}"</c>。
/// 认不出这个令牌 ⇒ 整条 NPC 条目被跳过（RSV 39 个条目，其中 13 个本可结婚的 NPC 直接从
/// `Data/Characters` 消失；后续换装补丁只创建出只剩 <c>Appearance</c> 的半截条目）。
/// </para>
/// <para>
/// 值来自该 mod 目录下的 <c>config.json</c>（<see cref="InstalledModIndex.GetModConfig"/>）。
/// 认不出/没有配置/没有该字段一律返回 <c>null</c>（原样保留，由令牌闸拒绝）—— 不猜。
/// </para>
/// </summary>
public static class ModConfigTokens
{
    public static string? Render(InstalledModIndex mods, string name, string? args)
    {
        int slash = name.IndexOf('/');
        if (slash <= 0 || slash == name.Length - 1) return null;

        string modId = name[..slash];
        string field = name[(slash + 1)..];
        JObject? config = mods.GetModConfig(modId);
        if (config == null) return null;

        JProperty? property = config.Property(field)
                              ?? config.Properties().FirstOrDefault(
                                  p => string.Equals(p.Name, field, StringComparison.OrdinalIgnoreCase));
        if (property == null) return null;

        string? rendered = RenderValue(property.Value);
        if (rendered == null) return null;

        if (string.IsNullOrWhiteSpace(args)) return rendered;
        if (!args.StartsWith("|contains=", StringComparison.OrdinalIgnoreCase)) return null;
        string want = args["|contains=".Length..].Trim();
        if (want.Length == 0) return null;

        // `|contains=A, B` 是 CP 的集合语义：**任一命中**（`Token.ApplyContains` 用 Overlaps）。
        string[] wants = want.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] have = rendered.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool hit = have.Any(v => wants.Any(w => v.Equals(w, StringComparison.OrdinalIgnoreCase)));
        return hit ? "true" : "false";
    }

    private static string? RenderValue(JToken value) => value.Type switch
    {
        JTokenType.Boolean => value.Value<bool>() ? "true" : "false",
        JTokenType.String => value.Value<string>(),
        JTokenType.Integer => value.ToString(),
        JTokenType.Float => value.Value<double>().ToString(System.Globalization.CultureInfo.InvariantCulture),
        // CP 的多值令牌按 `", "` 连接（`TokenString.cs:290`）。
        JTokenType.Array => string.Join(", ", value.Children()
            .Select(RenderValue).Where(v => v != null)),
        _ => null,
    };
}
