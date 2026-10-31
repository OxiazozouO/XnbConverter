using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// 已知 NPC 名集合。用途：判定 <c>Data/Festivals/&lt;id&gt;</c> 里某个键是不是"NPC 对话键" ——
/// 游戏只按 <c>actor.Name</c> 取节日对话（<c>Event.cs:4398-4412</c>），其余键（<c>mainEvent</c>/<c>set-up</c>…）
/// 是事件脚本，往上面套 <c>$query</c> 会把节日弄坏。
/// </summary>
public static class NpcNames
{
    /// <summary>
    /// 原版 <c>Data/Characters</c> 的键（资源树里能读到时）∪ 内容包所有打在
    /// <c>Data/Characters</c> 上的 <c>Entries</c> 键（mod 自建 NPC，资源树里没有）。
    /// </summary>
    public static IReadOnlySet<string> Build(ResourceTree tree, IEnumerable<ChangePatch> patches)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        string? resolved = tree.Resolve("Data/Characters");
        if (resolved != null && File.Exists(resolved))
        {
            try
            {
                if (JToken.Parse(File.ReadAllText(resolved)) is JObject characters)
                    foreach (JProperty p in characters.Properties()) names.Add(p.Name);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // 读不了原版清单就不加 —— 不能凭猜测认定某个键是 NPC 名。
            }
        }

        foreach (ChangePatch patch in patches)
        {
            if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)) continue;
            bool targetsCharacters = (patch.Target ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(t => t.Replace('\\', '/').Trim().Equals("Data/Characters", StringComparison.OrdinalIgnoreCase));
            if (!targetsCharacters) continue;
            if (patch.Raw["Entries"] is not JObject entries) continue;
            foreach (JProperty entry in entries.Properties()) names.Add(entry.Name);
        }

        return names;
    }
}
