using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;

namespace SdvCpTool.Core.Normalizing;

/// <summary>一个 <c>(载体, 维度, 资产类型)</c> 格子的实测条数与结论。</summary>
public sealed class EmitterCell
{
    public VariantCarrier Carrier;
    public VariantDimension Dimension;

    /// <summary>归一化后的资产类型（大小写不敏感、去掉条目/子路径那一层）。</summary>
    public string AssetType = "";

    public int PatchCount;
    public EmitPlan Plan;
}

/// <summary>归一化的量化结果。</summary>
public sealed class NormalizationReport
{
    /// <summary>条件里能识别出维度的补丁总数。</summary>
    public int DimensionPatches;

    /// <summary>各结论的补丁数。<b>四档之和恒等于 <see cref="DimensionPatches"/></b>。</summary>
    public int Native, None, CapacityExceeded, Unknown;

    /// <summary>识别出的变体意图（桶内 ≥2 条）。</summary>
    public readonly List<VariantIntent> Intents = new();

    /// <summary>
    /// ★ <b>收敛量</b>：被判「有原生最简形式」的意图里，<b>被压掉的补丁数</b>
    /// = Σ(<see cref="VariantIntent.PatchCount"/> − 1)。
    /// <para>
    /// 这就是「N 个实现 → 1 个最简实现」的直接量化。
    /// 例如 4 条各写一季的补丁归一成 1 个意图 → 这里记 3。
    /// </para>
    /// </summary>
    public int CollapsedPatches;

    /// <summary>
    /// 判为「有原生最简形式」的意图数 —— 这些意图里的补丁**逐条改写**成原生形式。
    /// <para><b>注意</b>：逐条改写<b>不减少条数</b>，与「合并」是两回事。</para>
    /// </summary>
    public int RewrittenIntents;

    /// <summary>被逐条改写成原生形式的补丁数。</summary>
    public int RewrittenPatches;

    /// <summary>★ 真的把多条补丁<b>合并</b>成更少产物的意图数。</summary>
    public int MergedIntents;

    /// <summary>
    /// 收敛会<b>丢语义</b>的意图数与补丁数（条件里带了本维度之外的东西）。
    /// <para>
    /// 实测依据：SVE 季节灌木 208 条全部带 <c>IsOutdoors</c>，归一到原版季节机制就丢掉「室外」。
    /// <b>报告必须把这一档单列</b>，否则会把有损收敛算成满分。
    /// </para>
    /// </summary>
    public int ApproximateIntents, ApproximatePatches;

    /// <summary>
    /// ★ <b>本次真的被集合发射的意图数与补丁数</b>（CLI 在发射后回填）。
    /// <para>
    /// 与 <see cref="CollapsedPatches"/> 的区别就是「<b>推的</b>」与「<b>做到的</b>」：
    /// 前者查发射表，后者查发射结局。<b>报给用户看的收敛量应当是这一对。</b>
    /// </para>
    /// </summary>
    public int EmittedIntents, EmittedPatches;

    /// <summary>被拒的意图数与补丁数（前提不成立，整组保持原判）—— 与「没查」是两回事。</summary>
    public int RejectedIntents, RejectedPatches;

    /// <summary>所有非空格子。</summary>
    public readonly List<EmitterCell> Cells = new();

    /// <summary>维度 → 出现过的载体集合。集合大小即该维度的<b>实现熵</b>。</summary>
    public readonly Dictionary<VariantDimension, HashSet<VariantCarrier>> CarrierSpread = new();

    /// <summary>
    /// 有最简形式的比例（含装不下的那档）。
    /// <para><b>分母为 0 时返回 0</b>，不要让它变成 NaN 混进报告。</para>
    /// </summary>
    public double NativeRatio => DimensionPatches == 0 ? 0 : (double)Native / DimensionPatches;

