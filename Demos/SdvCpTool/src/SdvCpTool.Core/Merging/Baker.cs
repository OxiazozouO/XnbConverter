using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 通道 C：把<b>可烘焙</b>的条件（<c>HasMod</c>/<c>ModId</c>/<c>HasFile</c>）离线求值成真/假。
/// <para>
/// CP 把这几个令牌注册成 <c>ImmutableValueProvider</c>（TokenManager.cs:268,286）—— 对给定的
/// mod 集与内容包，值在一次会话内不可变，所以固化的语义是对的：<b>真</b> → 补丁降级为无条件；
/// <b>假</b> → 补丁整个不适用（跳过，不是"阻塞"）。
/// </para>
/// <para>
/// 求值不了的条件（如混进 <c>Season</c>）一律报 <see cref="Result.Success"/>=false —— 绝不能默认成真。
/// </para>
/// <para>
/// 另支持<b>命名空间令牌</b><c>模组ID/令牌名</c>：命名空间就是提供该令牌的 mod，值读它的
/// <c>config.json</c>（装在 <c>Mods/</c> 里的那份；没有就求不了值，相关条件判 NeedsReview）。
/// 这类结果在 <see cref="Result.Note"/> 里显式标注来源，与 <c>HasMod</c> 的烘焙区分开。
/// </para>
/// </summary>
public sealed class Baker
{
    private readonly InstalledModIndex _mods;
    private readonly string _packRoot;
    private readonly string _packId;

    /// <summary>本内容包的配置令牌（<c>content.json</c> 的 <c>ConfigSchema</c> + 包根 <c>config.json</c>）。</summary>
    private readonly ConfigSchemaTokens _configTokens;

    /// <summary>
    /// 包内 <c>DynamicTokens</c> 的解析器（**构造后由调用方接上**：解析器求 <c>When</c> 时又要反过来问
    /// 这个 <see cref="Baker"/>，两边互相持有）。为 <c>null</c> 时不认包内令牌（旧行为）。
    /// </summary>
    public DynamicTokenResolver? DynamicTokens { get; set; }

    public Baker(InstalledModIndex mods, string packRoot, string packId)
    {
        _mods = mods;
        _packRoot = packRoot;
        _packId = packId;
        _configTokens = ConfigSchemaTokens.Load(packRoot);
    }

    /// <summary>求值结果。<c>Success=false</c> = "我不知道"（求值失败）；<c>Success=true</c> 时 <c>Value</c> 才是结论。</summary>
    /// <param name="Note">
    /// 求值依据（成功时的来源说明）。命名空间令牌会写明"按提供 mod 的 config.json 求值"，
    /// 以与 <c>HasMod</c> 的"已装模组索引"烘焙区分开。普通烘焙为 <c>null</c>。
    /// </param>
    public sealed record Result(bool Success, bool Value, string? FailureReason = null, string? Note = null);

    public Result Evaluate(WhenNode when) => Eval(when);

    /// <summary>
    /// 只对<b>一个 Key 节点</b>求值，供常量折叠使用（<see cref="SdvCpTool.Core.Conditions.ConstantFolder"/>）。
    /// <para>
    /// <c>true</c>/<c>false</c> = 已确定；<c>null</c> = <b>求不了值</b>（"我不知道"）——
    /// 绝不能当成 <c>false</c>。判定与折叠共用同一个 <see cref="EvalKey"/>，不另写第二份实现。
    /// </para>
    /// </summary>
    public bool? TryEvaluateLeaf(WhenNode key) =>
        key.Kind == WhenNodeKind.Key && EvalKey(key) is { Success: true } r ? r.Value : null;

    private Result Eval(WhenNode node)
    {
        switch (node.Kind)
        {
            case WhenNodeKind.All:
            {
                var notes = new List<string>();
                foreach (WhenNode c in node.Children)
                {
                    Result r = Eval(c);
                    if (!r.Success) return r;
                    if (r.Note != null) notes.Add(r.Note);
                    if (!r.Value)                                            // 已确定整条为假，短路
                        return new Result(true, false, null,
                            notes.Count == 0 ? null : string.Join("；", notes));
                }
                // 来源说明必须向上传递（WhenParser 顶层总包一层 All），否则报告里看不到求值依据。
                return new Result(true, true, null,
                    notes.Count == 0 ? null : string.Join("；", notes));
            }
            case WhenNodeKind.Any:
            {
                foreach (WhenNode c in node.Children)
                {
                    Result r = Eval(c);
                    if (!r.Success) return r;
                    if (r.Value) return new Result(true, true, null, r.Note);   // 已确定整条为真，短路
                }
                return new Result(true, false);
            }
            case WhenNodeKind.Not:
            {
                if (node.Children.Count == 0) return new Result(false, false, "NOT 没有子条件");
                Result r = Eval(node.Children[0]);
                return r.Success ? new Result(true, !r.Value, null, r.Note) : r;
            }
            case WhenNodeKind.Key:
                return EvalKey(node);
            default:
                return new Result(false, false, $"未知节点类型 {node.Kind}");
        }
    }

