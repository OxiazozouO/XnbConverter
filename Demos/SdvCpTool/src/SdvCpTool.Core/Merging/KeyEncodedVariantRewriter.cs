using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 「键即条件」补丁的原生改写：补丁的条件被条目键名<b>完全蕴含</b>时，删掉条件、无条件写入该键
/// （该键本来就只在那个状态下被游戏读取，条件成了冗余）。
/// <para>
/// 目前只实现源码确证的一种蕴含：<c>Data/mail</c> 的 <c>&lt;season&gt;_&lt;day&gt;[_&lt;year&gt;]</c> 键
/// （Game1.cs:8296-8302 —— 游戏只在对应季节的当天读这个键），因此
/// <c>When: {Season: spring}</c> 打在 <c>spring_13</c> 上是冗余的。
/// 其余维度（<c>_spouse</c>/<c>_Male</c>/<c>_yN</c>…）目前只被词表<b>识别</b>、不做蕴含改写 ——
/// 宁少勿多，未逐条确证蕴含关系的维度一律交人工（NeedsReview）。
/// </para>
/// <para>
/// 判定与写入<b>必须</b>调用同一个 <see cref="TryRewrite"/>。前提严格，任一不符即返回 <c>null</c>
/// （保持原判定，上层会给 NeedsReview）：尤其「键的季节与条件的季节不一致」时绝不改写 —— 那会静默改错别人的数据。
/// </para>
/// </summary>
public static class KeyEncodedVariantRewriter
{
    /// <summary>唯一确证「季节前缀键 = 季节维度」的数据资产（Game1.cs:8296-8302）。</summary>
    private const string MailAsset = "Data/mail";

    /// <summary>
    /// 识别「键即条件」形态并返回<b>删去条件后</b>的补丁；不满足前提时返回 <c>null</c>。
    /// 纯函数：不改动入参，也不读资源树。
    /// </summary>
    public static JObject? TryRewrite(ChangePatch patch)
    {
        // 前提 1：Action == EditData
        if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase))
            return null;

        // 前提 2：Target 恰好是 Data/mail（其它资产没有源码确证的「键名 = 季节」读取点）
        string target = (patch.Target ?? "").Replace('\\', '/').Trim();
        if (target.Contains(',')) return null;
        if (!target.Equals(MailAsset, StringComparison.OrdinalIgnoreCase)) return null;

        // 落点被 TargetField/Fields 改变时不再适用（<season>_ 键只认顶层键）。
        if (patch.Raw["TargetField"] != null || patch.Raw["Fields"] != null) return null;

        // 前提 3：Entries 至少一条
        if (patch.Raw["Entries"] is not JObject entries || entries.Count == 0) return null;

        // 前提 4：When 里全是 `Season: <季节>` 叶子，没有别的令牌
        JObject? whenObj = patch.Raw["When"] as JObject ?? patch.When;
        if (whenObj == null || whenObj.Count == 0) return null;

        string? wanted = null;
        foreach (JProperty prop in whenObj.Properties())
        {
            if (!string.Equals(prop.Name, "Season", StringComparison.OrdinalIgnoreCase)) return null;
            if (prop.Value.Type != JTokenType.String) return null;

            string? value = prop.Value.Value<string>()?.Trim();
            if (value == null || !KeyEncodedVariants.IsSeason(value)) return null;

            // 同一对象里不可能有重名键，走到第二个 Season 说明形态超出预期 → 放弃。
            if (wanted != null) return null;
            wanted = value.ToLowerInvariant();
        }

        // 前提 5：每个条目键名都以该季节开头 —— 键名蕴含条件
        foreach (JProperty entry in entries.Properties())
        {
            string? keySeason = KeyEncodedVariants.LeadingSeason(entry.Name);
            if (keySeason == null) return null;
            if (!string.Equals(keySeason, wanted, StringComparison.OrdinalIgnoreCase)) return null;
        }

        var rewritten = (JObject)patch.Raw.DeepClone();
        rewritten.Remove("When");
        return rewritten;
    }
}
