using System.Text;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp.Lexing;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 内容包 <c>content.json</c> 里 <c>DynamicTokens</c> 的「尽力展开」器。
/// <para>
/// 语义依据 CP 文档 <c>author-guide/tokens.md</c> §Dynamic tokens：
/// <list type="bullet">
/// <item>同名多块时，<b>最后一个条件匹配的块</b>胜出；</item>
/// <item><c>Value</c> 自身可含其它令牌（含<b>在它之前</b>定义的动态令牌）→ 需要递归展开；</item>
/// <item>块可带 <c>When</c>，条件不匹配则该块不生效。</item>
/// </list>
/// </para>
/// <para>
/// <b>只展开本包 <c>DynamicTokens</c> 里定义过的名字</b>。其余令牌（CP 内建的
/// <c>{{Random}}</c>、全局令牌 <c>{{season}}</c>、字段引用令牌 <c>{{Target}}</c> 等）
/// 一律<b>原样保留</b>并记入 <see cref="Expansion.Unresolved"/> —— 不猜、不误伤。
/// </para>
/// <para>
/// 带 <c>When</c> 的块由外部求值器（<c>Baker</c>）判定；<b>判不了的一律判失败</b>（不猜），
/// 因为"最后一个匹配块"是哪一块无法确知时，值就是不确知的。
/// </para>
/// </summary>
public sealed class DynamicTokenResolver
{
    /// <summary>一个 <c>When</c> 的求值结局：<see cref="Value"/>=<c>null</c> 表示求值失败（不知道）。</summary>
    public readonly record struct WhenResult(bool? Value, string? Reason = null);

    /// <summary>
    /// 展开结果。<paramref name="Text"/> 是尽力展开后的文本；
    /// <paramref name="Unresolved"/> 是没能展开的令牌名（含非 DynamicToken 的令牌）。
    /// </summary>
    /// <summary>
    /// 展开结果。
    /// <para>
    /// <paramref name="Unresolved"/> = 没能展开的令牌（原样留在文本里，下游会判失败）。
    /// <b><see cref="Lossy"/></b> 另立一档：<b>展开成功了，但值是「确定化」猜出来的</b>——
    /// 判不了的条件被当作不匹配，从而落到无条件默认块。
    /// </para>
    /// </summary>
    public sealed record Expansion(string Text, IReadOnlyList<string> Unresolved)
    {
        /// <summary>
        /// 做了<b>有损确定化</b>的令牌名 —— 值可能不等于运行时真值。
        /// <b>必须报出来</b>，不得混进「无损」。
        /// </summary>
        public IReadOnlyList<string> Lossy { get; init; } = Array.Empty<string>();

        /// <summary>
        /// 做了**无对错可言的确定化**的令牌名（目前只有 <c>Random</c>）—— 值本身没有「正确的那一个」，
        /// 我们取了**第一个**备选。与 <see cref="Lossy"/> 的区别：Lossy 是**可能猜错**（条件判不了），
        /// 这一档是**每个备选都同样正确**（CP 每次上下文更新都会重掷，取值随存档与时点变）。
        /// </summary>
        public IReadOnlyList<string> Determinized { get; init; } = Array.Empty<string>();
    }

    private sealed record Block(string Name, string Value, JObject? When);

    // 同名多块按 content.json 里的出现顺序保存（"最后一个匹配块胜出"依赖顺序）。
    private readonly Dictionary<string, List<Block>> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<JObject, WhenResult> _evaluateWhen;

    // 本包 ConfigSchema 的配置字段（= 配置令牌）。从同一个 content.json 根对象构建，不重复读盘。
    private ConfigSchemaTokens _configTokens = ConfigSchemaTokens.Empty;

    /// <summary>
    /// 本包之外、但**离线可定**的令牌的取值器（<c>(名字, 参数) → 渲染值</c>；<c>null</c> = 不认识）。
    /// <para>
    /// 目前只有 <c>HasMod</c>：它注册成 <c>ImmutableValueProvider("HasMod", installedMods)</c>
    /// （CP <c>TokenManager.cs:220</c>），值是**已装模组的 id 集合**，带 <c>|contains=X</c> 时折成
    /// <c>true</c>/<c>false</c>（CP <c>Token.GetValues</c>）。要知道装了哪些 mod 才能算 ——
    /// 那份索引只有 CLI 有，所以从外面传进来，本类不自己去读盘。
    /// </para>
    /// </summary>
    private readonly Func<string, string?, string?>? _externalTokens;

    private DynamicTokenResolver(Func<JObject, WhenResult> evaluateWhen, Func<string, string?, string?>? externalTokens)
    {
        _evaluateWhen = evaluateWhen;
        _externalTokens = externalTokens;
    }

    /// <summary>内容包是否定义了任何动态令牌。</summary>
    public bool IsEmpty => _blocks.Count == 0;

    /// <summary>已定义的动态令牌名（去重）。</summary>
    public IReadOnlyCollection<string> Names => _blocks.Keys;

    /// <summary>
    /// 某个动态令牌的各块（<c>Value</c> + 块上的 <c>When</c>，**按 content.json 里的出现顺序**）。
    /// 名字未定义时返回 <c>null</c>。
    /// <para>
    /// 给 <see cref="TokenEnumerationExpander"/> 用：它要把「按季节分块的令牌」拆成互斥的几条补丁
    /// —— 那需要看到**每一块**的值与条件，而不是只看「最后一个匹配块」折出来的那一个结果。
    /// 块的值**没有**递归展开，调用方要用 <see cref="Expand"/> 再过一遍。
    /// </para>
    /// </summary>
    /// <summary>这个名字是不是本包 <c>ConfigSchema</c> 字段（配置令牌）。</summary>
    public bool IsConfigToken(string name) => _configTokens.Contains(name);

