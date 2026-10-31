using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>解析 CP 的 JSONC 文本：支持行注释、块注释、尾逗号。</summary>
public static class JsoncReader
{
    private const char Backslash = '\\';

    public static JObject Parse(string text)
    {
        try { return JObject.Parse(text); }
        catch (Newtonsoft.Json.JsonException) { return JObject.Parse(Strip(text)); }
    }

    /// <summary>解析任意根节点的 JSON/JSONC；合法 JSON 直接解析，失败才走 Strip。</summary>
    public static JToken ParseToken(string text)
    {
        // 绝大多数文件是合法 JSON：直接解析，省掉 Strip 的两遍扫描和两份大字符串。
        try { return JToken.Parse(text); }
        catch (Newtonsoft.Json.JsonException) { return JToken.Parse(Strip(text)); }
    }

    public static JObject ParseFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>去掉注释与尾逗号，保留字符串字面量原样。</summary>
    public static string Strip(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0;
        int n = text.Length;
        bool inString = false;
        bool escaped = false;

        while (i < n)
        {
            char c = text[i];

            if (inString)
            {
                sb.Append(c);
                if (escaped) escaped = false;
                else if (c == Backslash) escaped = true;
                else if (c == '"') inString = false;
                i++;
                continue;
            }

            if (c == '"') { inString = true; sb.Append(c); i++; continue; }

            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                while (i < n && text[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return RemoveTrailingCommas(sb.ToString());
    }

    /// <summary>删除 `,` 后紧跟 `}` 或 `]` 的逗号（仅字符串外）。</summary>
    private static string RemoveTrailingCommas(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                sb.Append(c);
                if (escaped) escaped = false;
                else if (c == Backslash) escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; sb.Append(c); continue; }

            if (c == ',')
            {
                int j = i + 1;
                while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                if (j < text.Length && (text[j] == '}' || text[j] == ']')) continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
