using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Normalizing;

/// <summary>
/// 从一个 <c>When</c> 里读出它写的是**哪一季**。
/// <para>
/// <b>为什么必须共用一处</b>：四季 BigCraftable 与四季图集两套规划器都要读它，
/// 各写一份迟早漂移 —— 而这个库已经因为「两处各解释一遍同一条数据」吃过亏。
/// </para>
/// </summary>
public static class SeasonToken
{
    private static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };

    /// <summary>
    /// 恰好一个季节值就返回它的规范写法（<c>Spring</c>…）；否则返回 <c>null</c> 并给出原因。
    /// <para>
    /// <b>两种写法都认</b>：
    /// <list type="bullet">
    /// <item><c>{"Season": "Spring"}</c> —— 直接写。</item>
    /// <item><c>{"Season |contains=Spring": true}</c> —— <b>与上面严格等价</b>：
    ///   <c>Season</c> 令牌的值集**只有单元素**（当前季节），所以「与 {Spring} 有交集」
    ///   就是「当前季节 = Spring」。（<c>|contains=X</c> 且声明值为 <c>false</c> 是**补集**
    ///   ——「不是 X」——那**拆不出是哪一季**，仍然拒绝。）</item>
    /// </list>
    /// </para>
    /// <para>
    /// 实测依据：SVE 的 <c>Maps/Spring_z_extras</c> 四季变体就是
    /// <c>{"Season |contains=Spring": true}</c> 这种写法（全语料 <c>EditImage</c> 上共 5 处）。
    /// </para>
    /// </summary>
    public static string? Of(JObject when, out string? reason)
    {
        reason = null;
        string? found = null;

        foreach (JProperty p in when.Properties())
        {
            if (!Base(p.Name).Equals("Season", StringComparison.OrdinalIgnoreCase)) continue;

            string? value = ReadValue(p, out bool complement, out string? why);
            if (value == null) { reason = why; return null; }
            if (complement)
            {
                reason = $"`{p.Name}` 是**补集**（「不是这一季」），拆不出是哪一季";
                return null;
            }

            if (found != null) { reason = "一条补丁里出现了多个 Season"; return null; }
            found = value;
        }

        if (found == null) { reason = "这组里有一条补丁没给 Season"; return null; }
        return found;
    }

    /// <summary>
    /// 读一条 <c>Season</c> 条件可能覆盖的**全部季节**：<c>Season: "Spring, Summer"</c>、
    /// <c>Season |contains=Summer, Winter: true</c>、以及取反后的补集。
    /// 一条补丁覆盖多季时，四季发射器应把它复制到每一季，而不是整组拒绝。
    /// </summary>
    public static IReadOnlyList<string>? OfAll(JObject when, out string? reason)
    {
        reason = null;
        List<string>? found = null;
        foreach (JProperty p in when.Properties())
        {
            if (!Base(p.Name).Equals("Season", StringComparison.OrdinalIgnoreCase)) continue;
            List<string>? values = ReadValues(p, out bool complement, out string? why);
            if (values == null) { reason = why; return null; }
            if (complement)
                values = Seasons.Where(s => !values.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            if (found != null) { reason = "一条补丁里出现了多个 Season"; return null; }
            found = values;
        }
        if (found == null) { reason = "这组里有一条补丁没给 Season"; return null; }
        if (found.Count == 0) { reason = "Season 条件覆盖 0 个季节"; return null; }
        return found;
    }

    private static List<string>? ReadValues(JProperty p, out bool complement, out string? why)
    {
        complement = false;
        why = null;
        int bar = p.Name.IndexOf('|');
        if (bar < 0)
        {
            if (p.Value.Type == JTokenType.Boolean) { why = "Season 的值是布尔，不是季节名"; return null; }
            string? s = p.Value.Value<string>();
            if (string.IsNullOrWhiteSpace(s)) { why = "Season 的值为空"; return null; }
            var list = new List<string>();
            foreach (string part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Seasons.Contains(part, StringComparer.OrdinalIgnoreCase))
                { why = $"Season 值 `{part}` 不是四季之一"; return null; }
                list.Add(Canonical(part));
            }
            return list.Count == 0 ? null : list;
        }

        string args = p.Name[(bar + 1)..].Trim();
        if (!args.StartsWith("contains=", StringComparison.OrdinalIgnoreCase))
        { why = $"`Season` 带了不认识的参数 `{args}`"; return null; }
        if (p.Value.Type != JTokenType.Boolean)
        { why = "`|contains=` 的声明值必须是 true/false"; return null; }
        complement = !p.Value.Value<bool>();

        var wanted = new List<string>();
        foreach (string part in args["contains=".Length..].Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Seasons.Contains(part, StringComparer.OrdinalIgnoreCase))
            { why = $"`Season |contains={part}` 里的 `{part}` 不是四季之一"; return null; }
            wanted.Add(Canonical(part));
        }
        return wanted.Count == 0 ? null : wanted;
    }

    /// <summary>读一条 <c>Season</c> 条件的值；<paramref name="complement"/> = 它是「不是这一季」。</summary>
    private static string? ReadValue(JProperty p, out bool complement, out string? why)
    {
        complement = false;
        why = null;

        int bar = p.Name.IndexOf('|');
        if (bar < 0)
        {
            if (p.Value.Type == JTokenType.Boolean) { why = "Season 的值是布尔，不是季节名"; return null; }
            string? s = p.Value.Value<string>();
            if (string.IsNullOrEmpty(s)) { why = "Season 的值为空"; return null; }
            if (!Seasons.Contains(s, StringComparer.OrdinalIgnoreCase)) { why = $"Season 值 `{s}` 不是四季之一"; return null; }
            return Canonical(s);
        }

        // `|contains=` 形态：`Season` 只有单值，所以「包含 X」⇔「等于 X」。
        string args = p.Name[(bar + 1)..].Trim();
        if (!args.StartsWith("contains=", StringComparison.OrdinalIgnoreCase))
        {
            why = $"`Season` 带了不认识的参数 `{args}`";
            return null;
        }
        string wanted = args["contains=".Length..].Trim();
        if (!Seasons.Contains(wanted, StringComparer.OrdinalIgnoreCase))
        {
            why = $"`Season |contains={wanted}` 里的 `{wanted}` 不是四季之一";
            return null;
        }
        if (p.Value.Type != JTokenType.Boolean) { why = "`|contains=` 的声明值必须是 true/false"; return null; }

        complement = !p.Value.Value<bool>();
        return Canonical(wanted);
    }

    private static string Canonical(string s) =>
        Seasons.First(x => x.Equals(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>令牌名的基名：冒号前、竖线前。</summary>
    private static string Base(string key)
    {
        int colon = key.IndexOf(':');
        if (colon >= 0) key = key[..colon];
        int pipe = key.IndexOf('|');
        if (pipe >= 0) key = key[..pipe];
        return key.Trim();
    }
}
