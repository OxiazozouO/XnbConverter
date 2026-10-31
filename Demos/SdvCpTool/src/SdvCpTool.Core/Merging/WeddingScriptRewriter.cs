using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 「婚礼脚本」补丁的原生改写。
/// <para>
/// 原版 <c>Utility.cs</c>（1.6.15:2275）按<b>配偶名</b>取婚礼脚本：
/// <c>weddingData.EventScript.TryGetValue(spouse?.ToString() ?? farmer.spouse, out value)</c>，
/// 取不到再退回 <c>"default"</c>；模型见 <c>StardewValley.GameData.Weddings.WeddingData.EventScript</c>。
/// 而 <c>EventScript["default"]</c> 里硬引用
/// <c>[LocalizedText Strings\StringsFromCSFiles:Utility.cs.5367 [DayOfMonth] [Season]]</c> 等一串原版字符串，
/// 且代词只按<b>玩家</b>性别选（<c>[GenderedText ...]</c>）。
/// </para>
/// <para>
/// 于是「给某个 NPC 换一套婚礼台词」在 <c>strings/StringsFromCSFiles</c> 上只能靠 <c>When</c> 条件覆写那几个
/// <c>Utility.cs.53xx</c> 键 —— 而字符串资产没有条件位。原生等价写法是：把 <c>default</c> 脚本复制一份、
/// 把被覆写的那几处字符串引用就地换成补丁给的值，然后写成 <c>EventScript["&lt;NPC&gt;"]</c>。
/// 键即配偶名，<b>无需任何条件</b>：只有玩家与该 NPC 结婚时游戏才会去取这个键。
/// </para>
/// <para>
/// 判定与写入<b>必须</b>调用同一个 <see cref="TryRewrite"/>（与 Task 10b 的教训一致：两处各解释一遍同一条数据
/// 必然走散）。前提严格，任一不符即返回 <c>null</c>（保持原判定，宁可少写：绝不写出错位的婚礼台词）。
/// </para>
/// <para>
/// <b>边界：带位置参数的标签跳过替换。</b>母本里 5367/5369 长这样
/// <c>[LocalizedText Strings\StringsFromCSFiles:5367 [DayOfMonth] [Season]]</c>，靠这层包裹把文案里的
/// <c>{0}</c>/<c>{1}</c> 填成日期与季节。若补丁给的值是 <c>{{i18n:…}}</c> 令牌，<c>{0}</c>/<c>{1}</c> 要等
/// <b>写入阶段</b> i18n 展开后才出现，改写阶段根本看不到、代换不了 —— 整段换掉会把字面的
/// <c>{0} of {1}</c> 漏给玩家。因此<b>带参数、且值含 <c>{{</c> 的条目一律跳过、保留原标签</b>：
/// 那两个键本身就用 <c>%spouse</c> 引用配偶名，对同一场婚礼原版文案已会解析成
/// <c>…unite @ and Alissa…</c>，与补丁文案差别只在措辞；而这套改写的核心价值在 5371/5377
/// （<c>husband and husband</c>/<c>wife and wife</c> 的性别 bug），它们没有位置参数、照常替换。
/// 一个条目都没能替换时整条不转（<c>null</c>），与「宁可少转」一致。
/// </para>
/// </summary>
public static class WeddingScriptRewriter
{
    private const string StringsFromCSFilesAsset = "Strings/StringsFromCSFiles";
    private const string WeddingsAsset = "Data/Weddings";
    private const string LocalizedTextTag = "[LocalizedText";
    private const string StringsFromCSFilesRef = "Strings";

    /// <summary>被覆写的字符串键：原版婚礼事件硬引用的那一族（<c>Utility.cs.5367/5369/5371/5377</c>…）。</summary>
    private static readonly Regex UtilityKey = new(@"^Utility\.cs\.53\d\d$", RegexOptions.Compiled);

