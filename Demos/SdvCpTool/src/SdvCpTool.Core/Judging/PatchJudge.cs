using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Judging;

/// <summary>判定一条 patch 能否转换成原生形式。</summary>
public sealed class PatchJudge
{
    private static readonly HashSet<string> SupportedActions =
        new(StringComparer.OrdinalIgnoreCase) { "Load", "EditData", "EditImage", "EditMap" };

    private readonly ResourceTree _tree;
    private readonly string _packRoot;
    private readonly VanillaAssetIndex? _vanilla;

    /// <summary>
    /// 本内容包的配置令牌（<c>content.json</c> 的 <c>ConfigSchema</c> + 包根 <c>config.json</c>）。
    /// 条件里的配置字段名不再判「不认识的令牌」，而判可离线求值的 <see cref="TokenKind.Bakeable"/>。
    /// 值与 <c>Baker</c> 读的是同一份文件、同一份取数规则。
    /// </summary>
    private readonly ConfigSchemaTokens _packConfig;

    /// <summary>包内 <c>DynamicTokens</c> 的名字（判定用；求值由 <c>Baker</c> 负责）。</summary>
    private readonly IReadOnlyCollection<string> _packTokens;

    /// <summary>
    /// 本包的 <c>UniqueID</c>（manifest 里那个）。只用来**分辨目标资产挂在谁名下** ——
    /// `Mods/&lt;自己UID&gt;/…` 是**自己的**内容（真损失，措辞要说清楚），
    /// `Mods/&lt;别人UID&gt;/…` 才是写给别的模组的。为空时不敢认（一律按"别人的"措辞）。
    /// </summary>
    private readonly string _packUniqueId;

    /// <summary>
    /// 本包 <c>content.json</c> 声明的 <c>Format</c>（读不到 = <c>null</c>）。只用来判
    /// 「CP 会不会跑 <c>Migration_2_0</c>」—— 那套迁移改的是**目标资产名与数据形状**，
    /// 我们没实现（见 <see cref="Cp.LegacyFormatMigration"/>）。
    /// </summary>
    private readonly Version? _packFormat;

    private readonly Func<WhenNode, bool?>? _evaluateLeaf;
    private readonly DialogueQueryOverlap? _dialogueOverlap;

    /// <summary>地图里带 Warp/Action/TouchAction 的格子；<c>null</c> = 这张图读不出来（沿用旧门禁）。</summary>
    private readonly Dictionary<string, HashSet<(int X, int Y)>?> _passageCells = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlySet<string>? _npcNames;

    /// <summary>
    /// `Data/Characters` 的 `Appearance[]` 引用集合（懒加载一次）。
    /// 条件化 `EditImage` 打到 `<NPC>_<变体>` 这类**变体资产名**上时用它判"有没有消费方"。
    /// </summary>
    private IReadOnlySet<string>? _appearanceRefs;

    /// <summary>配偶房间地图（`Data/Characters[].SpouseRoom.MapAsset`）—— 门禁会被复制进农舍。</summary>
    private IReadOnlySet<string>? _spouseRoomMaps;

    /// <summary>婚礼台词改写的母本懒加载缓存（见 <see cref="DefaultWeddingScript"/>）。</summary>
    private string? _defaultWeddingScript;
    private bool _defaultWeddingScriptLoaded;

    /// <param name="evaluateLeaf">
    /// 单个条件叶子的静态求值器（<c>null</c> = 不折叠，保持旧行为）。
    /// 折叠必须发生在<b>判定之前</b>：判定看到的就是折叠后的条件，写入器也用同一棵树。
    /// </param>
    /// <param name="dialogueOverlap">
    /// 全包的「同一 (资产, 键) 上有几条条件/无条件补丁」计数。为 <c>null</c> 时不启用
    /// <c>$query</c> 落点（保持旧判定）。
    /// </param>
    /// <param name="npcNames">
    /// 已知 NPC 名集合，用于判定 <c>Data/Festivals</c> 的键是不是 NPC 对话。
    /// </param>
    public PatchJudge(ResourceTree tree, string packRoot, VanillaAssetIndex? vanilla = null,
                      Func<WhenNode, bool?>? evaluateLeaf = null,
                      DialogueQueryOverlap? dialogueOverlap = null,
                      IReadOnlySet<string>? npcNames = null,
                      string? packUniqueId = null,
                      Version? packFormat = null)
    {
        _tree = tree;
        _packRoot = packRoot;
        _vanilla = vanilla;
        _evaluateLeaf = evaluateLeaf;
        _dialogueOverlap = dialogueOverlap;
        _npcNames = npcNames;
        _packUniqueId = packUniqueId ?? "";
        _packFormat = packFormat;
        _packConfig = ConfigSchemaTokens.Load(packRoot);
        // 包内 `DynamicTokens` 的**名字**（值不求，只要名字）：判定要把它们认成可烘焙的（通道 C），
        // 与配置字段同一条道理 —— 值由包自己给、`When` 也由包自己写，读包内文件即可离线求值。
        _packTokens = DynamicTokenResolver.Load(packRoot, null).Names;
    }

    /// <summary>补丁目标里有没有配偶房间地图（这些图的图块会被 `ApplyMapOverride` 复制进农舍）。</summary>
    private bool TargetsSpouseRoomMap(ChangePatch patch)
    {
        _spouseRoomMaps ??= SpouseRoomMaps.Referenced(_tree);
        foreach (string target in AssetNameList.Split(patch.Target))
        {
            string normalized = target.Replace('\\', '/').Trim('/');
            if (_spouseRoomMaps.Contains(normalized)) return true;
        }
        return false;
    }

