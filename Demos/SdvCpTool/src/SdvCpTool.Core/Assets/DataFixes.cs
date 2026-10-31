using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// 把**只有装了某个 DLL 才生效**的数据字段换成原版能用的写法。
/// <para>
/// 目前已覆盖（2026-10-18 调查）：
/// <list type="bullet">
/// <item><c>Data/SpecialOrders</c> 的自定义订单板：原版只有三种板
/// （<c>""</c> 默认板、<c>"Qi"</c>、<c>"DesertFestivalMarlon"</c>），
/// <c>Action = SpecialOrders &lt;id&gt;</c> 的参数会被忽略（`GameLocation.cs:9020-9030`）——
/// 自定义板的订单在纯 XNB 里**永远打不开**。把 <c>OrderType</c> 改成 <c>""</c>，订单至少会进默认板候选池。</item>
/// <item><c>Data/Machines</c> 的 <c>OutputMethod</c>：`StaticDelegateBuilder` 用 `Type.GetType`
/// 找 mod 类型，纯 XNB 必然失败（`MachineDataUtility.cs:204-210` 返回 null ⇒ 机器无产出）。
/// 语料 11 处全是 OrnithologistsGuild 的喂食器；该方法本身是固定权重随机
/// （747×25 / 335×3 / 336×1），展开成原版 <c>OutputItem</c> 列表即可保留概率。</item>
/// </list>
/// </para>
/// </summary>
public static class DataFixes
{
    private static readonly HashSet<string> VanillaOrderBoards =
        new(StringComparer.OrdinalIgnoreCase) { "", "Qi", "DesertFestivalMarlon" };

    public static int Normalize(string assetName, JToken? data)
    {
        if (data is null) return 0;
        if (string.Equals(assetName, "Data/SpecialOrders", StringComparison.OrdinalIgnoreCase))
            return MergeCustomOrderBoards(data);
        if (string.Equals(assetName, "Data/Machines", StringComparison.OrdinalIgnoreCase))
            return ExpandFeederOutputMethods(data);
        return 0;
    }

    /// <summary>自定义订单板的 <c>OrderType</c> → 默认板 <c>""</c>。</summary>
    private static int MergeCustomOrderBoards(JToken data)
    {
        if (data is not JObject root) return 0;
        int rewritten = 0;
        foreach (JProperty order in root.Properties())
        {
            if (order.Value is not JObject body) continue;
            if (body["OrderType"] is not JValue { Type: JTokenType.String } token) continue;
            string? type = token.Value<string>();
            if (type == null || VanillaOrderBoards.Contains(type)) continue;
            token.Value = "";
            rewritten++;
        }
        return rewritten;
    }

    /// <summary>OrnithologistsGuild 的喂食器 <c>OutputMethod</c> → 加权 <c>OutputItem</c>。</summary>
    private static int ExpandFeederOutputMethods(JToken data)
    {
        if (data is not JObject machines) return 0;
        int rewritten = 0;

        foreach (JProperty machine in machines.Properties())
        {
            if (machine.Value is not JObject body || body["OutputRules"] is not JArray rules) continue;
            foreach (JToken ruleToken in rules)
            {
                if (ruleToken is not JObject rule) continue;
                if (rule["OutputItem"] is not JArray outputs) continue;

                bool changed = false;
                foreach (JToken outputToken in outputs.ToList())
                {
                    if (outputToken is not JObject output) continue;
                    if (output["OutputMethod"] is not JValue { Type: JTokenType.String } method) continue;
                    string? text = method.Value<string>();
                    if (text == null || !text.Contains("OrnithologistsGuild", StringComparison.Ordinal)
                        || !text.Contains("GetFeederOutput", StringComparison.Ordinal))
                        continue;

                    int index = outputs.IndexOf(outputToken);
                    outputs.RemoveAt(index);
                    // 权重：747 ×25、335 ×3、336 ×1（反编译 `OrnithologistsGuild` 的固定表）
                    var expanded = new List<JToken>();
                    for (int i = 0; i < 25; i++) expanded.Add(FeederClone(output, "(O)747"));
                    for (int i = 0; i < 3; i++) expanded.Add(FeederClone(output, "(O)335"));
                    expanded.Add(FeederClone(output, "(O)336"));
                    for (int i = 0; i < expanded.Count; i++) outputs.Insert(index + i, expanded[i]);
                    changed = true;
                    rewritten++;
                }
                if (changed) rule["UseFirstValidOutput"] = false;
            }
        }
        return rewritten;
    }

    private static JObject FeederClone(JObject source, string itemId)
    {
        var clone = (JObject)source.DeepClone();
        clone.Remove("OutputMethod");
        clone["ItemId"] = itemId;
        return clone;
    }
}
