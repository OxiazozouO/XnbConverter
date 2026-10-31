using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp.Lexing;

namespace SdvCpTool.Core.Conditions;

/// <summary>把 CP 的 `When` 对象解析成条件树。</summary>
public static class WhenParser
{
    public static WhenNode? Parse(JObject? when)
    {
        if (when == null || !when.Properties().Any()) return null;
        return ParseObject(when);
    }

    private static WhenNode ParseObject(JObject obj)
    {
        var children = new List<WhenNode>();
        foreach (JProperty prop in obj.Properties())
        {
            children.Add(ParseEntry(prop.Name, prop.Value));
        }
        return WhenNode.All(children.ToArray());
    }

    private static WhenNode ParseEntry(string name, JToken value)
    {
        if (string.Equals(name, "AND", StringComparison.OrdinalIgnoreCase))
            return new WhenNode { Kind = WhenNodeKind.All, Children = ParseList(value) };

        if (string.Equals(name, "OR", StringComparison.OrdinalIgnoreCase))
            return new WhenNode { Kind = WhenNodeKind.Any, Children = ParseList(value) };

        if (string.Equals(name, "NOT", StringComparison.OrdinalIgnoreCase))
            return new WhenNode { Kind = WhenNodeKind.Not, Children = { ParseSingle(value) } };

        return SplitKey(name, value);
    }

    private static List<WhenNode> ParseList(JToken value)
    {
        var list = new List<WhenNode>();
        if (value is JArray arr)
        {
            foreach (JToken item in arr) list.Add(ParseSingle(item));
        }
        else
        {
            list.Add(ParseSingle(value));
        }
        return list;
    }

    /// <summary>
    /// 解析嵌套位置的一个节点。单条目对象折叠为其唯一子节点 —— `All([X])` 与 `X` 语义等价，
    /// 不折叠会让 `NOT: {Season: winter}` 变成 `Not(All([Key]))`，多出无意义的一层。
    /// 顶层不走这里（`Parse` 直接调 `ParseObject`，始终返回 `All`）。
    /// </summary>
    private static WhenNode ParseSingle(JToken value)
    {
        if (value is not JObject o) return WhenNode.Key(value.ToString());
        var entries = o.Properties().ToList();
        return entries.Count == 1
            ? ParseEntry(entries[0].Name, entries[0].Value)
            : ParseObject(o);
    }

    /// <summary>
    /// 把条件键拆成名字与参数（<c>HasMod |contains=X</c> / <c>HasSeenEvent: |contains=X</c>）。
    /// <para>
    /// <b>走 CP 的词法器</b>（隐含花括号模式）—— 这正是 CP 那边解析条件键走的路
    /// （<c>Lexer.ParseBits(raw, impliedBraces: true)</c>），不再自己「找第一个 <c>:</c> 或 <c>|</c>」。
    /// 后者在参数里**嵌了令牌**时会切错（参数里那个 <c>:</c>/<c>|</c> 不是分隔符），词法器天生分得清。
    /// </para>
    /// <para>
    /// 词法失败（键名里有孤立的 <c>{{</c> 之类）时退化成「整串当键名、无参数」——
    /// CP 那边会直接判这条补丁失败；我们两条路都会走到「不认识的令牌 ⇒ 拒写」，
    /// 结论一致，但不抛异常、也不带走整次转换。
    /// </para>
    /// </summary>
    private static WhenNode SplitKey(string raw, JToken value)
    {
        string? text = value.Type == JTokenType.String
            ? value.Value<string>()
            : value.ToString();

        string? name = null;
        string? args = null;
        try
        {
            if (CpLexer.Instance.ParseBits(raw, impliedBraces: true).FirstOrDefault() is LexTokenToken tk)
            {
                name = tk.Name;
                args = tk.InputArgs?.ToString().Trim();
                if (args is { Length: 0 }) args = null;
            }
        }
        catch (LexFormatException)
        {
            name = null;
        }

        return name == null
            ? WhenNode.Key(raw.Trim(), null, text)
            : WhenNode.Key(name, args, text);
    }

    /// <summary>供报告使用：把键名还原成 CP 原文的写法。</summary>
    public static string Render(WhenNode node) => node.Kind switch
    {
        WhenNodeKind.Key => node.Arguments == null ? node.KeyName! : $"{node.KeyName}: {node.Arguments}",
        WhenNodeKind.Not => $"NOT {Render(node.Children[0])}",
        WhenNodeKind.All => "AND [" + string.Join(", ", node.Children.Select(Render)) + "]",
        WhenNodeKind.Any => "OR [" + string.Join(", ", node.Children.Select(Render)) + "]",
        WhenNodeKind.Literal => node.LiteralValue ? "TRUE" : "FALSE",
        _ => "?"
    };
}