    public PatchVerdict Judge(ChangePatch patch)
    {
        var result = new PatchVerdict { Patch = patch };

        if (!SupportedActions.Contains(patch.Action))
            return Fail(result, Verdict.UnsupportedAction, $"未实现的 Action：{patch.Action}");

        // ★ **旧格式包的 1.6 迁移**：`Format` 早于 2.0.0 的包，CP 会先跑 `Migration_2_0` ——
        //   把 `Data/ObjectInformation` 这样的老名字**改名**（→ `Data/Objects`）、把老形状的条目
        //   **按 1.6 的模型重写**。我们没实现迁移器 ⇒ 这类补丁照字面判/写都是错的，只能如实报出来。
        //   判定放这么前：迁移改的是**目标资产名本身**，所以它对每一条后面的判据都成立。
        //   （语料里 0 条命中，见 `LegacyFormatMigration` 的说明 —— 换成"看得见"而不是"静默写错"。）
        if (Cp.LegacyFormatMigration.NeedsMigration(patch.Target, _packFormat, out string? migrationReason))
            return Fail(result, Verdict.NeedsReview, migrationReason!);

        // 音频注入：`Data/AudioChanges` 的值里是 `{{AbsoluteFilePath: …}}`，那是绝对路径代不掉 ——
        // 但**根本不需要代**。改成把 ogg 并进 Wave Bank、把 cue 加进 Sound Bank，全程在 xnb 体系里。
        // 判定与写入走同一个 AudioChangesMerger，绝不两处各解释一遍。
        if (AudioChangesMerger.CanHandle(patch))
        {
            result.Channel = Channel.NativeKey;
            return Ok(result, Verdict.Converted,
                "音频注入：ogg 并进 Wave Bank、cue 加进 Sound Bank（不再产出 Data/AudioChanges）");
        }

        // Update 判据不在这里无条件判死：它在 CP 里的默认值就是 OnDayStart，且换日时一律
        // 重算 —— 只有在「要把条件写进原生 GSQ 落点」时才可能丢失额外重算时刻。判定因此
        // 下移到落点探测之后（见 NeedsRuntimeForUpdate），OnDayStart 视同默认不阻塞。

        // 节日配偶对话：原版 <NPC>_spouse 键本就表达「该 NPC 是玩家配偶」这一维度
        // （Event.cs 自动读取），无需 GSQ，也不该因 Data/Festivals 没有条件落点而被判
        // NoLandingPoint / NeedsReview。判定与写入走同一个 SpouseKeyRewriter.TryRewrite，
        // 绝不两处各解释一遍。
        if (SpouseKeyRewriter.TryRewrite(patch) != null)
        {
            result.Channel = Channel.NativeKey;
            return Ok(result, Verdict.Converted,
                "节日配偶对话：改用原版 <NPC>_spouse 键（游戏在该 NPC 是玩家配偶时自动读取），删去 Relationship 条件");
        }

        // 「键即条件」：补丁的条件被条目键名完全蕴含时（如 When: {Season: spring} 打在 Data/mail:spring_13 上），
        // 该键本来就只在那个状态下被游戏读取，条件冗余 → 删掉条件、无条件写入该键。
        // 判定与写入必须调用同一个 TryRewrite（Program.cs 写入阶段同样调它），绝不两处各解释一遍。
        if (KeyEncodedVariantRewriter.TryRewrite(patch) != null)
        {
            result.Channel = Channel.NativeKey;
            return Ok(result, Verdict.Converted,
                "条件被条目键名蕴含：改用该原生键，删去冗余条件");
        }

        // 婚礼脚本：在 strings/StringsFromCSFiles 上按 `Relationship:<NPC>: Engaged/Married` 覆写
        // Utility.cs.53xx 的补丁，原生等价写法是 Data/Weddings 的 EventScript[<NPC>]（键即配偶名，
        // 游戏只在与该 NPC 结婚时取这个键，因此不需要条件）。判定与写入走同一个 TryRewrite，
        // 母本也走同一个 ResolveDefaultEventScript —— 绝不两处各解释一遍。
        if (WeddingScriptRewriter.TryRewrite(patch, DefaultWeddingScript()) != null)
        {
            result.Channel = Channel.NativeKey;
            return Ok(result, Verdict.Converted,
                "婚礼台词覆写：改写为 Data/Weddings 的 EventScript[<配偶名>]（游戏按配偶名取键），删去 Relationship 条件");
        }

        WhenNode? when = WhenParser.Parse(patch.When);
        WhenNode? parsedWhen = when;   // 折叠**前**的树，只为「说清凭什么判死」留着

        // 常量折叠：先折掉能静态求值的叶子，判定看到的才是"残条件"。
        // 若放到 RunBake 去做，判定早已按原条件把补丁判死，残条件永远产不出来。
        if (when != null && _evaluateLeaf != null)
        {
            when = ConstantFolder.Fold(when, leaf => EvaluateLeafOverTargets(leaf, patch));
            result.FoldedWhen = when;
            if (when.Kind == WhenNodeKind.Literal)
            {
                // 整条折叠成常量：假 → 该补丁本就不适用（跳过，不是失败）；真 → 等价于无条件。
                if (!when.LiteralValue)
                    return Fail(result, Verdict.NotApplicable,
                        "条件里的可静态求值叶子折叠后整条恒为假，该补丁本就不适用"
                        + ExplainFold(parsedWhen!, patch));
                when = null;
            }
        }

        List<WhenNode> keys = when == null
            ? new List<WhenNode>()
            : when.Descendants().Where(n => n.Kind == WhenNodeKind.Key).ToList();
        bool hasCondition = keys.Count > 0;

        // 事件键前置条件：Data/Events/<地点> 的条目键自带前置条件段（`<ID>/<条件段>/…`），
        // 段间 AND；其中 `G <GSQ>` 是**通用 GSQ 入口**（Preconditions.cs:744-753）。
        // 只在**新事件 ID** 时改写 —— 覆盖既有事件时改键会新增一条而原事件仍在，两边都触发。
        // 判定与写入走同一个 EventKeyRewriter.TryRewrite，绝不两处各解释一遍。
        if (when != null)
        {
            GsqTranslation eventGsq = GsqTranslator.Translate(when);
            if (eventGsq.Success
                && EventKeyRewriter.TryRewrite(
                       patch, eventGsq.Gsq, EventKeyRewriter.LoadExistingEventIds(_tree, patch.Target)) != null)
            {
                result.Channel = Channel.NativeKey;
                return Ok(result, Verdict.Converted,
                    "事件键前置条件：把条件译成 `G <GSQ>` 段追加到事件键上（新事件 ID），删去 When");
            }
        }

        // 条件本身能否翻译
        foreach (WhenNode key in keys)
        {
            TokenKind kind = ConditionVocabulary.Classify(key.KeyName ?? "", _packConfig);
            if (kind == TokenKind.NoGsqEquivalent)
            {
                return Fail(result, Verdict.NonTranslatableCondition,
                    $"条件 `{WhenParser.Render(key)}` 在 GSQ 里没有等价关键字，也没有对应的名字维度（GSQ 无法返回集合或遍历实体）");
            }
        }

        // 通道 C：可烘焙。读 Mods/ 与包内文件求值成真/假 —— 真则降级为无条件，假则整个补丁不适用。
        // 本计划只判定通道，不实现求值；因此未提供 --mods 时判 NeedsReview。
        // 命名空间令牌 `模组ID/令牌名` 的值取自注册它的 mod 的 config.json —— 与 HasMod/HasFile
        // 同属「读 Mods/ 即可离线求值」，走同一条通道（分类仍是 CustomModToken，只改去向）。
        if (keys.Any(k => ConditionVocabulary.Classify(k.KeyName ?? "", _packConfig, _packTokens)
                is TokenKind.Bakeable or TokenKind.CustomModToken))
        {
            result.Channel = Channel.Bake;
            return Ok(result, Verdict.NeedsReview,
                "条件可由已装模组/包内文件离线烘焙（通道 C）；需提供 --mods 才能求值");
        }

        // 通道 B1：语言。内容写进对应语言后缀的资产即可表达。
        if (keys.Any(k => ConditionVocabulary.Classify(k.KeyName ?? "", _packConfig) == TokenKind.Locale))
        {
            result.Channel = Channel.LocaleSuffix;
            return Ok(result, Verdict.NeedsReview,
                "条件是游戏语言，可写进对应语言后缀的资产（通道 B1），需人工确认要产出哪些语言的资产");
        }

        // 条件里只要有一处我们无法确认能静态翻译，就记下原因，稍后在唯一会产出 Converted
        // 的两个落点分支里降级为 NeedsReview。
        //
        // 注意也要扫 Arguments：`Query: {{Day}} < 15`、`Query: '{{作者.Mod/令牌}}' = 'x'`
        // 这类写法把令牌嵌在**参数**里，只看 KeyName 是看不出来的（真实数据里就有）。
        string? blocker = null;
        foreach (WhenNode key in keys)
        {
            blocker ??= ConditionVocabulary.Classify(key.KeyName ?? "", _packConfig) switch
            {
                TokenKind.Unknown =>
                    $"条件含不认识的令牌 `{WhenParser.Render(key)}`，无法确认能否翻译成 GSQ",
                TokenKind.PureComputation =>
                    $"条件含纯计算令牌 `{WhenParser.Render(key)}`，参数能否离线内联需人工确认",
                TokenKind.GsqApproximate =>
                    $"条件 `{WhenParser.Render(key)}` 只能近似翻译成 GSQ，语义可能有偏差",
                _ when key.Arguments != null && key.Arguments.Contains("{{") =>
                    $"条件 `{WhenParser.Render(key)}` 的参数里嵌套了令牌表达式，无法静态翻译",
                _ => null
            };
        }

        // ★ **兜底：拿写入器用的同一个翻译器真跑一遍。**
        //   上面那张表是**按名字**判的，看不全 —— 实测漏过三种：
        //     · 令牌嵌在**值**里（`"Hearts:Sophia": "{{Range: 6, 10}}"`）—— 只查了 `Arguments`；
        //     · `Weather` 带了地点上下文（`Weather: island`，GSQ 表达不了 island/valley）；
        //     · 同一个值里还有别的不可译写法。
        //   结果就是**判定说 `Converted`、写入时翻译失败**，报告里表现为「写入失败」——
        //   正是本文件与 `ConditionWriter` 反复点名的「把判定不准伪装成写入出问题」。
        //   ⇒ 判据只留**一处**：让判定调 `GsqTranslator`，它失败就降级人工。
        //   （写入器用的也是它，两处结论必然一致。）
        if (blocker == null && when != null)
        {
            GsqTranslation probe = GsqTranslator.Translate(when);
            if (!probe.Success)
                blocker = $"条件翻译不成 GSQ：{probe.FailureReason}";
        }

        // 对话值内联 GSQ：`$query <GSQ>#<真分支>|<假分支>`（原版 Dialogue 解析器的正式机制）。
        // 判定与写入调同一个 DialogueQueryWriter.TryRewrite —— 这里只给结构（拿不到合并前的旧值），
        // 旧值由写入阶段补上。四个宿主之外一律返回 null，落回原有判定。
        // 起点就是 `blocker`：它同样是一条**真实**的「`$query` 走不下去」的理由 ——
        // 下面那个 `if` 要求 `blocker == null` 才进，于是有 blocker 时**根本不会尝试** `$query`；
        // 而 `LandingKind.DialogueKey` 那格只认 `dialogueQueryRefusal`，理由于是退回成
        // 「对话键只支持季节/地点/好感/topic 等受限语法」—— **那是键语法，不是真原因**。
        // 实测语料 23 条属于这一类（真正的拦阻是「条件**值**里嵌了令牌，翻译不成 GSQ」）。
        string? dialogueQueryRefusal = blocker;
        if (hasCondition && blocker == null && when != null && _dialogueOverlap != null)
        {
            GsqTranslation dialogueGsq = GsqTranslator.Translate(when);
            if (dialogueGsq.Success && !dialogueGsq.NeverApplies)
            {
                JObject? rewritten = DialogueQueryWriter.TryRewrite(
                    patch, dialogueGsq.Gsq, _dialogueOverlap, _npcNames, priorLookup: null,
                    baseLookup: key => BaseDialogueValue(patch.Target, key),
                    out dialogueQueryRefusal);
                if (rewritten != null)
                {
                    result.Channel = Channel.Gsq;
                    result.Landing = LandingKind.DialogueQuery;
                    return Ok(result, Verdict.Converted,
                        "对话值内联 GSQ：条件写进 `$query <GSQ>#新文案|原文案`（原版 Dialogue 解析器原生支持）");
                }
            }
            else
            {
                dialogueQueryRefusal = dialogueGsq.NeverApplies
                    ? "条件恒不成立（`NeverApplies`），没有任何分支可写"
                    : "条件翻译不成 GSQ，`$query` 无从下手";
            }
        }

        // Load 的 FromFile 必须在内容包里存在
        if (string.Equals(patch.Action, "Load", StringComparison.OrdinalIgnoreCase))
            return JudgeLoad(patch, result, hasCondition);

        string? target = patch.Target;
        if (string.IsNullOrWhiteSpace(target))
            return Fail(result, Verdict.TargetMissing, "patch 没有 Target");

        // CP 的 Target 支持逗号分隔多值，也支持令牌（CP 文档 action-editdata.md:161）
        IReadOnlyList<string> targets = Cp.AssetNameList.Split(target);
        if (targets.Count == 0)
            return Fail(result, Verdict.TargetMissing, "patch 没有 Target");

        string? withToken = targets.FirstOrDefault(t => t.Contains("{{"));
        if (withToken != null)
        {
            return Fail(result, Verdict.TargetContainsTokens,
                $"Target `{withToken}` 含令牌，静态无法解析资产名");
        }

        List<string> found = targets.Where(t => _tree.Resolve(t) != null).ToList();
        List<string> absent = targets.Where(t => _tree.Resolve(t) == null).ToList();

        if (absent.Count > 0)
        {
            // ⚠ **不要用 `found.Count == 0` 当门槛**：CP 的 `PatchLoader.SplitPatches` 把逗号分隔的
            //   `Target` 拆成**互相独立**的补丁（`PatchLoader.cs:165-200`），缺的那个由 EditData 新建，
            //   **与其它目标无关**。原先「只要有一个缺就整条判死」的代价：SVE 3 条补丁
            //   （Target 是 47 个 `Characters/Dialogue/*`，其中 6 个是 SVE 自己的 NPC）
            //   把 41 个**本可写**的目标一起丢掉了。
            bool anyVanillaAbsent = _vanilla != null && absent.Any(a => _vanilla.Contains(a));
            bool createsNew = (patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)
                            || patch.Action.Equals("Load", StringComparison.OrdinalIgnoreCase))
                           && !anyVanillaAbsent;

            if (createsNew)
            {
                // CP 的 When 语义是"整个补丁是否应用"。新建资产配条件 = 条件不成立时资产不存在；
                // 写成"无条件存在"的资产是**另一种语义**，绝不能静默丢弃条件。
                if (hasCondition)
                    return JudgeConditionalNewAsset(patch, result, blocker);

                string caveat = _vanilla == null
                    ? "（未提供原版资产清单 ContentHashes.json，无法确认它是不是原版资产）"
                    : "";
                string what = found.Count == 0
                    ? $"目标 `{absent[0]}` 不在资源树中"
                    : $"目标 `{string.Join("、", absent.Take(3))}`" +
                      (absent.Count > 3 ? $" 等 {absent.Count} 个" : "") +
                      $"不在资源树中（同一条补丁另有 {found.Count} 个目标在：" +
                      $"{string.Join("、", found.Take(2))}{(found.Count > 2 ? " 等" : "")}）";
                // ★ **挂在本包 / 别的 mod 命名空间下的自定义资产**：**无条件**也一样不该建。
                //   更要命的是它的**类型我们无从得知** —— `.config` 只能从树里拿别的资产凑，
                //   实测写出来**打不了包**（Cornucopia 往 `spacechase0.SpaceCore/ObjectExtensionData`
                //   写对象，而凑来的描述符说 `Dictionary<int, string>` ⇒ 回包直接失败 12 个资产）。
                //   ⚠ 措辞按归属分（自己的 / 别人的）—— 别把自己的说成别人的，理由见
                //   `ModNamespaceReason`；**判定不变**（两种都不写）。
                if (absent.All(a => IsModNamespace(a)))
                    return Ok(result, Verdict.NoLandingPoint, ModNamespaceReason(absent[0]));

                result.SuggestedChannel = "新资产";
                return Ok(result, Verdict.CreatesNewAsset,
                    $"{what}，也不是原版资产；{patch.Action} 会新建它，需确认新资产名可达{caveat}");
            }

            string detail = absent.Count == targets.Count
                ? $"{absent[0]}（该资产不在提供的资源树中；若它是 mod 自带的，请把 mod 的资源也解包进来）"
                : $"{absent[0]}（{targets.Count} 个目标中 {absent.Count} 个缺失）";
            return Fail(result, Verdict.TargetMissing, $"资源树中找不到资产 `{detail}`");
        }

