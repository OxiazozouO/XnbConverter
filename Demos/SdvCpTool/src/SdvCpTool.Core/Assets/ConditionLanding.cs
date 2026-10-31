using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// <c>$query</c>（对话值内联 GSQ）能生效的宿主类别。逐条查过调用链，值最终都会走
/// <c>new Dialogue(...)</c>；其余路径（<c>DialogueBox(string)</c>、<c>LetterViewerMenu</c>）
/// 里 <c>$query</c> 是<b>死文本</b>。
/// <para>
/// <b>怎么查的（方法要写明，否则下次没法复核）</b>：把 <c>new Dialogue(</c> / <c>Dialogue.TryGetDialogue(</c>
/// 的实参字面量抓出来统计，与 <c>drawObjectDialogue(</c> 的<b>分开</b>数 —— 后者走
/// <c>DialogueBox(string)</c>，不算数。两种数法的对照见
/// <c>docs/research/2026-10-06-condition-consumers.md</c>。
/// </para>
/// <para>
/// ⚠ <b>刻意不收 <c>Strings/Characters</c>、<c>Strings/Locations</c>、<c>Strings/UI</c> 等</b>：
/// 它们在**两条路上都出现**（<c>Strings/Characters</c> 实测 <c>new Dialogue</c> 18 处、
/// <c>drawObjectDialogue</c> 24 处）—— 同一个资产里**有的键走活路、有的走死路**，
/// 静态分不出某个键走哪条 ⇒ 不能当整片宿主。
/// </para>
/// </summary>
public enum DialogueQueryHost
{
    None,

    /// <summary><c>Characters/Dialogue/&lt;NPC&gt;</c> 的值（<c>NPC.cs:3961-3970</c>）。</summary>
    DialogueAsset,

    /// <summary><c>Strings/schedules/&lt;NPC&gt;</c> 的日程结束语（<c>NPC.cs:4356-4381</c>）。</summary>
    Schedules,

    /// <summary><c>Data/Festivals/&lt;id&gt;</c> 里以 NPC 名为键的对话（<c>Event.cs:4398-4412</c>）。</summary>
    Festivals,

    /// <summary><c>Strings/StringsFromCSFiles</c> 的 <c>Event.cs.1527</c>（<c>Event.cs:12559-12561</c>）。</summary>
    StringsFromCSFiles,

    /// <summary>
    /// <c>Data/ExtraDialogue</c> —— 值只经 <c>new Dialogue(...)</c> 读
    /// （<c>GameLocation.cs:8483</c>/<c>:8487</c>/<c>:8491</c>/<c>:10000</c>；
    /// <c>Game1.cs:8929</c> 走 <c>NPC.addExtraDialogue</c>）。
    /// </summary>
    ExtraDialogue,

    /// <summary>
    /// <c>Data/EngagementDialogue</c> —— 值只经 <c>new Dialogue(this, "Data\EngagementDialogue:…")</c>
    /// 读（<c>NPC.cs:1660</c>/<c>:1681</c>/<c>:3848</c>/<c>:3852</c>）。
    /// </summary>
    EngagementDialogue,
}

/// <summary>探测目标资产在哪承载条件。</summary>
public static class ConditionLanding
{
    private const string DialogueQueryDialoguePrefix = "Characters/Dialogue/";
    private const string DialogueQuerySchedulesPrefix = "Strings/schedules/";
    private const string DialogueQueryFestivalsPrefix = "Data/Festivals/";
    private const string DialogueQueryStringsFromCSFiles = "Strings/StringsFromCSFiles";

    /// <summary>
    /// <c>$query</c> 宿主的<b>唯一</b>识别处（<see cref="DialogueQueryHost"/>）。
    /// 判定、写入与单测都调它，绝不各写一份路径判断。
    /// </summary>
    public static DialogueQueryHost ClassifyDialogueQueryHost(string assetName)
    {
        string target = (assetName ?? "").Replace('\\', '/').Trim('/');

        foreach ((string prefix, DialogueQueryHost kind) in new[]
                 {
                     (DialogueQueryDialoguePrefix, DialogueQueryHost.DialogueAsset),
                     (DialogueQuerySchedulesPrefix, DialogueQueryHost.Schedules),
                     (DialogueQueryFestivalsPrefix, DialogueQueryHost.Festivals),
                 })
        {
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && target.Length > prefix.Length
                && !target[prefix.Length..].Contains('/'))
                return kind;
        }

