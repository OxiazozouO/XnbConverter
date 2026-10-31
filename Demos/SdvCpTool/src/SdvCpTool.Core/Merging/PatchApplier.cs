using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Diagnostics;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>把单条补丁写进产出树。</summary>
public sealed class PatchApplier
{
    public OutputTree Output { get; }

    private readonly PackTranslations? _translations;
    private readonly RawAssetCache? _rawCache;
    private readonly DynamicTokenResolver? _tokens;
    private readonly IReadOnlySet<string>? _npcNames;

    /// <param name="translations">
    /// 内容包的 i18n 翻译。提供时，含 <c>{{i18n:...}}</c> 的资产会按语言展开成带后缀的多个资产；
    /// 为 <c>null</c> 时含这种令牌的补丁一律判失败（绝不把字面量写进产物）。
    /// </param>
    /// <param name="rawCache">
    /// 运行内原文缓存。多条补丁写同一资产时必须以未翻译的原文继续合并，否则后写的补丁会读到
    /// 已翻成英文的基础文件，语言变体丢掉先前补丁的译文。
    /// </param>
    /// <param name="tokens">
    /// 内容包的 <c>DynamicTokens</c>。提供时 i18n 键里的内嵌动态令牌会先展开；
    /// 为 <c>null</c> 时含内嵌令牌的键判失败（不猜）。
    /// </param>
    /// <param name="npcNames">
    /// 已知 NPC 名集合。写入 <c>$query</c> 落点时要用它判定 <c>Data/Festivals</c> 的键是不是 NPC 对话 ——
    /// 判定阶段用的是同一个集合，两处必须一致。
    /// </param>
    /// <param name="packRoot">
    /// 内容包根目录。只有 <c>Data/AudioChanges</c> 用得上 —— 要拿它把
    /// <c>{{AbsoluteFilePath: 相对路径}}</c> 解析成包内真实文件（见 <see cref="AudioChangesMerger"/>）。
    /// </param>
    public PatchApplier(OutputTree output, PackTranslations? translations = null, RawAssetCache? rawCache = null,
        DynamicTokenResolver? tokens = null, IReadOnlySet<string>? npcNames = null, string? packRoot = null)
    {
        Output = output;
        _translations = translations;
        _rawCache = rawCache;
        _tokens = tokens;
        _npcNames = npcNames;
        _packRoot = packRoot;
    }

    private readonly string? _packRoot;

    /// <summary>
    /// 运行内写入追踪：key = <c>资产名\n落点条目路径</c>，值是该落点本次运行累积的条件状态。
    /// 用它区分「资产自带条件」（做 AND）与「前序补丁条件」（做 OR）—— 没有它就会退化成 last-writer-wins。
    /// </summary>
    private readonly Dictionary<string, LandingState> _landingStates = new(StringComparer.Ordinal);

    private static string LandingKey(string assetName, string entryPath) => assetName + "\n" + entryPath;

    /// <summary>
    /// 把一条 EditData 应用到目标资产（不存在则新建）。
    /// <paramref name="isArrayAsset"/> 显式声明目标是否为数组型资产（如 <c>Data/TriggerActions</c>）——
    /// 补丁本身无法推断目标形态，新建时必须由调用方给出，否则会产出形状错误的对象。
    /// <paramref name="gsq"/> 与 <paramref name="conditionLanding"/> 一同给出时，合并后把条件写进落点
    /// （通道 A）；落点条目定位不到则整条失败，绝不写出"能跑但语义错误"的无条件补丁。
    /// </summary>
    public MergeOutcome ApplyEditData(string assetName, string patchJson, bool isArrayAsset = false,
        string? gsq = null, LandingKind? conditionLanding = null, bool lastForAsset = false)
    {
        JObject patch;
        try { patch = JObject.Parse(patchJson); }
        catch (Exception ex) { return MergeOutcome.Fail($"补丁 JSON 解析失败：{ex.Message}"); }
        return ApplyEditData(assetName, patch, isArrayAsset, gsq, conditionLanding, lastForAsset);
    }