    /// <summary>
    /// 从资源树取 <c>Data/Weddings</c> 的 <c>EventScript["default"]</c> —— 改写要复制的母本。
    /// 判定与写入调用的是同一个实现（各自拿同一棵树来取），绝不两处各写一份读取逻辑。
    /// 取不到（资产缺失 / 不是 json / 没有 default）返回 <c>null</c>，改写作废。
    /// </summary>
    public static string? ResolveDefaultEventScript(ResourceTree tree)
    {
        string? path = tree.Resolve(WeddingsAsset);
        if (path == null) return null;
        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            var root = JToken.Parse(File.ReadAllText(path)) as JObject;
            return root?["EventScript"]?["default"]?.Value<string>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 识别「婚礼字符串覆写」形态并返回<b>改写后</b>的补丁；不满足前提时返回 <c>null</c>。
    /// 纯函数：不改动入参。母本 <paramref name="defaultEventScript"/> 由调用方从资源树取
    /// （见 <see cref="ResolveDefaultEventScript"/>）。
    /// </summary>
    public static JObject? TryRewrite(ChangePatch patch, string? defaultEventScript)
    {
        // 前提 1：Action == EditData
        if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase))
            return null;

        // 前提 2：Target 恰好一个、就是 strings/StringsFromCSFiles（大小写不敏感：真实数据写的是小写 s）
        string target = (patch.Target ?? "").Replace('\\', '/').Trim();
        if (target.Contains(',')) return null;
        if (!target.Equals(StringsFromCSFilesAsset, StringComparison.OrdinalIgnoreCase)) return null;

        // 落点被 TargetField/Fields 改变时就不是「覆写整份字符串字典里的键」了 —— 不猜。
        if (patch.Raw["TargetField"] != null || patch.Raw["Fields"] != null) return null;

        // 前提 3：Entries 全部是 Utility.cs.53xx 且值为字符串
        if (patch.Raw["Entries"] is not JObject entries || entries.Count == 0) return null;
        foreach (JProperty entry in entries.Properties())
        {
            if (!UtilityKey.IsMatch(entry.Name)) return null;
            if (entry.Value.Type != JTokenType.String) return null;
        }

        // 前提 4：When 恰好一个叶子 `Relationship:<NPC>`，值是 Engaged 或 Married
        JObject? whenObj = patch.Raw["When"] as JObject ?? patch.When;
        if (whenObj == null || whenObj.Count != 1) return null;

        WhenNode? when = WhenParser.Parse(whenObj);
        if (when == null || when.Kind != WhenNodeKind.All || when.Children.Count != 1) return null;
        WhenNode leaf = when.Children[0];
        if (leaf.Kind != WhenNodeKind.Key) return null;
        if (!string.Equals(leaf.KeyName?.Trim(), "Relationship", StringComparison.OrdinalIgnoreCase)) return null;

        string wanted = (leaf.Value ?? "").Trim();
        if (!wanted.Equals("Engaged", StringComparison.OrdinalIgnoreCase)
            && !wanted.Equals("Married", StringComparison.OrdinalIgnoreCase))
            return null;

        string npc = (leaf.Arguments ?? "").Trim();
        if (npc.Length == 0 || npc.Contains("{{") || npc.Contains('/') || npc.Contains(':') || npc.Contains('|'))
            return null;

        if (string.IsNullOrEmpty(defaultEventScript)) return null;

        // 前提 5：母本里必须真的引用了被覆写的每一个键 —— 少一个就说明我们对不上号，
        // 宁可整条不转，也不写出一份"少了半句台词"的婚礼脚本。
        string script = defaultEventScript;
        int replacedCount = 0;
        foreach (JProperty entry in entries.Properties())
        {
            (bool found, bool replaced, string updated) =
                ReplaceLocalizedTextTag(script, entry.Name, entry.Value.Value<string>()!);
            if (!found) return null;

            script = updated;
            if (replaced) replacedCount++;
        }

        // 一个条目都没能替换（全因带位置参数被跳过）→ 产出的脚本与母本一模一样，写了也白写，整条不转。
        if (replacedCount == 0) return null;

