using SdvCpTool.Core.Reporting;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Validating;

/// <summary>
/// 模型驱动的产出校验：按 <see cref="GameDataSchema"/>（从游戏源码生成）核对
/// 产出数据里每个字段的**容器形状**。
/// <para>
/// 与「拿原版数据反推形状」那套（<c>Reporting/OutputShapeAudit</c>）的区别：
/// 那个只能看原版**恰好用过**的形状，噪音大；这个有**真类型**，
/// 所以 `Fish` 该是数组、`FishAreas` 该是字典是**确定**的，不依赖原版数据里有没有。
/// </para>
/// <para>
/// <b>沿类型图下钻</b>：<see cref="GameDataSchema.AssetElement"/> 给出资产元素的类，
/// <see cref="GameDataSchema.Types"/> 给出类的字段及其**元素类名** —— 于是
/// <c>Data/Buildings</c> 的 <c>DrawLayers[].DrawPosition</c>（在**数组元素内**）也查得到。
/// 只到「资产元素的一层字段」是**不够的**：实测那一层正好漏掉了 SVE 回包失败的 6 处。
/// </para>
/// <para>
/// <b>只查容器形状</b>（数组 / 对象 / 结构化标量），不查取值 ——
/// 那需要更深的类型信息，收益也不成比例。
/// </para>
/// </summary>
public static class GameDataValidator
{
    /// <summary>一处形状不符。嵌套层的 <paramref name="Entry"/> 是**路径**（`Coop.DrawLayers[0]`）。</summary>
    public sealed record Finding(string Asset, string Entry, string Field, FieldKind Expected, string Actual);

    public sealed class Audit
    {
        /// <summary>被核对的资产数（模型里有、产出里也有）。</summary>
        public int CheckedAssets;

        public readonly List<Finding> Findings = new();
    }

    /// <summary>
    /// 下钻的最大层数（资产元素本身算第 1 层）。
    /// <para>
    /// 有限深是<b>防环</b>用的（模型里存在 <c>A</c> 含 <c>List&lt;A&gt;</c> 这类结构），
    /// 不是「只查这么深有意义」—— 生成器的类表按 6 层收，比这里宽一层。
    /// </para>
    /// </summary>
    private const int MaxDepth = 4;

