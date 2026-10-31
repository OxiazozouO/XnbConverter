using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// 结婚可用性的**最小修复**（不发明内容，只补会让原版流程断掉的字段）。
/// <para>
/// 已确认的两条硬性要求（2026-10-18）：
/// <list type="bullet">
/// <item><c>Data/Characters[&lt;NPC&gt;].CanBeRomanced</c> 必须是**布尔**。语料里
/// <c>Sterling</c> 写成了字符串 <c>"true"</c>；打包/游戏侧可能当成 false。</item>
/// <item><c>Data/NPCGiftTastes</c> 必须有该 NPC 的键，否则 `CanReceiveGifts()` 为 false
/// （`NPC.cs:1391-1398`）⇒ **花束/吊坠根本送不出去**。语料里 Lance / CorwinLK 就是缺这个键。
/// 补一个中性的 10 段条目（5 段文本 + 5 段空物品表）：物品反应走 `Universal_*`，不会误报偏好。</item>
/// </list>
/// </para>
/// </summary>
public static class MarriageReadiness
{
    /// <summary>中性礼物条目：必须是 10 段（`NPC.cs:1497-1510` 固定读 5 对）。</summary>
    private const string NeutralGiftTastes = "Thanks!/ /Thanks./ /Thanks./ /Thanks./ /Thanks./";

    /// <summary>
    /// <c>Data/Characters</c> 里**声明为 bool** 的字段：字符串 <c>"true"</c>/<c>"false"</c> → 布尔。
    /// 返回修正条数。
    /// <para>
    /// `CanBeRomanced` 是原来的唯一目标（Sterling 写成字符串）；本轮把 <c>Hurst</c> 这类
    /// <c>{{关系令牌}}</c> 按默认块代成 <c>"true"</c>/<c>"false"</c> 之后，其余布尔字段
    /// （CanReceiveGifts / WinterStarParticipant 是字符串字段，不在此列）也可能以字符串落盘。
    /// 字段表按反编译的 <c>CharacterData</c>（`StardewValley.GameData.decompiled.cs:3635-3760`）。
    /// </para>
    /// </summary>
    private static readonly string[] BooleanFields =
    {
        "IsDarkSkinned", "CanBeRomanced", "CanReceiveGifts", "CanGreetNearbyCharacters",
        "CanCommentOnPurchasedShopItems", "IntroductionsQuest", "PerfectionScore",
        "FlowerDanceCanDance", "SpawnIfMissing", "Breather",
    };

    /// <summary>本地化 `Data/Characters` 必须同步的婚姻结构字段（文本字段保留本地化）。</summary>
    private static readonly string[] MarriageFields =
    {
        "CanBeRomanced", "SpouseRoom", "SpousePatio", "SpouseFloors", "SpouseWallpapers",
        "SpouseAdopts", "SpouseWantsChildren", "SpouseGiftJealousy",
        "SpouseGiftJealousyFriendshipChange",
    };

