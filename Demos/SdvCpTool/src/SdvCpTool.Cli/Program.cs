using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdvCpTool.Cli;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Normalizing;
using SdvCpTool.Core.Reporting;
using SdvCpTool.Core.Validating;
using SdvCpTool.Core.Workspace;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(CommandLine.Usage);
    return 0;
}

// 框架增量烘焙：独立命令，必须在 CP/FTM 转换**之后**跑（它要读最终产出树里的基础条目）。
if (args.Length > 0 && args[0].Equals("bake-frameworks", StringComparison.OrdinalIgnoreCase))
    return FrameworkCommand.Run(args);

// 同进程批量转换：两趟全局语义不变，只去掉每包一次进程启动。
if (args.Length > 0 && args[0].Equals("convert-batch", StringComparison.OrdinalIgnoreCase))
    return BatchConvertCommand.Run(args);

// 链式转换在最后统一烘焙翻转格：等所有包的图片 Load 都尘埃落定，避免追加格被后续
// 的短图集覆盖、或索引分配互相踩踏。
if (args.Length > 0 && args[0].Equals("bake-flips", StringComparison.OrdinalIgnoreCase))
    return FlipBakeCommand.Run(args);

// 截图反查：截图 → 地图/坐标 → 图块/动画 → 覆盖该格的 CP 补丁。
if (args.Length > 0 && args[0].Equals("trace-screenshot", StringComparison.OrdinalIgnoreCase))
    return TraceScreenshotCommand.Run(args);

/// <summary>
/// 已装模组索引：**一进程一份**。
/// <para>
/// 递归扫 Mods 树实测约 0.6 s/次，而这一条链上原来要建 **2~4 次**
/// （主流程、<c>BuildTokenResolver</c>、Include 求值、烘焙各建一次）——
/// 同一份输入没必要扫第二遍（`InstalledModIndex` 只读、无副作用）。
/// </para>
/// <para>
/// ⚠ 键是**目录**：同目录复用、换目录重建，所以就算将来一进程跑多个包也不会串。
/// </para>
/// </summary>
Options? options = CommandLine.Parse(args);
if (options == null)
{
    Console.Error.WriteLine(CommandLine.Usage);
    return 2;
}

