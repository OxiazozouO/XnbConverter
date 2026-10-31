using System.Text;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp.Lexing;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 把补丁 <c>Target</c> / <c>FromFile</c> 里的 <b><c>{{&lt;本包配置字段&gt;}}</c></b>
/// 与**包内 <c>DynamicTokens</c>** 直接代成值。
/// <para>
/// 依据：CP 的 <c>TokenString</c> 在渲染字符串时把令牌值集
/// <b><c>string.Join(", ", values)</c></b>（<c>TokenString.cs:290</c>）。
/// 多值字段因此天然展开成逗号分隔的多个值 —— 而 <c>Target</c> 本来就按 <c>,</c> 切分
/// （<c>TargetNames</c>），所以同一套展开对两个字段都成立。
/// </para>
/// <para>
/// <b>只动这两个字段</b>：值里的 <c>{{...}}</c> 是另一回事（CP 按各自上下文解），
/// 在这里代掉会写坏对话文案 —— 那条路在 <see cref="ValueTokenSubstitutor"/>。
/// </para>
/// <para>
/// 取值用 <see cref="ConfigSchemaTokens"/>（<c>config.json</c> 优先、否则 <c>ConfigSchema.Default</c>）——
/// 与 CP 在<b>没有 config.json 时</b>的行为一致：把配置冻结在默认值上。
/// 这是<b>确定化</b>（配置选择被写死），由报告如实体现，不是无中生有。
/// <c>DynamicTokens</c> 走 <see cref="DynamicTokenResolver"/>：<b>判不了（有损）的不代</b>，
/// 与值那边同一条规矩（宁可整条报出来，不把猜的路径写进产物）。
/// </para>
/// </summary>
public static class ConfigTokenSubstitutor
{
    /// <summary>
    /// 把 <c>{{名字}}</c> 换成配置值。<b>走 CP 的词法器</b>，不用正则。
    /// <para>
    /// 原先这里是一条正则 <c>\{\{\s*([^{}:|]+?)\s*\}\}</c> —— 那是对 <c>{{…}}</c> 结构的**第四份**
    /// 手写匹配（前几份在 Task 48/49/51/52 里陆续出过事）。它靠"名字里不许出现 <c>{ } : |</c>"
    /// 来躲开嵌套，而不是真的懂结构；换成词法器之后，
    /// <list type="bullet">
    /// <item>名字里带空格照样认（<c>{{White Cow Default}}</c>）；</item>
    /// <item>带输入参数的令牌<b>不是</b>"整值代入"的形态，仍不替换（与旧正则同一条规矩）；</item>
    /// <item>**参数里嵌的**配置令牌照常替换（<c>{{i18n:K |x={{White Cow Default}}}}</c>）——
    ///   旧正则也会顺带命中它，但那是碰巧，这里是结构上就该命中。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <summary>
    /// 给**其它需要路径的地方**用（目前是 <c>Include.FromFile</c>）：只代配置/包内令牌，
    /// 带输入参数的不碰。`Include` 在解析阶段就展开文件列表，那时补丁级代换还没跑，
    /// 不代的话形如 <c>Data/{{SeasonalOutfits}}_NPCData.json</c> 的整个 Include 会被当成"文件不存在"丢掉
    /// （实测 Bear Family 的 3 个 `Data/Characters` 条目就是这么整包消失的）。
    /// </summary>
    public static string ReplaceForPath(string text, ConfigSchemaTokens schema)
    {
        if (!text.Contains("{{", StringComparison.Ordinal)) return text;
        return ReplaceTokens(text, schema, out _);
    }

    private static string ReplaceTokens(string text, ConfigSchemaTokens schema, out bool any)
    {
        any = false;
        if (!CpLexer.Instance.TryParse(text, out IReadOnlyList<ILexToken> tokens)) return text;

        var hits = new List<(int Start, int End, string Value)>();
        Collect(tokens, schema, ref any, hits);
        if (hits.Count == 0) return text;

        var sb = new StringBuilder(text);
        foreach ((int start, int end, string value) in hits.OrderByDescending(h => h.Start))
        {
            sb.Remove(start, end - start);
            sb.Insert(start, value);
        }
        return sb.ToString();

        static void Collect(IReadOnlyList<ILexToken> tokens, ConfigSchemaTokens schema,
            ref bool any, List<(int Start, int End, string Value)> hits)
        {
            foreach (ILexToken token in tokens)
            {
                if (token is not LexTokenToken tk) continue;

                // 带输入参数的令牌不替换（不是"整值代入"的形态），但**往里找**：
                // 参数里嵌的配置令牌该代。见方法说明。
                if (!tk.HasInputArgs() && tk.Start >= 0 && tk.End > tk.Start
                    && schema.TryGetValues(tk.Name, out IReadOnlyList<string> values) && values.Count > 0)
                {
                    any = true;
                    hits.Add((tk.Start, tk.End, string.Join(", ", values)));
                }
                else if (tk.InputArgs != null)
                {
                    Collect(tk.InputArgs.Parts, schema, ref any, hits);
                }
            }
        }
    }