    private Result EvalKey(WhenNode key)
    {
        string name = (key.KeyName ?? "").Trim();

        switch (name.ToLowerInvariant())
        {
            case "hasmod":
                return EvalHasMod(key.Arguments, key.Value);
            case "modid":
                return EvalModId(key.Value);
            case "hasfile":
                return EvalHasFile(key.Arguments);
            case "firstvalidfile":
                return EvalFirstValidFile(key.Arguments, key.Value);
            case "season":
                // ★ **季节确定化**：静态 XNB 树里没有"现在是什么季节"这回事，本项目对**值**上的
                //   `{{Season}}` 早就定了政策 —— 一律冻结到**春天**（见 `ConfigTokenSubstitutor.DeterminizeSeason`
                //   与文档里的"季节确定化"）。地图**条件**上的 `Season` 用同一条政策，否则
                //   "季节才出现的路/桥/入口"会整批消失（实测 9 张原版地图的 warp 变少，玩家看到的就是
                //   "地图之间的传送不见了"）。⚙ 代价说清楚：按春天烤 ⇒ 别的季节的**结构**变体不再出现
                //   （结构不像图集那样能按季换名），但至少春天那一版是完整的 —— 比"一律不出现"好。
                return EvalSeason(key.Arguments, key.Value);
            case "farmtype":
                // ★ **农场类型确定化**（与 `Season` 同一条政策：存档是固定的，静态 XNB 只能支持一种农场）。
                //   `FarmType` 读的是**存档**里选的农场类型，离线本来不知道；但同一块地图上各家农场模组
                //   会用 `FarmType` 写**互斥**的变体补丁 —— 全都不写会丢通路，全写则**后写的覆盖先写的**。
                //   实测（2026-10-19）：Frontier Farm 的 `Maps/BusStop` 变体覆盖了 Grandpa's Farm 的，
                //   把返程传送写成 `Farm 142 16`；本产物的农场图是 Grandpa's Farm（140 宽）⇒
                //   玩家被传到**地图外**卡死（实机："传送到巴士站……卡到地图外了"）。
                //   本项目产出面向 Standard 存档（Grandpa's Farm 也是装在 Standard 上的），故冻结到 Standard。
                return EvalFarmType(key.Arguments, key.Value);
            case "language":
                // ★ **语言确定化**（与 Season/FarmType 同一条政策：运行时单值，产出只能选一种）。
                //   实测（2026-10-19，业主报"主界面/界面提示还是英文"）：界面包用
                //   `Language |contains=zh` 分开中英文图；判不了时中文变体永远不生效。
                //   产出面向中文存档（启动配置 languageCode=zh），冻结到 `zh`。
                return EvalLanguage(key.Arguments, key.Value);
            default:
                // 内容包的配置令牌：`content.json` 的 `ConfigSchema` 字段名就是令牌名
                // （CP 注册成内容包本地令牌，ScreenManager.cs:401-407）。值取自包内文件，离线可定。
                if (_configTokens.Contains(name)) return EvalConfigToken(name, key.Arguments, key.Value);

                // 包内 `DynamicTokens`：值由**包自己**给（`content.json` 的 `DynamicTokens` 块），
                // `When` 也由包自己写 ⇒ 读包内文件即可离线求值（与配置字段同一条道理）。
                // 用**写入侧同一个** `Expand`，并遵守"**有损就不代**"：展开后有未代令牌、
                // 有损确定化、或值依赖运行时（如 `{{Season}}`），一律判失败，绝不猜。
                if (DynamicTokens != null
                    && DynamicTokens.Names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    return EvalPackDynamicToken(name, key);
                }

                // 命名空间令牌：`模组ID/令牌名`。命名空间部分就是提供该令牌的 mod 的 UniqueID，
                // 值来自它的 config.json（SMAPI 配置约定）。求不了值一律判失败，绝不猜。
                if (name.Contains('/')) return EvalNamespacedToken(name, key.Value);
                return new Result(false, false,
                    $"`{name}` 不是可烘焙令牌（通道 C 只处理 HasMod/ModId/HasFile、包内 ConfigSchema 配置令牌与 `模组ID/令牌名` 命名空间令牌）");
        }
    }