    /// <summary>
    /// 同上，直接吃已经解析好的补丁对象 —— 调用方手里本来就有 <c>Patch.Raw</c>，
    /// 不必先 <c>ToString</c> 再 <c>Parse</c>（大补丁实测每条几秒）。
    /// </summary>
    public MergeOutcome ApplyEditData(string assetName, JObject patch, bool isArrayAsset = false,
        string? gsq = null, LandingKind? conditionLanding = null, bool lastForAsset = false)
    {

        // 音频注入不走普通 EditData：它要并进 XACT 的音频清单，而且**不能**产出
        // `Data/AudioChanges` 本身 —— 否则游戏启动时会拿加载不到的 FilePaths 把新 cue 覆盖成哑的。
        // 判定与写入走同一个 AudioChangesMerger。
        if (AudioChangesMerger.CanHandle(new ChangePatch { Action = "EditData", Target = assetName }))
            return AudioChangesMerger.Apply(Output, _packRoot, patch, assetName);

        string rel = AssetCreator.RelativePath(assetName, ".json");

        // 坏路径（非法字符 / 保留设备名 / 超长）在这里直接降级为该条补丁写入失败 ——
        // 不落到文件系统上抛异常，也就不会带走整次转换（实测：SVE 的 `Characters/Dialogue/**Martin**`）。
        string? invalidPath = Output.ValidatePath(rel);
        if (invalidPath != null)
            return MergeOutcome.Fail($"目标资产 `{assetName}` 的输出路径非法：{invalidPath}");

        // 合并基础：本次运行改过就用缓存里那份**原文**（未翻译、已解析），
        // 否则读盘解析一次。
        // ⚠ 原来是「`_rawCache.Get` 深拷贝 → `ToString(None)` → 再 `Parse`」——
        //    而 `EditDataMerger.Apply` 内部**又**克隆一次 —— 同一份数据被来回拷四遍，
        //    比真正的合并还贵（实测 EditData 的大头）。
        //    `Get` 现在给的是缓存里那份**只读**引用；隔离由 `Apply` 内部那次克隆保证。
        // ⚠ 这里用**高精度计时器**，不用 `Process.TotalProcessorTime`：后者的粒度是时钟节拍
        //   （这台机器 15.6 ms），而这一段的每一步常常只有几毫秒 —— 归出来的账是噪声。
        long _p0 = Diagnostics.Prof.Now();
        long _a0 = Diagnostics.Prof.AllocNow();
        void Lap(string k)
        {
            if (!Diagnostics.Prof.On) return;
            Diagnostics.Prof.AddSinceAlloc(k, _p0, _a0);
            _p0 = Diagnostics.Prof.Now();
            _a0 = Diagnostics.Prof.AllocNow();
        }

        JToken? cachedRaw = _rawCache?.Get(rel);
        Lap("EditData.取基础(含深拷贝)");

        JToken target;
        bool isNew;
        if (cachedRaw != null)
        {
            target = cachedRaw;
            isNew = false;
        }
        else
        {
            // ★ 优先拿**已解析的对象**：同一资产常被几十条 EditData 连着改，
            //   原来是 `ReadTextOrNull`（pending 要先序列化成文本）+ `JToken.Parse`，
            //   每条补丁都把整份文档重新解析一遍 —— 大资产（`Data/Objects`）几 MB，
            //   解析出来的对象图是几十 MB 的分配，全量累计把 GC 拖成最大的一笔开销。
            //   ⚠ 拿到的是暂存里**同一个对象**：合并可能要原地改它，这正是「最终内容」；
            //     失败路径靠 `rollback` 快照恢复（`canMutateInPlace` / 条件 / i18n 的分支见下）。
            JToken? parsed = Output.ReadJsonOrNull(rel);
            if (parsed == null && Output.Exists(rel))
                return MergeOutcome.Fail($"目标资产 JSON 解析失败：{rel}");
            isNew = parsed == null;

            if (isNew)
            {
                // 新建资产：由调用方声明的形状决定空目标，不做猜测
                target = isArrayAsset ? new JArray() : new JObject();

                // 新建资产必须先确认有可复制的 .config —— 否则不写任何文件，避免产出回不了包的半成品
                if (AssetCreator.FindConfigTemplate(Output, assetName, target) == null)
                    return MergeOutcome.Fail($"新建资产 `{assetName}` 找不到可复制的 .config 模板");
            }
            else
            {
                target = parsed!;
            }
        }

        // 对话值内联 GSQ：把 `Entries` 的值改写成 `$query <GSQ>#新文案|<合并前原文案>`，
        // 再走普通合并。旧值只能在这里取 —— 它就是本条补丁合并前目标数据里的值。
        // 判定阶段拿不到旧值，所以这里才是「取不到旧值就整条失败」的最终把关点：
        // 绝不产出 `$query <GSQ>#新|`（假分支为空）或 `$query <GSQ>#新|新` 这种错的。
        if (conditionLanding == LandingKind.DialogueQuery)
        {
            if (string.IsNullOrEmpty(gsq))
                return MergeOutcome.Fail("落点 DialogueQuery 需要非空 GSQ");

            var change = new ChangePatch
            {
                Action = "EditData",
                Target = assetName,
                When = patch["When"] as JObject,
                Raw = patch,
            };
            JObject? rewritten = DialogueQueryWriter.TryRewrite(
                change, gsq, overlap: null, npcNames: _npcNames,
                priorLookup: key => PriorString(target, key), out string? refusal);
            if (rewritten == null)
                // 带上**具体缘由** —— 不带的话读者只知道「取不到旧值」，
                // 而实际拦住它的可能是「GSQ 含 # / |」「同键上没有无条件补丁」等等，改错方向。
                // 缘由与判定阶段同源（同一个 TryRewrite 实现里的同一个 Refuse）。
                return MergeOutcome.Fail($"对话值内联 GSQ 失败：{refusal ?? "未知缘由"}");
            patch = rewritten;
        }

        // ★ **能原地改就不克隆整份文档**。实测「整文档克隆」是 EditData 里最贵的一步
        //   （一个包 108 次 1658 ms），而真合并合计只要 13 ms —— 大资产（`Data/Objects` 三万多个节点）
        //   被几十条补丁连着改时，克隆几乎就是全部开销。
        //   ⚠ 交所有权的前提是「**这条补丁的整条写入流水线**失败时不需要回滚」——
        //     「`EditDataMerger.Apply` 自己是原子的」还不够：合并**之后**还有两步会失败，
        //     而它们失败时不会回滚已经就地改过的 `target`（它就是原文缓存、非 i18n 时
        //     还是产出树暂存的那一份）：
        //       · `ConditionWriter.Apply` —— 条件落点写入。它**自己**是先定位后落笔，
        //         但多条落点时后一条失败会留下前一条的改动；**单条**时 `Combine`
        //         （同一落点累积到 ≥2 条条件、其中含顶层逗号的复合 GSQ）也仍会失败，
        //         而判定侧 `RefusalFor` 不查 `Combine`，拦不住。⇒ 只要要写条件就克隆。
        //       · `TranslationApplier.WriteLocalized` —— 走 i18n 那条路时，
        //         键含未支持的内嵌令牌 / 具名参数值里含代不掉的令牌 / `{{i18n:}}` 未闭合
        //         都会返回失败。⇒ 只要可能走那条路就克隆。
        //     ⚠ 这里踩过一次：只看 `paths.Count` 就放行「单落点条件补丁」，漏了 `Combine`。
        //       审查时逮到的（另有一处「合并内部不原子」也一并修了，见 `EditDataMerger`）。
        //
        //   ★ 合并**内部**的原子性是靠这几处凑出来的（详见各自注释）：
        //     `TextOperationMerger.ValidateShape` 提到 `Apply` 最前、`Descend(create: true)`
        //     改成先走通再挂新建容器、`WriteLocalized` 的结构标量归一推迟到不会再有失败之后。
        bool writesConditions =
            gsq != null && conditionLanding != null && conditionLanding != LandingKind.DialogueQuery;

        // 「这份文档会不会走进 i18n 写入路」：目标里已有令牌，或**本条补丁**会带进令牌。
        // 两者取或即覆盖全部来源 —— 合并只会把补丁里的字符串搬进文档，不会凭空造出 `{{i18n:`。
        // ⚠ `target` 可能已经是上一次原地改过的那个对象，`ContainsToken` 读的是**当前内容**，没问题。
        //   ⚠ 试过「按资产记一个单调标记、省掉这次扫描」：省下的只有 ~0.45 s（SVE），
        //     却因为**保守取值**改变了走哪条写入路（i18n 路 vs 快路）⇒ 文件落盘时机变了 ⇒
        //     自检在链条中途看到的资产数 +1 ⇒ 报告与改前不一致（实测 30 包）。不值，撤了。
        bool mayWriteI18n = TranslationApplier.ContainsToken(target)
                            || TranslationApplier.ContainsToken(patch);
        Lap("EditData.检测令牌");

        bool canMutateInPlace = !writesConditions && !mayWriteI18n;
        if (Diagnostics.Prof.On)
            Diagnostics.Prof.Add(canMutateInPlace ? "原地改[是]"
                : writesConditions ? "原地改[否-条件落点]" : "原地改[否-i18n]", 0);

        // ★ 本补丁改到的**顶层键**（从补丁结构直接取，**不能**依赖"补丁前快照"：
        //   原地改会把暂存里的对象一起改掉，快照读出来已经是补丁后的内容）。
        //   语言变体（`<asset>.zh-CN`）要按这批键把补丁镜像到本地化资产上。
        IReadOnlyCollection<string>? changedKeys =
            Output.Exists(AssetCreator.RelativePath(assetName + LocaleVariants.FrozenSuffix, ".json"))
                ? ChangedTopKeys(patch)
                : null;

        // ★ 只含 `Entries` 的补丁：合并本身不会失败（目标类型已在最前面判过），
        //   真失败只会来自后面的条件落点/翻译。先在**顶层被改的键**上拍快照，
        //   然后原地改 —— 克隆成本从 O(整份文档) 降到 O(本补丁改到的条目)。
        //   失败时按快照回滚，成功时快照直接丢掉。目标必须是 JObject（数组资产回退整文档克隆）。
        Dictionary<string, (bool Exists, JToken? Value)>? rollback = null;
        // `Entries` / `Fields` / `TargetField` 这三种改动的顶层键都能从补丁结构直接取到；
        // `TextOperations`/`MoveEntries` 的落点可能不是顶层键，暂不纳入快照（继续整文档克隆）。
        if (!canMutateInPlace && target is JObject targetObj && CanSnapshotPatch(patch))
        {
            HashSet<string>? keys = ChangedTopKeys(patch);
            if (keys != null)
            {
                rollback = new Dictionary<string, (bool, JToken?)>(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    JProperty? prop = targetObj.Property(key);
                    rollback[key] = (prop != null, prop?.Value.DeepClone());
                }
            }
        }

        MergeOutcome merged = EditDataMerger.Apply(target, patch, assetName,
            takeOwnership: canMutateInPlace || rollback != null);
        Lap("EditData.合并");
        if (!merged.Success)
        {
            RestoreRollback(target, rollback);
            return merged;
        }

        // 通道 A：把条件写进落点。定位不到条目就整条失败 —— 不能写出无条件版本。
        // 同一落点被多条补丁写时，靠运行内追踪把条件 OR 合并（而非 last-writer-wins）。
        // 只有 EntryConditionField 是"一个槽"才需要追踪；事件键落点每条补丁各成一个 key，不在此列。
        // DialogueQuery 落点已在上面的改写里把条件写进值本身，不走 ConditionWriter。
        var pendingStates = new List<(string Key, LandingState State)>();
        if (writesConditions)
        {
            // 落点是纯看补丁结构算出来的（不依赖合并后的数据）。
            IReadOnlyList<string> paths = ConditionWriter.EntryPaths(patch, conditionLanding!.Value);

            // ⚠ **算不出落点不是失败**：那说明这条补丁**根本没有任何改动**（没有
            // `Entries`/`Fields`/`TargetField`），CP 下同样是空操作。
            // 原先这里直接 `Fail`，于是判定说 `Converted`、写入却失败 —— 报告里表现为
            // **写入失败**，正是 `ConditionWriter.RefusalFor` 注释里点名要避免的
            // 「把判定不准伪装成写入出问题」。实测语料里有 2 条就是这么来的。
            // 一致性靠「判定与写入共用同一个 `RefusalFor`」保证，这里只需与它口径一致：不失败。
            //
            // ⚠ 这条路**不写盘也不更新原文缓存** —— 所以它绝不能走原地改（否则产出树暂存里
            //    那个对象已经被合并结果改过了，落盘时会把「报成空操作」的内容写出去）。
            //    上面那道闸对「要写条件」一律克隆，已经挡住了。
            if (paths.Count == 0)
            {
                RestoreRollback(target, rollback);
                return MergeOutcome.Ok(merged.Data!);
            }

            bool track = conditionLanding!.Value == LandingKind.EntryConditionField;

            foreach (string path in paths)
            {
                string key = LandingKey(assetName, path);
                // 在工作副本上累积：本条补丁中途失败时，不能留下"这条已经写过"的假追踪状态
                LandingState? state = track
                    ? (_landingStates.TryGetValue(key, out LandingState? prior) ? prior.Clone() : new LandingState())
                    : null;

                MergeOutcome conditionResult = ConditionWriter.Apply(
                    merged.Data!, path, gsq!, conditionLanding!.Value, state, assetName, patch);
                if (!conditionResult.Success)
                {
                    RestoreRollback(target, rollback);
                    return conditionResult;
                }

                if (state != null) pendingStates.Add((key, state));
            }
        }

        // 写入时按语言展开：含 {{i18n:...}} 的资产落成基础资产 + 各语言后缀变体。
        // 翻译失败（键缺失 / 动态键 / 带参数）则整条补丁失败，不落任何文件。
        // 该资产本次运行最后一次写：原文缓存不会再被读，翻译克隆可以省掉。
        // 失败时无需恢复原文（没有后续补丁会读它），也不必回滚已合并的目标——
        // 翻译只在私有工作副本/最终目标上做，失败不会 stage 任何文件。
        bool canTakeOwnership = lastForAsset;
        MergeOutcome written = TranslationApplier.WriteLocalized(
            Output, assetName, merged.Data!, _translations, isNew, _tokens, changedKeys,
            takeOwnership: canTakeOwnership);
        Lap("EditData.写(含翻译/序列化)");
        if (!written.Success)
        {
            RestoreRollback(target, rollback);
            return written;
        }

        // 文件写成功后才提交追踪状态与原文缓存
        _rawCache?.Set(rel, merged.Data!);
        Lap("EditData.存回缓存(深拷贝)");
        foreach ((string key, LandingState state) in pendingStates) _landingStates[key] = state;

        return merged;
    }