    /// <summary>就地替换。返回被替换的字段数（便于报告"有多少条是**冻结配置默认值**换来的"）。</summary>
    /// <param name="builtins">
    /// 可空：包内 <c>DynamicTokens</c> / 配置字段 / CP 内建里**能静态解析**的令牌
    /// （<c>Random</c>/<c>Range</c>）的展开器。
    /// <c>Target</c>/<c>FromFile</c> 里写 <c>{{Random: a.png, b.png}}</c> 时 CP 会挑一个文件，
    /// 我们代不了就整条不写 —— 代成**第一个**只是「不再随机」，与配置冻结在默认值同一类确定化。
    /// </param>
    /// <param name="onNote">代换里做了**确定化**时逐条记一笔（报告要如实说出来）。</param>
    public static int Apply(IEnumerable<ChangePatch> patches, ConfigSchemaTokens schema,
        DynamicTokenResolver? builtins = null, Action<ChangePatch, string>? onNote = null)
    {
        int changed = 0;
        foreach (ChangePatch patch in patches)
        {
            var notes = new List<string>();
            if (Substitute(patch, "Target", schema, builtins, notes)) changed++;
            if (Substitute(patch, "FromFile", schema, builtins, notes)) changed++;
            foreach (string note in notes.Distinct()) onNote?.Invoke(patch, note);
        }
        return changed;
    }

    private static bool Substitute(ChangePatch patch, string field, ConfigSchemaTokens schema,
        DynamicTokenResolver? builtins, List<string> notes)
    {
        if (patch.Raw[field] is not JToken token || token.Type != JTokenType.String) return false;
        string text = token.Value<string>() ?? "";
        if (!text.Contains("{{", StringComparison.Ordinal)) return false;

        string replaced = ReplaceTokens(text, schema, out bool any);

        if (builtins != null)
        {
            // 包内 `DynamicTokens` 一类**上下文无关**的令牌，CP 在 `Target`/`FromFile` 里同样会展开
            // （`TokenString.cs:290`）。不展开的后果不是"少代一个名字"，而是**整条补丁落空** ——
            // 实测 `[CP] DaisyNiko's Tilesheets` 的
            // `Load Maps/spring_daisyextras, … ← assets/{{recolour}}/{{TargetWithoutPath}}.png`
            // 就是这一类：包内图集没进产出树 ⇒ 引用它的 **29 张地图建不出来**。
            // ⚠ 有损（条件判不了、落到默认块）**只要结果具体到一条路径就接受**：
            //   路径不代 ⇒ 整个 `Load` 落空（资产直接不存在，NPC/地图都建不出来），
            //   比"挑了一个默认形态"糟得多。实测 S&S 的
            //   `Portraits/Hector ← …/{{TrueShape}}/spring_Hector.png`：
            //   `TrueShape` 的条件块（看过某事件后用 `trueshape`）判不了 ⇒ 落默认块
            //   `{{Random: male, female}}` —— 有损，但 male/female 都是**真实文件**。
            //   不代的结果是 Hector 连行走图/立绘都没有，游戏里直接 `Failed to spawn NPC`。
            DynamicTokenResolver.Expansion ex = builtins.Expand(replaced);
            if (ex.Text != replaced)
            {
                // 先把内建 `{{Random}}`/`{{Range}}` 定下来（见 Apply 的说明），再看还剩不剩令牌。
                string candidate = builtins.DeterminizeBuiltins(ex.Text);
                // 路径里还允许**字段引用令牌**（`{{TargetWithoutPath}}` 等由 `LoadApplier` 下一步代）。
                // 用词法器判"还剩什么令牌"，**不手写 `{{` 扫描**；字段引用令牌留给下游精确代换。
                bool onlyFieldTokens = TokenScan.Unresolved(candidate).All(n =>
                    n.Equals("Target", StringComparison.OrdinalIgnoreCase)
                    || n.Equals("TargetPathOnly", StringComparison.OrdinalIgnoreCase)
                    || n.Equals("TargetWithoutPath", StringComparison.OrdinalIgnoreCase));
                if (onlyFieldTokens)
                {
                    replaced = candidate;
                    any = true;
                    if (ex.Determinized.Count > 0)
                        notes.Add($"`{field}` 里的 "
                                  + string.Join("`、`", ex.Determinized.Select(n => "{{" + n + "}}"))
                                  + " **确定化**（运行时单值冻结：Season→春天、Language→中文(zh)、"
                                  + "FarmType→Standard；其余备选取了第一个）");
                    if (ex.Lossy.Count > 0)
                        notes.Add($"`{field}` 里的 {string.Join("`、`", ex.Lossy.Select(n => "{{" + n + "}}"))}"
                                  + " 条件判不了，按**无条件默认块**确定化（否则整条 Load 落空）");
                }
            }
            else
            {
                // 再代 CP 内建里可确定化的 `{{Random}}`/`{{Range}}`（见 Apply 的说明）。
                string determinized = builtins.DeterminizeBuiltins(replaced);
                if (determinized != replaced) { replaced = determinized; any = true; }
            }

            // 最后再管 CP 内建的 `{{Season}}`（= 当前季节，单值）。纯 XNB 里**一个资产键只有一份内容**，
            // 「同一个资产名按季换整张图」表达不了 ⇒ **确定化到春天**，并如实报出来。
            // 实测 `[CP] HxW Tilesheets`：`FromFile: Assets/{{Coloring}}/{{SeasonWithDefault}}/hime_outdoorplants.png`
            // —— 包内令牌展开之后浮出来的正是内建 `{{Season}}`；不代就整条落空，
            // **引用它的 7 张地图建不出来**。代成春天 = 那几份图集四季都用春天那张
            // （看得见、且比"地图直接建不出来"好）。
            // ⚠ 两条边界：① 只在本包**没有**自己的 `Season` 令牌时做（那是包内令牌，轮不到我们猜）；
            //   ② 只认**不带输入参数**的形态（`{{Season: …}}` 不是这个内建）。
            // ⚠ 只动 `Target`/`FromFile` —— 值里的 `{{Season}}` 由那道 `UnexpandedTokenAudit` 继续报出来。
            if (builtins.BlocksOf("Season") == null)
                replaced = DeterminizeSeason(replaced, field, notes, ref any);

            // ★ CP 内建 `{{Language}}`（当前语言短码，如 `zh`）同理确定化到 `zh`。
            //   实测界面包 `[CP]猫露谷物语(Interface)` 的 `FromFile: assets/{{Target}}_{{language}}.png`
            //   —— 不代就是 `TargetMissing`，中文界面图整批不写（实机就是"界面还是英文"）。
            if (builtins.BlocksOf("Language") == null)
                replaced = DeterminizeLanguage(replaced, field, notes, ref any);
        }

        if (!any) return false;

        patch.Raw[field] = replaced;
        if (field == "Target") patch.Target = replaced;
        else patch.FromFile = replaced;
        return true;
    }

