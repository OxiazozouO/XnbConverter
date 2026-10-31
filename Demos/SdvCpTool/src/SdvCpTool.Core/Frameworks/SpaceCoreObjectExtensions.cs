using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Frameworks;

/// <summary>
/// SpaceCore 的 <c>spacechase0.SpaceCore/ObjectExtensionData</c> 里**有原版字段可承载**的子集。
/// <para>
/// 只映射三个语义等价、且原版 <c>ObjectData</c> 确实读取的字段：
/// <c>CanBeTrashed</c>、<c>CanBeGivenAsGift</c>、<c>CanBeShipped</c>/<c>HideFromShippingCollection</c>。
/// 其余（分类文字/颜色、MaxStack、TotemWarp、逐 NPC 礼物、ItemUsed 触发器）原版没有槽位，
/// 继续由报告列为丢失，不硬写。
/// </para>
/// <para>
/// 这是**语法层**：只解析补丁条目、算出要改哪些 <c>Data/Objects</c> 字段；
/// 真正的落盘仍由 <see cref="FrameworkBaker"/> 末尾的 <c>StageIfChanged</c> 统一做。
/// </para>
/// </summary>
public static class SpaceCoreObjectExtensions
{
    public const string Target = "spacechase0.SpaceCore/ObjectExtensionData";

    private static readonly string[] Supported =
    {
        "CanBeTrashed", "CanBeGivenAsGift", "CanBeShipped", "HideFromShippingCollection",
    };

    /// <summary>扫描一个包的 JSON，返回成功写入的条目数。</summary>
    public static int Apply(JObject objects, string packRoot, string uniqueId, List<string> notes)
        => Apply(objects, packRoot, uniqueId, notes, new SpaceCoreJsonCache());

    internal static int Apply(JObject objects, string packRoot, string uniqueId, List<string> notes,
        SpaceCoreJsonCache spaceCoreJson)
    {
        int applied = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SpaceCoreJsonCache.CachedFile file in spaceCoreJson.GetFiles(packRoot))
        {
            if (!file.Targets.Contains(Target)) continue;

            foreach (JObject change in Descendants(file.Root).OfType<JObject>())
            {
                // 递归遍历会把 `Entries` 里的对象也带进来；它们可能有 `Target` 数组
                // （例如地图 `TextOperations`）。只认 `Target` 是字符串的补丁对象。
                if (change["Target"]?.Type != JTokenType.String
                    || !string.Equals(change["Target"]!.Value<string>(), Target,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                // 没有通用的“原版条件槽位”可承载 SpaceCore 扩展条目；带 When 的补丁
                // 不能在纯 XNB 里无条件落下去，保守跳过并报出来。
                if (change["When"] is JObject when && when.HasValues)
                {
                    notes.Add($"SpaceCore 对象扩展：带条件的补丁未映射（`{when.ToString(Newtonsoft.Json.Formatting.None)}`）");
                    continue;
                }
                if (change["Entries"] is not JObject entries) continue;

                foreach (JProperty entry in entries.Properties())
                {
                    string dedup = entry.Name + "|" + entry.Value.ToString(Newtonsoft.Json.Formatting.None);
                    if (!seen.Add(dedup)) continue;
                    if (entry.Value is not JObject data) continue;

                    string id = ResolveId(entry.Name, uniqueId);
                    if (id.Contains("{{", StringComparison.Ordinal))
                    {
                        notes.Add($"SpaceCore 对象扩展：`{entry.Name}` 仍有未展开令牌，跳过");
                        continue;
                    }
                    if (objects[id] is not JObject target)
                    {
                        notes.Add($"SpaceCore 对象扩展：Data/Objects 里没有 `{id}`，跳过");
                        continue;
                    }

                    bool changed = false;
                    if (data["CanBeTrashed"]?.Type == JTokenType.Boolean)
                    {
                        target["CanBeTrashed"] = data["CanBeTrashed"]!.DeepClone();
                        changed = true;
                    }
                    if (data["CanBeGivenAsGift"]?.Type == JTokenType.Boolean)
                    {
                        target["CanBeGivenAsGift"] = data["CanBeGivenAsGift"]!.DeepClone();
                        changed = true;
                    }
                    // SpaceCore 的 CanBeShipped=false 对应原版“不进出货箱”；反过来解除排除。
                    if (data["CanBeShipped"]?.Type == JTokenType.Boolean)
                    {
                        target["ExcludeFromShippingCollection"] = !data["CanBeShipped"]!.Value<bool>();
                        changed = true;
                    }
                    if (data["HideFromShippingCollection"]?.Type == JTokenType.Boolean)
                    {
                        target["ExcludeFromShippingCollection"] =
                            data["HideFromShippingCollection"]!.Value<bool>();
                        changed = true;
                    }

                    if (changed) applied++;
                    if (!changed || data.Properties().Any(p => !Supported.Contains(p.Name)))
                    {
                        string ignored = string.Join("、", data.Properties()
                            .Select(p => p.Name).Where(n => !Supported.Contains(n)).Take(4));
                        if (ignored.Length > 0)
                            notes.Add($"SpaceCore 对象扩展 `{id}`：未映射 {ignored}");
                    }
                }
            }
        }

        return applied;
    }

    private static string ResolveId(string raw, string uniqueId) =>
        Regex.Replace(raw, @"\{\{ModId\}\}", uniqueId, RegexOptions.IgnoreCase);

    private static IEnumerable<JToken> Descendants(JToken root)
    {
        yield return root;
        foreach (JToken child in root.Children())
        foreach (JToken descendant in Descendants(child))
            yield return descendant;
    }
}