    public IReadOnlyList<(string Value, JObject? When)>? BlocksOf(string name)
        => _blocks.TryGetValue(name, out List<Block>? list)
            ? list.Select(b => (b.Value, b.When)).ToList()
            : null;

    /// <summary>
    /// 求一个块条件：<c>true</c>/<c>false</c> = 已确定；<c>null</c> = 判不了（"不知道"）。
    /// 给需要**区分「静态为假」与「取决于运行时」**的调用方用（目前是
    /// <see cref="CandidateTargetExpansion"/>：所有块都静态为假 ⇒ CP 里这个令牌没有值）。
    /// </summary>
    public bool? TryEvaluateWhen(JObject when) =>
        EvaluateWhen(when, new List<string>(), new List<string>()).Value;

    /// <summary>
    /// 读内容包根目录下 <c>content.json</c> 的 <c>DynamicTokens</c>。
    /// <paramref name="evaluateWhen"/> 为 <c>null</c> 时，带 <c>When</c> 的块一律判不了（不猜）。
    /// 文件缺失或解析失败时返回一个空解析器（而不是抛异常）—— 调用方随后会因"令牌未展开"判失败。
    /// </summary>
    public static DynamicTokenResolver Load(string packRootDirectory, Func<JObject, WhenResult>? evaluateWhen,
        Func<string, string?, string?>? externalTokens = null)
    {
        var resolver = new DynamicTokenResolver(
            evaluateWhen ?? (_ => new WhenResult(null, "没有条件求值器（未提供 --mods）")),
            externalTokens);

        string contentJson = Path.Combine(packRootDirectory, "content.json");
        if (!File.Exists(contentJson)) return resolver;

        JObject root;
        try { root = JsoncReader.ParseFile(contentJson); }
        catch { return resolver; }

        // content.json 的 ConfigSchema 字段名也是令牌名（CP 注册成本地令牌），值取自包根 config.json。
        resolver._configTokens = ConfigSchemaTokens.Load(root, packRootDirectory);

        if (root["DynamicTokens"] is not JArray blocks) return resolver;

        foreach (JToken token in blocks)
        {
            if (token is not JObject block) continue;

            string? name = block["Name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Value 非字符串时保留其文本形式（CP 里正常都是字符串）。
            string value = block["Value"] switch
            {
                null => "",
                JValue { Type: JTokenType.String } v => v.Value<string>() ?? "",
                JToken t => t.ToString(Newtonsoft.Json.Formatting.None),
            };

            if (!resolver._blocks.TryGetValue(name, out List<Block>? list))
                resolver._blocks[name] = list = new List<Block>();
            list.Add(new Block(name, value, block["When"] as JObject));
        }

        return resolver;
    }

    /// <summary>
    /// 尽力展开 <paramref name="text"/> 里的动态令牌。
    /// 展开不了的（未定义的令牌、条件判不了的令牌、成环的令牌）原样保留并记入结果的
    /// <see cref="Expansion.Unresolved"/>。
    /// <para>
    /// <paramref name="allowFirstBlock"/> = 「所有块都判不了时取第一块」（见
    /// <see cref="ResolveValue"/>）。**值路径**打开；**候选枚举**（`CandidateTargetExpansion`）
    /// 与**条件求值**（`Baker`）关闭 —— 它们必须看到"代不掉"，否则会把「8 种农场类型各写一份」
    /// 压成"只写第一份"。
    /// </para>
    /// </summary>
    public Expansion Expand(string text, bool allowFirstBlock = true)
    {
        bool saved = _allowFirstBlock;
        _allowFirstBlock = allowFirstBlock;
        try
        {
            var unresolved = new List<string>();
            var lossy = new List<string>();
            var determinized = new List<string>();
            string result = Expand(text, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                unresolved, lossy, determinized);
            // 同名令牌在一段文本里可能多次出现；报告里去重
            return new Expansion(result, unresolved.Distinct(StringComparer.Ordinal).ToList())
            {
                Lossy = lossy.Distinct(StringComparer.Ordinal).ToList(),
                Determinized = determinized.Distinct(StringComparer.Ordinal).ToList(),
            };
        }
        finally
        {
            _allowFirstBlock = saved;
        }
    }

    /// <summary>「所有块都判不了时取第一块」是否允许（值路径开、候选枚举/条件求值关）。</summary>
    private bool _allowFirstBlock = true;

    private string Expand(string text, HashSet<string> stack, List<string> unresolved, List<string> lossy,
        List<string> determinized)
    {
        if (!CpLexer.Instance.MightContainTokens(text)) return text;

        // 词法错误（未闭合等）：CP 会拒绝这条补丁；这里原样保留，并记一笔让下游别当它干净。
        if (!CpLexer.Instance.TryParse(text, out IReadOnlyList<ILexToken> tokens))
        {
            unresolved.Add("(词法错误)");
            return text;
        }

        List<Replacement>? replacements = null;
        Collect(tokens, stack, unresolved, lossy, determinized, ref replacements);
        if (replacements == null) return text;

        // **按原文位置替换**，从后往前做（偏移互不干扰）——
        // 不重建整串：重建会把花括号内的空白规范化掉（`{{i18n:K }}` → `{{i18n:K}}`），
        // 而代换结果会原样写进产物，那是改内容不是改格式。
        var sb = new StringBuilder(text);
        foreach (Replacement r in replacements.OrderByDescending(r => r.Start))
        {
            sb.Remove(r.Start, r.End - r.Start);
            sb.Insert(r.Start, r.Value);
        }
        return sb.ToString();
    }

    /// <summary>一处替换：把原文的 <c>[Start, End)</c> 换成 <see cref="Value"/>。</summary>
    private readonly record struct Replacement(int Start, int End, string Value);

    /// <summary>
    /// 收集所有**能代掉**的令牌位置。
    /// <para>
    /// <b>嵌套也要进去</b>：<c>{{i18n:KEY |Season={{Season}}}}</c> 里的 <c>{{Season}}</c> 是
    /// **内层**令牌，原实现「找第一个 <c>}}</c>」会把它连同外层一起原样放行 —— 于是
    /// <c>i18n</c> 拿到的是未展开的实参值、代出来的就是字面花括号。CP 的输入参数本身就是
    /// 一棵令牌树（词法器产出 <c>LexTokenInput</c>），内层当然要代。
    /// </para>
    /// <para>
    /// 代不掉的**不打散外层**：只把内层能代的换掉，外层原样保留 —— 于是
    /// <c>{{i18n:K |PlayerName={{PlayerName}}}}</c> 里 <c>PlayerName</c> 不认识时整串不变。
    /// </para>
    /// </summary>
    private void Collect(IReadOnlyList<ILexToken> tokens, HashSet<string> stack,
        List<string> unresolved, List<string> lossy, List<string> determinized, ref List<Replacement>? replacements,
        bool builtinsOnly = false)
    {
        foreach (ILexToken token in tokens)
        {
            if (token is not LexTokenToken tk) continue;

            string name = tk.Name;
            string? value = null;

            if (builtinsOnly && tk.HasInputArgs() && TryResolveBuiltin(tk, stack, unresolved, lossy, determinized, out string? onlyBuiltin))
            {
                value = onlyBuiltin;
            }
            else if (builtinsOnly)
            {
                // 只认内建：别的不碰，但继续往里找（`{{Random:{{Range:0,5}}}}` 的内层要代）。
                if (tk.InputArgs != null)
                    Collect(tk.InputArgs.Parts, stack, unresolved, lossy, determinized, ref replacements, builtinsOnly: true);
                continue;
            }
            else if (_blocks.ContainsKey(name))
            {
                if (stack.Contains(name)) unresolved.Add($"{name}（成环）");
                else
                {
                    value = ResolveValue(name, stack, unresolved, lossy, determinized);
                    if (value == null) unresolved.Add(name);
                }
            }
            else if (_externalTokens != null && _externalTokens(name, tk.InputArgs?.ToString().Trim()) is { } rendered)
            {
                // 本包之外的**离线可定**令牌（目前只有 `HasMod`）：外部取值器给得出值就代掉。
                value = rendered;
            }
            else if (TryResolveBuiltin(tk, stack, unresolved, lossy, determinized, out string? builtin))
            {
                value = builtin;
            }
            else if (tk.InputArgs != null && _configTokens.Contains(name))
            {
                // ★ 配置字段**带输入参数**的用法：只认 CP 的 `|contains=`（折成 true/false）。
                // ⚠ 这里原来写着"配置字段名本身不含 `:` / `|`，所以带参数的一定不是配置字段" ——
                //   **那句话是错的**：**字段名**里确实不会有 `|`，但**用法**可以带。
                //   实测 `[CP] Seasonal Cute Characters` 的 `IndoorsWinterOutfitEnabled` 值就是
                //   `{{WinterClothing |contains=IndoorAndOutdoor, IndoorOnly}}` ——
                //   代不掉时那个令牌的"值"就是一串字面花括号，拿它当条件必判错
                //   （上一版"令牌当条件"改动就是栽在这里，见 `TokenArgs` 的说明）。
                if (_configTokens.TryGetValues(name, out IReadOnlyList<string> fieldValues)
                    && TokenArgs.TryFoldContains(fieldValues, tk.InputArgs.ToString(), out string folded))
                {
                    value = folded;
                }
                else
                {
                    // 别的算子（`{{X:input}}` 之类）我们不会算 —— 原样保留，但**参数里的内层令牌照代**。
                    unresolved.Add(name);
                    Collect(tk.InputArgs.Parts, stack, unresolved, lossy, determinized, ref replacements);
                    continue;
                }
            }
            else if (tk.HasInputArgs())
            {
                // 不是本包的 DynamicToken，但带输入参数 —— 可能是 CP 内建 / 全局令牌，
                // 也可能是别的 mod 的（`作者.模组/令牌`）。**不碰它本身**，只继续往里找能代的。
                unresolved.Add(name);
                Collect(tk.InputArgs!.Parts, stack, unresolved, lossy, determinized, ref replacements);
                continue;
            }
            else if (!_configTokens.TryGetValues(name, out IReadOnlyList<string> configValues))
            {
                // ★ CP 内建里那几个**运行时单值**、但产出已经确定化到固定取值的令牌。
                //   不代它们，包内 DynamicToken 的 Value 就展开不出来（`CurrentSeasonAttire = {{Season}}`），
                //   进而 `FromFile: …/emojis_{{CurrentSeasonAttire}}.png` 整条 TargetMissing
                //   —— 实测 `[CP] Seasonal Cute Characters` 的 emoji/日历图。
                //   政策与 `ConfigTokenSubstitutor`/`Baker` 完全一致：Season→spring、Language→zh、FarmType→Standard。
                value = FrozenBuiltinValue(name);
                if (value != null) determinized.Add(name);
                else unresolved.Add(name);
            }
            else
            {
                value = string.Join(", ", configValues);   // CP 多值令牌的文本形式（TokenString.cs:289）
            }

            if (value != null && tk.Start >= 0 && tk.End > tk.Start)
                (replacements ??= new List<Replacement>()).Add(new Replacement(tk.Start, tk.End, value));
        }
    }

    /// <summary>
    /// CP **内建值令牌**里那两个「代不了但可以确定化」的：<c>Random</c> 与 <c>Range</c>。
    /// <para>
    /// <b>依据</b>（<c>ContentPatcher.Framework.Tokens.ValueProviders</c>）：
    /// <list type="bullet">
    /// <item><c>RangeValueProvider</c>：<c>{{Range: min,max}}</c>（可选 <c>|step=</c>）给出
    ///   <c>min..max</c> 的**多值集合**；作为值渲染时按 CP 的多值文本形式逗号连接 —— 这是**确定的**；</item>
    /// <item><c>RandomValueProvider</c>：<c>{{Random: a, b, c}}</c> 从**输入的位置参数**里取一个，
    ///   而种子 = <c>(BaseSeed, hash(补丁路径))</c>，<c>BaseSeed</c> 又由**存档 id + 天数**算出、
    ///   且**每次上下文更新都会重掷**。⇒ **没有「正确的那一个」**：每个备选都是 CP 可能产出的值。
    ///   我们取**第一个**并把它记进 <see cref="Expansion.Determinized"/>（与 <c>Lossy</c> 分开：
    ///   那是「猜错了会错」，这是「怎么选都对，只是不再随机」）。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>为什么值得做</b>：语料实测 46 条补丁卡在 <c>{{Random}}</c> 上 —— 一条婚姻日程里
    /// 「走到随机一个坐标」被整条丢掉，等于**那天没有日程**；而把 <c>{{Random:22,…,28}}</c>
    /// 定在 22 只是「不再随机」，是这台机器**本来就会产出**的值。与「配置冻结在默认值上」同一类确定化。
    /// </para>
    /// <para>认不出的形态一律返回 <c>false</c>（原样保留，由那道闸拒绝）—— 不猜。</para>
    /// </summary>
    private bool TryResolveBuiltin(LexTokenToken tk, HashSet<string> stack, List<string> unresolved,
        List<string> lossy, List<string> determinized, out string? value)
    {
        value = null;
        if (tk.InputArgs == null) return false;   // 这两个令牌的输入参数是**必填**的

        string input = tk.InputArgs.ToString();

        if (tk.Name.Equals("Range", StringComparison.OrdinalIgnoreCase))
        {
            // 顶层逗号切分（词法器：切不动令牌内部）。恰两个位置参数 min,max；`|step=` 可选。
            List<string> args = CpLexer.Instance.SplitLexically(input, ",").Select(s => s.Trim()).ToList();
            string? stepText = null;
            int pipe = args.FindLastIndex(a => a.StartsWith("|"));
            if (pipe >= 0)
            {
                string tail = string.Join(",", args.Skip(pipe));
                args = args.Take(pipe).ToList();
                int eq = tail.IndexOf('=');
                if (eq < 0) return false;
                string key = tail[1..eq].Trim();
                if (!key.Equals("step", StringComparison.OrdinalIgnoreCase)) return false;
                stepText = tail[(eq + 1)..].Trim();
            }
            if (args.Count != 2) return false;
            if (!int.TryParse(args[0], out int min) || !int.TryParse(args[1], out int max)) return false;
            int step = 1;
            if (stepText != null && !int.TryParse(stepText, out step)) return false;
            if (step <= 0 || min > max) return false;

            int count = (max - min) / step + 1;
            if (count > 5000) return false;   // CP 的上限
            value = string.Join(", ", Enumerable.Range(0, count).Select(i => (min + step * i).ToString()));
            return true;
        }

        // ★ `{{Lowercase: …}}` / `{{Uppercase: …}}`（`LetterCaseValueProvider.cs:41-42`）。
        //   实测 Bear Family 的婚后对话把 `{{Random: {{Lowercase: {{Player Pronouns 1}}}}}}`
        //   塞进 i18n 的具名参数；这里不代，整批 89 条婚后对话全部被拒（NPC 婚后只会说通用台词）。
        if (tk.Name.Equals("Lowercase", StringComparison.OrdinalIgnoreCase)
            || tk.Name.Equals("Uppercase", StringComparison.OrdinalIgnoreCase))
        {
            string expandedInput = Expand(input, stack, unresolved, lossy, determinized);
            if (TokenScan.HasUnresolved(expandedInput)) return false;
            value = tk.Name.Equals("Lowercase", StringComparison.OrdinalIgnoreCase)
                ? expandedInput.ToLowerInvariant()
                : expandedInput.ToUpperInvariant();
            return true;
        }

        // ★ `{{HasFlag: …|contains=X}}` / `{{HasSeenEvent: …|contains=X}}` 用在 i18n 键里
        //   （`{{i18n:HasMetHector.{{HasSeenEvent |contains=…}}}}`）。存档状态离线不可知，
        //   但两种键（`.true` / `.false`）都存在 ⇒ 冻结为 `false`（"还没发生过"），
        //   写进产物至少是一句通顺的台词。记进 `Determinized`，报告里说清楚是冻结值。
        if ((tk.Name.Equals("HasFlag", StringComparison.OrdinalIgnoreCase)
             || tk.Name.Equals("HasSeenEvent", StringComparison.OrdinalIgnoreCase))
            && input.Contains("contains=", StringComparison.OrdinalIgnoreCase))
        {
            value = "false";
            determinized.Add($"{tk.Name}（存档状态不可知，冻结为 false）");
            return true;
        }

        // ★ `{{Query: 15 + 2}}` —— **只认纯字面量算式**（整数与 `+ - * / %`、括号）。
        //   实测（`Lnh's Farms Convenient Road`）：包里用 **ConfigSchema + DynamicTokens** 拼出
        //   `{{Query:{{Map_Coordinates_Y}} + 2}}`，patch 里再 `AddWarps: "26 35 Farm {{Query:58}} …"`。
        //   CP 会算成数字写出去；我们只代了**内层**、把 `{{Query:58}}` 留成残留 ⇒
        //   整条 `Warp` 带着花括号进产物 ⇒ 游戏
        //   `Failed parsing warp … got a non-numeric value for one of the X/Y positions`
        //   ⇒ **那一整串传送全废**（实机 10 行，位置 `Custom_ConvenientRoad`）。
        //   ⚠ 顺序：**先代内层、再对结果求值**（与 `Random` 那条路同一做法）；
        //     内层代换**不单独记替换** —— 外层代成功时整段令牌会被一个数字换掉，内层的偏移就失效了。
        //   ⚠ 代完仍是令牌/字母 ⇒ **不代**（运行时语义，猜不得；调用方会把内层留着并报出来）。
        if (tk.Name.Equals("Query", StringComparison.OrdinalIgnoreCase))
        {
            string expandedInput = Expand(input, stack, unresolved, lossy, determinized);
            if (!LiteralQueryEvaluator.TryEvaluate(expandedInput, out string queryValue)) return false;

            value = queryValue;
            return true;
        }

        if (!tk.Name.Equals("Random", StringComparison.OrdinalIgnoreCase)) return false;

        // ★ 末尾的**具名参数** `|name=value`（可以有多个）：
        //   · `inputSeparator=<分隔符>` —— CP 用它声明备选之间的分隔符（默认才是逗号）⇒ **照做**；
        //   · `key=<名>` —— 只影响运行时「这次掷点用哪个键共享」，对「确定化取第一个」没有影响
        //     ⇒ **接受并忽略**。（2026-10-14 实测：把它当"认不出"拒了，直接让 4 个资产不写。）
        //   · 其余具名参数**不猜** ⇒ 返回 false，令牌原样保留、由那道闸拒绝。
        //
        //   实机来源（2026-10-14）：`NPC 'RaccoonSBV' failed to parse master schedule 'Mon'` ——
        //   包里写的是 `{{Random: 0 Forest …++0 Forest …|inputSeparator=++}}`。原先只按逗号切、
        //   又不认这个参数 ⇒ 「整串（含 `++` 与参数本身）」被当成**一个备选**写进产出 ⇒
        //   游戏拿到解析不了的日程；而残渣里已经没有 `{{` ⇒ `unexpandedTokens` 自检也看不见。
        //   （同一类残渣还污染了一大批 NPC 对白：多个备选被 `++` 拼在一起。）
        string separator = ",";
        while (true)
        {
            int argPipe = LastTopLevelPipe(input);
            if (argPipe < 0) break;
            string tail = input[(argPipe + 1)..];
            int eq = tail.IndexOf('=');
            string name = eq > 0 ? tail[..eq].Trim() : "";
            // 只有**看起来像具名参数**（`|标识符=…`）才当参数，免得把值里合法的 `|` 误吞
            bool looksLikeArg = name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_');
            if (!looksLikeArg) break;
            if (name.Equals("inputSeparator", StringComparison.OrdinalIgnoreCase))
            {
                separator = tail[(eq + 1)..].Trim();
                if (separator.Length == 0) return false;
            }
            else if (!name.Equals("key", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            input = input[..argPipe].TrimEnd();
        }

        // CP 用的是**展平后的位置参数**：`{{Random:{{Range:0,5}}}}` 里那个 Range 贡献 6 个备选，
        // 不是「一个字符串」。所以每一段先各自展开，再按逗号摊平。
        var alternatives = new List<string>();
        foreach (string piece in CpLexer.Instance.SplitLexically(input, separator))
        {
            string expanded = Expand(piece.Trim(), stack, unresolved, lossy, determinized);
            if (separator != ",")
            {
                // 声明了分隔符 ⇒ 它就是唯一的切分依据（CP 的语义），不再按逗号摊平
                if (expanded.Trim().Length > 0) alternatives.Add(expanded.Trim());
                continue;
            }
            foreach (string part in CpLexer.Instance.SplitLexically(expanded, ","))
                if (part.Trim().Length > 0) alternatives.Add(part.Trim());
        }
        if (alternatives.Count == 0) return false;

        value = alternatives[0];
        determinized.Add("Random");
        return true;
    }

    /// <summary>
    /// 令牌体里**顶层**（不在内层 <c>{{…}}</c> 里）的最后一个 <c>|</c> —— 具名参数只可能出现在这一层。
    /// <para>
    /// ⚠ 不能用 <c>LastIndexOf('|')</c>：实测（2026-10-14，`[CP] Custom NPC Lani`）体内嵌着
    /// <c>{{X|key=…}}</c> 时，那样会**从内层令牌中间截断**（切成 <c>{{X</c>）⇒ 词法器
    /// `Reached end of input, expected '}}'` ⇒ 整个包致命错误（那份产出 71 个资产一个都没写出来）。
    /// </para>
    /// </summary>
    private static int LastTopLevelPipe(string text)
    {
        int depth = 0;
        int found = -1;
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
            else if (text[i] == '|' && depth == 0)
            {
                found = i;
            }
        }
        return found;
    }

    /// <summary>
    /// **只**代 CP 内建里那几个**上下文无关**的令牌（<c>Random</c> / <c>Range</c>，
    /// 以及 `Query` 的**纯字面量算式**），其余令牌原样保留。
    /// <para>
    /// <b>用途</b>：<c>Target</c>/<c>FromFile</c> 里的 <c>{{Random: a.png, b.png}}</c> ——
    /// CP 会挑一个文件，我们代不了就整条不写；代成**第一个**只是「不再随机」。
    /// （这两个字段的**配置/动态令牌**由 <c>ConfigTokenSubstitutor</c> 负责，这里不越界。）
    /// </para>
    /// </summary>
    public string DeterminizeBuiltins(string text)
    {
        if (!CpLexer.Instance.MightContainTokens(text)) return text;
        if (!CpLexer.Instance.TryParse(text, out IReadOnlyList<ILexToken> tokens)) return text;

        var replacements = new List<Replacement>();
        var unresolved = new List<string>();
        var lossy = new List<string>();
        var determinized = new List<string>();
        Collect(tokens, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            unresolved, lossy, determinized, ref replacements, builtinsOnly: true);
        if (replacements == null) return text;

        var sb = new StringBuilder(text);
        foreach (Replacement r in replacements.OrderByDescending(r => r.Start))
        {
            sb.Remove(r.Start, r.End - r.Start);
            sb.Insert(r.Start, r.Value);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 把 <c>When</c> 的**键名**切出来：去掉开头的 <c>!</c>、去掉 <c>:</c>/<c>|</c> 之后的输入参数。
    /// <para>
    /// 公开给自检用（<c>TokenConditionAudit</c> 要按同一口径认"这个键是不是本包令牌"）——
    /// **只此一处**，别再手写第二份切法（本项目在 <c>{{…}}</c> 结构上已经吃过手写匹配的亏）。
    /// </para>
    /// </summary>
    public static string ConditionKeyName(string key)
    {
        if (key.StartsWith('!')) key = key[1..].Trim();
        int cut = key.IndexOfAny(new[] { ':', '|' });
        return (cut >= 0 ? key[..cut] : key).Trim();
    }

    /// <summary>
    /// 判一条 <c>When</c>，**含「键是本包另一个 DynamicToken」那一档**（CP 认这种写法）。
    /// <para>
    /// 三道闸（都是被 `[CP] Seasonal Cute Characters` 的
    /// `SeasonalShoes → SeasonToLoad → {{Season}}` 链逼出来的）：
    /// ① 只在真有令牌键时才另建对象，且复制条目**必须深拷贝**（Json.NET 会把子节点从原父节点摘走
    /// ⇒ 直接搬会把 `block.When` 清空 ⇒ 那块被当成"无条件" ⇒ 选错块）；
    /// ② **有损**判出来的令牌不当条件（值落到默认块 / 用了确定性备选）；
    /// ③ 值里**还有没代掉的令牌**时也不当条件（内建 `{{Season}}` 那种运行时值，比字面量必错）。
    /// ②③ 都退回老行为：整条判不了 ⇒ 调用方按"判不了 → 视为不匹配 → 落到默认块"走。
    /// </para>
    /// </summary>
    private WhenResult EvaluateWhen(JObject when, List<string> lossy, List<string> determinized,
        int depth = 0)
    {
        // 令牌条件互相引用（A 的 When 问 B、B 的 When 问 A）会无限递归；StackOverflow 捕不住。
        if (depth > 32) return new WhenResult(null, "DynamicToken 条件嵌套过深（>32）");
        // 没有本地令牌键时**原样把原对象交出去**（连对象同一性都不变）——下游有按 When 对象做键的结构。
        // ⚠ ConfigSchema 字段也是本地令牌（`When: {"Meredith Portrait Style": "Nyapu"}`），
        //   只查 `_blocks` 会把它漏给外部求值器 ⇒ 判不了 ⇒ NPC 条目不写。
        if (!when.Properties().Any(p =>
        {
            string n = ConditionKeyName(p.Name);
            return _blocks.ContainsKey(n) || _configTokens.Contains(n);
        }))
        {
            return _evaluateWhen(when);
        }

        JObject? rest = null;
        foreach (JProperty property in when.Properties())
        {
            string bare = property.Name.StartsWith('!') ? property.Name[1..].Trim() : property.Name;
            int cut = bare.IndexOfAny(new[] { ':', '|' });
            string name = (cut >= 0 ? bare[..cut] : bare).Trim();
            string? args = cut >= 0 ? bare[cut..] : null;
            bool negated = property.Name.StartsWith('!');

            // 本地令牌：包内 DynamicTokens **和 ConfigSchema 字段**都算。ConfigSchema 字段
            // 以前漏在这里 ⇒ `When: { "Meredith Portrait Style": "Nyapu" }` 被交给外部求值器，
            // 它不认识配置字段 ⇒ 判不了 ⇒ 令牌无值 ⇒ 整条 NPC 条目不写
            // （实测 Rodney/Meredith）。
            bool isBlock = _blocks.ContainsKey(name);
            bool isConfig = !isBlock && _configTokens.Contains(name);
            if (!isBlock && !isConfig)
            {
                (rest ??= new JObject())[property.Name] = property.Value.DeepClone();
                continue;
            }

            string? value;
            IReadOnlyList<string>? configValues = null;
            var localLossy = new List<string>();
            var localDeterminized = new List<string>();
            if (isBlock)
            {
                value = ResolveValue(name, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new List<string>(), localLossy, localDeterminized, depth + 1);
            }
            else
            {
                value = _configTokens.TryGetValues(name, out configValues)
                    ? string.Join(", ", configValues)
                    : null;
            }
            // ⚠ 判不了时返回 **false（这一块不生效）而不是"判不了"** —— 与老路径**逐字一致**：
            //   老路径把这个键交给 `Baker`，而它把**不认识的键当作不成立**（false），
            //   于是那块被跳过、**且不把令牌标成 lossy**。要是这里返回 null（判不了），
            //   调用方会把令牌标 `Lossy` ⇒ `ConfigTokenSubstitutor`「有损一律不代」⇒
            //   整条补丁的 FromFile 原样留着 `{{SeasonalClothes}}` ⇒ **补丁被拒**
            //   （实测 SCC 的 2 条农民立绘，231/334 vs 基线 233/334，就是这一处造成的）。
            if (value == null || localLossy.Count > 0 || localDeterminized.Count > 0
                || value.Contains("{{", StringComparison.Ordinal))
                return new WhenResult(false);

            bool holds;
            if (args != null && args.StartsWith("|contains=", StringComparison.OrdinalIgnoreCase))
            {
                string want = args["|contains=".Length..].Trim();
                // ⚠ 两边都可能是**集合**：值 `A,B` 与期望 `A, B` 都要按 any-of 比，
                //   子串匹配还会把 `No` 误命中 `None`。
                string[] wants = want.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                IReadOnlyList<string> have = configValues
                    ?? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                bool contains = have.Any(v =>
                    wants.Any(w => v.Equals(w, StringComparison.OrdinalIgnoreCase)));
                // `|contains=` 折出来的是布尔；声明值写成字符串 "false" 也是取反。
                // 实测 DaisyNiko 的 `"ManualRecolourSelection |contains=Off": "false"` ——
                // 原来只认 JSON 布尔，字符串 "false" 被当成"只要命中"，于是 Off 的块反而生效。
                bool? expectedBool = property.Value.Type == JTokenType.Boolean
                    ? property.Value.Value<bool>()
                    : bool.TryParse(property.Value.Value<string>(), out bool parsed) ? parsed : null;
                holds = expectedBool != null && contains == expectedBool.Value;
            }
            else if (property.Value.Type == JTokenType.Boolean)
            {
                bool expected = property.Value.Value<bool>();
                holds = configValues != null
                    ? configValues.Any(v => bool.TryParse(v, out bool b) && b == expected)
                    : bool.TryParse(value, out bool actual) && actual == expected;
            }
            else
            {
                // CP 的比较值按**逗号集合**处理：`"ManualRecolourSelection": "Off, Vanilla"`
                // 在配置值是 `Off` 时成立。原来整体字符串比 ⇒ 永远不等 ⇒ 落到了后面
                // `|contains=Off` 那一块（值 `{{ManualRecolourSelection}}` = Off），
                // 于是 DaisyNiko 的 `assets/{{recolour}}/…` 变成 `assets/Off/…` 全部缺失。
                string[] wants = (property.Value.Value<string>() ?? "").Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                holds = configValues != null
                    ? configValues.Any(v => wants.Any(w => v.Equals(w, StringComparison.OrdinalIgnoreCase)))
                    : wants.Any(w => string.Equals(value, w, StringComparison.OrdinalIgnoreCase));
            }
            if (holds == negated) return new WhenResult(false);   // 不成立 ⇒ 这一块不生效
        }

        return rest == null ? new WhenResult(true) : _evaluateWhen(rest);
    }

    /// <summary>解析出块的值；<c>null</c> = 判不了（不猜）。</summary>
    private string? ResolveValue(string name, HashSet<string> stack, List<string> unresolved,
        List<string> lossy, List<string> determinized, int depth = 0,
        int maxIndexExclusive = int.MaxValue)
    {
        if (depth > 32)
        {
            unresolved.Add($"{name}（条件嵌套过深）");
            return null;
        }
        List<Block> blocks = _blocks[name];

        // 从后往前找"最后一个确定匹配"的块：找不到匹配 → 无值。
        //
        // ⚠ **判不了的块当作「不匹配」继续往前找**（而不是整条判不了）。
        //   依据：语料的形态是「**无条件默认块在列表最前**，条件块在后」——
        //   一撞上判不了的 When 就放弃，会永远落不到那个默认块。
        //   这是**有损确定化**：运行时条件若成立，真值 ≠ 我们选的默认值。
        //   所以命中的令牌名要记进 `lossy`，报告必须标出来，不得混进「无损」。
        //
        // ⚠ **选中块的值代不掉时也退回更早的块**：语料里常见
        //   「无条件默认值 + `When: HasMod: 某 DLL 模组` 时改成 `{{作者.模组/令牌}}`」，
        //   而 DLL 模组的令牌离线不可知。原先直接把该值原样返回（含字面 `{{…}}`），
        //   下游看到残留令牌就拒掉整条补丁 —— 实测 `[CP] GiEX redux` 的
        //   `PortraitStyle` 因此让 Javi 的 3 张立绘、年轻时 4 张立绘等全部不写。
        //   退到包作者自己写的默认块，至少资产存在；同样记 `lossy`（运行时可能不同）。
        // `maxIndexExclusive` 只给“自引用追加”用：解析 `{{Self}}` 时只看它**前面**的块。
        int scanFrom = Math.Min(blocks.Count, maxIndexExclusive) - 1;
        bool sawUnjudgeable = false;

        while (true)
        {
            Block? chosen = null;
            int chosenIndex = -1;
            for (int k = scanFrom; k >= 0; k--)
            {
                Block block = blocks[k];

                if (block.When == null || !block.When.HasValues) { chosen = block; chosenIndex = k; break; }

                WhenResult r = EvaluateWhen(block.When, lossy, determinized, depth + 1);
                if (r.Value == true) { chosen = block; chosenIndex = k; break; }
                if (r.Value == null) { sawUnjudgeable = true; continue; }   // 判不了 → 视为不匹配，继续找
                // r.Value == false → 该块不生效，继续往前找
            }

            if (chosen == null)
            {
                // 没有任何块**确定匹配**。若全部块都因运行时条件判不了（`sawUnjudgeable`），
                // 说明这是「按存档状态二选一」的令牌（实测 Rodney/Nora 的 `*Datability`：
                // 两块分别是「没在约会→false」「在约会→true」）—— 纯 XNB 必须选一个，
                // 取**包作者写的第一块**（语料约定 = 默认状态），记 lossy。
                // 若全是**静态为假**（真·无值），仍返回 null，不猜。
                if (!sawUnjudgeable || blocks.Count == 0 || !_allowFirstBlock) return null;
                // 取**第一个非静态为假**的块（可能是无条件块，或条件"真/判不了"的块）；
                // 全都静态为假 ⇒ 令牌确实无值，不能取 blocks[0]。
                for (int k = 0; k < blocks.Count; k++)
                {
                    Block b = blocks[k];
                    if (b.When == null || !b.When.HasValues) { chosen = b; chosenIndex = k; break; }
                    WhenResult rr = EvaluateWhen(b.When, lossy, determinized, depth + 1);
                    if (rr.Value != false) { chosen = b; chosenIndex = k; break; }
                }
                if (chosen == null) return null;
                lossy.Add(name);
            }

            // ★ **自引用是 CP 的“追加”写法**，不是环：
            //   `[{Value: base}, {Value: "{{Self}} extra", When: …}]` 表示在基础值后追加。
            //   CP 的 `AddDynamicToken` 用 `GetTokensUsed().GetWithout(name)` 明确排除自身，
            //   `UpdateContext` 按顺序逐块替换 —— 后面的块拿到的是**前面已算出的值**。
            //   这里把 `{{Self}}` 换成“该块之前最近的匹配块”的值（再往前递归），
            //   于是多块追加能按顺序叠起来；真正互相引用的 A↔B 仍走成环路径。
            string chosenValue = chosen.Value;
            if (chosenIndex > 0)
            {
                string self = "{{" + name + "}}";
                if (chosenValue.Contains(self, StringComparison.OrdinalIgnoreCase))
                {
                    var priorStack = new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase);
                    priorStack.Remove(name);
                    var priorUnresolved = new List<string>();
                    string? prior = ResolveValue(name, priorStack, priorUnresolved, lossy, determinized,
                        depth + 1, chosenIndex);
                    if (prior != null && priorUnresolved.Count == 0)
                        chosenValue = chosenValue.Replace(self, prior, StringComparison.OrdinalIgnoreCase);
                }
            }

            // 值自身可含其它令牌（含动态令牌）→ 递归展开；栈用于防环。
            // ⚠ 用**局部** unresolved：退回更早的块时，被放弃那块的残留令牌不算数。
            var localUnresolved = new List<string>();
            bool added = stack.Add(name);
            string expanded = Expand(chosenValue, stack, localUnresolved, lossy, determinized);
            if (added) stack.Remove(name);

            if (localUnresolved.Count == 0)
            {
                // 落到了默认块、但前面确实有判不了的块，或退回过程发生过 → 这一选择是有损的。
                if (sawUnjudgeable) lossy.Add(name);
                return expanded;
            }

            // 只有**外部模组令牌**（CP 约定名里带 `/`，如 `mistyspring.GiEXredux.code/PortraitStyle`）
            // 才退回；本包内建/字段令牌代不掉（`{{Target}}`、`{{Weather}}`…）仍按原样返回，
            // 由下游判定处理 —— 那类不该在这里静默换成默认值。
            if (!localUnresolved.All(n => n.Contains('/')))
            {
                unresolved.AddRange(localUnresolved);
                return expanded;
            }

            // 该块的值依赖离线不可知的外部令牌 ⇒ 退到更早的块。
            scanFrom = chosenIndex - 1;
            lossy.Add(name);
            if (scanFrom < 0)
            {
                // 没有更早的块可退：保留原行为，让调用方看到残留令牌并如实报告。
                unresolved.AddRange(localUnresolved);
                return expanded;
            }
        }
    }

    /// <summary>
    /// 已确定化的 CP 内建单值令牌。<b>只在这三个</b>：
    /// <c>Season → spring</c>、<c>Language → zh</c>、<c>FarmType → Standard</c>。
    /// 其余运行时令牌（<c>HasFlag</c>/<c>Weather</c>/…）仍然判"不知道"。
    /// </summary>
    private static string? FrozenBuiltinValue(string name) => name.ToLowerInvariant() switch
    {
        "season" => "spring",
        "language" => "zh",
        "farmtype" => "Standard",
        _ => null,
    };
}
