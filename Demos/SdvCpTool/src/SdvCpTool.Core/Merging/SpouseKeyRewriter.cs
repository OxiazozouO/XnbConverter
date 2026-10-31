using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 「节日配偶对话」补丁的原生改写。
/// <para>
/// 原版 <c>Event.cs</c>（1.6.15:12610-12633）在玩家与该 NPC 结婚时自动去读
/// <c>&lt;NPC&gt;_spouse</c> 键，<c>TryGetFestivalDialogueForYear</c> 还会按年份取 <c>_yN</c> 变体。
/// 因此把
/// <c>{ "Target": "Data/Festivals/&lt;id&gt;", "Entries": { "&lt;NPC&gt;": 对话 }, "When": { "Relationship:&lt;NPC&gt; |contains= married": true } }</c>
/// 改写为 <c>Entries: { "&lt;NPC&gt;_spouse": 对话 }</c> 并删掉 <c>When</c>，是零损失的等价转换
/// （原版 8 个节日全都依赖这套键；RSV 自建的 fall20/winter28 走同一套读取逻辑）。
/// <para>
/// 条件有<b>三种等价拼写</b>（NPC 的位置不同），见 <see cref="TryReadSpouse"/>：
/// <c>Relationship:&lt;NPC&gt; |contains= married</c>（NPC 在键侧）、
/// <c>"Spouse": "&lt;NPC&gt;"</c>、<c>"Spouse |contains=&lt;NPC&gt;": true</c>（后两者 NPC 在值侧）。
/// 只认前一种会漏掉语料里大量真实补丁。
/// </para>
/// </para>
/// <para>
/// 判定与写入<b>必须</b>调用同一个 <see cref="TryRewrite"/> —— 两处各解释一遍同一条数据
/// 就会重演 Task 10b 的教训。前提严格，任一不符即返回 <c>null</c>（保持原判定）：
/// 尤其「条件里的 NPC 与条目键不一致」时绝不能改写，那会静默改错别人的对话。
/// </para>
/// </summary>
public static class SpouseKeyRewriter
{
    private const string FestivalPrefix = "Data/Festivals/";

    /// <summary>年份后缀 <c>_y2</c>、<c>_y3</c> …（原版节日对话按年份取变体的写法）。</summary>
    private static readonly Regex YearSuffix = new(@"^_y\d+$", RegexOptions.Compiled);

