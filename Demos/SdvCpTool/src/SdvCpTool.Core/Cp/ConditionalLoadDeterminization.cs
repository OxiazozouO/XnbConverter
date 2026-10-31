using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// **带条件的 <c>Load</c>**：如果目标在树里还不存在、且条件依赖的是运行时存档状态，
/// 就去掉条件、按无条件加载。
/// <para>
/// 这类 `Load` 在 fix26 里全被判 `NeedsReview` 丢掉（68 条，其中 27 条 `Characters/*`）：
/// 例如 SVE 的 `Characters/Dialogue/CharlieChicken`（看过某事件后才有对话）、
/// East Scarp 的条件立绘/对话。纯 XNB 里"运行时才知道要不要这个资产"必须二选一 ——
/// 这里选**写**（内容先存在，是否被引用仍由游戏数据决定），并报告。
/// </para>
/// <para>
/// 只做**文本/贴图/角色对话**（`Characters/Dialogue/`、`Strings/`、`Portraits/`、`Characters/`，
/// 但排除 `Characters/schedules/`）。**地图、日程、数据资产不动**：地图可能有互斥变体，
/// 写错一张就是"节日地图串场"；日程会直接改 NPC 日常行为。
/// </para>
/// </summary>
public static class ConditionalLoadDeterminization
{
    private static readonly string[] AllowedPrefixes =
    {
        "Characters/Dialogue/", "Strings/", "Portraits/", "Characters/",
        // 贴图类：目标不存在时才写；多个互斥变体最后一条胜出（确定化，报告里注明）。
        "TileSheets/", "LooseSprites/",
    };

    public static int Apply(List<ChangePatch> patches, DynamicTokenResolver tokens,
        Func<string, bool> exists, ICollection<string>? notes = null)
    {
        int changed = 0;
        foreach (ChangePatch patch in patches)
        {
            if (!patch.IsLoad) continue;
            if (patch.When == null || !patch.When.HasValues) continue;

            // CP 的 `Target` 可以是逗号分隔的多个资产（`Characters/X, Portraits/X`）。
            // **只有全部目标都不存在**时才整条去条件：只要有一个已存在，去掉条件就会
            // 把已有资产也一起 Load 覆盖掉。混合情况保持原样（保守）。
            IReadOnlyList<string> targets = AssetNameList.Split(patch.Target);
            if (targets.Count == 0) continue;
            var normalized = new List<string>(targets.Count);
            bool usable = true;
            foreach (string raw in targets)
            {
                // `\` 与 `/` 等价（CP/SMAPI 会归一）。
                string target = raw.Trim().Replace('\\', '/').Trim('/');
                if (target.Length == 0 || target.Contains("{{")
                    || target.StartsWith("Characters/schedules/", StringComparison.OrdinalIgnoreCase)
                    || !AllowedPrefixes.Any(p => target.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    usable = false;
                    break;
                }
                normalized.Add(target);
            }
            if (!usable || normalized.Any(exists)) continue;

            bool? when = tokens.TryEvaluateWhen(patch.When);
            // 真·静态为假（`HasMod` 不成立）→ 不动；运行时/不知道 → 确定为无条件。
            if (when == false && !RuntimeConditions.AllMayDependOnSave(patch.When, tokens)) continue;

            patch.Raw.Remove("When");
            patch.When = null;
            changed++;
            notes?.Add($"`Load {string.Join(", ", normalized)}` 的条件依赖运行时状态，无法静态表达 ⇒ 按**无条件加载**（内容可能提前存在）");
        }
        return changed;
    }
}