    /// <summary>
    /// 内容包配置令牌的求值。语义<b>照抄 CP</b>，不是自创：
    /// <list type="bullet">
    /// <item><c>Condition.cs:66</c>：<c>IsMatch = token.GetValues(Input).Any(value =&gt; CurrentValues.Contains(value))</c>
    /// —— 令牌值集合与条件声明值集合<b>有交集即匹配</b>。所以 <c>AllowMultiple: true</c> 的字段天然是
    /// 「包含」语义（<c>{"Goat Skins": "SolidCream"}</c> 在玩家选了 SolidCream + 另外三种时同样成立），
    /// 单值字段退化为相等。</item>
    /// <item><c>Token.cs:97-115</c> <c>GetValues</c>：带 <c>|contains=x</c> 时先把令牌值集合折成
    /// 单个布尔（「与 x 有没有交集」），再与声明值（必须是 true/false）求交集。</item>
    /// </list>
    /// <para>求不了值（不是配置字段、参数不认识、声明值不是布尔…）一律 <c>Success=false</c>，绝不猜。</para>
    /// </summary>
    /// <summary>
    /// 冻结到春天的季节条件：CP 的季节值是 `spring`/`summer`/`fall`/`winter`（不区分大小写）。
    /// <para>
    /// ⚠ 带具名参数 <c>|contains=A, B</c> 时语义**不同**（见方法内说明）—— 漏掉它会把
    /// 「**非**某几季」整批复读成假。
    /// </para>
    /// </summary>
    private static Result EvalSeason(string? arguments, string? declaredValue)
    {
        const string frozen = "spring";
        IReadOnlyList<string> declared = SplitValues(declaredValue);
        if (declared.Count == 0)
            return new Result(false, false, "季节条件没有给出要比较的值");

        IReadOnlyList<string>? contains = ContainsValues(arguments, out string? unknownArgs);
        if (unknownArgs != null)
            return new Result(false, false,
                $"季节条件的参数 `{unknownArgs}` 无法识别（CP 的季节令牌只接受 |contains=）");

        if (contains != null)
        {
            // ★ `Season |contains=Summer, Fall, Winter: false` **不是**「季节等于 …」：
            //   CP 的 `contains` 具名参数（`Token.cs:144-147` ⇒ `ApplyContains`）先把令牌的值集合
            //   与参数求交集、折成 **true/false 字符串**，**再**与声明值比 —— 因此声明 `false`
            //   就是**取反**（CP 官方文档 tokens.md 的例子：`"Year |contains=1": "false"`，
            //   注释写着 `// NOT year 1`）。
            //   漏掉参数这一步，实测 SVE 一个包 **365 处**（`Season |contains=Summer, Fall, Winter: false`
            //   配 `Season: Spring`）被判成"本就不适用"，整条内容丢掉 —— 这正是
            //   "季节怎么也丢"里**能修的那一半**（另一半是地图没有按季通道，只能留春天）。
            if (contains.Count == 0)
                return new Result(false, false, "季节条件的 |contains= 后面没有值");

            bool overlap = contains.Any(w => string.Equals(w.Trim(), frozen, StringComparison.OrdinalIgnoreCase));
            if (!TryDeclaredBools(declared, "季节条件", out List<bool> expected, out Result? failure))
                return failure!;

            bool hit = expected.Contains(overlap);
            return new Result(true, hit,
                $"按「季节确定化到春天」求值：`Season |contains={string.Join(", ", contains)}` ⇒ 与春天"
                + $"{(overlap ? "有" : "没有")}交集（{overlap.ToString().ToLowerInvariant()}），"
                + $"条件值 `{string.Join(", ", declared)}` ⇒ {(hit ? "成立" : "不成立")}");
        }

        bool match = declared.Any(d => string.Equals(d.Trim(), frozen, StringComparison.OrdinalIgnoreCase));
        return new Result(true, match, null, $"按「季节确定化到春天」求值：声明 {string.Join("/", declared)} ⇒ {match}");
    }

    /// <summary>
    /// 冻结到的农场类型。取 <c>Standard</c>：本产出面向标准农场存档（Grandpa's Farm 等也是**装在
    /// Standard 上**的整图替换）。与 <c>Season → 春天</c> 一样是**确定化**：把运行时的单一取值写死。
    /// </summary>
    public const string FrozenFarmType = "Standard";

