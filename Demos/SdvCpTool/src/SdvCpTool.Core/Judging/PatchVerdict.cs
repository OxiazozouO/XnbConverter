using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Judging;

public sealed class PatchVerdict
{
    public ChangePatch Patch = null!;
    public Verdict Verdict;
    public string Reason = "";
    public string? SuggestedChannel;

    /// <summary>
    /// 判定时<b>折叠后</b>的条件树（未启用折叠时为 <c>null</c>）。
    /// 写入器必须用它，<b>不得</b>重新解析原始 <c>When</c> —— 否则判定与写入会再次各解释一遍同一条数据。
    /// </summary>
    public WhenNode? FoldedWhen;

    /// <summary>走哪条通道达成转换。未转换时为 <see cref="Channel.None"/>。</summary>
    public Channel Channel = Channel.None;

    /// <summary>
    /// 判定时探测到的条件落点（无条件的补丁为 <c>null</c>）。
    /// 写入选道 A 时据此决定把 GSQ 写进 <c>Condition</c> 字段还是追加到条目 key。
    /// </summary>
    public LandingKind? Landing;

    /// <summary>
    /// 这条补丁是否真的写进了产出树：
    /// <c>true</c> = 已写入，<c>false</c> = 尝试写入但失败，<c>null</c> = 本任务未尝试写入。
    /// 判定（<see cref="Verdict"/>）与写入是两回事 —— 不得把"判定可转换"当成"已写入"。
    /// </summary>
    public bool? Written;

    /// <summary>写入失败原因；未写入时也记明缘由。</summary>
    public string? WriteNote;

    /// <summary>
    /// 这条补丁**自己没有落点**，但它的意图已被一次<b>集合发射</b>（如四季组）覆盖 ——
    /// 产物已产出，只是不由这条补丁单独写出。
    /// <para>
    /// 与 <see cref="Written"/> 分开：<c>Written</c> 是「这条补丁被逐条写出」，
    /// 这里是「这条补丁被并进别的产物」。混为一谈会让「1 条补丁 1 次写入」的口径失真，
    /// 但不算进去又会把**已经处理掉的**补丁报成 <c>blocked</c>（少报自己的进度）。
    /// </para>
    /// </summary>
    public bool GroupEmitted;

    /// <summary>
    /// 这条补丁的**内容已被组合产物认领**（四季图集组按季收走的那几条）—— 因此**不要再直写一遍**。
    /// <para>
    /// 与 <see cref="GroupEmitted"/> 的区别正是"谁被认领了"：`GroupEmitted` 是**报告口径**
    /// （"这条的意图已并进组合产物"，同一张资产上的补丁都会被标注，含提供底图的无条件补丁）；
    /// 这里只标**真被组收走**的那些。混用会把底图也挡掉 —— 实测
    /// `167_Seasonal Cute Characters SVE` 的 `Maps/Spring_z_extras`：12 条无条件 + 4 条四季，
    /// 按 `GroupEmitted` 挡就是"底图不写了"，而四季变体要从底图出发 ⇒ 整组发射失败。
    /// </para>
    /// </summary>
    public bool GroupOwned;

    /// <summary>
    /// **条件化地图改动的门禁 GSQ**（非 null ⇒ 这条 `EditMap` 要「无条件落图 + 在图块上加门禁」）。
    /// <para>
    /// 静态 XNB 的地图没有条件化 Warp，唯一的替代是图块动作 `ConditionalDoor &lt;GSQ&gt;`
    /// （`GameLocation.cs:3907/9549`：GSQ 为假 ⇒ 玩家被弹回）⇒ 条件不成立时"看得见、进不去"。
    /// 只对**持久型**条件 + 有 `ToArea` 的补丁设（判据见 <see cref="Assets.ConditionPersistence"/>）。
    /// 写入侧直接用 <b>这条 GSQ 本身</b>（条件成立放行、不成立弹回；别再取反 —— 2026-10-18 订正过）。
    /// </para>
    /// </summary>
    public string? DoorGateGsq;

    /// <summary>
    /// `Channel.DayNightTiles` 的显示朝向：
    /// <c>false</c> = 夜晚/雨天显示（雨雾、夜景）；
    /// <c>true</c> = 白天显示（纯白天 Time 变体）。
    /// </summary>
    public bool DayNightVisibleAtDay;
}
