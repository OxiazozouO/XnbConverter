using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Reporting;

/// <summary>
/// 产出树自检：**原版是数组的字段，我们有没有写成对象**。
/// <para>
/// 这一类是「结构性退化」—— 形状位置没变、容器类型变了，游戏按 `List&lt;T&gt;` 反序列化会失败或忽略。
/// 实测踩过：<c>Data/Locations</c> 的 <c>Fish</c>（模型里是 <c>List&lt;SpawnFishData&gt;</c>）
/// 被合成了字典，两个地点的鱼全部静默失效。
/// </para>
/// <para>
/// <b>为什么只留这一条</b>（试过的都不行，记下来免得再走一遍）：
/// 更宽的判据噪音大到不可用 ——
/// <list type="bullet">
/// <item>「形状对不上就报」：2267 份资产报 116 处，绝大多数是 <c>CustomFields</c> 这种
/// 原版 <c>null</c>、产出 <c>{}</c> 的假阳性（对字典字段两者都合法）。</item>
/// <item>「排除 null 后比形状+类型」：反而涨到 <b>9145</b> 处 ——
/// <c>null</c> / <c>[]</c> / <c>{}</c> 都是「空」但类型不同，全被算成不符。</item>
/// </list>
/// 收窄成「**原版数组 → 产出对象**」之后，同一份语料上 <b>0 处</b>误报、且确实覆盖真问题。
/// 再宽的判据要做对，得走**模型驱动**（从 `D:\sdv_gd` 的模型类取字段真类型），
/// 而不是拿原版数据反推 —— 那需要 class→asset 的映射，是另一块工作。
/// </para>
/// </summary>
public static class OutputShapeAudit
{
    /// <summary>一处「该是数组结果成了对象」的字段。</summary>
    public sealed record Finding(string Asset, string Path, string Field, string Expected);

    public sealed class Audit
    {
        public int CheckedAssets;
        public int SkippedNoVanilla;

        /// <summary>
        /// **输入树与产出树是同一棵** ⇒ 无从对照，整体跳过（链式跑法 `--append` 就是这种）。
        /// 判据与说明同 `ConditionPlacementAudit.Audit.SkippedSameTree` —— **0 是没比，不是没问题**。
        /// </summary>
        public bool SkippedSameTree;
        public readonly List<Finding> Findings = new();
    }

    /// <summary>对一棵产出树跑自检。</summary>
    public static Audit Run(OutputTree output, int maxDepth = 6, AuditParseCache? parseCache = null,
        AuditCache? auditCache = null) =>
        Run(output.OutputRoot, output.InputRoot, maxDepth, parseCache, auditCache,
            AuditCache.JsonWrittenBy(output));

