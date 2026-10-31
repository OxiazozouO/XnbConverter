namespace SdvCpTool.Core.Judging;

public enum Verdict
{
    Converted,
    ConvertedNoCondition,
    NeedsReview,

    /// <summary>
    /// 条件里的可静态求值叶子折叠后整条恒为假 —— 该补丁<b>本就不适用</b>（跳过，不是失败）。
    /// 与"需要人工复核"不同：这是已确定的结论。
    /// </summary>
    NotApplicable,

    /// <summary>目标资产不在资源树中，但 Action 是 EditData/Load —— CP 会新建该资产。</summary>
    CreatesNewAsset,

    NoLandingPoint,
    NonTranslatableCondition,
    NeedsRuntime,
    LocalEdit,

    /// <summary>Target 含 `{{...}}` 令牌，静态无法解析资产名。</summary>
    TargetContainsTokens,

    TargetMissing,
    UnsupportedAction
}
