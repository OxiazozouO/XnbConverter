using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 把 `Data/Characters.Appearance[].Sprite/Portrait` 里的 `SMAPI/&lt;uid&gt;/...`
/// **物化成普通内容资产**，并改写基础与所有语言变体。
/// <para>
/// 原版 `AnimatedSprite.loadTexture` 直接把字段当资产名加载；纯 XNB 里
/// `SMAPI/...` 文件虽然打得出，但 SMAPI 会拦截该前缀、原版也没有 CP 的内容管理器。
/// 这里把它落到语义正常的 <c>Characters/&lt;NPC&gt;/&lt;文件名&gt;</c> /
/// <c>Portraits/&lt;NPC&gt;/&lt;文件名&gt;</c>，不再依赖任何框架前缀。
/// </para>
/// </summary>
public static class NpcAssetMaterializer
{
    public static int Materialize(OutputTree output, IEnumerable<string>? changedFiles = null)
    {
        var files = new List<string>();
        if (changedFiles == null)
        {
            string dataDir = Path.Combine(output.OutputRoot, "Data");
            if (Directory.Exists(dataDir))
                files.AddRange(Directory.EnumerateFiles(dataDir, "Characters*.json")
                    .Select(p => Path.GetRelativePath(output.OutputRoot, p).Replace('\\', '/')));

            // `Data/Characters.zh-CN.json` 可能是本次运行刚 Stage 的，磁盘上还没有。
            foreach (string rel in output.InputFiles)
                if (rel.StartsWith("Data/Characters", StringComparison.OrdinalIgnoreCase) &&
                    rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    !files.Contains(rel, StringComparer.OrdinalIgnoreCase))
                    files.Add(rel);
        }
        else
        {
            files.AddRange(changedFiles!
                .Select(x => x.Replace('\\', '/'))
                .Where(x => x.StartsWith("Data/Characters", StringComparison.OrdinalIgnoreCase)
                            && x.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
        }

        int changed = 0;
        foreach (string rel in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? text = output.ReadTextOrNull(rel);
            if (text == null) continue;
            JObject root;
            try { root = JObject.Parse(text); }
            catch { continue; }

            bool dirty = false;
            foreach (JProperty npc in root.Properties())
            {
                if (npc.Value is not JObject entry ||
                    entry["Appearance"] is not JArray appearances) continue;
                foreach (JObject appearance in appearances.OfType<JObject>())
                {
                    foreach (string field in new[] { "Sprite", "Portrait" })
                    {
                        string? value = appearance.Value<string>(field);
                        if (value == null || !value.StartsWith("SMAPI/", StringComparison.OrdinalIgnoreCase))
                            continue;
                        string? target = MaterializeOne(output, value, field, npc.Name);
                        if (target == null) continue;
                        appearance[field] = target;
                        dirty = true;
                    }
                }
            }

            if (dirty)
            {
                output.StageJson(rel, root);
                changed++;
            }
        }
        return changed;
    }

    private static string? MaterializeOne(OutputTree output, string value, string field, string npc)
    {
        string rel = value["SMAPI/".Length..].Replace('\\', '/');
        string name = Path.GetFileName(rel);
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        string folder = field == "Sprite" ? "Characters" : "Portraits";
        string target = $"{folder}/{npc}/{name}";

        byte[]? source = ReadSource(output, rel);
        if (source == null) return null;
        if (!output.Exists(target + ".png"))
            output.StageBytes(target + ".png", source);
        byte[]? config = ReadSource(output, rel + ".config");
        if (config != null && !output.Exists(target + ".config"))
            output.StageBytes(target + ".config", config);
        return target;
    }

    private static byte[]? ReadSource(OutputTree output, string rel)
    {
        foreach (string candidate in new[] { rel, rel + ".png", rel + ".png.png" })
        {
            byte[]? bytes = output.ReadBytesOrNull("SMAPI/" + candidate);
            if (bytes != null) return bytes;
        }
        return null;
    }
}