    /// <summary>
    /// 农场类型条件按 <see cref="FrozenFarmType"/> 求值（语义与 <see cref="EvalSeason"/> 相同，
    /// 包括 <c>|contains=</c> 先折成布尔再与声明值比）。
    /// </summary>
    private static Result EvalFarmType(string? arguments, string? declaredValue)
    {
        const string frozen = FrozenFarmType;
        // 声明值里还有没展开的令牌（如 `{{LNHNewFarmReplacement}}`）⇒ 展开后可能恰好是 Standard，
        // 按字面比就是**猜** ⇒ 判"不知道"，绝不落到假。
        if (declaredValue != null && declaredValue.Contains("{{", StringComparison.Ordinal))
            return new Result(false, false,
                $"农场类型条件的值含未展开令牌：`{declaredValue}`");
        IReadOnlyList<string> declared = SplitValues(declaredValue);
        if (declared.Count == 0)
            return new Result(false, false, "农场类型条件没有给出要比较的值");

        IReadOnlyList<string>? contains = ContainsValues(arguments, out string? unknownArgs);
        if (unknownArgs != null)
            return new Result(false, false,
                $"农场类型条件的参数 `{unknownArgs}` 无法识别（`FarmType` 只接受 |contains=）");

        if (contains != null)
        {
            if (contains.Count == 0)
                return new Result(false, false, "农场类型条件的 |contains= 后面没有值");

            bool overlap = contains.Any(w => string.Equals(w.Trim(), frozen, StringComparison.OrdinalIgnoreCase));
            if (!TryDeclaredBools(declared, "农场类型条件", out List<bool> expected, out Result? failure))
                return failure!;

            bool hit = expected.Contains(overlap);
            return new Result(true, hit,
                $"按「农场类型确定化到 {frozen}」求值：`FarmType |contains={string.Join(", ", contains)}`"
                + $" ⇒ 与 {frozen} {(overlap ? "有" : "没有")}交集"
                + $"（{overlap.ToString().ToLowerInvariant()}），条件值 `{string.Join(", ", declared)}`"
                + $" ⇒ {(hit ? "成立" : "不成立")}");
        }

        bool match = declared.Any(d => string.Equals(d.Trim(), frozen, StringComparison.OrdinalIgnoreCase));
        return new Result(true, match, null,
            $"按「农场类型确定化到 {frozen}」求值：声明 {string.Join("/", declared)} ⇒ {match}");
    }

    /// <summary>
    /// 冻结到的游戏语言码。取 <c>zh</c>：本产出面向中文存档（启动配置
    /// <c>languageCode=zh</c>）；CP 的 <c>Language</c> 令牌给出的就是这种短码（文件也按它命名，
    /// 如 <c>logo_zh.png</c>）。
    /// </summary>
    public const string FrozenLanguage = "zh";

    /// <summary>
    /// 语言条件按 <see cref="FrozenLanguage"/> 求值（语义与 <see cref="EvalFarmType"/> 相同）。
    /// </summary>
    private static Result EvalLanguage(string? arguments, string? declaredValue)
    {
        const string frozen = FrozenLanguage;
        IReadOnlyList<string> declared = SplitValues(declaredValue);
        if (declared.Count == 0)
            return new Result(false, false, "语言条件没有给出要比较的值");

        IReadOnlyList<string>? contains = ContainsValues(arguments, out string? unknownArgs);
        if (unknownArgs != null)
            return new Result(false, false,
                $"语言条件的参数 `{unknownArgs}` 无法识别（`Language` 只接受 |contains=）");

        if (contains != null)
        {
            if (contains.Count == 0)
                return new Result(false, false, "语言条件的 |contains= 后面没有值");

            bool overlap = contains.Any(w => string.Equals(w.Trim(), frozen, StringComparison.OrdinalIgnoreCase));
            if (!TryDeclaredBools(declared, "语言条件", out List<bool> expected, out Result? failure))
                return failure!;

            bool hit = expected.Contains(overlap);
            return new Result(true, hit,
                $"按「语言确定化到 {frozen}」求值：`Language |contains={string.Join(", ", contains)}`"
                + $" ⇒ 与 {frozen} {(overlap ? "有" : "没有")}交集"
                + $"（{overlap.ToString().ToLowerInvariant()}），条件值 `{string.Join(", ", declared)}`"
                + $" ⇒ {(hit ? "成立" : "不成立")}");
        }

        bool match = declared.Any(d => string.Equals(d.Trim(), frozen, StringComparison.OrdinalIgnoreCase));
        return new Result(true, match, null,
            $"按「语言确定化到 {frozen}」求值：声明 {string.Join("/", declared)} ⇒ {match}");
    }

    /// <summary>
    /// CP 的具名参数 <c>|contains=A, B</c> ⇒ 要比较的值列表；**没有**该参数 ⇒ <c>null</c>；
    /// 有参数但不是 <c>|contains=</c> ⇒ <paramref name="unknown"/> 填上原文（调用方判失败，不猜）。
    /// </summary>
    private static IReadOnlyList<string>? ContainsValues(string? arguments, out string? unknown)
    {
        unknown = null;
        if (string.IsNullOrWhiteSpace(arguments)) return null;

        string args = arguments.Trim();
        if (!args.StartsWith("|contains=", StringComparison.OrdinalIgnoreCase))
        {
            unknown = arguments;
            return null;
        }
        return SplitValues(args["|contains=".Length..].Trim());
    }