    /// <summary>只改 <c>Entries</c> 的补丁：目标 JObject 时合并本身不会失败，可走快照原地改。</summary>
    private static bool IsEntriesOnly(JObject patch) =>
        patch["Entries"] is JObject
        && patch["TargetField"] == null
        && patch["Fields"] == null
        && patch["TextOperations"] == null
        && patch["MoveEntries"] == null;

    /// <summary>
    /// 可以按顶层键快照/回滚的补丁：只含 <c>Entries</c>/<c>Fields</c>/<c>TargetField</c>。
    /// 这三类的改动落点都能从补丁结构确定到顶层键；<c>TextOperations</c>/<c>MoveEntries</c>
    /// 的路径不是，保留整文档克隆。
    /// </summary>
    private static bool CanSnapshotPatch(JObject patch) =>
        (patch["Entries"] is JObject || patch["Fields"] is JObject || patch["TargetField"] is JArray)
        && patch["TextOperations"] == null
        && patch["MoveEntries"] == null;

    /// <summary>把快照里的顶层键写回目标（失败回滚；成功路径不调用）。</summary>
    private static void RestoreRollback(JToken target,
        Dictionary<string, (bool Exists, JToken? Value)>? rollback)
    {
        if (rollback == null || target is not JObject obj) return;
        foreach ((string key, (bool exists, JToken? value)) in rollback)
        {
            if (exists) obj[key] = value;
            else obj.Remove(key);
        }
    }