try
{
    // Include 上的 When 不能默认展开/默认跳过：先只收集，拿到 --mods 后在本流程里逐个求值。
    // FTM 的 `CPConditions.HasMod` 也在 Load 阶段判：这里给一个懒建的已装模组索引。
    InstalledModIndex? judgeMods = null;
    Func<string, bool?>? ftmHasMod = options.Mods == null
        ? null
        : id => (judgeMods ??= ModIndexProvider.Get(options.Mods, options.ModIndexCache)).Contains(id);
    ContentPack pack = ContentPack.Load(options.Pack, deferConditionalIncludes: true, hasMod: ftmHasMod);
    int unresolvedIncludes = ResolveIncludes(options.Mods, options.ModIndexCache, pack);

    var tree = new ResourceTree(options.Tree);

    var conversionNotes = new List<string>();

    // ★ CP 的 `CustomLocations`（`content.json` 根上的独立字段）展开成 `Load Maps/<Name>` +
    //   `Data/Locations` 的 `CreateOnLoad` 登记（见 `ContentPack.ExpandCustomLocations` 的说明）。
    //   必须**在判定之前**：否则那些地图不存在，改它们的补丁一律判 `TargetMissing`
    //   （实测 SVE 声明 71 个自定义地点，其中 14 个被 53 条补丁改到）。
    //   名字已在 `Data/Locations` 里时**跳过** —— CP 只警告不覆盖，我们照做（覆盖会把原版那条整个换掉）。

    int customLocations = pack.ExpandCustomLocations(name =>
    {
        if (tree.Resolve("Data/Locations") is not string locationsPath) return false;
        try { return JObject.Parse(File.ReadAllText(locationsPath)).Property(name) != null; }
        catch { return false; }   // 读不了就当"没有" —— 与 CP 的宽松口径一致
    });
    if (customLocations > 0)
    {
        // ⚠ 这行**也要进报告**：它说的是"哪些内容被展开成了别的形态"，只看报告的人得看得到
        //   （与 FTM 的翻译说明同一个道理 —— 原先只打 stdout，回头只看报告就会漏）。
        string line = $"CustomLocations 展开：{customLocations} 个自定义地点（地图 + 地点登记）";
        Console.WriteLine(line);
        conversionNotes.Add(line);
    }

    // ★ `Target` 里带 `{{Season}}` 的补丁**展开成四季各一份**（CP 的 `Season` 是**单值**令牌，
    //   每个季节解析成不同资产 —— 见 `SeasonTargetExpansion` 的说明）。
    //   必须在**判定之前**：否则它们一律判 `TargetContainsTokens` 整条不写。
    int seasonExpanded = SeasonTargetExpansion.Apply(pack.Patches);
    if (seasonExpanded > 0)
        Console.WriteLine($"Target 里的 `{{{{Season}}}}` 展开成四季：新增 {seasonExpanded} 条");

    // ★ `FromFile` 里带 `{{Season}}` 的补丁同理展开（并**补一条 `Season` 条件**）——
    //   这样换出来的条件正好被既有的两套发射器接住（图集季节前缀 / NPC 的 Appearance[]）。
    int seasonFromFile = SeasonFromFileExpansion.Apply(pack.Patches);
    if (seasonFromFile > 0)
        Console.WriteLine($"FromFile 里的 `{{{{Season}}}}` 展开成四季：新增 {seasonFromFile} 条");

    VanillaAssetIndex? vanilla = null;
    if (options.ContentHashes != null)
    {
        vanilla = new VanillaAssetIndex(options.ContentHashes);
    }
    else
    {
        Console.Error.WriteLine(
            "警告：未提供 --content-hashes，无法区分 mod 新增资产与原版资产，判定会放宽。");
    }

    // 判定前的常量折叠：提供 --mods 时给一个叶子求值器（与 RunBake 同一个 EvalKey 实现）。
    // ⚠ `DynamicTokens` 解析器要在**判定之前**就位：值里的令牌展开要用它（见下面 ValueTokenSubstitutor），
    //   而且**判定用的烘焙器也要用它** —— 条件里写包内令牌（`{DLXCabin: true}` 这种）时，
    //   `Classify` 认它是可烘焙的，求值就得靠这个解析器。两边**互相持有**（解析器求 When 时又回来问烘焙器），
    //   所以先建解析器、再把它接给烘焙器。
    //   后面 i18n 那段复用同一个实例，不重复读盘。
    DynamicTokenResolver tokenResolver = BuildTokenResolver(options.Mods, options.ModIndexCache, pack);

    // ★ `Target` 里**代不掉但取值可枚举**的令牌（实测：按**农场类型**分块的 `{{Farm}}`，
    //   `Lnh's Farms Convenient Road` 用它把便捷通道盖到玩家实际在玩的那张农场图上）
    //   ⇒ 按候选资产名**各写一份**。必须在判定之前：否则一律判 `TargetContainsTokens`、整条不写
    //   ⇒ 实机就是"农场图上那条传送根本没有"。判据与 `SeasonTargetExpansion` 同构（见那个类）。
    var candidateNotes = new List<string>();
    int candidateExpanded = CandidateTargetExpansion.Apply(pack.Patches, tokenResolver, candidateNotes);
    if (candidateExpanded > 0)
    {
        Console.WriteLine($"Target 里按候选值展开：净增 {candidateExpanded} 条");
        foreach (string note in candidateNotes)
        {
            Console.WriteLine($"    · {note}");
            conversionNotes.Add($"按候选值展开 Target：{note}");
        }
    }

    // 未提供 --mods 时没有求值依据，不折叠、保持旧行为。
    Func<WhenNode, bool?>? evaluateLeaf = null;
    if (options.Mods != null)
    {
        judgeMods ??= ModIndexProvider.Get(options.Mods, options.ModIndexCache);
        var judgeBaker = new Baker(judgeMods, pack.RootPath, pack.UniqueId)
        {
            DynamicTokens = tokenResolver,
        };
        evaluateLeaf = judgeBaker.TryEvaluateLeaf;
    }

    // 纯 XNB 没有 SMAPI：如果这个包依赖一个**不会运行**的代码框架（CustomBush 等），
    // 它把原版条目设成 `null` 的「删除」很可能是在给框架的替代内容腾位置 ——
    // 替代内容烘不进来，删了就是净损失（实测葡萄/啤酒花/咖啡/蓝莓/仙人掌从 Data/Crops 消失）。
    // ⇒ 这类包的空值删除一律保留原版条目，并在报告里说明。
    if (judgeMods != null)
    {
        int stripped = StripNullDeletionsForInertDependencies(pack, judgeMods);
        if (stripped > 0)
        {
            string line = $"依赖未烘焙框架 ⇒ 保留原版条目：跳过 {stripped} 个空值删除";
            Console.WriteLine(line);
            conversionNotes.Add(line);
        }
    }

    // **按季节分块的动态令牌**：拆成互斥的几条补丁（一条 → 四季各一条）。
    // 必须在判定**之前**：整条补丁原来会因为 `{{Aerobics}}` 代不掉而被拒，
    // 拆开之后走的是现成的四季/立绘发射器 —— 与同包里其它 NPC 走同一条路。
    // 形状证明不了互斥就整条不拆（见 `TokenEnumerationExpander` 的五条前提）。
    TokenEnumerationExpander.Result enumerated = TokenEnumerationExpander.Apply(pack.Patches, tokenResolver);
    if (enumerated.Expanded > 0)
        Console.WriteLine($"按季节分块的动态令牌**枚举展开**：{enumerated.Expanded} 条补丁 → 净增 "
                          + $"{enumerated.Added} 条（令牌代不掉的，拆成各季一条）");

    // ★ 两阶段链：`--pass loads` 只装 `Load`、`--pass edits` 跳过 `Load`。
    //   依据 CP 的 `PatchManager.ApplyPatchesToAsset`：请求资产时**先选出 Load 装上去，
    //   再按优先级依次应用全部 Edit** —— 所以「后加载的包创建的资产」照样能被先加载的包编辑。
    //   逐包单阶段应用做不到这一点：`EditMap` 撞上尚未创建的资产就判 TargetMissing。
    //   实测 `Lnh's Farms Convenient Road` 的 `Maps/Custom_ConvenientRoad`（编辑来自更早的包）、
    //   SVE 对 `Maps/Custom_GrampletonFields` 的编辑（地图由后加载的 Grampleton Fields 创建）。
    if (options.Pass != "all")
    {
        bool loadsOnly = options.Pass == "loads";
        int before = pack.Patches.Count;
        pack.Patches.RemoveAll(p => p.IsLoad != loadsOnly);
        Console.WriteLine($"两阶段链 `--pass {options.Pass}`：补丁 {before} → {pack.Patches.Count}");
    }

    // `Target`/`FromFile` 里的配置令牌先代成值（CP `TokenString.cs:290` 的 `string.Join(", ", values)`）——
    // **必须在判定之前**：否则下游一律报「含无法静态解析的令牌」而拒绝。
    // 取值走 config.json 优先、否则 ConfigSchema.Default —— 与 CP 在**没有 config.json** 时一致：
    // 把配置**冻结在默认值**上。这是确定化（配置选择被写死），报告里如实报出来。
    // **包内 `DynamicTokens` 也在这里代**：CP 在 `Target`/`FromFile` 里同样会展开它们，
    // 不代就是整条落空（实测 `[CP] DaisyNiko's Tilesheets` 的 `assets/{{recolour}}/…`：
    // 图集没进树 ⇒ 29 张地图建不出来）。判不了（有损）的不代，照旧整条报出来。
    // 逐条记下**确定化**说明 —— 写盘时并进那条补丁的 writeNote。
    var determinized = new Dictionary<ChangePatch, List<string>>();
    void NoteDeterminized(ChangePatch patch, string note)
    {
        if (!determinized.TryGetValue(patch, out List<string>? list))
            determinized[patch] = list = new List<string>();
        list.Add(note);
    }

    int configSubstituted = ConfigTokenSubstitutor.Apply(
        pack.Patches, ConfigSchemaTokens.Load(pack.RootPath), tokenResolver, NoteDeterminized);
    if (configSubstituted > 0)
        Console.WriteLine($"配置/DynamicTokens 代入 Target/FromFile：{configSubstituted} 个字段" +
                          "（配置**冻结在默认值**上）");

    // **值**里能精确解析的令牌也代掉：包内 DynamicTokens / 配置字段 / `{{ModId}}`。
    // 不代的话它们会**原样落进产物**，而游戏不认识 `{{…}}` —— `Condition` 里的是非法 GSQ
    // （条目静默失效）、`Sprite`/`Texture` 里取不到图、**事件键**里的事件永不触发。
    // 实测语料 12/32 包中招、**575 处影响行为**（见 `Validating/UnexpandedTokenAudit`）。
    // 解析不了的（`{{Random}}`/`{{Spouse}}`…）**原样保留**，继续由那道自检报出来。
    int valueSubstituted = ValueTokenSubstitutor.Apply(pack.Patches, tokenResolver, pack.UniqueId,
        NoteDeterminized);
    if (valueSubstituted > 0)
        Console.WriteLine($"值里的令牌展开：{valueSubstituted} 处（只代**能精确解析**的）");
    if (determinized.Count > 0)
        Console.WriteLine($"值里的 `{{{{Random}}}}` **确定化**（取第一个备选）：{determinized.Count} 条补丁" +
                          "—— CP 每次上下文更新会重掷，取值随存档与时点变；我们固定其中一个");

    // ★ `Data/Locations` 的键大小写不一致：CP 的地名查找是 `EqualsIgnoreCase`，
    //   `TargetField: ["RailRoad", "Forage"]` 会落到一个**不存在的键**上（游戏按字典精确取）。
    //   实测 FTM 的 Lumisteria Serene Meadow 写 `RailRoad`，结果 6 条觅食/埋藏物不可达。
    //   放在令牌展开**之后**：`TargetField` 里的 `{{…}}` 先代成真名，再按已有地名归一。
    int normalizedLocationKeys = NormalizeLocationTargetFields(pack.Patches, tree);
    if (normalizedLocationKeys > 0)
        conversionNotes.Add($"`Data/Locations` 的 `TargetField` 按已有地名大小写归一：{normalizedLocationKeys} 处");

    // ★ `{{InternalAssetKey: 包内图片}}` → `SMAPI/<id>/<路径>`，并把素材记下来在写盘阶段物化。
    //   不代的话整条数据补丁被拒（实测 Hurst 的 38 个物品、Rodney 的书/稻草人、Metalcore 的 NPC）。
    var internalAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    int internalKeyCount = InternalAssetKeyExpander.Apply(
        pack.Patches, pack.RootPath, pack.UniqueId, internalAssets, conversionNotes);
    if (internalKeyCount > 0)
        Console.WriteLine($"`{{{{InternalAssetKey}}}}` 展开：{internalKeyCount} 处 → "
                          + $"{internalAssets.Count} 个随包素材");

    // ★ **带条件创建的新条目**：多数数据资产（NPC/物品/商店/能力…）的条目没有条件字段可落；
    //   原生 CP 会按运行时状态决定"这条存不存在"。纯 XNB 只能二选一 —— 这里对**目标里还没有的条目**
    //   按无条件写，静态为假的条件仍跳过；已有条目的条件修改不动（`Data/Locations` 除外）。
    // 同包 `Load Assets/Blank.json` 打底的目标：单趟时树里还没有它，但语义上就是**空对象**
    // （`Characters/schedules/` 与 `Characters/Dialogue/` 的“只补空资产”判据要用）。
    string NormTarget(string? t) => (t ?? "").Trim().Replace('\\', '/').Trim('/');
    var emptyLoadTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (ChangePatch p in pack.Patches)
    {
        if (!p.IsLoad || p.Target == null || p.FromFile == null) continue;
        string[] targets = p.Target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (targets.Length != 1) continue;
        string full;
        try { full = Path.GetFullPath(Path.Combine(pack.RootPath,
            LoadApplier.ResolveFirstValidFile(p.FromFile, pack.RootPath))); }
        catch { continue; }
        if (!File.Exists(full) || !full.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
        try
        {
            JToken token = JToken.Parse(File.ReadAllText(full));
            if ((token is JObject o && !o.HasValues) || (token is JArray a && a.Count == 0))
                emptyLoadTargets.Add(NormTarget(targets[0]));
        }
        catch { /* 不是 JSON 就算了 */ }
    }

    var existingByTarget = new Dictionary<string, JObject?>(StringComparer.OrdinalIgnoreCase);
    JObject? ExistingFor(string target)
    {
        if (existingByTarget.TryGetValue(target, out JObject? cached)) return cached;
        JObject? parsed = null;
        if (tree.Resolve(target) is string path)
        {
            try { parsed = JObject.Parse(File.ReadAllText(path)); }
            catch { parsed = null; }
        }
        if (parsed == null && emptyLoadTargets.Contains(NormTarget(target))) parsed = new JObject();
        existingByTarget[target] = parsed;
        return parsed;
    }

    // ⚠ 没有 `--mods` 时 `HasMod`/`Season` 等条件一律"判不了"，会把**本不适用**的条目也写出去；
    //   这个确定化只在有模组索引（能判断静态为假）时启用。
    int conditionalEntries = options.Mods == null
        ? 0
        : ConditionalNewEntryCreation.Apply(
            pack.Patches, tokenResolver, ExistingFor, conversionNotes, NoteDeterminized);
    if (conditionalEntries > 0)
        Console.WriteLine($"带条件的新条目无条件化：{conditionalEntries} 个"
                          + "（运行时可能提前出现，报告里有逐条说明）");

    // 条件 Load（只限文本/贴图/对话，且目标尚不存在）：同样按无条件加载，见类注释。
    // 目标存在但**内容是空 JSON**（`{}` / `[]`）不算"已有资产"——那多半是
    // `Load Assets/Blank.json` 打底，后面条件 Load 才填内容。
    bool AssetExistsNonEmpty(string target)
    {
        string? path = tree.Resolve(target);
        if (path == null) return false;
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            JToken token = JToken.Parse(File.ReadAllText(path));
            return token switch
            {
                JObject obj => obj.HasValues,
                JArray arr => arr.Count > 0,
                _ => true,
            };
        }
        catch { return true; }
    }

    int conditionalLoads = options.Mods == null
        ? 0
        : ConditionalLoadDeterminization.Apply(
            pack.Patches, tokenResolver, AssetExistsNonEmpty, conversionNotes);
    if (conditionalLoads > 0)
        Console.WriteLine($"带条件的 Load 无条件化：{conditionalLoads} 个");

    DialogueQueryOverlap dialogueOverlap = DialogueQueryOverlap.Build(pack.Patches);
    IReadOnlySet<string> npcNames = NpcNames.Build(tree, pack.Patches);

    var judge = new PatchJudge(tree, pack.RootPath, vanilla, evaluateLeaf, dialogueOverlap, npcNames, pack.UniqueId, pack.FormatForCpMigration);

    // 分阶段计时（临时诊断，`SDVCP_PROFILE=1` 才开）
    // 用 **CPU 时间**而不是墙钟：这台机器噪声大到同一份二进制能差 65%（135s vs 82s），
    // 墙钟数字完全没法用来判断优化有没有用。
    using var profProc = System.Diagnostics.Process.GetCurrentProcess();
    TimeSpan profCpu = profProc.TotalProcessorTime;
    long profAlloc = GC.GetTotalAllocatedBytes(precise: false);
    void Phase(string name)
    {
        if (Environment.GetEnvironmentVariable("SDVCP_PROFILE") == null) return;
        profProc.Refresh();
        TimeSpan now = profProc.TotalProcessorTime;
        long alloc = GC.GetTotalAllocatedBytes(precise: false);
        Console.Error.WriteLine($"[prof] {name,-24} cpu={(long)(now - profCpu).TotalMilliseconds,8} ms"
                                + $" alloc={(alloc - profAlloc) / (1024 * 1024),7} MB");
        profCpu = now;
        profAlloc = alloc;
    }
    Phase("判定准备");

    var verdicts = pack.Patches.Select(judge.Judge).ToList();
    Phase("判定");

    // ★ **值里还有代不掉的 CP 令牌 ⇒ 整条不写**（「宁可少写，不可写错」）。
    //   游戏不认识 `{{…}}`（只认 `[Token]`，见 `TokenParser.cs`）：落在 `Condition` 里是非法 GSQ
    //   （条目**永远不生效**，且每次判定都记一条错误日志）、落在 `Sprite`/`Texture` 里取不到图、
    //   落在**事件键**里事件永不触发。CP 会在写入时把它代掉，我们代不了 —— 那就**不写**。
    //   量过账（tools/token-blocked-patches.py）：受影响 **58 条**（占已写入 1.6%），
    //   换掉的是 **431 处**永远不生效的内容。**只降级「本来会写」的那些**，
    //   已经因别的理由被拒的不动（免得把更具体的原因盖掉）。
    //   ⚠ 判据走 `WritePolicy.TokenGateBlocks` —— 与「会不会写」**同一处**。
    //     原先这里**枚举**了 `Converted`/`ConvertedNoCondition` 两个判定，于是
    //     `CreatesNewAsset` 从旁边漏了过去：实测语料 **19 条**新建资产补丁把
    //     **有损猜出来的** i18n 键写进了产物（`{{i18n:kid1_intro1_Andy{{Baby1Gender}}}}`）。
    int tokenBlocked = 0;
    foreach (PatchVerdict pv in verdicts)
    {
        if (!WritePolicy.TokenGateBlocks(pv)) continue;

        pv.Verdict = Verdict.NeedsReview;
        IReadOnlyList<string> stuck = ValueTokenSubstitutor.UnresolvedTokens(pv.Patch);
        pv.Reason = "值里含**无法静态解析**的 CP 令牌"
                    + (stuck.Count > 0 ? $"（`{{{{{string.Join("}}`/`{{", stuck)}}}}}`）" : "")
                    + "—— CP 会在写入时代掉，我们代不了；写出去就是**永远不生效**的条目"
                    + "（非法 GSQ / 取不到图 / 事件键非法），故整条不写";
        pv.Channel = Channel.None;
        tokenBlocked++;
    }
    if (tokenBlocked > 0)
        Console.WriteLine($"值里含代不掉的令牌、整条不写：{tokenBlocked} 条");

    Phase("令牌门");
    AnalysisReport report = AnalysisReport.Build(pack.RootPath, tree.Root, verdicts);
    Phase("首次报告");

    {
    // ★ 延迟写 + 地图内存模型缓存：所有补丁先在**内存**里算完，再一次性落盘。
        //   两个作用：
        //   ① 性能 —— 图片/地图不再「每个补丁拷/写一遍」；
        //   ② **正确性** —— 判定用的 `ResourceTree` 读的是**磁盘**，延迟写让它看不到
        //      同一次运行里刚写的（与「判定读输入树、写另一棵树」的旧行为一致）。
        //      实测：不延迟时会 13 个 `Data/Events/*` 与旧行为不一致。
        var output = new OutputTree(tree.Root, options.Out);
        var mapCache = new MapCache(output);
        // 图片的内存模型：同一张图只解码一次、改动累积在内存、最后统一重编码写回。
        // 实测本语料 `EditImage` 862 条打在 274 个目标上，`LooseSprites/Cursors` 一张被改 107 次。
        var imageCache = new ImageCache(output);
        // 地图落盘前要把 TMX 的翻转/旋转烘进图集（纯 XNB 没有 SMAPI 的 SDisplayDevice）。
        mapCache.Images = imageCache;
        // 链式转换（多个进程写同一棵树）里先保留标记，最后统一 bake-flips。
        mapCache.DeferFlips = options.DeferFlips;
        Console.WriteLine(output.Append
            ? $"续写已有资源树：{output.OutputRoot}"
            : $"已拷贝资源树到：{output.OutputRoot}");

        // 内容包的 i18n 翻译：含 {{i18n:...}} 的资产按语言展开为带后缀的多个资产（通道 B1）。
        PackTranslations translations = PackTranslations.Load(pack.RootPath);
        // 地图属性里的 `{{i18n:…}}` 用本包译文冻结（地图没有语言变体）。
        mapCache.Translations = translations;
        if (translations.HasAny)
        {
            Console.WriteLine(
                $"内容包 i18n：{translations.VariantLocales.Count} 个语言变体" +
                (translations.VariantLocales.Count > 0
                    ? $"（{string.Join(", ", translations.VariantLocales)}）"
                    : "（只有 default，无后缀变体）"));
        }

        // 内容包的 DynamicTokens：i18n 键里的内嵌令牌（如 {{GenderNeutralRSV}}）先展开再查键。
        // 带 When 的令牌块由 Baker 求值；求不了值就不猜（该键判失败）。
        if (!tokenResolver.IsEmpty)
            Console.WriteLine($"内容包 DynamicTokens：{tokenResolver.Names.Count} 个" +
                              (options.Mods == null ? "（未提供 --mods，带 When 的令牌无法求值）" : ""));

        var rawCache = new RawAssetCache();
        // npcNames 与判定用的是同一个集合：写入 $query 时也要判定 Data/Festivals 的键是不是 NPC 对话，
        // 两处不一致会导致「判定说能写、写入说不能」。
        var applier = new PatchApplier(output, translations, rawCache, tokenResolver, npcNames, pack.RootPath);
        var loader = new LoadApplier(output, pack.RootPath, translations, rawCache, tokenResolver, mapCache, imageCache);
        var imageApplier = new ImageApplier(output, pack.RootPath, imageCache);
        var imageEditApplier = new EditImageApplier(output, pack.RootPath, imageCache);

        // `{{InternalAssetKey}}` 指向的包内图片：现物化成 `SMAPI/<id>/<路径>` 资产，
        // 游戏按 CP 给出的资产名直接加载（纯 XNB 里没有 SMAPI 的服务层）。
        foreach ((string assetName, string relPath) in internalAssets)
        {
            MergeOutcome materialized = imageApplier.Apply(assetName, relPath);
            if (!materialized.Success)
            {
                string warning = $"⚠ InternalAssetKey 素材 `{assetName}` 未物化：{materialized.FailureReason}";
                Console.WriteLine(warning);
                conversionNotes.Add(warning);
            }
        }
        int written = 0, failed = 0;

        foreach (PatchVerdict pv in verdicts)
        {
            if (!WritePolicy.ShouldWrite(pv))
                pv.WriteNote = WritePolicy.SkipReason(pv);
        }

        // 通道 C：烘焙。提供了 --mods 才求值；未提供时保持 M1 判定（NeedsReview + 原因里的说明）。
        // 求值为真 → 降级为无条件写入；为假 → 该补丁不适用，跳过。
        var bakedTrue = RunBake(options.Mods, options.ModIndexCache, pack, verdicts);

        // 婚礼台词改写的母本：判定阶段用同一个 ResolveDefaultEventScript 取过，两处必须一致。
        string? defaultWeddingScript = WeddingScriptRewriter.ResolveDefaultEventScript(tree);

        // 单条补丁的写入（Load / EditImage / EditData 三分支）——阶段 1、2 共用这一个实现，绝不写两份。
        void ApplyOneCore(PatchVerdict pv, bool lastForAsset = false)
        {
            string? failure = null;
            // 成功但有保留意见（**有损确定化** / **`{{Random}}` 确定化**）—— 必须报出来，不得混进「无损」。
            var writeNotes = new List<string>();
            if (determinized.TryGetValue(pv.Patch, out List<string>? dz)) writeNotes.AddRange(dz);
            bool RecordWriteNotes(MergeOutcome o)
            {
                if (!o.Success) { failure = o.FailureReason; return false; }
                writeNotes.AddRange(o.Notes);
                return true;
            }
            string? rewrittenTarget = null;

            // ⚠ **逐目标独立成败**：CP 的 `PatchLoader.SplitPatches` 把逗号列表拆成**互相独立**的补丁，
            //   所以一个坏目标名只该让**那一个**目标落空。实测 SVE 的
            //   `Characters/Dialogue/**Martin**`（作者自己写的错项，夹在 ~45 个目标中间）：
            //   旧实现走到它就把整条补丁判「写入失败」，**它后面那几十个目标一个都没写**。
            //   （CP 那边这条会去 Load 一个名字带 `*` 的资产 —— 运行时没人读它，等于什么都不做。）
            bool SkipIfBadTarget(string target)
            {
                string? bad = output.ValidatePath(AssetCreator.RelativePath(target, ".json"));
                if (bad == null) return false;
                writeNotes.Add($"目标 `{target}` 的路径非法（{bad}），**跳过这一个目标**，其余照写");
                return true;
            }

            if (pv.Patch.Action.Equals("Load", StringComparison.OrdinalIgnoreCase))
            {
                // 图片 Load（FromFile 是 .png）走逐字节复制路径；数据 Load 走 JSON 路径。
                foreach (string target in TargetNames(pv.Patch.Target))
                {
                    if (SkipIfBadTarget(target)) continue;
                    MergeOutcome outcome = WritePolicy.IsImageLoad(pv.Patch)
                        ? imageApplier.Apply(target, pv.Patch.FromFile!, target)
                        : loader.Apply(target, pv.Patch.FromFile!, target);
                    if (!RecordWriteNotes(outcome)) break;
                }
            }
            else if (pv.Patch.Action.Equals("EditMap", StringComparison.OrdinalIgnoreCase))
            {
                // Task 12a：只做属性类改动（MapProperties / AddWarps / AddNpcWarps /
                // 根为 MapProperties 的 TextOperations），完全不碰瓦片几何。
                JObject writeRaw = pv.Patch.Raw;
                bool canWrite = true;
                if (pv.Channel == Channel.DayNightAmbient)
                {
                    JObject? rewritten = MapAmbientRewriter.Rewrite(pv.Patch.Raw, pv.DayNightVisibleAtDay);
                    if (rewritten == null)
                    {
                        failure = "昼夜 AmbientLight 没有可写入的颜色值";
                        canWrite = false;
                    }
                    else
                    {
                        writeRaw = rewritten;
                    }
                }
                if (canWrite)
                {
                    foreach (string target in TargetNames(rewrittenTarget ?? pv.Patch.Target))
                    {
                        if (SkipIfBadTarget(target)) continue;
                        if (!RecordWriteNotes(MapApplier.Apply(output, target, writeRaw, pack.RootPath, mapCache, imageCache,
                                // 条件化地图改动的门禁（判定侧算好的 GSQ；见 `PatchVerdict.DoorGateGsq`）
                                pv.DoorGateGsq, translations: translations,
                                weatherDayNightOverlay: pv.Channel == Channel.DayNightTiles,
                                dayNightVisibleAtDay: pv.DayNightVisibleAtDay)))
                            break;
                    }
                }
            }
            else if (pv.Patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase))
            {
                // 图片合成：判定与写入共用同一个 (Target × FromFile) 展开（EditImageApplier.ExpandJobs），
                // 保证两侧条数口径一致；只有折叠后无条件的才判可写，所以这里不再处理条件。
                if (!EditImageApplier.TryParseAreas(pv.Patch, out ImageArea? fromArea, out ImageArea? toArea,
                        out string? areaError))
                    failure = areaError;
                else if (!EditImageApplier.TryParseMode(pv.Patch.Raw["PatchMode"]?.Value<string>(),
                             out PatchImageMode mode, out string? modeError))
                    failure = modeError;
                else
                {
                    IReadOnlyList<EditImageJob> jobs = EditImageApplier.ExpandJobs(pv.Patch);
                    if (jobs.Count == 0)
                        failure = "EditImage 缺少 Target 或 FromFile";
                    else
                    {
                        foreach (EditImageJob job in jobs)
                        {
                            if (SkipIfBadTarget(job.Target)) continue;
                            MergeOutcome outcome = imageEditApplier.Apply(
                                job.Target, job.FromFile, mode, fromArea, toArea, job.Target);
                            if (!RecordWriteNotes(outcome)) break;
                        }
                    }
                }
            }
            else
            {
                // 通道 A：先把 When 翻成 GSQ。翻译失败就整条跳过（不得猜、不得写出无条件版本）。
                string? gsq = null;
                LandingKind? landing = null;
                long tPatch = SdvCpTool.Core.Diagnostics.Prof.Now();
                // 直接复用已解析的补丁对象：DTZ 的 22 条大补丁光是 ToString→Parse 就要 80 s。
                JObject writePatch = pv.Patch.Raw;

                if (pv.Channel == Channel.NativeKey)
                {
                    // 原生键改写：节日配偶对话（<NPC>_spouse）、「键即条件」（删冗余条件）、
                    // 婚礼台词（Data/Weddings 的 EventScript[<配偶名>]）。
                    // 调用判定阶段的同一个识别函数，拿不到改写结果 = 两处判定不一致 ——
                    // 判失败，绝不写出原始条件版本。
                    // 顺序必须与判定阶段**完全一致** —— 任一处不同就会出现「判定说 A、写入做 B」。
                    // 音频注入**不改写 patch** —— 它直接落进产出树的 XACT 清单（见 AudioChangesMerger），
                    // 所以这里原样放行，由下面的 ApplyEditData 分派到音频分支。
                    JObject? rewritten = AudioChangesMerger.CanHandle(pv.Patch)
                        ? pv.Patch.Raw
                        : SpouseKeyRewriter.TryRewrite(pv.Patch)
                          ?? KeyEncodedVariantRewriter.TryRewrite(pv.Patch)
                          ?? WeddingScriptRewriter.TryRewrite(pv.Patch, defaultWeddingScript)
                          ?? TryEventKeyRewrite(pv, tree);
                    if (rewritten == null)
                        failure = "判定为原生键改写，但写入阶段识别失败（判定与写入不一致）";
                    else
                    {
                        writePatch = rewritten;
                        // 原生键改写通常保持 Target 不变；婚礼台词改写会换成 Data/Weddings，
                        // 写入必须落到改写后的目标上，否则写回了 strings/StringsFromCSFiles。
                        rewrittenTarget = rewritten["Target"]?.Value<string>();
                    }
                }
                else if (pv.Verdict == Verdict.Converted && pv.Channel == Channel.Gsq)
                {
                    // 用判定阶段存下的折叠树；没有才回退到原始 When（未启用折叠时）。
                    // 绝不重新解析原始 When —— 否则判定与写入又各解释一遍。
                    GsqTranslation translation = GsqTranslator.Translate(
                        pv.FoldedWhen ?? WhenParser.Parse(pv.Patch.When));
                    if (!translation.Success)
                        failure = $"条件无法翻译成 GSQ：{translation.FailureReason}";
                    else if (pv.Landing == null)
                        failure = "判定为条件化写入，但判定阶段没有记录条件落点";
                    else
                    {
                        gsq = translation.Gsq;
                        landing = pv.Landing;
                    }
                }

                if (SdvCpTool.Core.Diagnostics.Prof.On)
                    SdvCpTool.Core.Diagnostics.Prof.AddSince("Write.补丁准备", tPatch);
                if (failure == null)
                {
                    tPatch = SdvCpTool.Core.Diagnostics.Prof.Now();
                    foreach (string target in TargetNames(rewrittenTarget ?? pv.Patch.Target))
                    {
                        if (SkipIfBadTarget(target)) continue;
                        bool isArray = ArrayAssets.IsArrayAsset(target);
                        MergeOutcome outcome = applier.ApplyEditData(
                            target, writePatch, isArray, gsq, landing, lastForAsset);
                        if (!RecordWriteNotes(outcome)) break;
                    }
                    if (SdvCpTool.Core.Diagnostics.Prof.On)
                        SdvCpTool.Core.Diagnostics.Prof.AddSince("Write.逐目标", tPatch);
                }
            }

            if (failure == null)
            {
                pv.Written = true;
                Interlocked.Increment(ref written);

                if (writeNotes.Count > 0)
                    pv.WriteNote = string.Join("；", writeNotes.Distinct(StringComparer.Ordinal));
            }
            else { pv.Written = false; pv.WriteNote = failure; Interlocked.Increment(ref failed); }
        }

        // 兜底：写入路径上任何<b>意外异常</b>（含坏路径引发的 IO 异常）都降级为「该条写入失败」，
        // 绝不让单条补丁把整次转换带走。实测教训：SVE 的 `**Martin**` 让整包零产出、连报告都没有。
        // 按动作累计耗时（临时诊断，`SDVCP_PROFILE=1` 才开）
        var actMs = new Dictionary<string, long>(StringComparer.Ordinal);
        var actN = new Dictionary<string, long>(StringComparer.Ordinal);
        // 阶段 2 会按目标资产并行跑，计时账本要加锁。
        object actLock = new();

        void ApplyOne(PatchVerdict pv, bool lastForAsset = false)
        {
            using var actP = System.Diagnostics.Process.GetCurrentProcess();
            TimeSpan actCpu0 = actP.TotalProcessorTime;
            try
            {
                ApplyOneCore(pv, lastForAsset);
            }
            catch (Exception ex)
            {
                // ApplyOneCore 只在末尾置 Written，抛出时必仍为 null；这里补记失败并不重复计数。
                if (pv.Written == null)
                {
                    pv.Written = false;
                    pv.WriteNote = $"写入时异常：{ex.Message}";
                    failed++;
                }
            }
            finally
            {
                if (Environment.GetEnvironmentVariable("SDVCP_PROFILE") != null)
                {
                    actP.Refresh();
                    string k = pv.Patch.Action;
                    long ms = (long)(actP.TotalProcessorTime - actCpu0).TotalMilliseconds;
                    lock (actLock)
                    {
                        actMs[k] = actMs.GetValueOrDefault(k) + ms;
                        actN[k] = actN.GetValueOrDefault(k) + 1;
                    }
                }
            }
        }

        // 本任务要写入的补丁：判定可写，或通道 C 烘焙求值为真（降级为无条件）。
        // ⚠ **已被集合发射认领的（`GroupOwned`）不再直写**：那些补丁的内容已经并进组合产物
        //   （四季图集组按季收走的那几条），再直写一遍就是同一份内容落两处。
        //   以前靠"判定本来就不可写"来保证这一点，但四季图集组现在**优先于直写**（见 `EmitTilesheetGroups`），
        //   组里可能有本来可写的成员 ⇒ 必须在这里显式挡一道。
        //   ⚠ **别用 `GroupEmitted` 挡**：那个是报告口径，同一张资产上的补丁都会被标上，
        //   包括提供底图的无条件补丁 —— 挡了就"底图不写了"（实测 prof91 的 13 条退化）。
        bool Writable(PatchVerdict v) =>
            (WritePolicy.ShouldWrite(v) || bakedTrue.Contains(v)) && !v.GroupOwned;

        // 阶段 1：Load 打底（ApplyOrder 保证 Load 恒在编辑补丁之前）。
        foreach (PatchVerdict pv in ApplyOrder.Sort(verdicts.Where(Writable)))
        {
            if (pv.Patch.Action.Equals("Load", StringComparison.OrdinalIgnoreCase))
                ApplyOne(pv);
        }

        // 阶段 1.5：四季贴图**集合发射**（Task 29）。
        // 4 条条件 EditImage（春夏秋冬各一条、写进同一格）合成为一个原生「四季 BigCraftable」：
        // 四季横排成 4 个连续索引 + Data/BigCraftables 的 Name 加 Seasonal。
        // <b>必须夹在阶段 1 与阶段 2 之间</b>：改图要在 Load 打底之后，
        // 改 Name 要在该 EditData 补丁被写出去之前 —— 两件事错开就成半套。
        var seasonalGroups = SeasonalSpritePlanner.Plan(verdicts,
            v => WritePolicy.ShouldWrite(v) || bakedTrue.Contains(v));
        int seasonalEmitted = 0, seasonalRejected = 0, seasonalPatches = 0;
        SeasonalEmissionAudit? seasonalAudit = null;
        SeasonalEmissionAudit? tilesheetAudit = null;
        SeasonalEmissionAudit? appearanceAudit = null;
        // 集合发射的**结局**先攒着，等最终报告重建后再回填进归一化结果 ——
        // 中间那次 `report = AnalysisReport.Build(...)` 会把意图重新算一遍，标记会丢。
        var emittedMarks =
            new List<(string Base, VariantCarrier Carrier, VariantDimension Dim, string Mechanism, bool Emitted, int Patches)>();
        var seasonalApproximate = 0;
        var seasonalLost = new List<string>();
        var seasonalRejects = new List<string>();
        foreach (SeasonalSpriteGroup group in seasonalGroups)
        {
            // 组内成员补丁：报告里要能逐条看出「为什么这个四季组没发射」。
            IEnumerable<PatchVerdict> Members() => verdicts.Where(v =>
                v.Patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase)
                && SameAsset(v.Patch.Target, group.Target));

            if (group.Reject != null)
            {
                seasonalRejected++;
                seasonalRejects.Add(group.Reject);
                emittedMarks.Add((group.Target, VariantCarrier.Sprite, VariantDimension.Season,
                    "", false, group.Variants.Count));
                foreach (PatchVerdict pv in Members())
                    pv.WriteNote = $"四季组未发射（{group.Variants.Count} 条保持原判）：{group.Reject}";
                continue;
            }

            if (SeasonalSpriteEmitter.TryEmit(output, imageCache, pack.RootPath, verdicts, group, out string? emitError))
            {
                seasonalEmitted++;
                seasonalPatches += group.Variants.Count;
                if (group.LostConditions.Count > 0)
                {
                    seasonalApproximate++;
                    foreach (string c in group.LostConditions)
                        if (!seasonalLost.Contains(c, StringComparer.OrdinalIgnoreCase)) seasonalLost.Add(c);
                }
                string rel = AssetCreator.RelativePath(group.Target, ".png");
                if (!output.CreatedFiles.Contains(rel, StringComparer.OrdinalIgnoreCase))
                    output.CreatedFiles.Add(rel);
                emittedMarks.Add((group.Target, VariantCarrier.Sprite, VariantDimension.Season,
                    "四季 BigCraftable 索引（Name 加 Seasonal）", true, group.Variants.Count));
                foreach (PatchVerdict pv in Members())
                {
                    pv.WriteNote = $"已归一到四季组发射（{group.Variants.Count} 条合 1 张贴图 + 1 处数据改写）"
                        + (group.LostConditions.Count > 0
                            ? $"；**近似**：原生机制装不下 {string.Join("/", group.LostConditions)}" : "");
                    // 自己没写出、但意图已由组发射产出 —— 报告里不得再算成 blocked。
                    pv.GroupEmitted = true;
                }
            }
            else
            {
                seasonalRejected++;
                seasonalRejects.Add(emitError ?? "未说明");
                foreach (PatchVerdict pv in Members())
                    pv.WriteNote = $"四季组发射失败：{emitError}";
            }
        }
        if (seasonalGroups.Count > 0)
            Console.WriteLine($"四季贴图集合发射：{seasonalEmitted} 组成，{seasonalRejected} 组未发射"
                + $"（覆盖 {seasonalPatches} 条条件 EditImage）");
        seasonalAudit = new SeasonalEmissionAudit
        {
            Groups = seasonalGroups.Count,
            Emitted = seasonalEmitted,
            Rejected = seasonalRejected,
            PatchesCovered = seasonalPatches,
            ApproximateGroups = seasonalApproximate,
            LostDimensions = seasonalLost,
            RejectReasons = seasonalRejects
                .GroupBy(r => r, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Count() > 1 ? $"{g.Key}（{g.Count()} 组）" : g.Key)
                .ToList(),
        };

        // 立绘/行走图的 Appearance[] 组（Task 32）。
        SeasonalEmissionAudit EmitAppearanceGroups()
        {
            // 「NPC 已知」= 原版 Data/Characters 里的键 + 本包 EditData 新建的键。
            var knownNpcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? charPath = tree.Resolve("Data/Characters");
            if (charPath != null && File.Exists(charPath))
            {
                try
                {
                    if (JObject.Parse(File.ReadAllText(charPath)).Properties().Select(p => p.Name)
                        is IEnumerable<string> names)
                        foreach (string n in names) knownNpcs.Add(n);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ 读 Data/Characters 失败，Appearance 组会全部判「NPC 未知」：{ex.Message}");
                }
            }
            foreach (PatchVerdict v in verdicts)
                if (v.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)
                    && (v.Patch.Target ?? "").Replace('\\', '/').Trim('/')
                        .Equals("Data/Characters", StringComparison.OrdinalIgnoreCase)
                    && v.Patch.Raw["Entries"] is JObject e)
                    foreach (JProperty p in e.Properties()) knownNpcs.Add(p.Name);

            var groups = AppearancePlanner.Plan(verdicts,
                knownNpcs, v => WritePolicy.ShouldWrite(v) || bakedTrue.Contains(v));

            int emitted = 0, rejected = 0, covered = 0, approximate = 0;
            var lost = new List<string>();
            var rejects = new List<string>();

            foreach (AppearanceGroup group in groups)
            {
                IEnumerable<PatchVerdict> Members() => verdicts.Where(v =>
                    v.Patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase)
                    && AppearancePlanner.NpcOf(v.Patch.Target, out _) is string n
                    && string.Equals(n, group.Npc, StringComparison.OrdinalIgnoreCase));
                int count = group.Variants.Count;

                if (group.Reject != null)
                {
                    rejected++; rejects.Add(group.Reject);
                    emittedMarks.Add((group.SpriteAsset ?? group.PortraitAsset ?? "", VariantCarrier.Sprite,
                        VariantDimension.SeenEvent, "", false, count));
                    foreach (PatchVerdict pv in Members())
                        pv.WriteNote = $"外观组未发射（{count} 条保持原判）：{group.Reject}";
                    continue;
                }

                if (!AppearanceEmitter.TryEmit(output, imageCache, pack.RootPath, verdicts, group, out var res, out string? err))
                {
                    rejected++; rejects.Add(err ?? "未说明");
                    foreach (PatchVerdict pv in Members())
                        pv.WriteNote = $"外观组发射失败：{err}";
                    continue;
                }

                // 原版 NPC：合成的改写要**立刻**施加 —— 走的是同一条 ApplyEditData。
                if (res!.DirectPatchJson != null)
                {
                    MergeOutcome applied = applier.ApplyEditData("Data/Characters", res.DirectPatchJson, false);
                    if (!applied.Success)
                    {
                        rejected++; rejects.Add($"`Data/Characters` 改写失败：{applied.FailureReason}");
                        foreach (PatchVerdict pv in Members())
                            pv.WriteNote = $"外观组发射失败：Data/Characters 改写失败：{applied.FailureReason}";
                        continue;
                    }
                }

                // ⚠ 覆盖数要数**被标记的补丁**，不是变体数 —— 一个变体同时覆盖
                //   `Characters/X` 与 `Portraits/X` 两条（实测 SVE 因此少算 5 条）。
                foreach (PatchVerdict pv in Members())
                {
                    pv.WriteNote = $"已归一到原版 Appearance[]（{count} 个外观变体 → "
                        + $"{res.Assets.Count} 份独立资产 + 1 处 Data/Characters 条目）";
                    pv.GroupEmitted = true;
                    covered++;
                }
                emitted++;
                if (group.Variants.Any(v => v.IsOutdoors == true)) approximate++;
            }

            if (groups.Count > 0)
                Console.WriteLine($"外观（Appearance[]）集合发射：{emitted} 组成，{rejected} 组未发射（覆盖 {covered} 条条件 EditImage）");

            return new SeasonalEmissionAudit
            {
                Groups = groups.Count, Emitted = emitted, Rejected = rejected,
                PatchesCovered = covered, ApproximateGroups = approximate, LostDimensions = lost,
                RejectReasons = rejects.GroupBy(r => r, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Count() > 1 ? $"{g.Key}（{g.Count()} 组）" : g.Key).ToList(),
            };
        }

        // 四季图集组（Task 30）：另一套机制，判据与产物都不同，单独统计。
        SeasonalEmissionAudit EmitTilesheetGroups()
        {
            ConfigSchemaTokens schema = ConfigSchemaTokens.Load(pack.RootPath);
            // ★ **可直写的补丁也参与分组**（谓词恒 `false`）：四季组是**归一化**，比"只留一季的直写"更忠实。
            //   实测（2026-10-13）`167_Seasonal Cute Characters SVE` 的 `Maps/Spring_z_extras`：四条
            //   `Season |contains=<季>: true` 里"春"那条在季节确定化下变成可直写，谓词一挡就凑不齐四季
            //   ⇒ **四季变体全丢**（树侧少 8 个文件：`{spring,summer,fall}_Spring_z_extras.*`），
            //   只剩春天那份。组发射之后由 `Writable` 里的 `GroupEmitted` 抑制直写，不会落两遍。
            var groups = SeasonalTilesheetPlanner.Plan(verdicts,
                _ => false,
                (name, value) =>
                {
                    // 配置令牌按 CP 语义求值：包里有这个字段名才算，取 config.json/Default 解析出的值集。
                    if (!schema.Contains(name)) return null;
                    if (!schema.TryGetValues(name, out IReadOnlyList<string> values)) return null;
                    string want = value.Type == JTokenType.Boolean
                        ? (value.Value<bool>() ? "true" : "false")
                        : value.ToString();
                    return values.Any(x => string.Equals(x, want, StringComparison.OrdinalIgnoreCase));
                });

            int emitted = 0, rejected = 0, covered = 0, approximate = 0;
            var lost = new List<string>();
            var rejects = new List<string>();

            foreach (SeasonalTilesheetGroup group in groups)
            {
                IEnumerable<PatchVerdict> Members() => verdicts.Where(v =>
                    v.Patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase)
                    && SameAsset(v.Patch.Target, group.Target));
                int count = group.BySeason.Values.Sum(l => l.Count);

                if (group.Reject != null)
                {
                    rejected++; rejects.Add(group.Reject);
                    emittedMarks.Add((group.Target, VariantCarrier.Sprite, VariantDimension.Season,
                        "", false, count));
                    foreach (PatchVerdict pv in Members())
                        pv.WriteNote = $"四季图集组未发射（{count} 条保持原判）：{group.Reject}";
                    continue;
                }

                if (SeasonalTilesheetEmitter.TryEmit(output, imageCache, mapCache, pack.RootPath, group, out var res, out string? err))
                {
                    emitted++; covered += count;
                    emittedMarks.Add((group.Target, VariantCarrier.Sprite, VariantDimension.Season,
                        "图集文件名季节前缀 + 地图改指", true, count));
                    if (group.LostConditions.Count > 0)
                    {
                        approximate++;
                        foreach (string c in group.LostConditions)
                            if (!lost.Contains(c, StringComparer.OrdinalIgnoreCase)) lost.Add(c);
                    }
                    string note = $"已归一到四季图集发射（{count} 条 → {res!.Assets.Count} 份 <季>_ 图集"
                        + $" + {res.Maps.Count} 张地图改指）"
                        + (group.LostConditions.Count > 0
                            ? $"；**近似**：原生机制装不下 {string.Join("/", group.LostConditions)}" : "");
                    foreach (PatchVerdict pv in Members())
                        pv.WriteNote = note;

                    // ⚠ **只有"被这一组按季收走的"才算认领**（`group.BySeason`），不是该 target 上的全部：
                    //   同一张图上还有**无条件**的 `EditImage`（它们提供底图），按认领挡掉就是"底图不写了"。
                    //   报告口径：这些补丁显示为"已发射"（`GroupEmitted`），写入侧靠 `GroupOwned` 抑制直写。
                    foreach (PatchVerdict pv in group.BySeason.Values.SelectMany(list => list))
                    {
                        pv.GroupEmitted = true;
                        pv.GroupOwned = true;
                    }
                }
                else
                {
                    rejected++; rejects.Add(err ?? "未说明");
                    foreach (PatchVerdict pv in Members())
                        pv.WriteNote = $"四季图集组发射失败：{err}";
                }
            }

            if (groups.Count > 0)
                Console.WriteLine($"四季图集集合发射：{emitted} 组成，{rejected} 组未发射（覆盖 {covered} 条条件 EditImage）");

            return new SeasonalEmissionAudit
            {
                Groups = groups.Count,
                Emitted = emitted,
                Rejected = rejected,
                PatchesCovered = covered,
                ApproximateGroups = approximate,
                LostDimensions = lost,
                RejectReasons = rejects
                    .GroupBy(r => r, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Count() > 1 ? $"{g.Key}（{g.Count()} 组）" : g.Key)
                    .ToList(),
            };
        }

        // 阶段 1.5b：四季**图集**集合发射（Task 30）——另一套机制，与上面那个**不共用判据**：
        // 那个走「BigCraftable 四季索引」，这个走「文件名季节前缀 + 改地图 TileSheet.ImageSource」。
        // 必须也在阶段 2 之前：地图改指要在后续可能的 EditMap 之前落定。
        tilesheetAudit = EmitTilesheetGroups();

        // 阶段 1.5c：立绘/行走图的 `Appearance[]` 集合发射（Task 32）——第三套机制。
        // 必须在阶段 2 之前：条目要么就地追加进本包那条建条目的补丁（由它自己写出去），
        // 要么合成一次 `Data/Characters` 改写走同一个 ApplyEditData。
        appearanceAudit = EmitAppearanceGroups();

        // 判定可见性（Task 10i 必须真生效，否则同包 Load 出来的目标在写入阶段仍被判 TargetMissing）：
        // 写入落在产出树，判定查询的是资源树 —— 两棵树不打通，
        // 就会出现「自己刚 Load 出来的资产，下一步 Edit* 判 TargetMissing」
        // （铁证：Data/Extras/RSVMap.json idx1 Load LooseSprites/RSVIcon → written:true，
        //  idx2 EditImage 同一资产 → TargetMissing）。
        // 把本次新建的资产登记进判定树，然后<b>只重判 TargetMissing 的补丁</b> ——
        // TargetMissing 是唯一会因「新建可见」翻转的判定；其余判定本就不依赖本次运行的新建，
        // 一并重判只会让分类抖动（把「新建资产」重标成「无条件可转」，两者行为等价但报告含义变了）。
        // 这一步放在两个写入阶段之间：阶段 2 的编辑补丁（尤其 EditImage）在<b>写入前</b>就能看见同包 Load 的产物。
        Phase("阶段1 写入");
        tree.RegisterCreatedAssets(output);
        var rejudge = new PatchJudge(tree, pack.RootPath, vanilla, evaluateLeaf, dialogueOverlap, npcNames, pack.UniqueId, pack.FormatForCpMigration);
        foreach (PatchVerdict pv in verdicts.Where(v => v.Verdict == Verdict.TargetMissing))
        {
            PatchVerdict fresh = rejudge.Judge(pv.Patch);
            pv.Verdict = fresh.Verdict;
            pv.Reason = fresh.Reason;
            pv.SuggestedChannel = fresh.SuggestedChannel;
            pv.Channel = fresh.Channel;
            pv.Landing = fresh.Landing;
            pv.FoldedWhen = fresh.FoldedWhen;
            // ★ 门禁 GSQ 必须一起带过来：`TargetMissing` 的补丁重判后可能走「条件化地图改动」通道，
            //   漏了这一行，写入侧拿到的 `DoorGateGsq` 还是 null ⇒ 图能改、门禁没落。
            //   实测 Grandpa's Farm 的 `Maps/Farm,Maps`（Backwoods 楼梯）：初判 TargetMissing
            //   （多目标里的 `Maps` 不存在），重判成 ConvertedNoCondition 后门禁一直没写。
            pv.DoorGateGsq = fresh.DoorGateGsq;

            // 只有判定真的翻盘的才补跳过缘由（原先 TargetMissing 的缘由是 null）。
            // 仍判 TargetMissing 的保持原样，不制造无谓的报告抖动。
            if (fresh.Verdict != Verdict.TargetMissing && pv.Written == null)
                pv.WriteNote = WritePolicy.SkipReason(pv);
        }

        // 阶段 2 的**合批快路径**：同一资产的多条纯 Entries、无 i18n、无条件落点补丁
        // 只克隆一次文档、按顺序合并、最后写一次。行内补丁常把 `Data/Objects` 这类大文档
        // 反复改几十次 —— 逐条克隆是 O(补丁数 × 文档节点数)，合批后是 O(资产数 × 节点数)。
        // 任何边界不满足（i18n/条件/新资产/非 Entries/产生 notes）就返回 null，
        // 这些补丁仍走原来的逐条写入，输出语义不变。
        int batchGroups = 0, batchPatches = 0;
        // 同一目标还有多少条可写的 EditData（含不合批的）—— 用来判断这批是不是该资产最后一次写。
        var editTotals = verdicts
            .Where(v => v.Written == null && Writable(v)
                        && v.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(v.Patch.Target))
            .GroupBy(v => v.Patch.Target!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var batchCandidates = ApplyOrder.Sort(verdicts.Where(v =>
                v.Written == null && Writable(v)
                && v.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)
                && v.Channel == Channel.None
                && v.Landing == null
                && v.Patch.Raw["Entries"] is JObject
                && (v.Patch.Target ?? "").Length > 0
                && !(v.Patch.Target ?? "").Contains(',')
                && !determinized.ContainsKey(v.Patch)
                && !AudioChangesMerger.CanHandle(v.Patch)))
            .ToList();
        foreach (var group in batchCandidates.GroupBy(v => v.Patch.Target, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            List<PatchVerdict> members = group.ToList();
            bool lastForAsset = editTotals.TryGetValue(group.Key ?? "", out int total)
                                && total == members.Count;
            List<MergeOutcome>? outcomes = applier.TryApplyEditDataBatch(
                group.Key ?? "", members.Select(v => v.Patch.Raw).ToList(), lastForAsset);
            if (outcomes == null) continue;

            foreach (PatchVerdict pv in members)
            {
                pv.Written = true;
                written++;
            }
            batchGroups++;
            batchPatches += members.Count;
        }
        if (batchGroups > 0)
        {
            Console.WriteLine($"EditData 合批：{batchGroups} 组 / {batchPatches} 条");
            if (Environment.GetEnvironmentVariable("SDVCP_PROFILE") != null)
                Console.Error.WriteLine($"[prof]   EditData 合批 {batchGroups} 组 / {batchPatches} 条");
        }

        // 阶段 2：编辑补丁（EditData / EditImage）。重判后新翻盘的补丁在这里被写入 ——
        // 这正是 Task 10i 曾经「只翻判定、不写入」的边界被补上的地方。
        var phase2 = ApplyOrder.Sort(verdicts.Where(v => v.Written == null && Writable(v))).ToList();
        var lastEditByTarget = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < phase2.Count; i++)
        {
            PatchVerdict v = phase2[i];
            if (!v.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(v.Patch.Target) || v.Patch.Target.Contains(',')) continue;
            lastEditByTarget[v.Patch.Target] = i;
        }
        // ★ 阶段 2 按**目标资产**分 lane 并行：同一资产的多条补丁仍在同一条 lane 里按序执行，
        //   不同资产互不相干（暂存字典已加锁）。拿不准的（多目标、原生键改写会换目标、
        //   EditImage/EditMap 走各自的内存模型）留在串行道上 —— 串行补丁之前先把已攒的
        //   并行组跑完，保证它看到的前序写入与串行执行时一致。
        static int LaneThreads()
        {
            string? raw = Environment.GetEnvironmentVariable("SDVCP_LANES");
            if (int.TryParse(raw, out int n) && n > 0) return n;
            return Math.Max(1, Math.Min(Environment.ProcessorCount / 2, 6));
        }

        int laneThreads = LaneThreads();
        bool Laneable(PatchVerdict v)
        {
            if (!v.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)) return false;
            string? target = v.Patch.Target;
            if (string.IsNullOrEmpty(target) || target.Contains(',')) return false;
            return v.Channel != Channel.NativeKey;
        }

        var lanes = new Dictionary<string, List<(PatchVerdict Pv, bool Last)>>(StringComparer.OrdinalIgnoreCase);
        var laneOrder = new List<string>();
        void RunLaneBatch()
        {
            if (lanes.Count == 0) return;
            if (lanes.Count > 1 && laneThreads > 1)
            {
                Parallel.ForEach(laneOrder, new ParallelOptions { MaxDegreeOfParallelism = laneThreads },
                    key =>
                    {
                        foreach ((PatchVerdict lanePv, bool laneLast) in lanes[key])
                            ApplyOne(lanePv, laneLast);
                    });
            }
            else
            {
                foreach (string key in laneOrder)
                    foreach ((PatchVerdict lanePv, bool laneLast) in lanes[key])
                        ApplyOne(lanePv, laneLast);
            }
            lanes.Clear();
            laneOrder.Clear();
        }

        for (int i = 0; i < phase2.Count; i++)
        {
            PatchVerdict pv = phase2[i];
            if (pv.Patch.Action.Equals("Load", StringComparison.OrdinalIgnoreCase))
                continue;   // Load 已在阶段 1 落盘
            bool lastForAsset = pv.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrEmpty(pv.Patch.Target)
                                && lastEditByTarget.TryGetValue(pv.Patch.Target, out int last)
                                && last == i;
            if (laneThreads > 1 && Laneable(pv))
            {
                string laneKey = pv.Patch.Target!.Replace('\\', '/').Trim('/');
                if (!lanes.TryGetValue(laneKey, out var list))
                {
                    list = new List<(PatchVerdict, bool)>();
                    lanes[laneKey] = list;
                    laneOrder.Add(laneKey);
                }
                list.Add((pv, lastForAsset));
            }
            else
            {
                RunLaneBatch();
                ApplyOne(pv, lastForAsset);
            }
        }
        RunLaneBatch();

        Phase("阶段2 写入");
        if (Environment.GetEnvironmentVariable("SDVCP_PROFILE") != null)
            foreach (var kv in actMs.OrderByDescending(k => k.Value))
                Console.Error.WriteLine($"[prof]   动作 {kv.Key,-10} {kv.Value,8} ms / {actN[kv.Key],5} 条");
        // Summary 是判定后取的一次快照；判定变过就得重算，否则报告里还是旧数字。
        report = AnalysisReport.Build(pack.RootPath, tree.Root, verdicts);
        report.SeasonalEmission = seasonalAudit;
        // ★ 回填集合发射的**结局** —— 归一化报告里那一档「推的收敛」与这一档「做到的收敛」
        //    必须能分开看，否则就是虚报。
        foreach ((string markBase, VariantCarrier markCarrier, VariantDimension markDim,
                  string mechanism, bool markEmitted, int markPatches) in emittedMarks)
            report.Normalization.MarkEmitted(markBase, markCarrier, markDim, mechanism, markEmitted, markPatches);
        report.TilesheetEmission = tilesheetAudit;
        report.AppearanceEmission = appearanceAudit;

        // 产出树自检：写出去的 Condition 有没有落在**游戏不读**的位置。
        // 这类错只会被静默忽略、实机很难碰到 —— 拿原版树对照路径形状来查。**只报告，不改产物**。
        Phase("重判+重算报告");
        // 这几道自检都是「扫整棵产出树并解析」，代价与树大小成正比、与这一包改了多少无关 ——
        // 靠产出根**外面**那份 `.audit-cache.json`（`CacheDir`）把「文件没变就不重算」接住
        //（链式跑语料时每个包是一个独立进程，进程内的解析缓存帮不到它）。
        // 另一道自检：原版是数组的字段有没有被写成对象（`Fish` 那一类结构性退化）。
        // ★ 落盘点：所有补丁都已应用完（改动都在内存里），这里一次性写出去。
        //   **必须在下面几道审计之前** —— 它们扫的是 `output.OutputRoot` 上的**文件**。
        //   先写两个内存模型（地图 / 图片），再让 `OutputTree` 把剩下的文本/二进制/拷贝落盘。
        //   `OutputTree` 放最后：缓存层接管过的路径已被 `DropPending` 撤掉，不会再被拷回去。
        // 「先汇总、再执行」：落盘前先报清这一轮要动哪些文件、各是什么形态。
        Console.WriteLine("本次落盘汇总：" + string.Join("、",
            output.PlannedWrites.GroupBy(kv => kv.Value)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key} {g.Count()}"))
            + $"（共 {output.PlannedWrites.Count} 个文件）");

        // 婚姻可用性最小修复：`CanBeRomanced` 字符串→布尔；可婚 NPC 缺 `NPCGiftTastes` 时补中性条目
        // （否则花束/吊坠送不出去）。只补硬性断点，不发明婚房/对话。
        // 本地化 Data 是整份替换：把 base 有、locale 没有的顶层键补回来。
        // 这一步必须在婚姻检查之前，Characters/NPCGiftTastes/Weddings 才能看到完整行。
        var localizedNotes = new List<string>();
        int localizedAdded = LocalizedDataMirror.Mirror(output, localizedNotes);
        Phase("本地化镜像");
        if (localizedAdded > 0)
            Console.WriteLine($"本地化 Data 补键：{localizedAdded} 条");
        conversionNotes.AddRange(localizedNotes);

        MarriageReadinessFix(output);
        Phase("婚姻修复");

        // CP 的内部资产键 `SMAPI/<uid>/...` 在纯 XNB 里没有内容管理器；NPC 立绘/精灵
        // 落到普通 `Characters|Portraits/<NPC>/...` 并改写 §Data/Characters§ 的全部语言变体。
        int npcAssets = NpcAssetMaterializer.Materialize(output, output.PlannedWrites.Keys);
        Phase("NPC资产物化");
        if (npcAssets > 0)
            Console.WriteLine($"NPC 立绘/精灵物化：{npcAssets} 个 Data/Characters 变体");

        // 纯 XNB 不存在的 TriggerAction 与悬空 GOTO：修到“能执行/不报错”，不发明行为。
        int triggerActions = TriggerActionSanitizer.Sanitize(output);
        Phase("Trigger清理");
        if (triggerActions > 0)
            Console.WriteLine($"TriggerActions 清理：{triggerActions} 条无效动作/触发器/重复 ID");
        int schedules = ScheduleRepair.Repair(output);
        Phase("日程修复");
        if (schedules > 0)
            Console.WriteLine($"日程悬空 GOTO 修复：{schedules} 个文件");
        int locationTypes = LocationTypeSanitizer.Sanitize(output, output.PlannedWrites.Keys);
        Phase("地点类型清理");
        if (locationTypes > 0)
            Console.WriteLine($"Data/Locations 自定义类型退化：{locationTypes} 处（DLL 类型 ⇒ GameLocation）");

        List<string> flushedMaps = mapCache.Flush();
        Phase("地图Flush");
        var capeRemainingNotes = new List<string>();
        // 只有本包确实带了 Cape 动作才做全树补扫：否则每个包都读一遍所有地图，链式转换会被拖垮。
        // 前面的包写下的动作已经在它自己那轮由 MapCache 处理；补扫只是兜住“本轮没弄脏”的旧图。
        int capeRemaining = mapCache.CapeOrbActions > 0
            ? CapeOrbBridge.RewriteRemaining(output, capeRemainingNotes)
            : 0;
        List<string> flushedImages = imageCache.Flush();
        Phase("图片Flush");
        output.Flush();
        Phase("OutputFlush");
        // 图片/地图已经全部写出，内存模型可以释放给下一包复用；后续自检只读磁盘。
        imageCache.Release();
        mapCache.Release();
        Phase("落盘");
        if (flushedMaps.Count > 0)
            Console.WriteLine($"地图写回：{flushedMaps.Count} 张（内存模型，每张只写一次；"
                              + $"源地图解码 {mapCache.SourceDecodes} 次、命中缓存 {mapCache.SourceHits} 次）");
        if (mapCache.CapeOrbActions > 0)
        {
            string line = $"Cape 宝珠事件桥：{mapCache.CapeOrbActions} 个图块动作（PlayEvent + HasItem）";
            Console.WriteLine(line);
            conversionNotes.Add(line);
        }
        if (capeRemaining > 0)
        {
            string line = $"Cape 宝珠事件桥（既有地图补扫）：{capeRemaining} 个图块动作";
            Console.WriteLine(line);
            conversionNotes.Add(line);
        }
        foreach (string note in mapCache.CapeOrbNotes) conversionNotes.Add(note);
        foreach (string note in capeRemainingNotes) conversionNotes.Add(note);
        if (flushedImages.Count > 0)
            Console.WriteLine($"图片写回：{flushedImages.Count} 张（内存模型，每张只写一次；"
                              + $"源图解码 {imageCache.SourceDecodes} 次、命中缓存 {imageCache.SourceHits} 次）");
        if (mapCache.LayersRenamed > 0)
        {
            string line = $"自定义图层名归一：{mapCache.LayersRenamed} 个图层"
                          + "（`Back+1`/`Always Front`/`Draw=…` → `锚点+整数`，否则原版整层不画）";
            Console.WriteLine(line);
            conversionNotes.Add(line);
        }
        if (mapCache.TilesheetPathsRepaired > 0)
        {
            string line = $"地图图集引用修正：{mapCache.TilesheetPathsRepaired} 处"
                          + "（引用解析后不存在，已改指产出树里真实存在的图）";
            Console.WriteLine(line);
            conversionNotes.Add(line);
        }
        if (mapCache.I18nTranslated > 0)
        {
            string line = $"地图属性 i18n 展开：{mapCache.I18nTranslated} 处（按中文/默认译文冻结）";
            Console.WriteLine(line);
            conversionNotes.Add(line);
        }
        if (mapCache.FlipBaked > 0)
        {
            string line = $"翻转/旋转烘焙：{mapCache.FlipBaked} 个图块"
                          + "（在对应图集末尾追加变换后的格子，地图的 TileIndex 改指新格）";
            Console.WriteLine(line);
            conversionNotes.Add(line);
        }
        foreach (string note in mapCache.FlipNotes.Distinct(StringComparer.Ordinal))
        {
            Console.WriteLine("    · 翻转烘焙：" + note);
            conversionNotes.Add("翻转烘焙：" + note);
        }

        // ⚠ 这几道自检扫的都是 `output.OutputRoot` 上的**文件**，必须在 `Flush()` **之后**。
        //    `ConditionPlacementAudit` 原先排在 `Flush()` 之前 —— 延迟写之前「写是立即的」，
        //    排在哪儿都一样；改成延迟写之后它扫的就成了**上一包的状态**。挪到这里才对得上。
        //    这一段在**单趟**与第二趟（edits）跑；两趟链的第 1 趟只装 Load，
        //    全树自检等到第 2 趟看最终树就行 —— 每趟都扫一遍纯属把总时长翻倍。
        if (options.Pass != "loads"
            && Environment.GetEnvironmentVariable("SDVCP_SKIP_AUDITS") != "1")
        {
        // 两道自检都要把「产出 + 原版」两份 JSON 解析一遍（合起来 4 遍全树解析）——
        // 共享一个解析缓存，同一份文件只解析一次。这段里没人再写文件，缓存不会串。
        var auditParseCache = new SdvCpTool.Core.Reporting.AuditParseCache();
        // 跨包的那一层：每个包是独立进程，进程内的解析缓存帮不到它。
        var writtenJson = SdvCpTool.Core.Reporting.AuditCache.JsonWrittenBy(output);
        var auditCache = SdvCpTool.Core.Reporting.AuditCache.Load(
            Path.Combine(CacheDir(output.OutputRoot), ".audit-cache.json"),
            output.OutputRoot, output.InputRoot);

        report.ConditionAudit = ConditionPlacementAudit.Run(output, ConditionLanding.ConditionFieldNames,
            auditParseCache, auditCache, writtenJson);
        Phase("ConditionAudit");

        report.ShapeAudit = OutputShapeAudit.Run(output, parseCache: auditParseCache,
            auditCache: auditCache);
        Phase("ShapeAudit");

        // 地点类自检：`CreateOnLoad.Type` 指向 **mod 的程序集** ⇒ 纯 XNB 里没有那个程序集，
        // 游戏建地点时直接抛 `Invalid type for location` ⇒ 地点根本不存在（它的地图/传送/内容全到不了）。
        // ⚠ **只报不改**：删掉 `Type` 能让地点以普通 `GameLocation` 建起来，但那个类承载的行为一起消失
        //   （实测 Cape Stardew 的岩浆洞逻辑）—— 取舍留给装机的人。**不需要基准树**，逐包都跑。
        report.LocationClassAudit = LocationClassAudit.Run(output.OutputRoot);

        // 资产**类型**自检：产出与原版的 `.config` 声明的扩展名对不上 ⇒ 打包后游戏会拿错类型
        // （实测 `Maps/Coop`：树里贴图与地图同时存在、描述符说 `.png` ⇒ 进鸡舍必炸）。
        // ⚠ 这项自检**每包 1.9 s**（一万多份 `.config` × JSON 解析两遍，与那包写了多少无关）⇒
        //   必须走增量缓存；「这一轮写过哪些」由落盘账本说了算（与其它自检同一口径）。
        report.AssetTypeAudit = AssetTypeAudit.Run(output, auditCache,
            output.PlannedWrites.Keys.Where(k => k.EndsWith(".config", StringComparison.OrdinalIgnoreCase))
                .ToArray());

        // 条件键是**包内令牌**的补丁：这一档我们不认（CP 认）⇒ 那些条件一律当作不成立。
        // 报告出来，别让它只是"某条补丁安静地没生效"。
        report.TokenConditionAudit = TokenConditionAudit.Run(pack);
        Phase("AssetTypeAudit");

        // 再一道：**模型驱动**的容器形状校验 —— 数据来源是游戏自己（DataLoader.cs + GameData 模型类），
        // 比拿原版数据反推精确得多。
        report.GameDataAudit = SdvCpTool.Core.Validating.GameDataValidator.Run(output.OutputRoot,
            auditParseCache, auditCache, writtenJson);
        Phase("GameDataAudit");

        // 再一道：按**游戏自己的贴图寻址公式**核对「条目指到的贴图取不取得到」。
        // 前两道都只看 JSON 本身，这一道看 JSON 指向的资源 —— 形状全对、贴图却缺或索引越界，
        // 正是实机最难碰到的那类（只在特定物品被绘制时才出问题）。
        report.SpriteAudit = SdvCpTool.Core.Validating.SpriteSheetValidator.Run(output.OutputRoot, auditParseCache);
        report.TokenAudit = SdvCpTool.Core.Validating.UnexpandedTokenAudit.Run(output.OutputRoot,
            auditParseCache, auditCache, writtenJson);
        Phase("Sprite+TokenAudit");

        // 再一道：**同一模型对象里仅大小写不同的键** —— 反序列化大小写不敏感，
        // 两键会撞到同一属性，该资产**打不出 XNB**（实测 SVE 的 Data/WorldMap 就是，
        // 回包时才暴露，前四道自检结构上都抓不到）。
        report.KeyCaseAudit = SdvCpTool.Core.Validating.KeyCaseConflictAudit.Run(output.OutputRoot,
            auditParseCache, auditCache, writtenJson);
        Phase("KeyCaseAudit");
        auditCache.Save(Path.Combine(CacheDir(output.OutputRoot), ".audit-cache.json"),
            output.OutputRoot, output.InputRoot);
        if (Environment.GetEnvironmentVariable("SDVCP_PROFILE") != null)
            Console.Error.WriteLine($"[prof] 自检缓存：复用 {auditCache.Reused} 份、重算 {auditCache.Recomputed} 份");
        }
        SdvCpTool.Core.Diagnostics.Prof.Dump();

        Console.WriteLine(
            $"已写入补丁：{written} 条（写入失败 {failed} 条），新建文件 {output.CreatedFiles.Count} 个");

        // 收尾：把新建的 .json 资产补进输出树的 ContentHashes.json。
        int addedKeys = MaintainContentHashes(output, options.ContentHashes);
        Console.WriteLine($"ContentHashes.json 补入 {addedKeys} 个新 key");
    }

    // 解析警告 + 非 CP 框架包的导入说明（FTM：哪些落了、哪些落不了）——
    // 原先只打 stdout，报告里没有；而它们说的正是"哪些内容拿不到"，得让只看报告的人也看得到。
    report.Notes.Clear();
    foreach (string w in pack.Warnings) report.Notes.Add($"解析警告：{w}");
    report.Notes.AddRange(pack.FtmNotes);
    report.Notes.AddRange(enumerated.Notes);
    report.Notes.AddRange(conversionNotes);

    ReportWriter.WriteJson(report, options.Report!);
    Console.WriteLine(ReportWriter.RenderSummary(report));
    Console.WriteLine($"报告已写入：{Path.GetFullPath(options.Report!)}");

    if (pack.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"内容包解析警告（{pack.Warnings.Count}）：");
        foreach (string w in pack.Warnings.Take(20)) Console.WriteLine($"  {w}");
    }

    // 非 CP 框架包（FTM）翻译的交代：哪些落了、哪些落不了。**必须打出来** ——
    // 用户得知道"这个方案里哪些内容拿不到"，而不是自己去猜地图上为什么没怪。
    if (pack.FtmNotes.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"非 CP 框架包（FTM）翻译说明（{pack.FtmNotes.Count}）：");
        foreach (string n in pack.FtmNotes) Console.WriteLine($"  · {n}");
    }

    // 判定不了 Include 的条件 = 明确失败，不能让产出树假装完整而静默返回成功。
    return report.BlockedCount == 0 && unresolvedIncludes == 0 ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"致命错误：{ex.Message}");
    if (Environment.GetEnvironmentVariable("SDVCP_STACK") != null)
        Console.Error.WriteLine(ex.ToString());   // 第三方包崩了要能定位，默认关：SDVCP_STACK=1
    return 2;
}