        if (target.Equals(DialogueQueryStringsFromCSFiles, StringComparison.OrdinalIgnoreCase))
            return DialogueQueryHost.StringsFromCSFiles;
        if (target.Equals("Data/ExtraDialogue", StringComparison.OrdinalIgnoreCase))
            return DialogueQueryHost.ExtraDialogue;
        if (target.Equals("Data/EngagementDialogue", StringComparison.OrdinalIgnoreCase))
            return DialogueQueryHost.EngagementDialogue;
        return DialogueQueryHost.None;
    }

    public static LandingKind Probe(string filePath, string assetName) =>
        Probe(filePath, assetName, out _);

    /// <summary>
    /// 同上，另把<b>解析好的内容</b>交给调用方 —— 调用方往往还要拿它做落点核对
    /// （<c>ConditionWriter.RefusalFor</c>），重新读一遍文件既浪费又可能与这里读到的不是同一份。
    /// 非 <c>.json</c> 或解析失败时 <paramref name="root"/> 为 <c>null</c>。
    /// </summary>
    public static LandingKind Probe(string filePath, string assetName, out JToken? root)
    {
        root = null;
        string ext = Path.GetExtension(filePath);
        if (!ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return LandingKind.None;

        try
        {
            root = JToken.Parse(File.ReadAllText(filePath));
        }
        catch (JsonException)
        {
            return LandingKind.Unknown;
        }

        return ProbeToken(root, assetName);
    }

    /// <summary>
    /// 直接在已拿到的数据上探测条件落点。新建资产在资源树里没有文件可读 ——
    /// 用补丁将要写入的条目结构（<c>Entries</c>/<c>Fields</c>）作为该资产的内容来探测，
    /// 与 <see cref="Probe"/> 共用同一套判据。
    /// </summary>
    public static LandingKind ProbeToken(JToken root, string assetName)
    {
        // 必须在判断是不是 JObject **之前**做这一步：
        // Data/TriggerActions 等资产是 List<T>，顶层是 JSON 数组，而它们的条目带 Condition 字段。
        if (HasConditionFieldAnywhere(root))
            return LandingKind.EntryConditionField;

        if (root is not JObject obj || !obj.Properties().Any())
            return LandingKind.None;

        if (AllValuesAreStrings(obj))
        {
            // 顺序要紧：先按路径识别对话资产。反过来的话，一个恰好带斜杠键的对话文件会被
            // 判成 EntryKeyConditions（会导致"可转换"的危险方向），而不是 DialogueKey（人工复核）。
            if (IsUnderPath(assetName, "Characters/Dialogue"))
                return LandingKind.DialogueKey;

            // 事件键判据（`/`）只对 Data/Events 那类语法成立，排在这之后，互不干扰。
            if (LooksLikeEventKeys(obj))
                return LandingKind.EntryKeyConditions;

            // 字符串字典 + 键名命中已确证的维度（`_spouse` / `_yN` / `_Male` / `<season>_` 前缀…）
            // → 条件可能就编码在键里，交给「键即条件」规则判定能不能删条件直写该键。
            if (obj.Properties().Any(p => KeyEncodedVariants.Hits(p.Name)))
                return LandingKind.KeyEncodedVariant;

            // 字符串字典，但键维度**一个都没认出来**。AllValuesAreStrings 只证明"这是个字符串字典"，
            // 不证明"没有键维度" —— 我们不知道它有没有，于是**不能**断言"无处承载条件"。
            // None 的语义被上层映射成 NoLandingPoint（断言无处承载），必须只留给"确证没有落点"的情形
            // （非 .json、空对象）。这里返回 Unknown → 上层映射成 NeedsReview：不知道就说不知道。
            return LandingKind.Unknown;
        }

        // 结构认不出来 → 交人工判断。不要伪装成"确定没有落点"：上层把 None 映射为 NoLandingPoint
        // （断言无处承载），把 Unknown 映射为 NeedsReview。
        return LandingKind.Unknown;
    }

    /// <summary>
    /// 原版「条件字段」的确切名字 —— <b>逐条核对过是 GSQ</b>（由 <c>GameStateQuery.CheckConditions</c> 消费）。
    /// <para>
    /// <b>为什么是白名单，而不是「以 <c>Condition</c> 结尾」</b>：原版里就有名字带 <c>Condition</c>
    /// 却<b>不是</b> GSQ 的 —— <c>IgnoreUnlockConditions</c> 是布尔旁路开关，
    /// 收进来会写出**语义错误**的产物。<b>宁少勿多</b>：没核对过的一律不收。
    /// </para>
    /// <para>
    /// <b>⚠ 名字里没有 <c>Condition</c> 字样、却确实是 GSQ 的字段</b>（按名字筛会全漏）：
    /// <c>Data/Characters</c> 的 <c>CanSocialize</c> / <c>CanVisitIsland</c> /
    /// <c>ItemDeliveryQuests</c> / <c>WinterStarParticipant</c> / <c>SpouseGiftJealousy</c> /
    /// <c>SpouseWantsChildren</c> / <c>SpouseAdopts</c>，
    /// 以及任意 spawn-item 条目上的 <c>PerItemCondition</c>。
    /// 依据：<c>docs/research/2026-10-06-condition-consumers.md</c>（逐点附源码行号）。
    /// </para>
    /// <para>
    /// <b>确认<b>不收</b>的两个</b>（都在台账里核实过，理由不同）：
    /// <c>WeatherConditions</c> 是 <b>容器</b>不是条件串（GSQ 在它的元素里的 <c>Condition</c>）；
    /// <c>IgnoreUnlockConditions</c> 是 <b>bool 旁路开关</b>。
    /// </para>
    /// </summary>
    public static readonly string[] ConditionFieldNames =
    {
        "Condition",
        "TriggerCondition",                   // Data/IncomingPhoneCalls
        "RingCondition",                      // Data/IncomingPhoneCalls
        "UnlockCondition",
        "UnlockConditions",
        "UnlockedCondition",
        "BuildCondition",
        "BuildConditions",
        "SkipPermanentlyCondition",
        "DefaultMusicCondition",
        "InvalidItemMessageCondition",
        // ── 下面这些名字里**没有** `Condition` 字样，但确实是 GSQ ──────────────
        "CanSocialize",                       // Data/Characters（NPC.cs:4876、Quests/ItemDeliveryQuest.cs:89）
        "CanVisitIsland",                     // Data/Characters
        "ItemDeliveryQuests",                 // Data/Characters
        "WinterStarParticipant",              // Data/Characters（Utility.cs:6016）
        "SpouseGiftJealousy",                 // Data/Characters
        "SpouseWantsChildren",                // Data/Characters
        "SpouseAdopts",                       // Data/Characters（NPC.cs:6317）
        "PerItemCondition",                   // ISpawnItemData（Internal/ItemQueryResolver.cs:767）
        "ClearContentsOvernightCondition",
        "KnownCondition",
    };

    private static bool HasConditionFieldAnywhere(JToken token)
    {
        switch (token)
        {
            case JObject o:
                if (o.Properties().Any(p => ConditionFieldNames.Contains(p.Name, StringComparer.Ordinal)))
                    return true;
                return o.Properties().Any(p => HasConditionFieldAnywhere(p.Value));
            case JArray a:
                return a.Any(HasConditionFieldAnywhere);
            default:
                return false;
        }
    }

    private static bool AllValuesAreStrings(JObject obj) =>
        obj.Properties().All(p => p.Value.Type == JTokenType.String);

    /// <summary>事件键形如 `123/t 600 1200` 或 `123/e 456`：至少一半的键含 `/`，且 `/` 前为非空段。</summary>
    private static bool LooksLikeEventKeys(JObject obj)
    {
        int total = obj.Properties().Count();
        int withSlash = 0;
        foreach (JProperty p in obj.Properties())
        {
            if (p.Name.IndexOf('/') > 0) withSlash++;   // > 0 保证 `/` 前非空
        }
        return withSlash > 0 && withSlash * 2 >= total;
    }

    private static bool IsUnderPath(string assetName, string prefix)
    {
        string normalized = assetName.Replace('\\', '/').Trim('/');
        return normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
