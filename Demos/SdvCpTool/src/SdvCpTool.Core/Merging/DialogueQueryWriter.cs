using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 同一个 <c>(资产名, 条目键)</c> 上各写了多少条 <c>EditData</c> 补丁、其中多少条带条件。
/// <para>
/// 用途：<c>$query</c> 的假分支只能装"该键合并前的原有值"。若同一个键上还有<b>另一条也带条件的补丁</b>，
/// 把两条都转成 <c>$query</c> 会互相嵌套 —— 游戏解析嵌套 <c>$query</c> 时会拿<b>原始 masterString</b>
/// 重新 <c>Split('#', 2)</c>（<c>Dialogue.cs:646-652</c>），导致取到错误分支。所以只有
/// 「该键上恰有一条条件补丁 + 至少一条无条件补丁（提供旧值）」时才允许转换。
/// </para>
/// </summary>
public sealed class DialogueQueryOverlap
{
    private readonly Dictionary<(string Asset, string Key), (int Conditional, int Unconditional)> _counts = new();

    public static DialogueQueryOverlap Build(IEnumerable<ChangePatch> patches)
    {
        var overlap = new DialogueQueryOverlap();

        foreach (ChangePatch patch in patches)
        {
            if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase))
                continue;
            if (patch.Raw["Entries"] is not JObject entries || entries.Count == 0)
                continue;

            // 会被 SpouseKeyRewriter 改写成 `<NPC>_spouse` 的补丁，实际写的是**兄弟键**，
            // 不占本条键的坑（它要么改成 _spouse 键写入成功，要么整条失败什么都不写）。
            if (SpouseKeyRewriter.TryRewrite(patch) != null)
                continue;

            bool conditional = patch.When != null && patch.When.Count > 0;

            foreach (string asset in Targets(patch.Target))
            {
                foreach (JProperty entry in entries.Properties())
                {
                    (string, string) key = (asset, entry.Name);
                    (int Conditional, int Unconditional) prior =
                        overlap._counts.TryGetValue(key, out var value) ? value : (0, 0);
                    overlap._counts[key] = conditional
                        ? (prior.Conditional + 1, prior.Unconditional)
                        : (prior.Conditional, prior.Unconditional + 1);
                }
            }
        }

        return overlap;
    }

    /// <summary>该键上条件补丁 / 无条件补丁的条数；一条都没有时返回 <c>(0, 0)</c>。</summary>
    public (int Conditional, int Unconditional) Count(string asset, string key) =>
        _counts.TryGetValue((Normalize(asset), key), out var value) ? value : (0, 0);

    private static IEnumerable<string> Targets(string? target) =>
        Cp.AssetNameList.Split(target)          // 令牌里的逗号不是分隔符（见 Cp/AssetNameList）
            .Select(Normalize)
            .Where(t => t.Length > 0);

    private static string Normalize(string target) => target.Replace('\\', '/').Trim('/');
}

/// <summary>
/// 对话值内联 GSQ：<c>$query &lt;GSQ&gt;#&lt;真分支&gt;|&lt;假分支&gt;</c>。
/// <para>
/// 原版机制（<c>StardewValley\Dialogue.cs:646-652</c>，<c>parseDialogueString</c> 构造时调用）：
/// 条件成立取 <c>#</c> 后 <c>|</c> 前的部分，否则取 <c>|</c> 后的部分。原版自己在用
/// （<c>Characters/Dialogue/Abigail.json</c> 的 <c>Mon8</c>）。
/// </para>
/// <para>
/// <b>只对逐条查过调用链的宿主生效</b>（值最终都会走 <c>new Dialogue(...)</c>）：
/// <list type="bullet">
/// <item><c>Characters/Dialogue/&lt;NPC&gt;</c> 的值</item>
/// <item><c>Data/Festivals/&lt;id&gt;</c> 里以 NPC 名为键的对话</item>
/// <item><c>Strings/schedules/&lt;NPC&gt;</c> 的日程结束语</item>
/// <item><c>Strings/StringsFromCSFiles</c> 的 <c>Event.cs.1527</c>（节日主持问答）</item>
/// <item><c>Data/ExtraDialogue</c>（<c>GameLocation.cs:8483/8487/8491/10000</c>；<c>Game1.cs:8929</c>）</item>
/// <item><c>Data/EngagementDialogue</c>（<c>NPC.cs:1660/1681/3848/3852</c>）</item>
/// </list>
/// 其余走 <c>DialogueBox(string)</c> 的路径（地图 <c>Action: Message</c> → <c>Strings/StringsFromMaps</c>、
/// 事件 <c>message</c>/<c>textAboveHead</c>/<c>question</c>）里 <c>$query</c> 是<b>死文本</b>，绝不套用。
/// </para>
/// <para>
/// 判定与写入调用<b>同一个</b> <see cref="TryRewrite"/>：判定阶段只给结构（<paramref name="priorLookup"/>
/// 为 <c>null</c>），写入阶段额外给真实旧值。<c>Compose</c> 是唯一的合成实现。
/// </para>
/// </summary>
public static class DialogueQueryWriter
{
    /// <summary>原版常量（<c>Dialogue.cs:56</c>）。</summary>
    public const string Prefix = "$query ";

