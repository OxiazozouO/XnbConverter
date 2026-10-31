using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 「事件键前置条件」的原生改写：把 CP 的 <c>When</c> 译成一段 <c>G &lt;GSQ&gt;</c> 追加到事件键上。
/// <para>
/// 原版 <c>Data/Events/&lt;地点&gt;</c> 的条目键形如 <c>&lt;事件ID&gt;/&lt;前置条件段&gt;/…</c>
/// （<c>Event.SplitPreconditions</c>，Event.cs:11720 按 <c>/</c> 切），段间 AND。
/// 其中 <c>G</c> 是**通用 GSQ 入口**（<c>Preconditions.cs:744-753</c>，规范名 <c>GameStateQuery</c>），
/// 它把该段其余实参按空格重拼后交给 <c>GameStateQuery.CheckConditions</c>。
/// </para>
/// <para>
/// <b>为什么用 <c>G</c> 而不是逐个令牌</b>：<c>G</c> 是<b>精确</b>的，而专用令牌有语义差 ——
/// <c>O</c> 含订婚（CP 的 Spouse 不含）、<c>f</c> 收的是**点数**（1 心 = 250 点）。
/// 而且我们**已经有** <c>GsqTranslator</c> 产出精确 GSQ，直接塞进 <c>G</c> 即可。
/// </para>
/// <para>
/// <b>只在「新事件」时改写</b>（实测：语料 239 条新事件 vs 33 条覆盖既有事件）。
/// </para>
/// <para>
/// ⚠ <b>拒绝覆盖既有事件的真正理由（2026-10-07 逐行核对后改正）</b>：
/// 原注释写「改键会新增一条而原事件仍在，<b>两边都触发</b>」—— <b>「两边都触发」是错的</b>，
/// 游戏不会都触发。真正拦得住的是**另一条入口**：
/// <list type="bullet">
/// <item><c>GameLocation.checkForEvents</c>（<c>:15659-15668</c>）：按文件键序取
///   <b>首个前置条件命中</b>的键并 <c>break</c> ⇒ 我们前插的那条先命中 ⇒ 这条路径**是对等的**；</item>
/// <item><c>GameLocation.findEventById</c>（<c>:15680-15718</c>）：按 ID **线性取第一个键**，
///   <b>根本不看前置条件</b>。
///   <b>覆盖既有事件时</b>原键还在 ⇒ 同一 ID 有两条键 ⇒ 它拿到的是**我们前插的那条**，
///   于是「条件不成立时该走原版内容」的路径上**永远拿到改写后的内容** ⇒ **不对等**；</item>
/// <item>另：<c>checkEventPrecondition</c>（<c>:16460-16463</c>）只要 ID 已在
///   <c>eventsSeen</c> 里就返回 <c>-1</c> ⇒ 同一 ID 的事件**只会触发一次**。</item>
/// </list>
/// ⇒ 覆盖既有事件**必须拒绝**（决定不变，理由换掉）。
/// </para>
/// <para>
/// ★ 反过来，**新事件**的「同 ID 多键」是**安全的**：新 ID 在原版里没有键，
/// <c>findEventById</c> 只会拿到我们写的那一条；多条之间靠前插维持「CP 后写赢」的顺序，
/// 两条入口取到的**是同一条**（都是最后写的那条）。与 Task 22 的结论一致。
/// </para>
/// </summary>
public static class EventKeyRewriter
{
    private const string EventPrefix = "Data/Events/";

    /// <summary>
    /// 识别「事件键前置条件」形态并返回<b>改写后</b>的补丁；不满足前提时返回 <c>null</c>。
    /// 纯函数：不改动入参，也不读资源树（目标资产里已有的事件 ID 由调用方取好传入）。
    /// </summary>
    /// <param name="gsq">已译好的 GSQ（来自 <c>GsqTranslator</c>）。</param>
    /// <param name="existingEventIds">
    /// 目标资产里**已有**的事件 ID 集合。
    /// <c>null</c> 表示该资产不在资源树中（自建地点）—— 此时无法判定是否覆盖，<b>一律不改写</b>。
    /// </param>
    public static JObject? TryRewrite(ChangePatch patch, string? gsq, IReadOnlySet<string>? existingEventIds)
    {
        // 前提 1：Action == EditData
        if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(gsq)) return null;

        // GSQ 会被按空格重拼，但段本身是按 `/` 切的 —— GSQ 里出现 `/` 会把它劈成两段。
        // 我们的 GSQ 理论上不含 `/`，出现即拒绝，不猜。
        if (gsq.Contains('/')) return null;

        // 前提 2：Target 恰好一个、形如 Data/Events/<地点>
        string target = (patch.Target ?? "").Replace('\\', '/').Trim();
        if (target.Contains(',')) return null;
        if (!target.StartsWith(EventPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        string location = target[EventPrefix.Length..];
        if (location.Length == 0 || location.Contains('/') || location.Contains("{{")) return null;

        // 落点被 TargetField/Fields 改变时事件键约定不再适用。
        if (patch.Raw["TargetField"] != null || patch.Raw["Fields"] != null) return null;

        // 资产不在树里 → 判不了「覆盖 vs 新增」，不猜。
        if (existingEventIds == null) return null;

        if (patch.Raw["Entries"] is not JObject entries || entries.Count == 0) return null;

        var newEntries = new JObject();
        foreach (JProperty entry in entries.Properties())
        {
            // 键必须长成事件键的样子（`<ID>/<前置条件…>`）。没有 `/` 的（如原版的 `didntHear`）
            // 不是事件键 —— 给它加 `/G` 会把它**变成**事件，语义完全不同。
            int slash = entry.Name.IndexOf('/');
            if (slash <= 0) return null;

            string id = entry.Name[..slash];

            // ⚠ 覆盖既有事件时**绝不能**改键（改了就变成新增，原事件仍在 → 两边都触发）。
            if (existingEventIds.Contains(id)) return null;

            // 已经带了 G 段就不叠加 —— 交给人工，避免写出重复条件。
            if (entry.Name.Contains("/G ", StringComparison.Ordinal)) return null;

            string baseKey = entry.Name.TrimEnd('/');
            newEntries[baseKey + "/G " + gsq] = entry.Value.DeepClone();
        }

        var rewritten = (JObject)patch.Raw.DeepClone();
        rewritten["Entries"] = newEntries;
        rewritten.Remove("When");
        return rewritten;
    }

    /// <summary>
    /// 从资源树取目标 <c>Data/Events/&lt;地点&gt;</c> 里**已有**的事件 ID。
    /// 资产不在树里（自建地点）时返回 <c>null</c> —— 判不了「覆盖 vs 新增」，调用方据此拒绝改写。
    /// <para>
    /// <b>判定与写入必须调这同一个函数</b>，否则两边对「哪些 ID 是新的」会有不同答案。
    /// </para>
    /// </summary>
    public static IReadOnlySet<string>? LoadExistingEventIds(ResourceTree? tree, string? target)
    {
        string t = (target ?? "").Replace('\\', '/').Trim();
        if (tree == null || !t.StartsWith(EventPrefix, StringComparison.OrdinalIgnoreCase)) return null;

        string? path = tree.Resolve(t);
        if (path == null || !File.Exists(path)) return null;

        try
        {
            JObject data = JObject.Parse(File.ReadAllText(path));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JProperty p in data.Properties())
            {
                int slash = p.Name.IndexOf('/');
                if (slash > 0) ids.Add(p.Name[..slash]);
            }
            return ids;
        }
        catch (Exception)
        {
            return null;   // 读不了就当判不了
        }
    }
}
