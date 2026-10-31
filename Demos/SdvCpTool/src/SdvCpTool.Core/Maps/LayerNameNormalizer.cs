using System.Globalization;
using System.Text.RegularExpressions;
using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Maps;

/// <summary>
/// 把 TMX 里的**自定义图层名**归一成原版会渲染的名字。
///
/// <para>
/// 原版 <c>GameLocation.SortLayers()</c> 并不是只认 8 个固定图层：它把
/// <c>Back</c>/<c>Buildings</c>/<c>Front</c>/<c>AlwaysFront</c> 后面**跟整数**的图层
/// 全部收进对应桶里，按数字升序画（<c>Back</c>=0、<c>Back2</c>=2、<c>Buildings-1</c>=-1…）。
/// 也就是说 <c>AlwaysFront4</c>、<c>Buildings3</c> 这类图层**原版本来就会画**。
/// </para>
/// <para>
/// 但 TMXL/PyTK 的图通常写成 <c>Back+1</c>、<c>Always Front</c>、<c>Buildings 2</c>，
/// 或任意名字 + <c>Draw/DrawAbove/DrawBefore</c> 属性 —— 这些后缀原版解析不了
/// （<c>int.TryParse("+1")</c> 失败），于是**整层不画**（业主报的云雾层就是这一类）。
/// 这里在落盘前把它们改名成 <c>锚点+整数</c>，并保住相对顺序。
/// </para>
/// </summary>
public static class LayerNameNormalizer
{
    private static readonly string[] Anchors = { "AlwaysFront", "Buildings", "Front", "Back" }; // 长的在前，防 "Front" 先匹配 "AlwaysFront"

    private static readonly Regex Named = new(
        @"^(?<anchor>AlwaysFront|Buildings|Front|Back)(?<order>[+-]?\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>这个图层名原版会不会画（裸锚点或锚点 + 整数）。</summary>
    public static bool IsRenderable(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        foreach (string anchor in Anchors)
        {
            if (!id.StartsWith(anchor, StringComparison.Ordinal)) continue;
            string rest = id[anchor.Length..];
            if (rest.Length == 0) return true;
            return int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }
        return false;
    }

    /// <summary>就地改名；返回改了几个图层。</summary>
    public static int Normalize(TBin10 map)
    {
        if (map.Layers == null) return 0;

        var used = new HashSet<string>(
            map.Layers.Where(l => l?.Id != null).Select(l => l!.Id), StringComparer.Ordinal);
        var orders = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (string anchor in Anchors) orders[anchor] = CollectOrders(map, anchor);

        int renamed = 0;
        foreach (Layer layer in map.Layers)
        {
            if (layer?.Id == null || IsRenderable(layer.Id)) continue;
            if (!TryInfer(layer, out string anchor, out int desired, out bool before)) continue;

            int target = FindFree(anchor, desired, before, used, orders[anchor]);
            string newId = target == 0 ? anchor : anchor + target.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(newId, layer.Id, StringComparison.Ordinal)) continue;

            used.Remove(layer.Id);
            layer.Id = newId;
            used.Add(newId);
            orders[anchor].Add(target);
            renamed++;
        }
        return renamed;
    }

    private static SortedSet<int> CollectOrders(TBin10 map, string anchor)
    {
        var set = new SortedSet<int>();
        foreach (Layer layer in map.Layers)
        {
            if (layer?.Id == null || !layer.Id.StartsWith(anchor, StringComparison.Ordinal)) continue;
            string rest = layer.Id[anchor.Length..];
            if (rest.Length == 0) set.Add(0);
            else if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                set.Add(n);
        }
        return set;
    }

    /// <summary>
    /// 推断这个图层想画在哪个锚点、哪个相对位置。
    /// <list type="bullet">
    /// <item>有 <c>DrawBefore=锚点</c> ⇒ 锚点层之前；<c>Draw=锚点</c>/<c>DrawAbove=锚点</c> ⇒ 之后；</item>
    /// <item>没有属性时按名字里的锚点 + 数字（<c>Back+1</c>=1、<c>Back 2</c>=2、<c>Always Front</c>=0）；</item>
    /// <item>两样都对不上 ⇒ 不碰（<c>Set-Up</c>/<c>Schedule</c>/<c>MainEvent</c> 这类数据层）。</item>
    /// </list>
    /// </summary>
    private static bool TryInfer(Layer layer, out string anchor, out int desired, out bool before)
    {
        anchor = "";
        desired = 0;
        before = false;

        string? prop = Prop(layer, "DrawBefore");
        if (prop != null && TryParseAnchor(prop, out anchor))
        {
            before = true;
            desired = 0;
            return true;
        }

        prop = Prop(layer, "Draw") ?? Prop(layer, "DrawAbove");
        if (prop != null && TryParseAnchor(prop, out anchor))
        {
            before = false;
            desired = 0;
            return true;
        }

        // 名字里的空格/下划线只是作者习惯（`Always Front`、`Buildings 3`）—— 先去掉再匹配。
        string compact = layer.Id.Replace(" ", "").Replace("_", "").Trim();
        Match m = Named.Match(compact);
        if (!m.Success) return false;
        anchor = Canonical(m.Groups["anchor"].Value);
        string order = m.Groups["order"].Value;
        desired = order.Length == 0
            ? 0
            : int.Parse(order, NumberStyles.Integer, CultureInfo.InvariantCulture);
        before = desired < 0;
        return true;
    }

    private static string? Prop(Layer layer, string key) =>
        layer.Properties?.FirstOrDefault(p => p.Key == key)?.Value?.ToString();

    private static bool TryParseAnchor(string? value, out string anchor)
    {
        anchor = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        string trimmed = value.Trim();
        string merged = trimmed.Replace(" ", "").Replace("_", "");
        foreach (string candidate in Anchors)
        {
            if (merged.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                anchor = candidate;
                return true;
            }
        }
        return false;
    }

    private static string Canonical(string raw)
    {
        foreach (string anchor in Anchors)
            if (raw.Equals(anchor, StringComparison.OrdinalIgnoreCase)) return anchor;
        return raw;
    }

    /// <summary>找一个没被占用的整数后缀，尽量靠近 <paramref name="desired"/>，方向由 before 决定。</summary>
    private static int FindFree(string anchor, int desired, bool before,
        HashSet<string> used, SortedSet<int> usedOrders)
    {
        int candidate = desired;
        if (candidate == 0) candidate = before ? usedOrders.Min == 0 ? -1 : usedOrders.Min - 1
                                               : usedOrders.Count == 0 ? 1 : usedOrders.Max + 1;
        while (true)
        {
            string id = candidate == 0 ? anchor : anchor + candidate.ToString(CultureInfo.InvariantCulture);
            if (candidate != 0 && !used.Contains(id)) return candidate;
            candidate += before ? -1 : 1;
        }
    }
}