    /// <summary>节日主持问答：<c>Event.cs:12559-12561</c> 读的固定键。</summary>
    private const string FestivalHostKey = "Event.cs.1527";

    /// <summary>
    /// 返回把 <c>Entries</c> 的值内联成 <c>$query</c> 后的新补丁；不满足任一前提时返回 <c>null</c>
    /// （保持原判定，绝不硬塞）。纯函数：不改动入参。
    /// </summary>
    /// <param name="gsq">已翻译好的 GSQ（调用方负责先用 <c>GsqTranslator</c> 产出）。</param>
    /// <param name="overlap">全包的重叠计数；为 <c>null</c> 时跳过重叠校验（仅供单测）。</param>
    /// <param name="npcNames">已知 NPC 名集合，用于判定 <c>Data/Festivals</c> 的键是不是 NPC 对话。</param>
    /// <param name="priorLookup">
    /// 写入阶段传入：给出某键合并前的原有值。为 <c>null</c> 表示判定阶段（拿不到旧值），只校验结构。
    /// </param>
    public static JObject? TryRewrite(ChangePatch patch, string? gsq, DialogueQueryOverlap? overlap,
        IReadOnlySet<string>? npcNames, Func<string, string?>? priorLookup = null) =>
        TryRewrite(patch, gsq, overlap, npcNames, priorLookup, baseLookup: null, out _);

    /// <summary>
    /// 同上，另给出**拒绝的具体缘由**（<paramref name="refusal"/>）。
    /// <para>
    /// <b>为什么要它</b>：判定阶段拒绝后会落到「对话键只支持季节/地点/好感/topic 等受限语法」——
    /// 那句话把**结构性原因**（该键上没有无条件补丁、取不到假分支的旧值…）说成了**键语法**，
    /// 读者会照着错的方向去改。措辞必须与实际拦住它的那一条对应。
    /// </para>
    /// <para>拒绝缘由只在**同一个实现**里产生，不另写一份判据（否则两处会漂移）。</para>
    /// </summary>
    public static JObject? TryRewrite(ChangePatch patch, string? gsq, DialogueQueryOverlap? overlap,
        IReadOnlySet<string>? npcNames, Func<string, string?>? priorLookup, out string? refusal) =>
        TryRewrite(patch, gsq, overlap, npcNames, priorLookup, baseLookup: null, out refusal);