    /// <summary>
    /// 用了 <c>|contains=</c> 时的**声明值**：CP 只接受 true/false
    /// （`Token.cs:87-98` 的 <c>TryValidateValues</c>，其它值 CP 直接判这条补丁失败）—— 我们同样失败，不猜。
    /// </summary>
    private static bool TryDeclaredBools(IReadOnlyList<string> declared, string what,
        out List<bool> expected, out Result? failure)
    {
        expected = new List<bool>(declared.Count);
        foreach (string text in declared)
        {
            if (!bool.TryParse(text, out bool value))
            {
                failure = new Result(false, false,
                    $"{what} 用了 |contains=，条件值必须是 true/false（CP 会拒绝其它值），实际是 `{text}`");
                return false;
            }
            expected.Add(value);
        }
        failure = null;
        return true;
    }

    /// <summary>
    /// 包内 `DynamicTokens` 的求值。CP 把它们注册成包本地令牌（值由包的 <c>DynamicTokens</c> 块给），
    /// 所以对给定的包**离线可定** —— 与配置字段同一档。
    /// <para>
    /// ⚠ 只用**写入侧同一个** <see cref="DynamicTokenResolver.Expand(string)"/> 展开一次：
    /// 展开后仍有未代令牌、或过程里有损确定化、或值依赖运行时（`{{Season}}` 之类），
    /// 一律 <c>Success=false</c>（"我不知道"），**绝不把不知道当假**。
    /// </para>
    /// </summary>
    private Result EvalPackDynamicToken(string name, WhenNode key)
    {
        DynamicTokenResolver.Expansion e = DynamicTokens!.Expand("{{" + name + "}}");
        if (e.Unresolved.Count > 0)
            return new Result(false, false,
                $"包内令牌 `{name}` 展开后仍有未代令牌：{string.Join(", ", e.Unresolved)}");
        if (e.Lossy.Count > 0)
            return new Result(false, false,
                $"包内令牌 `{name}` 的展开有损（{string.Join(", ", e.Lossy)}）—— 按「有损就不代」不表态");
        if (e.Determinized.Count > 0)
            return new Result(false, false,
                $"包内令牌 `{name}` 的值依赖运行时（{string.Join(", ", e.Determinized)}），静态求不了值");

        string value = e.Text.Trim();
        if (value.Length == 0)
            return new Result(false, false, $"包内令牌 `{name}` 展开成空");

        // 比较语义照 CP：令牌的**值集合**与条件声明的值集合有交集即成立。DynamicToken 是单值，
        // 于是退化为（不区分大小写的）相等 —— 布尔字面量 `true`/`false` 也走这一条。
        string source = $"按包内 DynamicTokens 求值：`{name}` = {value}";
        IReadOnlyList<string> declared = SplitValues(key.Value);
        if (declared.Count == 0)
            return new Result(false, false, $"包内令牌 `{name}` 的条件没有给出要比较的值");

        if (key.Arguments is string args && args.Trim().Length > 0)
        {
            // `|contains=A, B`：CP 的集合语义是**任一命中**；值本身也可能是逗号多值
            // （DynamicToken 的值来自多值配置字段时）。
            string want = args.Trim();
            if (!want.StartsWith("|contains=", StringComparison.OrdinalIgnoreCase))
                return new Result(false, false, $"包内令牌 `{name}` 的参数 `{args}` 无法识别");
            string[] wants = SplitValues(want["|contains=".Length..].Trim()).ToArray();
            string[] have = SplitValues(value).ToArray();
            bool hit = have.Any(v => wants.Any(w => v.Equals(w, StringComparison.OrdinalIgnoreCase)));
            bool wantTrue = declared.Any(d => bool.TryParse(d, out bool b) && b);
            return new Result(true, hit == wantTrue, null, source);
        }

        bool match = declared.Any(d => string.Equals(d, value, StringComparison.OrdinalIgnoreCase));
        return new Result(true, match, null, source);
    }