    /// <summary>
    /// 把同一个资产的多条 **纯 Entries、无 i18n、无条件落点** 补丁合批：
    /// 只克隆一次目标文档，按顺序原地合并，最后统一写一次。
    /// 返回 <c>null</c> 表示这批不适合快路径（调用方回退到逐条写入）。
    /// <para>
    /// 边界刻意收得很窄：目标/补丁里出现 <c>{{i18n:</c>、补丁不是纯 Entries、
    /// 合并产生保留意见、资产是新资产 —— 一律回退。这样输出语义与逐条路径完全一致。
    /// </para>
    /// </summary>
    public List<MergeOutcome>? TryApplyEditDataBatch(string assetName, IReadOnlyList<JObject> patches,
        bool lastForAsset = false)
    {
        if (patches.Count < 2) return null;
        if (ArrayAssets.IsArrayAsset(assetName)) return null;
        if (AudioChangesMerger.CanHandle(new ChangePatch { Action = "EditData", Target = assetName }))
            return null;

        string rel = AssetCreator.RelativePath(assetName, ".json");
        if (Output.ValidatePath(rel) != null) return null;

        string? onDisk = Output.ReadTextOrNull(rel);
        if (onDisk == null) return null;   // 新资产：配置模板/类型推断另走原路径，不在这里猜

        JToken? cached = _rawCache?.Get(rel);
        JToken target;
        try
        {
            target = cached != null ? cached.DeepClone() : JToken.Parse(onDisk);
        }
        catch { return null; }

        if (target is not JObject obj) return null;

        // 带 i18n 的资产不合批：合批会把多个补丁的翻译/本地化覆盖合并成一次，
        // 实测会让已有 `.fr-FR`/`.ko-KR` 变体丢掉只存在于本地化文件里的键
        // （`Jas`/`Vincent` 的 East Scarp 对话、`EngagementDialogue` 等）。
        // 这些资产继续逐条写，保证本地化变体语义不变。
        if (TranslationApplier.ContainsToken(obj)) return null;
        foreach (JObject patch in patches)
            if (TranslationApplier.ContainsToken(patch)) return null;

        // `Data/Events/*` 的**键序**是语义：游戏取首个命中的事件并 break。
        // 已有本地化变体时，合批会让变体的覆盖顺序与逐条写入不同
        //（实测 `Data/Events/Town.zh-CN` 只是键序变化，但事件选择可能跟着变）⇒ 回退逐条。
        // 没有令牌、也没有任何本地化变体的纯文本事件，基础键序由原地合并保证，可以合批。
        if (assetName.Replace('\\', '/').StartsWith("Data/Events/", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string suffix in LocaleVariants.Suffixes)
                if (Output.Exists(AssetCreator.RelativePath(assetName + suffix, ".json")))
                    return null;
        }

        var outcomes = new List<MergeOutcome>(patches.Count);
        foreach (JObject patch in patches)
        {
            if (!IsEntriesOnly(patch)) return null;

            // 纯 Entries + JObject：合并本身不会失败；仍保留检查，失败就整批回退。
            MergeOutcome merged = EditDataMerger.Apply(obj, patch, assetName, takeOwnership: true);
            if (!merged.Success || merged.Notes.Count > 0) return null;
            outcomes.Add(merged);
        }

        MergeOutcome written = TranslationApplier.WriteLocalized(
            Output, assetName, obj, _translations, isNew: false, _tokens,
            takeOwnership: lastForAsset);
        if (!written.Success || written.Notes.Count > 0) return null;

        _rawCache?.Set(rel, obj);
        return outcomes;
    }

