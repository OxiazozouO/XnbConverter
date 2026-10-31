using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// 把**只有装了某个 DLL 才认识的物品查询**换成原版能解析的值。
/// <para>
/// 原版 `ItemQueryResolver.TryResolve` 遇到未知查询键会退化成 `ItemRegistry.Create(query)`，
/// 得到一个**玩家可见的 Error Item**（`ItemQueryResolver.cs:743,774-786`），不是空值。
/// </para>
/// <para>
/// 已确认的一类（2026-10-18 调查）：Visit Mount Vapius 的
/// <c>VMV.MISSING_ARTIFACT_OR_ITEM &lt;fallback&gt; --filter …</c>。
/// 它的语义是「随机给一个尚未捐给博物馆的文物，匹配不到就退回 <c>&lt;fallback&gt;</c>」
/// （`MissingArtifact.cs:24-83`）。纯 XNB 没有这个查询 ⇒ 直接取 **fallback 物品 ID** 最安全：
/// 玩家拿到的从"随机缺文物"变成"固定物品"，但不会出现 Error Item。
/// </para>
/// </summary>
public static class ItemQueryFixes
{
    private const string VmvMissingArtifact = "VMV.MISSING_ARTIFACT_OR_ITEM";

    /// <summary>改写 `Data/*` 资产里所有字符串值中的已知非原版查询。返回改写的值数。</summary>
    public static int Rewrite(string assetName, JToken? data)
    {
        if (data == null) return 0;
        if (!assetName.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)) return 0;
        return Walk(data);
    }

    private static int Walk(JToken token)
    {
        int rewritten = 0;
        switch (token)
        {
            case JObject obj:
                foreach (JProperty property in obj.Properties())
                {
                    if (property.Value is JValue { Type: JTokenType.String } value
                        && value.Value<string>() is string text
                        && TryRewrite(text, out string replacement))
                    {
                        value.Value = replacement;
                        rewritten++;
                    }
                    else
                    {
                        rewritten += Walk(property.Value);
                    }
                }
                break;
            case JArray array:
                foreach (JToken item in array)
                {
                    if (item is JValue { Type: JTokenType.String } value
                        && value.Value<string>() is string text
                        && TryRewrite(text, out string replacement))
                    {
                        value.Value = replacement;
                        rewritten++;
                    }
                    else
                    {
                        rewritten += Walk(item);
                    }
                }
                break;
        }
        return rewritten;
    }

    /// <summary><c>VMV.MISSING_ARTIFACT_OR_ITEM &lt;fallback&gt; --filter …</c> ⇒ <c>&lt;fallback&gt;</c>。</summary>
    private static bool TryRewrite(string text, out string replacement)
    {
        replacement = "";
        if (!text.StartsWith(VmvMissingArtifact, StringComparison.Ordinal)) return false;
        string rest = text[VmvMissingArtifact.Length..].TrimStart();
        if (rest.Length == 0 || rest[0] == ' ') return false;

        int space = rest.IndexOf(' ');
        string fallback = space < 0 ? rest : rest[..space];
        if (fallback.Length == 0) return false;
        replacement = fallback;
        return true;
    }
}
