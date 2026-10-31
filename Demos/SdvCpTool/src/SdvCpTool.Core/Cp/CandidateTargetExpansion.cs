using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 把 <c>Target</c> 里**代不掉、但取值可枚举**的包内令牌按候选值**逐个发射**。
/// <para>
/// <b>为什么需要它</b>（实机来源，2026-10-15）：`Lnh's Farms Convenient Road` 用同名多块
/// 的 <c>DynamicTokens</c> 按**农场类型**定义同一个令牌 <c>Farm</c>
/// （<c>Farm</c> / <c>Farm_Foraging</c> / <c>Farm_Fishing</c> / <c>Farm_Mining</c> /
/// <c>Farm_Combat</c> / <c>Farm_Island</c> / <c>Farm_FourCorners</c> / <c>Farm_Ranching</c>），
/// 补丁写的是 <c>"Target": "Maps/{{Farm}}"</c> —— 也就是「**给玩家实际在玩的那张农场图**盖一条便捷通道」。
/// 我们静态代不掉（取决于存档选的农场类型）⇒ 判 <c>TargetContainsTokens</c>、整条不写
/// ⇒ **任何农场图上都没有这条传送**。
/// </para>
/// <para>
/// <b>为什么按候选值展开是对的</b>：与 <see cref="SeasonTargetExpansion"/> 逐字同构 ——
/// 令牌的一个候选值对应**一个不同的资产名**，而游戏一次只加载其中一个（当前季节 / 当前农场图），
/// 所以「每个候选都写一份」在实机里等价于 CP 的「按当前存档状态写一份」，
/// 代价只是多改了几张玩家用不到的图。
/// </para>
/// <para>
/// ⚠ <b>多目标要按项拆</b>：<c>Target: "A, B/{{x}}"</c> 里只有 <c>B/{{x}}</c> 含令牌，
/// 那么不含令牌的 <c>A</c> 必须**只出一份**（否则每个候选那份都带着 <c>A</c> ⇒ <c>A</c> 被写 N 遍，
/// 而 CP 只写一遍）。实测 <c>[CP] Ultimate Farm</c> 的 <c>Maps/UltimateFarm, Maps/{{file}}</c>
/// 第二条用的是 <c>TextOperations: Append</c>（往 <c>DayTiles</c>/<c>NightTiles</c>/warps **追加**）
/// ⇒ 不拆就把那张图追加了 8 遍。
/// </para>
/// <para>
/// <b>只在形状能被证明是互斥时才展开</b>（与 <see cref="TokenEnumerationExpander"/> 同一判据，
/// 但**不加条件** —— 四季那条能靠 GSQ 的 <c>SEASON</c> 表达，农场类型在 GSQ 里根本没有这一档，
/// 只能靠「目标资产本身不同」来区分）：
/// <list type="number">
/// <item>令牌是**包内定义**的，且此时确实**代不掉**（代得掉的走原路，不在这里碰）；</item>
/// <item>取其中**同键、取值两两不同且都是字面量**的那一组（≥2 个、≤ <see cref="MaxCandidates"/> 个）；
///   同一令牌里别的维度的块（键不同、或取值本身还是令牌）**不计入**，但会在说明里记一笔
///   —— 实测那个令牌 10 块里就有 2 块是自定义农场那两档（键是 <c>Query: …</c>、取值是令牌），
///   要求"所有块都合法"会把整个令牌否掉，八张原版农场图一个也覆盖不到。</item>
/// </list>
/// </para>
/// </summary>
public static class CandidateTargetExpansion
{
    /// <summary>候选数上限：超过就说明这不是"一个维度的取值"，宁可不拆。</summary>
    public const int MaxCandidates = 12;

    /// <summary>文本里的 <c>{{令牌}}</c>（不含参数，取到内层花括号为止）。</summary>
    private static readonly Regex TokenPattern = new(@"\{\{\s*([A-Za-z_][\w. ]*?)\s*\}\}", RegexOptions.Compiled);

