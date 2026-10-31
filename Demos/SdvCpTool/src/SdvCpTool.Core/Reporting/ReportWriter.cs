using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Judging;

namespace SdvCpTool.Core.Reporting;

public static class ReportWriter
{
    private static readonly Verdict[] BlockedVerdicts =
    {
        Verdict.NoLandingPoint,
        Verdict.NonTranslatableCondition,
        Verdict.NeedsRuntime,
        Verdict.LocalEdit,
        Verdict.TargetContainsTokens,
        Verdict.TargetMissing,
        Verdict.UnsupportedAction
    };

    public static void WriteJson(AnalysisReport report, string path)
    {
        var patches = new JArray();
        foreach (PatchVerdict pv in report.Patches)
        {
            patches.Add(new JObject
            {
                ["source"] = RelativeSource(pv.Patch.SourceFile, report.Pack),
                ["index"] = pv.Patch.Index,
                ["action"] = pv.Patch.Action,
                ["target"] = pv.Patch.Target,
                ["displayName"] = pv.Patch.DisplayName,
                // ⚠ 被**集合发射**覆盖的补丁，`Verdict` 是**发射前**的判定，直接写出去会自相矛盾：
                //   实测 220 条的 `verdict` 写着 `NoLandingPoint`、`reason` 写着
                //   「本条没被任何可用机制归入」，而**它们其实被四季发射器产出过**。
                //   汇总数本来是对的（`blocked` 已扣掉 `groupEmitted`），错的只有逐条记录。
                //   ⇒ 这里如实改写：判成 `Emitted`，理由前置一句说明。
                ["verdict"] = pv.GroupEmitted ? "Emitted" : pv.Verdict.ToString(),
                ["reason"] = pv.GroupEmitted
                    ? $"★ 已由集合发射产出（不是逐条写入）。原判定 `{pv.Verdict}`：{pv.Reason}"
                    : pv.Reason,
                ["suggestedChannel"] = pv.SuggestedChannel,
                ["channel"] = pv.Channel.ToString(),
                // 仅 DayNightTiles 有意义：true = 白天显示，false = 夜晚/雨天显示
                ["dayNightVisibleAtDay"] = pv.Channel == Channel.DayNightTiles
                    ? new JValue(pv.DayNightVisibleAtDay)
                    : JValue.CreateNull(),
                // ---- 下面三个是给「同一目的有几条路、各要改多少」用的（2026-10-10 加）----
                // 落点：选道 A 时 GSQ 写进哪个字段/键。与 channel 一起才说明"这条路具体怎么走"。
                ["landing"] = pv.Landing?.ToString(),
                // 代价：这条补丁碰多少个条目。Entries 用 Count，其余（整块替换、单字段）记 1。
                ["entryCount"] = CountTargets(pv.Patch.Raw),
                // 原始 When（判定用的是折叠后的，但原始那份才是"包作者写的条件"）
                ["when"] = pv.Patch.Raw["When"]?.DeepClone(),
                // 判定与写入是两回事：null = 本任务未尝试写入，false = 写入失败
                ["written"] = pv.Written == null ? JValue.CreateNull() : new JValue(pv.Written.Value),
                // 自己没写出、但意图已由一次集合发射产出（如四季组）
                ["groupEmitted"] = pv.GroupEmitted,
                ["writeNote"] = pv.WriteNote
            });
        }

        var summary = new JObject();
        foreach (Verdict v in Enum.GetValues<Verdict>())
            summary[v.ToString()] = report.Summary[v];

        var root = new JObject
        {
            ["pack"] = report.Pack,
            ["tree"] = report.Tree,
            ["generatedAt"] = report.GeneratedAt,
            ["total"] = report.Total,
            ["convertible"] = report.ConvertibleCount,
            ["groupEmitted"] = report.GroupEmittedCount,
            ["blocked"] = report.BlockedCount,
            ["attemptedWrite"] = report.AttemptedWriteCount,
            ["written"] = report.WrittenCount,
            ["writeFailed"] = report.WriteFailedCount,
            ["summary"] = summary,
            ["notes"] = report.Notes.Count == 0 ? null : new JArray(report.Notes),
            ["normalization"] = BuildNormalization(report.Normalization),
            ["seasonalEmission"] = BuildSeasonalEmission(report.SeasonalEmission),
            ["tilesheetEmission"] = BuildSeasonalEmission(report.TilesheetEmission),
            ["appearanceEmission"] = BuildSeasonalEmission(report.AppearanceEmission),
            ["conditionPlacement"] = BuildConditionAudit(report.ConditionAudit),
            ["outputShape"] = BuildShapeAudit(report.ShapeAudit),
            ["locationClass"] = BuildLocationClassAudit(report.LocationClassAudit),
            ["assetType"] = BuildAssetTypeAudit(report.AssetTypeAudit),
            ["tokenConditions"] = BuildTokenConditionAudit(report.TokenConditionAudit),
            ["gameData"] = BuildGameDataAudit(report.GameDataAudit),
            ["spriteSheet"] = BuildSpriteAudit(report.SpriteAudit),
            ["unexpandedTokens"] = BuildTokenAudit(report.TokenAudit),
            ["keyCase"] = BuildKeyCaseAudit(report.KeyCaseAudit),
            ["patches"] = patches
        };

        SdvCpTool.Core.Workspace.JsonFile.Write(path, root);
    }