    /// <summary>
    /// 对「产出根 + 原版根」跑自检。**分两个根是为了能手工业余跑** ——
    /// <see cref="OutputTree"/> 的构造要求输出目录为空，已有产物就没法再包一层。
    /// </summary>
    public static Audit Run(string outputRoot, string inputRoot, int maxDepth = 6,
        AuditParseCache? parseCache = null, AuditCache? auditCache = null,
        IReadOnlyCollection<string>? writtenJson = null)
    {
        // ★ 同一个根 ⇒ 拿产出跟它自己比，永远 0（链式跑法 --append 就是这种）；报出来，别让 0 被读成干净。
        if (ConditionPlacementAudit.SameTree(outputRoot, inputRoot))
            return new Audit { SkippedSameTree = true };

        // 「这一轮写过哪些」由落盘账本说了算：写过的必须重算，没写过的必然没变 ——
        // 比拿「大小 + 修改时间」猜更准，也省掉逐文件 stat。
        HashSet<string> changed = writtenJson == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(writtenJson, StringComparer.OrdinalIgnoreCase);

        var audit = new Audit();
        JToken? TryParseCached(string p) => parseCache?.Parse(p) ?? TryParse(p);

        foreach (string rel in Directory
                     .EnumerateFiles(outputRoot, "*.json", SearchOption.AllDirectories)
                     .Select(p => Path.GetRelativePath(outputRoot, p).Replace('\\', '/'))
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string producedPath = Path.Combine(outputRoot, rel);

            if (auditCache != null && !changed.Contains(rel)
                && auditCache.TryGet("shape", rel, out AuditCache.Entry hit))
            {
                if (hit.Skipped) audit.SkippedNoVanilla++;
                else if (hit.Checked)
                {
                    audit.CheckedAssets++;
                    foreach (string[] row in hit.Findings)
                        audit.Findings.Add(new Finding(row[0], row[1], row[2], row[3]));
                }
                continue;
            }

            string vanillaPath = Path.Combine(inputRoot, rel);
            if (!File.Exists(vanillaPath))
            {
                audit.SkippedNoVanilla++;   // 原版没有这份资产 → 无从对照，不妄下结论
                auditCache?.Put("shape", rel, new AuditCache.Entry(Skipped: true, Checked: false,
                    new List<string[]>()));
                continue;
            }

            JToken? produced = TryParseCached(producedPath);
            JToken? vanilla = TryParseCached(vanillaPath);
            if (produced == null || vanilla == null)
            {
                auditCache?.Put("shape", rel, new AuditCache.Entry(Skipped: false, Checked: false,
                    new List<string[]>()));
                continue;
            }

            audit.CheckedAssets++;

            // 原版里「是数组」的字段：字段名 → 它出现在哪些路径形状上
            var arrayShapes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            CollectArrayFields(vanilla, "", arrayShapes, maxDepth);

            var found = new List<Finding>();
            FindObjectWhereArrayExpected(rel, produced, "", arrayShapes, found, maxDepth);
            audit.Findings.AddRange(found);

            auditCache?.Put("shape", rel, new AuditCache.Entry(Skipped: false, Checked: true,
                found.Select(f => new[] { f.Asset, f.Path, f.Field, f.Expected }).ToList()));
        }

        return audit;
    }

    private static void CollectArrayFields(JToken token, string path,
        Dictionary<string, HashSet<string>> arrayShapes, int maxDepth)
    {
        if (maxDepth < 0) return;

        switch (token)
        {
            case JObject obj:
                foreach (JProperty p in obj.Properties())
                {
                    if (p.Value is JArray)
                    {
                        if (!arrayShapes.TryGetValue(p.Name, out HashSet<string>? set))
                            arrayShapes[p.Name] = set = new HashSet<string>(StringComparer.Ordinal);
                        set.Add(path + "/" + p.Name);
                    }
                    CollectArrayFields(p.Value, path + "/*", arrayShapes, maxDepth - 1);
                }
                break;

            case JArray arr:
                foreach (JToken item in arr) CollectArrayFields(item, path + "[]", arrayShapes, maxDepth - 1);
                break;
        }
    }

    private static void FindObjectWhereArrayExpected(string asset, JToken token, string path,
        Dictionary<string, HashSet<string>> arrayShapes, List<Finding> findings, int maxDepth)
    {
        if (maxDepth < 0) return;

        switch (token)
        {
            case JObject obj:
                foreach (JProperty p in obj.Properties())
                {
                    // 只报**同一路径形状**上原版是数组、我们成了对象的那种 ——
                    // 形状也对不上的是别的毛病（如写到游戏不读的位置），由别的自检管。
                    if (p.Value is JObject
                        && arrayShapes.TryGetValue(p.Name, out HashSet<string>? set)
                        && set.Contains(path + "/" + p.Name))
                    {
                        findings.Add(new Finding(asset, path + "/" + p.Name, p.Name, "array"));
                    }
                    FindObjectWhereArrayExpected(asset, p.Value, path + "/*", arrayShapes, findings, maxDepth - 1);
                }
                break;

            case JArray arr:
                foreach (JToken item in arr)
                    FindObjectWhereArrayExpected(asset, item, path + "[]", arrayShapes, findings, maxDepth - 1);
                break;
        }
    }

    private static JToken? TryParse(string path)
    {
        try { return JToken.Parse(File.ReadAllText(path)); }
        catch (Exception) { return null; }   // 非 JSON 或损坏的跳过
    }
}
