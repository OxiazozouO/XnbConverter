using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Validating;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Reporting;

/// <summary>
/// 产出树自检：我们写出去的 <c>Condition</c>，**位置**是不是游戏真的会读的那种。
/// <para>
/// <b>为什么需要它</b>：落在游戏不读的位置的 <c>Condition</c> 会被**静默忽略** ——
/// 补丁记成「写入成功」，物件却变成无条件存在。这类错只有实机跑到才会暴露，很容易测不到。
/// 实测踩过一次：SVE 产物 <c>Data/Objects.json</c> 里 25 个物件条目被加上条目级 <c>Condition</c>，
/// 而原版 <c>Data/Objects</c> **只在 <c>GeodeDrops[]</c> 下有** <c>Condition</c>（全库 11 处）。
/// </para>
/// <para>
/// <b>判据</b>：拿**原版树里同一份资产**做对照 —— 把它所有条件字段的**路径形状**收集起来，
/// 我们写出的每个条件字段都必须命中其中一个形状。
/// 形状把条目键抽象成 <c>*</c>、数组下标抽象成 <c>[]</c>，只比「字段名序列」。
/// </para>
/// <para>
/// <b>这是自检，不是写入闸门</b>：它只报告，不改产物。判定的闸门在
/// <c>Merging/ConditionWriter</c>（那里拒写）。
/// </para>
/// </summary>
public static class ConditionPlacementAudit
{
    /// <summary>一处可疑的条件落点。</summary>
    public sealed record Finding(string Asset, string Path, string Field, string Snippet);

    /// <summary>自检结果。</summary>
    public sealed class Audit
    {
        public int CheckedAssets;
        public int SkippedNoVanilla;

        /// <summary>
        /// **输入树与产出树是同一棵** ⇒ 这项自检**无从对照**（拿产出跟它自己比），已整体跳过。
        /// <para>
        /// 什么时候会这样：`convert-mods.py` 的链式跑法是
        /// `--tree &lt;同一棵&gt; --out &lt;同一棵&gt; --append`（为了不每包重拷整棵树），
        /// 于是「原版那份」读到的就是**刚写过的产出** —— 形状当然一模一样，永远 0 findings。
        /// 实测：同一份 SVE，逐包对原版树跑（`run-packs.py`）报 `misplaced 1`
        /// （`Data/WorldMap.json` 的 `MapAreas[]/KnownCondition`，
        /// 见 `docs/verification/2026-10-09-self-audit-findings-reviewed.md` §3），
        /// 链式跑法里每个包都是 0 ⇒ **0 是"没比"，不是"没问题"**。
        /// </para>
        /// </summary>
        public bool SkippedSameTree;
        public readonly List<Finding> Findings = new();
    }

