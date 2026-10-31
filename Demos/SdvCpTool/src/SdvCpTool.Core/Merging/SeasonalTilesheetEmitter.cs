using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Normalizing;
using SdvCpTool.Core.Workspace;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// <see cref="SeasonalTilesheetGroup"/> 的发射器：把按季分叉的 <c>EditImage</c>
/// 写成一整套 <c>&lt;季&gt;_X</c> 图集，并把引用它的地图指到春天那份。
/// <para>
/// 消费点：<c>GameLocation.cs:16411-16447</c> —— <c>updateSeasonalTileSheets</c> 对地图的每个
/// <c>TileSheets[].ImageSource</c> 调 <c>GetSeasonalTilesheetName</c>，把文件名开头的
/// <c>spring_</c>/<c>summer_</c>/<c>fall_</c>/<c>winter_</c> <b>替换</b>成当季。
/// 所以只要把地图指到 <c>spring_X</c>，游戏自己按季换 —— <b>零条件</b>。
/// </para>
/// <para>
/// 图集名匹配用的是 SMAPI 的同一套归一（<c>NormalizeTilesheetPathForComparison</c>：
/// 去 <c>Maps/</c> 前缀、去 <c>.png</c>、统一分隔符），不是我们自己拍的。
/// </para>
/// </summary>
public static class SeasonalTilesheetEmitter
{
    private static readonly string[] SeasonOrder = { "Spring", "Summer", "Fall", "Winter" };

    /// <summary>发射结果摘要（每份产物相对路径）。</summary>
    public sealed record Result(List<string> Assets, List<string> Maps);

    /// <summary>
    /// 发射一个组。成功时四份 <c>&lt;季&gt;_X</c> 已写入，且引用该图集的地图已改指春天那份。
    /// <para>任一季写不出、或没有任何地图引用它 ⇒ 整体失败，<b>不留半套</b>。</para>
    /// </summary>
    public static bool TryEmit(OutputTree output, ImageCache images, MapCache maps, string packRoot,
        SeasonalTilesheetGroup group,
        out Result? result, out string? error)
    {
        result = null;
        error = null;
        if (group.Reject != null) { error = group.Reject; return false; }

        // 底图走 `ImageCache`：本包刚 Load/EditImage 过的底图磁盘上还没有。
        string baseRel = AssetCreator.RelativePath(group.Target, ".png");
        if (!images.TryGetPixels(baseRel, out byte[] baseData, out int baseW, out int baseH,
                out string? baseError))
        {
            error = $"找不到底图 `{baseRel}` —— 季节变体要从它出发（同包先 Load 或原版资产）：{baseError}";
            return false;
        }

        // 先把四季都**算出来**再落盘：中途失败就不该留下半套产物。
        var sheets = new List<(string Rel, byte[] Data, int W, int H)>();
        foreach (string season in SeasonOrder)
        {
            byte[] cur = (byte[])baseData.Clone();
            int w = baseW, h = baseH;

            // 缺季 = CP 下该季没有补丁 ⇒ 用底图补一份；四份文件必须齐全，地图才能按季换。
            if (group.BySeason.TryGetValue(season, out List<PatchVerdict>? patches))
            {
                foreach (PatchVerdict v in patches)
                {
                    if (!ApplyOne(images, packRoot, v, ref cur, ref w, ref h, out string? oneError))
                    {
                        error = $"{season}：{oneError}";
                        return false;
                    }
                }
            }
            // 产出树的图片资产一律是 `<资产名>.png`（与 AssetCreator.RelativePath 同一约定）。
            sheets.Add((AssetCreator.RelativePath(Prefixed(group.Target, season), ".png"), cur, w, h));
        }

        // 消费方：必须有地图把这张图当图集。没有就不发射 —— 写了没人指过去，等于凭空多 4 份死资产。
        // 走缓存的图集引用索引：一次运行只扫一遍全树 tbin（52 个组里有 11 个真的扫到）
        IReadOnlyDictionary<string, List<(string Rel, TBin10 Map, string Original, TileSheet Sheet)>> index =
            maps.TileSheetRefIndex(output, NormalizeTilesheet);
        List<(string Rel, TBin10 Map, string Original, TileSheet Sheet)> refs =
            index.TryGetValue(NormalizeTilesheet(group.Target), out var hit) ? hit : new();
        if (refs.Count == 0)
        {
            error = $"没有任何地图把它当 TileSheet 图集引用 —— 季节前缀靠地图的 ImageSource 生效，"
                  + "没人引用就不发射（写了也是死资产）";
            return false;
        }

        var assets = new List<string>();
        var touchedMaps = new List<string>();
        try
        {
            foreach ((string rel, byte[] data, int w, int h) in sheets)
            {
                string? invalid = output.ValidatePath(rel);
                if (invalid != null) { error = $"产物路径非法（{rel}）：{invalid}"; return false; }
                images.SetPixels(rel, data, w, h);
                if (!output.CreatedFiles.Contains(rel, StringComparer.OrdinalIgnoreCase))
                    output.CreatedFiles.Add(rel);
                // 新建的图片资产要配一份 Texture2DReader 的 .config，否则回包不出 xnb。
                AssetCreator.CopyImageConfig(output, rel[..^".png".Length]);

                assets.Add(rel);
            }

            // 地图改指春天那份。**在四份产物都写成功之后**才动地图 —— 顺序反了会留下"指过去但文件不在"。
            string spring = sheets[0].Rel;
            foreach ((string rel, TBin10 map, string original, TileSheet sheet) in refs)
            {
                sheet.Image = Prefixed(original, "Spring");
                // 改完收进地图内存模型，由 `MapCache.Flush()` 统一落盘 —— 这里不直接写文件。
                maps.Set(rel, TBin10Reader.Create(Array.Empty<byte>()), map);
                touchedMaps.Add(rel);
            }
        }
        catch (Exception ex)
        {
            error = $"写入失败：{ex.Message}";
            return false;
        }

        result = new Result(assets, touchedMaps);
        return true;
    }