    /// <summary>就地展开整份补丁列表；返回**净增**的补丁数，<paramref name="notes"/> 收给人看的说明。</summary>
    public static int Apply(List<ChangePatch> patches, DynamicTokenResolver tokens, List<string> notes)
    {
        int added = 0;
        for (int i = 0; i < patches.Count; i++)
        {
            ChangePatch patch = patches[i];
            if (patch.Target == null) continue;

            foreach (string name in TokenPattern.Matches(patch.Target).Select(m => m.Groups[1].Value))
            {
                List<string> candidates = Candidates(name, tokens, out int skipped);
                if (candidates.Count == 0) continue;

                // 多目标**按项拆**：不含令牌的项只出一份（见类说明里 Ultimate Farm 那条实测）
                List<string> entries = SplitTopLevel(patch.Target, ',');
                var fixedEntries = entries.Where(e => !HasToken(e, name)).ToList();
                var tokenEntries = entries.Where(e => HasToken(e, name)).ToList();
                string? fixedTarget = fixedEntries.Count > 0 && tokenEntries.Count > 0
                                      && !HasTokenElsewhere(patch, name)
                    ? string.Join(", ", fixedEntries)
                    : null;

                var expanded = new List<ChangePatch>(candidates.Count + 1);
                if (fixedTarget != null)
                {
                    var rawFixed = (JObject)patch.Raw.DeepClone();
                    rawFixed["Target"] = fixedTarget;
                    expanded.Add(Copy(patch, rawFixed));
                }
                foreach (string value in candidates)
                {
                    JObject raw = Substitute(patch.Raw, name, value);
                    if (fixedTarget != null)
                    {
                        raw["Target"] = string.Join(", ",
                            tokenEntries.Select(e => ReplaceAll(e, name, value)));
                    }
                    expanded.Add(Copy(patch, raw));
                }

                patches.RemoveAt(i);
                patches.InsertRange(i, expanded);
                added += expanded.Count - 1;
                notes.Add($"`{{{{{name}}}}}` 代不掉但取值可枚举（{candidates.Count} 个）"
                          + $"⇒ 按候选资产名各写一份：{Name(patch)} → "
                          + string.Join("、", expanded.Select(p => p.Target))
                          + (fixedTarget != null ? $"（其中 `{fixedTarget}` **只写一次**：它不含令牌）" : "")
                          + (skipped > 0 ? $"（另有 {skipped} 个块不属于这个维度或取值本身是令牌，未展开）" : ""));
                break;   // 一条补丁只展开一个维度（两个就说明不止一个维度，不猜）
            }
        }
        return added;
    }

