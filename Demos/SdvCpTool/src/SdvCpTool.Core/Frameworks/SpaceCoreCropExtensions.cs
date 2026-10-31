using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Frameworks;

/// <summary>
/// SpaceCore <c>CropExtensionData</c> 里能精确落到原版 <c>Data/Crops</c> 的子集。
/// <para>
/// 只映射 <c>RegrowDays</c>：原版 `CropData.RegrowDays` 是全局值，而 SpaceCore 的
/// `YieldOverrides.&lt;harvest&gt;.RegrowDays` 是逐次收获的覆盖。只有当所有被覆盖的
/// 收获序号给出**同一个**再生天数时才写回；多个不同值无法用原版单值表达，报歧义不猜。
/// </para>
/// <para>
/// `Drops` / `IgnoreDefaultDrops` / `ExperienceGained` 等没有对应的原版作物字段，继续列为损失。
/// </para>
/// </summary>
public static class SpaceCoreCropExtensions
{
    public const string Target = "spacechase0.SpaceCore/CropExtensionData";

    public static int Apply(JObject crops, string packRoot, string uniqueId, List<string> notes)
        => Apply(crops, packRoot, uniqueId, notes, new SpaceCoreJsonCache());

    internal static int Apply(JObject crops, string packRoot, string uniqueId, List<string> notes,
        SpaceCoreJsonCache spaceCoreJson)
    {
        int applied = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SpaceCoreJsonCache.CachedFile file in spaceCoreJson.GetFiles(packRoot))
        {
            if (!file.Targets.Contains(Target)) continue;

            foreach (JObject change in Descendants(file.Root).OfType<JObject>())
            {
                // 递归遍历会把 `Entries` 里的对象也带进来；它们可能有 `Target` 数组，
                // 直接 `Value<string>` 会抛 InvalidCastException。
                if (change["Target"]?.Type != JTokenType.String
                    || !string.Equals(change["Target"]!.Value<string>(), Target,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (change["When"] is JObject when && when.HasValues)
                {
                    notes.Add($"SpaceCore 作物扩展：带条件的补丁未映射（`{when.ToString(Newtonsoft.Json.Formatting.None)}`）");
                    continue;
                }
                if (change["Entries"] is not JObject entries) continue;

                foreach (JProperty entry in entries.Properties())
                {
                    string dedup = entry.Name + "|" + entry.Value.ToString(Newtonsoft.Json.Formatting.None);
                    if (!seen.Add(dedup)) continue;
                    if (entry.Value is not JObject data) continue;

                    string id = Regex.Replace(entry.Name, @"\{\{ModId\}\}", uniqueId,
                        RegexOptions.IgnoreCase);
                    if (id.Contains("{{", StringComparison.Ordinal))
                    {
                        notes.Add($"SpaceCore 作物扩展：`{entry.Name}` 仍有未展开令牌，跳过");
                        continue;
                    }
                    if (crops[id] is not JObject target)
                    {
                        notes.Add($"SpaceCore 作物扩展：Data/Crops 里没有 `{id}`，跳过");
                        continue;
                    }

                    var days = new HashSet<int>();
                    if (TryInt(data["RegrowDays"], out int direct)) days.Add(direct);
                    if (data["YieldOverrides"] is JObject overrides)
                    {
                        foreach (JProperty y in overrides.Properties())
                            if (y.Value is JObject yv && TryInt(yv["RegrowDays"], out int d))
                                days.Add(d);
                    }

                    bool changed = false;
                    if (days.Count == 1)
                    {
                        target["RegrowDays"] = days.First();
                        changed = true;
                    }
                    else if (days.Count > 1)
                    {
                        notes.Add($"SpaceCore 作物扩展 `{id}`：RegrowDays 有多个值（{string.Join("/", days.OrderBy(x => x))}），原版单值无法表达");
                    }

                    if (changed) applied++;
                    var ignored = new List<string>();
                    foreach (JProperty p in data.Properties())
                    {
                        if (p.Name.Equals("RegrowDays", StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.Name.Equals("YieldOverrides", StringComparison.OrdinalIgnoreCase))
                        {
                            bool hasOther = p.Value is JObject o && o.Properties().Any(x =>
                                x.Value is JObject oo && oo.Properties().Any(k =>
                                    !k.Name.Equals("RegrowDays", StringComparison.OrdinalIgnoreCase)));
                            if (hasOther) ignored.Add("YieldOverrides(其它字段)");
                            continue;
                        }
                        ignored.Add(p.Name);
                    }
                    if (ignored.Count > 0)
                        notes.Add($"SpaceCore 作物扩展 `{id}`：未映射 {string.Join("、", ignored.Take(4))}");
                }
            }
        }

        return applied;
    }

    private static bool TryInt(JToken? token, out int value)
    {
        value = 0;
        if (token == null) return false;
        if (token.Type == JTokenType.Integer)
        {
            value = token.Value<int>();
            return true;
        }
        return token.Type == JTokenType.String && int.TryParse(token.Value<string>(), out value);
    }

    private static IEnumerable<JToken> Descendants(JToken root)
    {
        yield return root;
        foreach (JToken child in root.Children())
        foreach (JToken descendant in Descendants(child))
            yield return descendant;
    }
}