        string resolvedName = found[0];
        string resolved = _tree.Resolve(resolvedName)!;

        // ★ **条目的值要跟资产声明的类型对得上**。CP 是按声明的类型**反序列化**的，
        //   转不了就记一条警告**跳过那个条目**（`EditDataPatch` 的
        //   `WarnForRecord("failed converting … to the expected type")`）；
        //   我们在 JSON 层面合并、看不见类型，于是会把**对象**写进声明为
        //   `Dictionary<int, string>` 的资产里。实测代价：Cornucopia 往
        //   `spacechase0.SpaceCore/ObjectExtensionData`（描述符明写 `Dictionary<int, string>`）
        //   与 `Mods/mistyspring.ItemExtensions/MixedSeeds` 里写对象 ⇒ **回包时直接失败**
        //   （打包器报「无效的JSON文件格式」）。CP 自己也会跳过 ⇒ 我们**不写**才对。
        if (string.Equals(patch.Action, "EditData", StringComparison.OrdinalIgnoreCase)
            && ValueShapeMismatch(patch, resolvedName) is string mismatch)
        {
            return Ok(result, Verdict.NeedsReview, mismatch);
        }

        // 地图
        if (string.Equals(patch.Action, "EditMap", StringComparison.OrdinalIgnoreCase))
        {
            if (hasCondition)
            {
                // ★ **条件能离线判真假的地图改动，走烘焙通道**（与数据那条路同一条）：
                //   求值为真 ⇒ 按无条件改 tbin（`WritePolicy.IsWritableAction` 本来就收 EditMap，
                //   烘焙之后由既有的地图写入路径落盘）；为假 ⇒ 判"不适用"。
                //   实测代价（2026-10-12）：**52 条条件化的 EditMap 被整条丢掉**，直接表现是
                //   **9 张原版地图的 warp 比原版少**（`Summit` 8→2、`AdventureGuild` 10→5、
                //   `JoshHouse` 15→9、`Forest-IceFestival` 9→5、`ArchaeologyHouse` 29→26 …）——
                //   玩家看的正是"地图之间的传送不见了"（`GI Extra locations` 那类加门/加通路）。
                //   运行时条件（`Season`/`HasSeenEvent`/`HasFlag`/`HasWalletItem`/`FarmType` 之类）
                //   **仍然判不了** ⇒ 维持下面的 `LocalEdit`（静态 XNB 树里没有通道，只报不写）。
                // ① 目标地图**自己就能自证**的条件：`FarmType: X` 而目标就是 `Maps/Farm_X`
                //    （那张图只会在玩家选了 X 农场时被加载）⇒ 条件必然成立，按无条件改即可。
                //    其余带 FarmType 的（改的是别的地图）**不能**这么算 —— 那些地图跟农场类型无关。
                if (keys.All(k => SelfSatisfiedByTarget(k, patch)))
                {
                    return Ok(result, Verdict.ConvertedNoCondition,
                        "条件由目标地图自证（`FarmType: X` 且目标就是 `Maps/Farm_X`）⇒ 按无条件改 tbin");
                }

                if (keys.All(k => BakeableForMap(k.KeyName ?? "")))
                {
                    result.Channel = Channel.Bake;
                    return Ok(result, Verdict.NeedsReview,
                        "条件可由已装模组/包内文件离线烘焙（通道 C）；求值为真就按无条件改地图");
                }

                // 雨天叠加层：原版 DayTiles/NightTiles 能近似“雨天显示”。
                // Day/NightTiles 在夜晚**和**雨天都走 NightTiles ⇒ 晴天夜里也会显示；
                // 这是静态 XNB 能拿到的最接近语义，报告里写明代价。
                DayNightOverlayMode overlayMode = TargetsSpouseRoomMap(patch)
                    ? DayNightOverlayMode.None
                    : WeatherMapOverlay.Classify(patch, when, keys);
                if (overlayMode != DayNightOverlayMode.None)
                {
                    bool ambient = patch.Raw["FromFile"] == null;
                    result.Channel = ambient ? Channel.DayNightAmbient : Channel.DayNightTiles;
                    result.DayNightVisibleAtDay = overlayMode == DayNightOverlayMode.Day;
                    if (ambient)
                        return Ok(result, Verdict.ConvertedNoCondition, overlayMode == DayNightOverlayMode.Day
                            ? "近似：白天 `AmbientLight` 写入地图属性；原版只有 `AmbientLight`/`AmbientNightLight` 两态"
                            : "近似：夜间 `AmbientNightLight` 写入地图属性；原版只有 `AmbientLight`/`AmbientNightLight` 两态");
                    return Ok(result, Verdict.ConvertedNoCondition, overlayMode == DayNightOverlayMode.Day
                        ? "近似：白天地图叠加层写成 `DayTiles`（原图块）+ `NightTiles`（透明）"
                          + "—— 白天显示；雨天白天会隐藏（原版这两个属性共用“夜晚/下雨”分支）"
                        : "近似：雨/夜地图叠加层写成 `DayTiles`（透明）+ `NightTiles`（原图块）"
                          + "—— 雨天/夜间显示；代价是晴天夜里（或夜间的雨）也会显示"
                          + "（原版这两个属性共用“夜晚/下雨”分支）");
                }

                // ★ **条件化地图改动的替代通道**（2026-10-16，按业主口径"能代替就代替"）：
                //   静态 XNB 的地图**没有条件化 Warp**（`GameLocation.cs` 里 `Warp` 那几处都不查 GSQ），
                //   唯一的替代是**图块门禁** `ConditionalDoor <GSQ>`（`:3907/9549`：GSQ 为假 ⇒ 玩家被弹回）
                //   ⇒ **无条件落图 + 在 `ToArea` 的图块上加门禁**：条件不成立时"看得见、进不去/用不了"。
                //   ⚠ 只对**持久型**条件（`HasFlag`/`HasSeenEvent`/`FarmType`/`Year`…）—— 轮流型
                //     （`Weather`/`Time`/`Season`/`Random`）落到同一区域是**互斥变体**，无条件全写会互相覆盖 ✗。
                //   ⚠ 必须有 `ToArea`：否则不知道它写了哪几块图，门禁无处可放 ⇒ 维持 `LocalEdit`。
                bool hasArea = patch.Raw["ToArea"] is JObject
                               || (patch.Raw["MapTiles"] as JArray)?.Count > 0;
                // 只改牌子文字：先按“无条件落一条”近似，别给它套门禁（门禁会挡路）。
                bool textOnly = patch.Raw["TextOperations"] is JArray { Count: > 0 } textOps
                    && textOps.All(IsSafeTextOperation)
                    && patch.Raw["MapTiles"] == null
                    && patch.Raw["FromFile"] == null
                    && patch.Raw["AddWarps"] == null
                    && patch.Raw["AddNpcWarps"] == null
                    && patch.Raw["MapProperties"] == null;
                if (!textOnly && hasArea && when != null
                    && ConditionPersistence.AllLeavesStable(when)
                    && GsqTranslator.Translate(when) is { Success: true, NeverApplies: false } gate
                    && !string.IsNullOrEmpty(gate.Gsq))
                {
                    // ★ **配偶房间地图不设门禁**：婚房是 `FarmHouse.loadSpouseRoom()` 用
                    //   `ApplyMapOverride` 把 `MapSourceRect` 那块原样复制进农舍的
                    //   （`FarmHouse.cs:1988-2065`）—— 门禁图块会被一起复制，婚后在自己的房间被弹回。
                    //   条件化的婚房变体**照常写入、选一个变体**即可。
                    if (TargetsSpouseRoomMap(patch))
                    {
                        result.DoorGateGsq = null;
                        result.Channel = Channel.None;
                        return Ok(result, Verdict.ConvertedNoCondition,
                            "目标含**配偶房间地图**（`Data/Characters[].SpouseRoom.MapAsset`）"
                            + "—— 婚房由 `ApplyMapOverride` 复制进农舍，门禁会被一起复制 ⇒ 照常写入、不设门禁");
                    }

                    // ★ **面积上限**：门禁是逐格写的 "条件不成立就弹回"。小改动没问题；
                    //   整片区域的变体（实测 `Custom_FerngillRepublicFrontier` 有 24336 格的整图变体）
                    //   会在条件不成立时把**原有通路一起挡住** ⇒ 整张图都不能走。
                    //   超过上限维持 `LocalEdit`（宁可少写，不可把整张图写死）。
                    List<(int X, int Y)> gateCells = CollectGateCells(patch.Raw);
                    if (gateCells.Count > ConditionPersistence.MaxDoorGateCells)
                    {
                        // 范围太大：逐格门禁会把原有通路一起挡住，不能走门禁。
                        // 也不能再「无条件照写」—— 那样会把后期状态提前烘焙（实测 SVE 的
                        // Shearwater 桥翻新：事件 2000009 未发生，翻新桥面已经出现在初始地图）。
                        //
                        // 口径（2026-10-24，业主）：能确定初始存档状态就先按初始状态落；
                        // 初始不成立/判不了的，纯 XNB 没有原版可承载的大范围运行时换图通道
                        // （原版只有少量硬编码 ApplyMapOverride，如社区中心修复），保留初始状态、不写。
                        bool? initial = InitialStateConditions.Evaluate(when, _evaluateLeaf);
                        result.DoorGateGsq = null;
                        result.Channel = Channel.None;
                        if (initial == true)
                            return Ok(result, Verdict.ConvertedNoCondition,
                                $"条件化地图改动范围过大（约 {gateCells.Count} 格 > "
                                + $"{ConditionPersistence.MaxDoorGateCells}），但条件在初始存档就成立"
                                + " ⇒ 按无条件写入");

                        return Fail(result, Verdict.NeedsReview,
                            $"条件化地图改动范围过大（约 {gateCells.Count} 格 > "
                            + $"{ConditionPersistence.MaxDoorGateCells}），且条件在初始存档"
                            + (initial == false ? "不成立" : "无法确定")
                            + " ⇒ 保留初始状态、不提前烘焙"
                            + "（纯 XNB 没有原版可承载的大范围运行时换图通道）");
                    }

                    // 改动落在开放地面/装饰上时不要写门禁：那会变成一块"看不见的锁"。
                    // 只有真正落在 Warp/Action/TouchAction 门上，才值得用"条件不满足进不去"换。
                    bool? passage = CellsHavePassage(patch.Target ?? "", gateCells);
                    if (passage == false)
                    {
                        result.DoorGateGsq = null;
                        result.Channel = Channel.None;
                        return Fail(result, Verdict.NeedsReview,
                            $"条件化地图改动落在开放地面/装饰上（{gateCells.Count} 格里没有 "
                            + "`Warp`/`Action`/`TouchAction` 门）⇒ 不写门禁、保留初始状态"
                            + "（否则会在广场/灯柱上留下看不见的锁）");
                    }

                    result.DoorGateGsq = gate.Gsq;
                    result.Channel = Channel.Gsq;
                    return Ok(result, Verdict.ConvertedNoCondition,
                        "条件化地图改动 ⇒ 无条件落图 + 在它改的图块上（同一格的 `Back` 层）放 `TouchAction=ConditionalDoor "
                        + gate.Gsq
                        + "` 门禁（条件成立时放行、不成立时进不去；依据见 `ConditionPersistence`）");
                }

                // ★ 近似（2026-10-20，低风险）：只改牌子/告示文字的 TextOperations
                //   不碰图块几何，条件不成立时最多多显示一条牌子文字。先无条件落一条，
                //   报告里写清“近似”，以后有天气/时间图块通道再改成条件文本。
                if (textOnly)
                {
                    result.Channel = Channel.None;
                    return Ok(result, Verdict.ConvertedNoCondition,
                        "近似：条件化的牌子文字，冻结为一条文本（条件不成立时也会显示；"
                        + "TODO 以后做条件文本/DayTiles 近似）");
                }

                return Fail(result, Verdict.LocalEdit,
                    "条件化的地图改动在原版没有通道（图块级只有 ConditionalDoor，且不适用于此）");
            }
            return Ok(result, Verdict.ConvertedNoCondition, "无条件地图改动，直接改 tbin");
        }