/// <summary>把原因里的具体令牌名换成占位符，好让同类失败聚合到一组。</summary>
/// <summary>
/// 自检增量缓存放哪：产出树**外面那一层**（`--out` 的父目录）。
/// <para>
/// ⚠ 别放进树里：那棵树是要交给打包器的产出物，混进一个开发期的缓存文件既脏又会被
/// 「产出树逐字节对比」当成差异（实测：`只在 B：1  .audit-cache.json`）。
/// 放父目录也正好是「一轮语料一个缓存」的位置（`tools/convert-mods.py` 的产出结构就是这样）。
/// </para>
/// </summary>
static string CacheDir(string outputRoot)
{
    string full = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return Path.GetDirectoryName(full) ?? full;
}

static string GroupReason(string reason) =>
    Regex.Replace(reason, "`[^`]*`", "`<X>`");

/// <summary>CP 的 Target 支持逗号分隔多值，逐个拆成资产名（统一用正斜杠）。</summary>
static IEnumerable<string> TargetNames(string? target) =>
    SdvCpTool.Core.Cp.AssetNameList.Split(target)     // 令牌里的逗号不是分隔符（见 AssetNameList）
        .Select(t => t.Replace('\\', '/').Trim('/'))
        .Where(t => t.Length > 0);

/// <summary>补丁的 Target 是否指向某个已知资产名（Target 可能是逗号分隔的多资产）。</summary>
static bool SameAsset(string? target, string asset) =>
    TargetNames(target).Any(t => string.Equals(t, asset, StringComparison.OrdinalIgnoreCase));

