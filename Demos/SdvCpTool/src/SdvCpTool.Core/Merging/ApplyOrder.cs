using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// 补丁的应用顺序，对齐 CP 语义：<c>Load</c> 打底先应用，其余（EditData 等）按 <see cref="ChangePatch.Priority"/> 升序叠加。
/// 与 CP <c>PatchManager.SortAndGroupEditPatches</c> 一致 —— 用 <see cref="Enumerable.OrderBy{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey})"/> 保证稳定：
/// 同优先级内保持内容包里的原有顺序。
/// </summary>
public static class ApplyOrder
{
    /// <summary>按 CP 语义排序；Load 恒定排在编辑补丁之前，编辑补丁按 Priority 升序、同值稳定。</summary>
    public static IEnumerable<PatchVerdict> Sort(IEnumerable<PatchVerdict> patches) =>
        patches.OrderBy(pv => pv.Patch.IsLoad ? (0, 0) : (1, pv.Patch.Priority));
}
