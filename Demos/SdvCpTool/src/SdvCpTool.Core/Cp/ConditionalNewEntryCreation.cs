using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// **带条件的「创建新条目」补丁**：拆出一条无条件补丁。
/// <para>
/// 原版多数数据资产（`Data/Characters` / `Data/Objects` / `Data/Shops` / `Data/Powers` …）的条目上
/// **没有条件字段**（`ConditionWriter` 判 `EntryConditionField` 落点不存在），所以
/// 「看过某事件后才创建这个 NPC / 物品 / 能力」的补丁在纯 XNB 里只有两个选择：
/// 整条丢掉（内容永远不存在）或按无条件写（内容提前存在）。
/// 本项目选择后者，但**只对"目标里还没有的条目"**，并且**静态为假的条件仍然跳过**；
/// 已有条目的条件修改保持原判（那会改错已存在的内容）。
/// </para>
/// <para>
/// 实测：Lavril / CharlieChicken / GoblinZic（NPC）、SVE/S&amp;S 的 7 个 Powers、
/// Trolley Expansion 的 `CC_DwarfInvite`、Bear Family 的商店/电话条目 —— fix26 里全部缺失。
/// 报告里会写明「条件不可静态表达，按无条件写入（运行时可能提前出现）」。
/// </para>
/// <para>
/// ⚠ **`Data/Locations` 不在白名单**：无条件创建地点会把"还没解锁的地点"直接暴露在存档里，
/// 风险比物品/NPC 大。
/// </para>
/// </summary>
public static class ConditionalNewEntryCreation
{
    /// <summary>允许「新条目无条件化」的资产名（不含地点）。</summary>
    private static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data/Characters", "Data/Objects", "Data/BigCraftables", "Data/Weapons", "Data/Tools",
        "Data/Furniture", "Data/Crops", "Data/WildTrees", "Data/FruitTrees", "Data/FarmAnimals",
        "Data/Machines", "Data/Buildings", "Data/Shops", "Data/IncomingPhoneCalls",
        "Data/Powers", "Data/CraftingRecipes", "Data/CookingRecipes",
        // 这些资产是**被动数据**：条目存在不等于会触发（邮件要人送、话题要人加、
        // 动画描述要日程引用、任务要触发器启动、礼物偏好要 NPC 存在）。无条件写安全。
        "Data/Mail", "Data/ConversationTopics", "Data/animationDescriptions",
        "Data/NPCGiftTastes", "Data/Quests", "Data/Buffs", "Data/Trinkets",
        // ⚠ **不加** `Data/TriggerActions`（会无条件触发动作）、`Data/Events`（会触发剧情）、
        //   `Data/Festivals`（节日台词会串场）、`Data/SpecialOrders`（会提前上板）。
    };

    public static int Apply(List<ChangePatch> patches, DynamicTokenResolver tokens,
        Func<string, JObject?> existingFor, ICollection<string>? notes = null,
        Action<ChangePatch, string>? onNote = null)
    {
        int added = 0;

        // 同一资产里，若**不同补丁**用**仅大小写不同**的键写法（`Wizard_Sleep` vs `wizard_sleep`），
        // 不能再拆一份无条件条目，否则会写出重复键。**完全相同**的键不算冲突
        // （多个条件变体写同一键，最后一个胜出是确定化，Lavril 就是这种）。
        var spellings = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (ChangePatch p in patches)
        {
            if (!p.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)) continue;
            string t = Normalize(p.Target);
            if (t.Length == 0 || p.Raw["Entries"] is not JObject es) continue;
            foreach (JProperty e in es.Properties())
            {
                string key = t + "\u0000" + e.Name;
                if (!spellings.TryGetValue(key, out HashSet<string>? set))
                    spellings[key] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(e.Name);
            }
        }

        // **保持原始补丁顺序**：拆出的无条件补丁插在原补丁**之前**（原补丁还剩条目时）
        // 或原位置；倒序追加会让「后写的补丁」反而先应用（CP 是后者胜）。
        var result = new List<ChangePatch>(patches.Count);
        foreach (ChangePatch patch in patches)
        {
            if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(patch);
                continue;
            }
            string target = Normalize(patch.Target);
            // 日程是**特例**：只有当目标资产是**空对象**（`Load Assets/Blank.json` 打底）
            // 时才补条目 —— 否则会覆盖已有的日程、直接改 NPC 日常行为。
            bool schedule = target.StartsWith("Characters/schedules/", StringComparison.OrdinalIgnoreCase);
            // 对话同理：只补**空资产**（`Load` 一个空 JSON 再用条件加台词），不动已有对话。
            bool emptyOnly = schedule
                             || target.StartsWith("Characters/Dialogue/", StringComparison.OrdinalIgnoreCase);
            // `Strings/*` 是纯文本：新键无条件写安全（只在被引用时显示）。
            bool strings = target.StartsWith("Strings/", StringComparison.OrdinalIgnoreCase);
            if ((!Targets.Contains(target) && !emptyOnly && !strings)
                || patch.When == null || !patch.When.HasValues
                // ⚠ 有 `TargetField` 时 `Entries` 的键是**目标条目的字段名**（`BirthSeason`/`Price`…），
                //   不是新条目。把它当新条目拆开会把条件字段修改变成无条件的（实测 East Scarp）。
                || patch.Raw["TargetField"] != null
                || patch.Raw["Entries"] is not JObject entries)
            {
                result.Add(patch);
                continue;
            }

            bool? when = tokens.TryEvaluateWhen(patch.When);
            // `false` 分两种：**真的静态为假**（`HasMod` 不成立等）→ 跳过；
            // **运行时状态被冻结成 false**（`HasSeenEvent`/`HasFlag`…）→ 按"未知"处理，
            // 走无条件创建（否则 Apples 这类剧情 NPC 永远不会出现）。
            // ⚠ 混合条件里只要有一个**已知静态**子句为假就不能写；所以这里要求所有子句都是运行时。
            if (when == false && !RuntimeConditions.AllMayDependOnSave(patch.When, tokens))
            {
                result.Add(patch);
                continue;
            }

            JObject? existing = existingFor(target);
            if (emptyOnly && (existing == null || existing.HasValues))
            {
                result.Add(patch);
                continue;
            }

            var created = new JObject();
            foreach (JProperty entry in entries.Properties().ToList())
            {
                // 已存在 → 不许无条件覆盖。**大小写不敏感**：游戏的数据字典按不敏感处理，
                // `wizard_sleep` 与 `Wizard_Sleep` 是同一个键（实测 SVE 婚姻动画，写成两条会撞车）。
                if (existing != null && existing.Properties().Any(p =>
                        p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))) continue;
                // 别的补丁用**不同大小写**写过这个键 ⇒ 也不拆，避免重复键。
                if (spellings.TryGetValue(target + "\u0000" + entry.Name, out HashSet<string>? variants)
                    && variants.Count > 1)
                    continue;
                created[entry.Name] = entry.Value.DeepClone();
                entry.Remove();
            }
            if (created.Count == 0)
            {
                result.Add(patch);
                continue;
            }

            var raw = (JObject)patch.Raw.DeepClone();
            raw.Remove("When");
            raw["Entries"] = created;
            var split = new ChangePatch
            {
                SourceFile = patch.SourceFile,
                Index = patch.Index,
                Action = patch.Action,
                LogName = (patch.LogName ?? target) + "（新条目无条件化）",
                Target = patch.Target,
                FromFile = patch.FromFile,
                Update = patch.Update,
                When = null,
                Raw = raw,
                Priority = patch.Priority,
            };
            result.Add(split);
            if (entries.Count > 0) result.Add(patch);

            // `when == true` 是**静态可判为真**（例如 HasMod 已装），不是“表达不了”：
            // 报告说明不能自相矛盾（实测 Lavender：同一 HasMod 条件在别处已判真）。
            string reason = when == true
                ? "条件静态为真，按**无条件**写入"
                : "条件无法静态表达，按**无条件**写入（运行时可能提前出现）";
            onNote?.Invoke(split, reason);
            notes?.Add($"`{target}` 条件创建条目 "
                       + string.Join("`、`", created.Properties().Select(p => "`" + p.Name + "`"))
                       + "：" + reason);
            added += created.Count;
        }

        patches.Clear();
        patches.AddRange(result);
        return added;
    }

    /// <summary>资产名统一成 `/` 分隔、去首尾斜杠（CP/SMAPI 对 `\` 与 `/` 等价）。</summary>
    private static string Normalize(string? target) =>
        (target ?? "").Trim().Replace('\\', '/').Trim('/');
}