    /// <summary>
    /// ★ <b>回填一次集合发射的结局</b>：把落在同一 <paramref name="baseName"/> × 载体 × 维度上的
    /// 意图标记为「已发射」或「被拒」。
    /// <para>
    /// 写入器发射时只知道「资产名」，归一化器只知道「意图」—— 这里是两者唯一的接缝，
    /// <b>一处对齐</b>，不在两侧各算一遍。
    /// </para>
    /// </summary>
    public void MarkEmitted(string baseName, VariantCarrier carrier, VariantDimension dimension,
        string mechanism, bool emitted, int patches)
    {
        string want = (baseName ?? "").Replace('\\', '/').Trim('/');
        foreach (VariantIntent intent in Intents)
        {
            if (intent.Carrier != carrier || intent.Dimension != dimension) continue;
            if (!string.Equals(intent.Base.Replace('\\', '/').Trim('/'), want, StringComparison.OrdinalIgnoreCase))
                continue;

            if (emitted)
            {
                intent.EmittedAs = mechanism;
                EmittedIntents++;
                EmittedPatches += patches;
            }
            else
            {
                RejectedIntents++;
                RejectedPatches += patches;
            }
            return;
        }
    }
}

/// <summary>
/// ★ <b>变体归一化器</b> —— 编译器里的「指令选择」遍。
/// <para>
/// 输入是既有流水线（M1 判定 → M2 翻译）产出的 <see cref="PatchVerdict"/>；
/// 输出是<b>同一条数据被几种写法实现</b>的量化 + 每个格子的发射结论。
/// </para>
/// <para>
/// <b>本遍不改写任何补丁</b> —— 它只回答「能归一到什么」。
/// 真正发射由写入器按 <see cref="EmitterTable"/> 的结论执行。
/// </para>
/// </summary>
public static class VariantNormalizer
{
    /// <summary>
    /// 令牌名 → 维度。键是 <c>When</c> 的 KEY 的<b>基名</b>（冒号前、竖线前、去掉 <c>|contains=</c> 参数），小写。
    /// </summary>
    /// <remarks>
    /// 「名字就是行为」：这些名字全部来自 CP 的内置令牌表，<b>只有逐条确证过的才收</b>。
    /// </remarks>
    private static readonly Dictionary<string, VariantDimension> TokenDimensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["season"] = VariantDimension.Season,
        ["spouse"] = VariantDimension.Marriage,
        ["roommate"] = VariantDimension.Marriage,
        ["year"] = VariantDimension.Year,
        ["isoddyear"] = VariantDimension.Year,
        ["isevenyear"] = VariantDimension.Year,
        ["hasseenevent"] = VariantDimension.SeenEvent,
        ["hearts"] = VariantDimension.Hearts,
    };

    private static readonly HashSet<string> Seasons =
        new(new[] { "spring", "summer", "fall", "winter" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>跑一遍归一化。纯函数，不改动入参。</summary>
    public static NormalizationReport Run(IEnumerable<PatchVerdict> verdicts)
    {
        var report = new NormalizationReport();
        var buckets = new Dictionary<(string, VariantCarrier, VariantDimension), VariantIntent>();
        var cellCounts = new Dictionary<(VariantCarrier, VariantDimension, string), int>();

        foreach (PatchVerdict verdict in verdicts)
        {
            VariantDimension? dimension = DimensionOf(verdict);
            if (dimension is null) continue;

            VariantCarrier carrier = CarrierOf(verdict);
            string assetType = AssetTypeOf(verdict.Patch.Target);
            report.DimensionPatches++;

            var cellKey = (carrier, dimension.Value, assetType);
            cellCounts[cellKey] = cellCounts.TryGetValue(cellKey, out int n) ? n + 1 : 1;

            if (!report.CarrierSpread.TryGetValue(dimension.Value, out HashSet<VariantCarrier>? spread))
                report.CarrierSpread[dimension.Value] = spread = new HashSet<VariantCarrier>();
            spread.Add(carrier);

            string @base = verdict.Patch.Target ?? "";
            var key = (@base, carrier, dimension.Value);
            if (!buckets.TryGetValue(key, out VariantIntent? intent))
            {
                intent = new VariantIntent { Base = @base, Carrier = carrier, Dimension = dimension.Value };
                buckets[key] = intent;
            }
            intent.PatchCount++;
            foreach (string value in ValuesOf(verdict, dimension.Value))
                if (!intent.Values.Contains(value, StringComparer.OrdinalIgnoreCase))
                    intent.Values.Add(value);
            foreach (string other in OtherConditionKeysOf(verdict, dimension.Value))
                intent.OtherConditions.Add(other);
        }

        foreach (((VariantCarrier carrier, VariantDimension dimension, string assetType), int count) in cellCounts)
        {
            EmitPlan plan = EmitterTable.Lookup(carrier, dimension, assetType);
            report.Cells.Add(new EmitterCell
            {
                Carrier = carrier, Dimension = dimension, AssetType = assetType,
                PatchCount = count, Plan = plan,
            });
            switch (plan.Kind)
            {
                case EmitKind.Native: report.Native += count; break;
                case EmitKind.None: report.None += count; break;
                case EmitKind.CapacityExceeded: report.CapacityExceeded += count; break;
                case EmitKind.Unknown: report.Unknown += count; break;
            }
        }

        foreach (VariantIntent intent in buckets.Values)
        {
            if (intent.PatchCount < 2) continue;   // 单条不成「变体」，没有可归一的熵
            intent.CoversWholeDimension = intent.Dimension == VariantDimension.Season
                ? intent.Values.Count == Seasons.Count && intent.Values.All(Seasons.Contains)
                : null;
            if (intent.IsApproximate)
            {
                report.ApproximateIntents++;
                report.ApproximatePatches += intent.PatchCount;
            }

            // ★ 真·合并：只有发射表标明「本格会合并多条补丁」的格子才算。
            // ⚠ 多数格子是**逐条改写**（N 条 → N 个原生形式，条数不变），
            //   把它们算进「收敛」是过报 —— 实测踩过（SVE 报出 314 条假的收敛）。
            //   另外「未定」不算，把「没查」算成「已收敛」同样是虚报。
            EmitPlan plan = EmitterTable.Lookup(intent.Carrier, intent.Dimension, AssetTypeOf(intent.Base));
            if (plan.Kind == EmitKind.Native)
            {
                report.RewrittenIntents++;
                report.RewrittenPatches += intent.PatchCount;
                if (plan.MergesPatches)
                {
                    report.MergedIntents++;
                    report.CollapsedPatches += intent.PatchCount - 1;
                }
            }

            report.Intents.Add(intent);
        }

        report.Intents.Sort((a, b) => b.PatchCount.CompareTo(a.PatchCount));
        report.Cells.Sort((a, b) => b.PatchCount.CompareTo(a.PatchCount));
        return report;
    }

    /// <summary>
    /// 从 <c>When</c> 里认出维度。一条补丁<b>只取第一个命中的维度</b> ——
    /// 多维度组合的补丁归到主维度，避免同一条被数进两个格子（会虚增总数）。
    /// </summary>
    private static VariantDimension? DimensionOf(PatchVerdict verdict)
    {
        foreach (string keyName in KeyNamesOf(verdict))
            if (TokenDimensions.TryGetValue(BaseName(keyName), out VariantDimension d))
                return d;
        return null;
    }

    /// <summary>取 <c>When</c> 里的全部键名。优先用判定时折叠后的树（与写入器同源）。</summary>
    private static IEnumerable<string> KeyNamesOf(PatchVerdict verdict)
    {
        if (verdict.FoldedWhen != null)
        {
            foreach (WhenNode node in verdict.FoldedWhen.Descendants())
                if (node.Kind == WhenNodeKind.Key && node.KeyName != null)
                    yield return node.KeyName;
            yield break;
        }

        if (verdict.Patch.When == null) yield break;
        foreach (JProperty prop in verdict.Patch.When.Properties())
            yield return prop.Name;
    }

    /// <summary>
    /// <c>When</c> 的 KEY 可能是 <c>"HasMod |contains=X"</c> 或 <c>"Hearts:Abigail"</c>。
    /// 取冒号前、竖线前的基名。
    /// </summary>
    private static string BaseName(string key)
    {
        int bar = key.IndexOf('|');
        if (bar >= 0) key = key[..bar];
        int colon = key.IndexOf(':');
        if (colon >= 0) key = key[..colon];
        return key.Trim();
    }

    /// <summary>
    /// 取该补丁在<b>指定维度</b>上的取值（令牌的值侧）。取不到时返回空集 —— 不猜。
    /// <para>
    /// <b>只收同维度的键</b>：一条补丁同时带 <c>Season</c> 与 <c>Spouse</c> 时，
    /// 把配偶名混进季节桶会让「值集是否覆盖四季」判错。
    /// </para>
    /// </summary>
    private static IEnumerable<string> ValuesOf(PatchVerdict verdict, VariantDimension dimension)
    {
        foreach (string keyName in KeyNamesOf(verdict))
            if (TokenDimensions.TryGetValue(BaseName(keyName), out VariantDimension d) && d == dimension)
                foreach (string value in ValuesOfKey(verdict, keyName))
                    yield return value;
    }

    /// <summary>
    /// 取该补丁条件里<b>不属于本维度</b>的其余令牌名。非空 ⇒ 收敛后会丢语义（<b>近似</b>）。
    /// </summary>
    private static IEnumerable<string> OtherConditionKeysOf(PatchVerdict verdict, VariantDimension dimension)
    {
        foreach (string keyName in KeyNamesOf(verdict))
            if (!(TokenDimensions.TryGetValue(BaseName(keyName), out VariantDimension d) && d == dimension))
                yield return BaseName(keyName);
    }

    private static IEnumerable<string> ValuesOfKey(PatchVerdict verdict, string keyName)
    {
        if (verdict.FoldedWhen != null)
        {
            foreach (WhenNode node in verdict.FoldedWhen.Descendants())
                if (node.Kind == WhenNodeKind.Key &&
                    string.Equals(node.KeyName, keyName, StringComparison.Ordinal) &&
                    node.Value != null)
                    yield return node.Value;
            yield break;
        }

        if (verdict.Patch.When?[keyName] is JValue { Value: not null } v)
            yield return v.Value.ToString() ?? "";
    }

    /// <summary>
    /// 把目标资产名归到「类型」。**必须大小写不敏感** ——
    /// 语料里 `Characters/schedules` / `characters/schedules` / `Characters/Schedules`
    /// 是同一个资产，不归一化会把一个类型拆成三个桶。
    /// </summary>
    public static string AssetTypeOf(string? target)
    {
        string t = (target ?? "").Split(',')[0].Trim().Replace('\\', '/');
        string low = t.ToLowerInvariant();

        foreach ((string prefix, string canonical) in AssetTypePrefixes)
            if (low.StartsWith(prefix, StringComparison.Ordinal))
                return canonical;

        // ★ **别的 mod 注册的自定义资产** → 也归 `Mods/*`。
        //   形如 `sophie.ConversationTopics`、`furyx639.CustomBush/Data`、
        //   `spacechase0.SpaceCore/ObjectExtensions` —— **第一段带点**就是命名空间约定。
        //   与 `Mods/...` 是**同一个结论**：原版不认识这个资产名，条目由注册它的 mod 的 C# 消费，
        //   没有原版条件位（也不需要 —— 纯 XNB 场景里那段代码本就是那个 mod 自己的）。
        //   ⚠ 不归的话，这些格子会落到 `Unknown`（「待查」），
        //   而其实早就是查清的结论了 —— 表里 `Mods/*` 那两格写的就是它。
        //   实测语料里这一档是 **32 条**（全是 `sophie.ConversationTopics`）。
        //   ⚠ 我一度按「未写入的新建资产带条件」整档估成 249 条 —— **那是我把两件事混了**：
        //     其余 217 条用的是**原版资产路径**（`Characters/Dialogue/Claire` 这类），
        //     只是那个角色本身不在树里，不是自定义命名空间。**订正为 32。**
        if (t.Length > 0 && t.IndexOf('/') is int slash && (slash < 0 ? t : t[..slash]).Contains('.'))
            return "Mods/*";

        return t;
    }

    /// <summary>
    /// 「类型 + 实例名」的多级资产 → 归到类型那一层。
    /// 前缀一律小写（配合大小写不敏感匹配）；<b>顺序敏感，更具体的放前面</b>。
    /// <para>
    /// 不归一化会让<b>每个实例各成一个格子</b>：`Maps/Forest`、`Maps/Town`… 各算一类，
    /// 格子数爆炸、未定虚高。实测踩过（SVE 未定一度报到 657）。
    /// </para>
    /// </summary>
    private static readonly (string Prefix, string Canonical)[] AssetTypePrefixes =
    {
        ("data/festivals", "Data/Festivals"),
        ("data/events/", "Data/Events"),
        ("characters/schedules", "Characters/schedules"),
        // ⚠ `Characters/Dialogue/*` 是 **$query 宿主**，落点与立绘/行走图完全不同 ——
        // 早先被 `characters/` 那条吞掉，约 400 条对话因此混进了立绘那一格。
        ("characters/dialogue", "Characters/Dialogue"),
        ("characters/monsters", "Characters/Monsters"),
        ("strings/schedules", "Strings/schedules"),
        ("data/characters", "Data/Characters"),
        ("data/bigcraftables", "Data/BigCraftables"),
        // ⚠ 不加这条，`Data/PassiveFestivals/<id>` 会**每个实例各成一格**（格子数虚高）。
        ("data/passivefestivals", "Data/PassiveFestivals"),
        ("data/animationdescriptions", "Data/animationDescriptions"),
        ("maps/", "Maps"),
        ("portraits/", "Portraits"),
        ("characters/", "Characters"),
        ("mods/", "Mods/*"),
        ("tilesheets/", "Tilesheets/*"),
        ("loosesprites/", "LooseSprites/*"),
    };

    /// <summary>
    /// 判定载体。**这是启发式，注释写明依据**；判不准一律 <see cref="VariantCarrier.Unknown"/>。
    /// </summary>
    private static VariantCarrier CarrierOf(PatchVerdict verdict)
    {
        ChangePatch patch = verdict.Patch;
        string target = patch.Target ?? "";

        if (patch.Action.Equals("EditMap", StringComparison.OrdinalIgnoreCase))
            return VariantCarrier.Map;

        // 改图 = 改图集 = 变体是同张图上的不同矩形。**按 Action 判，不按 Target 名字猜**：
        // 早先按 `Mods/` 前缀把它们判成 `AssetName` 是错的 —— 那会把发射表里
        // `(Sprite, 季节) → Seasonal` 那条正好适用的一大类判到别的格子去。
        if (patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase))
            return VariantCarrier.Sprite;

        // Load = 新建资产。每个变体一个新资产名时，才是「靠资产名区分」。
        if (patch.Action.Equals("Load", StringComparison.OrdinalIgnoreCase))
            return VariantCarrier.AssetName;

        if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase))
            return VariantCarrier.Unknown;

        // EditData 一律是「条目」载体。**不再区分「键名 vs 值」** ——
        // 同一个资产两种都用得上（`Data/Festivals` 既用 `<NPC>_spouse` 键又用 `$query` 值；
        // `Strings/schedules` 的值是 `$query` 宿主而键不是），一个载体装不下两种机制。
        // 「用哪种」由**资产类型 × 维度**决定，见 EmitterTable。
        return VariantCarrier.Entry;
    }
}
