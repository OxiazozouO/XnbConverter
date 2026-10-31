using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Normalizing;

/// <summary>一条季节变体：从 <see cref="FromFile"/> 的 <see cref="FromArea"/> 取出这一季的图。</summary>
public sealed record SeasonalVariant(string Season, string FromFile, ImageArea FromArea);

/// <summary>
/// 一个可发射的<b>四季贴图组</b>：同一 <see cref="Target"/> 上的 4 条 <c>EditImage</c>（春/夏/秋/冬各一条），
/// 各自把一季的图块写进同一块 <see cref="ToArea"/>。
/// <para>
/// 原生最简形式（<c>Object.cs:1940-1943</c>）：把四季图排成 <b>4 个连续索引</b>，
/// 并让 <c>Data/BigCraftables</c> 条目的 <c>Name</c> 含 <c>Seasonal</c> ——
/// 游戏自己按 <c>GetSeasonIndex()</c> 取当季那一格。<b>4 条补丁 → 1 张贴图 + 1 处数据改写</b>。
/// </para>
/// </summary>
public sealed class SeasonalSpriteGroup
{
    /// <summary>被改的贴图资产名（CP 的 <c>Target</c>，已归一成正斜杠）。</summary>
    public string Target = "";

    /// <summary>四季写入的同一块目标区域（4 条必须一致，否则不成组）。</summary>
    public ImageArea ToArea;

    /// <summary>按 春/夏/秋/冬 排好的变体，恒 4 条。条目 key 是 <c>Data/BigCraftables</c> 的条目键。</summary>
    public List<SeasonalVariant> Variants = new();

    /// <summary><c>Data/BigCraftables</c> 里以该贴图为 <c>Texture</c> 的那个条目键（唯一消费方）。</summary>
    public string BigCraftableEntry = "";

    /// <summary>
    /// 发射后<b>丢失</b>的条件维度（如 <c>IsOutdoors</c>）。非空 ⇒ 该组是<b>近似</b>，
    /// 必须如实报出来 —— 原生机制不看室内外，而这 212 条全带 <c>IsOutdoors: true</c>。
    /// </summary>
    public List<string> LostConditions = new();

    /// <summary>非空表示<b>不成组</b>（保持原判，不发射）。必须逐字报出原因。</summary>
    public string? Reject;
}

/// <summary>
/// <c>EditImage × Season</c> 的<b>集合发射</b>规划器（Task 29）。
/// <para>
/// 与 <see cref="VariantNormalizer"/> 的分工：那个只管<b>报告</b>「能不能」，
/// 这个真的决定<b>发射什么</b>。纯函数、不碰文件系统 —— 图片合成与写盘在
/// <see cref="Merging.SeasonalSpriteEmitter"/>。
/// </para>
/// <para>
/// <b>宁可不成组，也不半套发射</b>：任何一条前提不成立，整组保持原判。
/// </para>
/// </summary>
public static class SeasonalSpritePlanner
{
    /// <summary>四季的枚举名与<b>顺序</b>（与 <c>Season</c> 枚举一致：Spring=0…Winter=3）。</summary>
    private static readonly string[] SeasonOrder = { "Spring", "Summer", "Fall", "Winter" };

