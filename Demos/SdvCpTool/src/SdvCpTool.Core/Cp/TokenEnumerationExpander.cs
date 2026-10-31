using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// **枚举展开**：把「按季节分块的动态令牌」拆成 N 条**带互斥季节条件**的补丁。
/// <para>
/// <b>为什么需要它</b>：CP 的 <c>DynamicTokens</c> 可以同名多块、各带 <c>When</c>，
/// 运行时「最后一个匹配块胜出」。这种令牌出现在 <c>FromFile</c> 里时，我们**静态代不掉**
/// —— 于是整条补丁被拒。实测（SVE 季节性服装，全语料 9 条 / **约 24 个 NPC**）：
/// </para>
/// <code>
/// { "Name": "Aerobics", "Value": "Spring" },
/// { "Name": "Aerobics", "Value": "Summer",       "When": { "Season": "Summer" } },
/// { "Name": "Aerobics", "Value": "Fall",         "When": { "Season": "Fall" } },
/// { "Name": "Aerobics", "Value": "Winter_Indoor","When": { "Season": "Winter" } },
///
/// { "Action": "EditImage", "Target": "Characters/Andy",
///   "FromFile": "assets/{{Target}}/{{TargetWithoutPath}}_{{Aerobics}}.png" }
/// </code>
/// <para>
/// 拆成 4 条（<c>Season</c> 各自一条 + 默认那条取**补集**）之后，走的就是**现成的**
/// 四季/立绘发射器 —— 与这个包里其它 NPC 走的是同一条路（那批本来就已落地）。
/// </para>
/// <para>
/// <b>只在形状**能被证明是互斥**时才拆</b>，任何一条前提不成立就整条不拆（保持原判）：
/// <list type="number">
/// <item>条件块<b>只有一个键</b>且键名是 <c>Season</c>、值是四季之一 —— 这是**互斥**的唯一依据；</item>
/// <item>各条件块的季节<b>两两不同</b>；</item>
/// <item>无条件块<b>至多一块、且必须在所有条件块之前</b> —— CP 是「最后一个匹配块胜出」，
///   无条件块要是排在后面，条件块就永远轮不上，拆开就**语义反了**；</item>
/// <item>块的值用 <see cref="DynamicTokenResolver.Expand"/> 能**干净**展开（无未解析、无有损、无确定化）；</item>
/// <item>一条补丁里**只有一个**这种令牌（两个就说明按季节分的不止一个维度，不猜）。</item>
/// </list>
/// </para>
/// </summary>
public static class TokenEnumerationExpander
{
    /// <summary>四季（与游戏 <c>Season</c> 枚举一致）。</summary>
    private static readonly string[] Seasons = { "Spring", "Summer", "Fall", "Winter" };

    /// <summary>文本里的 <c>{{令牌}}</c>。取到内层花括号为止（i18n 那类嵌套不在这里处理）。</summary>
    private static readonly Regex TokenPattern = new(@"\{\{([^{}]+)\}\}", RegexOptions.Compiled);

    /// <summary>展开结果：拆了几条原始补丁、净增几条、以及给人看的说明。</summary>
    public readonly record struct Result(int Expanded, int Added, IReadOnlyList<string> Notes);

    /// <summary>一个可枚举的令牌：名字 + 它的块（默认块在前）。</summary>
    private sealed record Candidate(string Name, List<string> Values, string DefaultValue, string[] CoveredSeasons);

