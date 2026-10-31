using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Reporting;

/// <summary>
/// 一轮自检里，同一份文件只解析一次。
///
/// <para>
/// 几道自检都是「扫全树 <c>.json</c> → 逐份解析 → 看结构」，各自把整棵树解析一遍纯属重复：
/// <c>ConditionPlacementAudit</c> 与 <c>OutputShapeAudit</c> 都要「产出 + 原版」两份都解析，
/// 加起来就是 4 遍全树解析。实测 SVE 包这两道各 4 秒上下。
/// </para>
///
/// <para>
/// ⚠ **只许读**：缓存里的 token 是共享的，谁就地改了它，后面的自检就会看到被改过的内容。
/// 目前引用它的都是只读分析（形状、条件落点），没有任何就地修改。
/// </para>
///
/// <para>
/// ⚠ 只在**同一个进程内、文件不再变**的窗口里用：本工具一道自检跑完才跑下一道，
/// 中间没有写入，所以不需要按修改时间判新鲜度。跨包是新进程，天然不会串。
/// </para>
/// </summary>
public sealed class AuditParseCache
{
    private readonly Dictionary<string, JToken?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>解析一份文件；读不出或不是 JSON 时返回 <c>null</c>（与各处的 <c>TryParse</c> 同义）。</summary>
    public JToken? Parse(string fullPath)
    {
        if (_cache.TryGetValue(fullPath, out JToken? hit)) return hit;

        JToken? parsed;
        try { parsed = JToken.Parse(File.ReadAllText(fullPath)); }
        catch (Exception) { parsed = null; }   // 非 JSON（.config 等）或损坏的跳过

        _cache[fullPath] = parsed;
        return parsed;
    }
}
