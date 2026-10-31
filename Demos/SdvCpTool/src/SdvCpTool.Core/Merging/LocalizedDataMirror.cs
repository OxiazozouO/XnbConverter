using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 把所有 `Data/**/&lt;asset&gt;.&lt;locale&gt;.json` 缺失的 base 顶层键补回来。
/// <para>
/// 游戏的 <c>LocalizedContentManager</c> 对本地化文件是**整份替换**，不会按 key 回落 base。
/// 如果中文文件有 865 个地点、日文只有 353 个，日文客户端就真的少了 512 个地点；
/// 同理会出现本地化的任务、特别订单、礼物口味、节日行缺失。
/// </para>
/// <para>
/// 只**补缺键**，不覆盖本地化已有内容；数组只补带 <c>Id</c> 的条目，避免把没有身份的对象重复塞进去。
/// 这不是把英文翻译成当前语言，而是保证“结构存在 + 文本回落 base”。
/// </para>
/// </summary>
public static class LocalizedDataMirror
{
    public static int Mirror(OutputTree output, List<string> notes)
    {
        int changed = 0;
        // 链式转换每包一个进程；上一包已经镜像过的本地化文件不会因为这一包而变。
        // 只处理“这一包写过的本地化文件”以及“这一包写过的 base 对应的本地化变体”，
        // 把每包 O(整棵 Data 树) 的读取/解析降到 O(本轮写入)。
        var planned = new HashSet<string>(
            output.PlannedWrites.Keys.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var changedBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rel in planned)
        {
            if (TryBasePath(rel, out string baseRel))
            {
                changedBases.Add(Normalize(baseRel));
                changedBases.Add(rel);
            }
            else changedBases.Add(rel);
        }

        var files = output.InputFiles
            .Concat(output.PlannedWrites.Keys)
            .Select(Normalize)
            .Where(IsLocalizedDataFile)
            .Where(rel => planned.Contains(rel)
                          || (TryBasePath(rel, out string b) && changedBases.Contains(Normalize(b))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        // 同一份 base 常有 7 个语言变体：base 只解析一次（优先直接拿内存里的对象，
        // 避免「把 pending 序列化成文本再解析回来」），各变体复用它。
        foreach (var group in files.GroupBy(rel => TryBasePath(rel, out string b) ? Normalize(b) : null))
        {
            if (group.Key == null) continue;
            long t0 = SdvCpTool.Core.Diagnostics.Prof.Now();
            JToken? baseToken;
            try { baseToken = output.ReadJsonOrNull(group.Key); }
            catch (Exception ex)
            {
                notes.Add($"本地化 Data 补键 `{group.Key}` 跳过：{ex.Message}");
                continue;
            }
            if (SdvCpTool.Core.Diagnostics.Prof.On)
                SdvCpTool.Core.Diagnostics.Prof.AddSince("镜像.base解析", t0);
            if (baseToken == null) continue;

            foreach (string rel in group)
            {
                t0 = SdvCpTool.Core.Diagnostics.Prof.Now();
                JToken? localToken = output.ReadJsonOrNull(rel);
                if (localToken == null) continue;
                if (SdvCpTool.Core.Diagnostics.Prof.On)
                    SdvCpTool.Core.Diagnostics.Prof.AddSince("镜像.locale解析", t0);

                try
                {
                    t0 = SdvCpTool.Core.Diagnostics.Prof.Now();
                    int n = MirrorToken(baseToken, localToken);
                    if (SdvCpTool.Core.Diagnostics.Prof.On)
                        SdvCpTool.Core.Diagnostics.Prof.AddSince("镜像.补键", t0);
                    if (n == 0) continue;
                    t0 = SdvCpTool.Core.Diagnostics.Prof.Now();
                    bool staged = output.TryStageJson(rel, localToken, out string? error);
                    if (SdvCpTool.Core.Diagnostics.Prof.On)
                        SdvCpTool.Core.Diagnostics.Prof.AddSince("镜像.暂存", t0);
                    if (staged)
                        changed += n;
                    else
                        notes.Add($"本地化 Data 补键 `{rel}` 失败：{error}");
                }
                catch (Exception ex)
                {
                    notes.Add($"本地化 Data 补键 `{rel}` 跳过：{ex.Message}");
                }
            }
        }

        return changed;
    }

    /// <summary>递归补对象/数组的缺键；返回新增的元素数。</summary>
    public static int MirrorToken(JToken baseToken, JToken localToken)
    {
        if (baseToken is JObject b && localToken is JObject l)
        {
            int changed = 0;
            foreach (JProperty p in b.Properties())
            {
                if (l.Property(p.Name) == null)
                {
                    l[p.Name] = p.Value.DeepClone();
                    changed++;
                }
                else
                {
                    // 已有对象/数组也要继续补嵌套字段（如 Locations 的 Fish、Characters 的
                    // Appearance[]、Shops.Items[]），否则本地化只“有行、没内容”。
                    changed += MirrorToken(p.Value, l[p.Name]!);
                }
            }
            return changed;
        }

        if (baseToken is JArray ba && localToken is JArray la)
        {
            var byId = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject item in la.OfType<JObject>())
            {
                string? id = item.Value<string>("Id");
                if (!string.IsNullOrEmpty(id)) byId[id] = item;
            }

            int changed = 0;
            foreach (JObject item in ba.OfType<JObject>())
            {
                string? id = item.Value<string>("Id");
                if (string.IsNullOrEmpty(id)) continue;
                if (!byId.TryGetValue(id, out JObject? localItem))
                {
                    var clone = (JObject)item.DeepClone();
                    la.Add(clone);
                    byId[id] = clone;
                    changed++;
                    continue;
                }
                changed += MirrorToken(item, localItem);
            }
            return changed;
        }

        return 0;
    }

    private static string Normalize(string rel) => rel.Replace('\\', '/').TrimStart('/');

    private static bool IsLocalizedDataFile(string rel)
    {
        if (!rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        // Data、Strings、Characters/Dialogue 都是整份替换的本地化资产；
        // 只补 key 不会丢已有译文，缺 key 则会让对应文本直接消失。
        if (!rel.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)
            && !rel.StartsWith("Strings/", StringComparison.OrdinalIgnoreCase)
            && !rel.StartsWith("Characters/Dialogue/", StringComparison.OrdinalIgnoreCase))
            return false;
        string name = Path.GetFileNameWithoutExtension(rel);
        return LocaleVariants.Suffixes.Any(s =>
            name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryBasePath(string rel, out string baseRel)
    {
        baseRel = rel;
        string name = Path.GetFileNameWithoutExtension(rel);
        foreach (string suffix in LocaleVariants.Suffixes)
        {
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            string stem = name[..^suffix.Length];
            baseRel = rel[..^Path.GetFileName(rel).Length] + stem + ".json";
            return !string.Equals(rel, baseRel, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
