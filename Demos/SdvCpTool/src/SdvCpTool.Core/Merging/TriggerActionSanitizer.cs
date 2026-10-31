using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 清掉纯 XNB 里**不可能执行**的 TriggerActions：
/// <list type="bullet">
/// <item>触发器不是原版四种（<c>DayEnding/DayStarted/LocationChanged/Manual</c>）——没有框架注册；</item>
/// <item>动作不是原版 27 个之一（CP/SpaceCore/BETAS/UnlockableBundles 的自定义动作）；</item>
/// <item>同 Id 重复（游戏只保留第一条，后面的永远不会跑）。</item>
/// </list>
/// 只删已经确定不会执行的条目，不尝试猜自定义动作的语义。
/// </summary>
public static class TriggerActionSanitizer
{
    private static readonly HashSet<string> ValidTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        "DayEnding", "DayStarted", "LocationChanged", "Manual",
    };

    private static readonly HashSet<string> ValidActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddBuff", "AddConversationTopic", "AddFriendshipPoints", "AddItem", "AddMail",
        "AddMoney", "AddQuest", "AddSpecialOrder", "If", "IncrementStat",
        "MarkActionApplied", "MarkCookingRecipeKnown", "MarkCraftingRecipeKnown",
        "MarkEventSeen", "MarkQuestionAnswered", "MarkSongHeard", "Null", "RemoveBuff",
        "RemoveConversationTopic", "RemoveItem", "RemoveMail", "RemoveQuest",
        "RemoveSpecialOrder", "RemoveTemporaryAnimatedSprites", "SetNpcInvisible",
        "SetNpcVisible",
    };

    public static int Sanitize(OutputTree output)
    {
        int removed = SanitizeFile(output, "Data/TriggerActions.json");
        foreach (string suffix in LocaleVariants.Suffixes)
        {
            string rel = "Data/TriggerActions" + suffix + ".json";
            if (output.Exists(rel)) removed += SanitizeFile(output, rel);
        }
        return removed;
    }

    private static int SanitizeFile(OutputTree output, string relativePath)
    {
        string? text = output.ReadTextOrNull(relativePath);
        if (text == null) return 0;

        JArray entries;
        try { entries = JArray.Parse(text); }
        catch { return 0; }

        var kept = new JArray();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        bool changed = false;

        foreach (JObject entry in entries.OfType<JObject>())
        {
            string trigger = entry.GetValue("Trigger", StringComparison.OrdinalIgnoreCase)?.ToString() ?? "";
            if (!ValidTriggers.Contains(trigger))
            {
                removed++;
                changed = true;
                continue;
            }

            bool hasAction = false;
            bool removedAction = false;

            // 1.6 的条目两种写法都有：单条 `Action` 字符串与 `Actions` 数组。
            // 字段名本身也可能是全大写（`ID`/`Action`），JObject 的索引器区分大小写。
            JProperty? actionProp = entry.Properties()
                .FirstOrDefault(p => p.Name.Equals("Action", StringComparison.OrdinalIgnoreCase));
            if (actionProp?.Value.Type == JTokenType.String)
            {
                string name = (actionProp.Value.ToString() ?? "").Split(' ', 2,
                    StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (ValidActions.Contains(name)) hasAction = true;
                else { actionProp.Remove(); removed++; removedAction = true; changed = true; }
            }

            JProperty? actionsProp = entry.Properties()
                .FirstOrDefault(p => p.Name.Equals("Actions", StringComparison.OrdinalIgnoreCase));
            if (actionsProp?.Value is JArray actions)
            {
                var good = new JArray();
                foreach (JToken action in actions)
                {
                    string name = (action.ToString() ?? "").Split(' ', 2,
                        StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    if (ValidActions.Contains(name)) good.Add(action);
                    else { removed++; removedAction = true; changed = true; }
                }
                if (good.Count == 0) actionsProp.Remove();
                else { actionsProp.Value = good; hasAction = true; }
            }

            if (!hasAction)
            {
                if (!removedAction) removed++;
                changed = true;
                continue;
            }

            string id = entry.GetValue("Id", StringComparison.OrdinalIgnoreCase)?.ToString() ?? "";
            if (id.Length > 0 && !ids.Add(id))
            {
                removed++;
                changed = true;
                continue;
            }
            kept.Add(entry);
        }

        if (changed)
            output.StageJson(relativePath, kept);
        return changed ? removed : 0;
    }
}
