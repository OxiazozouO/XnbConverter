using SdvCpTool.Core.Reporting;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Normalizing;

namespace SdvCpTool.Core.Validating;

/// <summary>
/// 产出自检：同一<b>模型对象</b>里有没有**仅大小写不同**的两个键。
/// <para>
/// <b>为什么这是致命的</b>：SDV（以及 XnbConverter）反序列化模型时属性名<b>大小写不敏感</b>，
/// 两个只差大小写的键会**撞到同一个属性**；类型再不一致就直接抛异常 —— 该资产**打不出 XNB**。
/// 实测：SVE 的 <c>Data/WorldMap</c> 在 <c>Valley.MapAreas[7]</c> 上同时有
/// <c>Tooltips</c>（模型字段）与 <c>ToolTips</c>（mod 写错大小写），回包时 4 个资产失败。
/// </para>
/// <para>
/// <b>为什么既有的三道自检抓不到</b>：
/// <list type="bullet">
/// <item><c>OutputShapeAudit</c> 拿**原版**对照 —— 原版该处**两个键都没有**，无从对比；</item>
/// <item><c>GameDataValidator</c> 按模型核形状 —— 它<b>已经能下钻</b>到 <c>MapAreas[]</c>，
///   但取字段用的是<b>精确名</b> <c>Tooltips</c>：mod 多出来的那个键叫 <c>ToolTips</c>（大小写不同），
///   形状检查看到的 <c>Tooltips</c> 本身是合法的数组，遂<b>无从报起</b> ——
///   这是另一种失效模式，只能由本道自检覆盖；</item>
/// <item><c>SpriteSheetValidator</c> 只管贴图。</item>
/// </list>
/// <b>只有真的回包才会暴露</b> —— 这道自检把它提前到报告里。
/// </para>
/// <para>
/// <b>不误报「键即数据」的资产</b>：<c>Data/Events/*</c>（<c>Map&lt;string,string&gt;</c>）、
/// <c>Characters/Dialogue/*</c>、<c>Strings/*</c>、<c>Characters/schedules/*</c> 的**键本身就是数据**，
/// 大小写不同是**合法**的两个条目。判据复用 <see cref="VariantNormalizer.AssetTypeOf"/>，
/// 不另起一份白名单。
/// </para>
/// <para><b>只报告，不改产物</b>。</para>
/// </summary>
public static class KeyCaseConflictAudit
{
    /// <summary>
    /// 一处冲突。<paramref name="Fatal"/> = 两侧**值类型不同**（对象 vs 数组 vs 标量）。
    /// <para>
    /// <b>为什么必须分档</b>：类型不同时反序列化<b>直接抛异常</b>（该资产打不出 XNB）；
    /// 类型相同时 Newtonsoft 只是把两个键都赋给同一属性 —— <b>后者覆盖前者</b>，
    /// 不报错、**静默改数据**。
    /// 实测：SVE 产出树 72 处冲突里**只有 4 处致命**（回包失败数正好是 4）——
    /// 不分档就会把「静默覆盖」也说成「打不出包」，那是对不上的数。
    /// </para>
    /// </summary>
    public sealed record Finding(string Asset, string Path, string Key1, string Key2, bool Fatal);

    public sealed class Audit
    {
        /// <summary>被检查的资产数。</summary>
        public int CheckedAssets;

        /// <summary>仅大小写不同的键对数。</summary>
        public int Conflicts;

        /// <summary>其中**两侧值类型不同**的键对数（类型不同 ⇒ 反序列化抛异常）。</summary>
        public int FatalConflicts;

        /// <summary>
        /// <b>受致命冲突影响的资产数</b>（按资产去重）。
        /// <para>
        /// 为什么要与 <see cref="FatalConflicts"/> 分开：那个是**处数**，这个是**资产数**，
        /// 而「打不出 XNB」是按**资产**计的。实测 SVE：致命 2 处**都**在
        /// <c>Data/WorldMap</c> 的 <c>MapAreas[]</c> 里 ⇒ 受影响资产 **1 个** ⇒
        /// 回包失败列表里键名这一类正好 **1 条**。（我一度拿「2 处」去比「1 条」，
        /// 差点以为判据不准。）
        /// </para>
        /// </summary>
        public readonly HashSet<string> FatalAssets = new(StringComparer.OrdinalIgnoreCase);