    private Result EvalConfigToken(string name, string? arguments, string? declaredValue)
    {
        if (!_configTokens.TryGetValues(name, out IReadOnlyList<string> values))
            return new Result(false, false, $"`{name}` 不是本包 ConfigSchema 里的配置字段");

        // 配置令牌是 ImmutableValueProvider，不接受任何具名参数；唯一被认的是 CP 保留参数 contains
        // （判据与季节那条**共用**同一个 `ContainsValues`，不写第二份）。
        IReadOnlyList<string>? contains = ContainsValues(arguments, out string? unknownArgs);
        if (unknownArgs != null)
            return new Result(false, false,
                $"配置令牌 `{name}` 的参数 `{unknownArgs}` 无法识别（CP 的配置令牌只接受 |contains=）");

        IReadOnlyList<string> declared = SplitValues(declaredValue);
        if (declared.Count == 0)
            return new Result(false, false, $"配置令牌 `{name}` 的条件没有给出要比较的值");

        string source = $"按内容包配置求值：`{name}` = {(values.Count == 0 ? "（空）" : string.Join(", ", values))}";

        if (contains != null)
        {
            if (contains.Count == 0)
                return new Result(false, false, $"配置令牌 `{name}` 的 |contains= 后面没有值");

            bool overlap = contains.Any(w => values.Contains(w.Trim(), StringComparer.OrdinalIgnoreCase));

            // 带 contains 时声明值只能是 true/false（CP Token.cs:87-98 TryValidateValues）。
            if (!TryDeclaredBools(declared, $"配置令牌 `{name}`", out List<bool> expected, out Result? failure))
                return failure!;

            bool match = expected.Contains(overlap);
            return new Result(true, match,
                Note: $"{source}，|contains={string.Join(", ", contains)} → {overlap.ToString().ToLowerInvariant()}，" +
                      $"条件值 `{string.Join(", ", declared)}` {(match ? "相符" : "不符")}");
        }

        bool any = declared.Any(d => values.Contains(d, StringComparer.OrdinalIgnoreCase));
        return new Result(true, any,
            Note: $"{source}，条件值 `{string.Join(", ", declared)}` {(any ? "有交集" : "无交集")}");
    }

