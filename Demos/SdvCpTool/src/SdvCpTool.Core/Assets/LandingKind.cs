namespace SdvCpTool.Core.Assets;

/// <summary>目标资产承载条件的位置。</summary>
public enum LandingKind
{
    /// <summary>没有任何可承载条件的位置。</summary>
    None,

    /// <summary>条目里有 `Condition` 字段，条件可原样放入。</summary>
    EntryConditionField,

    /// <summary>键形如 `&lt;事件ID&gt;/&lt;前置条件&gt;/...`，条件可翻译成键里的一段。</summary>
    EntryKeyConditions,

    /// <summary>对话键，语法受限（季节/地点/好感/topic），不支持任意 GSQ。</summary>
    DialogueKey,

    /// <summary>
    /// 对话值本身就是原生 <c>Dialogue</c> 的宿主，条件可内联成
    /// <c>$query &lt;GSQ&gt;#&lt;真分支&gt;|&lt;假分支&gt;</c>（原版正式机制）。
    /// 只对逐条查过调用链的宿主生效（清单见 <c>Assets/ConditionLanding.ClassifyDialogueQueryHost</c>），见 <c>Merging/DialogueQueryWriter</c>。
    /// </summary>
    DialogueQuery,

    /// <summary>
    /// 键名本身就编码了条件维度（如 <c>&lt;NPC&gt;_spouse</c> / <c>_yN</c> / <c>_Male</c> / <c>&lt;season&gt;_</c> 前缀），
    /// 由游戏按状态选键读取。条件若被键名完全蕴含可删条件直接写该键；否则交人工。
    /// </summary>
    KeyEncodedVariant,

    /// <summary>形状认不出来，降级人工判断。</summary>
    Unknown
}