    /// <summary>
    /// 这条 <c>EditData</c> 补丁改到的**顶层键**（按补丁结构取）。
    /// <para>
    /// 为什么不能靠"补丁前/后文档对比"：原地改（<c>takeOwnership</c>）会让产出树暂存里的
    /// 对象**一起被改**，事后读到的"补丁前基线"已经是补丁后的内容 —— 实测
    /// Cape Stardew 的 `14635122/SawEvent` 因此没被镜像进 `Data/Events/Farm.zh-CN`，
    /// 中文存档下模组事件整个不出现。
    /// </para>
    /// <para>认不出的动态键（含 <c>{{</c>）原样带上；对不上就是那一条不镜像，不猜。</para>
    /// </summary>
    private static HashSet<string>? ChangedTopKeys(JObject raw)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        void AddObjectKeys(JToken? token)
        {
            if (token is JObject obj)
                foreach (JProperty prop in obj.Properties()) keys.Add(prop.Name);
        }

        AddObjectKeys(raw["Entries"]);
        AddObjectKeys(raw["Fields"]);
        static void AddString(JToken? token, HashSet<string> keys)
        {
            // ⚠ 不要对任意 JToken 调 `Value<string>()`：`From`/`To`/`TargetField` 可能是**数组**，
            //   那会抛 `Cannot cast JArray to JToken`（实测 80 个包因此整条写入异常）。
            if (token is JValue { Type: JTokenType.String } value
                && value.Value<string>() is { Length: > 0 } text)
                keys.Add(text);
        }