    /// <summary>
    /// 这个令牌此刻能不能被**枚举**：包内定义、代不掉，且有一组**同键、取值两两不同的字面量**块。
    /// 返回候选值（不能枚举时返回空表），<paramref name="skipped"/> 收"没能纳入的几个块"。
    /// </summary>
    private static List<string> Candidates(string name, DynamicTokenResolver tokens, out int skipped)
    {
        skipped = 0;
        IReadOnlyList<(string Value, JObject? When)>? blocks = tokens.BlocksOf(name);
        if (blocks == null || blocks.Count < 2) return new List<string>();

        // 代得掉的令牌不在这里碰（原路走判定/代入）。
        // ⚠ 关掉「所有块判不了时取第一块」：这里要看到的正是"代不掉"，才能按候选枚举；
        //   打开了会把 8 种农场类型压成第一块（实测 4 条单测直接失败）。
        if (!tokens.Expand("{{" + name + "}}", allowFirstBlock: false)
                .Unresolved.Any(u => u.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return new List<string>();

        // ★ 所有块的条件都**静态为假**（没有一块是"不知道"）⇒ CP 里这个令牌**没有值**，
        //   整条补丁不会落 —— 不能当成"取决于运行时"去枚举候选。
        //   实测（2026-10-19）`[CP] Ultimate Farm` 的 `{{file}}`：8 块全是 `ReplaceVanillaFarm: X`，
        //   而配置里该项是**空**（`AllowBlank`）⇒ CP 下令牌无值。我们原来按候选写了 8 份，
        //   其中 `Maps/Farm` 那份把 Ultimate Farm 的洞穴传送追加到了 Grandpa's Farm 上
        //   （`Farm 60 6 -> FarmCave 37 13`，25×20 的图外 ⇒ 又一个"卡到地图外"）。
        bool sawUnknown = false, sawMatch = false;
        foreach ((_, JObject? when) in blocks)
        {
            if (when == null || !when.HasValues) { sawMatch = true; break; }   // 默认块本会给出值
            bool? holds = tokens.TryEvaluateWhen(when);
            if (holds == null) sawUnknown = true;
            else if (holds.Value) { sawMatch = true; break; }
        }
        if (!sawUnknown && !sawMatch) return new List<string>();

        var values = new List<string>(blocks.Count);
        string? key = null;
        foreach ((string value, JObject? when) in blocks)
        {
            string text = (value ?? "").Trim();
            bool usable = when != null && when.Properties().Count() == 1;
            if (usable)
            {
                JProperty only = when!.Properties().First();
                key ??= only.Name;
                usable = only.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
                         && only.Value.Type == JTokenType.String
                         && text.Length > 0
                         && !text.Contains("{{", StringComparison.Ordinal)
                         && !values.Contains(text, StringComparer.OrdinalIgnoreCase);
            }
            if (!usable)
            {
                skipped++;
                continue;
            }
            values.Add(text);
        }

        if (values.Count < 2 || values.Count > MaxCandidates)
        {
            skipped += values.Count;
            return new List<string>();
        }
        return values;
    }

    /// <summary>造一份新的补丁记录（字段全从 <paramref name="raw"/> 现读，避免漏字段）。</summary>
    private static ChangePatch Copy(ChangePatch patch, JObject raw) => new()
    {
        SourceFile = patch.SourceFile,
        Index = patch.Index,
        Action = raw["Action"]?.Value<string>() ?? patch.Action,
        LogName = raw["LogName"]?.Value<string>(),
        Target = raw["Target"]?.Value<string>(),
        FromFile = raw["FromFile"]?.Value<string>(),
        Update = raw["Update"]?.Value<string>(),
        When = raw["When"] as JObject,
        Raw = raw,
        Priority = patch.Priority,
    };

    /// <summary>这段文本里有没有这个令牌（<c>{{名字}}</c>，忽略大小写与空白）。</summary>
    private static bool HasToken(string text, string name) =>
        TokenPattern.Matches(text).Any(m => m.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary><c>Target</c> **之外**的字段里还有没有这个令牌（有就不出"不含令牌"那一份）。</summary>
    private static bool HasTokenElsewhere(ChangePatch patch, string name)
    {
        var clone = (JObject)patch.Raw.DeepClone();
        clone.Remove("Target");
        return HasToken(clone.ToString(Formatting.None), name);
    }

    /// <summary>把这段文本里的这个令牌换成候选值（其余令牌原样保留）。</summary>
    private static string ReplaceAll(string text, string name, string value) =>
        TokenPattern.Replace(text, m => m.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase)
            ? value
            : m.Value);

    /// <summary>顶层（不在 <c>{{…}}</c> 里）按分隔符切分。</summary>
    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{')
            {
                depth++;
                i++;
            }
            else if (i + 1 < text.Length && text[i] == '}' && text[i + 1] == '}')
            {
                if (depth > 0) depth--;
                i++;
            }
            else if (text[i] == separator && depth == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(text[start..].Trim());
        return parts;
    }

    /// <summary>
    /// 返回一份**新**的 raw：把（<c>When</c> 之外的）这个令牌换成候选值。
    /// <c>When</c> 整棵子树原样保留 —— 它说的是条件，不是"这个资产叫什么"。
    /// </summary>
    private static JObject Substitute(JObject raw, string name, string value)
    {
        var clone = (JObject)raw.DeepClone();
        Walk(clone);
        return clone;

        void Walk(JToken node)
        {
            switch (node)
            {
                case JObject obj:
                    foreach (JProperty p in obj.Properties().ToList())
                    {
                        if (p.Name.Equals("When", StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.Value.Type == JTokenType.String)
                        {
                            string text = p.Value.Value<string>() ?? "";
                            if (HasToken(text, name)) p.Value = ReplaceAll(text, name, value);
                        }
                        else
                        {
                            Walk(p.Value);
                        }
                    }
                    break;
                case JArray arr:
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].Type == JTokenType.String)
                        {
                            string text = arr[i].Value<string>() ?? "";
                            if (HasToken(text, name)) arr[i] = ReplaceAll(text, name, value);
                        }
                        else
                        {
                            Walk(arr[i]);
                        }
                    }
                    break;
            }
        }
    }

    private static string Name(ChangePatch patch) =>
        patch.LogName ?? $"{Path.GetFileName(patch.SourceFile)}#{patch.Index}";
}
