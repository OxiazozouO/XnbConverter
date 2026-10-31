using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// **补丁字段守卫**：一条 CP 补丁里出现了我们不认识的键就**报出来**。
/// <para>
/// <b>为什么需要它</b>：CP 的补丁字段是**会长的**（`TargetLocale` 就是后加的）。漏读一个字段的后果
/// 与 FTM 那次一模一样 —— **静默按"它不存在"处理**，写出来的东西与作者意图不同，而报告里一声不响。
/// 实测那次 FTM 的教训：`&lt;段&gt;SpawnEnabled` 与 `File_Conditions` 两个字段我们压根没读，
/// 语料里恰好不触发才没出事（见 `2026-10-11-ftm-can-land-forage-buried.md` §6）。
/// </para>
/// <para>
/// <b>白名单的来源</b>：CP 自己的解析器 <c>ContentPatcher.Framework/PatchLoader.cs</c>
/// 里出现的字段名**全集**（反编译 <c>D:\tmp\cp_decompile</c>），另加 <c>LogName</c>
/// （CP 文档里的调试字段）。**宁可多列**：多列一个只是少报一次警，漏列一个会天天误报。
/// </para>
/// </summary>
public static class PatchFieldGuard
{
    /// <summary>CP 认识的补丁字段（大小写敏感，与 CP 一致）。</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        // CP 的 `PatchLoader.cs` 里出现过的全部键
        "Action", "Enabled", "Priority", "Update", "LogName",
        "Target", "TargetField", "TargetLocale", "When",
        "FromFile", "FromArea", "ToArea", "PatchMode",
        "Entries", "Fields", "TextOperations", "MoveEntries",
        "MapProperties", "MapTiles", "AddWarps", "SetProperties", "SetTilesheet",
        "Remove", "ReplaceMode", "Search", "Operation", "Value", "Delimiter",
        "Layer", "Position", "SetIndex", "ToPosition", "BeforeID", "AfterID", "ID",
        "X", "Y", "Width", "Height", "Top", "Bottom", "HasMod",
    };

    /// <summary>
    /// CP 有、**但我们没实现**的字段（说到做到：认识它 ≠ 处理它）。命中就单报一条，
    /// 免得读者把"没报警"当成"处理了"。
    /// <para>
    /// <c>TargetLocale</c>：CP 用它把补丁只作用于某个语言版本。**本语料 0 处**
    /// （`2026-10-09-needsreview-top-buckets.md` 记过），所以一直没做；真遇到时这条会先响。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> KnownButUnhandled =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TargetLocale"] = "CP 用它把补丁限定到某个语言版本，我们**没有实现**（本语料 0 处）",
        };

    /// <summary>
    /// 把补丁里的**字段名**统一成 CP 的规范拼写（大小写不敏感地找）。
    /// <para>
    /// <b>为什么需要</b>：CP 是把补丁对象**反序列化进 `PatchConfig`** 的（JSON.NET 的属性匹配
    /// **大小写不敏感**），所以 `"when"` / `"Patchmode"` 在 CP 那边**照常生效**。
    /// 而我们是按名字直接取的 —— 实测本语料里就有模组这么写（SVE 的 2 条 `"when"`、
    /// 10 条 `"Patchmode": "Overlay"`），**不规范化就会把它们当成"没写"**：
    /// `Overlay` 被当成默认的 `Replace` ⇒ **整块地图/图被覆盖**，条件被丢掉 ⇒ 该门控的内容变成无条件。
    /// </para>
    /// <para>
    /// 只动**字段名**，值一律不碰；只认 <see cref="Known"/> 与 <see cref="KnownButUnhandled"/> 里的名字
    /// （别的键原样留着，交给 <see cref="Check"/> 去报）。
    /// </para>
    /// </summary>
    public static JObject Normalize(JObject raw)
    {
        // ★ **按文档顺序"最后一个赢"**：CP 是反序列化进 `PatchConfig`，一条补丁里要是
        //   `when` 与 `When` 并存（JSON 允许重名键，Newtonsoft 取最后一个），CP 拿到的是**最后那个值**。
        //   所以这里按出现顺序收集、后出现的覆盖先出现的，最后统一写回 ——
        //   只"跳过已存在的规范键"会把**先**出现的值留下，与 CP 不一致（审查逮到过）。
        var canonicalValues = new Dictionary<string, JToken>(StringComparer.Ordinal);
        var rename = new List<JProperty>();
        foreach (JProperty p in raw.Properties().ToList())
        {
            string? canonical = Known.Contains(p.Name) || KnownButUnhandled.ContainsKey(p.Name)
                                || KnownElsewhere.ContainsKey(p.Name)
                ? p.Name
                : Known.FirstOrDefault(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                  ?? KnownButUnhandled.Keys.FirstOrDefault(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                  ?? KnownElsewhere.Keys.FirstOrDefault(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            if (canonical == null) continue;                 // 不认识的键原样留着，交给 Check 去报

            canonicalValues[canonical] = p.Value;            // 后出现的覆盖先出现的（= CP 的口径）
            if (!string.Equals(p.Name, canonical, StringComparison.Ordinal))
                rename.Add(p);                               // 拼写不对的记下来，值已经收好
        }

        // 先删再写：边遍历边增删会炸；删干净之后再写才是"最后一个赢"的结果
        foreach (JProperty p in rename) p.Remove();
        foreach ((string name, JToken value) in canonicalValues) raw[name] = value;
        return raw;
    }

    /// <summary>
    /// CP 里**有**这个字段，但**不在补丁对象上**（在别的配置对象里）。写在补丁上 CP 也不会读 ——
    /// 报出来免得有人当成我们的漏读去"修"。
    /// <para>
    /// <c>MigrateLegacyNames</c>：属于 `CustomLocations`（`ConfigModels/CustomLocationConfig.cs:11`），
    /// 而 SVE 把它写在了 `Load Maps/…` 补丁上（13 处）—— **CP 那边同样不生效**。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> KnownElsewhere =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MigrateLegacyNames"] = "它属于 `CustomLocations`，不在补丁对象上（`CustomLocationConfig.cs:11`）",
        };

    /// <summary>
    /// 扫一遍所有补丁，把**不认识的键**收成警告（每条补丁只报一次）。
    /// </summary>
    public static void Check(IEnumerable<ChangePatch> patches, List<string> warnings)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ChangePatch patch in patches)
        {
            foreach (string key in patch.Raw.Properties().Select(p => p.Name))
            {
                // 去重键带**全路径**：不同目录下的同名文件（Include 出来的 content.json）不该互相吞掉报警
                // ⚠ 去重键用**全路径**（不同目录下的同名 `content.json` 不该互相吞掉报警），
                //   打给人看的标签只取最后两段 —— 全路径太长，报告里一行会被撑爆。
                string dedupKey = $"{patch.SourceFile}#{patch.Index}";
                string where = $"{ShortPath(patch.SourceFile)}#{patch.Index}";
                if (KnownButUnhandled.TryGetValue(key, out string? why))
                {
                    if (!seen.Add(key + "@" + dedupKey)) continue;
                    warnings.Add($"{where}（{patch.Action}）：字段 `{key}` —— {why}，**没生效**");
                    continue;
                }
                if (Known.Contains(key)) continue;
                if (KnownElsewhere.TryGetValue(key, out string? elsewhere))
                {
                    if (!seen.Add(key + "@" + dedupKey)) continue;
                    warnings.Add($"{where}（{patch.Action}）：字段 `{key}` {elsewhere} —— "
                                 + "**CP 自己也不会读它**，所以这不是我们的漏读（照实报出来，别去「修」）");
                    continue;
                }
                if (!seen.Add(key + "@" + dedupKey)) continue;
                warnings.Add($"{where}（{patch.Action}）：补丁里有**我们不认识的字段** `{key}` —— "
                             + "它没被处理，写出来的东西可能与该字段的意图不符。"
                             + "要么是我们漏读了 CP 新加的字段，要么这条补丁来自别的框架");
            }
        }
    }

    /// <summary>报告里用的短标签：`<上一层目录>/<文件名>`（全路径太长）。</summary>
    private static string ShortPath(string path)
    {
        string file = Path.GetFileName(path);
        string? dir = Path.GetFileName(Path.GetDirectoryName(path));
        return string.IsNullOrEmpty(dir) ? file : dir + "/" + file;
    }
}
