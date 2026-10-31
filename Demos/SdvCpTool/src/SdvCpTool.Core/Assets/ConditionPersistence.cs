using SdvCpTool.Core.Conditions;

namespace SdvCpTool.Core.Assets;

/// <summary>
/// **持久型条件**：一旦成立就不再变（`HasFlag` / `HasSeenEvent` / `FarmType` / `Year` …）。
/// <para>
/// <b>为什么需要它</b>：静态 XNB 的地图**没有条件化 Warp**（`GameLocation.cs` 里 `Warp` 那几处都不查 GSQ），
/// 唯一的替代是**无条件落图 + 图块门禁** —— `ConditionalDoor &lt;GSQ&gt;`（`GameLocation.cs:3907/9549`：
/// GSQ 为假 ⇒ 玩家被弹回），也就是「看得见、进不去」。
/// </para>
/// <para>
/// ⚠ 这条替代**只在"同一片区域不会有两个互斥变体"时成立**。持久型天然满足（同一片区域只会"由无到有"）；
/// 而**轮流型**（`Weather` / `Time` / `Season` / `Day` / `DayOfWeek` / `Random` / `LocationName` …）
/// 落到同一区域是**互斥变体** —— 无条件全写会互相覆盖 ✗ ⇒ 那些维持原判（只报不写）。
/// </para>
/// </summary>
public static class ConditionPersistence
{
    /// <summary>
    /// 持久型的条件键（CP 的名字；大小写不敏感）。
    /// ⚠ 加一条要想清楚「它会不会**变回去**」——会变的（`Spouse` 离婚、`Friendship` 掉好感）
    /// 不是持久型，别放进来。
    /// </summary>
    /// <summary>
    /// **轮流型**（每次上下文更新都可能变，且**同一片区域的多个变体是互斥的**）——
    /// 这些不能走"无条件落图 + 门禁"（全写会互相覆盖 ✗）。
    /// <para>
    /// ⚠ **用黑名单而不是白名单**：配置键（`Enablejibb.IF2R` 这种）在离线是**冻结值**，
    /// 只会产生一个变体 ⇒ 不构成"互斥变体"⇒ 不该拦。而条件名是开放集合（各框架都能定义），
    /// 白名单必然漏 ⇒ 漏的后果是"该拦的没拦"。
    /// </para>
    /// </summary>
    private static readonly HashSet<string> VolatileKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // 时间/天气/季节：同一区域会各写一份（互斥变体）
        "Weather", "WeatherToday", "Time", "Season", "Day", "DayOfWeek", "DayEvent",
        "SeasonDay", "IsGreenRainDay", "Random", "RandomNumber",
    };

    /// <summary>
    /// 门禁通道允许的最大改动面积（图块数）。
    /// <para>
    /// **为什么要有上限**：门禁是逐格写 `TouchAction=ConditionalDoor`，踩上去条件为假就弹回。
    /// 小改动（一个矿车、一段楼梯）没问题；但整片区域的变体（实测 Frontier Farm 有一条
    /// `ToArea` = 24336 格的整图变体）会在条件不成立时把**原有通路一起挡住** ——
    /// 玩家不是"进不去新区域"，而是**整张图都不能走**。
    /// </para>
    /// <para>
    /// 语料实测（2026-10-18，497 条走门禁的补丁）：门禁格数中位数 **3**、75 分位 40、
    /// 90 分位 216；整图级的是极少数。64 保留约 79% 的补丁、总共只有 3240 格，
    /// 又足以挡掉那些"整片变体"。超过上限的**不再无条件照写**：先求该条件在**新存档初始状态**
    /// 下的真假 —— 初始成立才写；初始不成立/判不了的保留初始地图、不提前烘焙。
    /// 依据：实测 SVE 的 Shearwater 桥翻新（`HasSeenEvent: 2000009`）曾被无条件写进初始地图，
    /// 事件还没发生翻新桥面就出现了。纯 XNB 没有原版可承载的大范围运行时换图通道
    /// （原版只有少量硬编码 `ApplyMapOverride`，如社区中心修复）⇒ 这类补丁只能放弃后期变体。
    /// </para>
    /// </summary>
    public const int MaxDoorGateCells = 64;

    /// <summary>这个条件键是不是**轮流型**（会变、且变体互斥）。</summary>
    public static bool IsVolatile(string? keyName) =>
        keyName != null && VolatileKeys.Contains(keyName);

    /// <summary>
    /// 整棵树里**没有一个是轮流型**（`All`/`Any`/`Not`/`Literal` 是结构，不算叶子）。
    /// <para>空树（无条件）返回 <c>false</c> —— 无条件的地图改动走的是另一条路，不在这里判。</para>
    /// </summary>
    public static bool AllLeavesStable(WhenNode? node)
    {
        if (node == null) return false;
        bool sawKey = false;
        foreach (WhenNode n in node.Descendants())
        {
            if (n.Kind != WhenNodeKind.Key) continue;
            sawKey = true;
            if (IsVolatile(n.KeyName)) return false;
        }
        return sawKey;
    }

}
