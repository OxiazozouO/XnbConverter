using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tracing;

/// <summary>反查哪些 CP 补丁覆盖了某张地图的某个格子。</summary>
public static class PatchProvenance
{
    public sealed record PatchInfo(
        string Pack, string UniqueId, string Source, int Index, string Action, string Target,
        string? When, string? FromFile, int[]? ToArea, List<string> MapTiles,
        string? PatchMode, string? Update, bool CoversTile);

    public static List<PatchInfo> Find(string modsRoot, string mapAssetName, int x, int y,
        Action<string>? log = null)
    {
        string wanted = Normalize(mapAssetName);
        var result = new List<PatchInfo>();
        var mods = new InstalledModIndex(modsRoot);
        string[] manifests = Directory.EnumerateFiles(modsRoot, "manifest.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        int done = 0;

        foreach (string manifest in manifests)
        {
            string dir = Path.GetDirectoryName(manifest)!;
            JObject obj;
            try { obj = JsoncReader.ParseFile(manifest); }
            catch { continue; }
            string? uid = obj["UniqueID"]?.Value<string>();
            string? framework = obj["ContentPackFor"]?["UniqueID"]?.Value<string>();
            if (uid == null || framework == null
                || !framework.StartsWith("pathoschild.contentpatcher", StringComparison.OrdinalIgnoreCase))
                continue;

            ContentPack pack;
            try
            {
                pack = ContentPack.Load(dir, deferConditionalIncludes: false,
                    hasMod: id => mods.Contains(id));
            }
            catch (Exception ex)
            {
                log?.Invoke($"跳过包 {dir}：{ex.Message}");
                continue;
            }

            foreach (ChangePatch patch in pack.Patches)
            {
                if (patch.Target == null) continue;
                bool targets = patch.Target.Split(',')
                    .Select(t => Normalize(t.Trim()))
                    .Any(t => t.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                if (!targets) continue;

                int[]? toArea = null;
                if (patch.Raw["ToArea"] is JObject
                    && EditImageApplier.TryParseAreas(patch, out _, out ImageArea? area, out _)
                    && area != null)
                    toArea = new[] { area.Value.X, area.Value.Y, area.Value.Width, area.Value.Height };

                var mapTiles = new List<string>();
                bool covers = toArea is { Length: 4 }
                              && x >= toArea[0] && x < toArea[0] + toArea[2]
                              && y >= toArea[1] && y < toArea[1] + toArea[3];
                if (patch.Raw["MapTiles"] is JArray tiles)
                {
                    foreach (JToken t in tiles)
                    {
                        if (t is not JObject tile) continue;
                        if (!TryParsePosition(tile["Position"], out int mx, out int my)) continue;
                        mapTiles.Add($"{mx},{my}");
                        if (mx == x && my == y) covers = true;
                    }
                }

                result.Add(new PatchInfo(
                    pack.Name, uid, Rel(pack.RootPath, patch.SourceFile), patch.Index,
                    patch.Action, patch.Target,
                    patch.When?.ToString(Newtonsoft.Json.Formatting.None),
                    patch.FromFile, toArea, mapTiles,
                    patch.Raw["PatchMode"]?.Value<string>(),
                    patch.Update, covers));
            }

            done++;
            if (done % 50 == 0) log?.Invoke($"  已扫描 {done} 个 CP 包");
        }

        return result
            .OrderByDescending(p => p.CoversTile)
            .ThenBy(p => p.Pack, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Source, StringComparer.Ordinal)
            .ThenBy(p => p.Index)
            .ToList();
    }

    private static string Normalize(string name)
    {
        name = name.Replace('\\', '/').Trim('/');
        return name.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? name : "Maps/" + name;
    }

    private static string Rel(string root, string file)
    {
        try { return Path.GetRelativePath(root, file).Replace('\\', '/'); }
        catch { return file; }
    }

    private static bool TryParsePosition(JToken? token, out int x, out int y)
    {
        x = y = 0;
        if (token is JObject o)
            return int.TryParse(o["X"]?.ToString(), out x) && int.TryParse(o["Y"]?.ToString(), out y);
        if (token is JValue { Type: JTokenType.String } v)
        {
            string[] parts = (v.Value<string>() ?? "").Split(',');
            return parts.Length == 2
                   && int.TryParse(parts[0].Trim(), out x)
                   && int.TryParse(parts[1].Trim(), out y);
        }
        return false;
    }
}
