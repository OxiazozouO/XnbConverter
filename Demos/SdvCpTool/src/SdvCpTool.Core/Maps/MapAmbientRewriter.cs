using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 把带昼夜条件的 <c>MapProperties.AmbientLight</c> 改写成原版的
/// <c>AmbientLight</c>（白天）/ <c>AmbientNightLight</c>（夜晚），并删掉 <c>When</c>。
/// <para>
/// 原版 `GameLocation.setUpLocationSpecificFlair` 只读这两个属性，且没有条件；
/// 因此每一条只能表达一个昼夜状态。白天/夜间的取值由多条补丁依次覆盖，
/// 不是完整的时分段曲线。
/// </para>
/// </summary>
public static class MapAmbientRewriter
{
    public static JObject? Rewrite(JObject raw, bool visibleAtDay)
    {
        if (raw["MapProperties"] is not JObject properties) return null;
        string key = visibleAtDay ? "AmbientLight" : "AmbientNightLight";
        string? value = properties.Value<string>(key);
        if (value == null && !visibleAtDay)
            value = properties.Value<string>("AmbientLight");
        if (value == null) return null;

        var clone = (JObject)raw.DeepClone();
        var output = new JObject { [key] = value };
        clone["MapProperties"] = output;
        clone.Remove("When");
        return clone;
    }
}
