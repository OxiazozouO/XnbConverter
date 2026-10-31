namespace SdvCpTool.Core.Conditions;

/// <summary>
/// 叶子级<b>常量折叠 + 部分求值</b>：把能静态求值的条件叶子折成布尔字面量，再做布尔化简。
/// <para>
/// 这<b>不是</b>布尔化简（CP 条件本来就是最简的），而是把"知道的那部分"折掉，
/// 让剩下的残条件交给 GSQ —— 避免一条求不了值的叶子把旁边能处理的叶子一起拖死。
/// </para>
/// <para>
/// 最要紧的约束：<paramref name="evaluateLeaf"/> 返回 <c>null</c> 表示<b>不知道</b>，
/// 必须原样保留该叶子，<b>绝不能当成 false</b>。
/// </para>
/// </summary>
public static class ConstantFolder
{
    /// <summary>纯函数：返回一棵<b>新树</b>，不改动原树。</summary>
    /// <param name="evaluateLeaf">
    /// 单个 Key 节点的静态求值：<c>true</c>/<c>false</c> = 已确定，折成字面量；
    /// <c>null</c> = 求不了，原样保留。
    /// </param>
    public static WhenNode Fold(WhenNode node, Func<WhenNode, bool?> evaluateLeaf) => node.Kind switch
    {
        WhenNodeKind.Key => FoldKey(node, evaluateLeaf),
        WhenNodeKind.Not => FoldNot(node, evaluateLeaf),
        WhenNodeKind.All => FoldAll(node, evaluateLeaf),
        WhenNodeKind.Any => FoldAny(node, evaluateLeaf),
        _ => Clone(node),   // 已是 Literal
    };

    private static WhenNode FoldKey(WhenNode node, Func<WhenNode, bool?> evaluateLeaf)
    {
        bool? value = evaluateLeaf(node);
        return value.HasValue ? WhenNode.Literal(value.Value) : Clone(node);
    }

    private static WhenNode FoldNot(WhenNode node, Func<WhenNode, bool?> evaluateLeaf)
    {
        if (node.Children.Count == 0) return Clone(node);
        WhenNode child = Fold(node.Children[0], evaluateLeaf);
        if (child.Kind == WhenNodeKind.Literal)
            return WhenNode.Literal(!child.LiteralValue);
        return new WhenNode { Kind = WhenNodeKind.Not, Children = { child } };
    }

    private static WhenNode FoldAll(WhenNode node, Func<WhenNode, bool?> evaluateLeaf)
    {
        var kept = new List<WhenNode>();
        foreach (WhenNode child in node.Children)
        {
            WhenNode folded = Fold(child, evaluateLeaf);
            if (folded.Kind == WhenNodeKind.Literal)
            {
                if (!folded.LiteralValue) return WhenNode.Literal(false);   // 任一为假 → 整条为假（短路）
                continue;                                                    // Literal(true) 丢掉
            }
            kept.Add(folded);
        }
        if (kept.Count == 0) return WhenNode.Literal(true);                  // 全丢光 → 恒真
        if (kept.Count == 1) return kept[0];                                 // 只剩一个 → 提升
        return new WhenNode { Kind = WhenNodeKind.All, Children = kept };
    }

    private static WhenNode FoldAny(WhenNode node, Func<WhenNode, bool?> evaluateLeaf)
    {
        var kept = new List<WhenNode>();
        foreach (WhenNode child in node.Children)
        {
            WhenNode folded = Fold(child, evaluateLeaf);
            if (folded.Kind == WhenNodeKind.Literal)
            {
                if (folded.LiteralValue) return WhenNode.Literal(true);      // 任一为真 → 整条为真（短路）
                continue;                                                    // Literal(false) 丢掉
            }
            kept.Add(folded);
        }
        if (kept.Count == 0) return WhenNode.Literal(false);                 // 全丢光 → 恒假
        if (kept.Count == 1) return kept[0];                                 // 只剩一个 → 提升
        return new WhenNode { Kind = WhenNodeKind.Any, Children = kept };
    }

    private static WhenNode Clone(WhenNode node) => new()
    {
        Kind = node.Kind,
        Children = node.Children.ToList(),
        KeyName = node.KeyName,
        Arguments = node.Arguments,
        Value = node.Value,
        LiteralValue = node.LiteralValue,
    };
}
