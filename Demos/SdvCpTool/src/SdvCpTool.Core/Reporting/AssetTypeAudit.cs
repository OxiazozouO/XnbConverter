using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Reporting;

/// <summary>
/// **资产类型自检**：产出树里每份 <c>&lt;资产&gt;.config</c> 声明的 <c>Content.Extension</c>，
/// 与原版同资产的声明**对得上**吗？
///
/// <para>
/// <b>为什么要它</b>：XNB 的类型是 <c>.config</c> 里那个 <c>Readers</c> 说了算 —— 原版是**地图**
/// （<c>.tbin</c> / <c>xTile.Pipeline.TideReader</c>）的资产，如果我们写成**贴图**
/// （<c>.png</c> / <c>Texture2DReader</c>），打包之后游戏就会拿一个 <c>Texture2D</c> 去当 <c>Map</c> 用
/// ⇒ **进那个地点就炸**。这东西不报出来就是纯静默。
/// </para>
///
/// <para>
/// <b>实测（2026-10-12，新语料）</b>：<c>Maps/Coop</c> 就是这种 —— 树里
/// <c>Coop.png</c>（6827 B）与 <c>Coop.tbin</c>（11198 B）**同时存在**，而 <c>.config</c> 说 <c>.png</c>
/// （写贴图那条补丁后落盘 ⇒ 把地图那条盖掉了）。CP 下同样会坏（把贴图给 xTile 加载器）⇒
/// **这是包的 bug**，但必须让读报告的人看见。
/// </para>
///
/// <para>
/// ⚠ 这是**自检不是闸门**：它只报告。类型变化有时是包**故意**的（比如整块资产换类型），
/// 那也需要人看一眼再决定。
/// </para>
/// </summary>
public static class AssetTypeAudit
{
    /// <summary>一处「产出与原版的资产类型不一致」。</summary>
    public sealed record Finding(string Asset, string ProducedExtension, string VanillaExtension);

    public sealed class Audit
    {
        /// <summary>核过的、原版有参照的带类型描述符资产数。</summary>
        public int CheckedAssets;

        public readonly List<Finding> Findings = new();
    }

    /// <summary>
    /// 跑一遍（产出树的 <c>.config</c> 逐个与原版树同名文件比 <c>Content.Extension</c>）。
    /// </summary>
    /// <param name="auditCache">
    /// 给了它 ⇒ **没被动过的文件直接沿用上次的结论**（不解析 JSON，连 <c>File.Exists</c> 都省）。
    /// <para>
    /// <b>为什么要缓存</b>：全树一万多份 <c>.config</c>，每份要 **JSON 解析两遍**（产出那份 + 原版那份）。
    /// 实测（2026-10-13，一个只写 7 条补丁的小包）：这一项自检**每包 1.9 s**，
    /// 与那包写了多少**无关** —— 232 包就是 **7 分多钟**，占整轮语料（24.7 分钟）的近三成。
    /// 结论只依赖「产出那份 + 原版那份」，两者这一轮都没动过 ⇒ 必然没变（与其它自检同一口径）。
    /// </para>
    /// </param>
    /// <param name="writtenConfig">
    /// 这一轮**写过的**文件（落盘账本里筛 <c>.config</c>）。<c>null</c> = 每个文件都重算
    /// （逐包对原版树跑时用，那里每棵都是新树）。
    /// </param>
    public static Audit Run(OutputTree output, AuditCache? auditCache = null,
        IReadOnlyCollection<string>? writtenConfig = null)
    {
        var audit = new Audit();
        string root = output.OutputRoot;
        string vanillaRoot = output.InputRoot;
        if (!Directory.Exists(root) || !Directory.Exists(vanillaRoot)) return audit;

        HashSet<string> changed = new(writtenConfig ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(root, "*.config", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');

            if (auditCache != null && !changed.Contains(rel)
                && auditCache.TryGet("assettype", rel, out AuditCache.Entry hit))
            {
                if (hit.Checked) audit.CheckedAssets++;
                foreach (string[] row in hit.Findings)
                    audit.Findings.Add(new Finding(row[0], row[1], row[2]));
                continue;
            }

            string? produced = Extension(path);
            if (produced == null)                                     // 数据资产的描述符 ⇒ 另说，不猜
            {
                auditCache?.Put("assettype", rel, AuditCache.Entry.SkippedOnly);
                continue;
            }

            string vanillaPath = Path.Combine(vanillaRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(vanillaPath))                            // 原版没有这份资产 ⇒ 无参照，不猜
            {
                auditCache?.Put("assettype", rel, AuditCache.Entry.SkippedOnly);
                continue;
            }
            string? vanilla = Extension(vanillaPath);
            if (vanilla == null)
            {
                auditCache?.Put("assettype", rel, AuditCache.Entry.SkippedOnly);
                continue;
            }

            audit.CheckedAssets++;
            bool mismatch = !string.Equals(produced, vanilla, StringComparison.OrdinalIgnoreCase);
            if (mismatch)
                audit.Findings.Add(new Finding(rel[..^".config".Length], produced, vanilla));

            auditCache?.Put("assettype", rel, new AuditCache.Entry(Skipped: false, Checked: true,
                mismatch
                    ? new List<string[]> { new[] { rel[..^".config".Length], produced, vanilla } }
                    : new List<string[]>()));
        }

        audit.Findings.Sort((a, b) => string.CompareOrdinal(a.Asset, b.Asset));
        return audit;
    }

    private static string? Extension(string configPath)
    {
        try
        {
            if (JObject.Parse(File.ReadAllText(configPath)) is not JObject root) return null;
            string? ext = root["Content"]?["Extension"]?.Value<string>();
            return string.IsNullOrWhiteSpace(ext) ? null : ext;
        }
        catch (Newtonsoft.Json.JsonException) { return null; }    // 读不了就不认（不猜）
    }
}
