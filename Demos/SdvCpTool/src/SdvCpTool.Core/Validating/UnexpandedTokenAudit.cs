using SdvCpTool.Core.Reporting;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Validating;

/// <summary>
/// 产出自检：**写出去的字符串里还留着没展开的 CP 令牌**（<c>{{…}}</c>）。
/// <para>
/// <b>为什么这是硬问题</b>：CP 会在写入前把令牌代成值；我们没代的部分会**原样落进产物**，
/// 而游戏**不认识** <c>{{…}}</c> —— 后果是静默的，且分几档严重程度：
/// </para>
/// <list type="bullet">
/// <item><b>严重</b>：<c>Condition</c> 里带令牌 ⇒ 那不是合法 GSQ；
///   <c>GameStateQuery</c> 报错并返回假 ⇒ 该条目**永远不生效**；</item>
/// <item><b>严重</b>：<c>Sprite</c>/<c>Portrait</c>/<c>Texture</c> 里带令牌 ⇒
///   找一张名字里带花括号的图 ⇒ 取不到 ⇒ 画不出来（游戏只记一条日志）；</item>
/// <item><b>提示</b>：<c>Id</c> 之类的**标识**字段里带令牌 ⇒ 游戏拿它当不透明字符串用，
///   不影响行为，只是与 CP 的产物不一致（仍报出来，但单独一档，免得淹没真问题）。</item>
/// </list>
/// <para>
/// <b>怎么发现的</b>：给 <see cref="SpriteSheetValidator"/> 补上
/// <c>Data/Characters</c> 的 <c>Appearance[]</c> 引用检查后，它报出
/// <c>Sprite: "Characters/Claire_{{Outfit}}"</c> —— 顺着查才发现这一整类。
/// </para>
/// <para>
/// ⚠ <c>{{i18n:…}}</c> **不算**：那是通道 B1 自己处理的键，展开在
/// <c>TranslationApplier</c> 里做，且只出现在 <c>Strings</c>/对话类资产上。
/// </para>
/// <para><b>只报告，不改产物</b>。</para>
/// </summary>
public static class UnexpandedTokenAudit
{
    private static readonly Regex Token = new(@"\{\{\s*([A-Za-z_][A-Za-z0-9_.\-]*)", RegexOptions.Compiled);

    /// <summary>这些字段里带令牌**不影响行为**（当作不透明标识用），单独一档。</summary>
    private static readonly HashSet<string> IdentifierFields =
        new(StringComparer.OrdinalIgnoreCase) { "Id", "LogName", "Name" };

    /// <summary>一处未展开的令牌。</summary>
    /// <param name="Asset">相对资产名（如 <c>Data/Characters</c>）。</param>
    /// <param name="Path">出现位置（如 <c>Claire.Appearance[11].Sprite</c>）。</param>
    /// <param name="Field">承载它的字段名（用来分档）。</param>
    /// <param name="TokenName">令牌名（不含花括号）。</param>
    /// <param name="IdentifierOnly">真 = 只是标识字段，不影响行为。</param>
    public sealed record Finding(
        string Asset, string Path, string Field, string TokenName, bool IdentifierOnly);

    public sealed class Audit
    {
        public int CheckedAssets;

        /// <summary>会影响行为的（<c>Condition</c>/<c>Sprite</c>/值…）。</summary>
        public int Harmful;

        /// <summary>只是标识字段的。</summary>
        public int IdentifierOnly;

        public readonly List<Finding> Findings = new();

        public const int MaxFindings = 200;
    }

    public static Audit Run(string outputRoot, AuditParseCache? parseCache = null,
        AuditCache? auditCache = null, IReadOnlyCollection<string>? writtenJson = null)
    {
        var audit = new Audit();
        if (!Directory.Exists(outputRoot)) return audit;

        HashSet<string> changed = writtenJson == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(writtenJson, StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory
                     .EnumerateFiles(outputRoot, "*.json", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string rel = Path.GetRelativePath(outputRoot, path).Replace('\\', '/');
            string asset = rel[..^".json".Length];
            if (Path.GetFileName(asset).Contains('.')) continue;   // 本地化变体与主资产同形，跳过省一半

            // 这一轮没写过 ⇒ 上次的结论照旧。这里贵的是**逐字符串扫全树**
            // （正则找 `{{…}}`），实测每包 0.8 秒上下。
            if (auditCache != null && !changed.Contains(rel)
                && auditCache.TryGet("token", rel, out AuditCache.Entry hit))
            {
                if (hit.Checked) audit.CheckedAssets++;
                if (hit.Counts is { Length: 2 })
                {
                    audit.IdentifierOnly += hit.Counts[0];
                    audit.Harmful += hit.Counts[1];
                }
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

            int identBefore = audit.IdentifierOnly;
            int harmfulBefore = audit.Harmful;
            int findingsBefore = audit.Findings.Count;

            // 内容包自己的资源（图片目录等）里不会有数据资产，但 `*.json` 仍可能是别的用途；
            // 只在**真的扫到令牌**时才计入 CheckedAssets，避免把「一个都没有」的树算成已核对。
            bool any = false;
            Walk(root, "", asset, audit, ref any);
            if (any) audit.CheckedAssets++;

            auditCache?.Put("token", rel, new AuditCache.Entry(
                Skipped: false, Checked: any,
                audit.Findings.Skip(findingsBefore)
                    .Select(f => new[] { f.Asset, f.Path, f.Field, f.TokenName, f.IdentifierOnly ? "1" : "0" })
                    .ToList(),
                Counts: new[]
                {
                    audit.IdentifierOnly - identBefore,
                    audit.Harmful - harmfulBefore,
                }));
        }
        return audit;
    }

    private static void Walk(JToken node, string path, string asset, Audit audit, ref bool any)
    {
        switch (node)
        {
            case JObject o:
                foreach (JProperty p in o.Properties())
                {
                    string child = path.Length == 0 ? p.Name : $"{path}.{p.Name}";
                    if (p.Value.Type == JTokenType.String)
                    {
                        foreach (Match m in Token.Matches(p.Value.Value<string>() ?? ""))
                        {
                            if (m.Groups[1].Value.Equals("i18n", StringComparison.OrdinalIgnoreCase))
                                continue;   // 通道 B1 自己处理，且只出现在别处
                            any = true;
                            bool ident = IdentifierFields.Contains(p.Name);
                            if (ident) audit.IdentifierOnly++; else audit.Harmful++;
                            if (audit.Findings.Count < Audit.MaxFindings)
                                audit.Findings.Add(new Finding(asset, child, p.Name, m.Groups[1].Value, ident));
                        }
                    }
                    else
                    {
                        Walk(p.Value, child, asset, audit, ref any);
                    }
                }
                break;

            case JArray a:
                for (int i = 0; i < a.Count; i++)
                    Walk(a[i], $"{path}[{i}]", asset, audit, ref any);
                break;
        }
    }
}