    /// <summary>CP 的逗号分隔值列表（与 <c>ConfigSchemaTokens</c> 同一约定，大小写不敏感）。</summary>
    private static IReadOnlyList<string> SplitValues(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();

    /// <summary>
    /// 命名空间令牌 <c>模组ID/令牌名</c> 的求值。
    /// <para>
    /// 依据：令牌的命名空间部分就是<b>注册它的 mod 的 UniqueID</b>（CP 官方限定形式），
    /// 而这类令牌的值取自该 mod 的 <c>config.json</c>（SMAPI 配置约定）——
    /// 这是有据可依的离线解析路径，不是猜。
    /// </para>
    /// <para>
    /// 找不到该 mod、没有 <c>config.json</c>、config 里没有同名键、值不是标量、
    /// 或条件没给比较值 → <see cref="Result.Success"/>=false（<b>不猜真假</b>）。
    /// </para>
    /// </summary>
    private Result EvalNamespacedToken(string name, string? expectedText)
    {
        int slash = name.IndexOf('/');
        string ns = name[..slash].Trim();
        string token = name[(slash + 1)..].Trim();
        if (ns.Length == 0 || token.Length == 0)
            return new Result(false, false, $"`{name}` 不是合法的命名空间令牌（应为 `模组ID/令牌名`）");

        string? dir = _mods.GetModDirectory(ns);
        if (dir == null)
            return new Result(false, false,
                $"命名空间令牌 `{name}`：未找到 UniqueID 为 `{ns}` 的已装 mod，无法求值");

        // 取值来源只有一个：装好的那个 mod 自己的 config.json（没有就求不了值）。
        JObject? config = _mods.GetModConfig(ns);
        if (config == null)
            return new Result(false, false,
                File.Exists(Path.Combine(dir, "config.json"))
                    ? $"命名空间令牌 `{name}`：mod `{ns}` 的 config.json 解析失败"
                    : $"命名空间令牌 `{name}`：mod `{ns}` 没有 config.json，取不到 `{token}` 的值，不猜");

        // 键名大小写敏感：令牌名对应 mod 代码里的属性名，宁可判失败也不做大小写猜测。
        JProperty? prop = config.Property(token);
        if (prop == null)
            return new Result(false, false,
                $"命名空间令牌 `{name}`：mod `{ns}` 的 config.json 里没有键 `{token}`，不猜");

        string expected = (expectedText ?? "").Trim();
        if (expected.Length == 0)
            return new Result(false, false, $"命名空间令牌 `{name}`：条件没有给出要比较的值，无法求值");

        if (prop.Value is not JValue)
            return new Result(false, false,
                $"命名空间令牌 `{name}`：mod `{ns}` 配置的 `{token}` 是 {prop.Value.Type}，不是可比较的标量");

        string actual = RenderConfigValue(prop.Value);
        bool equal = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        return new Result(true, equal, Note:
            $"按 `{ns}/config.json` 的 `{token}` = {actual} 求值，条件值 `{expected}` {(equal ? "相符" : "不符")}");
    }

    /// <summary>把 config.json 里的标量值渲染成 CP 令牌会产出的字符串（布尔小写，其余原样）。</summary>
    private static string RenderConfigValue(JToken value) =>
        value.Type == JTokenType.Boolean
            ? (value.Value<bool>() ? "true" : "false")
            : value.ToString();

    /// <summary>
    /// <c>HasMod</c> 的求值。两种真实/计划写法都认：
    /// <list type="bullet">
    /// <item>键槽：<c>{"HasMod |contains=Some.Mod": true}</c> —— <c>|contains=</c> 给出要查的 mod id，JSON 值给期望真假（取反开关）。</item>
    /// <item>值槽：<c>{"HasMod": "|contains=Some.Mod"}</c> —— 只有 mod id，没有取反，期望真假默认 true。</item>
    /// <item>直写：<c>{"HasMod": "Some.Mod"}</c> —— 值即 mod id。</item>
    /// </list>
    /// 解析方式与 <c>TokenTranslator</c> 的 <c>|contains=</c> 处理保持一致：<c>|contains=</c> 之后才是值，
    /// 声明值（若有）是拿 true/false 去比。
    /// </summary>
    private Result EvalHasMod(string? args, string? value)
    {
        string? argsContains = ExtractContains(args);
        string? valueContains = ExtractContains(value);

        string modId;
        string? boolSlot;
        if (argsContains != null)
        {
            // 真实写法：键槽承载 |contains=，JSON 值是取反开关
            modId = argsContains;
            boolSlot = value;
        }
        else if (valueContains != null)
        {
            // 计划测试写法：值槽承载 |contains=，没有单独的取反开关
            modId = valueContains;
            boolSlot = null;
        }
        else
        {
            modId = (value ?? "").Trim();
            boolSlot = null;
        }

        if (modId.Length == 0)
            return new Result(false, false, "HasMod 没有给出模组 UniqueID");

        bool want = true;
        if (!string.IsNullOrWhiteSpace(boolSlot) && !bool.TryParse(boolSlot, out want))
            return new Result(false, false, $"HasMod 的期望值必须是 true/false，实际是 `{boolSlot}`");

        // `|contains=A, B` 是 CP 的**多值集合**语义：`Token.ApplyContains` 用 `Overlaps`，
        // **任一命中**即可（实测 Juliet & Jessie 的 `HasMod |contains=SVE, Nikolai`；
        // 当成一个 id 查会永远为假，把 SVE 已装时的取反条件判错）。
        string[] ids = modId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Length == 0)
            return new Result(false, false, "HasMod 没有给出模组 UniqueID");

        // ⚠ 不能用 `ContainsRuntime`：`HasMod` 判假会让 FTM/Include **整块内容不展开**
        //（实测 16 条 Data/Locations、14 条 Roses In The Sand 等直接消失）。
        // 框架没烘进来造成的危害单独在「空值删除」那一层处理（见 Program
        // `StripNullDeletionsForInertDependencies`）。
        return new Result(true, ids.Any(id => _mods.Contains(id)) == want);
    }

