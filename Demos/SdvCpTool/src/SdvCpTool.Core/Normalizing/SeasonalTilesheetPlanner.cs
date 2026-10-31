using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Normalizing;

/// <summary>
/// 一个可发射的<b>四季图集组</b>：同一图集资产上按季分叉的多条条件 <c>EditImage</c>。
/// <para>
/// 原生最简形式（<c>GameLocation.cs:16439-16447</c> <c>GetSeasonalTilesheetName</c>）：
/// 把四季各写成 <c>spring_X</c> / <c>summer_X</c> / <c>fall_X</c> / <c>winter_X</c> 四份资产，
/// 再把引用它的地图 <c>TileSheet.ImageSource</c> 指到 <c>spring_X</c> —— 游戏按当季**替换文件名前缀**，零条件。
/// </para>
/// <para>
/// 只在「室外、非 Summit、非 Desert」生效（<c>:16417</c>）—— 恰与语料里 <c>IsOutdoors: true</c> 吻合。
/// </para>
/// </summary>
public sealed class SeasonalTilesheetGroup
{
    /// <summary>被季节化编辑的图集资产名（已归一成正斜杠）。</summary>
    public string Target = "";

    /// <summary>
    /// 按 春/夏/秋/冬 归好的补丁（每季可能不止一条编辑，按补丁原序）。
    /// <para>
    /// 存**补丁本身**而不是解析好的区域 —— 与 <c>EditImageApplier</c> 共用同一套
    /// <c>FromArea</c>/<c>ToArea</c>/<c>PatchMode</c> 解析，绝不两处各解一遍。
    /// </para>
    /// </summary>
    public Dictionary<string, List<Judging.PatchVerdict>> BySeason = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 发射后<b>丢失</b>的条件维度。非空 ⇒ 该组是<b>近似</b>：
    /// 原生机制只看「是否室外」，装不下别的。
    /// </summary>
    public List<string> LostConditions = new();

    /// <summary>非空表示<b>不成组</b>（保持原判）。必须逐字报出原因。</summary>
    public string? Reject;
}

/// <summary>
/// 四季<b>图集</b>（<c>EditImage × Season</c>）的集合发射规划器。
/// <para>
/// 与 <see cref="SeasonalSpritePlanner"/> 的分工：那个走「BigCraftable 四季索引」，
/// 这个走「文件名季节前缀 + 改地图图集指向」。判据不同，**不能混用一个**。
/// </para>
/// </summary>
public static class SeasonalTilesheetPlanner
{
    private static readonly string[] SeasonOrder = { "Spring", "Summer", "Fall", "Winter" };