    /// <summary>
    /// 识别「节日配偶对话」形态并返回<b>改写后</b>的补丁；不满足前提时返回 <c>null</c>。
    /// 纯函数：不改动入参，也不读资源树。
    /// </summary>
    public static JObject? TryRewrite(ChangePatch patch)
    {
        // 前提 1：Action == EditData
        if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase))
            return null;

        // 前提 2：Target 恰好一个、形如 Data/Festivals/<id>
        string target = (patch.Target ?? "").Replace('\\', '/').Trim();
        if (target.Contains(',')) return null;
        if (!target.StartsWith(FestivalPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        string festivalId = target[FestivalPrefix.Length..];
        if (festivalId.Length == 0 || festivalId.Contains('/') || festivalId.Contains("{{")) return null;

        // 落点被 TargetField/Fields 改变时 _spouse 约定不再适用（它只认节日资产的顶层键）。
        // 比计划的四条前提更严一点 —— 只会拒绝，不会误转。
        if (patch.Raw["TargetField"] != null || patch.Raw["Fields"] != null) return null;

        // 前提 3：When 恰好一个叶子
        JObject? whenObj = patch.Raw["When"] as JObject ?? patch.When;
        if (whenObj == null || whenObj.Count != 1) return null;
        JProperty whenProp = whenObj.Properties().First();

        // 用共享的 WhenParser 解析「是不是恰好一个 Key 叶子」，与判定阶段同一套语法。
        WhenNode? when = WhenParser.Parse(whenObj);
        if (when == null || when.Kind != WhenNodeKind.All || when.Children.Count != 1) return null;
        WhenNode leaf = when.Children[0];
        if (leaf.Kind != WhenNodeKind.Key) return null;

        // 读出「配偶/室友是谁」及该用哪个后缀键。
        if (!TryReadSpouse(whenProp, leaf, out string npc, out string spouseSuffix)) return null;

        // 前提 4：Entries 恰好一个条目，且其键就是条件里的 NPC（允许 _yN 年份后缀）
        if (patch.Raw["Entries"] is not JObject entries || entries.Count != 1) return null;
        JProperty entry = entries.Properties().First();
        string entryKey = entry.Name;

        string yearSuffix;
        if (string.Equals(entryKey, npc, StringComparison.Ordinal))
            yearSuffix = "";
        else if (entryKey.Length > npc.Length
                 && entryKey.StartsWith(npc, StringComparison.Ordinal)
                 && YearSuffix.IsMatch(entryKey[npc.Length..]))
            yearSuffix = entryKey[npc.Length..];
        else
            return null;   // 键与 NPC 不是同一个人 → 绝不改写

        var rewritten = (JObject)patch.Raw.DeepClone();
        rewritten["Entries"] = new JObject { [npc + spouseSuffix + yearSuffix] = entry.Value.DeepClone() };
        rewritten.Remove("When");
        return rewritten;
    }

    /// <summary>
    /// 从条件叶子读出「配偶/室友是谁」。CP 有<b>三种等价拼写</b>，NPC 的位置不同：
    /// <list type="bullet">
    /// <item><c>{"Relationship:&lt;NPC&gt; |contains= married": true}</c> —— NPC 在<b>键侧</b></item>
    /// <item><c>{"Spouse": "&lt;NPC&gt;"}</c> —— NPC 在<b>值侧</b></item>
    /// <item><c>{"Spouse |contains=&lt;NPC&gt;": true}</c> —— NPC 在<b>值侧</b></item>
    /// </list>
    /// <para>
    /// 依据：CP 的值令牌表（<c>TokenManager.cs:200-201</c>）里 <c>Spouse</c>/<c>Roommate</c>
    /// <b>返回的是玩家配偶的名字</b>，所以名字在值侧；而 <c>Relationship</c> 是另一种令牌，
    /// 名字在键侧。两者不可混。
    /// </para>
    /// </summary>
    private static bool TryReadSpouse(JProperty whenProp, WhenNode leaf, out string npc, out string spouseSuffix)
    {
        npc = "";
        spouseSuffix = "_spouse";
        string name = leaf.KeyName?.Trim() ?? "";
        string? args = leaf.Arguments;

        // ── `Relationship:<NPC> |contains= married`：NPC 在键侧 ──
        // 复用 TokenTranslator 的拆分：**竖线后的空格可有可无**（真实语料两种都出现）。
        if (name.Equals("Relationship", StringComparison.OrdinalIgnoreCase))
        {
            if (whenProp.Value.Type != JTokenType.Boolean || !whenProp.Value.Value<bool>()) return false;
            if (!TokenTranslator.TrySplitContains(args, out string npcHead, out string wanted)) return false;
            if (!wanted.Equals("married", StringComparison.OrdinalIgnoreCase)) return false;

            npc = npcHead;
            return IsPlainNpc(npc);
        }

        // ── `Spouse` / `Roommate`：**值令牌**，NPC 在值侧 ──
        if (name.Equals("Spouse", StringComparison.OrdinalIgnoreCase)) spouseSuffix = "_spouse";
        else if (name.Equals("Roommate", StringComparison.OrdinalIgnoreCase)) spouseSuffix = "_roommate";
        else return false;

        // 键槽必须为空：有键槽说明是别的形态，不猜。
        bool hasContains = TokenTranslator.TrySplitContains(args, out string head, out string containsTail);
        if (head.Length != 0) return false;

        if (hasContains)
        {
            // `Spouse |contains=<NPC>`：NPC 来自 `|contains=`，JSON 值只是取反开关
            if (whenProp.Value.Type != JTokenType.Boolean || !whenProp.Value.Value<bool>()) return false;
            TokenTranslator.TrySplitContains(args, out _, out string tail);
            npc = tail;
        }
        else
        {
            // `Spouse: "<NPC>"`：NPC 就是值本身
            if (args != null) return false;
            if (whenProp.Value.Type != JTokenType.String) return false;
            npc = whenProp.Value.Value<string>()?.Trim() ?? "";
        }

        return IsPlainNpc(npc);
    }

    /// <summary>
    /// 单个字面的 NPC 名。多值（含逗号）不是本形态 —— 它要拆成多条条目，
    /// 而前提 4 要求 <c>Entries</c> 恰好一条，所以在这里先挡掉，避免误改。
    /// </summary>
    private static bool IsPlainNpc(string npc) =>
        npc.Length > 0
        && !npc.Contains("{{") && !npc.Contains('|') && !npc.Contains(':') && !npc.Contains(',');
}
