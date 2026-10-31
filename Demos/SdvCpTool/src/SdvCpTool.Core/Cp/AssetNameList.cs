namespace SdvCpTool.Core.Cp;

/// <summary>
/// 切「资产名列表」—— CP 里 `Target` / `FromFile` / Include 的 `FromFile` 都支持逗号分隔多值。
/// <para>
/// <b>为什么不能直接 <c>Split(',')</c></b>：CP 是**先分词再取值**，
/// <c>{{Random:null, 3}}</c> 是**一个**令牌（它的实参里就带逗号）。
/// 朴素切法会把它拦腰切断 —— 实测「佳能友好对话扩展」的
/// <c>Include FromFile: Data/Festivals/Y{{Random:null, 3}}.json</c> 被切成了
/// <c>Data/Festivals/Y{{Random:null</c> 与 <c>3}}.json</c>，两个都不存在，
/// 于是那条 Include 贡献了 0 条补丁（只留下两条「目标不存在」的警告）。
/// </para>
/// <para>
/// 于是这里**按 <c>{{…}}</c> 的配对深度切**：只有在深度为 0 的逗号才是分隔符。
/// 不配对时（<c>{{</c> 多于 <c>}}</c>）退化为「剩下的全算一段」，不抛异常。
/// </para>
/// </summary>
public static class AssetNameList
{
    /// <summary>切成若干段并去掉空白；空段丢弃。</summary>
    public static IReadOnlyList<string> Split(string? text)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return parts;

        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{') { depth++; i++; continue; }
            if (i + 1 < text.Length && text[i] == '}' && text[i + 1] == '}')
            {
                if (depth > 0) { depth--; i++; }
                continue;
            }
            if (text[i] == ',' && depth == 0)
            {
                Add(parts, text, start, i - start);
                start = i + 1;
            }
        }
        Add(parts, text, start, text.Length - start);
        return parts;

        static void Add(List<string> parts, string text, int start, int length)
        {
            string part = text.Substring(start, length).Trim();
            if (part.Length > 0) parts.Add(part);
        }
    }
}