    /// <summary>把一条 <c>EditImage</c> 合成到当前缓冲区上。</summary>
    private static bool ApplyOne(ImageCache images, string packRoot, PatchVerdict v,
        ref byte[] data, ref int w, ref int h, out string? error)
    {
        error = null;
        string resolved = LoadApplier.ExpandFieldTokens(v.Patch.FromFile ?? "", v.Patch.Target ?? "");
        if (resolved.Contains("{{", StringComparison.Ordinal))
        {
            error = $"FromFile 含无法静态解析的令牌：{v.Patch.FromFile}";
            return false;
        }

        string full;
        try { full = Path.GetFullPath(Path.Combine(packRoot, resolved.Replace('/', Path.DirectorySeparatorChar))); }
        catch (Exception ex) { error = $"FromFile 路径无效：{ex.Message}"; return false; }
        if (!File.Exists(full)) { error = $"找不到 FromFile：{resolved}"; return false; }

        if (!EditImageApplier.TryParseAreas(v.Patch, out ImageArea? fromArea, out ImageArea? toArea,
                out string? areaError))
        {
            error = areaError;
            return false;
        }
        if (!EditImageApplier.TryParseMode(v.Patch.Raw.Value<string>("PatchMode"),
                out PatchImageMode mode, out string? modeError))
        {
            error = modeError;
            return false;
        }

        if (!images.TryReadSource(full, out byte[] source, out int sw, out int sh, out string? srcError))
        {
            error = $"源图读取失败：{srcError}";
            return false;
        }

        ImageArea from = fromArea ?? new ImageArea(0, 0, sw, sh);
        ImageArea to = toArea ?? new ImageArea(0, 0, from.Width, from.Height);

        if (!EditImageApplier.TryCompose(data, w, h, source, sw, sh, from, to, mode,
                out byte[] composed, out int rw, out int rh, out string? composeError))
        {
            error = composeError;
            return false;
        }
        data = composed;
        w = rw;
        h = rh;
        return true;
    }

    /// <summary>把资产名 <c>dir/file</c> 的文件名部分加上季节前缀。</summary>
    private static string Prefixed(string assetName, string season)
    {
        string normalized = assetName.Replace('\\', '/').Trim('/');
        int slash = normalized.LastIndexOf('/');
        string dir = slash < 0 ? "" : normalized[..slash];
        string file = slash < 0 ? normalized : normalized[(slash + 1)..];
        string prefixed = season.ToLowerInvariant() + "_" + file;
        return dir.Length == 0 ? prefixed : dir + "/" + prefixed;
    }

    /// <summary>
    /// 图集名归一 —— <b>照抄 SMAPI</b> 的 <c>NormalizeTilesheetPathForComparison</c>
    /// （<c>StardewModdingAPI.dll</c>）：统一分隔符、去 <c>Maps/</c> 前缀、去 <c>.png</c>。
    /// 自己拍一套会出现「看着一样却匹配不上」。
    /// </summary>
    private static string NormalizeTilesheet(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string p = path.Replace('\\', '/').Trim();
        if (p.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase)) p = p[5..];
        if (p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) p = p[..^4];
        return p;
    }
}