/// <summary>
/// 求值每个带 <c>When</c> 的 Include 并决定是否展开。返回<b>无法求值</b>的 Include 数。
/// <para>
/// 三种结局严格区分：真 → 展开；假 → 跳过（该 Include 不适用）；判不了 → <b>不展开并报失败</b>
/// （返回计数让调用方以非零退出码暴露，绝不默认猜真或猜假）。
/// </para>
/// </summary>
static int ResolveIncludes(string? modsDirectory, string? cachePath, ContentPack pack)
{
    Func<JObject, IncludeDecision> evaluate;
    DynamicTokenResolver? includeTokens = null;
    if (modsDirectory == null)
    {
        // 没有模组索引就没有任何依据，只能全部判为"不知道"。
        evaluate = _ => IncludeDecision.Unknown("未提供 --mods，无法求值 Include 上的 When");
    }
    else
    {
        InstalledModIndex mods = ModIndexProvider.Get(modsDirectory, cachePath);
        var baker = new Baker(mods, pack.RootPath, pack.UniqueId);
        // ★ **Include 条件里也可能写包内 `DynamicTokens`**（实测 `Lurking in the Dark`：
        //   `When: { IsDitRInstalled: false }`，而 `IsDitRInstalled` 的值是
        //   `{{HasMod: |contains=…}}`）。不接解析器 ⇒ 判不了 ⇒ Include 被「内联」，
        //   内联后的 `Load` 又因"资产创建不能带条件"被整条跳过 ⇒ SenS/Hector 连底图都没有
        //   （实机 `Failed to spawn NPC`）。`RunBake` 早就接了同一份解析器，这里以前漏了。
        includeTokens = DynamicTokenResolver.Load(pack.RootPath, when =>
        {
            WhenNode? node = WhenParser.Parse(when);
            if (node == null)
                return new DynamicTokenResolver.WhenResult(null, "DynamicToken 的 When 为空，无法求值");
            Baker.Result r = baker.Evaluate(node);
            return new DynamicTokenResolver.WhenResult(r.Success ? r.Value : (bool?)null, r.FailureReason);
        }, (name, args) => RenderExternalToken(name, args, mods));
        baker.DynamicTokens = includeTokens;
        evaluate = when =>
        {
            WhenNode? node = WhenParser.Parse(when);
            if (node == null) return IncludeDecision.Unknown("Include 的 When 为空，无法求值");

            Baker.Result r = baker.Evaluate(node);
            return r.Success
                ? (r.Value ? IncludeDecision.Yes(r.Note) : IncludeDecision.No(r.Note))
                : IncludeDecision.Unknown(r.FailureReason ?? "烘焙求值失败");
        };
    }

    // ★ 路径含令牌的 Include 现在也会走到这里（为了等令牌求值器）；空 `When` 在
    //   `ContentPack.Flatten` 里直接按无条件展开，不再交给求值器判“不知道”。
    IReadOnlyList<IncludeResolution> resolutions = pack.ResolveIncludes(evaluate, path =>
    {
        if (includeTokens == null) return null;
        DynamicTokenResolver.Expansion expanded = includeTokens.Expand(path);
        return expanded.Unresolved.Count == 0 ? expanded.Text : null;
    });
    int unresolved = 0;

    foreach (IncludeResolution res in resolutions)
    {
        string name = $"{Path.GetFileName(res.SourceFile)}#{res.Index}" +
                      (res.LogName != null ? $" [{res.LogName}]" : "");

        switch (res.Outcome)
        {
            case IncludeOutcome.Expanded:
                Console.WriteLine($"Include 展开：{name}，When {res.When?.ToString(Formatting.None)}" +
                                  (res.Reason != null ? $"（{res.Reason}）" : ""));
                break;

            case IncludeOutcome.Skipped:
                Console.WriteLine($"Include 跳过（条件为假）：{name}，When {res.When?.ToString(Formatting.None)}" +
                                  (res.Reason != null ? $"（{res.Reason}）" : ""));
                break;

            case IncludeOutcome.Unresolved:
                unresolved++;
                string message =
                    $"Include 求值失败、未展开（可能因此丢补丁）：{name}，FromFile={res.FromFile}，" +
                    $"When={res.When?.ToString(Formatting.None)}，原因：{res.Reason}";
                Console.Error.WriteLine(message);
                pack.Warnings.Add(message);
                break;

            // ★ 判不了但**没丢**：条件与子补丁相与后内联，让条件走正常的翻译通道。
            //   所以这一档**不算失败**（退出码不该因为它变 1）—— 那条件能不能落地，
            //   由每条子补丁自己的判定说了算（带不动的会各自被拒并说明）。
            case IncludeOutcome.Inlined:
                Console.WriteLine(
                    $"Include 条件判不了，已内联并逐条带上该条件：{name}，FromFile={res.FromFile}，" +
                    $"When={res.When?.ToString(Formatting.None)}，原因：{res.Reason}");
                break;
        }
    }

    if (resolutions.Count > 0)
    {
        Console.WriteLine(
            $"Include 条件求值：展开 {resolutions.Count(r => r.Outcome == IncludeOutcome.Expanded)}，" +
            $"跳过 {resolutions.Count(r => r.Outcome == IncludeOutcome.Skipped)}，" +
            $"判不了但内联 {resolutions.Count(r => r.Outcome == IncludeOutcome.Inlined)}，" +
            $"求值失败 {unresolved}");
    }

    return unresolved;
}

