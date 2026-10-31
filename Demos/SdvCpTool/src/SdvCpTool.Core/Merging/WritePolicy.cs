using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// M3 决定哪些补丁在产出树里真的写入。
/// <list type="bullet">
/// <item><c>Load</c>（无条件，判 <see cref="Verdict.Converted"/>）→ 写入（新建资产的打底）。</item>
/// <item><c>EditData</c> 且判「新建资产」或「无条件可转换」→ 写入。</item>
/// <item><c>EditData</c> 判 <see cref="Verdict.Converted"/> 且 <see cref="Channel.Gsq"/> → 写入（通道 A：把 GSQ 写进条件落点）。</item>
/// <item><c>EditData</c> 判 <see cref="Verdict.Converted"/> 且 <see cref="Channel.NativeKey"/> → 写入（条件改用原生专用键，如节日对话 <c>&lt;NPC&gt;_spouse</c>）。</item>
/// <item><c>EditImage</c> 判无条件可转换 → 写入（图片合成，走 <see cref="EditImageApplier"/>）。</item>
/// </list>
/// </summary>
public static class WritePolicy
{
    /// <summary>
    /// 该 <c>Action</c> 是否属于本任务已实现的写入范围
    /// （<c>Load</c> / <c>EditData</c> / <c>EditImage</c> / <c>EditMap</c>）。
    /// <para>
    /// ⚠ <c>EditMap</c> 只是<b>有这个 Action 的写入路径</b>，具体能不能写还要过
    /// <see cref="IsWritableMapEdit"/> —— 只做属性类改动，不碰瓦片几何。
    /// </para>
    /// </summary>
    public static bool IsWritableAction(string action) =>
        action.Equals("Load", StringComparison.OrdinalIgnoreCase)
        || action.Equals("EditData", StringComparison.OrdinalIgnoreCase)
        || action.Equals("EditImage", StringComparison.OrdinalIgnoreCase)
        || action.Equals("EditMap", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 这是一条<b>图片</b> <c>Load</c>：<c>FromFile</c> 以 <c>.png</c> 结尾，走
    /// <see cref="ImageApplier"/> 的逐字节复制路径（不是数据层）。
    /// </summary>
    public static bool IsImageLoad(ChangePatch patch) =>
        patch.Action.Equals("Load", StringComparison.OrdinalIgnoreCase)
        && (patch.FromFile?.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// <c>EditMap</c> 是否可写。
    /// <para>
    /// 支持 <c>MapProperties</c> / <c>AddWarps</c> / <c>AddNpcWarps</c> / <c>MapTiles</c> /
    /// 指向 <c>MapProperties</c> 的 <c>TextOperations</c>。
    /// <c>MapTiles</c> 的四个字段<b>全部支持</b>（<c>SetProperties</c> / <c>SetIndex</c> /
    /// <c>SetTilesheet</c> / <c>Remove</c>）—— 语义逐条照抄 CP
    /// <c>EditMapPatch.cs:235-312</c>，包含「空位在同时给了 <c>SetTilesheet</c> 与 <c>SetIndex</c> 时可新建」。
    /// </para>
    /// <para>
    /// <c>FromFile</c>（整图替换 / 合并）现在也写，合并语义**逐段照抄 SMAPI 的 <c>PatchMap</c>**
    /// （见 <see cref="Maps.MapPatcher"/>）—— 之前不做是因为那段实现不在可读的反编译源码里，
    /// 属「未确证，不猜」；现已从 <c>StardewModdingAPI.dll</c> 读出，猜测的前提消失。
    /// </para>
    /// </summary>
    public static bool IsWritableMapEdit(ChangePatch patch)
    {
        if (!patch.Action.Equals("EditMap", StringComparison.OrdinalIgnoreCase))
            return false;

        return patch.Raw["FromFile"] != null
            || patch.Raw["MapProperties"] != null
            || patch.Raw["AddWarps"] != null
            || patch.Raw["AddNpcWarps"] != null
            || patch.Raw["MapTiles"] != null
            || patch.Raw["TextOperations"] != null;
    }

    /// <summary>这条补丁本任务是否要写入（不代表写入一定成功）。</summary>
    public static bool ShouldWrite(PatchVerdict verdict)
    {
        if (!IsWritableAction(verdict.Patch.Action))
            return false;

        // JudgeLoad 给无条件 Load 判 Converted（有条件的判 NeedsReview，不写）
        if (verdict.Patch.Action.Equals("Load", StringComparison.OrdinalIgnoreCase))
            return verdict.Verdict == Verdict.Converted;

        // EditImage：图片层的唯一可写形态是"折叠后无条件"。图片没有通用条件落点（research §11），
        // 有条件的只会判 NoLandingPoint / 其它阻塞 —— 不允许漏到下面的 Converted 分支。
        if (verdict.Patch.Action.Equals("EditImage", StringComparison.OrdinalIgnoreCase))
            return verdict.Verdict == Verdict.ConvertedNoCondition;

        // EditMap：只写「属性类」改动（无条件；有条件的地图改动判定阶段已判 LocalEdit）。
        if (verdict.Patch.Action.Equals("EditMap", StringComparison.OrdinalIgnoreCase))
            return verdict.Verdict is Verdict.Converted or Verdict.ConvertedNoCondition
                && IsWritableMapEdit(verdict.Patch);

        // 通道 A：条件可翻译且落点已探明 → 进写入路径。
        if (verdict.Verdict == Verdict.Converted && verdict.Channel == Channel.Gsq)
            return true;

        // 原生键名：条件维度由资产自带专用键表达（节日对话 <NPC>_spouse）→ 进写入路径。
        if (verdict.Verdict == Verdict.Converted && verdict.Channel == Channel.NativeKey)
            return true;

        return verdict.Verdict is Verdict.CreatesNewAsset or Verdict.ConvertedNoCondition;
    }

    /// <summary>
    /// 「值里还有**代不掉的 CP 令牌**」这道闸是否该拦下它 —— 拦下的整条不写（宁可少写，不可写错）。
    /// <para>
    /// <b>判据必须与「会不会写」同一处</b>（<see cref="ShouldWrite"/>）。原先那道闸**只枚举了
    /// <c>Converted</c> / <c>ConvertedNoCondition</c>**，于是 <c>CreatesNewAsset</c> 从旁边漏了过去 ——
    /// 实测语料 **19 条**新建资产补丁就是这样把**有损猜出来的** i18n 键写进了产物
    /// （`{{i18n:kid1_intro1_Andy{{Baby1Gender}}}}`：`Baby1Gender` 依存档状态，判不了 ⇒ 落到默认块）。
    /// </para>
    /// <para>
    /// 依据是游戏不认识 <c>{{…}}</c>：落在 <c>Condition</c> 里是非法 GSQ（条目永不生效）、
    /// 落在 <c>Sprite</c>/<c>Texture</c> 里取不到图、落在事件键里事件永不触发。
    /// </para>
    /// </summary>
    public static bool TokenGateBlocks(PatchVerdict verdict)
    {
        if (!ShouldWrite(verdict)) return false;

        ChangePatch patch = verdict.Patch;

        // `Data/AudioChanges` 免检：值里的 `{{AbsoluteFilePath}}` 确实代不掉，但**根本不需要代** ——
        // 这条补丁的落法不是"写数据资产"，而是把 ogg 并进 Wave Bank、把 cue 加进 Sound Bank
        // （见 AudioChangesMerger）。判定与写入走同一个 CanHandle。
        if (AudioChangesMerger.CanHandle(patch))
            return false;

        // `Entries` 可以**逐条**取舍（CP `ApplyRecords` 就是逐条 Warn 后 continue），
        // 所以只有「一个能写的条目都没有」时才整条不写。
        if (patch.Raw["Entries"] is JObject entries && entries.Count > 0)
        {
            (List<string> writable, List<string> bad) = ValueTokenSubstitutor.SplitEntries(entries);
            if (bad.Count == 0) return false;
            if (writable.Count > 0) return false;      // 有好条目 ⇒ 写好的，只丢坏的
            return true;                                // 全坏 ⇒ 整条不写
        }

        // 没有 `Entries`（或为空）：沿用原来的判据 —— 别处没法逐条取舍。
        return ValueTokenSubstitutor.HasUnresolvedTokenOutsideEntries(patch);
    }

    /// <summary>未写入时给报告记的缘由（可读，供用户判断"为什么没写"）。</summary>
    public static string? SkipReason(PatchVerdict verdict)
    {
        if (ShouldWrite(verdict)) return null;

        if (verdict.Patch.Action.Equals("Load", StringComparison.OrdinalIgnoreCase))
            return verdict.Verdict == Verdict.NeedsReview
                ? "有条件 Load 只能靠新增资产名区分，需人工确认命名方案"
                : $"Load 判定为 {verdict.Verdict}，不在本任务写入范围";

        if (verdict.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)
            && verdict.Verdict == Verdict.Converted)
            return "判定为可转换，但通道不是 GSQ 落点写入（Task 6 只处理 Channel.Gsq）";

        // NeedsReview（含"新建资产的条件无法落地"）本任务不写 —— 把判定原因原样作为跳过缘由，
        // 报告里必须能逐条看出被拒的理由。
        if (verdict.Verdict == Verdict.NeedsReview)
            return verdict.Reason.Length > 0 ? verdict.Reason : "判定为 NeedsReview，需人工复核";

        // ⚠ 这条以前一律印「含瓦片几何字段…本任务只做属性类改动」—— 但
        //   `IsWritableMapEdit` **早就**把 `MapTiles`/`FromFile` 收进来了（语义逐条照抄
        //   CP `EditMapPatch.cs:235-312` 与 SMAPI 的 `PatchMap`）。走到这里的 EditMap 另有原因
        //   （最多的是**条件化**地图改动 = `LocalEdit`），**判定阶段已经写清**了 ——
        //   拿那句旧话去顶，会让 74 个资产被记到错的原因上（`tools/gap-assets.py` 量出来的）。
        if (verdict.Patch.Action.Equals("EditMap", StringComparison.OrdinalIgnoreCase))
            return verdict.Reason.Length > 0
                ? verdict.Reason
                : "EditMap 无法写入（瓦片几何字段本任务支持，说明这条另有原因）";

        if (verdict.Verdict is Verdict.CreatesNewAsset or Verdict.ConvertedNoCondition or Verdict.Converted)
            return $"Action {verdict.Patch.Action} 的写入不在本任务范围（本任务只写 EditData/Load/EditImage/EditMap 属性类）";

        return null;
    }
}