        public List<Finding> Findings = new();

        public const int MaxFindings = 200;
    }

    /// <summary>键即数据的资产类型（大小写不同的键是**合法**的两个条目）。</summary>
    private static readonly HashSet<string> KeyIsData = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data/Events", "Characters/Dialogue", "Strings/schedules", "Characters/schedules",
    };

    public static Audit Run(string outputRoot, AuditParseCache? parseCache = null,
        AuditCache? auditCache = null, IReadOnlyCollection<string>? writtenJson = null)
    {
        var audit = new Audit();
        string dataDir = Path.Combine(outputRoot, "Data");
        if (!Directory.Exists(dataDir)) return audit;

        HashSet<string> changed = writtenJson == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(writtenJson, StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(dataDir, "*.json", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(outputRoot, path).Replace('\\', '/');
            if (!rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            string asset = rel[..^".json".Length];

            // 本地化变体（`X.de-DE`）与主资产同形，跳过省一半工作。
            if (Path.GetFileName(asset).Contains('.')) continue;

            string type = VariantNormalizer.AssetTypeOf(asset);
            if (KeyIsData.Contains(type)) continue;
            // `Strings/*` 整片都是键即数据的（`Strings/StringsFromCSFiles` 等）。
            if (asset.StartsWith("Strings/", StringComparison.OrdinalIgnoreCase)) continue;

            // 这一轮没写过 ⇒ 上次的结论照旧。它虽然只扫 `Data/`，但那几份一两 MB 的
            // （Objects / Shops 之类）每包都要解析+走查一遍，实测 0.3 秒上下。
            if (auditCache != null && !changed.Contains(rel)
                && auditCache.TryGet("keycase", rel, out AuditCache.Entry hit))
            {
                if (hit.Checked) audit.CheckedAssets++;
                if (audit.Findings.Count < Audit.MaxFindings)
                    foreach (string[] row in hit.Findings)
                    {
                        if (audit.Findings.Count >= Audit.MaxFindings) break;
                        audit.Findings.Add(new Finding(row[0], row[1], row[2], row[3], row[4] == "1"));
                    }
                continue;
            }

            JToken root;
            try { root = parseCache?.Parse(path) ?? JToken.Parse(File.ReadAllText(path)); }
            catch { continue; }   // 解析不了是别处的职责

            int first = audit.Findings.Count;
            audit.CheckedAssets++;
            Walk(root, "", asset, audit);

            auditCache?.Put("keycase", rel, new AuditCache.Entry(Skipped: false, Checked: true,
                audit.Findings.Skip(first)
                    .Select(f => new[] { f.Asset, f.Path, f.Key1, f.Key2, f.Fatal ? "1" : "0" })
                    .ToList()));
        }
        return audit;
    }

    /// <summary>容器种类（与反序列化的「类型是否兼容」同口径）。</summary>
    private static string Kind(JToken t) => t.Type switch
    {
        JTokenType.Object => "object",
        JTokenType.Array => "array",
        JTokenType.Null => "null",
        _ => "scalar",
    };

    private static void Walk(JToken node, string path, string asset, Audit audit)
    {
        switch (node)
        {
            case JObject o:
                var seen = new Dictionary<string, JProperty>(StringComparer.OrdinalIgnoreCase);
                foreach (JProperty p in o.Properties())
                {
                    if (seen.TryGetValue(p.Name, out JProperty? first) && first.Name != p.Name)
                    {
                        audit.Conflicts++;
                        bool fatal = Kind(first.Value) != Kind(p.Value);
                        if (fatal)
                        {
                            audit.FatalConflicts++;
                            audit.FatalAssets.Add(asset);
                        }
                        if (audit.Findings.Count < Audit.MaxFindings)
                            audit.Findings.Add(new Finding(asset, path, first.Name, p.Name, fatal));
                    }
                    else
                    {
                        seen[p.Name] = p;
                    }
                }
                foreach (JProperty p in o.Properties())
                    Walk(p.Value, path.Length == 0 ? p.Name : $"{path}.{p.Name}", asset, audit);
                break;

            case JArray a:
                for (int i = 0; i < a.Count; i++)
                    Walk(a[i], $"{path}[{i}]", asset, audit);
                break;
        }
    }
}
