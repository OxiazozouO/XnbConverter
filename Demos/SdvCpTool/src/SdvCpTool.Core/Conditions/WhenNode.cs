namespace SdvCpTool.Core.Conditions;

public enum WhenNodeKind { All, Any, Not, Key, Literal }

/// <summary>CP `When` 条件的树形表示。</summary>
public sealed class WhenNode
{
    public WhenNodeKind Kind;
    public List<WhenNode> Children = new();
    public string? KeyName;
    public string? Arguments;

    /// <summary>Key 节点的原始值（CP 值侧）。组合节点（All/Any/Not）为 null。</summary>
    public string? Value;

    /// <summary>Literal 节点的布尔常量值；其余节点为 null/false 无意义。</summary>
    public bool LiteralValue;

    public static WhenNode All(params WhenNode[] children) =>
        new() { Kind = WhenNodeKind.All, Children = children.ToList() };

    public static WhenNode Key(string name, string? arguments = null, string? value = null) =>
        new() { Kind = WhenNodeKind.Key, KeyName = name, Arguments = arguments, Value = value };

    /// <summary>常量折叠产生的布尔字面量节点。</summary>
    public static WhenNode Literal(bool value) =>
        new() { Kind = WhenNodeKind.Literal, LiteralValue = value };

    /// <summary>深度优先遍历自身与所有后代。</summary>
    public IEnumerable<WhenNode> Descendants()
    {
        yield return this;
        foreach (WhenNode child in Children)
            foreach (WhenNode d in child.Descendants())
                yield return d;
    }
}