    private Result EvalModId(string? value)
    {
        string id = (value ?? "").Trim();
        if (id.Length == 0) return new Result(false, false, "ModId 没有给出值");
        return new Result(true, string.Equals(_packId, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <c>HasFile</c> 求值。语义照抄 CP <c>HasFileValueProvider</c>：
    /// <list type="bullet">
    /// <item><b>路径取自<b>输入参数槽</b></b>（<c>EnableInputArguments(required: true)</c> +
    ///   <c>input.GetPositionalSegment()</c>）—— 即 <c>"HasFile: &lt;路径&gt;"</c> 里冒号后面的那段，
    ///   <b>不是</b> 冒号后面的"比较值"。</item>
    /// <item><c>Path.IsPathRooted</c> ⇒ 直接 <c>false</c>（不是报错）。</item>
    /// <item>相对路径不安全（含 <c>../</c>）⇒ CP <b>抛异常</b>；我们判「求不了值」，同样不展开。</item>
    /// </list>
    /// <para>
    /// <b>⚠ 曾经读错了槽</b>（读的是比较值）：<c>"HasFile: {{FromFile}}": true</c> 会变成
    /// <c>File.Exists(packRoot + "true")</c> ⇒ <b>恒为假</b>。实测代价：SVE「季节性服装」子包
    /// 21 条 Include 全被判「条件为假」跳过，整个包 0 条补丁进流水线。
    /// </para>
    /// </summary>
    private Result EvalHasFile(string? argument)
    {
        string rel = (argument ?? "").Trim();
        if (rel.Length == 0)
            return new Result(false, false, "HasFile 没有给出文件路径（CP 要求输入参数必填）");

        string norm = rel.Replace('\\', '/');
        // ⚠ 路径里**还有没展开的令牌** ⇒ **不判假，判「不知道」**。
        //   实测：SVE 季节性服装 `"FromFile": "assets/{{NoNose}}/maps/Cursors_{{Season}}.png"` +
        //   `"When": {"HasFile:{{FromFile}}": true}` —— `{{NoNose}}`（配置令牌）与
        //   `{{Season}}`（**运行时**令牌）都还在，`File.Exists` 必然假。
        //   可 CP 是**先代令牌再查**：`{{Season}}` 按当季取值，文件在不在**随季节变**。
        //   判假 = 13 条补丁被静默丢掉（语料实测）。与 `ConstantFolder` 同一条规矩：
        //   不知道就返回不知道，**绝不能当成 false**。
        if (norm.Contains("{{", StringComparison.Ordinal))
            return new Result(false, false,
                $"HasFile 的路径 `{rel}` 里还有未展开的令牌 —— 文件在不在取决于运行时值，不判");
        if (Path.IsPathRooted(norm))
            return new Result(true, false, $"HasFile 的路径 `{rel}` 是绝对路径 —— CP 一律判假");
        if (norm.Split('/').Any(seg => seg == ".."))
            return new Result(false, false, $"HasFile 的路径 `{rel}` 含目录上跳（../），CP 会拒绝，我们不展开");

        return new Result(true, File.Exists(Path.Combine(_packRoot, norm.Replace('/', Path.DirectorySeparatorChar))),
            $"按包内文件求值：`{norm}`");
    }

    /// <summary>
    /// <c>FirstValidFile</c> 求值。语义照抄 CP <c>FirstValidFileValueProvider</c>：
    /// 输入是**路径列表**（<c>input.PositionalArgs</c>），返回**第一个存在的**那一个
    /// （<c>Where(GetPathExists).Take(1)</c>）；比较值是"期望它是哪一个文件"。
    /// 路径规则与 <see cref="EvalHasFile"/> 同（绝对路径判假、目录上跳拒判）。
    /// <para>
    /// 返回的集合为空（一个都不存在）时对任何声明值都是**假**。
    /// </para>
    /// </summary>
    private Result EvalFirstValidFile(string? arguments, string? declaredValue)
    {
        IReadOnlyList<string> paths = SplitValues(arguments);
        if (paths.Count == 0)
            return new Result(false, false, "FirstValidFile 没有给出候选路径");

        foreach (string raw in paths)
        {
            string norm = raw.Replace('\\', '/');
            // 与 EvalHasFile 同一条规矩：候选路径里还有未展开的令牌 ⇒ 不判（见那里的说明）。
            if (norm.Contains("{{", StringComparison.Ordinal))
                return new Result(false, false,
                    $"FirstValidFile 的候选路径 `{raw}` 里还有未展开的令牌 —— 结果取决于运行时值，不判");
            if (Path.IsPathRooted(norm) || norm.Split('/').Any(seg => seg == ".."))
                return new Result(false, false,
                    $"FirstValidFile 的候选路径 `{raw}` 是绝对路径或含目录上跳，CP 会拒绝，我们不展开");

            if (File.Exists(Path.Combine(_packRoot, norm.Replace('/', Path.DirectorySeparatorChar))))
            {
                IReadOnlyList<string> declared = SplitValues(declaredValue);
                if (declared.Count == 0)
                    return new Result(false, false, "FirstValidFile 的条件没有给出要比较的值");
                bool match = declared.Any(d => string.Equals(d, norm, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(d, raw, StringComparison.OrdinalIgnoreCase));
                return new Result(true, match,
                    $"首个存在的候选是 `{norm}`，条件值 `{string.Join(", ", declared)}` {(match ? "相符" : "不符")}");
            }
        }

        return new Result(true, false, "FirstValidFile 的候选路径一个都不存在 ⇒ 假");
    }

    /// <summary>
    /// 取 <c>|contains=</c> 之后的值；没有 <c>|contains=</c> 时返回 null。
    /// 与 <c>TokenTranslator</c> 同一约定：<c>|contains=</c> 之前是键槽（此处不需要），之后才是值列表。
    /// </summary>
    private static string? ExtractContains(string? text)
    {
        if (text == null) return null;

        int at = text.IndexOf("|contains=", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        string tail = text[(at + "|contains=".Length)..].Trim();
        return tail.Length == 0 ? null : tail;
    }
}