    /// <summary>
    /// 允许出现、但原生机制<b>装不下</b>的条件基名（会导致「近似」标记）。
    /// </summary>
    private static readonly HashSet<string> ToleratedExtra =
        new(new[] { "IsOutdoors", "LocationContext" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 额外的条件求值器：返回 <c>true</c>/<c>false</c> = 该条件可静态求值；
    /// <c>null</c> = 求不了 ⇒ **整组拒绝**（配置令牌没给默认值、或条件本身是动态的）。
    /// <para>
    /// 为什么必须有它：实测语料里唯一的四季图集组（SVE 的 <c>Tilesheets/ChairTiles</c>）
    /// 有一条补丁带 <c>SeasonalEdits</c> —— 那是 CP 的<b>配置令牌</b>，默认 <c>true</c>。
    /// 不求值就只能整组拒绝，功能等于零。求值器由 CLI 用 <c>ConfigSchemaTokens</c> 提供。
    /// </para>
    /// </summary>
    public delegate bool? ExtraConditionEvaluator(string baseName, JToken value);

    public static IReadOnlyList<SeasonalTilesheetGroup> Plan(IEnumerable<PatchVerdict> verdicts,
        Func<PatchVerdict, bool>? willWrite = null, ExtraConditionEvaluator? evaluate = null)
    {
        willWrite ??= WritePolicy.ShouldWrite;
        var byTarget = new Dictionary<string, List<PatchVerdict>>(StringComparer.OrdinalIgnoreCase);

        foreach (PatchVerdict v in verdicts)
        {
            if (!v.Patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase)) continue;
            if (v.Patch.When == null) continue;
            if (willWrite(v)) continue;
            // ⚠ **Target 含令牌的一律不参与**：静态确定不了它是哪个资产。
            //   实测代价：SVE 的 `Maps/{{season}}_z_extras` 4 条（条件其实是
            //   `HasMod: <重着色 mod>`、`{{season}}` 只在运行期展开）被按 target 字符串
            //   分成一组、当成"四季变体"，还给它们**覆盖写**了「四季组未发射」——
            //   而那 4 条本已被正确判为 `NotApplicable`（没装那些 mod ⇒ 恒假），
            //   planner 多嘴把它们的说明盖掉了。
            if ((v.Patch.Target ?? "").Contains("{{", StringComparison.Ordinal)) continue;
            string target = Normalize(v.Patch.Target);
            if (target.Length == 0) continue;
            if (!byTarget.TryGetValue(target, out List<PatchVerdict>? bucket))
                byTarget[target] = bucket = new List<PatchVerdict>();
            bucket.Add(v);
        }

        var groups = new List<SeasonalTilesheetGroup>();
        foreach ((string target, List<PatchVerdict> bucket) in byTarget)
            groups.Add(PlanOne(target, bucket, evaluate));
        groups.Sort((a, b) => string.CompareOrdinal(a.Target, b.Target));
        return groups;
    }

    private static SeasonalTilesheetGroup PlanOne(string target, List<PatchVerdict> bucket,
        ExtraConditionEvaluator? evaluate)
    {
        var group = new SeasonalTilesheetGroup { Target = target };
        var lost = new List<string>();

        foreach (PatchVerdict v in bucket)
        {
            JObject when = v.Patch.When!;
            IReadOnlyList<string>? seasons = SeasonOfAll(when, out string? naReason);
            if (seasons == null) return Reject(group, naReason!);

            // 其它条件：先看容忍表，再看求值器；都不认才拒绝。
            foreach (JProperty p in when.Properties())
            {
                string bare = BaseName(p.Name);
                if (bare.Equals("Season", StringComparison.OrdinalIgnoreCase)) continue;

                if (ToleratedExtra.Contains(bare))
                {
                    if (!lost.Contains(bare, StringComparer.OrdinalIgnoreCase)) lost.Add(bare);
                    continue;
                }

                bool? ok = evaluate?.Invoke(bare, p.Value);
                if (ok == null)
                    return Reject(group, $"When 里还有 `{p.Name}` —— 原生季节前缀机制装不下，"
                        + "且该条件无法静态求值，硬发射会把它静默丢掉");
                // 求值为假 ⇒ 这条补丁本就不适用，跳过它（不是整组失败）。
                if (ok == false) goto SkipPatch;
            }

            if (!EditImageApplier.TryParseMode(v.Patch.Raw["PatchMode"]?.Value<string>(),
                    out _, out string? modeError))
                return Reject(group, $"PatchMode 无法解析：{modeError}");

            if (string.IsNullOrEmpty(v.Patch.FromFile))
                return Reject(group, "缺少 FromFile");

            foreach (string season in seasons)
            {
                if (!group.BySeason.TryGetValue(season, out List<Judging.PatchVerdict>? list))
                    group.BySeason[season] = list = new List<Judging.PatchVerdict>();
                list.Add(v);
            }
            continue;

        SkipPatch: ;
        }

        // ★ 缺的季**不拒绝**：用底图（当前资产）补齐那一季，正是 CP 下“该季没有补丁”的效果。
        //   四季图集机制要求地图指向 `spring_X`、游戏按季替换前缀 ⇒ 四份文件都必须存在；
        //   缺季时复制底图即可，既不会串季，也不会少内容。
        if (group.BySeason.Count == 0)
            return Reject(group, "四季图集组里没有任何一季可用的补丁");

        group.LostConditions = lost;
        return group;

        static SeasonalTilesheetGroup Reject(SeasonalTilesheetGroup g, string why)
        {
            g.Reject = why;
            return g;
        }
    }

    /// <summary>读「哪一季」—— 与四季 BigCraftable 那套**共用一处判定**（<see cref="SeasonToken"/>）。</summary>
    private static IReadOnlyList<string>? SeasonOfAll(JObject when, out string? reason) =>
        SeasonToken.OfAll(when, out reason);

    private static string BaseName(string key)
    {
        int colon = key.IndexOf(':');
        if (colon >= 0) key = key[..colon];
        int pipe = key.IndexOf('|');
        if (pipe >= 0) key = key[..pipe];
        return key.Trim();
    }

    private static string Normalize(string? name) => (name ?? "").Replace('\\', '/').Trim('/');
}