    /// <summary>
    /// 把 CP 内建的 <c>{{Season}}</c> 代成 <c>spring</c>（**确定化**：CP 取的是运行时当前季节，
    /// 四季里哪一季都"同样对"；纯 XNB 里一个资产键只有一份内容，只能挑一份）。
    /// 走**词法器**认令牌，不拿正则碰花括号。
    /// </summary>
    private static string DeterminizeSeason(string text, string field, List<string> notes, ref bool any) =>
        DeterminizeBuiltin(text, "Season", "spring", field, notes, ref any,
            "**确定化**：一律取**春天**那份（CP 是当前季节；纯 XNB 里同一个资产键只有一份内容）");

    /// <summary>
    /// 把 CP 内建的 <c>{{Language}}</c> 代成 <c>zh</c>（**确定化**：CP 取的是运行时当前语言，
    /// 本产出面向中文存档）。与 <see cref="DeterminizeSeason"/> 共用同一套词法器实现。
    /// </summary>
    private static string DeterminizeLanguage(string text, string field, List<string> notes, ref bool any) =>
        DeterminizeBuiltin(text, "Language", Baker.FrozenLanguage, field, notes, ref any,
            "**确定化**：一律取**中文（zh）**那份（CP 是当前游戏语言；产出面向中文存档）");

    private static string DeterminizeBuiltin(string text, string name, string value,
        string field, List<string> notes, ref bool any, string why)
    {
        if (!CpLexer.Instance.MightContainTokens(text)) return text;
        if (!CpLexer.Instance.TryParse(text, out IReadOnlyList<ILexToken> tokens)) return text;

        var hits = new List<(int Start, int End)>();
        Collect(tokens, hits, name);

        if (hits.Count == 0) return text;
        any = true;
        notes.Add($"`{field}` 里的 `{{{{{name}}}}}` {why}");

        var sb = new StringBuilder(text);
        foreach ((int start, int end) in hits.OrderByDescending(h => h.Start))
        {
            sb.Remove(start, end - start);
            sb.Insert(start, value);
        }
        return sb.ToString();

        static void Collect(IReadOnlyList<ILexToken> tokens, List<(int Start, int End)> hits, string name)
        {
            foreach (ILexToken token in tokens)
            {
                if (token is not LexTokenToken tk) continue;
                // 带输入参数的形态不是这个内建；但**往里找**（参数里可能嵌着真的内建令牌）。
                if (tk.InputArgs != null)
                {
                    Collect(tk.InputArgs.Parts, hits, name);
                    continue;
                }
                if (tk.Start >= 0 && tk.End > tk.Start
                    && tk.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    hits.Add((tk.Start, tk.End));
            }
        }
    }
}
