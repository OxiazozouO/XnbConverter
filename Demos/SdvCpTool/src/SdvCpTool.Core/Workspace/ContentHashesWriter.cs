using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Workspace;

/// <summary>
/// 维护 <c>ContentHashes.json</c>。游戏的 <c>DoesAssetExist</c> 只查这张表的<b>键</b>
/// （见 LocalizedContentManager.cs:329/144），缺键会让新资产直接抛 ContentLoadException。
/// </summary>
public static class ContentHashesWriter
{
    /// <summary>
    /// 把新资产的 key 补进清单。已存在的键不动（保留原 MD5）。
    /// 数据（<c>.json</c>）、图片（<c>.png</c>）、地图（<c>.tbin</c>）都要补 ——
    /// 三者回包后都是同名 <c>.xnb</c>。
    /// </summary>
    public static void AddKeys(JObject hashes, IEnumerable<string> relativePaths)
    {
        foreach (string rel in relativePaths)
        {
            string normalized = rel.Replace('\\', '/');
            string? ext = AssetExtension(normalized);
            if (ext == null) continue;

            string key = normalized[..^ext.Length] + ".xnb";
            // 资产名大小写不敏感：原表里已有仅大小写不同的键时不要再加一条
            // （否则同一资产在清单里出现两条，打包/校验会各看一份）。
            if (hashes.Properties().Any(p =>
                    p.Name.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;

            // 值（MD5）不参与存在性判断，留空串即可
            hashes[key] = "";
        }
    }

    /// <summary>回包成 <c>.xnb</c> 的资产扩展名；非资产文件（如 <c>.config</c>）返回 null。</summary>
    private static string? AssetExtension(string normalizedPath) =>
        normalizedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ".json"
        : normalizedPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png"
        // 地图源文件是 `.tbin`，回包后同样是 `.xnb`；漏掉它 ⇒ **所有新建地图**都没有键
        // （实测 Custom_LavenderMeadows_Spa / ARVAAHall 等）。
        : normalizedPath.EndsWith(".tbin", StringComparison.OrdinalIgnoreCase) ? ".tbin"
        : null;
}
