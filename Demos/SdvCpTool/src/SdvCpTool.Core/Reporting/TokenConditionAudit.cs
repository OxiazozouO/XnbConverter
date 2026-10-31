using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Reporting;

/// <summary>
/// **条件键是包内令牌**的补丁有多少 —— 这一档我们的 `When` 求值器**不认**（CP 认）。
///
/// <para>
/// <b>为什么要单独报</b>：这类补丁不会报错，只会**安静地不生效** ——
/// `When` 判不了 ⇒ 调用方按"判不了 → 视为不匹配 → 落到默认块"处理，于是：
/// 该条补丁不写，或者走到一个**别人以为不会走到**的分支。报告里原本只能看到
/// "资产 X 是图片 —— 按条件换图有机制…" 这种**下游**说法，看不出是这里卡住的。
/// </para>
///
/// <para>
/// <b>实测规模</b>（2026-10-12 新语料）：**99 个令牌块 / 12 个包**这么写 ——
/// `[CP] DaisyNiko's Tilesheets` 的 <c>{"recolour": "Vanilla"}</c>（`{{pan}}` 靠它才代得掉，
/// `Maps/panorama` 因此进不了产出树 ⇒ 引用它的 5 张地图建不出来）、
/// `Lurking in the Dark` 的 <c>{"IsSVEInstalled": false}</c>、SVE / Integrated Minecarts 等。
/// </para>
///
/// <para>
/// ⚠ **这是自检不是闸门**：它只报告。试过三版"认这一档"的实现都因为
/// 不可解释的行为变化回退了（诊断见 `docs/research/2026-10-12-remaining-gaps-and-decisions.md`）。
/// </para>
/// </summary>
public static class TokenConditionAudit
{
    /// <summary>一处「条件键是包内令牌」。</summary>
    public sealed record Finding(string Patch, string Target, string Key);

    public sealed class Audit
    {
        /// <summary>看过多少条补丁（有 `When` 的）。</summary>
        public int CheckedPatches;

        /// <summary>其中多少条的条件键里有包内令牌。</summary>
        public int ConditionalOnTokens;

        /// <summary>看过多少块 **DynamicToken 定义**（有 `When` 的块）。</summary>
        public int CheckedTokenBlocks;

        /// <summary>其中多少块的条件键里有**另一个**包内令牌。</summary>
        public int TokenBlocksConditionalOnTokens;

        public readonly List<Finding> Findings = new();
    }

    /// <summary>
    /// 跑一遍。**两处都要看**：补丁自己的 `When`，以及 **DynamicToken 定义的块的 `When`** ——
    /// 后者才是 `{{pan}}` 那种卡的（`{"recolour": "Vanilla"}` 写在令牌定义里，不在补丁上，
    /// 所以只看补丁会误报成 0）。
    /// </summary>
    public static Audit Run(ContentPack pack)
    {
        var audit = new Audit();

        DynamicTokenResolver resolver = DynamicTokenResolver.Load(pack.RootPath, null);
        IReadOnlyCollection<string> names = resolver.Names;
        if (names.Count == 0) return audit;
        var tokens = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        foreach (ChangePatch patch in pack.Patches)
        {
            if (patch.When is not JObject when || !when.HasValues) continue;
            audit.CheckedPatches++;

            string? key = FirstTokenKey(when, tokens);
            if (key == null) continue;
            audit.ConditionalOnTokens++;
            audit.Findings.Add(new Finding(
                $"{Relative(pack, patch.SourceFile)}#{patch.Index}",
                patch.Target ?? "",
                key));
        }

        foreach (string name in names)
        {
            foreach ((string _, JObject? blockWhen) in resolver.BlocksOf(name) ?? Array.Empty<(string, JObject?)>())
            {
                if (blockWhen is not { HasValues: true }) continue;
                audit.CheckedTokenBlocks++;

                string? key = FirstTokenKey(blockWhen, tokens);
                if (key == null) continue;
                audit.TokenBlocksConditionalOnTokens++;
                if (audit.Findings.Count < 200)
                    audit.Findings.Add(new Finding($"DynamicTokens[{name}]", "", key));
            }
        }

        return audit;
    }

    /// <summary>这条 `When` 里第一个"键是包内令牌"的键名（没有则 <c>null</c>）。</summary>
    private static string? FirstTokenKey(JObject when, HashSet<string> tokens)
    {
        foreach (JProperty property in when.Properties())
            if (tokens.Contains(DynamicTokenResolver.ConditionKeyName(property.Name)))
                return property.Name;
        return null;
    }

    /// <summary>包内相对路径（`SourceFile` 有时是绝对路径 —— 报出来要能一眼看出是哪个文件）。</summary>
    private static string Relative(ContentPack pack, string? sourceFile)
    {
        if (string.IsNullOrWhiteSpace(sourceFile)) return "(未注来源)";
        try
        {
            string rel = Path.GetRelativePath(pack.RootPath, sourceFile);
            return rel.StartsWith("..", StringComparison.Ordinal) ? sourceFile : rel.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return sourceFile;
        }
    }
}