    /// <summary>
    /// 允许出现在 <c>When</c> 里、但原生机制<b>装不下</b>的条件基名（会导致「近似」标记）。
    /// 之外的任何条件键 ⇒ <b>不成组</b>。
    /// </summary>
    private static readonly HashSet<string> ToleratedExtra =
        new(new[] { "IsOutdoors", "LocationContext" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 规划全部四季贴图组。<paramref name="verdicts"/> 里<b>已经会被逐条写入</b>的补丁不参与
    /// —— 它们不该被集合发射重复处理。默认口径是 <see cref="WritePolicy.ShouldWrite"/>；
    /// 调用方若另有「本次真的会写」的集合（如烘焙后的），用 <paramref name="willWrite"/> 传进来。
    /// </summary>
    public static IReadOnlyList<SeasonalSpriteGroup> Plan(IEnumerable<PatchVerdict> verdicts,
        Func<PatchVerdict, bool>? willWrite = null)
    {
        willWrite ??= WritePolicy.ShouldWrite;
        var list = verdicts.ToList();
        Dictionary<string, List<Consumer>> textureToConsumers = BigCraftableConsumers(list);

        var byTarget = new Dictionary<string, List<PatchVerdict>>(StringComparer.OrdinalIgnoreCase);
        foreach (PatchVerdict v in list)
        {
            if (!v.Patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase)) continue;
            if (v.Patch.When == null) continue;              // 无条件的不成「四季组」
            if (willWrite(v)) continue;                      // 已经逐条写了的，不重复发射
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

        var groups = new List<SeasonalSpriteGroup>();
        foreach ((string target, List<PatchVerdict> bucket) in byTarget)
            groups.Add(PlanOne(target, bucket, textureToConsumers));
        groups.Sort((a, b) => string.CompareOrdinal(a.Target, b.Target));
        return groups;
    }

    /// <summary><c>Data/BigCraftables</c> 里一个以某贴图为 <c>Texture</c> 的条目。</summary>
    private readonly record struct Consumer(string Key, int SpriteIndex);

    /// <summary>
    /// 单个 target 上的一组补丁 → 一个组。前提逐条检查，任何一条不成立就写进 <see cref="SeasonalSpriteGroup.Reject"/>。
    /// </summary>
    private static SeasonalSpriteGroup PlanOne(string target, List<PatchVerdict> bucket,
        Dictionary<string, List<Consumer>> textureToConsumers)
    {
        var group = new SeasonalSpriteGroup { Target = target };

        if (bucket.Count == 0)
            return Reject(group, "该 target 上没有条件 EditImage");

        // 每条必须：季节值落在四季里、PatchMode 是默认的 Replace、ToArea 解析得出。
        var bySeason = new Dictionary<string, SeasonalVariant>(StringComparer.OrdinalIgnoreCase);
        ImageArea? toArea = null;
        var lost = new List<string>();

        foreach (PatchVerdict v in bucket)
        {
            if (v.Patch.Raw["PatchMode"] != null)
                return Reject(group, "带 PatchMode（非默认 Replace），四季写进同一块区域的前提不成立");

            JObject when = v.Patch.When!;   // Plan() 已滤掉无 When 的
            // ⚠ 把底层原因**带出来**，别用自己的兜底文案盖掉 ——
            //   「没给出 Season」与「是补集」是两种不同的数据问题，读者要能分辨。
            IReadOnlyList<string>? seasons = SeasonOfAll(when, out string? seasonWhy);
            if (seasons == null) return Reject(group, seasonWhy!);
            foreach (string season in seasons)
                if (bySeason.ContainsKey(season))
                    return Reject(group, $"Season `{season}` 出现两条 —— 同一季不该有两个版本");

            foreach (string extra in ExtraConditionNames(when))
            {
                // 比的是**基名**：真实写法带参数（`LocationContext |contains=Island, Desert`），
                // 拿原始键去查白名单会把它们全误杀。
                string bare = BaseName(extra);
                if (!ToleratedExtra.Contains(bare))
                    return Reject(group, $"When 里还有 `{extra}` —— 原生 Seasonal 机制装不下，"
                        + "硬发射会把它静默丢掉");
                if (!lost.Contains(bare, StringComparer.OrdinalIgnoreCase)) lost.Add(bare);
            }

            if (!EditImageApplier.TryParseAreas(v.Patch, out ImageArea? from, out ImageArea? to, out string? err))
                return Reject(group, $"FromArea/ToArea 解析失败：{err}");
            if (from == null || to == null)
                return Reject(group, "缺少 FromArea 或 ToArea（四季必须写进同一块已知区域）");

            if (toArea == null) toArea = to;
            else if (toArea.Value != to.Value)
                return Reject(group, $"ToArea 不一致（{toArea.Value} 与 {to.Value}）—— 四季写进不同区域，不是一组");

            if (string.IsNullOrEmpty(v.Patch.FromFile))
                return Reject(group, "缺少 FromFile");
            foreach (string season in seasons)
                bySeason[season] = new SeasonalVariant(season, v.Patch.FromFile!, from.Value);
        }

        // 写入区域必须是**一个 BigCraftable 精灵格**：16x32、且落在贴图左上角。
        // 四季横排后，第 i 季的格子在 x = i*16，游戏按 SpriteIndex（=0）取到 (0,0,16,32)。
        // 区域不是这一格，合成出来的贴图就不是游戏要读的那张 —— 宁可不成组。
        ImageArea cell = toArea!.Value;
        if (cell != new ImageArea(0, 0, 16, 32))
            return Reject(group, $"ToArea 是 {cell}，不是 BigCraftable 的精灵格 (0,0,16,32) —— "
                + "合出来的贴图不是游戏要读的那张");

        // 消费方：必须是**恰好一个** Data/BigCraftables 条目，且 SpriteIndex 为 0。
        // 说不清消费方就不发射 —— 贴图会被改宽，若有别的消费方（地图图集等）会被改坏。
        if (!textureToConsumers.TryGetValue(target, out List<Consumer>? consumers))
            return Reject(group, "找不到以该贴图作 Texture 的 Data/BigCraftables 条目 —— "
                + "原生 Seasonal 机制改的是 BigCraftable，消费方不明不敢改");
        if (consumers.Count != 1)
            return Reject(group, $"有 {consumers.Count} 个 Data/BigCraftables 条目用这张贴图，无法确定改哪一个");

        group.BigCraftableEntry = consumers[0].Key;
        if (consumers[0].SpriteIndex != 0)
            return Reject(group, $"消费方 SpriteIndex 是 {consumers[0].SpriteIndex}，不是 0 —— "
                + "本发射把整张贴图换成 4 格，索引不为 0 会指到贴图外");

        // 缺季不拒绝：用**底图**（当前贴图）补那一季 —— CP 下该季没有补丁就是保持底图。
        // 四季 BigCraftable 必须正好 4 格，游戏按季索引；缺格会串季/越界。
        foreach (string s in SeasonOrder)
        {
            group.Variants.Add(bySeason.TryGetValue(s, out SeasonalVariant? v)
                ? v
                : new SeasonalVariant(s, "", new ImageArea(0, 0, cell.Width, cell.Height)));
        }
        group.ToArea = cell;
        group.LostConditions = lost;
        return group;

        static SeasonalSpriteGroup Reject(SeasonalSpriteGroup g, string why)
        {
            g.Reject = why;
            return g;
        }
    }

    /// <summary>
    /// 从判定结果里读出「贴图 → <c>Data/BigCraftables</c> 条目」的引用关系。
    /// 数据来源是<b>本包自己的</b> <c>EditData Data/BigCraftables</c> 补丁 —— 与写入走同一份数据。
    /// </summary>
    private static Dictionary<string, List<Consumer>> BigCraftableConsumers(IEnumerable<PatchVerdict> verdicts)
    {
        var map = new Dictionary<string, List<Consumer>>(StringComparer.OrdinalIgnoreCase);

        foreach (PatchVerdict v in verdicts)
        {
            if (!v.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Normalize(v.Patch.Target).Equals("Data/BigCraftables", StringComparison.OrdinalIgnoreCase)) continue;
            if (v.Patch.Raw["Entries"] is not JObject entries) continue;

            foreach (JProperty p in entries.Properties())
            {
                if (p.Value is not JObject entry) continue;
                string texture = Normalize(entry["Texture"]?.Value<string>());
                if (texture.Length == 0) continue;

                var consumer = new Consumer(p.Name, entry["SpriteIndex"]?.Value<int?>() ?? -1);
                if (!map.TryGetValue(texture, out List<Consumer>? list)) map[texture] = list = new List<Consumer>();
                if (!list.Contains(consumer)) list.Add(consumer);
            }
        }
        return map;
    }

    /// <summary>读「哪一季」—— 与四季图集那套**共用一处判定**（<see cref="SeasonToken"/>）。</summary>
    private static IReadOnlyList<string>? SeasonOfAll(JObject when, out string? reason) =>
        SeasonToken.OfAll(when, out reason);

    /// <summary><c>When</c> 里除 <c>Season</c> 之外的键名（原样，含 <c>|contains=</c> 参数）。</summary>
    private static IEnumerable<string> ExtraConditionNames(JObject when) =>
        when.Properties().Select(p => p.Name)
            .Where(n => !BaseName(n).Equals("Season", StringComparison.OrdinalIgnoreCase));

    /// <summary>令牌名的基名：冒号前、竖线前（与 <c>VariantNormalizer.BaseName</c> 同一口径）。</summary>
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
