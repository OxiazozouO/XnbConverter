namespace SdvCpTool.Core.Conditions;

/// <summary>翻译结果。失败时带可读原因，不抛异常。</summary>
public sealed record GsqTranslation
{
    public bool Success { get; init; }
    public string? Gsq { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>
    /// 条件恒为假（常量折叠后的 <c>Literal(false)</c>）：该补丁<b>本就不适用</b>，
    /// 与"翻译失败"和"翻译成空串（无条件）"都不同。
    /// </summary>
    public bool NeverApplies { get; init; }

    public static GsqTranslation Ok(string gsq) => new() { Success = true, Gsq = gsq };
    public static GsqTranslation Fail(string reason) => new() { Success = false, FailureReason = reason };
}

/// <summary>
/// 把 CP 的 <c>When</c> 条件树翻译成 GameStateQuery 字符串。
/// 权威依据：docs/research/2026-10-05-cp-to-gsq-token-mapping.md
/// </summary>
public static class GsqTranslator
{
    public static GsqTranslation Translate(WhenNode? when)
    {
        if (when == null) return GsqTranslation.Ok("");
        return Render(when);
    }

    private static GsqTranslation Render(WhenNode node) => node.Kind switch
    {
        WhenNodeKind.All => RenderAll(node),
        WhenNodeKind.Any => RenderAny(node),
        WhenNodeKind.Not => RenderNot(node),
        WhenNodeKind.Key => TokenTranslator.Translate(node),
        // 常量折叠的产物：恒真 → 无条件（空串）；恒假 → 该补丁不适用。
        WhenNodeKind.Literal => node.LiteralValue
            ? GsqTranslation.Ok("")
            : new GsqTranslation { Success = true, NeverApplies = true },
        _ => GsqTranslation.Fail($"未知的节点类型 {node.Kind}")
    };

    /// <summary>`{"a":..,"b":..}` 与 `AND` 都是与 —— GSQ 用顶层逗号连接。</summary>
    private static GsqTranslation RenderAll(WhenNode node)
    {
        var parts = new List<string>();
        foreach (WhenNode child in node.Children)
        {
            GsqTranslation r = Render(child);
            if (!r.Success) return r;
            if (!string.IsNullOrEmpty(r.Gsq)) parts.Add(r.Gsq!);
        }
        return GsqTranslation.Ok(string.Join(", ", parts));
    }

    /// <summary>`OR` → `ANY "q1" "q2"`。ANY 只能包单个 query，所以子节点必须各是一个 query。</summary>
    private static GsqTranslation RenderAny(WhenNode node)
    {
        var parts = new List<string>();
        foreach (WhenNode child in node.Children)
        {
            GsqTranslation r = Render(child);
            if (!r.Success) return r;
            if (string.IsNullOrEmpty(r.Gsq)) continue;
            if (r.Gsq!.Contains(", "))
            {
                return GsqTranslation.Fail(
                    "OR 分支里出现了多条件（GSQ 的 ANY 只能包单个 query，包不住\"与\"）");
            }
            parts.Add(r.Gsq!);
        }
        if (parts.Count == 0) return GsqTranslation.Fail("OR 没有任何分支");
        if (parts.Count == 1) return GsqTranslation.Ok(parts[0]);
        return GsqTranslation.Ok("ANY " + string.Join(" ", parts.Select(p => $"\"{p}\"")));
    }

    private static GsqTranslation RenderNot(WhenNode node)
    {
        WhenNode child = node.Children[0];
        if (child.Kind != WhenNodeKind.Key)
        {
            return GsqTranslation.Fail("NOT 只能作用于单个条件，不能包住多条件（GSQ 的 `!` 不接受组合）");
        }
        GsqTranslation r = Render(child);
        return r.Success ? GsqTranslation.Ok("!" + r.Gsq) : r;
    }
}
