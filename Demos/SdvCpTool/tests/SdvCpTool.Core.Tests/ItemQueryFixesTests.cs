using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// DLL 专有的物品查询 → fallback 物品（<see cref="ItemQueryFixes"/>）。
/// <para>
/// 实测来源（2026-10-18）：`Data/Locations` 的 <c>ArtifactSpots[].ItemId</c> 与
/// `Data/Objects` 的 <c>GeodeDrops[].ItemId</c> 用了
/// <c>VMV.MISSING_ARTIFACT_OR_ITEM (O)Lumisteria.MtVapius_Treasures_EfficientCog --filter …</c>。
/// 原版对未知查询键会 `ItemRegistry.Create(query)` ⇒ **Error Item**（`ItemQueryResolver.cs:743,774-786`）；
/// VMV 的语义是"随机缺文物，匹配不到退回 fallback"（`MissingArtifact.cs:24-83`），
/// 纯 XNB 直接取 fallback 最安全。
/// </para>
/// </summary>
public class ItemQueryFixesTests
{
    private const string Query =
        "VMV.MISSING_ARTIFACT_OR_ITEM (O)Lumisteria.MtVapius_Treasures_EfficientCog --filter ITEM_OBJECT_TYPE Target Arch";

    [Fact]
    public void 嵌套数组和对象里的查询都换成fallback()
    {
        var data = JObject.Parse($$"""
        {
          "ArtifactSpots": [ { "ItemId": "{{Query}}" } ],
          "GeodeDrops": { "Default": [ { "ItemId": "{{Query}}, ITEM_ID_PREFIX Target Lumisteria.MtVapius_" } ] }
        }
        """);

        Assert.Equal(2, ItemQueryFixes.Rewrite("Data/Locations", data));
        Assert.Equal("(O)Lumisteria.MtVapius_Treasures_EfficientCog",
            data["ArtifactSpots"]![0]!["ItemId"]!.Value<string>());
        Assert.Equal("(O)Lumisteria.MtVapius_Treasures_EfficientCog",
            data["GeodeDrops"]!["Default"]![0]!["ItemId"]!.Value<string>());
    }

    [Fact]
    public void 非Data资产与无关字符串不动()
    {
        var data = JObject.Parse($$"""
        { "A": "{{Query}}", "B": "普通文本" }
        """);

        Assert.Equal(0, ItemQueryFixes.Rewrite("Characters/Dialogue/Abigail", data));
        Assert.Equal(0, ItemQueryFixes.Rewrite("Data/Objects",
            JObject.Parse("""{ "A": "普通文本" }""")));
    }
}
