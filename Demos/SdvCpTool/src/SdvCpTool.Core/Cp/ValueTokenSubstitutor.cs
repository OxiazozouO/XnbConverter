using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 把补丁**值**里**能精确解析**的令牌代成值：包内 <c>DynamicTokens</c>、配置令牌、
/// 以及 CP 内建的 <c>{{ModId}}</c>。
/// <para>
/// <b>为什么现在才做</b>：早先有一条注释写着「值里的 <c>{{…}}</c> 是另一回事，代掉会写坏对话文案」。
/// 那句话**过宽**了 —— <c>{{Random}}</c>、<c>{{i18n:…}}</c> 这些确实要按上下文解（我们不动），
/// 但**包内 DynamicTokens / 配置字段 / ModId 是上下文无关的**，CP 在值里**同样会展开**。
/// 不展开的后果是**静默**的（实测语料 32 包里 12 包中招、**575 处影响行为**）：
/// </para>
/// <list type="bullet">
/// <item><c>Condition</c> 里带令牌 ⇒ **不是合法 GSQ** ⇒ 游戏报错返假 ⇒ 条目**永远不生效**；</item>
/// <item><c>Sprite</c>/<c>Portrait</c>/<c>Texture</c> 里带令牌 ⇒ 取不到图 ⇒ 画不出来；</item>
/// <item>**事件键**里带令牌 ⇒ 前置条件段非法 ⇒ 事件**永不触发**。</item>
/// </list>
/// <para>
/// <b>两条不做</b>（都写在代码里，免得以后被"顺手扩展"）：
/// <list type="number">
/// <item><b>解析不了的令牌原样保留</b>（<c>{{Random}}</c>/<c>{{Spouse}}</c>/CP 内建的运行时令牌）——
///   展开结果是<b>猜</b>的，宁可少写。这些由 <c>UnexpandedTokenAudit</c> 继续报出来；</item>
/// <item><c>When</c> 与 <c>Target</c>/<c>FromFile</c> **不碰**：前者是条件（走 GSQ 那条路），
///   后者已由 <see cref="ConfigTokenSubstitutor"/> 处理。</item>
/// </list>
/// </para>
/// <para>
/// <b><c>Lossy</c> 一律拒绝</b>：<see cref="DynamicTokenResolver"/> 会在「条件判不了、
/// 落到无条件默认块」时标 <c>Lossy</c> —— 那是**猜出来的值**，写进产物就是把语义改掉。
/// </para>
/// </summary>
public static class ValueTokenSubstitutor
{
    /// <summary>这些字段不碰（条件走 GSQ 那条路；路径字段已另有处理）。</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "When", "Target", "FromFile", "LogName",
    };

    /// <summary>就地替换；返回被改动的<b>字符串处数</b>。</summary>
    /// <param name="onNote">
    /// 每条补丁的**确定化说明**（目前只有 <c>{{Random}}</c> 取第一个备选）—— 逐条报出来，
    /// 由调用方记进那条补丁的报告里。传 <c>null</c> 时只做替换、不报。
    /// </param>
    public static int Apply(IEnumerable<ChangePatch> patches, DynamicTokenResolver resolver, string? modId,
        Action<ChangePatch, string>? onNote = null)
    {
        int changed = 0;
        foreach (ChangePatch patch in patches)
        {
            var notes = new List<string>();
            changed += FreezeRomanceFlags(patch, resolver, notes);
            changed += Walk(patch.Raw, resolver, modId, notes);
            // 顺序：先代令牌，再删**代不掉**的条件变体，最后把 CP 布尔折成合法 GSQ。
            changed += SanitizeCharacterConditions(patch, notes);
            changed += FoldBooleanConditions(patch);
            foreach (string note in notes.Distinct())
                onNote?.Invoke(patch, note);
        }
        return changed;
    }

    /// <summary>
    /// **婚姻目标**：`Data/Characters` 条目里的 <c>CanBeRomanced</c> 若挂着**判不了**的关系状态令牌，
    /// 但同一条目**带 `SpouseRoom`**，就冻结为 <c>true</c>。
    /// <para>
    /// 语料依据（2026-10-18）：S&amp;S 的 Mateo/Hector/Cirrus 与 Bear Family 的 Sigurd/Gunnar/Teren
    /// 都把 `CanBeRomanced` 写成 <c>{{XRelationshipStatus}}</c>。该令牌的默认块是 <c>false</c>，
    /// 只有一条 `HasFlag &lt;XRomanticFlag&gt;` 的块是 <c>true</c>；存档旗标离线不可知 ⇒ 解析器标
    /// <c>Lossy</c> ⇒ 整条 `EditData` 被拒（NPC 根本不存在）。纯 XNB 没有「运行时按旗标改可婚状态」的机制，
    /// 只能冻结一个值；这些条目**带自定义婚房**，婚姻意图明确 ⇒ 取 <c>true</c>。
    /// </para>
    /// <para>
    /// ⚠ 只对「带 `SpouseRoom.MapAsset`」的条目生效：Hurst / Nora / Rodney / Cambria 同样用了关系状态令牌，
    /// 但**没有任何婚房资产**（只有约会内容），冻结成可婚会让玩家娶进 Abigail 的默认房间。
    /// </para>
    /// </summary>
    private static int FreezeRomanceFlags(ChangePatch patch, DynamicTokenResolver resolver, List<string> notes)
    {
        if (!TargetsCharacters(patch.Target)) return 0;
        if (patch.Raw["Entries"] is not JObject entries) return 0;

        int changed = 0;
        foreach (JProperty npc in entries.Properties())
        {
            if (npc.Value is not JObject entry) continue;
            if (entry["CanBeRomanced"] is not JValue { Type: JTokenType.String } value) continue;
            string text = value.Value<string>() ?? "";
            if (TokenScan.Unresolved(text).Count == 0) continue;
            if (entry["SpouseRoom"] is not JObject room) continue;
            if (room["MapAsset"] is not JValue { Type: JTokenType.String } map
                || string.IsNullOrWhiteSpace(map.Value<string>())) continue;

            DynamicTokenResolver.Expansion e = resolver.Expand(text);
            bool unfrozen = TokenScan.Unresolved(e.Text).Count > 0;
            bool localToken = e.Unresolved.Any(n => resolver.Names.Contains(n, StringComparer.OrdinalIgnoreCase));
            if (e.Lossy.Count == 0 && !(unfrozen && localToken)) continue;   // 能精确解出来的照常走

            entry["CanBeRomanced"] = true;   // 必须是布尔：字符串 "true" 原版不认
            notes.Add($"`{npc.Name}.CanBeRomanced` 的关系状态令牌 `{text}` 离线不可判，"
                      + "该条目带婚房 ⇒ 冻结为 `true`（纯 XNB 无运行时改可婚状态的机制）");
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// `Data/Characters` 条目里**代不掉的条件令牌**（`Appearance[].Condition` / `Home[].Condition`）：
    /// 删掉那一条变体，别让整条 NPC 一起陪葬。
    /// <para>
    /// ⚠ 必须在 <see cref="Walk"/> **之后**跑：能代的条件令牌（配置、包内动态）已经代成文本，
    /// 这里只处理真正的残渣。原版这些字段是 **GSQ**，而 CP 令牌代进去以后通常不是合法 GSQ
    /// （`{{Query: {{Year}} % 2 = 1}}` 代出来是算式）。留着既不会生效又刷报错日志。
    /// </para>
    /// <para>
    /// 实测（Bear Family）：Sigurd/Gunnar/Teren 的条目里有
    /// `Condition: "IS_EVENT festival_fall27, {{Query: {{Year}} % 2 = 1 }}"` 等 4 处，
    /// 整条 `EditData` 因「值里含无法静态展开的令牌」被拒 ⇒ 三个 NPC 从 `Data/Characters` 消失。
    /// </para>
    /// </summary>
    private static int SanitizeCharacterConditions(ChangePatch patch, List<string> notes)
    {
        if (!TargetsCharacters(patch.Target)) return 0;
        if (patch.Raw["Entries"] is not JObject entries) return 0;

        static bool HasTokenCondition(JObject o) => o.Properties().Any(p =>
            p.Name.Equals("Condition", StringComparison.OrdinalIgnoreCase)
            && p.Value.Type == JTokenType.String
            && TokenScan.Unresolved(p.Value.Value<string>() ?? "").Count > 0);

        int removed = 0;

        // 形态 A：Entries 是 NPC 条目本身（`Entries: { "Eyvinder": { "Appearance": [...] } }`）。
        foreach (JProperty npc in entries.Properties())
        {
            if (npc.Value is not JObject entry) continue;
            foreach (string field in new[] { "Appearance", "Home" })
            {
                if (entry[field] is not JArray variants) continue;
                JObject? fallback = null;
                for (int i = variants.Count - 1; i >= 0; i--)
                {
                    if (variants[i] is not JObject o || !HasTokenCondition(o)) continue;
                    fallback ??= (JObject)o.DeepClone();
                    variants.RemoveAt(i);
                    removed++;
                }
                // 全部变体都被删光 ⇒ 至少留一条（去掉条件），否则这个 NPC **没有任何外观/住所**。
                if (variants.Count == 0 && fallback != null)
                {
                    fallback.Remove("Condition");
                    variants.Add(fallback);
                    notes.Add($"`{npc.Name}.{field}` 的变体条件全部代不掉，保留第一条并去掉条件"
                              + "（外观/住所会固定成这一种）");
                }
            }
        }

        // 形态 B：`TargetField: [ "<NPC>", "Appearance" ]`，Entries 的值**就是**变体对象
        // （实测 `[CP] Seasonal Cute Characters East Scarp`）。
        if (TargetsVariantField(patch))
        {
            var fallback = new List<(string Key, JObject Value)>();
            foreach (JProperty prop in entries.Properties().ToList())
            {
                if (prop.Value is not JObject o || !HasTokenCondition(o)) continue;
                fallback.Add((prop.Name, (JObject)o.DeepClone()));
                entries.Remove(prop.Name);
                removed++;
            }
            if (entries.Count == 0 && fallback.Count > 0)
            {
                (string key, JObject value) = fallback[0];
                value.Remove("Condition");
                entries[key] = value;
                notes.Add($"`TargetField` 的变体条件全部代不掉，保留第一条并去掉条件"
                          + "（外观/住所会固定成这一种）");
            }
        }
        return removed;
    }

    /// <summary>`TargetField` 是否直接指向 `Appearance` / `Home` 数组。</summary>
    private static bool TargetsVariantField(ChangePatch patch)
    {
        if (patch.Raw["TargetField"] is not JArray path || path.Count == 0) return false;
        string last = path[path.Count - 1]?.Value<string>() ?? "";
        return last.Equals("Appearance", StringComparison.OrdinalIgnoreCase)
               || last.Equals("Home", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 这些字段**允许有损确定化**（条件判不了时用默认块）：语料里 `Home[].Location` / `Tile.X/Y`
    /// 写成 `{{GunnarMap}}` 这类「按存档旗标选地图」的令牌。纯 XNB 没有运行时切换机制，
    /// 取默认块（无额外成员时的住所）是唯一可写方案；不取的话整条 NPC 消失。
    /// </summary>
    private static bool ToleratesLossy(string name, JObject parent)
    {
        if (name.Equals("MapAsset", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Location", StringComparison.OrdinalIgnoreCase)) return true;
        // `Data/Characters` 的**枚举字段**：取值来自包作者写的闭集（Polite/Rude、Male/Female…）。
        // 条件判不了时落到默认块只是"选了闭集里的另一个合法值"，比整条 NPC 不写好。
        // 实测 `[CP] Lumisteria Mt Vapius` 的 Aster：`Manner: {{PolitenessAster}}`（Polite/Rude）
        // 判不了 ⇒ 整个 NPC 从 Data/Characters 消失。
        if (CharacterEnumFields.Contains(name)) return true;
        return (name.Equals("X", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Y", StringComparison.OrdinalIgnoreCase))
               && parent.Property("X") != null && parent.Property("Y") != null;
    }

    /// <summary><c>CharacterData</c> 里取值是闭集枚举的字段（见反编译的 `StardewValley.GameData`）。</summary>
    private static readonly HashSet<string> CharacterEnumFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Gender", "Age", "Manner", "SocialAnxiety", "Optimism", "HomeRegion",
        "Calendar", "SocialTab", "EndSlideShow",
    };

    /// <summary>
    /// 把 `Data/Characters` 条目里的 **CP 布尔字面量**折成合法 GSQ：
    /// `SEASON Summer, true`（`{{FestivalOutfits}}` 代出来的）里那个 `true` 不是 GSQ
    /// （游戏只认大写的 `TRUE`/`FALSE`，`GameStateQuery.cs:2076`）⇒ 整条条件判假、外观永不生效。
    /// <list type="bullet">
    /// <item>`true` 子句 ⇒ 删掉（恒真）；</item>
    /// <item>`false` 子句 ⇒ 整条变体删除（恒假）；</item>
    /// <item>`!true` ⇒ 删变体；`!false` ⇒ 删子句。</item>
    /// </list>
    /// 只动 `Appearance[]` / `Home[]` 的 `Condition`。
    /// </summary>
    private static int FoldBooleanConditions(ChangePatch patch)
    {
        if (!TargetsCharacters(patch.Target)) return 0;
        if (patch.Raw["Entries"] is not JObject entries) return 0;

        int changed = 0;

        // 返回 true = 整条变体恒假，调用方删掉它。
        bool Fold(JObject variant)
        {
            if (variant["Condition"] is not JValue { Type: JTokenType.String } cond) return false;
            string original = cond.Value<string>() ?? "";
            string text = original;
            if (!text.Contains("true", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("false", StringComparison.OrdinalIgnoreCase)) return false;

            // `ANY "query, true" "query2"` —— 引号里的 GSQ 也要折；`true` 子句恒真，直接删。
            text = Regex.Replace(text, @",\s*true\b", "", RegexOptions.IgnoreCase);

            var kept = new List<string>();
            foreach (string clause in SplitConditionClauses(text))
            {
                string c = clause.Trim();
                if (c.Equals("true", StringComparison.OrdinalIgnoreCase)) continue;
                if (c.Equals("false", StringComparison.OrdinalIgnoreCase)) { changed++; return true; }
                if (c.Equals("!true", StringComparison.OrdinalIgnoreCase)) { changed++; return true; }
                if (c.Equals("!false", StringComparison.OrdinalIgnoreCase)) continue;
                kept.Add(c);
            }

            string folded = kept.Count == 0 ? "TRUE" : string.Join(", ", kept);
            if (folded == original) return false;
            cond.Value = folded;
            changed++;
            return false;
        }

        // 形态 A：Entries 是 NPC 条目本身。
        foreach (JProperty npc in entries.Properties())
        {
            if (npc.Value is not JObject entry) continue;
            foreach (string field in new[] { "Appearance", "Home" })
            {
                if (entry[field] is not JArray variants) continue;
                for (int i = variants.Count - 1; i >= 0; i--)
                    if (variants[i] is JObject variant && Fold(variant)) variants.RemoveAt(i);
            }
        }

        // 形态 B：TargetField 直接指向 Appearance/Home，Entries 的值就是变体对象。
        if (TargetsVariantField(patch))
        {
            foreach (JProperty prop in entries.Properties().ToList())
                if (prop.Value is JObject variant && Fold(variant)) entries.Remove(prop.Name);
        }
        return changed;
    }

    /// <summary>按逗号切条件子句，**引号里的逗号不切**（`ANY "a, b" "c"`）。</summary>
    private static List<string> SplitConditionClauses(string text)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"') quoted = !quoted;
            if (c == ',' && !quoted) { parts.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        parts.Add(sb.ToString());
        return parts;
    }

    private static bool TargetsCharacters(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        return target.Split(',')
            .Any(t => t.Trim().Equals("Data/Characters", StringComparison.OrdinalIgnoreCase));
    }

    private static int Walk(JToken node, DynamicTokenResolver resolver, string? modId, List<string> notes,
        bool allowLossy = false, JObject? root = null)
    {
        root ??= node as JObject;
        switch (node)
        {
            case JObject obj:
            {
                int changed = 0;
                // 键也要代：`Data/Events` 的事件键里就有 `{{…}}`（实测事件键那档 227 处）。
                foreach (JProperty p in obj.Properties().ToList())
                {
                    // ⚠ 只跳过**补丁根**上的 When/Target/FromFile —— 嵌套的 `TextOperations[].Target`
                    //   是普通的键路径，不代就会把 `{{FestivalYearCheck}}` 原样写进产出键
                    //   （实测 10 个节日文件：`Set-Up_additionalCharacters{{FestivalYearCheck}}`）。
                    if (ReferenceEquals(obj, root) && Skipped.Contains(p.Name)) continue;

                    // 键名允许**有损默认值**：留着裸令牌一定是坏键；默认块至少是 CP 在“条件不成立”
                    // 那条世界线里的真实取值。
                    string? newKey = Expand(p.Name, resolver, modId, notes, allowLossy: true);
                    // ⚠ **展开后的键名可能撞车**（原键 A 展开成 B，而同对象里已经有 B）。
                    //   直接 `obj[newKey] = value` 会把**已有那条覆盖掉** —— 静默丢数据。
                    //   撞车时不改名：原键带着令牌留下，`UnexpandedTokenAudit` 会把它报出来。
                    //   **宁可留着可见的令牌，也不静默丢一条数据。**
                    // 大小写不敏感：`Name` 与 `name` 在游戏反序列化里是同一个键。
                    if (newKey != null && newKey != p.Name
                        && !obj.Properties().Any(q => !ReferenceEquals(q, p)
                                                      && q.Name.Equals(newKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        JToken value = p.Value;
                        p.Remove();
                        obj[newKey] = value;
                        changed++;
                        // 换过键之后要拿新属性继续往下走
                        changed += Walk(obj[newKey]!, resolver, modId, notes, allowLossy, root);
                        continue;
                    }
                    // 条件/键路径字段（`TextOperations` 等）里的令牌允许有损默认值：
                    // 裸令牌写进产出键或路径，比"选了默认块"糟得多。
                    bool pathField = p.Name.Equals("TextOperations", StringComparison.OrdinalIgnoreCase)
                                     || p.Name.Equals("MoveEntries", StringComparison.OrdinalIgnoreCase)
                                     || p.Name.Equals("TargetField", StringComparison.OrdinalIgnoreCase);
                    changed += Walk(p.Value, resolver, modId, notes,
                        allowLossy || pathField || ToleratesLossy(p.Name, obj), root);
                }
                return changed;
            }

            case JArray arr:
            {
                int changed = 0;
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i].Type == JTokenType.String)
                    {
                        string? done = Expand(arr[i].Value<string>()!, resolver, modId, notes, allowLossy);
                        if (done != null) { arr[i] = done; changed++; }
                    }
                    else
                    {
                        changed += Walk(arr[i], resolver, modId, notes, allowLossy, root);
                    }
                }
                return changed;
            }

            case JValue { Type: JTokenType.String } v:
            {
                string? done = Expand(v.Value<string>()!, resolver, modId, notes, allowLossy);
                if (done == null) return 0;
                node.Replace(new JValue(done));
                return 1;
            }

            default:
                return 0;
        }
    }

    /// <summary>
    /// 展开一处字符串；<b>不该改时返回 <c>null</c></b>（调用方保持原样）。
    /// </summary>
    private static string? Expand(string text, DynamicTokenResolver resolver, string? modId, List<string> notes,
        bool allowLossy = false)
    {
        if (text.Length == 0 || !text.Contains("{{", StringComparison.Ordinal)) return null;

        // `{{ModId}}` 是 CP 内建，取值就是本包的 UniqueID（`manifest.json`）——**精确**，先代掉。
        // ⚠ **大小写不敏感**：CP 的令牌表是 `InvariantDictionary`（大小写不敏感），
        //   `{{ModID}}` 与 `{{ModId}}` 是同一个令牌。实测 SVE 写的正是 `{{ModID}}`
        //   （`data/TriggerActions` 的条目键），精确替换会漏掉它 ⇒ 整条被误判「代不掉的令牌」。
        string work = modId == null
            ? text
            : text.Replace("{{ModId}}", modId, StringComparison.OrdinalIgnoreCase);

        DynamicTokenResolver.Expansion e = resolver.Expand(work);

        // 有 `Lossy` = 有的令牌是**猜**出来的（条件判不了、落到无条件默认块）⇒ 整处不动。
        // ★ 例外：**结果只剩 `{{i18n:…}}`** 时放行 —— 那是译文通道的键，写进产物由
        //   `TranslationApplier` 展开；猜的只是「用哪个 i18n 键」。实测 S&S 的 `TrueName`：
        //   `{{i18n:Core.Hector.Name}}`（默认）vs `{{i18n:Core.Biróg.Name}}`（看过某事件后），
        //   整条 Hector 条目因为这一个显示名被拒 ⇒ NPC 根本不存在。
        if (e.Lossy.Count > 0)
        {
            bool i18nOnly = e.Text.Contains("{{i18n:", StringComparison.OrdinalIgnoreCase)
                            && !TokenScan.HasUnresolved(e.Text);
            bool tolerated = allowLossy && !TokenScan.HasUnresolved(e.Text);
            // ★ **布尔字段**：关系/剧情令牌的默认块常常就是 `true`/`false`（实测 Hurst：
            //   `CanBeRomanced: {{HurstRomanceStatus}}`、`ItemDeliveryQuests: {{HurstSocialStatus}}`…）。
            //   原先把有损一律拒 ⇒ **整个 NPC 条目**不写（`Data/Characters` 里根本没有 Hurst），
            //   而纯 XNB 里"少一个 NPC"比"按默认块少一项行为"糟得多。
            //   只放行**结果恰好是布尔字面量**的值：其它类型（贴图路径、事件键）仍不猜。
            bool booleanLiteral = e.Text.Equals("true", StringComparison.OrdinalIgnoreCase)
                                  || e.Text.Equals("false", StringComparison.OrdinalIgnoreCase);
            if (!i18nOnly && !tolerated && !booleanLiteral) return null;
            if ((tolerated && !i18nOnly) || booleanLiteral)
                notes.Add("值里的关系/地图令牌离线不可判，按**无条件默认块**确定化"
                          + $"（`{string.Join("`、`", e.Lossy)}`）");
        }

        // `Determinized`（`{{Random}}` 取第一个备选）**不算猜**：每个备选都是 CP 可能产出的值，
        // 只是不再随存档/时点变化。照常代掉，但**必须报出来**。
        foreach (string token in e.Determinized)
            notes.Add($"值里的 `{{{{{token}}}}}` 已**确定化**：取了第一个备选（CP 每次上下文更新会重掷，取值随存档与时点变）");

        return e.Text == text ? null : e.Text;
    }

    /// <summary>
    /// 这条补丁**写出去的值**里，还有没有**没展开的** CP 令牌。
    /// <para>
    /// <b>用途</b>：`UnexpandedTokenAudit` 是**资产级**的 —— 它能说「这个文件里有 3 处」，
    /// 但说不出「是**哪一条补丁**带进来的」。而在写入路径上我们能直接问这条补丁，
    /// 于是可以把它记进**逐条记录**（`writeNote`）⇒ 报告里能精确点到补丁，
    /// 「要不要为它们整条不写」这笔账也就不用再拿上界估。
    /// </para>
    /// <para>
    /// ⚠ <c>{{i18n:…}}</c> **不算**：那是通道 B1 自己处理的键，`TranslationApplier` 写盘时展开。
    /// 其余令牌（`Random`/`Spouse`/别的 mod 的 C# 令牌…）只要能留到这里，就是**解析不了**的。
    /// </para>
    /// </summary>
    public static bool HasUnresolvedToken(ChangePatch patch) => HasToken(patch.Raw, skipPatchFields: true);

    /// <summary>
    /// 任意数据里有没有没展开的 CP 令牌。
    /// <para>
    /// <b>为什么要有「不跳字段」这一档</b>：`Load` 把**文件内容**整个拷进资产 ——
    /// 令牌可能只在**那个文件**里（实测语料剩的 13 处全是这种：`Data/Events` 的事件键里带
    /// `{{FarmName}}`/`{{PlayerName}}`）。补丁的值里一处都看不到，只查补丁会漏掉它们。
    /// 而被拷进来的内容是**数据**，`When`/`Target` 这些名字在那里面没有「补丁字段」的含义，不该跳。
    /// </para>
    /// </summary>
    public static bool HasUnresolvedTokenIn(JToken data) => HasToken(data, skipPatchFields: false);

    /// <summary>
    /// 这条补丁写出去的值里，**还没展开的令牌名**（去重）—— 给报告用，见
    /// <see cref="TokenScan.Unresolved"/> 的说明：112 条被这闸拦下的补丁原先看不出卡在哪个令牌上。
    /// </summary>
    public static IReadOnlyList<string> UnresolvedTokens(ChangePatch patch) => Tokens(patch.Raw, skipPatchFields: true);

    /// <summary>
    /// 把 <c>Entries</c> 分成「写得出去」与「写不出去（键或值里含代不掉的令牌）」两组键名。
    /// <para>
    /// <b>为什么要逐条</b>：CP 的 <c>ApplyRecords</c> 就是**逐条** <c>WarnForRecord(…)</c>
    /// 后 <c>continue</c>（<c>EditDataPatch.cs:13242+</c>），取舍粒度是**条目**。
    /// 整条不写会把一条坏条目放大成上千条 —— 实机症状：SVE 的 <c>Strings/StringsFromMaps</c>
    /// 一条补丁 1037 个条目，只有 1 个带 <c>{{FarmName}}</c>，结果另 1036 条全丢，
    /// 玩家在蓝月酒庄地图上看到裸键 <c>Strings\StringsFromMaps:BlueMoonVineyard.7</c>。
    /// </para>
    /// </summary>
    public static (List<string> Writable, List<string> Unresolved) SplitEntries(JObject? entries)
    {
        var ok = new List<string>();
        var bad = new List<string>();
        if (entries == null) return (ok, bad);

        foreach (JProperty p in entries.Properties())
        {
            bool dirty = TokenScan.Unresolved(p.Name).Count > 0 || HasUnresolvedTokenIn(p.Value);
            (dirty ? bad : ok).Add(p.Name);
        }
        return (ok, bad);
    }

    /// <summary>
    /// **除 <c>Entries</c> 之外**还有没有代不掉的令牌。
    /// <para><c>Entries</c> 能逐条取舍；<c>Fields</c>/<c>MoveEntries</c>/<c>TextOperations</c>
    /// 不行 —— 落点被坏令牌污染时只能整条不写（沿用原来的规矩）。</para>
    /// </summary>
    public static bool HasUnresolvedTokenOutsideEntries(ChangePatch patch)
    {
        if (patch.Raw["Entries"] is not JObject) return HasUnresolvedToken(patch);

        JObject copy = (JObject)patch.Raw.DeepClone();
        copy.Remove("Entries");
        return HasToken(copy, skipPatchFields: true);
    }

    private static IReadOnlyList<string> Tokens(JToken node, bool skipPatchFields)
    {
        var names = new List<string>();
        WalkTokens(node, skipPatchFields, names);
        return names.Distinct(StringComparer.Ordinal).Take(5).ToList();

        static void WalkTokens(JToken node, bool skipPatchFields, List<string> names)
        {
            switch (node)
            {
                case JObject obj:
                    foreach (JProperty p in obj.Properties())
                    {
                        if (skipPatchFields && Skipped.Contains(p.Name)) continue;
                        names.AddRange(TokenScan.Unresolved(p.Name));
                        if (p.Value.Type == JTokenType.String)
                            names.AddRange(TokenScan.Unresolved(p.Value.Value<string>() ?? ""));
                        else
                            WalkTokens(p.Value, skipPatchFields, names);
                    }
                    break;
                case JArray arr:
                    foreach (JToken item in arr)
                    {
                        if (item.Type == JTokenType.String) names.AddRange(TokenScan.Unresolved(item.Value<string>() ?? ""));
                        else WalkTokens(item, skipPatchFields, names);
                    }
                    break;
            }
        }
    }

    private static bool HasToken(JToken node, bool skipPatchFields)
    {
        switch (node)
        {
            case JObject obj:
                foreach (JProperty p in obj.Properties())
                {
                    if (skipPatchFields && Skipped.Contains(p.Name)) continue;

                    // ⚠ **键名本身也要查** —— 实测踩过：13 处未展开令牌**全在键名里**
                    //   （`Data/Events` 的事件键，如 `59443124/O Shane/f Shane 3250{{PlayerName}}`）。
                    //   第一版只查了值，于是 `Load` 那道闸一次都没拦住它们。
                    if (HasTokenText(p.Name)) return true;

                    if (p.Value.Type == JTokenType.String)
                    {
                        if (HasTokenText(p.Value.Value<string>() ?? "")) return true;
                    }
                    else if (HasToken(p.Value, skipPatchFields))
                    {
                        return true;
                    }
                }
                return false;

            case JArray arr:
                foreach (JToken item in arr)
                {
                    if (item.Type == JTokenType.String)
                    {
                        if (HasTokenText(item.Value<string>() ?? "")) return true;
                    }
                    else if (HasToken(item, skipPatchFields))
                    {
                        return true;
                    }
                }
                return false;

            case JValue { Type: JTokenType.String } v:
                // ⚠ 裸字符串也要查 —— 少了这一支，`HasUnresolvedTokenIn(某个 JValue)` 会**永远为 false**。
                //   实测踩过：逐条取舍 `Entries` 时传进来的正是条目的值（一个 JValue），
                //   于是「坏条目」一个都认不出来，那次改动形同没做。
                return HasTokenText(v.Value<string>() ?? "");

            default:
                return false;
        }
    }

    /// <summary>
    /// 走 <see cref="TokenScan.HasUnresolved"/>（判定与写入共用的**唯一**扫描实现）。
    /// <para>
    /// ⚠ 先前这里自己写了一份「找第一个 <c>}}</c>」—— 与 <c>TranslationApplier</c> 里
    /// <b>早已正确</b>的配对实现**不一致**，于是 <c>{{i18n:KEY |PlayerName={{PlayerName}}}}</c>
    /// 被当成纯 i18n 整个放行，代不掉的 <c>{{PlayerName}}</c> 随译文写进产物。
    /// </para>
    /// </summary>
    private static bool HasTokenText(string text) => TokenScan.HasUnresolved(text);
}
