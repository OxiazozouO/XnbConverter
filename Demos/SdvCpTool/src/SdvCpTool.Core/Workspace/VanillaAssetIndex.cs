using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Workspace;

/// <summary>
/// 原版资产名索引，数据来自游戏 <c>Content/ContentHashes.json</c>。
/// 用来区分"目标资产不存在因为树不完整"与"目标资产是 mod 新增的"。
/// </summary>
public sealed class VanillaAssetIndex
{
    private readonly HashSet<string> _names;

    public int Count => _names.Count;

    /// <summary>从 ContentHashes.json 构造。文件不存在时抛 <see cref="FileNotFoundException"/>。</summary>
    public VanillaAssetIndex(string contentHashesJsonPath)
    {
        if (!File.Exists(contentHashesJsonPath))
            throw new FileNotFoundException("找不到 ContentHashes.json", contentHashesJsonPath);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JObject root = JsoncReader.ParseFile(contentHashesJsonPath);
        foreach (JProperty prop in root.Properties())
        {
            string key = prop.Name.Replace('\\', '/');
            if (key.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
                key = key[..^4];
            names.Add(key);
        }
        _names = names;
    }

    /// <summary>资产名是否属于原版。</summary>
    public bool Contains(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName)) return false;
        return _names.Contains(assetName.Replace('\\', '/').Trim('/'));
    }
}