    /// <summary>
    /// 把 base `Data/Characters` 补进一份本地化变体：缺行克隆整条；已有行只同步婚姻结构字段，
    /// 保留本地化的 `DisplayName`/`Description` 等文本。返回改动条目数。
    /// <para>
    /// `LocalizedContentManager` 对 `Data/Characters.&lt;locale&gt;` 是整份替换，不会回落 base；
    /// 只同步 base 会让新 NPC 在非中文语言里直接不存在，Sandy/Wizard 这类被改成可婚的
    /// 原版 NPC 也会继续是 `CanBeRomanced=false`。
    /// </para>
    /// </summary>
    public static int MirrorLocalizedCharacters(JObject baseCharacters, JObject localized)
    {
        int changed = 0;
        foreach (JProperty npc in baseCharacters.Properties())
        {
            if (localized.Property(npc.Name) is not JProperty local)
            {
                localized[npc.Name] = npc.Value.DeepClone();
                changed++;
                continue;
            }
            if (npc.Value is not JObject baseEntry || local.Value is not JObject localEntry) continue;

            foreach (string field in MarriageFields)
            {
                if (baseEntry.Property(field) == null) continue;
                if (JToken.DeepEquals(localEntry[field], baseEntry[field])) continue;
                localEntry[field] = baseEntry[field]!.DeepClone();
                changed++;
            }

            // 已有角色行也可能缺季节/节日外观变体；Appearance[] 按 Id 补缺，
            // 否则非中文客户端会少 Seasonal Cute Characters 等外观。
            if (baseEntry["Appearance"] is JArray baseAppearance)
            {
                if (localEntry["Appearance"] is not JArray localAppearance)
                {
                    localEntry["Appearance"] = baseAppearance.DeepClone();
                    changed++;
                }
                else
                {
                    var ids = new HashSet<string>(
                        localAppearance.OfType<JObject>()
                            .Select(x => x.Value<string>("Id") ?? "")
                            .Where(x => x.Length > 0),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (JObject appearance in baseAppearance.OfType<JObject>())
                    {
                        string? id = appearance.Value<string>("Id");
                        if (string.IsNullOrEmpty(id) || ids.Contains(id)) continue;
                        localAppearance.Add(appearance.DeepClone());
                        ids.Add(id);
                        changed++;
                    }
                }
            }
        }
        return changed;
    }

    /// <summary>
    /// 把 base `Data/Weddings` 的 `Attendees` / `EventScript` 缺键补进本地化变体。
    /// <para>
    /// `Data/Weddings.<locale>` 也是整份替换；只写 base 会让非中文婚礼缺少新 NPC 的
    /// attendee 与自定义婚礼脚本。已有本地化键不覆盖。
    /// </para>
    /// </summary>
    public static int MirrorLocalizedWeddings(JObject baseWeddings, JObject localized)
    {
        int changed = 0;
        foreach (string container in new[] { "Attendees", "EventScript" })
        {
            if (baseWeddings[container] is not JObject baseEntries) continue;
            if (localized[container] is not JObject localEntries)
            {
                localEntries = new JObject();
                localized[container] = localEntries;
            }
            foreach (JProperty entry in baseEntries.Properties())
            {
                if (localEntries.Property(entry.Name) != null) continue;
                localEntries[entry.Name] = entry.Value.DeepClone();
                changed++;
            }
        }
        return changed;
    }

    /// <summary>把 <see cref="BooleanFields"/> 里仍在字符串形态的 <c>true</c>/<c>false</c> 折成布尔。</summary>
    public static int NormalizeBooleanFlags(JObject characters)
    {
        int fixedCount = 0;
        foreach (JProperty npc in characters.Properties())
        {
            if (npc.Value is not JObject entry) continue;
            foreach (string field in BooleanFields)
            {
                if (entry[field] is not JValue { Type: JTokenType.String } value) continue;
                if (!bool.TryParse(value.Value<string>(), out bool parsed)) continue;
                entry[field] = parsed;
                fixedCount++;
            }

            // `Appearance[].IsIslandAttire` 也是 bool（`CharacterAppearanceData`）。
            if (entry["Appearance"] is JArray appearances)
            {
                foreach (JObject appearance in appearances.OfType<JObject>())
                {
                    if (appearance["IsIslandAttire"] is not JValue { Type: JTokenType.String } flag) continue;
                    if (!bool.TryParse(flag.Value<string>(), out bool parsed)) continue;
                    appearance["IsIslandAttire"] = parsed;
                    fixedCount++;
                }
            }
        }
        return fixedCount;
    }

    /// <summary>
    /// 婚后对话的**资产名别名**：游戏按 <c>Characters/Dialogue/MarriageDialogue&lt;NPC 名&gt;</c> 加载
    /// （`NPC.cs:3542-3599`）。语料里 East Scarp 的 NPC 叫 <c>JadeMalic</c>，作者却把 79 条婚后对话写进了
    /// <c>MarriageDialogueJade</c> —— 目标资产是空文件，婚后配偶只会说通用台词。
    /// <para>
    /// 规则：本 NPC 的对话为空、存在一个**名字是它前缀**的非 NPC 资产且非空 ⇒ 用它。
    /// 「非 NPC」这条件防止把另一个角色的对话复制过来；取最长前缀，避免多个候选时选错。
    /// </para>
    /// </summary>
    public static string? FindDialogueAlias(string npc, IEnumerable<string> characterNames,
        IReadOnlyDictionary<string, int> keyCounts)
    {
        if (keyCounts.TryGetValue(npc, out int own) && own > 0) return null;

        List<string> names = characterNames as List<string> ?? characterNames.ToList();
        string? best = null;
        foreach ((string candidate, int count) in keyCounts)
        {
            if (count == 0 || candidate.Length < 3) continue;
            if (candidate.Equals(npc, StringComparison.OrdinalIgnoreCase)) continue;
            if (!npc.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) continue;
            if (names.Any(n => n.Equals(candidate, StringComparison.OrdinalIgnoreCase))) continue;
            if (best == null || candidate.Length > best.Length) best = candidate;
        }
        return best;
    }

    /// <summary>
    /// 给所有 <c>CanBeRomanced == true</c> 但缺礼物条目的 NPC 补中性条目。返回新增条数。
    /// </summary>
    public static int AddMissingGiftTastes(JObject characters, JObject giftTastes)
    {
        int added = 0;
        foreach (JProperty npc in characters.Properties())
        {
            if (npc.Value is not JObject entry) continue;
            if (entry["CanBeRomanced"] is not JValue { Type: JTokenType.Boolean } flag
                || flag.Value<bool>() != true) continue;
            if (giftTastes.Property(npc.Name) != null) continue;
            giftTastes[npc.Name] = NeutralGiftTastes;
            added++;
        }
        return added;
    }
}
