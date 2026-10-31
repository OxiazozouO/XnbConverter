using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Validating;

/// <summary>
/// 从资产的 <c>&lt;资产&gt;.config</c>（XNB 的**类型描述符**）里读出「元素是标量还是对象」。
/// <para>
/// <b>为什么需要它</b>：CP 把条目的值**按声明的类型反序列化**，转不了就**记一条警告并跳过**
/// （<c>EditDataPatch.cs</c> 的 <c>WarnForRecord("failed converting … to the expected type")</c>）。
/// 我们是**在 JSON 层面合并**的，看不见类型 —— 于是会把**对象**写进声明为
/// <c>Dictionary&lt;int, string&gt;</c> 的资产里。实测代价：Cornucopia 两个包往
/// <c>spacechase0.SpaceCore/ObjectExtensionData</c>（描述符明写 <c>Dictionary&lt;int, string&gt;</c>）、
/// <c>Mods/mistyspring.ItemExtensions/MixedSeeds</c> 里写对象 ⇒ **回包时 12 个资产直接失败**
/// （打包器报「无效的JSON文件格式」）。
/// </para>
/// <para>
/// <b>只认「简单形状」</b>：<c>Readers</c> 恰好 2 项（<c>List&lt;标量&gt;</c>）或 3 项
/// （<c>Dictionary&lt;标量, 标量&gt;</c>）且**最后一项是标量读取器** —— 这时"末项即元素类型"才成立。
/// 更深的嵌套（<c>Dictionary&lt;string, List&lt;string&gt;&gt;</c> 之类）末项不是元素类型，
/// 一律**不表态**（返回 <c>Unknown</c>），免得拿错结论去拒绝正常的包。
/// </para>
/// </summary>
public static class ConfigLeafTypes
{
    /// <summary>元素类型。<c>ManagedScalar</c> = 标量（字符串/数字/布尔），<c>Unknown</c> = 不表态。</summary>
    public enum LeafKind { Unknown, Scalar }

    /// <summary>CP/XNA 里「标量」的读取器简单名。</summary>
    private static readonly HashSet<string> ScalarReaders = new(StringComparer.Ordinal)
    {
        "StringReader", "CharReader", "BooleanReader", "ByteReader", "SByteReader",
        "Int16Reader", "UInt16Reader", "Int32Reader", "UInt32Reader", "Int64Reader", "UInt64Reader",
        "SingleReader", "DoubleReader", "DecimalReader", "EnumReader",
    };

    /// <summary>读 <paramref name="configPath"/>；读不了/认不出时返回 <see cref="LeafKind.Unknown"/>。</summary>
    public static LeafKind ReadLeafKind(string? configPath)
    {
        if (configPath == null || !File.Exists(configPath)) return LeafKind.Unknown;

        JObject root;
        try { root = JObject.Parse(File.ReadAllText(configPath)); }
        catch { return LeafKind.Unknown; }

        if (root["Readers"] is not JArray readers) return LeafKind.Unknown;
        if (readers.Count is not (2 or 3)) return LeafKind.Unknown;

        // ⚠ 用 `readers[readers.Count - 1]`，**不能**写 `readers[^1]`：`JArray` 的索引器收 `object`，
        //   `^1` 会被当成"键"抛 `ArgumentException`（实测踩过）。
        string last = SimpleName(readers[readers.Count - 1]?["Type"]?.Value<string>());
        return ScalarReaders.Contains(last) ? LeafKind.Scalar : LeafKind.Unknown;
    }

    /// <summary>取泛型类型串的**简单名**：<c>…Content.StringReader</c> → <c>StringReader</c>。</summary>
    private static string SimpleName(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "";
        string text = type;
        int backtick = text.IndexOf('`');
        if (backtick >= 0)
        {
            int end = text.IndexOf('[', backtick);
            text = end < 0 ? text[..backtick] : text[..end];
        }
        int dot = text.LastIndexOf('.');
        return dot < 0 ? text : text[(dot + 1)..];
    }
}