    /// <summary>核对产出树里所有「模型认得」的数据资产。</summary>
    public static Audit Run(string outputRoot, AuditParseCache? parseCache = null,
        AuditCache? auditCache = null, IReadOnlyCollection<string>? writtenJson = null)
    {
        // 「这一轮写过哪些」由落盘账本说了算（见 AuditCache 的新鲜度说明）
        HashSet<string> changed = writtenJson == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(writtenJson, StringComparer.OrdinalIgnoreCase);
        var audit = new Audit();

        foreach (string path in Directory
                     .EnumerateFiles(outputRoot, "*.json", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string rel = Path.GetRelativePath(outputRoot, path).Replace('\\', '/');
            if (!rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

            string asset = rel[..^".json".Length];
            if (!GameDataSchema.AssetElement.TryGetValue(PrimaryAsset(asset), out string? elemClass)) continue;

            // 这份文件这轮没被动过 ⇒ 上次的结论照旧。
            // ⚠ 这里贵的**不是解析**（解析有 AuditParseCache）而是**逐条目的模型校验**：
            //    `Data/Objects` 一类几千条 × 几十个字段，实测每包 1.5 秒。
            if (auditCache != null && !changed.Contains(rel)
                && auditCache.TryGet("gamedata", rel, out AuditCache.Entry hit))
            {
                if (hit.Checked)
                {
                    audit.CheckedAssets++;
                    foreach (string[] row in hit.Findings)
                        audit.Findings.Add(new Finding(row[0], row[1], row[2],
                            Enum.Parse<FieldKind>(row[3]), row[4]));
                }
                continue;
            }

            JToken? root = parseCache?.Parse(path) ?? TryParse(path);
            if (root == null) continue;

            audit.CheckedAssets++;

            // 根可以是「按 ID 索引的对象」或「元素数组」—— 两种都取元素再查。
            IEnumerable<(string Name, JToken Element)> elements = root switch
            {
                JObject obj => obj.Properties().Select(p => (p.Name, p.Value)),
                JArray arr => arr.Select((t, i) => ($"[{i}]", t)),
                _ => Enumerable.Empty<(string, JToken)>(),
            };

            int first = audit.Findings.Count;
            foreach ((string name, JToken element) in elements)
                Check(asset, name, elemClass, element, 1, audit.Findings);

            auditCache?.Put("gamedata", rel, new AuditCache.Entry(
                Skipped: false, Checked: true,
                audit.Findings.Skip(first)
                    .Select(f => new[] { f.Asset, f.Entry, f.Field, f.Expected.ToString(), f.Actual })
                    .ToList()));
        }

        return audit;
    }

    /// <param name="entryPath">该元素在资产里的**路径**（顶层是键名或 `[i]`）。</param>
    /// <param name="className">该元素的模型类名。</param>
    /// <param name="depth">第几层（资产元素为 1）。</param>
    private static void Check(
        string asset, string entryPath, string className, JToken element, int depth, List<Finding> findings)
    {
        if (depth > MaxDepth) return;
        if (element is not JObject entry) return;
        if (!GameDataSchema.Types.TryGetValue(className, out Dictionary<string, FieldRef>? fields)) return;

        // 数据里的键按**不区分大小写**取：打包器与游戏都这么匹配字段名
        // （实测 mod 把 `Tooltips` 写成 `ToolTips`，打包器照认并按 `List<WorldMapTooltipData>`
        //  校验）—— 按字面查会**看不见**这个字段，于是「写错了形状」这类问题就漏过去。
        var byName = new Dictionary<string, JToken>(entry.Count, StringComparer.OrdinalIgnoreCase);
        foreach (JProperty p in entry.Properties()) byName[p.Name] = p.Value;

        foreach ((string field, FieldRef expected) in fields)
        {
            if (!byName.TryGetValue(field, out JToken? value)) continue;

            // 取空值（null）不算不符 —— 「没设」与「设错形状」是两回事。
            if (value.Type == JTokenType.Null) continue;

            bool ok = expected.Kind switch
            {
                FieldKind.Array => value is JArray,
                FieldKind.Map => value is JObject,
                // 结构化标量（Vector2/Vector3/Rectangle）**必须写成对象** ——
                // 写成字符串打包器直接报 `Error converting value "28, 116" to type 'Vector2'`，
                // 该资产打不出 XNB。原版也都写对象（实测 Data/Buildings 的 DrawLayers[].DrawPosition）。
                FieldKind.Struct => value is JObject,
                _ => true,
            };

            if (!ok)
            {
                findings.Add(new Finding(asset, entryPath, field, expected.Kind, value switch
                {
                    JArray => "array",
                    JObject => "object",
                    _ => "scalar",
                }));
                // 形状都不对，没有可下钻的元素 —— 硬钻只会报出一堆二次噪音。
                continue;
            }

            // 结构化标量的 `ElementType` 是**结构类型名**（`Vector2`…），不是可下钻的类 ——
            // 它没有 `Types` 条目，下钻只会白跑一趟。
            if (expected.Kind == FieldKind.Struct) continue;
            if (expected.ElementType.Length == 0) continue;

            switch (value)
            {
                case JArray arr:
                    for (int i = 0; i < arr.Count; i++)
                        Check(asset, $"{entryPath}.{field}[{i}]", expected.ElementType, arr[i], depth + 1, findings);
                    break;
                case JObject map:
                    foreach (JProperty p in map.Properties())
                        Check(asset, $"{entryPath}.{field}.{p.Name}", expected.ElementType, p.Value, depth + 1, findings);
                    break;
            }
        }
    }

    /// <summary>
    /// 资产路径 → 查模型用的**主资产名**：语言变体（<c>Data/Machines.zh-CN</c>）去掉后缀。
    /// <para>
    /// 变体在游戏眼里是**独立资产**，但与主资产**同形**，形状该按同一个模型查。
    /// 不去后缀的话 <c>AssetElement</c> 查不到 ⇒ 变体被**静默跳过**，自检看不见它们
    /// （实测代价：Cornucopia 的 <c>Data/Machines</c> 连 8 个变体回包全失败，
    /// 自检只报了基础资产那 8 处）。
    /// </para>
    /// </summary>
    private static string PrimaryAsset(string asset)
    {
        int dot = asset.LastIndexOf('.');
        return dot > 0 && Workspace.PackTranslations.IsRegionCode(asset[(dot + 1)..]) ? asset[..dot] : asset;
    }

    private static JToken? TryParse(string path)
    {
        try { return JToken.Parse(File.ReadAllText(path)); }
        catch (Exception) { return null; }
    }
}
