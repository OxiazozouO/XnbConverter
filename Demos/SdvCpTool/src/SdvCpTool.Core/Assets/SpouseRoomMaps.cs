using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// **配偶房间地图**（`Data/Characters[].SpouseRoom.MapAsset`，默认 <c>spouseRooms</c>）。
/// <para>
/// 这些地图不是让玩家走进去的普通地点：`FarmHouse.loadSpouseRoom()` 会把
/// `MapSourceRect` 那块 6×9 **原样 `ApplyMapOverride` 复制进农舍**
/// （`D:/sdv_src/StardewValley.Locations/FarmHouse.cs:1988-2065`）。
/// ⇒ 在它们上面写 `TouchAction = ConditionalDoor` 会被**一起复制进婚房**，
/// 婚后玩家在自己的房间里被门禁弹回。条件化的婚房变体应当**无条件写出**（选一个变体），
/// 而不是设门禁。
/// </para>
/// </summary>
public static class SpouseRoomMaps
{
    /// <summary>读一次 `Data/Characters`，返回所有配偶房间地图的资产名（`Maps/…`，大小写不敏感）。</summary>
    public static IReadOnlySet<string> Referenced(ResourceTree tree)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Maps/spouseRooms" };

        string? path = tree.Resolve("Data/Characters");
        if (path == null || !File.Exists(path)) return set;

        JObject? root;
        try { root = JObject.Parse(File.ReadAllText(path)); }
        catch (Newtonsoft.Json.JsonException) { return set; }

        foreach (JProperty npc in root.Properties())
        {
            if (npc.Value is not JObject entry || entry["SpouseRoom"] is not JObject room) continue;
            string asset = room["MapAsset"]?.Value<string>() is string s && s.Trim().Length > 0
                ? s.Trim()
                : "spouseRooms";
            set.Add(asset.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? asset : "Maps/" + asset);
        }
        return set;
    }
}
