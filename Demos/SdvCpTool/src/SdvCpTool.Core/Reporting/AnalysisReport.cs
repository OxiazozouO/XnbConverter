using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Normalizing;

namespace SdvCpTool.Core.Reporting;

public sealed class AnalysisReport
{
    public string Pack = "";
    public string Tree = "";
    public string GeneratedAt = "";
    public int Total;
    public Dictionary<Verdict, int> Summary = new();
    public List<PatchVerdict> Patches = new();

    /// <summary>
    /// 变体归一化的量化结果（熵：同一意图被几种写法实现）。
    /// <para>
    /// 由 <see cref="VariantNormalizer"/> 现算 —— <b>发射表只此一份</b>，
    /// 不许再有第二份手工同步的表（本轮已因两份表漂移吃过一次亏）。
    /// </para>
    /// </summary>
    public NormalizationReport Normalization { get; private set; } = new();

    /// <summary>
    /// 产出树自检：写出去的 <c>Condition</c> 有没有落在**游戏不读**的位置。
    /// 由调用方（CLI）在写完后填入 —— 它要看产出树，<see cref="Build"/> 拿不到。
    /// </summary>
    public ConditionPlacementAudit.Audit? ConditionAudit { get; set; }

    /// <summary>
    /// 产出树自检：**原版是数组的字段有没有被写成对象**（`Fish` 那一类结构性退化）。
    /// 同样在写完后由 CLI 填入。
    /// </summary>
    public OutputShapeAudit.Audit? ShapeAudit { get; set; }

    /// <summary>地点类自检：`CreateOnLoad.Type` 指向 mod 程序集的地点（纯 XNB 里建不出来）。</summary>
    public LocationClassAudit.Audit? LocationClassAudit { get; set; }

    /// <summary>资产类型自检（产出 vs 原版的 `.config` 声明的扩展名）。</summary>
    public AssetTypeAudit.Audit? AssetTypeAudit { get; set; }

    /// <summary>
    /// **条件键是包内令牌**的补丁有多少（这一档我们的 `When` 求值器不认 ⇒ 那些条件一律当作不成立）。
    /// 只报告，不改产物 —— 详见 `TokenConditionAudit`。
    /// </summary>
    public TokenConditionAudit.Audit? TokenConditionAudit { get; set; }

    /// <summary>
    /// **模型驱动**的产出校验：按 <c>GameDataSchema</c>（从游戏源码生成）核对容器形状。
    /// 比形状自检精确 —— 它有字段的**真类型**，不依赖原版数据里恰好用过什么。
    /// </summary>
    public Validating.GameDataValidator.Audit? GameDataAudit { get; set; }

    /// <summary>
    /// **按游戏自己的贴图寻址公式**核对产出：条目指到的贴图在不在、精灵格有没有越界。
    /// 前两道自检都只看 JSON 本身，这一道看 JSON <b>指向的资源</b>。
    /// </summary>
    public Validating.SpriteSheetValidator.Audit? SpriteAudit { get; set; }

    /// <summary>
    /// 产出自检：写出去的字符串里还留着**没展开的 CP 令牌**（<c>{{…}}</c>）——
    /// <c>Condition</c> 里带令牌是非法 GSQ（条目静默失效）、<c>Sprite</c> 里带令牌取不到图。
    /// 只报告，不改产物。
    /// </summary>
    public Validating.UnexpandedTokenAudit.Audit? TokenAudit { get; set; }

    /// <summary>
    /// 产出自检：同一**模型对象**里有没有**仅大小写不同**的两个键。
    /// 反序列化大小写不敏感 ⇒ 两键撞到同一属性 ⇒ 该资产**打不出 XNB**。
    /// 既有的三道自检结构上都抓不到它（详见 <see cref="Validating.KeyCaseConflictAudit"/>）。
    /// </summary>
    public Validating.KeyCaseConflictAudit.Audit? KeyCaseAudit { get; set; }

    /// <summary>
    /// 四季贴图**集合发射**的结果（Task 29）：几条条件补丁被合成一个原生「四季 BigCraftable」。
    /// 由 CLI 在发射阶段填入。<c>null</c> = 本包没有四季组（不是「失败」）。
    /// </summary>
    public SeasonalEmissionAudit? SeasonalEmission { get; set; }