    /// <summary>
    /// 跑一遍自检。<paramref name="conditionFields"/> 是已确证的条件字段名
    /// （调用方传 <c>ConditionLanding.ConditionFieldNames</c>，不要再抄一份）。
    /// </summary>
    public static Audit Run(OutputTree output, IReadOnlyCollection<string> conditionFields,
        AuditParseCache? parseCache = null, AuditCache? auditCache = null,
        IReadOnlyCollection<string>? writtenJson = null)
    {
        // ★ 输入树与产出树是**同一棵**（链式跑法的 `--append`）⇒ 拿产出跟它自己比，**必是 0**。
        //   必须报出来：0 会被读成"没问题"，而它其实是"没比"（见 Audit.SkippedSameTree 的说明）。
        if (SameTree(output.InputRoot, output.OutputRoot))
            return new Audit { SkippedSameTree = true };

        JToken? TryParseCached(string p) => parseCache?.Parse(p) ?? TryParse(p);

        // 「这一轮写过哪些」由落盘账本说了算：写过的必须重算，没写过的必然没变 ——
        // 比拿「大小 + 修改时间」猜更准，也省掉逐文件 stat。
        HashSet<string> changed = writtenJson == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(writtenJson, StringComparer.OrdinalIgnoreCase);

        var audit = new Audit();
        var vanillaShapes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // ⚠ 老老实实枚举整棵树。曾试过「有缓存就不枚举、改遍历『账本 ∪ 缓存』」，
        //   实测**没有可测量的收益**（1.80s → 1.78s），却把 `checkedAssets` 数错了
        //   （2762 → 2876）—— 为省那点毫秒去冒数错的风险，不值。
        foreach (string rel in Directory
                     .EnumerateFiles(output.OutputRoot, "*.json", SearchOption.AllDirectories)
                     .Select(p => Path.GetRelativePath(output.OutputRoot, p).Replace('\\', '/'))
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string producedPath = Path.Combine(output.OutputRoot, rel);

            // 这份文件这轮没被动过 ⇒ 上次的结论照旧（见 AuditCache）
            if (auditCache != null && !changed.Contains(rel)
                && auditCache.TryGet("condition", rel, out AuditCache.Entry hit))
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

            string vanillaPath = Path.Combine(output.InputRoot, rel);
            if (!File.Exists(vanillaPath))
            {
                audit.SkippedNoVanilla++;   // 原版没有这份资产 → 无从对照，不妄下结论
                auditCache?.Put("condition", rel, new AuditCache.Entry(Skipped: true, Checked: false,
                    new List<string[]>()));
                continue;
            }

            JToken? produced = TryParseCached(producedPath);
            JToken? vanilla = TryParseCached(vanillaPath);
            if (produced == null || vanilla == null)
            {
                auditCache?.Put("condition", rel, new AuditCache.Entry(Skipped: false, Checked: false,
                    new List<string[]>()));
                continue;
            }

            audit.CheckedAssets++;

            if (!vanillaShapes.TryGetValue(rel, out HashSet<string>? shapes))
            {
                shapes = new HashSet<string>(StringComparer.Ordinal);
                CollectShapes(vanilla, "", shapes, conditionFields, maxDepth: 6);
                vanillaShapes[rel] = shapes;
            }

            var found = new List<Finding>();
            // 下钻的起点：这份资产的元素类（模型里没有就传 null ⇒ 只靠原版对照那条）
            string asset = rel[..^".json".Length];
            GameDataSchema.AssetElement.TryGetValue(asset, out string? elementClass);
            CollectFindings(rel, produced, "", elementClass, shapes, conditionFields, found, maxDepth: 6);
            audit.Findings.AddRange(found);

            auditCache?.Put("condition", rel, new AuditCache.Entry(Skipped: false, Checked: true,
                found.Select(f => new[] { f.Asset, f.Path, f.Field, f.Snippet }).ToList()));
        }

        return audit;
    }

    /// <summary>收集某份数据里所有条件字段的路径形状。</summary>
    private static void CollectShapes(JToken token, string path, HashSet<string> shapes,
        IReadOnlyCollection<string> fields, int maxDepth)
    {
        if (maxDepth < 0) return;

        switch (token)
        {
            case JObject obj:
                foreach (JProperty p in obj.Properties())
                {
                    if (fields.Contains(p.Name)) { shapes.Add(path + "/" + p.Name); continue; }
                    CollectShapes(p.Value, path + "/*", shapes, fields, maxDepth - 1);
                }
                break;

            case JArray arr:
                foreach (JToken item in arr) CollectShapes(item, path + "[]", shapes, fields, maxDepth - 1);
                break;
        }
    }

