using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Normalizing;

/// <summary>
/// 一个 <c>Appearance[]</c> 变体：一条条件对应的一整套外观（立绘 + 行走图）。
/// </summary>
public sealed record AppearanceVariant(
    string Gsq,
    int Index,
    string? SpriteFrom,
    string? PortraitFrom,
    bool? IsOutdoors);

/// <summary>
/// 一个 NPC 的立绘/行走图集合：把「同一 <c>When</c> 下换哪张图」归一到原版
/// <c>Data/Characters/&lt;id&gt;/Appearance[]</c>。
/// </summary>
public sealed class AppearanceGroup
{
    public string Npc = "";

    /// <summary>本包改过的行走图 / 立绘资产名；没改的那张留空。</summary>
    public string? SpriteAsset, PortraitAsset;

    /// <summary>按**补丁出现顺序**排好的变体（后者压前者，见 <c>Precedence</c> 的取法）。</summary>
    public List<AppearanceVariant> Variants = new();

    /// <summary>非空 = 不成组（保持原判）。</summary>
    public string? Reject;
}

/// <summary>
/// 条件 <c>EditImage</c> 打在 <c>Characters/&lt;NPC&gt;</c> / <c>Portraits/&lt;NPC&gt;</c> 上时，
/// 归一到 <c>Data/Characters</c> 的 <c>Appearance[]</c>。
/// <para>
/// <b>机制</b>（<c>NPC.ChooseAppearance</c>，<c>NPC.cs:957-1066</c>）：进地点时逐条筛
/// <c>Appearance</c> —— <c>Season</c>（可空 = 任意）· <c>Outdoors</c>/<c>Indoors</c>（按当前地点二选一）
/// · <c>Condition</c>（<b>任意 GSQ</b>）· <c>IsIslandAttire</c> 必须与当前一致；
/// 命中的里取 <b>Precedence 最小</b>的那批，再按 <c>Weight</c> 加权随机。
/// </para>
/// <para>
/// ⚠ <b>`Indoors`/`Outdoors` 的 C# 初值都是 `true`</b>（不是 false）——
/// 不写就是室内外都适用。要表达 CP 的「仅室外」必须显式写 <c>Outdoors: true, Indoors: false</c>。
/// </para>
/// <para>
/// ⚠ <b>Precedence 必须给成各不相同的</b>：默认全是 0 ⇒ 全部并列 ⇒ 走加权随机。
/// CP 的语义是「后写的赢」，所以<b>越靠后的补丁 Precedence 越小</b>。
/// </para>
/// </summary>
public static class AppearancePlanner
{
    /// <summary>该资产名是不是 <c>Characters/&lt;NPC&gt;</c> / <c>Portraits/&lt;NPC&gt;</c>，是则给出 NPC id。</summary>
    public static string? NpcOf(string? assetName, out bool isSprite)
    {
        isSprite = false;
        string t = (assetName ?? "").Replace('\\', '/').Trim('/');
        int slash = t.IndexOf('/');
        if (slash < 0 || t[(slash + 1)..].Contains('/')) return null;

        string head = t[..slash];
        if (head.Equals("Characters", StringComparison.OrdinalIgnoreCase)) isSprite = true;
        else if (!head.Equals("Portraits", StringComparison.OrdinalIgnoreCase)) return null;

        return t[(slash + 1)..];
    }

    public static IReadOnlyList<AppearanceGroup> Plan(IEnumerable<PatchVerdict> verdicts,
        IReadOnlySet<string> knownNpcs, Func<PatchVerdict, bool>? willWrite = null)
    {
        willWrite ??= WritePolicy.ShouldWrite;

        // NPC → 规范化 When → 该 When 下的补丁
        var byNpc = new Dictionary<string, Dictionary<string, List<PatchVerdict>>>(StringComparer.OrdinalIgnoreCase);
        foreach (PatchVerdict v in verdicts)
        {
            if (!v.Patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase)) continue;
            if (v.Patch.When == null || v.Patch.When.Count == 0) continue;
            if (willWrite(v)) continue;
            if (v.Patch.Raw["PatchMode"] != null) continue;   // 非默认模式另说，不成组

            string? npc = NpcOf(v.Patch.Target, out _);
            if (npc == null) continue;
            if (!byNpc.TryGetValue(npc, out var byWhen))
                byNpc[npc] = byWhen = new Dictionary<string, List<PatchVerdict>>(StringComparer.Ordinal);
            string key = v.Patch.When.ToString(Newtonsoft.Json.Formatting.None);
            if (!byWhen.TryGetValue(key, out List<PatchVerdict>? bucket))
                byWhen[key] = bucket = new List<PatchVerdict>();
            bucket.Add(v);
        }