        var rewritten = (JObject)patch.Raw.DeepClone();
        rewritten.Remove("When");
        rewritten["Target"] = WeddingsAsset;
        // 必须写进 EventScript 这一层：CP 的 Entries 是**赋值**语义（顶层整个键被替换），
        // 直接写 `Entries: { EventScript: {...} }` 会把母本 `default` 一起冲掉，
        // 别的 NPC 结婚时游戏就会抛 "no wedding script with the 'default' script key"。
        rewritten["TargetField"] = new JArray("EventScript");
        rewritten["Entries"] = new JObject { [npc] = script };
        return rewritten;
    }

    /// <summary>
    /// 把脚本里引用 <paramref name="key"/> 的那个 <c>[LocalizedText Strings\StringsFromCSFiles:&lt;key&gt; …]</c>
    /// 整段换成 <paramref name="value"/>。
    /// <para>
    /// 返回 <c>(found, replaced, script)</c>：<c>found=false</c> 表示母本里没有这个键的引用（调用方据此整条不转）；
    /// <c>found=true, replaced=false</c> 表示标签带位置参数、值又含 <c>{{</c>（改写阶段代换不了 <c>{i}</c>），
    /// 于是<b>保留原标签</b>、脚本原样返回。详见类注释的边界说明。
    /// </para>
    /// </summary>
    private static (bool Found, bool Replaced, string Script) ReplaceLocalizedTextTag(
        string script, string key, string value)
    {
        int from = 0;
        while (true)
        {
            int at = script.IndexOf(LocalizedTextTag, from, StringComparison.Ordinal);
            if (at < 0) return (false, false, script);

            int end = FindTagEnd(script, at);
            if (end < 0) return (false, false, script);

            string content = script[(at + 1)..end];
            if (IsReferenceTo(content, key))
            {
                string[] args = TagArgs(content);
                // 带位置参数、值又是 {{i18n:…}} 令牌 → {0}/{1} 在写入阶段才展开，这里代换不了，
                // 整段换掉会把字面占位符漏给玩家。跳过该条目，保留原标签。
                if (args.Length > 0 && value.Contains("{{", StringComparison.Ordinal))
                    return (true, false, script);

                return (true, true, script[..at] + ApplyArgs(value, args) + script[(end + 1)..]);
            }

            from = end + 1;
        }
    }

    /// <summary>标签内容是不是 <c>LocalizedText</c> 对 <c>Strings/StringsFromCSFiles:&lt;key&gt;</c> 的引用。</summary>
    private static bool IsReferenceTo(string content, string key)
    {
        string[] parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (!parts[0].Equals("LocalizedText", StringComparison.OrdinalIgnoreCase)) return false;

        string reference = parts[1].Replace('\\', '/');
        string prefix = StringsFromCSFilesRef + "/StringsFromCSFiles:";
        if (!reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return reference[prefix.Length..].Equals(key, StringComparison.Ordinal);
    }

    /// <summary>标签里的位置参数（<c>[DayOfMonth]</c>、<c>[Season]</c>…），即 <c>{0}</c>/<c>{1}</c> 的值。</summary>
    private static string[] TagArgs(string content)
    {
        string[] parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 2 ? parts[2..] : Array.Empty<string>();
    }

    /// <summary>
    /// 把补丁给的值里的位置占位符 <c>{i}</c> 换成原标签的第 i 个参数 —— 原版就是靠
    /// <c>[LocalizedText &lt;key&gt; [DayOfMonth] [Season]]</c> 把 <c>{0}</c>/<c>{1}</c> 填成日期与季节
    /// （<c>TokenParser.DefaultResolvers.LocalizedText</c> 把参数当 <c>string.Format</c> 的实参）。
    /// <para>
    /// 只在值里<b>不含 <c>{{</c></b>（纯文本，<c>{0}</c>/<c>{1}</c> 改写阶段就看得见）时才会走到这里；
    /// 值含 <c>{{i18n:…}}</c> 的带参数标签已在 <see cref="ReplaceLocalizedTextTag"/> 里整条跳过。
    /// </para>
    /// </summary>
    private static string ApplyArgs(string value, string[] args)
    {
        for (int i = 0; i < args.Length; i++)
            value = value.Replace("{" + i + "}", args[i], StringComparison.Ordinal);
        return value;
    }

    /// <summary>从 <paramref name="open"/> 处的 <c>[</c> 找配对的 <c>]</c>（形参里的嵌套标签要一起跳过）。</summary>
    private static int FindTagEnd(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '[') depth++;
            else if (text[i] == ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }
}