        if (!hasCondition)
        {
            // 图片层：判定与写入必须走同一个 (Target × FromFile) 展开（EditImageApplier.ExpandJobs），
            // 逐个组合确认 FromFile 能静态解析且文件存在 —— 否则判定放行、写入必失败（Task 10b 的教训）。
            // PatchMode=Mask 也在这一步报错（CP 2.9 已禁用，不静默降级）。
            if (string.Equals(patch.Action, "EditImage", StringComparison.OrdinalIgnoreCase))
            {
                PatchVerdict? sourceBlock = CheckEditImageSource(patch, result);
                if (sourceBlock != null) return sourceBlock;
            }

            return Ok(result, Verdict.ConvertedNoCondition, "无条件，直接合并");
        }

        LandingKind landing = ConditionLanding.Probe(resolved, resolvedName, out JToken? targetData);
        result.SuggestedChannel = landing.ToString();
        result.Landing = landing;

        // 落点探测是**资产级**的（「这个资产里任意位置有没有条件字段」），不等于「**本补丁这条**落得下去」。
        // 实测 Data/Objects 的 `GeodeDrops[].Condition` 与 Data/Locations 的 `Forage[].Condition`
        // 形状完全一样，资产级判据分不开 —— 于是一批补丁判了 Converted、写入时才被落点校验拦下，
        // 报告里表现为「写入失败」（SVE 32 条、RSV 3 条），把「判定不准」伪装成「写入出问题」。
        // 这里用**与写入器同一个** `ConditionWriter.RefusalFor` 提前拦住 —— 一处判定，两处共用。
        if (landing == LandingKind.EntryConditionField && targetData != null
            && ConditionWriter.RefusalFor(targetData, patch.Raw, landing, resolvedName) is string refusal)
        {
            return Fail(result, Verdict.NoLandingPoint, $"`{resolvedName}`：{refusal}");
        }

        // ★ **这张图已经有消费方**（`Data/Characters` 的 `Appearance[]` 用条件引用它）⇒
        //   图本身**无条件写出去**就等价于 CP：CP 那边条件的含义是"图存不存在"，
        //   而"用不用它"由注册侧的 `Condition` 决定。实测那 92 条
        //   `Portraits/<NPC>_<变体>`（East Scarp / Lunna 的季节立绘）就是这么被误拒的：
        //   外观发射器按 NPC 名分组（提取出的是 `Aideen_spring` ⇒ 不认得），
        //   而引用它的 `Appearance[]` 条目（`Poltergeister.SeasonalCuteSpritesES` 那批）
        //   早就在树里了。
        if (landing == LandingKind.None)
        {
            _appearanceRefs ??= AppearanceConsumers.Referenced(_tree);
            string key = resolvedName.Replace('\\', '/').Trim('/');
            if (_appearanceRefs.Contains(key))
                return Ok(result, Verdict.ConvertedNoCondition,
                    $"资产 `{resolvedName}` 是图片、已被 `Data/Characters` 的 `Appearance[]` 条件引用"
                    + "（消费方在那边按条件选图）—— **图本身无条件写出**即可，条件不必落在图上");
        }