    /// <summary>
    /// 四季<b>图集</b>集合发射的结果（Task 30）—— 与 <see cref="SeasonalEmission"/> 是<b>两套机制</b>：
    /// 那个走「BigCraftable 四季索引 + Name 加 Seasonal」，
    /// 这个走「文件名季节前缀 + 改地图 <c>TileSheet.ImageSource</c>」。分开统计，不混在一个数里。
    /// </summary>
    public SeasonalEmissionAudit? TilesheetEmission { get; set; }

    /// <summary>
    /// 立绘/行走图的 <c>Appearance[]</c> 集合发射结果（Task 32）—— 第三套机制。
    /// 与另两套一样单独统计，不混在一个数里。
    /// </summary>
    public SeasonalEmissionAudit? AppearanceEmission { get; set; }

    /// <summary>预计可合并的 patch 数。</summary>
    public int ConvertibleCount =>
        Summary[Verdict.Converted] + Summary[Verdict.ConvertedNoCondition]
        + Summary[Verdict.NeedsReview] + Summary[Verdict.CreatesNewAsset]
        // 条件折叠后恒假 = 该补丁本就不适用（跳过），既不是"可合并"也不是"无法合并"；
        // 计入此处以免把"已解决的跳过"误算成阻塞。
        + Summary[Verdict.NotApplicable];

    /// <summary>
    /// 被**集合发射**覆盖的 patch 数（自己没有落点，但意图已由一次组合发射产出，如四季组）。
    /// 这些**不算 blocked** —— 算进去就是少报自己的进度。
    /// </summary>
    public int GroupEmittedCount => Patches.Count(p => p.GroupEmitted);

    /// <summary>无法合并的 patch 数（已扣除被集合发射覆盖的那部分）。</summary>
    public int BlockedCount => Total - ConvertibleCount - GroupEmittedCount;

    /// <summary>本任务尝试写入的 patch 数（写入成功或失败都算）。</summary>
    public int AttemptedWriteCount => Patches.Count(p => p.Written != null);

    /// <summary>
    /// **内容包解析警告**与**非 CP 框架包的导入说明**（FTM 那种）。
    /// <para>
    /// ⚠ 这两样原先只打到 stdout —— 报告里没有。而它们说的正是"**哪些内容拿不到**"
    /// （FTM 的刷怪 246 个 area、没有 config.json 的包…）—— 回头只看报告的人就会漏掉。
    /// </para>
    /// </summary>
    public List<string> Notes { get; init; } = new();

    /// <summary>真正写进产出树的 patch 数。</summary>
    public int WrittenCount => Patches.Count(p => p.Written == true);

    /// <summary>尝试写入但失败的 patch 数。</summary>
    public int WriteFailedCount => Patches.Count(p => p.Written == false);

    public static AnalysisReport Build(string pack, string tree, IEnumerable<PatchVerdict> verdicts)
    {
        var report = new AnalysisReport
        {
            Pack = pack,
            Tree = tree,
            GeneratedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            Patches = verdicts.ToList()
        };

        foreach (Verdict v in Enum.GetValues<Verdict>())
            report.Summary[v] = 0;

        foreach (PatchVerdict pv in report.Patches)
            report.Summary[pv.Verdict]++;

        report.Total = report.Patches.Count;
        report.Normalization = VariantNormalizer.Run(report.Patches);
        return report;
    }
}

/// <summary>
/// 四季贴图集合发射的量化结果（Task 29）。
/// <para>
/// <b>没发射的各组要能逐条看出缘由</b> —— 只报「发射了几组」会把「为什么剩下的没发射」藏起来。
/// </para>
/// </summary>
public sealed class SeasonalEmissionAudit
{
    /// <summary>规划出的四季组数（含未发射的）。</summary>
    public int Groups;

    /// <summary>真发射出去的组数。</summary>
    public int Emitted;

    /// <summary>规划出来但**未发射**的组数（前提不成立，整组保持原判）。</summary>
    public int Rejected;

    /// <summary>被发射组覆盖的条件 <c>EditImage</c> 条数（每组 4 条）。</summary>
    public int PatchesCovered;

    /// <summary>
    /// 带**丢失维度**的已发射组数。非零说明有组是<b>近似</b> ——
    /// 原生 Seasonal 机制不看室内外，而这些补丁全带 <c>IsOutdoors</c>。
    /// </summary>
    public int ApproximateGroups;

    /// <summary>丢失过的维度名（去重）。</summary>
    public List<string> LostDimensions = new();

    /// <summary>未发射的缘由，按条数降序（去重后带计数）。</summary>
    public List<string> RejectReasons = new();
}