/// <summary>
/// 构建内容包 <c>DynamicTokens</c> 的展开器。提供 <c>--mods</c> 时用 <c>Baker</c> 求值令牌块上的
/// <c>When</c>；未提供时带 <c>When</c> 的块一律判不了（不猜）。
/// </summary>
static DynamicTokenResolver BuildTokenResolver(
    string? modsDirectory, string? cachePath, ContentPack pack)
{
    if (modsDirectory == null)
        return DynamicTokenResolver.Load(pack.RootPath, null);

    InstalledModIndex mods = ModIndexProvider.Get(modsDirectory, cachePath);
    var baker = new Baker(mods, pack.RootPath, pack.UniqueId);
    DynamicTokenResolver resolver = DynamicTokenResolver.Load(pack.RootPath, when =>
    {
        WhenNode? node = WhenParser.Parse(when);
        if (node == null)
            return new DynamicTokenResolver.WhenResult(null, "DynamicToken 的 When 为空，无法求值");

        Baker.Result r = baker.Evaluate(node);
        return new DynamicTokenResolver.WhenResult(r.Success ? r.Value : (bool?)null, r.FailureReason);
    }, (name, args) => RenderExternalToken(name, args, mods));
    // 反向接上：条件的**求值**侧也要能问包内 DynamicTokens（与上面的 When 求值互为递归，解析器自带环保护）。
    baker.DynamicTokens = resolver;
    return resolver;
}