        AddString(raw["TargetField"], keys);
        // FTM 翻出来的补丁用 `TargetField: [地点, "Forage"]`：顶层键是**数组第一项**。
        if (raw["TargetField"] is JArray targetField && targetField.Count > 0)
            AddString(targetField[0], keys);

        if (raw["MoveEntries"] is JArray moves)
        {
            foreach (JToken move in moves)
            {
                switch (move)
                {
                    case JObject obj:
                        foreach (string side in new[] { "From", "To" })
                            AddString(obj[side], keys);
                        break;
                    default:
                        AddString(move, keys);
                        break;
                }
            }
        }

        if (raw["TextOperations"] is JArray ops)
        {
            foreach (JToken op in ops)
            {
                if (op is JObject obj
                    && obj["Target"] is JArray path
                    && path.Count >= 2
                    && path[1]?.Type == JTokenType.String
                    && path[1]!.Value<string>() is { Length: > 0 } key)
                    keys.Add(key);
            }
        }

        return keys.Count == 0 ? null : keys;
    }

    /// <summary>目标数据里某键的字符串旧值；不存在或不是字符串时返回 <c>null</c>。</summary>
    private static string? PriorString(JToken target, string key) =>
        target is JObject obj && obj[key]?.Type == JTokenType.String
            ? obj[key]!.Value<string>()
            : null;
}
