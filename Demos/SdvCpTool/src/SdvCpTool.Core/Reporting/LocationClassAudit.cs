using System.Text.Json;
using System.Text.Json.Nodes;

namespace SdvCpTool.Core.Reporting;

/// <summary>
/// 产出树自检：<c>Data/Locations</c> 里**哪些地点的类来自 mod 的程序集**
/// （<c>CreateOnLoad.Type</c> 写成 <c>类型, 程序集</c>，而那个程序集不是游戏自己的）。
///
/// <para>
/// <b>为什么要它</b>：纯 XNB 装机里**没有那个程序集** ⇒ 游戏建地点时直接抛
/// <c>Invalid type for location '&lt;名&gt;': &lt;类型&gt;, &lt;程序集&gt;</c> ⇒
/// **地点根本创建不出来**（实机日志：`Couldn't create the 'Custom_CapeKeyMaze' location.
/// Is its data in Data/Locations invalid?`）。后果不只是"那个地点进不去" ——
/// 它的地图、从别处指向它的传送、挂在它下面的内容**全都到不了**。
/// 而这一类以前在报告里是**静默**的（条目原样写出去、没有任何提示）。
/// </para>
///
/// <para>
/// ⚠ <b>只报不改</b>：把 <c>Type</c> 删掉确实能让地点以普通 <c>GameLocation</c> 建起来、
/// 地图也就到得了 —— 但那个类承载的行为（伤害 / 传送 / 生成逻辑，实测是 Cape Stardew 的
/// 岩浆洞逻辑）会一起消失，可能比"地点不存在"更糟（甚至可能没有出口）⇒
/// 这是**取舍**，不做默认行为。报告里说清楚是哪几个地点、缺的是哪个程序集，由装机的人决定。
/// </para>
///
/// <para>
/// 只扫**中性**的 <c>Data/Locations.json</c>：语言变体（<c>Data/Locations.zh-CN.json</c>…）
/// 是同一份条目的翻译，扫它们只会把同一处问题报 11 遍。
/// </para>
/// </summary>
public static class LocationClassAudit
{
    /// <summary>游戏自己的程序集 —— 写成这些的（含不写程序集的）都当"游戏类"放行。</summary>
    private static readonly HashSet<string> GameAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "StardewValley", "Stardew Valley", "StardewValley.GameData",
    };

    /// <summary>一处「地点的类来自 mod 程序集」。</summary>
    public sealed record Finding(string Location, string Type, string Assembly, string? MapPath);

    public sealed class Audit
    {
        /// <summary>看过多少个地点条目。</summary>
        public int CheckedLocations;

        public readonly List<Finding> Findings = new();
    }

    /// <summary>
    /// 对一棵产出树的 <c>Data/Locations.json</c> 跑自检。**不需要基准树** ——
    /// 原版条目一律是游戏类（`StardewValley.Locations.Town` 这种不带程序集的写法），
    /// 所以这里任何"带 mod 程序集"的条目都是本轮引进的。
    /// </summary>
    public static Audit Run(string outputRoot)
    {
        var audit = new Audit();
        string path = Path.Combine(outputRoot, "Data", "Locations.json");
        if (!File.Exists(path)) return audit;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return audit;   // 读不了不是这条自检的事（别的检查会报）
        }
        if (root is not JsonObject locations) return audit;

        foreach ((string name, JsonNode? node) in locations)
        {
            if (node is not JsonObject entry) continue;
            audit.CheckedLocations++;

            string? type = entry["CreateOnLoad"]?["Type"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(type)) continue;

            // 只有写成 `类型, 程序集` 的才谈得上"来自哪个程序集"；不写的都是游戏类。
            int comma = type.LastIndexOf(',');
            if (comma < 0) continue;

            string assembly = type[(comma + 1)..].Trim();
            if (assembly.Length == 0 || GameAssemblies.Contains(assembly)) continue;

            audit.Findings.Add(new Finding(name, type[..comma].Trim(), assembly,
                entry["CreateOnLoad"]?["MapPath"]?.GetValue<string>()));
        }

        audit.Findings.Sort((a, b) => string.CompareOrdinal(a.Location, b.Location));
        return audit;
    }
}