    public static string RenderSummary(AnalysisReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"内容包：{report.Pack}");
        sb.AppendLine($"资源树：{report.Tree}");
        sb.AppendLine($"生成时间：{report.GeneratedAt}");
        sb.AppendLine();
        sb.AppendLine($"补丁总数：{report.Total}");
        sb.AppendLine($"  判定可合并：{report.ConvertibleCount}");
        if (report.GroupEmittedCount > 0)
            sb.AppendLine($"  集合发射覆盖：{report.GroupEmittedCount}（自己无落点，意图已并进组合产物 —— 不计入「无法合并」）");
        sb.AppendLine($"  无法合并：  {report.BlockedCount}");
        sb.AppendLine($"  实际写入：  {report.WrittenCount}");
        sb.AppendLine($"  写入失败：  {report.WriteFailedCount}");
        int unattempted = report.ConvertibleCount - report.AttemptedWriteCount;
        // ⚠ 那个差值**不是**「本任务范围外」的同义词。它按判定档分成两类：
        //   `NeedsReview`（需人工确认 —— **待解决**）与 `NotApplicable`（条件折叠后恒假 —— **本就不适用**）。
        //   原先那句「判定可转换，但本任务范围外」把「还没解决」说成了「不归本任务管」。
        //   构成直接从 `Patches` 数出来，**不假设**它等于哪几个档的和（别拿恒等式当契约）。
        sb.AppendLine(unattempted > 0
            ? $"  未尝试写入：{unattempted}（{DescribeUnattempted(report)}）"
            : $"  未尝试写入：{unattempted}");
        sb.AppendLine();
        sb.AppendLine("按判定分类：");

        foreach (Verdict v in Enum.GetValues<Verdict>())
        {
            int n = report.Summary[v];
            if (n > 0) sb.AppendLine($"  {v,-26} {n,5}");
        }

        sb.AppendLine();
        sb.AppendLine("按通道分类：");
        foreach (Channel c in Enum.GetValues<Channel>())
        {
            int n = report.Patches.Count(p => p.Channel == c);
            if (n > 0) sb.AppendLine($"  {c,-26} {n,5}");
        }

        Normalizing.NormalizationReport norm = report.Normalization;
        if (norm.DimensionPatches > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"变体归一化（带维度条件的补丁共 {norm.DimensionPatches}）：");
            sb.AppendLine($"  有原生最简形式：{norm.Native,6}（{norm.Native * 100 / norm.DimensionPatches}%）");
            sb.AppendLine($"  已确证无机制：  {norm.None,6}");
            sb.AppendLine($"  有机制装不下：  {norm.CapacityExceeded,6}");
            // 「未定」单列 —— 并进「有」会虚报覆盖率
            sb.AppendLine($"  未定（待查）：  {norm.Unknown,6}");
            // ★ 「待查」必须**列出是哪几格** —— 只给个数的话，读者没法据此去啃骨头，
            //   而这份清单本来就是「还有什么没查」的答案。按条数降序。
            foreach (Normalizing.EmitterCell cell in norm.Cells
                         .Where(c => c.Plan.Kind == Normalizing.EmitKind.Unknown)
                         .OrderByDescending(c => c.PatchCount).Take(10))
                sb.AppendLine($"      {cell.PatchCount,5}  ({cell.Carrier}, {cell.Dimension}, {cell.AssetType})");
            int unknownCells = norm.Cells.Count(c => c.Plan.Kind == Normalizing.EmitKind.Unknown);
            if (unknownCells > 10)
                sb.AppendLine($"      ... 其余 {unknownCells - 10} 格见 JSON 报告");
            if (norm.ApproximatePatches > 0)
            {
                sb.AppendLine($"  ⚠ 其中收敛会丢语义：{norm.ApproximatePatches} 条" +
                              $"（{norm.ApproximateIntents} 个意图带了本维度之外的条件，只能标「近似」）");
            }