    /// <summary>
    /// 在产出数据里找条件字段。判据**两条并用，两条都成立才记**：
    /// <list type="number">
    /// <item><b>模型里没有这个字段</b>（沿类型图下钻得到当前位置的类，查
    ///   <see cref="GameDataSchema.KnownFields"/>）—— 那说明游戏**整段忽略**它
    ///   （实测 `WorldMap` 的 `MapAreas[]/KnownCondition`）；</item>
    /// <item><b>原版同一形状上也没有**这个条件字段**（<paramref name="shapes"/>）—— 用来兜住
    ///   模型认不出当前位置的情形（类名下钻不到 ⇒ 没有模型依据 ⇒ 只信原版对照）。</item>
    /// </list>
    /// <para>
    /// ⚠ 为什么加第一条（2026-10-13）：原来的判据**只有第二条** ⇒ 凡是"合法但原版自己没用过"的
    /// 条件字段全被报成"写在游戏不读的位置"—— 实测 `Data/FruitTrees` 的 `Fruit[]/Condition`
    /// （`FruitTreeFruitData` 从 `GenericSpawnItemDataWithCondition` **继承**来的字段，
    /// 模型注释还专门指向它）每个包报 **19 处**假阳性，把自检淹了。
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠ 编译器对下面那个递归调用报 <c>CS8604</c>（`p.Value` 可能为 null）—— **可以忽略**：
    /// `switch` 只有 `JObject` / `JArray` 两个 case，null 不匹配任何一个 ⇒ 直接跳出，不会解引用。
    /// （2026-10-10 查过，所以留着不改成 `p.Value!`——那反而掩盖了真实意图。）
    /// </remarks>
    private static void CollectFindings(string asset, JToken token, string path, string? className,
        HashSet<string> shapes, IReadOnlyCollection<string> fields, List<Finding> findings, int maxDepth)
    {
        if (maxDepth < 0) return;

        switch (token)
        {
            case JObject obj:
                // 根是**字典型资产**（`Data/FruitTrees` 这种：键是条目标识、值才是元素）⇒ 键不是字段，
                // 值的类**不变**。漏了这一步就会在第一个键上把类名丢掉（实测 `Data/FruitTrees` 的
                // `Fruit[]/Condition` 因此仍被误报）。
                WalkMap(asset, obj, path, className, shapes, fields, findings, maxDepth - 1);
                break;

            case JArray arr:
                foreach (JToken item in arr)
                    WalkEntry(asset, item, path + "[]", className, shapes, fields, findings, maxDepth - 1);
                break;
        }
    }

    /// <summary>字典那一层：成员是**键**，值的类不变（`className` 已是对应元素的类）。</summary>
    private static void WalkMap(string asset, JObject map, string path, string? className,
        HashSet<string> shapes, IReadOnlyCollection<string> fields, List<Finding> findings, int maxDepth)
    {
        if (maxDepth < 0) return;
        foreach (JProperty p in map.Properties())
            WalkEntry(asset, p.Value, path + "/*", className, shapes, fields, findings, maxDepth - 1);
    }

    /// <summary>元素实例那一层：成员是**字段** ⇒ 按模型查「有没有这个字段」并沿类型图下钻。</summary>
    private static void WalkEntry(string asset, JToken token, string path, string? className,
        HashSet<string> shapes, IReadOnlyCollection<string> fields, List<Finding> findings, int maxDepth)
    {
        if (maxDepth < 0) return;

        switch (token)
        {
            case JObject obj:
                foreach (JProperty p in obj.Properties())
                {
                    if (fields.Contains(p.Name))
                    {
                        bool declared = false;
                        if (className != null
                            && GameDataSchema.KnownFields.TryGetValue(className, out HashSet<string>? known))
                        {
                            declared = known.Contains(p.Name);
                        }
                        if (!declared && !shapes.Contains(path + "/" + p.Name))
                        {
                            string snippet = p.Value?.ToString() ?? "";
                            if (snippet.Length > 70) snippet = snippet[..70] + "…";
                            findings.Add(new Finding(asset, path + "/" + p.Name, p.Name, snippet));
                        }
                        continue;
                    }

                    // 下钻：按模型给的这个字段的类型
                    FieldRef? reference = null;
                    if (className != null
                        && GameDataSchema.Types.TryGetValue(className, out Dictionary<string, FieldRef>? typeFields))
                    {
                        typeFields.TryGetValue(p.Name, out FieldRef got);
                        reference = got;
                    }
                    string? child = reference is { ElementType.Length: > 0 } r ? r.ElementType : null;

                    if (p.Value is JObject nested && reference is { Kind: FieldKind.Map })
                        WalkMap(asset, nested, path + "/*", child, shapes, fields, findings, maxDepth - 1);
                    else
                        WalkEntry(asset, p.Value, path + "/*", child, shapes, fields, findings, maxDepth - 1);
                }
                break;

            case JArray arr:
                foreach (JToken item in arr)
                    WalkEntry(asset, item, path + "[]", className, shapes, fields, findings, maxDepth - 1);
                break;
        }
    }


    private static JToken? TryParse(string path)
    {
        try { return JToken.Parse(File.ReadAllText(path)); }
        catch (Exception) { return null; }   // 非 JSON（.config 等）或损坏的跳过
    }

    /// <summary>两个根是不是同一个目录（按 realpath 比，大小写不敏感）。</summary>
    internal static bool SameTree(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
