using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 把 <c>Data/Locations</c> 补丁的 <c>TargetField</c> 首段（地点名）归一成树里已有键的写法。
/// <para>
/// 游戏的 `Game1.getLocationFromName` 用忽略大小写比较，但地点数据是按字典**精确**取键；
/// 实测 FTM 的 `MapName: "RailRoad"` 写出了 `Data/Locations.RailRoad` 这个不存在的键
/// （真正的地点叫 `Railroad`），6 条觅食/埋藏物因此不可达。
/// </para>
/// 只做**忽略大小写唯一匹配**；没有匹配时不猜。
/// </summary>
public static class LocationKeyNormalizer
{
    public static int Apply(IEnumerable<ChangePatch> patches, JObject locations)
    {
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JProperty p in locations.Properties())
            canonical.TryAdd(p.Name, p.Name);

        int changed = 0;
        foreach (ChangePatch patch in patches)
        {
            if (!Normalize(patch.Target).Equals("Data/Locations", StringComparison.OrdinalIgnoreCase))
                continue;

            JToken? field = patch.Raw["TargetField"];
            if (field is JArray arr && arr.Count > 0 && arr[0].Type == JTokenType.String)
            {
                string raw = arr[0].Value<string>() ?? "";
                if (canonical.TryGetValue(raw.Trim(), out string? name) && name != raw)
                {
                    arr[0] = name;
                    changed++;
                }
            }
            else if (field is JValue { Type: JTokenType.String } value)
            {
                string raw = value.Value<string>() ?? "";
                if (canonical.TryGetValue(raw.Trim(), out string? name) && name != raw)
                {
                    patch.Raw["TargetField"] = name;
                    changed++;
                }
            }
        }
        return changed;
    }

    private static string Normalize(string? target) =>
        (target ?? "").Replace('\\', '/').Trim('/');
}
