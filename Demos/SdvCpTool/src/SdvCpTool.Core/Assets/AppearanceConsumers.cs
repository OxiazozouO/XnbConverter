using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// **谁在引用这批立绘**：把 <c>Data/Characters</c> 里所有
/// <c>Appearance[].Sprite</c> / <c>Appearance[].Portrait</c> 引用的资产名收成一个集合。
///
/// <para>
/// <b>为什么要它</b>：条件化 <c>EditImage</c> 打到 <c>Portraits/&lt;NPC&gt;_&lt;变体&gt;</c> 这种**变体资产名**上时，
/// 我们的外观发射器找不到对应的 NPC（提取出的名字是 <c>Aideen_spring</c> 而不是 <c>Aideen</c>）⇒ 整组不发射。
/// 但这类图**往往已经有消费方**：别的包（或本包）在 <c>Data/Characters[...].Appearance[]</c> 里
/// 用 <b>条件</b> 引用它（实测 `Poltergeister.SeasonalCuteSpritesES` 那批：
/// <c>{"Condition":"SEASON Spring","Sprite":"Characters/Aideen_Spring","Portrait":"Portraits/Aideen_Spring"}</c>）。
/// 游戏只在那个条件成立时才去读图 ⇒ 我们**把图无条件写出去**就等价于 CP 的行为
/// （CP 那边条件是管"图存不存在"，注册侧的条件管"用不用它"）。
/// </para>
///
/// <para>
/// ⚠ 判据是**引用**，不是"名字长得像"（`2026-10-09-non-cp-framework-channels.md` §7.2 那条错报的教训）。
/// 名字大小写不敏感（Windows 文件系统吸收 —— 实测那批小写 <c>Portraits/Aideen_spring</c> 与
/// 引用里的大写 <c>Portraits/Aideen_Spring</c> 就是这么对上的）。
/// </para>
/// </summary>
public static class AppearanceConsumers
{
    /// <summary>读一次 <c>Data/Characters</c>，返回被 <c>Appearance[]</c> 引用的资产名（小写、正斜杠）。</summary>
    public static IReadOnlySet<string> Referenced(ResourceTree tree)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? path = tree.Resolve("Data/Characters");
        if (path == null || !File.Exists(path)) return set;

        JObject? root;
        try { root = JObject.Parse(File.ReadAllText(path)); }
        catch (Newtonsoft.Json.JsonException) { return set; }   // 读不了就不认（不猜）

        foreach (JProperty npc in root.Properties())
        {
            if (npc.Value is not JObject entry) continue;
            if (entry["Appearance"] is not JArray appearances) continue;

            foreach (JToken item in appearances)
            {
                if (item is not JObject a) continue;
                foreach (string field in new[] { "Sprite", "Portrait" })
                    if (a[field]?.Value<string>() is string v && v.Trim().Length > 0)
                        set.Add(v.Replace('\\', '/').Trim('/'));
            }
        }
        return set;
    }
}