        switch (landing)
        {
            case LandingKind.None:
                return Fail(result, Verdict.NoLandingPoint, NoLandingReason(resolvedName, resolved, targetData));

            case LandingKind.Unknown:
                // ⚠ 别**断言**「条件可翻译」—— 那是没查过就下的结论。有 blocker 就照实说，
                //   否则读报告的人会以为只剩「落点」一个问题（实测这一档里就有条件本身翻不了的）。
                return Ok(result, Verdict.NeedsReview, blocker == null
                    ? $"条件可翻译，但 {resolvedName} 的结构无法自动识别，需人工确认落点"
                    : $"结构无法自动识别（{resolvedName}），且{blocker}");

            case LandingKind.DialogueKey:
                // ⚠ 措辞必须说**实际拦住它的那一条**。以前一律写「对话键只支持季节/地点/好感/topic
                //   等受限语法」—— 那是**键语法**，而实测语料里绝大多数是被
                //   `$query` 的**结构前提**拦下的（该键上没有无条件补丁提供假分支的旧值，
                //   而本条是**新增键**）。照那句话去改会改错方向。
                return Ok(result, Verdict.NeedsReview, dialogueQueryRefusal == null
                    ? "对话键只支持季节/地点/好感/topic 等受限语法，条件需要人工改写"
                    : $"对话键落点：`$query` 内联不成立 —— {dialogueQueryRefusal}");

            case LandingKind.KeyEncodedVariant:
                // ★ 这几族的**条件容量是封闭的**（键空间写死、值里只有有限几种写法）——
                //   补丁里只要有一项落在容量外，就能**确证**无处安放，不必让它去人工那一档排队。
                //   ⚠ 口径见 `ConditionCapacity`：只见证"确证"，凡有一丝可能是"有落点但没实现"的，
                //   一律交回下面的 NeedsReview（说死就是说错）。
                if (ConditionCapacity.ProvablyUnlandable(resolvedName, patch.When, out string? capacityReason))
                    return Fail(result, Verdict.NoLandingPoint, capacityReason!);

                // 键名带维度，但条件不被键名完全蕴含（能蕴含的在上面的 TryRewrite 已转掉）——
                // 不知道该怎么落，交人工，不猜。
                // ⚠ 措辞要分清两种情形：补丁用 `TextOperations`/`Fields` 时**没有条目键**，
                //   此时说「条目键名编码了条件维度」与事实不符。实测 `Data/Festivals` 那 68 条
                //   改的是节日脚本字段 `Set-Up_additionalCharacters`，条件**根本无处安放** ——
                //   两者的人工处置方式不同，理由必须可分辨。
                return Ok(result, Verdict.NeedsReview,
                    patch.Raw["Entries"] is JObject
                        ? $"资产 `{resolvedName}` 的条目键名编码了条件维度，但补丁条件不被键名完全蕴含，需人工确认落点"
                        : $"资产 `{resolvedName}` 虽有键名编码的维度，但本条改的不是条目（TextOperations/Fields），条件无处安放，需人工确认落点");

            case LandingKind.EntryConditionField:
            case LandingKind.EntryKeyConditions:
                // 落点没问题，但条件本身未必翻译得了。只要 blocker 有值就降级人工，
                // 绝不能当成 Converted —— 那会写出"能跑但语义错误"的 mod。
                if (blocker != null)
                    return Ok(result, Verdict.NeedsReview, blocker);

                // Update 判据（下移到落点探测之后）：默认值不阻塞；非默认值见 NeedsRuntimeForUpdate。
                PatchVerdict? updateBlock = NeedsRuntimeForUpdate(patch, result);
                if (updateBlock != null) return updateBlock;

                result.Channel = Channel.Gsq;
                return landing == LandingKind.EntryConditionField
                    ? Ok(result, Verdict.Converted, "条件可写入条目的 Condition 字段")
                    : Ok(result, Verdict.Converted, "条件可写入条目 key 的前置条件段（GSQ 用 G 前缀）");

            default:
                return Ok(result, Verdict.NeedsReview, "落点未知");
        }
    }

    /// <summary>
    /// 这一条**地图**条件能不能交给烘焙通道（<c>Baker</c>）判真假。
    /// <list type="bullet">
    /// <item>配置字段 / <c>HasMod</c>/<c>HasFile</c> / 命名空间令牌 / **包内 `DynamicTokens`** ⇒ 能（通道 C 本来的范围）；</item>
    /// <item><c>Season</c> ⇒ 能，按本项目既有的**季节确定化**政策冻结到春天
    ///   （值上的 <c>{{Season}}</c> 一直这么办；地图条件不这么做，"季节才出现的路/桥/入口"会整批消失 ——
    ///   实测 9 张原版地图的 warp 比原版少）；</item>
    /// <item><c>FarmType</c> ⇒ 能，按**农场类型确定化**政策冻结到 Standard（见 <c>Baker.FrozenFarmType</c>）——
    ///   各家农场的互斥变体必须只留一份；否则后写的覆盖先写的，实测把返程传送写成
    ///   <c>Farm 142 16</c>（140 宽的农场图外）导致实机角色卡在地图外；</item>
    /// <item>其余运行时条件（<c>HasSeenEvent</c>/<c>HasFlag</c>/<c>HasWalletItem</c> …）⇒ **不能**，
    ///   静态 XNB 树里没有它们的依据，维持 <c>LocalEdit</c>（报，不猜）。</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// 这条条件**由目标地图自证**吗：只认一种 —— `FarmType: X` 而目标是 `Maps/Farm_X`。
    /// 依据：`Maps/Farm_X` 这张地图只会在玩家选了 X 农场时被加载（`Data/Locations` 的农场图按类型选），
    /// 所以对它而言「我的农场类型是 X」恒真 ⇒ 可以按无条件写。
    /// ⚠ 只对**同一张图**成立：`FarmType` 条件改别的地图（如 `Maps/Town`）时**不成立**，不能这么算。
    /// </summary>
    private static bool SelfSatisfiedByTarget(WhenNode key, ChangePatch patch)
    {
        if (!(key.KeyName ?? "").Trim().Equals("FarmType", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(key.Value)) return false;

        string? map = AssetNameList.Split(patch.Target).FirstOrDefault();
        if (map == null) return false;

        string wanted = "Farm_" + key.Value.Trim();
        return map.Trim().EndsWith(wanted, StringComparison.OrdinalIgnoreCase);
    }

    private bool BakeableForMap(string keyName)
    {
        if (keyName.Trim().Equals("Season", StringComparison.OrdinalIgnoreCase)) return true;

        // 与 `Season` 同一条确定化政策（见上面文档与 `Baker.EvalFarmType`）。
        if (keyName.Trim().Equals("FarmType", StringComparison.OrdinalIgnoreCase)) return true;

        return ConditionVocabulary.Classify(keyName, _packConfig, _packTokens)
            is TokenKind.Bakeable or TokenKind.CustomModToken;
    }

    private PatchVerdict JudgeLoad(ChangePatch patch, PatchVerdict result, bool hasCondition)
    {
        // Load 在 Judge 里走了单独分支，跳过了通用的 Target 校验，这里必须自己补上
        if (string.IsNullOrWhiteSpace(patch.Target))
            return Fail(result, Verdict.TargetMissing, "Load 缺少 Target");

        if (string.IsNullOrWhiteSpace(patch.FromFile))
            return Fail(result, Verdict.TargetMissing, "Load 缺少 FromFile");

        // CP 的 Target 是逗号分隔多值；FromFile 里的字段引用令牌按「该目标」展开，
        // 因此必须逐个目标展开后判存在性，全部命中才算过（任一目标取不到文件，CP 整条报错）。
        //
        // 展开必须走 LoadApplier 的**同一个实现**：判定与写入一旦各写一份，就会重演
        // 2026-10-05 那次 —— 判定拿原始未展开字符串查文件，101 条 FromFile 含
        // {{TargetWithoutPath}} 的 Load 被判 TargetMissing，而写入器本来能处理。
        string[] targets = (patch.Target ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Replace('\\', '/').Trim('/'))
            .Where(t => t.Length > 0)
            .ToArray();
        if (targets.Length == 0)
            return Fail(result, Verdict.TargetMissing, "Load 缺少 Target");

        foreach (string target in targets)
        {
            string resolved = LoadApplier.ResolveFirstValidFile(
                LoadApplier.ExpandFieldTokens(patch.FromFile.Trim(), target), _packRoot);
            if (resolved.Contains("{{", StringComparison.Ordinal))
                return Fail(result, Verdict.TargetMissing, $"FromFile 含无法静态解析的令牌：{patch.FromFile}");

            string full;
            try { full = Path.GetFullPath(Path.Combine(_packRoot, resolved)); }
            catch { return Fail(result, Verdict.TargetMissing, $"FromFile 路径无效：{resolved}"); }

            if (!File.Exists(full))
                return Fail(result, Verdict.TargetMissing, $"FromFile 不存在：{resolved}");
        }

        if (hasCondition)
        {
            return Ok(result, Verdict.NeedsReview,
                "Load 带条件时只能靠新增资产名区分，需要人工确认命名方案");
        }

        return Ok(result, Verdict.Converted, "无条件加载，直接写入资源树");
    }

    /// <summary>
    /// 无条件的 <c>EditImage</c>：逐个 <c>(Target, FromFile)</c> 组合确认源文件可静态解析且存在。
    /// <para>
    /// 判定与写入必须走<b>同一个</b> <see cref="EditImageApplier.ExpandJobs"/> 展开，绝不两处各解释一遍
    /// —— 否则判定放行、写入失败（Task 10b 的教训）。
    /// </para>
    /// <para>
    /// 缺 <c>FromFile</c> 的补丁 CP 本就非法，既存判定不拦它，这里也不改变其判定。
    /// </para>
    /// </summary>
    private PatchVerdict? CheckEditImageSource(ChangePatch patch, PatchVerdict result)
    {
        if (!EditImageApplier.TryParseMode(patch.Raw["PatchMode"]?.Value<string>(), out _, out string? modeError))
            return Fail(result, Verdict.UnsupportedAction, modeError!);

        if (string.IsNullOrWhiteSpace(patch.FromFile)) return null;

        foreach (EditImageJob job in EditImageApplier.ExpandJobs(patch))
        {
            string resolved = LoadApplier.ResolveFirstValidFile(
                LoadApplier.ExpandFieldTokens(job.FromFile, job.Target), _packRoot);
            if (resolved.Contains("{{", StringComparison.Ordinal))
                return Fail(result, Verdict.TargetMissing,
                    $"FromFile 含无法静态解析的令牌：{job.FromFile}");

            string full;
            try { full = Path.GetFullPath(Path.Combine(_packRoot, resolved)); }
            catch { return Fail(result, Verdict.TargetMissing, $"FromFile 路径无效：{resolved}"); }

            if (!File.Exists(full))
                return Fail(result, Verdict.TargetMissing, $"FromFile 不存在：{resolved}");
        }

        return null;
    }

    /// <summary>
    /// 新建资产 + 条件：只有当条件能挂到该新资产的条目落点上时才写，否则判
    /// <see cref="Verdict.NeedsReview"/> 且不写。
    /// <para>
    /// 依据 CP 语义：条件成立 → 资产存在，不成立 → 资产不存在。把条件挂到条目上可等价表达
    /// （条目条件不成立时该条目不被游戏加载 / 事件不触发），因此是安全的转换；
    /// 挂不上又不可烘焙时"宁可少写，不可写错" —— 绝不能静默丢弃条件写成无条件资产。
    /// </para>
    /// <para>可烘焙 / 语言类条件在更早的分支已返回，不会走到这里。</para>
    /// </summary>
    private PatchVerdict JudgeConditionalNewAsset(ChangePatch patch, PatchVerdict result, string? blocker)
    {
        string target = (patch.Target ?? "").Replace('\\', '/').Trim('/');

        // ★ **挂在某个 mod 命名空间下的自定义资产**（第一段带点，如 `sophie.ConversationTopics`；
        //   或 `Mods/<谁的 UID>/…`，**包括本包自己**）：不在树里 ⇒ 这条会新建它，而它的
        //   **类型无从得知**（`.config` 只能拿别的资产凑，写出来打不了包）。别人的那种另有
        //   一层理由（条目由那个 mod 的 C# 消费，纯 XNB 场景里没有那段代码）。措辞按归属分，
        //   判定不分。以前这里会判成「落点 Unknown（待查）」，读报告的人会去找一个并不存在的原版机制。
        //   实测语料里这一档是 **32 条**（全是 `sophie.ConversationTopics`）。
        if (IsModNamespace(target))
        {
            return Ok(result, Verdict.NoLandingPoint, ModNamespaceReason(target));
        }

        // ★ 条件容量封闭的那几族（日程/节日）：**新建**资产同理 —— 条件无处安放是**确证**的
        //   （容量论证与已存在资产一模一样：候选键写死、值里没有条件槽）。这类原本落在
        //   "落点 Unknown" 那一档，读报告的人会去找一个并不存在的机制。
        if (ConditionCapacity.ProvablyUnlandable(target, patch.When, out string? capacityReason))
        {
            return Ok(result, Verdict.NoLandingPoint,
                $"新建资产 `{target}` 带条件：{capacityReason}");
        }

        // 新建资产在资源树里没有文件可读 → 用补丁将要写入的条目结构探测落点。
        JToken? prospective = patch.Raw["Entries"] as JToken ?? patch.Raw["Fields"] as JToken;
        LandingKind landing = prospective == null
            ? LandingKind.None
            : ConditionLanding.ProbeToken(prospective, target);

        if (landing is not (LandingKind.EntryConditionField or LandingKind.EntryKeyConditions))
        {
            return Ok(result, Verdict.NeedsReview,
                $"新建资产 `{target}` 带条件，但该资产类型的条目无法承载条件（落点 {landing}）；" +
                "条件无法表达，不写该条（宁可少写，不可把条件静默丢弃写成无条件资产）");
        }

        // 与已存在资产同一道关：落点探测是资产级的，还得核对**本补丁这条**落不落得下去。
        // 新建资产没有既有数据，"资产现状"就是补丁将要写入的那份（写入器用的是同一份）。
        if (landing == LandingKind.EntryConditionField && prospective != null
            && ConditionWriter.RefusalFor(prospective, patch.Raw, landing, target) is string refusal)
        {
            return Ok(result, Verdict.NeedsReview,
                $"新建资产 `{target}` 带条件，但落点核对不通过：{refusal}；" +
                "条件无法表达，不写该条（宁可少写，不可把条件静默丢弃写成无条件资产）");
        }

        if (blocker != null)
            return Ok(result, Verdict.NeedsReview, blocker);

        // Update 判据同样是「落点探测之后」：新建资产的条件也写进原生 GSQ 落点，额外重算时刻同样丢失。
        PatchVerdict? updateBlock = NeedsRuntimeForUpdate(patch, result);
        if (updateBlock != null) return updateBlock;

        result.SuggestedChannel = landing.ToString();
        result.Landing = landing;
        result.Channel = Channel.Gsq;
        return Ok(result, Verdict.Converted,
            landing == LandingKind.EntryConditionField
                ? "新建资产的条件可写入其条目的 Condition 字段"
                : "新建资产的条件可写入其条目 key 的前置条件段（GSQ 用 G 前缀）");
    }

    /// <summary>
    /// 这个资产名是不是**挂在某个 mod 的命名空间下**（自己或别人的）：`Mods/...`，或第一段带点
    /// （`作者.模组名/…`，也可能是**本包自己**的 UniqueID）。
    /// <para>
    /// 判据与 <c>ConditionVocabulary</c> 里「命名空间令牌」同源：CP 的自定义资产一律挂在
    /// 注册它的 mod 的 UniqueID 下（`Mods/&lt;UniqueID&gt;/…` 或 `&lt;UniqueID&gt;/…`）。
    /// 原版资产名（`Data/*`、`Characters/*`、`Maps/*`…）第一段**不带点**也不叫 `Mods`。
    /// </para>
    /// <para>
    /// ⚠ **"归谁"与"有没有消费方"是两件事**（2026-10-11 那条错报的病根，见
    /// `docs/research/2026-10-09-non-cp-framework-channels.md` §7.2）：命名空间只说明这条补丁是
    /// 给谁定制的，**不**说明游戏读不读它。所以这里只用来**选措辞**（自己的 / 别人的），
    /// 判不判死靠的是下面那条"类型无从得知" —— 见 <see cref="ModNamespaceReason"/>。
    /// </para>
    /// </summary>
    private bool IsModNamespace(string assetName)
    {
        string target = (assetName ?? "").Replace('\\', '/').Trim('/');
        if (target.Length == 0) return false;
        string first = target.Contains('/') ? target[..target.IndexOf('/')] : target;
        return first.Contains('.') || first.Equals("Mods", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>这个命名空间是不是**本包自己**的（`Mods/&lt;自己UID&gt;/…` 或 `&lt;自己UID&gt;/…`）。</summary>
    private bool IsOwnNamespace(string assetName)
    {
        if (string.IsNullOrEmpty(_packUniqueId)) return false;   // 不知道自己的 id 时不敢认
        string t = (assetName ?? "").Replace('\\', '/').Trim('/');
        if (t.Length == 0) return false;
        string[] segs = t.Split('/');
        string ns = segs.Length > 1 && segs[0].Equals("Mods", StringComparison.OrdinalIgnoreCase)
            ? segs[1]
            : segs[0];
        return ns.Equals(_packUniqueId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 挂在本包命名空间下的资产**没写成**时的理由。措辞按归属分两种 ——
    /// 判死的**依据是同一句**（类型无从得知 ⇒ 打不了包），但**别把自己的说成别人的**：
    /// 说成"别的 mod 的、纯 XNB 场景里没人读它"会让读报告的人以为"这本来就不是我们的东西"，
    /// 而实际上那是本包自己的内容、是真损失（2026-10-11 复核时加的这条分支）。
    /// </summary>
    private string ModNamespaceReason(string target)
    {
        string t = (target ?? "").Replace('\\', '/').Trim('/');
        string first = t.Contains('/') ? t[..t.IndexOf('/')] : t;
        if (IsOwnNamespace(t))
            return $"`{t}` 挂在**本包自己的**命名空间 `{first}` 下，但它不在资源树里 ⇒ 这条会**新建**它。"
                 + "它的**类型无从得知**（`.config` 只能拿树里别的资产凑，写出来打不了包；"
                 + "见 Cornucopia 往 `spacechase0.SpaceCore/ObjectExtensionData` 写对象那次，回包直接失败），故不写";
        return $"`{t}` 是**别的 mod 注册的自定义资产**（命名空间 `{first}`）—— "
             + "原版不认识这个资产名，条目由注册它的 mod 的 C# 消费：纯 XNB 场景里没人读它，"
             + "而它的**类型也无从得知**（`.config` 只能拿别的资产凑，写出来打不了包），故不写";
    }

    /// <summary>
    /// 条目的值形状与该资产**声明的元素类型**对不上时给出理由（对得上/看不出来时返回 <c>null</c>）。
    /// <para>
    /// 判据来自资产的 <c>&lt;资产&gt;.config</c>（见 <see cref="Validating.ConfigLeafTypes"/>）；
    /// 只在「<c>Readers</c> 恰好 2 或 3 项、末项是标量读取器」时才表态 —— 更深的嵌套一律**不表态**，
    /// 免得拿错类型去拒绝正常的包。`TargetField`（改的是子数组/子字段）**不查**：那时元素类型
    /// 不是资产级的那个。
    /// </para>
    /// </summary>
    private string? ValueShapeMismatch(ChangePatch patch, string assetName)
    {
        if (patch.Raw["TargetField"] != null) return null;
        if (patch.Raw["Entries"] is not JObject entries || entries.Count == 0) return null;

        // ⚠ 不能问 `Resolve(asset + ".config")` —— `.config` 在资源树索引里是**被忽略**的扩展名
        //   （`ResourceTree.IgnoredExtensions`），那个键根本不存在，问出来永远是 null。
        //   要按**已解析到的资产路径**改扩展名（与 `LoadApplier`/`AssetCreator` 同一做法）。
        string config = Path.ChangeExtension(_tree.Resolve(assetName) ?? assetName, ".config");
        if (Validating.ConfigLeafTypes.ReadLeafKind(config) != Validating.ConfigLeafTypes.LeafKind.Scalar)
            return null;

        string? bad = entries.Properties()
            .FirstOrDefault(p => p.Value is JObject or JArray)?.Name;
        return bad == null
            ? null
            : $"`{assetName}` 声明的元素类型是**标量**，而条目 `{bad}` 的值是对象/数组 —— "
              + "CP 会按声明类型反序列化、转不了就跳过那个条目；我们照做（不写，而不是写出去让打包失败）";
    }

    /// <summary>
    /// 近似通道只接“纯文字”的 TextOperations：目标键必须是牌子/告示文本。
    /// 不把 <c>Warp</c>/<c>Music</c>/<c>Light</c> 这类地图属性算进来，避免把结构改动无条件落盘。
    /// </summary>
    private static bool IsSafeTextOperation(JToken? operation)
    {
        if (operation is not JObject op || op["Target"] is not JArray target || target.Count == 0)
            return false;
        string key = target[target.Count - 1]?.Value<string>() ?? "";
        return key.Equals("Message", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Sign", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Text", StringComparison.OrdinalIgnoreCase)
            || key.Equals("DisplayName", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Description", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Name", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把条件写进原生 GSQ 落点前查 <c>Update</c>：默认值不阻塞，非默认值判
    /// <see cref="Verdict.NeedsRuntime"/>，否则返回 <c>null</c>（继续按 Converted 走）。
    /// </summary>
    private static PatchVerdict? NeedsRuntimeForUpdate(ChangePatch patch, PatchVerdict result) =>
        IsDefaultUpdate(patch.Update)
            ? null
            : Fail(result, Verdict.NeedsRuntime,
                $"带 Update={patch.Update}，要求运行时在额外时刻重算；写进原生 GSQ 落点的条件只在游戏读取该资产时求值，表达不了这些时刻");

    /// <summary>
    /// CP 的 <c>Update</c> <b>默认就是 <c>OnDayStart</c></b>
    /// （<c>ContentPatcher.Framework\PatchLoader.cs:130-151</c>：<c>updateRate = UpdateRate.OnDayStart;</c>），
    /// 且换日时一律按 <c>ContextUpdateType.All</c> 重算（<c>PatchManager.cs:519-538</c>）。
    /// 所以 <c>OnDayStart</c> 与缺省等价 —— <b>不构成阻塞</b>；其余取值只是"额外再多算几个时刻"
    /// （时间变化 / 地点变化 / 存档载入），并非"只在运行时才算"。
    /// </summary>
    private static bool IsDefaultUpdate(string? update) =>
        string.IsNullOrWhiteSpace(update)
        || update.Trim().Equals("OnDayStart", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>Data/Weddings</c> 的 <c>EventScript["default"]</c>：婚礼台词改写的母本。
    /// 一旦取过就缓存 —— 它来自输入资源树（本次运行不会改它），逐条补丁重读 1664 次没有意义。
    /// </summary>
    private string? DefaultWeddingScript()
    {
        if (!_defaultWeddingScriptLoaded)
        {
            _defaultWeddingScript = WeddingScriptRewriter.ResolveDefaultEventScript(_tree);
            _defaultWeddingScriptLoaded = true;
        }
        return _defaultWeddingScript;
    }

    private static PatchVerdict Ok(PatchVerdict r, Verdict v, string reason)
    {
        r.Verdict = v;
        r.Reason = reason;
        return r;
    }

    private static PatchVerdict Fail(PatchVerdict r, Verdict v, string reason)
    {
        r.Verdict = v;
        r.Reason = reason;
        return r;
    }

    /// <summary>理由里最多列几片叶子，免得一条理由几百字符。</summary>
    private const int MaxFoldNoteLeaves = 6;

    /// <summary>
    /// 「凭什么判这条不适用」——把折叠**前**那棵树里每片叶子求值的结果列进理由。
    /// <para>
    /// <b>为什么非列不可</b>：实测语料 **804 条（12%）** 是靠折叠判死的，而依据
    /// （<c>HasMod</c> 看已装模组、配置令牌看 <c>config.json</c>、命名空间令牌看提供方的
    /// <c>config.json</c>）原先只落进一个布尔 —— 报告里看不到「丢的是哪一条、凭什么丢」。
    /// 折叠判错一条就是**静默丢内容**，事后无从核对。<b>列出来才能逐类去核</b>。
    /// </para>
    /// <para>
    /// 求不了值的叶子照列「不知道」（<b>不得</b>显示成假）—— 那是
    /// <see cref="ConstantFolder"/> 的核心约束，理由里也要看得出来。
    /// 描述与求值用**同一份**展开结果（<see cref="FieldReferenceTokens"/>）——
    /// 否则理由里会印 <c>HasFile {{FromFile}} = 假</c>，读者不知道是**哪个文件**不存在。
    /// </para>
    /// </summary>
    private string ExplainFold(WhenNode original, ChangePatch patch)
    {
        var parts = new List<string>();
        foreach (WhenNode raw in original.Descendants().Where(n => n.Kind == WhenNodeKind.Key))
        {
            if (parts.Count >= MaxFoldNoteLeaves) { parts.Add("…"); break; }
            WhenNode leaf = FieldReferenceTokens.Expand(raw, patch);
            bool? value = EvaluateLeafOverTargets(raw, patch);
            parts.Add($"`{DescribeLeaf(leaf)}` = {(value.HasValue ? (value.Value ? "真" : "假") : "不知道")}");
        }
        return parts.Count == 0 ? "" : $"（叶子求值：{string.Join("、", parts)}）";
    }

    /// <summary>
    /// 对一个叶子求值，<b>多目标补丁按目标逐个求</b>，取「至少一个成立」。
    /// <para>
    /// <b>为什么必须逐目标</b>：CP 把 <c>Target</c> 的逗号列表拆成互相独立的补丁，于是
    /// <c>{{Target}}</c>/<c>{{TargetWithoutPath}}</c> 每个目标各取一份（<c>HasFile</c> 也各查一次）。
    /// 按**整串**展开的话，<c>{{FromFile}}</c> 会被换成一个「拿最后一个目标名拼出来」的
    /// **必然是假**的路径 —— 于是整条条件折成假、补丁被当「本就不适用」**静默跳过**。
    /// 实测 SVE 季节性服装 10 条 <c>EditImage</c>：改之前报「烘焙求值失败」（响），
    /// 只修展开不修折叠就变成**不响地丢掉**（更糟）—— 两处必须一起改。
    /// </para>
    /// <para>
    /// 三值逻辑不变：有一个真 → 真；全假 → 假；有求不出来的 → 不知道（绝不把不知道当假）。
    /// </para>
    /// </summary>
    private bool? EvaluateLeafOverTargets(WhenNode raw, ChangePatch patch)
    {
        string[] targets = (patch.Target ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (targets.Length <= 1)
            return _evaluateLeaf!(FieldReferenceTokens.Expand(raw, patch));

        bool anyUnknown = false;
        foreach (string target in targets)
        {
            bool? value = _evaluateLeaf!(FieldReferenceTokens.Expand(raw, patch, target));
            if (value == true) return true;
            if (value == null) anyUnknown = true;
        }
        return anyUnknown ? null : false;
    }

    /// <summary>叶子的可读写法：<c>HasMod |contains=X</c> / <c>Season = spring</c>。</summary>
    private static string DescribeLeaf(WhenNode leaf)
    {
        var sb = new System.Text.StringBuilder(leaf.KeyName ?? "");
        if (!string.IsNullOrWhiteSpace(leaf.Arguments)) sb.Append(' ').Append(leaf.Arguments);
        if (leaf.Value != null) sb.Append(" = ").Append(leaf.Value);
        return sb.ToString();
    }

    /// <summary>
    /// <see cref="Verdict.NoLandingPoint"/>（<see cref="LandingKind.None"/>）的理由。
    /// <para>
    /// <b>为什么不能一句话包打天下</b>：<c>None</c> 有三处来源，处置方式**互不相同** ——
    /// 图片/地图要的是「这一格没法按状态选」，空 JSON 是「这资产根本没内容」，
    /// 顶层数组是「元素里没有可挂条件的字段」。旧文案对所有情形都印
    /// 「如 Data/Festivals 类资产名写死」，而实测 <c>NoLandingPoint</c> 的
    /// <b>绝大多数是图片资产的条件 EditImage</b> —— 那句话与它们<b>毫无关系</b>，
    /// 会把读报告的人引到错的方向上去。
    /// </para>
    /// </summary>
    private static string NoLandingReason(string assetName, string filePath, JToken? data)
    {
        string ext = Path.GetExtension(filePath);
        if (!ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            string kind = ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "图片"
                : ext.Equals(".tbin", StringComparison.OrdinalIgnoreCase) ? "地图（tbin）"
                : $"二进制资产（{ext}）";

            // ⚠ **不能笼统说「图片没有条件位」** —— 那就把话说反了：引擎里确实有按条件换图的机制，
            //   本工具也已经实现了两条（四季 BigCraftable 索引、图集文件名季节前缀）。
            //   问题不是「没有机制」，而是**这些机制都要能确认消费方**：
            //   · BigCraftable 索引 —— 要找到恰好一个引用该贴图、SpriteIndex=0 的 Data/BigCraftables 条目；
            //   · 图集前缀     —— 要有地图把它当 TileSheet 引用；
            //   · 立绘 Appearance[] —— 要它确实是某个 `Data/Characters` 条目的贴图。
            //   本条**没被任何一条归入**，所以整块改会丢条件，这才是拒绝的真实理由。
            //   （逐条判据见 `EmitterTable` 的对应格子 —— 一处判定，两处共用。）
            string mechanisms = kind == "图片"
                ? "引擎里按条件换图有机制（四季 BigCraftable 索引 / 图集文件名季节前缀 / "
                  + "NPC 立绘的 Appearance[]），但每一条都要能**确认消费方**"
                : "这个二进制资产的图块/像素层";
            return $"资产 `{assetName}` 是{kind} —— {mechanisms}；"
                 + "本条没被任何可用机制归入，整块改又会把条件丢掉";
        }

        // ⚠ 「不是 JObject」**不等于**「空」—— `Data/MoviesReactions` 是 82KB 的顶层数组。
        //    按「非 JObject ⇒ 空」写，会给一个有内容的资产印「是空 JSON」，那也是说错。
        if (data is JArray)
            return $"资产 `{assetName}` 是列表资产，元素里没有任何可挂条件的字段";

        if (data is JObject o && !o.Properties().Any())
            return $"资产 `{assetName}` 是空 JSON，没有任何条目可以把条件挂上去";

        return $"资产 `{assetName}` 里没有任何可挂条件的字段";
    }

    /// <summary>
    /// **基础资产**里某个键的原有值（取不到返回 <c>null</c>）。
    /// <para>
    /// 用途：判定阶段判 <c>$query</c> 时给「旧值」找一个来源 —— 写入器的 <c>PriorString</c>
    /// 读的是**合并前的目标数据**，**基础资产也算**；而判定原先只认「同键上的无条件补丁」，
    /// 比写入<b>严了一档</b>，把本来写得出去的补丁判成了人工。
    /// 实测：语料里这类补丁的 73 个条目键中 **46 个原版本来就有**
    /// （<c>Characters/Dialogue/Shane</c> 的 <c>FlowerDance_Accept</c> 等）。
    /// </para>
    /// <para>
    /// 只取**字符串**值：<c>$query</c> 的四个宿主全是「键 → 字符串」的扁平字典；
    /// 取到非字符串（结构变了）就当没有，<b>不猜</b>。读不了/解析不了同理 —— 宁可退回人工。
    /// </para>
    /// </summary>
    private string? BaseDialogueValue(string? target, string key)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        string? file = _tree.Resolve(target.Replace(Path.DirectorySeparatorChar, '/').Trim('/'));
        if (file == null || !File.Exists(file)) return null;

        try
        {
            if (JToken.Parse(File.ReadAllText(file)) is not JObject root) return null;
            return root[key]?.Type == JTokenType.String ? root[key]!.Value<string>() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>门禁要落的格子：`MapTiles` 的 Position + `ToArea` 矩形。</summary>
    private static List<(int X, int Y)> CollectGateCells(JObject raw)
    {
        var cells = new List<(int X, int Y)>();
        if (raw["MapTiles"] is JArray tiles)
        {
            foreach (JToken t in tiles)
            {
                if (t is JObject o && TryParseCell(o["Position"], out int x, out int y))
                    cells.Add((x, y));
            }
        }

        if (raw["ToArea"] is JObject
            && EditImageApplier.TryParseAreas(new ChangePatch { Raw = raw }, out _,
                out ImageArea? area, out _)
            && area != null)
        {
            for (int y = area.Value.Y; y < area.Value.Y + area.Value.Height; y++)
                for (int x = area.Value.X; x < area.Value.X + area.Value.Width; x++)
                    cells.Add((x, y));
        }
        return cells.Distinct().ToList();
    }

    private static bool TryParseCell(JToken? token, out int x, out int y)
    {
        x = y = 0;
        if (token is JObject obj)
            return int.TryParse(obj["X"]?.ToString(), out x) && int.TryParse(obj["Y"]?.ToString(), out y);
        if (token is JValue { Type: JTokenType.String } v)
        {
            string[] parts = (v.Value<string>() ?? "").Split(',');
            return parts.Length == 2
                   && int.TryParse(parts[0].Trim(), out x)
                   && int.TryParse(parts[1].Trim(), out y);
        }
        return false;
    }

    /// <summary>
    /// 这些格子里有没有**真正的门/通路**（`Warp` 地图属性、`Action`/`TouchAction`/`Warp` 图块属性）。
    /// <c>null</c> = 地图读不出来（沿用旧行为，不拦）。
    /// <para>
    /// 为什么需要：门禁是"踩上去条件不成立就弹回"。如果改动的只是开放广场的地砖/装饰，
    /// 门禁就变成**看不见的锁**（实测 SVE 在星之果实酒吧前的石板上写了一片
    /// `PLAYER_HAS_SEEN_EVENT 6184643`，玩家莫名其妙被弹回；Joja 门口的灯柱也被挂上门禁）。
    /// 只有改动确实落在门/传送格上，才值得用门禁换"条件不满足时进不去"。
    /// </para>
    /// </summary>
    private bool? CellsHavePassage(string target, List<(int X, int Y)> cells)
    {
        if (cells.Count == 0) return false;
        if (!_passageCells.TryGetValue(target, out HashSet<(int X, int Y)>? passages))
        {
            passages = LoadPassageCells(target);
            _passageCells[target] = passages;
        }
        return passages == null ? null : cells.Any(passages.Contains);
    }

    private HashSet<(int X, int Y)>? LoadPassageCells(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        string? file = _tree.Resolve(target.Replace(Path.DirectorySeparatorChar, '/').Trim('/'));
        if (file == null || !File.Exists(file)) return null;

        TBin10 map;
        try { map = (TBin10)TBin10Reader.Create(File.ReadAllBytes(file)).Read(); }
        catch { return null; }

        var cells = new HashSet<(int X, int Y)>();
        foreach (Layer layer in map.Layers ?? new List<Layer>())
        {
            if (layer.Tiles == null) continue;
            for (int i = 0; i < layer.Tiles.Count; i++)
            {
                BaseTile? tile = layer.Tiles[i];
                if (tile?.Properties == null) continue;
                if (tile.Properties.Any(p =>
                        p.Key.Equals("Action", StringComparison.OrdinalIgnoreCase)
                        || p.Key.Equals("TouchAction", StringComparison.OrdinalIgnoreCase)
                        || p.Key.Equals("Warp", StringComparison.OrdinalIgnoreCase)))
                {
                    cells.Add((i % layer.LayerSize.X, i / layer.LayerSize.X));
                }
            }
        }

        // 地图自身的 `Warp` 属性：每 5 个 token 一组，前两个是入口格。
        Propertie? warp = map.Properties?.FirstOrDefault(
            p => p.Key.Equals("Warp", StringComparison.OrdinalIgnoreCase));
        if (warp?.Value is string text)
        {
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 4 < parts.Length; i += 5)
                if (int.TryParse(parts[i], out int x) && int.TryParse(parts[i + 1], out int y))
                    cells.Add((x, y));
        }
        return cells;
    }
}