    /// <summary>
    /// 同上。多一个 <paramref name="baseLookup"/>：<b>基础资产</b>里该键的原有值。
    /// <para>
    /// <b>为什么需要它</b>：判定阶段原先要求「同一键上至少一条**无条件补丁**」提供旧值。
    /// 但写入阶段的 `PriorString(target, key)` 读的是「合并前的目标数据」——**也包括基础资产本身**。
    /// ⇒ 判定比写入**严了一档**：本来写得出去的补丁被判成了人工确认。
    /// 实测语料里这类补丁的 73 个条目键中，**46 个原版资产里本来就有**（如
    /// <c>Characters/Dialogue/Shane</c> 的 <c>FlowerDance_Accept</c>）。
    /// </para>
    /// <para>
    /// ⚠ 判定拿得到基础值时就**顺带把合成验一遍**（用的是写入器同一个 <see cref="Compose"/>），
    /// 所以「判定说能写、写入说不能」不会再从这里冒出来。
    /// </para>
    /// </summary>
    public static JObject? TryRewrite(ChangePatch patch, string? gsq, DialogueQueryOverlap? overlap,
        IReadOnlySet<string>? npcNames, Func<string, string?>? priorLookup,
        Func<string, string?>? baseLookup, out string? refusal)
    {
        refusal = null;

        if (string.IsNullOrEmpty(gsq)) return Refuse(out refusal, "没有可用的 GSQ");
        // 会撑破 `Split('#', 2)` / `Split('|')`
        if (gsq.Contains('#') || gsq.Contains('|'))
            return Refuse(out refusal, "GSQ 串里含 `#` 或 `|`，会撑破 `$query` 的分支切分");

        if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase))
            return Refuse(out refusal, $"`$query` 只适用于 EditData（本条是 {patch.Action}）");

        string target = (patch.Target ?? "").Replace('\\', '/').Trim('/');
        if (target.Length == 0 || target.Contains(','))
            return Refuse(out refusal, "`$query` 只支持单一 Target（逗号多值表达不了）");

        DialogueQueryHost host = ConditionLanding.ClassifyDialogueQueryHost(target);
        if (host == DialogueQueryHost.None)
            return Refuse(out refusal, $"`{target}` 不是已确证的 `$query` 宿主（值最终不走 `new Dialogue(...)`）");

        // 落点被 TargetField/Fields 改变时不再适用（$query 只认顶层键的字符串值）。
        if (patch.Raw["TargetField"] != null || patch.Raw["Fields"] != null)
            return Refuse(out refusal, "本条用 TargetField/Fields 改结构，不是顶层键的字符串值");

        if (patch.Raw["Entries"] is not JObject entries || entries.Count == 0)
            return Refuse(out refusal, "本条没有 Entries（没有可内联的对话键值）");

        var rewritten = (JObject)patch.Raw.DeepClone();
        var newEntries = (JObject)rewritten["Entries"]!;

        foreach (JProperty entry in newEntries.Properties().ToList())
        {
            if (!KeyIsEligible(host, entry.Name, npcNames))
                return Refuse(out refusal, $"条目键 `{entry.Name}` 在该宿主里不会被当作对话读");
            if (entry.Value.Type != JTokenType.String)
                return Refuse(out refusal, $"条目键 `{entry.Name}` 的值不是字符串");

            string newValue = entry.Value.Value<string>() ?? "";
            if (newValue.Length == 0)
                return Refuse(out refusal, $"条目键 `{entry.Name}` 的新值为空");

            if (overlap != null)
            {
                (int conditional, int unconditional) = overlap.Count(target, entry.Name);
                // 只有这一条条件补丁：再多一条就会互相嵌套（原版解析不了三岔）。
                if (conditional != 1)
                    return Refuse(out refusal,
                        $"同一键 `{entry.Name}` 上有 {conditional} 条**带条件**的补丁 —— "
                        + "两条都转 `$query` 会互相嵌套（游戏解析嵌套 `$query` 时按原始串重切，会取错分支）");
                // 旧值的两个来源：① 同键上的无条件补丁；② **基础资产本身**。
                // 后者原先没算，导致判定比写入严一档（见方法注释）。
                if (unconditional < 1)
                {
                    string? baseValue = baseLookup?.Invoke(entry.Name);
                    if (string.IsNullOrEmpty(baseValue))
                        return Refuse(out refusal,
                            $"同一键 `{entry.Name}` 上**没有无条件补丁**、**基础资产里也没有这个键** —— "
                            + "`$query` 的假分支必须是个真值（空假分支会让「条件不成立时不显示」变成「显示空对话」）");

                    // 判定阶段就把合成验一遍：与写入器同一个 Compose，结论必然一致。
                    if (ComposeDetailed(gsq, newValue, baseValue, out string? why) == null)
                        return Refuse(out refusal,
                            $"同一键 `{entry.Name}` 在基础资产里的旧值不可用：{why}");
                }
            }

            if (priorLookup == null) continue;   // 判定阶段：拿不到旧值，只校验结构

            string? composed = ComposeDetailed(gsq, newValue, priorLookup(entry.Name), out string? reason);
            if (composed == null)
                return Refuse(out refusal, $"条目键 `{entry.Name}` 无法内联成 `$query`：{reason}");
            entry.Value = composed;
        }

        return rewritten;
    }

    private static JObject? Refuse(out string? refusal, string reason)
    {
        refusal = reason;
        return null;
    }

    /// <summary>
    /// 合成 <c>$query &lt;GSQ&gt;#&lt;新值&gt;|&lt;旧值&gt;</c>；拿不到有效旧值时返回 <c>null</c>。
    /// <para>
    /// 旧值必须存在、非空、与旧值不同、且本身不是 <c>$query</c> —— 最后一条防止嵌套：
    /// 游戏解析嵌套 <c>$query</c> 时用原始 <c>masterString</c> 重新切分，会取到错误分支。
    /// </para>
    /// </summary>
    /// <para>
    /// ⚠ <b>为什么「取不到旧值」必须整条拒绝，而不是把假分支留空</b>：
    /// 看上去 <c>$query Q#新|</c> 正好表达「条件不成立时就当没这条」—— <b>并不是</b>。
    /// <c>NPC.tryToRetrieveDialogue</c> 的候选键链只判 <c>!= null</c>（<c>NPC.cs:4024-4026</c>、
    /// <c>:4032-4034</c>、<c>:4042-4044</c>、<c>:4048-4050</c>、<c>:4053-4055</c>、<c>:4062-4068</c>），
    /// <b>不判空</b>；而 <c>Dialogue.TryGetDialogue</c>（<c>Dialogue.cs:447-455</c>）只在**键找不到**时
    /// 返回 <c>null</c>，值是空串照样构造出一个 <c>Dialogue</c>。
    /// ⇒ 空假分支 = <b>命中并返回一句空话</b>，而不是继续往下试候选键。
    /// 「条件不成立时不显示」与「显示空对话」是两种语义。
    /// </para>
    /// </summary>
    public static string? Compose(string gsq, string newValue, string? prior) =>
        ComposeDetailed(gsq, newValue, prior, out _);

    /// <summary>
    /// 同上，另给出<b>具体</b>拒绝缘由。
    /// <para>
    /// <b>为什么要它</b>：<see cref="Compose"/> 有 6 个返回 <c>null</c> 的分支，而调用方原先只报
    /// 一句「取不到可用的原有值（缺失/空/与新值相同/本身已是 $query）」—— 那 4 项**漏了**
    /// 「GSQ 串含 # / |」与「新值含 |」。实测 SVE / 礼物对话那几条失败的真正原因是**新值含 `|`**
    /// （mod 自己写的原生 `$query` 值），照着那句文案去查旧值只会查错方向。
    /// </para>
    /// </summary>
    private static string? ComposeDetailed(string gsq, string newValue, string? prior, out string? reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(gsq)) { reason = "没有可用的 GSQ"; return null; }
        if (gsq.Contains('#') || gsq.Contains('|'))
        { reason = "GSQ 串里含 `#` 或 `|`，会撑破 `$query` 的分支切分"; return null; }
        if (string.IsNullOrEmpty(newValue)) { reason = "新值为空"; return null; }
        // 取不到旧值 → 绝不产出 `$query Q#新|`
        if (string.IsNullOrEmpty(prior)) { reason = "取不到旧值（合并前的目标里没有这个键）"; return null; }
        // `$query Q#新|新` 是错的
        if (string.Equals(prior, newValue, StringComparison.Ordinal))
        { reason = "旧值与新值相同（`$query Q#新|新` 的假分支等于真分支）"; return null; }
        // 已经是 $query → 会嵌套（游戏按原始串重切，取错分支）
        if (prior.StartsWith(Prefix, StringComparison.Ordinal))
        { reason = "旧值本身已是 `$query` —— 嵌套 `$query` 会被游戏取错分支"; return null; }
        // `Split('|')` 会被撑破
        if (newValue.Contains('|') || prior.Contains('|'))
        { reason = "新值或旧值含 `|`，会撑破 `$query` 的分支切分"; return null; }

        return Prefix + gsq + "#" + newValue + "|" + prior;
    }

    /// <summary>
    /// 该键是不是宿主里"会被当作对话读"的键。
    /// <c>Data/Festivals</c> 只认 NPC 名（其余键是 <c>mainEvent</c>/<c>set-up</c> 那类事件脚本）；
    /// <c>StringsFromCSFiles</c> 只认节日主持问答那一个键。
    /// </summary>
    private static bool KeyIsEligible(DialogueQueryHost host, string key, IReadOnlySet<string>? npcNames)
    {
        if (key.Contains('/') || key.Contains('|')) return false;

        if (host == DialogueQueryHost.Festivals) return npcNames != null && npcNames.Contains(key);
        if (host == DialogueQueryHost.StringsFromCSFiles)
            return key.Equals(FestivalHostKey, StringComparison.OrdinalIgnoreCase);
        return true;
    }
}