        var groups = new List<AppearanceGroup>();
        foreach ((string npc, Dictionary<string, List<PatchVerdict>> byWhen) in byNpc)
            groups.Add(PlanOne(npc, byWhen, knownNpcs));
        groups.Sort((a, b) => string.CompareOrdinal(a.Npc, b.Npc));
        return groups;
    }

    private static AppearanceGroup PlanOne(string npc, Dictionary<string, List<PatchVerdict>> byWhen,
        IReadOnlySet<string> knownNpcs)
    {
        var group = new AppearanceGroup { Npc = npc };
        if (!knownNpcs.Contains(npc))
            return Reject(group,
                $"`{npc}` 不在 `Data/Characters` 里 —— Appearance[] 是**该资产的字段**，没有它无处可写");

        int index = 0;
        foreach ((string _, List<PatchVerdict> bucket) in byWhen)
        {
            PatchVerdict first = bucket[0];

            // 同一条 When 下，同一张图出现两次说不清用哪个 —— 拒绝而不是猜。
            foreach (bool isSprite in new[] { true, false })
            {
                List<PatchVerdict> same = bucket.Where(v => NpcOf(v.Patch.Target, out bool s) != null && s == isSprite).ToList();
                if (same.Count > 1)
                    return Reject(group,
                        $"同一个 `When` 下有 {same.Count} 条补丁改同一张{(isSprite ? "行走图" : "立绘")}，"
                        + "Appearance 一个条目只能指一张图，说不清用哪个");
            }

            PatchVerdict? sprite = bucket.FirstOrDefault(v => NpcOf(v.Patch.Target, out bool s) != null && s);
            PatchVerdict? portrait = bucket.FirstOrDefault(v => NpcOf(v.Patch.Target, out bool s) != null && !s);

            // ★ `IsOutdoors` 在 Appearance 里**不是条件，是字段**（`Outdoors`/`Indoors`）——
            //   必须先摘出来再翻译剩下的条件。不摘的话 GsqTranslator 会以「只能近似映射」拒绝整组
            //   （它确实不该把「室外」翻成 `LOCATION_IS_OUTDOORS`：那是**地点**是否室外，
            //    与 CP 的 IsOutdoors 不是一回事）。
            bool? isOutdoors = BoolOf(first.Patch.When!, "IsOutdoors", out string? boolError);
            if (boolError != null) return Reject(group, boolError);

            string? gsq = Translate(first, isOutdoors != null, out string? gsqError);
            if (gsq == null) return Reject(group, gsqError!);

            group.SpriteAsset ??= sprite?.Patch.Target?.Replace('\\', '/').Trim('/');
            group.PortraitAsset ??= portrait?.Patch.Target?.Replace('\\', '/').Trim('/');

            group.Variants.Add(new AppearanceVariant(
                gsq, index++,
                sprite?.Patch.FromFile, portrait?.Patch.FromFile, isOutdoors));
        }

        return group;

        static AppearanceGroup Reject(AppearanceGroup g, string why)
        {
            g.Reject = why;
            return g;
        }
    }

    /// <summary>
    /// 把一条补丁的 <c>When</c> 翻成 GSQ；<paramref name="dropIsOutdoors"/> 时先把
    /// <c>IsOutdoors</c> 摘掉（它由 <c>Outdoors</c>/<c>Indoors</c> 两个字段承载）。
    /// </summary>
    private static string? Translate(PatchVerdict v, bool dropIsOutdoors, out string? error)
    {
        error = null;
        JObject when = v.Patch.When!;
        if (dropIsOutdoors)
        {
            when = (JObject)when.DeepClone();
            foreach (JProperty p in when.Properties()
                         .Where(x => x.Name.Equals("IsOutdoors", StringComparison.OrdinalIgnoreCase)).ToList())
                p.Remove();

            // 摘完什么都不剩 ⇒ 会变成**无条件**条目，而无条件条目会压住该 NPC 的其它条目
            // （Precedence 比原版高）。那多半不是作者的本意，拒绝而不是猜。
            if (when.Count == 0)
            {
                error = $"`{v.Patch.Target}` 的条件只剩 `IsOutdoors` —— 摘掉后没有任何条件，"
                      + "无条件 Appearance 条目会压住其它条目，不发射";
                return null;
            }
        }

        GsqTranslation t = GsqTranslator.Translate(WhenParser.Parse(when));
        if (!t.Success)
        {
            error = $"`{v.Patch.Target}` 上有一组条件翻不成 GSQ：{t.FailureReason}";
            return null;
        }
        // 恒假的条目挂进 Appearance 没有意义（永远命不中），拒绝而不是写个死条目。
        if (t.NeverApplies)
        {
            error = $"`{v.Patch.Target}` 上有一组条件折叠后恒假，挂进 Appearance 也永远命不中";
            return null;
        }
        return t.Gsq;
    }

    /// <summary>读 <c>When</c> 里的布尔条件；给的不是布尔就报错（不猜）。</summary>
    private static bool? BoolOf(JObject when, string name, out string? error)
    {
        error = null;
        JProperty? p = when.Properties()
            .FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (p == null) return null;
        if (p.Value.Type != JTokenType.Boolean)
        {
            error = $"`When` 里的 `{name}` 不是布尔（Appearance 只要 true/false）";
            return null;
        }
        return p.Value.Value<bool>();
    }
}