    /// <summary>
    /// 就地把 <paramref name="patches"/> 里**能枚举**的补丁拆开（顺序不变，拆出来的接在它后面）。
    /// 返回拆了几条、净增几条、以及每条一句说明（调用方打印/进报告）。
    /// </summary>
    public static Result Apply(List<ChangePatch> patches, DynamicTokenResolver tokens)
    {
        var notes = new List<string>();
        int expanded = 0, added = 0;

        for (int i = 0; i < patches.Count; i++)
        {
            ChangePatch patch = patches[i];
            if (FindCandidate(patch, tokens) is not Candidate candidate) continue;

            List<ChangePatch> replacements = Build(patch, candidate, tokens, out string? failure);
            if (replacements.Count == 0)
            {
                // 只在"本来会拆、结果没拆成"时出声；形状不对的静默跳过（那是常态）
                if (failure != null) notes.Add($"{Name(patch)}：`{{{{{candidate.Name}}}}}` 没能枚举展开 —— {failure}");
                continue;
            }

            patches.RemoveAt(i);
            patches.InsertRange(i, replacements);
            expanded++;
            added += replacements.Count - 1;
            notes.Add($"{Name(patch)}：`{{{{{candidate.Name}}}}}` 按季节枚举展开 → {replacements.Count} 条"
                      + $"（{string.Join(" / ", replacements.Select(Files))})");
            i += replacements.Count - 1;
        }

        return new Result(expanded, added, notes);
    }

    private static string Name(ChangePatch patch) =>
        patch.LogName ?? patch.Target ?? Path.GetFileName(patch.SourceFile);

    private static string Files(ChangePatch patch) => Path.GetFileName(patch.FromFile ?? "");