            if (norm.RewrittenPatches > 0)
            {
                // ⚠ 逐条改写**不减少条数** —— 与「合并」是两回事，必须分开报。
                sb.AppendLine($"  逐条改写为原生形式：{norm.RewrittenPatches} 条（{norm.RewrittenIntents} 个意图，条数不变）");
            }
            if (norm.MergedIntents > 0)
            {
                // ★ 真·压缩：N 条补丁 → 更少的产物（**按发射表推的**）
                sb.AppendLine($"  ★ 真·合并（按发射表推）：{norm.CollapsedPatches + norm.MergedIntents} 条补丁 " +
                              $"→ {norm.MergedIntents} 个最简形式（压掉 {norm.CollapsedPatches} 条）");
            }

            // ★★ **实际**发射的收敛量。与上面那一档必须分开看：
            //    上面是「这一格原版有没有机制」（查表），这里是「本次真的压了没有」（查结局）。
            //    两者相等是巧合，不等才是常态 —— 发射有前提（消费方唯一、索引对齐…）。
            if (norm.EmittedIntents > 0 || norm.RejectedIntents > 0)
            {
                int asked = norm.EmittedPatches + norm.RejectedPatches;
                sb.AppendLine($"  ★★ 实际集合发射：{norm.EmittedIntents} 个意图、{norm.EmittedPatches} 条补丁"
                              + $" → {norm.EmittedIntents} 个最简形式（压掉 {norm.EmittedPatches - norm.EmittedIntents} 条）");
                if (norm.RejectedIntents > 0)
                {
                    sb.AppendLine($"      另有 {norm.RejectedIntents} 个意图（{norm.RejectedPatches} 条）"
                                  + "**前提不成立未发射**，整组保持原判 —— 这是如实留在原判里，不是失败");
                }
                if (asked > 0)
                    sb.AppendLine($"      发射率：{norm.EmittedPatches * 100 / asked}%（按补丁数）");
            }

            sb.AppendLine("  注：以上「按发射表推」是**原版有没有机制**的结论；"
                          + "「实际集合发射」是**本次真做到的**。两者不等是常态（发射有前提）。");