/// <summary>
/// 本包之外、但**离线可定**的令牌 —— 值路径上的取值（条件路径由 <c>Baker</c> 负责）。
/// <para>
/// 目前只有 <c>HasMod</c>。依据 CP：<c>TokenManager.cs:220</c> 把它注册成
/// <c>ImmutableValueProvider("HasMod", installedMods)</c>，值是**已装模组的 id 集合**；
/// 带 <c>|contains=X</c> 时 <c>Token.GetValues</c> 把值集折成单个 <c>true</c>/<c>false</c>。
/// 实测用途：SVE 在 <c>Data/WorldMap</c>/<c>Data/Characters</c> 的**值**里写
/// <c>"Condition": "!{{HasMod |contains=flashshifter.GrandpasFarm}}"</c> —— 那是游戏数据里的 GSQ，
/// 把 <c>{{…}}</c> 代成字面 <c>true</c>/<c>false</c> 就能照原样写出去。
/// </para>
/// <para>认不出的形态一律返回 <c>null</c>（原样保留，由那道闸拒绝）—— 不猜。</para>
/// </summary>
static void MarriageReadinessFix(OutputTree output)
{
    bool relevant = output.PlannedWrites.Keys
        .Select(x => x.Replace('\\', '/'))
        .Any(x => x.StartsWith("Data/Characters", StringComparison.OrdinalIgnoreCase)
                  || x.StartsWith("Data/Weddings", StringComparison.OrdinalIgnoreCase)
                  || x.StartsWith("Data/NPCGiftTastes", StringComparison.OrdinalIgnoreCase));
    if (!relevant) return;

    string? charText = output.ReadTextOrNull("Data/Characters.json");
    if (charText == null) return;

    JObject characters;
    try { characters = JObject.Parse(charText); }
    catch { return; }

    int flagFixes = MarriageReadiness.NormalizeBooleanFlags(characters);
    if (flagFixes > 0)
    {
        if (!output.TryStageJson("Data/Characters.json", characters, out string? error))
            Console.WriteLine($"⚠ 婚姻修复：Data/Characters 暂存失败：{error}");
        else
            Console.WriteLine($"婚姻修复：`CanBeRomanced` 字符串→布尔 {flagFixes} 条");
    }

    // 本地化 `Data/Characters.<locale>` 是整份替换：补 missing 行并同步婚姻结构字段。
    int mirrored = 0;
    foreach (string rel in output.InputFiles.Concat(output.PlannedWrites.Keys)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .Where(f => f.StartsWith("Data/Characters", StringComparison.OrdinalIgnoreCase)
                             && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(f, "Data/Characters.json", StringComparison.OrdinalIgnoreCase))
                 .ToList())
    {
        string? text = output.ReadTextOrNull(rel);
        if (text == null) continue;
        try
        {
            var variant = JObject.Parse(text);
            int rows = MarriageReadiness.MirrorLocalizedCharacters(characters, variant);
            int flags = MarriageReadiness.NormalizeBooleanFlags(variant);
            if (rows > 0 || flags > 0)
                output.TryStageJson(rel, variant, out _);
            mirrored += rows;
        }
        catch { /* 坏变体不阻断 */ }
    }
    if (mirrored > 0)
        Console.WriteLine($"婚姻修复：本地化 Data/Characters 补齐/同步 {mirrored} 条");

    // 婚礼数据同样是整份替换；把 base 的 Attendees / EventScript 缺键补进本地化。
    int weddingMirrored = 0;
    string? weddingsText = output.ReadTextOrNull("Data/Weddings.json");
    if (weddingsText != null)
    {
        try
        {
            var baseWeddings = JObject.Parse(weddingsText);
            foreach (string rel in output.InputFiles.Concat(output.PlannedWrites.Keys)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Where(f => f.StartsWith("Data/Weddings", StringComparison.OrdinalIgnoreCase)
                                     && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                                     && !string.Equals(f, "Data/Weddings.json", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                string? text = output.ReadTextOrNull(rel);
                if (text == null) continue;
                var localized = JObject.Parse(text);
                int n = MarriageReadiness.MirrorLocalizedWeddings(baseWeddings, localized);
                if (n == 0) continue;
                output.TryStageJson(rel, localized, out _);
                weddingMirrored += n;
            }
        }
        catch { /* 坏婚礼数据不阻断 */ }
    }
    if (weddingMirrored > 0)
        Console.WriteLine($"婚姻修复：本地化 Data/Weddings 补齐 {weddingMirrored} 条");

    int giftFixes = 0;
    foreach (string rel in output.InputFiles.Concat(output.PlannedWrites.Keys)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .Where(f => f.StartsWith("Data/NPCGiftTastes", StringComparison.OrdinalIgnoreCase)
                             && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                 .ToList())
    {
        string? text = output.ReadTextOrNull(rel);
        if (text == null) continue;
        try
        {
            var gifts = JObject.Parse(text);
            int added = MarriageReadiness.AddMissingGiftTastes(characters, gifts);
            if (added == 0) continue;
            giftFixes += added;
            output.TryStageJson(rel, gifts, out _);
        }
        catch { /* 坏变体不阻断 */ }
    }
    if (giftFixes > 0)
        Console.WriteLine($"婚姻修复：可婚 NPC 补中性 `Data/NPCGiftTastes` 条目 {giftFixes} 条");

    // 只报不改：这些 NPC 原 mod 也没给婚房，纯 XNB 更造不出来；娶了会落进 Abigail 的默认房间。
    List<string> noRoom = characters.Properties()
        .Where(p => p.Value is JObject e
                    && e["CanBeRomanced"] is JValue { Type: JTokenType.Boolean } f && f.Value<bool>()
                    && (e["SpouseRoom"] is not JObject room
                        || room["MapAsset"] is not JValue { Type: JTokenType.String } map
                        || string.IsNullOrWhiteSpace(map.Value<string>())))
        .Select(p => p.Name).ToList();
    if (noRoom.Count > 0)
        Console.WriteLine($"婚姻提示：可婚但无 SpouseRoom（婚后落进默认房间）：{string.Join(", ", noRoom)}");

    MarriageDialogueAliasFix(output, characters);
}

/// <summary>
/// 婚后对话写错了资产名时补一份（语料：East Scarp 把 JadeMalic 的 79 条写进 `MarriageDialogueJade`）。
/// 游戏按 NPC 名加载，目标文件为空 ⇒ 婚后只会说通用台词。
/// </summary>
static void MarriageDialogueAliasFix(OutputTree output, JObject characters)
{
    const string prefix = "Characters/Dialogue/MarriageDialogue";
    List<string> rels = output.InputFiles.Concat(output.PlannedWrites.Keys)
        .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (string rel in rels)
    {
        string? text = output.ReadTextOrNull(rel);
        if (text == null) continue;
        try
        {
            var data = JObject.Parse(text);
            string name = rel[prefix.Length..^".json".Length];
            if (name.Contains('.')) continue;   // 本地化变体不参与别名
            counts[name] = data.Count;
        }
        catch { /* 坏文件不参与别名 */ }
    }

    List<string> npcNames = characters.Properties().Select(p => p.Name).ToList();
    foreach (JProperty npc in characters.Properties())
    {
        if (npc.Value is not JObject entry) continue;
        if (entry["CanBeRomanced"] is not JValue { Type: JTokenType.Boolean } flag || !flag.Value<bool>())
            continue;
        string? alias = MarriageReadiness.FindDialogueAlias(npc.Name, npcNames, counts);
        if (alias == null) continue;

        string? sourceText = output.ReadTextOrNull(prefix + alias + ".json");
        if (sourceText == null) continue;
        JObject dialogue;
        try { dialogue = JObject.Parse(sourceText); } catch { continue; }
        if (output.TryStageJson(prefix + npc.Name + ".json", dialogue, out string? error))
            Console.WriteLine($"婚姻修复：`{npc.Name}` 的婚后对话为空，复制自 `MarriageDialogue{alias}`（{dialogue.Count} 条）");
        else
            Console.WriteLine($"⚠ 婚姻修复：`{npc.Name}` 婚后对话复制失败：{error}");

        // 语言变体（`.zh-CN.json` 等）也要复制 —— 只复制 base 会让中文玩家看到英文 fallback。
        string sourcePrefix = prefix + alias;
        foreach (string rel in rels)
        {
            if (!rel.StartsWith(sourcePrefix + ".", StringComparison.OrdinalIgnoreCase)) continue;
            string suffix = rel[sourcePrefix.Length..];          // ".zh-CN.json"
            string target = prefix + npc.Name + suffix;
            string? variantText = output.ReadTextOrNull(rel);
            if (variantText == null) continue;
            string? existing = output.ReadTextOrNull(target);
            if (existing != null)
            {
                try { if (JObject.Parse(existing).Count > 0) continue; } catch { /* 坏了就覆盖 */ }
            }
            try
            {
                output.TryStageJson(target, JObject.Parse(variantText), out _);
            }
            catch { continue; }

            // 变体资产还需要自己的 `.config` 描述符（基资产那份不覆盖变体）。
            string sourceConfig = rel[..^".json".Length] + ".config";
            string targetConfig = target[..^".json".Length] + ".config";
            string? configText = output.ReadTextOrNull(sourceConfig);
            if (configText != null)
                output.TryStageLazyText(targetConfig, () => configText, out _);
        }
    }
}

static string? RenderExternalToken(string name, string? args, InstalledModIndex mods)
{
    // `{{<UniqueID>/<configField>}}`：跨模组配置令牌（RSV 的 RSVNPCSAttendFestivals 等）。
    string? configValue = ModConfigTokens.Render(mods, name, args);
    if (configValue != null) return configValue;

    // 代码模组（EntryDll）的命名空间令牌：字段名与 config 不完全同名的少数几个
    // （GiEX 的 ExcludeFishes / ExcludeAnimalProduce / PortraitStyle）。
    string? dllValue = DllConfigTokens.Resolve(name, args, mods);
    if (dllValue != null) return dllValue;

    if (!name.Equals("HasMod", StringComparison.OrdinalIgnoreCase)) return null;
    return HasModValues.Render(mods, args);
}

/// <summary>
/// 通道 C：把可烘焙的补丁离线求值。返回<b>求值为真</b>（应降级为无条件写入）的补丁集合。
/// 未提供 <paramref name="modsDirectory"/> 时不求值，保持 M1 的 NeedsReview 判定。
/// <para>
/// 三种结果严格区分：真 = 转无条件写入；假 = 该补丁不适用（跳过）；求值失败 = 不知道（判失败并说明）。
/// </para>
/// </summary>
static HashSet<PatchVerdict> RunBake(
    string? modsDirectory, string? cachePath, ContentPack pack, List<PatchVerdict> verdicts)
{
    var bakedTrue = new HashSet<PatchVerdict>();
    List<PatchVerdict> bakeable = verdicts.Where(v => v.Channel == Channel.Bake).ToList();
    if (bakeable.Count == 0) return bakedTrue;

    if (modsDirectory == null)
    {
        Console.WriteLine(
            $"通道 C：{bakeable.Count} 条补丁依赖已装模组/包内文件，未提供 --mods 无法烘焙，维持 NeedsReview");
        return bakedTrue;
    }

    InstalledModIndex mods = ModIndexProvider.Get(modsDirectory, cachePath);
    var baker = new Baker(mods, pack.RootPath, pack.UniqueId);
    // 条件里写包内 `DynamicTokens` 的那些，求值要靠解析器（与 `BuildTokenResolver` 同一份实现，
    // 只是这里不需要值路径的渲染器）。**必须接上**，否则判定说"可烘焙"、这里却求不了，白丢补丁。
    baker.DynamicTokens = DynamicTokenResolver.Load(pack.RootPath, when =>
    {
        WhenNode? node = WhenParser.Parse(when);
        if (node == null)
            return new DynamicTokenResolver.WhenResult(null, "DynamicToken 的 When 为空，无法求值");

        Baker.Result r = baker.Evaluate(node);
        return new DynamicTokenResolver.WhenResult(r.Success ? r.Value : (bool?)null, r.FailureReason);
    }, (name, args) => RenderExternalToken(name, args, mods));
    int truthy = 0, falsy = 0, evalFailed = 0, outOfScope = 0;

    foreach (PatchVerdict pv in bakeable)
    {
        // ⚠ 求值前必须和判定期一样展开**字段引用令牌**（`{{FromFile}}`/`{{Target}}` 族）——
        //   两处对同一份数据的不同解释就是这类静默失败的温床（实测 SVE 季节性服装 9 条：
        //   折叠那边展开了、这里没展开，于是烘焙一律报「`{{FromFile}}` 里还有未展开的令牌」）。
        // ★ 而且多目标补丁要**逐目标**展开求值 —— 见 `BakeOverTargets`。
        WhenNode? when = WhenParser.Parse(pv.Patch.When);

        Baker.Result r = when == null
            ? new Baker.Result(false, false, "补丁没有可解析的 When 条件")
            : BakeOverTargets(when, pv.Patch, baker);

        if (!r.Success)
        {
            // 求值失败 = "我不知道" —— 与"求值为假"（该补丁本就不适用）是两回事，判失败并说明。
            pv.Written = false;
            pv.WriteNote = $"烘焙求值失败：{r.FailureReason}";
            evalFailed++;
        }
        else if (!r.Value)
        {
            // 求值为假 = 该补丁整个不适用。跳过，不是写入失败。
            pv.Written = null;
            pv.WriteNote = "烘焙求值为假，该补丁不适用（跳过，不是写入失败）" +
                           (r.Note != null ? $"（{r.Note}）" : "");
            falsy++;
        }
        else if (!WritePolicy.IsWritableAction(pv.Patch.Action))
        {
            // 条件成立，但 EditMap 属于地图层，不在本任务写入范围 ——
            // 降级为无条件并不能让它变得可写，绝不能塞进数据/图片路径。
            pv.Written = null;
            pv.WriteNote = $"通道 C 烘焙求值为真，但 Action {pv.Patch.Action} 的写入不在本任务范围（本任务只写 EditData/Load/EditImage）";
            outOfScope++;
        }
        else
        {
            bakedTrue.Add(pv);
            pv.WriteNote = "通道 C 烘焙求值为真，按无条件写入" +
                           (r.Note != null ? $"（{r.Note}）" : "");
            truthy++;
        }
    }

    Console.WriteLine(
        $"通道 C 烘焙（已装模组 {mods.Count} 个）：求值为真 {truthy} 条（转无条件写入）、" +
        $"求值为假 {falsy} 条（不适用，跳过）、求值失败 {evalFailed} 条" +
        (outOfScope > 0 ? $"、条件为真但动作超出数据层范围 {outOfScope} 条" : ""));

    return bakedTrue;
}

/// <summary>
/// 按补丁的 `Target` **逐个目标**求值，取「至少一个成立」。
/// <para>
/// 为什么：CP 把 `Target` 的逗号列表拆成**互相独立**的补丁，所以 `{{Target}}`/`{{TargetWithoutPath}}`
/// 是**每个目标各取一份**、`HasFile` 也各查一次。按整条补丁展开的话，那些令牌会被换成一整串
/// 逗号名字，拼出来的路径必然不存在。实测 SVE 季节性服装 10 条 `EditImage` 卡在这里。
/// </para>
/// <para>
/// 三个结果的含义保持不变：有一个成立就是**真**；全都不成立才是**假**；
/// 一个都不成立但**有目标求不出来**就返回那个「不知道」（保守，绝不把不知道当假）。
/// </para>
/// <para>⚠ 只影响这**一条补丁**的结论，不改补丁条数、不拆分补丁（拆分已被测过并回滚，见下面的说明）。</para>
/// </summary>
static Baker.Result BakeOverTargets(WhenNode when, ChangePatch patch, Baker baker)
{
    IReadOnlyList<string> targets = SdvCpTool.Core.Cp.AssetNameList.Split(patch.Target);  // 令牌里的逗号不是分隔符
    if (targets.Count <= 1)
        return baker.Evaluate(FieldReferenceTokens.Expand(when, patch));

    Baker.Result? unknown = null;
    foreach (string target in targets)
    {
        Baker.Result r = baker.Evaluate(FieldReferenceTokens.Expand(when, patch, target));
        if (!r.Success) { unknown ??= r; continue; }
        if (r.Value) return r;
    }
    return unknown ?? new Baker.Result(true, false);
}

/// <summary>
/// 把 <c>Data/Locations</c> 补丁的 `TargetField` 首段（地点名）归一成树里已有键的写法。
/// 只做**忽略大小写唯一匹配**；没有匹配或多个不同写法时不猜。
/// </summary>
static int NormalizeLocationTargetFields(IEnumerable<ChangePatch> patches, ResourceTree tree)
{
    string? path = tree.Resolve("Data/Locations");
    if (path == null) return 0;

    try { return LocationKeyNormalizer.Apply(patches, JObject.Parse(File.ReadAllText(path))); }
    catch { return 0; }
}

/// <summary>
/// 包依赖了**纯 XNB 里不会运行**的代码框架时，去掉它 `Entries` 里的空值删除（保留原版条目）。
/// <para>
/// CP 里 <c>Entries: { "301": null }</c> 是删除条目。内容包常用它给框架的替代内容腾位置
///（实测 Cornucopia 的葡萄/啤酒花/咖啡/蓝莓/仙人掌：删掉原版作物，等 CustomBush 在运行时补回，
///  而 CustomBush 是 DLL，纯 XNB 装机里根本不会加载）⇒ 删了就是净损失。
/// </para>
/// <para>
/// 只处理**顶层 <c>Entries</c> 的直接空值**；嵌套字段删除（<c>Fields</c> 里的 null）不动 ——
/// 那是改条目字段，不是删条目。
/// </para>
/// </summary>
static int StripNullDeletionsForInertDependencies(ContentPack pack, InstalledModIndex mods)
{
    bool dependsOnInert = pack.DependencyIds.Any(
        id => mods.Contains(id) && !mods.ContainsRuntime(id));
    if (!dependsOnInert) return 0;

    int stripped = 0;
    foreach (ChangePatch patch in pack.Patches)
    {
        if (!patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)) continue;
        if (patch.Raw["Entries"] is not JObject entries) continue;
        foreach (JProperty property in entries.Properties().Where(p => p.Value.Type == JTokenType.Null).ToList())
        {
            entries.Remove(property.Name);
            stripped++;
        }
    }
    return stripped;
}

/// <summary>
/// 把新建资产的 key 补进<b>输出树</b>根目录的 <c>ContentHashes.json</c>。
/// 解包树不含该文件（解包器只复制 xnb），所以输出树里若没有，就从游戏原文件补一份再补 key；
/// 原树与游戏原文件始终只读。返回新补入的 key 数。
/// </summary>
static int MaintainContentHashes(OutputTree output, string? vanillaHashesPath)
{
    const string rel = "ContentHashes.json";
    string full = output.ToOutputPath(rel);

    if (!File.Exists(full))
    {
        if (vanillaHashesPath == null || !File.Exists(vanillaHashesPath)) return 0;
        File.Copy(vanillaHashesPath, full);
    }

    JObject hashes = JsoncReader.ParseFile(full);
    int before = hashes.Count;
    ContentHashesWriter.AddKeys(hashes, output.CreatedFiles);
    File.WriteAllText(full, hashes.ToString(Formatting.None));
    return hashes.Count - before;
}

/// <summary>
/// 事件键前置条件改写（写入选道用）。判定阶段走的是**同一个** <see cref="EventKeyRewriter.TryRewrite"/>，
/// 且 GSQ 同样取自 <c>FoldedWhen</c> —— 绝不重新解析原始 When（否则判定与写入又各解释一遍）。
/// </summary>
static JObject? TryEventKeyRewrite(SdvCpTool.Core.Judging.PatchVerdict pv, SdvCpTool.Core.Workspace.ResourceTree tree)
{
    if (pv.Patch.When == null) return null;
    SdvCpTool.Core.Conditions.GsqTranslation gsq = SdvCpTool.Core.Conditions.GsqTranslator.Translate(
        pv.FoldedWhen ?? SdvCpTool.Core.Conditions.WhenParser.Parse(pv.Patch.When));
    if (!gsq.Success) return null;
    return EventKeyRewriter.TryRewrite(
        pv.Patch, gsq.Gsq, EventKeyRewriter.LoadExistingEventIds(tree, pv.Patch.Target));
}