    /// <summary>
    /// 这条补丁里有没有**恰好一个**可枚举的令牌。<c>Target</c> 与 <c>FromFile</c> 都要看 ——
    /// 令牌出现在哪一个字段上都一样能枚举（两块字段一起代）。
    /// </summary>
    private static Candidate? FindCandidate(ChangePatch patch, DynamicTokenResolver tokens)
    {
        if (tokens.IsEmpty) return null;

        var found = new List<Candidate>();
        var texts = new List<string>();
        if (!string.IsNullOrEmpty(patch.FromFile)) texts.Add(patch.FromFile);
        if (!string.IsNullOrEmpty(patch.Target)) texts.Add(patch.Target);

        // ★ **`When` 里也要看** —— 实测（新语料 94 条，East Scarp 的动画立绘包）：
        //   `Target: Portraits/Aideen_{{season}}` + `When: { "HasFile:assets/Aideen/Aideen_{{Season}}.png": true }`
        //   令牌**只出现在条件里**时，以前这里看不见 ⇒ 不拆 ⇒ 条件里的令牌留着 ⇒
        //   "文件在不在"判不了 ⇒ 整条退回 `NeedsReview`（理由还写着"需提供 --mods"，而 `--mods` 明明给了）。
        //   拆了之后同一个值由 `Patch()` 的**深度替换**贯穿全条（含 `When` 的键与值）。
        if (patch.When != null)
            foreach (Newtonsoft.Json.Linq.JProperty prop in patch.When.Properties())
            {
                texts.Add(prop.Name);                       // `HasFile:<路径>` 这种**键**里也有令牌
                if (prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    texts.Add(prop.Value.Value<string>() ?? "");
            }

        foreach (string text in texts)
        {
            foreach (string field in FieldTexts(text))
                foreach (Match m in TokenPattern.Matches(field))
                    if (CandidateShape(m.Groups[1].Value, tokens) is Candidate c && !found.Any(x => x.Name == c.Name))
                        found.Add(c);
        }

        return found.Count == 1 ? found[0] : null;   // 两个以上：不猜
    }

    private static IEnumerable<string> FieldTexts(string? text)
    {
        if (!string.IsNullOrEmpty(text)) yield return text;
    }

    /// <summary>
    /// 形状判据（见类注释的五条）。<c>tokens</c> 里没有这个名字、或块值展不干净，都返回 <c>null</c>。
    /// </summary>
    private static Candidate? CandidateShape(string inner, DynamicTokenResolver tokens)
    {
        // 带参数的写法（`X |contains=…`）不在这里处理 —— 枚举的是「裸令牌」
        string name = inner.Trim();
        if (name.Length == 0 || name.Contains('|') || name.Contains(':')) return null;
        if (name.StartsWith("i18n", StringComparison.OrdinalIgnoreCase)) return null;

        if (tokens.BlocksOf(name) is not List<(string Value, JObject? When)> blocks) return null;
        if (blocks.Count < 2 || blocks.Count > Seasons.Length + 1) return null;

        bool seenConditional = false;
        string? defaultValue = null;
        var values = new List<string>();
        var seasons = new List<string>();

        foreach ((string value, JObject? when) in blocks)
        {
            if (when == null)
            {
                if (seenConditional) return null;        // 无条件块排在后面 ⇒ 语义会反
                if (defaultValue != null) return null;   // 两个无条件块 ⇒ 后一个才生效，别猜
                defaultValue = CleanValue(value, tokens);
                if (defaultValue == null) return null;
                continue;
            }

            seenConditional = true;
            if (when.Properties().Count() != 1) return null;
            JProperty p = when.Properties().First();
            if (!p.Name.Equals("Season", StringComparison.OrdinalIgnoreCase)) return null;
            if (p.Value.Type != JTokenType.String) return null;

            string season = Seasons.FirstOrDefault(s =>
                s.Equals(p.Value.Value<string>()?.Trim(), StringComparison.OrdinalIgnoreCase))!;
            if (season == null || seasons.Contains(season)) return null;

            string? clean = CleanValue(value, tokens);
            if (clean == null) return null;

            seasons.Add(season);
            values.Add(clean);
        }

        if (values.Count == 0) return null;
        string[] covered = seasons.ToArray();

        // 四季全被条件块占满 ⇒ 默认块永远轮不上，丢掉它（不是损失，是 CP 语义）
        if (covered.Length == Seasons.Length)
            return new Candidate(name, values, "", covered);

        // ⚠ 剩下这两点是**证明不了就别拆**（审查逮到过一次 NRE）：
        //   ① **没有无条件默认块**时，未被占用的那几季里这个令牌**根本没有值** ——
        //      CP 那边它代成什么并不确定，硬拆就是把"不确定"当"空串"写出去；
        //   ② 默认块的值是**空串**时同理（`a_{{X}}.png` 会写成 `a_.png`）。
        //   两种情况都保持原判（不拆），等真遇到这种包再说。
        if (string.IsNullOrEmpty(defaultValue)) return null;

        return new Candidate(name, values, defaultValue, covered);
    }

    /// <summary>块的值能不能**干净**展开（有未解析/有损/确定化的一律不要）。</summary>
    private static string? CleanValue(string value, DynamicTokenResolver tokens)
    {
        DynamicTokenResolver.Expansion e = tokens.Expand(value);
        if (e.Unresolved.Count > 0 || e.Lossy.Count > 0 || e.Determinized.Count > 0) return null;
        return e.Text;
    }

    /// <summary>按候选拆补丁。返回空列表表示这条拆不了（<paramref name="failure"/> 给原因）。</summary>
    private static List<ChangePatch> Build(ChangePatch patch, Candidate candidate,
        DynamicTokenResolver tokens, out string? failure)
    {
        failure = null;
        var built = new List<ChangePatch>();

        for (int i = 0; i < candidate.Values.Count; i++)
        {
            var added = new JObject { ["Season"] = candidate.CoveredSeasons[i] };
            if (Patch(patch, candidate.Name, candidate.Values[i], added, tokens) is ChangePatch p)
            {
                built.Add(p);
            }
        }

        // 默认那一块：条件是**没被条件块占掉的那些季**
        if (candidate.DefaultValue.Length > 0)
        {
            string[] rest = Seasons.Where(s => !candidate.CoveredSeasons.Contains(s)).ToArray();
            if (rest.Length == 0) return built;   // 四季占满（上面已判过，这里只是兜底）
            var added = new JObject { ["Season"] = string.Join(", ", rest) };
            if (Patch(patch, candidate.Name, candidate.DefaultValue, added, tokens) is ChangePatch p)
                built.Add(p);
        }

        if (built.Count == 0) failure ??= "拆出来的每一条都因为条件冲突被否掉了";
        return built;
    }

    /// <summary>
    /// 造拆出来的一条：克隆原始 <c>raw</c>，把 <c>Target</c>/<c>FromFile</c> 里的裸令牌换成这一块的值，
    /// 并把这一块的条件 **AND** 进 <c>When</c>。条件冲突（同名不同值）⇒ 返回 <c>null</c>。
    /// </summary>
    private static ChangePatch? Patch(ChangePatch patch, string token, string value, JObject added,
        DynamicTokenResolver tokens)
    {
        if (Merge(patch.When, added) is not JObject when) return null;

        var raw = (JObject)patch.Raw.DeepClone();
        // ★ **深度替换**：这个令牌在**整条补丁**里都要换成"这一块的值"，不只是 `Target`/`FromFile`。
        //   实测代价（新语料 94 条）：East Scarp 的动画立绘包写
        //   `Target: Portraits/Aideen_{{Season}}` + `When: {HasFile: assets/Aideen/Aideen_{{Season}}.png}`
        //   —— 我们只换了目标名（→ `Aideen_spring`），同一个令牌**留在 `When` 的路径里** ⇒
        //   "文件在不在"判不了 ⇒ 整条退回 `NeedsReview`（理由还是"需提供 --mods"，而 `--mods` 明明给了）。
        //   替换范围与 CP 展开语义一致：**展开成哪一块，这一块的值就贯穿全条**。
        // ⚠ 顺序要紧：`when` 是**从原来的 When 合并出来的**（里面同样带着裸令牌），
        //   所以先把它也深度替换掉，再写回 `raw` —— 否则「先 ReplaceDeep 整条、后覆盖 When」
        //   等于把 When 还原成没换过的样子（我第一版就是这么错的，被新加的测试逮住）。
        var whenFixed = (JObject)when.DeepClone();
        ReplaceDeep(whenFixed, token, value);
        ReplaceDeep(raw, token, value);
        if (whenFixed.HasValues) raw["When"] = whenFixed;
        else raw.Remove("When");

        return new ChangePatch
        {
            SourceFile = patch.SourceFile,
            Index = patch.Index,
            Action = patch.Action,
            LogName = patch.LogName,
            Target = Replace(patch.Target ?? "", token, value),
            FromFile = Replace(patch.FromFile ?? "", token, value),
            Update = patch.Update,
            When = whenFixed.HasValues ? whenFixed : null,
            Raw = raw,
            Priority = patch.Priority,
        };
    }

    /// <summary>把 `raw` 里**所有**字符串（含键名）里的裸令牌换成这一块的值。</summary>
    private static void ReplaceDeep(JToken node, string token, string value)
    {
        switch (node)
        {
            case JObject obj:
                foreach (JProperty p in obj.Properties().ToList())
                {
                    string renamed = Replace(p.Name, token, value);
                    ReplaceDeep(p.Value, token, value);
                    if (!string.Equals(renamed, p.Name, StringComparison.Ordinal)) p.Replace(new JProperty(renamed, p.Value));
                }
                break;
            case JArray arr:
                foreach (JToken item in arr) ReplaceDeep(item, token, value);
                break;
            case JValue { Type: JTokenType.String } v:
                v.Value = Replace(v.Value<string>() ?? "", token, value);
                break;
        }
    }

    private static string Replace(string text, string token, string value) =>
        Regex.Replace(text, @"\{\{\s*" + Regex.Escape(token) + @"\s*\}\}", _ => value,
            RegexOptions.IgnoreCase);

    /// <summary>
    /// 两个 <c>When</c> 取 **AND**。同名键值不同 ⇒ 无解，返回 <c>null</c>（不猜）。
    /// 键名按**不区分大小写**比（CP 的条件键就是这么比的）。
    /// </summary>
    private static JObject? Merge(JObject? a, JObject b)
    {
        var merged = a == null ? new JObject() : (JObject)a.DeepClone();
        foreach (JProperty p in b.Properties())
        {
            JProperty? same = merged.Properties()
                .FirstOrDefault(q => q.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            if (same == null) merged[p.Name] = p.Value.DeepClone();
            else if (!JToken.DeepEquals(same.Value, p.Value)) return null;
        }
        return merged;
    }
}