            sb.AppendLine("  各维度的实现熵（载体数越多 = 同一意图被越多写法实现）：");
            foreach ((Normalizing.VariantDimension dim, HashSet<Normalizing.VariantCarrier> spread)
                     in norm.CarrierSpread.OrderByDescending(kv => kv.Value.Count))
            {
                sb.AppendLine($"    {dim,-12} {spread.Count} 种载体");
            }
        }

        // 四季贴图集合发射（Task 29）：**真的把 N 条补丁并成了更少的产物**，与上面「按机制统计」不同 ——
        // 这里报的是本工具已经发射出去的量。没发射的各组要能看出缘由。
        RenderSeasonal(sb, "四季贴图（BigCraftable 索引）", report.SeasonalEmission);
        RenderSeasonal(sb, "四季图集（文件名前缀 + 地图改指）", report.TilesheetEmission);
        RenderSeasonal(sb, "立绘/行走图（Data/Characters 的 Appearance[]）", report.AppearanceEmission);

        static void RenderSeasonal(System.Text.StringBuilder sb, string title, SeasonalEmissionAudit? seasonal)
        {
            if (seasonal is not { Groups: > 0 }) return;
            sb.AppendLine();
            sb.AppendLine($"{title}集合发射（{seasonal.Groups} 个四季组）：");
            sb.AppendLine($"  已发射：{seasonal.Emitted,4} 组（覆盖 {seasonal.PatchesCovered} 条条件 EditImage）");
            if (seasonal.Rejected > 0)
            {
                sb.AppendLine($"  未发射：{seasonal.Rejected,4} 组（前提不成立，整组保持原判）");
                foreach (string reason in seasonal.RejectReasons)
                    sb.AppendLine($"      · {reason}");
            }
            if (seasonal.ApproximateGroups > 0)
            {
                sb.AppendLine($"  ⚠ 其中 {seasonal.ApproximateGroups} 组是**近似**：原生 Seasonal 机制装不下 "
                              + string.Join("/", seasonal.LostDimensions));
            }
        }

        // 有损确定化：写入成功，但值是「判不了 → 落默认块」猜出来的，可能不等于运行时真值。
        // **必须单独列**，否则会被当成无损转换读过去。
        List<PatchVerdict> lossy = report.Patches
            .Where(p => p.Written == true && p.WriteNote != null && p.WriteNote.Contains("有损确定化"))
            .ToList();
        if (lossy.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"⚠ 有损确定化（写入成功，但值可能是猜的）：{lossy.Count} 条");
            foreach (PatchVerdict pv in lossy.Take(10))
                sb.AppendLine($"  {pv.Patch.DisplayName}");
            if (lossy.Count > 10) sb.AppendLine($"  ... 其余 {lossy.Count - 10} 条见 JSON 报告");
        }

        Validating.GameDataValidator.Audit? gd = report.GameDataAudit;
        if (gd is { Findings.Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"⚠ 数据形状校验（按游戏模型）：{gd.Findings.Count} 处容器形状不符" +
                          $"（核对 {gd.CheckedAssets} 个模型认得的资产）—— 游戏按模型反序列化会失败/忽略");
            foreach (Validating.GameDataValidator.Finding f in gd.Findings.Take(10))
                sb.AppendLine($"  {f.Asset}  {f.Entry}/{f.Field}  应为 {f.Expected}，实际 {f.Actual}");
        }

        Validating.SpriteSheetValidator.Audit? sprite = report.SpriteAudit;
        if (sprite is { Entries: > 0 })
        {
            sb.AppendLine();
            if (sprite.TotalProblems == 0)
            {
                sb.AppendLine($"贴图寻址校验（按游戏公式）：{sprite.Entries} 个条目全部取得到图 ✓");
            }
            else
            {
                sb.AppendLine($"⚠ 贴图寻址校验（按游戏公式）：{sprite.TotalProblems} 个条目取不到图 —— "
                              + $"贴图缺失 {sprite.MissingTexture}、精灵格越界 {sprite.OutOfBounds}、"
                              + $"贴图宽不合格 {sprite.BadSheetWidth}、Seasonal 贴图不足四季位 {sprite.SeasonalTooNarrow}"
                              + "（游戏只记日志/画出界，不报错）");
                foreach (Validating.SpriteSheetValidator.Finding f in sprite.Findings.Take(10))
                    sb.AppendLine($"  {f.Asset}  `{f.EntryKey}`  → {f.Texture}#{f.SpriteIndex}  {f.Reason}");
            }
        }

        Validating.KeyCaseConflictAudit.Audit? kc = report.KeyCaseAudit;
        if (kc is { CheckedAssets: > 0 })
        {
            sb.AppendLine();
            if (kc.Conflicts == 0)
            {
                sb.AppendLine($"键名大小写校验：{kc.CheckedAssets} 份数据资产里没有「仅大小写不同」的键 ✓");
            }
            else
            {
                sb.AppendLine($"⚠ 键名大小写校验：{kc.Conflicts} 处**同一对象内仅大小写不同**的键；"
                              + $"其中 {kc.FatalConflicts} 处两侧**类型不同**，涉及 {kc.FatalAssets.Count} 个资产");
                sb.AppendLine($"      类型不同的那 {kc.FatalAssets.Count} 个资产 **打不出 XNB**；"
                              + "类型相同的：两键撞到同一属性，**后者覆盖前者** —— 不报错但**静默改数据**");
                foreach (Validating.KeyCaseConflictAudit.Finding f in kc.Findings.Take(8))
                    sb.AppendLine($"  {(f.Fatal ? "✗" : "·")} {f.Asset}  {f.Path}  `{f.Key1}` / `{f.Key2}`");
            }
        }

        AssetTypeAudit.Audit? assetType = report.AssetTypeAudit;
        if (assetType is { Findings.Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"⚠ 资产类型自检：{assetType.Findings.Count} 份资产的类型与原版**不一致**"
                          + $"（对照 {assetType.CheckedAssets} 份）—— 打包后游戏按产出这份加载，"
                          + "把地图当贴图（或反过来）就是**进那个地点/场景就炸**");
            foreach (AssetTypeAudit.Finding f in assetType.Findings.Take(10))
                sb.AppendLine($"  {f.Asset}  产出 {f.ProducedExtension} / 原版 {f.VanillaExtension}");
        }

        TokenConditionAudit.Audit? tokenCond = report.TokenConditionAudit;
        if (tokenCond is { ConditionalOnTokens: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"⚠ 条件键是**包内令牌**：补丁 {tokenCond.ConditionalOnTokens} 条"
                          + $"（看过 {tokenCond.CheckedPatches} 条带条件补丁）、"
                          + $"令牌块 {tokenCond.TokenBlocksConditionalOnTokens} 块"
                          + $"（看过 {tokenCond.CheckedTokenBlocks} 块带条件令牌）"
                          + " —— 这一档**我们的求值器不认**（CP 认）⇒ 那些条件一律当作不成立、"
                          + "补丁落到默认分支或不写");
            foreach (TokenConditionAudit.Finding f in tokenCond.Findings.Take(8))
                sb.AppendLine($"  {f.Patch}  {f.Target}  条件键 `{f.Key}`");
        }

        OutputShapeAudit.Audit? shape = report.ShapeAudit;
        if (shape is { SkippedSameTree: true })
        {
            sb.AppendLine();
            sb.AppendLine("· 产出形状自检：**跳过** —— 输入树与产出树是同一棵（链式跑法的 --append），"
                          + "这两项自检无从对照（`checkedAssets` 会是 0，**别读成没问题**）；"
                          + "要真跑就对**原版树**逐包跑（`tools/run-packs.py` 那种）。");
        }
        else if (shape is { Findings.Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"⚠ 产出形状自检：{shape.Findings.Count} 处**原版是数组、我们写成了对象**" +
                          $"（对照 {shape.CheckedAssets} 份原版资产）—— 游戏按 List 反序列化会失败/忽略");
            foreach (OutputShapeAudit.Finding f in shape.Findings.Take(10))
                sb.AppendLine($"  {f.Asset}  {f.Path}");
        }

        LocationClassAudit.Audit? loc = report.LocationClassAudit;
        if (loc is { Findings.Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"⚠ 地点类自检：{loc.Findings.Count} 个地点的 `CreateOnLoad.Type` 来自 **mod 的程序集**"
                          + $"（共 {loc.CheckedLocations} 个地点）—— 纯 XNB 装机里没有那个程序集，"
                          + "游戏建地点时会抛 `Invalid type for location` ⇒ **地点根本不存在**，"
                          + "它的地图/传送/内容都到不了；报告只报不改（删 `Type` 会连行为一起丢，是取舍）");
            foreach (LocationClassAudit.Finding f in loc.Findings.Take(10))
                sb.AppendLine($"  {f.Location}  ← {f.Type}, {f.Assembly}" + (f.MapPath == null ? "" : $"  （地图 {f.MapPath}）"));
            if (loc.Findings.Count > 10) sb.AppendLine($"  ... 其余 {loc.Findings.Count - 10} 处见 JSON 报告");
        }

        ConditionPlacementAudit.Audit? audit = report.ConditionAudit;
        if (audit is { SkippedSameTree: true })
        {
            sb.AppendLine();
            sb.AppendLine("· 条件落点自检：**跳过** —— 输入树与产出树是同一棵（链式跑法的 --append），"
                          + "拿产出跟它自己比必是 0；要真跑就用 `tools/run-packs.py`（逐包对原版树）。");
        }
        else if (audit is { Findings.Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"⚠ 条件落点自检：{audit.Findings.Count} 处 Condition 写在**游戏不读**的位置" +
                          $"（对照 {audit.CheckedAssets} 份原版资产；{audit.SkippedNoVanilla} 份原件不在树里，跳过）");
            foreach (ConditionPlacementAudit.Finding f in audit.Findings.Take(10))
                sb.AppendLine($"  {f.Asset}  {f.Path}");
            if (audit.Findings.Count > 10) sb.AppendLine($"  ... 其余 {audit.Findings.Count - 10} 处见 JSON 报告");
        }

        List<PatchVerdict> writeFailed = report.Patches.Where(p => p.Written == false).ToList();
        if (writeFailed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"写入失败的条目（共 {writeFailed.Count}，显示前 20）：");
            foreach (PatchVerdict pv in writeFailed.Take(20))
            {
                sb.AppendLine($"  [{pv.Verdict}] {pv.Patch.DisplayName}");
                sb.AppendLine($"      {pv.WriteNote}");
            }
            if (writeFailed.Count > 20)
                sb.AppendLine($"  ... 其余 {writeFailed.Count - 20} 条见 JSON 报告");
        }

        // ⚠ 必须与 BlockedCount **同一个口径**：已由集合发射覆盖的（GroupEmitted）要扣掉。
        //   少这一步，汇总行说 658、下面的列表却列 866 —— 实测差 208，正好是四季组发射的数。
        //   同一次报告里两个数对不上，读者只会信错的那个。
        List<PatchVerdict> blocked = report.Patches
            .Where(p => BlockedVerdicts.Contains(p.Verdict) && !p.GroupEmitted)
            .ToList();
        if (blocked.Count > 0)
        {
            // ★ 先按**理由**分组给一张汇总表。只有前 20 条流水账时，读者看不出
            //   「卡住的是同一件事的若干条」还是「若干件不同的事」—— 而处置方式完全不同。
            sb.AppendLine();
            sb.AppendLine($"无法合并的条目（共 {blocked.Count}），按理由归并：");
            foreach ((int count, string verdict, string reason) in GroupReasons(blocked).Take(10))
                sb.AppendLine($"  {count,5}  [{verdict}] {reason}");
            if (GroupReasons(blocked).Count > 10)
                sb.AppendLine($"  ... 其余 {GroupReasons(blocked).Count - 10} 类见 JSON 报告");

            sb.AppendLine();
            sb.AppendLine($"  逐条（前 20）：");
            foreach (PatchVerdict pv in blocked.Take(20))
            {
                sb.AppendLine($"  [{pv.Verdict}] {pv.Patch.DisplayName}");
                sb.AppendLine($"      {pv.Reason}");
            }
            if (blocked.Count > 20)
                sb.AppendLine($"  ... 其余 {blocked.Count - 20} 条见 JSON 报告");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 「未尝试写入」那批的判定构成，条数降序。
    /// <para>
    /// 排除 <c>GroupEmitted</c>：那些补丁没有逐条落点（意图已并进一次组合发射），
    /// 本来就不该出现在「没试过写」这句里。
    /// </para>
    /// </summary>
    private static string DescribeUnattempted(AnalysisReport report)
    {
        List<string> parts = report.Patches
            .Where(p => p.Written == null && !p.GroupEmitted)
            .GroupBy(p => p.Verdict)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {g.Count()}")
            .ToList();
        return parts.Count == 0 ? "无" : string.Join("、", parts);
    }

    /// <summary>
    /// 按「判定 + 理由」归并阻塞条目，条数降序。
    /// <para>
    /// 理由里的<b>反引号内容会被替换成 <c>X</c></b> —— 否则每条的具体资产名/条目键都不同，
    /// 归并出来的每组都只有 1 条，等于没归并。实测：SVE 的 377 条 `NoLandingPoint`
    /// 里 212 条其实是同一件事（四季 BigCraftable），不掩码就看不出来。
    /// </para>
    /// </summary>
    private static List<(int Count, string Verdict, string Reason)> GroupReasons(List<PatchVerdict> blocked)
    {
        var groups = new Dictionary<(string, string), int>();
        foreach (PatchVerdict pv in blocked)
        {
            string masked = MaskReason(pv.Reason);
            var key = (pv.Verdict.ToString(), masked);
            groups[key] = groups.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        return groups
            .Select(kv => (Count: kv.Value, Verdict: kv.Key.Item1, Reason: kv.Key.Item2))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Verdict, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 把理由里的<b>具体值</b>换成 <c>X</c>，让同类的理由能归到一组。
    /// <list type="bullet">
    /// <item>反引号包着的一段 → <c>`X`</c>（理由里大多数具体值都是这个形态）。</item>
    /// <item><b>裸的路径形态</b>（含 <c>/</c> 的标识串）→ <c>X</c>。少这一步，
    /// <c>TargetMissing</c> 的理由里那个没加反引号的资产名会让<b>每个资产各成一组</b>，
    /// 把汇总表的前十挤满（实测 SVE：11 个 TargetMissing 组本该是 1 类）。</item>
    /// </list>
    /// </summary>
    private static string MaskReason(string reason)
    {
        string masked = System.Text.RegularExpressions.Regex.Replace(reason, "`[^`]*`", "`X`");
        return System.Text.RegularExpressions.Regex.Replace(
            masked, @"[A-Za-z0-9_.\-]+(?:/[A-Za-z0-9_.\-]+)+", "X");
    }

    /// <summary>模型驱动的产出校验一节。**只报告，不改产物**。</summary>
    /// <summary>贴图寻址校验一节。**只报告，不改产物**。</summary>
    private static JObject? BuildTokenAudit(Validating.UnexpandedTokenAudit.Audit? audit)
    {
        if (audit == null) return null;
        return new JObject
        {
            ["checkedAssets"] = audit.CheckedAssets,
            ["harmful"] = audit.Harmful,
            ["identifierOnly"] = audit.IdentifierOnly,
            ["findings"] = new JArray(audit.Findings.Take(Validating.UnexpandedTokenAudit.Audit.MaxFindings)
                .Select(f => new JObject
                {
                    ["asset"] = f.Asset,
                    ["path"] = f.Path,
                    ["field"] = f.Field,
                    ["token"] = f.TokenName,
                    ["identifierOnly"] = f.IdentifierOnly,
                })),
        };
    }

    private static JObject? BuildSpriteAudit(Validating.SpriteSheetValidator.Audit? audit)
    {
        if (audit == null) return null;
        var findings = new JArray();
        foreach (Validating.SpriteSheetValidator.Finding f in audit.Findings)
            findings.Add(new JObject
            {
                ["asset"] = f.Asset,
                ["entry"] = f.EntryKey,
                ["texture"] = f.Texture,
                ["spriteIndex"] = f.SpriteIndex,
                ["reason"] = f.Reason,
            });
        return new JObject
        {
            ["assets"] = audit.Assets,
            ["entries"] = audit.Entries,
            ["missingTexture"] = audit.MissingTexture,
            ["outOfBounds"] = audit.OutOfBounds,
            ["badSheetWidth"] = audit.BadSheetWidth,
            ["seasonalTooNarrow"] = audit.SeasonalTooNarrow,
            ["findings"] = findings,
        };
    }

    /// <summary>键名大小写冲突一节。**只报告，不改产物**。</summary>
    private static JObject? BuildKeyCaseAudit(Validating.KeyCaseConflictAudit.Audit? audit)
    {
        if (audit == null) return null;
        var findings = new JArray();
        foreach (Validating.KeyCaseConflictAudit.Finding f in audit.Findings)
            findings.Add(new JObject
            {
                ["asset"] = f.Asset, ["path"] = f.Path, ["key1"] = f.Key1, ["key2"] = f.Key2,
            });
        return new JObject
        {
            ["checkedAssets"] = audit.CheckedAssets,
            ["conflicts"] = audit.Conflicts,
            ["fatalConflicts"] = audit.FatalConflicts,
            ["fatalAssets"] = new JArray(audit.FatalAssets.OrderBy(x => x, StringComparer.Ordinal)),
            ["findings"] = findings,
        };
    }

    private static JObject? BuildGameDataAudit(Validating.GameDataValidator.Audit? audit)
    {
        if (audit == null) return null;
        return new JObject
        {
            ["checkedAssets"] = audit.CheckedAssets,
            ["shapeMismatch"] = audit.Findings.Count,
            ["findings"] = new JArray(audit.Findings.Take(200).Select(f => new JObject
            {
                ["asset"] = f.Asset,
                ["entry"] = f.Entry,
                ["field"] = f.Field,
                ["expected"] = f.Expected.ToString(),
                ["actual"] = f.Actual,
            })),
        };
    }

    /// <summary>产出形状自检一节。**只报告，不改产物**。</summary>
    private static JObject? BuildAssetTypeAudit(AssetTypeAudit.Audit? audit)
    {
        if (audit == null) return null;
        return new JObject
        {
            ["checkedAssets"] = audit.CheckedAssets,
            ["typeMismatch"] = audit.Findings.Count,
            ["findings"] = new JArray(audit.Findings.Take(200).Select(f => new JObject
            {
                ["asset"] = f.Asset,
                ["produced"] = f.ProducedExtension,
                ["vanilla"] = f.VanillaExtension,
            })),
        };
    }

    /// <summary>条件键是包内令牌那一档。**只报告，不改产物**。</summary>
    private static JObject? BuildTokenConditionAudit(TokenConditionAudit.Audit? audit)
    {
        if (audit == null) return null;
        return new JObject
        {
            ["checkedPatches"] = audit.CheckedPatches,
            ["conditionalOnTokens"] = audit.ConditionalOnTokens,
            ["checkedTokenBlocks"] = audit.CheckedTokenBlocks,
            ["tokenBlocksConditionalOnTokens"] = audit.TokenBlocksConditionalOnTokens,
            ["findings"] = new JArray(audit.Findings.Take(200).Select(f => new JObject
            {
                ["patch"] = f.Patch,
                ["target"] = f.Target,
                ["key"] = f.Key,
            })),
        };
    }

    private static JObject? BuildShapeAudit(OutputShapeAudit.Audit? audit)
    {
        if (audit == null) return null;
        return new JObject
        {
            ["checkedAssets"] = audit.CheckedAssets,
            ["skippedNoVanilla"] = audit.SkippedNoVanilla,
            ["skippedSameTree"] = audit.SkippedSameTree,   // 真时 checkedAssets=0：**没比**，不是没问题
            ["arrayBecameObject"] = audit.Findings.Count,
            ["findings"] = new JArray(audit.Findings.Take(200).Select(f => new JObject
            {
                ["asset"] = f.Asset,
                ["path"] = f.Path,
                ["field"] = f.Field,
                ["expected"] = f.Expected,
            })),
        };
    }

    /// <summary>地点类自检一节。**只报告，不改产物** —— 删 `Type` 是取舍，不做默认行为。</summary>
    private static JObject? BuildLocationClassAudit(LocationClassAudit.Audit? audit)
    {
        if (audit == null) return null;
        return new JObject
        {
            ["checkedLocations"] = audit.CheckedLocations,
            ["modClasses"] = audit.Findings.Count,
            ["findings"] = new JArray(audit.Findings.Take(200).Select(f => new JObject
            {
                ["location"] = f.Location,
                ["type"] = f.Type,
                ["assembly"] = f.Assembly,
                ["mapPath"] = f.MapPath,
            })),
        };
    }

    /// <summary>条件落点自检一节。**只报告，不改产物** —— 闸门在 ConditionWriter。</summary>
    private static JObject? BuildConditionAudit(ConditionPlacementAudit.Audit? audit)
    {
        if (audit == null) return null;
        return new JObject
        {
            ["checkedAssets"] = audit.CheckedAssets,
            ["skippedNoVanilla"] = audit.SkippedNoVanilla,
            ["skippedSameTree"] = audit.SkippedSameTree,   // 真时 checkedAssets=0：**没比**，不是没问题
            ["misplaced"] = audit.Findings.Count,
            ["findings"] = new JArray(audit.Findings.Take(200).Select(f => new JObject
            {
                ["asset"] = f.Asset,
                ["path"] = f.Path,
                ["field"] = f.Field,
                ["value"] = f.Snippet,
            })),
        };
    }

    /// <summary>
    /// 变体归一化一节。<b>四档各自成键、互不合并</b> ——
    /// 把「未定」并进「有」会虚报覆盖率（本轮实测差 9 个百分点）。
    /// </summary>
    private static JObject? BuildSeasonalEmission(SeasonalEmissionAudit? s) =>
        s is null ? null : new JObject
        {
            ["groups"] = s.Groups,
            ["emitted"] = s.Emitted,
            ["rejected"] = s.Rejected,
            ["patchesCovered"] = s.PatchesCovered,
            ["approximateGroups"] = s.ApproximateGroups,
            ["lostDimensions"] = new JArray(s.LostDimensions),
            ["rejectReasons"] = new JArray(s.RejectReasons),
        };

    private static JObject BuildNormalization(Normalizing.NormalizationReport n)
    {
        var cells = new JArray();
        foreach (Normalizing.EmitterCell c in n.Cells)
        {
            cells.Add(new JObject
            {
                ["carrier"] = c.Carrier.ToString(),
                ["dimension"] = c.Dimension.ToString(),
                ["assetType"] = c.AssetType,
                ["patches"] = c.PatchCount,
                ["kind"] = c.Plan.Kind.ToString(),
                ["nativeForm"] = c.Plan.NativeForm,
                ["evidence"] = c.Plan.Evidence,
                ["precondition"] = c.Plan.Precondition
            });
        }

        var carSpreads = new JObject();
        foreach ((Normalizing.VariantDimension dim, HashSet<Normalizing.VariantCarrier> spread) in n.CarrierSpread)
            carSpreads[dim.ToString()] = spread.Count;   // 这个数就是该维度的「熵」

        return new JObject
        {
            ["dimensionPatches"] = n.DimensionPatches,
            ["native"] = n.Native,
            ["none"] = n.None,
            ["capacityExceeded"] = n.CapacityExceeded,
            ["unknown"] = n.Unknown,
            // 收敛会丢语义的那一档：带第二维度时原版机制装不下，不得算成满分转换
            // ★★ 实际发射的收敛量（查结局），与上面的「按发射表推」分开
            ["emittedIntents"] = n.EmittedIntents,
            ["emittedPatches"] = n.EmittedPatches,
            ["rejectedIntents"] = n.RejectedIntents,
            ["rejectedPatches"] = n.RejectedPatches,
            ["approximateIntents"] = n.ApproximateIntents,
            ["approximatePatches"] = n.ApproximatePatches,
            // ★ 收敛量：N 个实现 → 1 个最简实现（只算发射表判 Native 的格子）
            ["rewrittenIntents"] = n.RewrittenIntents,
            ["rewrittenPatches"] = n.RewrittenPatches,
            ["mergedIntents"] = n.MergedIntents,
            ["collapsedPatches"] = n.CollapsedPatches,
            ["carrierSpread"] = carSpreads,
            ["cells"] = cells
        };
    }

    /// <summary>
    /// 这条补丁**碰多少个条目**（代价的一个粗略口径，给「同一目的有几条路」比较用）。
    /// <para>
    /// `Entries`/`Fields` 看第一层的键数、`MoveEntries`/`MapTiles`/`AddWarps`/`TextOperations`
    /// 等看数组长度；都没有的（整块替换、单字段改写）记 <c>1</c>。
    /// ⚠ 只是**数量级**，不是"改了之后产出会差多少字节"——那个要真跑才知道。
    /// </para>
    /// </summary>
    private static int CountTargets(JObject raw)
    {
        foreach (string field in new[]
                 {
                     "Entries", "Fields", "MoveEntries", "MapProperties", "MapTiles",
                     "AddWarps", "AddNpcWarps", "TextOperations", "TargetField"
                 })
        {
            if (raw[field] is JContainer c)
                return c.Count;
        }

        return 1;
    }

    private static string RelativeSource(string sourceFile, string packRoot)
    {
        if (string.IsNullOrEmpty(sourceFile) || string.IsNullOrEmpty(packRoot)) return sourceFile;
        try
        {
            return Path.GetRelativePath(packRoot, sourceFile).Replace('\\', '/');
        }
        catch
        {
            return sourceFile;
        }
    }
}
