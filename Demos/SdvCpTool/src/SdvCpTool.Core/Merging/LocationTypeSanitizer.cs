using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 把 <c>Data/Locations</c> 里**依赖 DLL 的程序集限定类型**退化成原版 <c>GameLocation</c>。
/// <para>
/// 纯 XNB 没有 SMAPI/模组程序集，原版 <c>Game1.CreateGameLocation</c> 拿到
/// <c>Type: "CapeStardewCode.CustomLavaLogic, CapeStardewCode"` 这样的字符串会直接拒绝创建地点
/// （实机：<c>Couldn't create the 'Custom_CapeKeyMaze' location</c>）。
/// 退化后地图本身仍能加载，只是失去模组自定义的地形逻辑 —— 比整个地点建不起来好。
/// </para>
/// </summary>
public static class LocationTypeSanitizer
{
    public static int Sanitize(OutputTree output, IEnumerable<string>? files = null)
    {
        int fixedCount = 0;
        foreach (string rel in files ?? output.InputFiles)
        {
            string normalized = rel.Replace('\\', '/');
            if (!normalized.StartsWith("Data/Locations", StringComparison.OrdinalIgnoreCase)
                || !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            string? text = output.ReadTextOrNull(normalized);
            if (text == null) continue;

            JObject root;
            try { root = JObject.Parse(text); }
            catch { continue; }

            bool changed = false;
            foreach (JProperty entry in root.Properties())
            {
                if (entry.Value is not JObject location) continue;

                // 1.6 的 `Data/Locations` 把地图/类型放在 `CreateOnLoad` 里；
                // 老式/部分包也可能写顶层 `Type`，两处都要看。
                foreach (JObject holder in new[] { location, location["CreateOnLoad"] as JObject }
                             .Where(x => x != null)!)
                {
                    if (holder.Value<string>("Type") is not string type || type.Length == 0) continue;
                    if (type.StartsWith("StardewValley.", StringComparison.Ordinal)) continue;

                    holder["Type"] = "StardewValley.GameLocation";
                    fixedCount++;
                    changed = true;
                }
            }

            if (changed) output.StageJson(normalized, root);
        }
        return fixedCount;
    }
}
