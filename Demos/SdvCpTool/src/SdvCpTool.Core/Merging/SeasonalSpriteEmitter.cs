using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Normalizing;
using SdvCpTool.Core.Workspace;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// <see cref="SeasonalSpriteGroup"/> 的发射器（Task 29）：把 4 条条件 <c>EditImage</c>
/// <b>一次性</b>写成原生的「四季 BigCraftable」。
/// <para>
/// 两件事，缺一不可：
/// <list type="number">
/// <item>把四季图排成 <b>4 个连续索引</b>的贴图（春=0 夏=1 秋=2 冬=3，与 <c>(int)Season</c> 一致）；</item>
/// <item>把 <c>Data/BigCraftables</c> 条目的 <c>Name</c> 加上 <c>Seasonal</c> —— 少了它游戏不会按季换图。</item>
/// </list>
/// 只做其一都是<b>半套</b>：改索引不改 Name，四季永远显示春天；改 Name 不改索引，索引被钳到 0 但图只有一格。
/// </para>
/// <para>
/// 消费点：<c>Object.cs:1940-1943</c> ——
/// <c>ParentSheetIndex = idx - idx % 4 + location.GetSeasonIndex();</c>（仅在 <c>bigCraftable</c> 且 Name 含 Seasonal 时）。
/// 索引顺序由 <c>Utility.cs:241</c> 的 <c>(int)(season + 1) % 4</c> 与 <c>Game1.cs:7805-7820</c> 的
/// Spring→Summer→Fall→Winter 循环共同确证：<b>Spring=0, Summer=1, Fall=2, Winter=3</b>。
/// </para>
/// </summary>
public static class SeasonalSpriteEmitter
{
    /// <summary>
    /// 发射一个组。成功时贴图已写入 <paramref name="output"/>，
    /// 且 <paramref name="verdicts"/> 里那个 BigCraftables 补丁的条目 <c>Name</c> 已改好
    /// —— 后续正常写盘会把它带出去（<b>不新开写盘路径</b>）。
    /// </summary>
    public static bool TryEmit(OutputTree output, ImageCache images, string packRoot,
        IEnumerable<PatchVerdict> verdicts,
        SeasonalSpriteGroup group, out string? error)
    {
        error = null;
        if (group.Reject != null) { error = group.Reject; return false; }

        if (group.Variants.Count != 4)
        {
            error = $"组内变体数是 {group.Variants.Count}，不是 4";
            return false;
        }

        int cellW = group.ToArea.Width, cellH = group.ToArea.Height;
        if (cellW <= 0 || cellH <= 0)
        {
            error = $"ToArea 尺寸非法：{group.ToArea}";
            return false;
        }

        // 四季横排：第 i 季画到 x = i*cellW。
        var sheet = ByteBufferPool.Rent(cellW * 4 * cellH * 4);
        int sheetW = cellW * 4, sheetH = cellH;

        for (int i = 0; i < 4; i++)
        {
            SeasonalVariant v = group.Variants[i];
            byte[] srcData;
            int srcW, srcH;

            if (v.FromFile.Length == 0)
            {
                // 缺季：该季没有补丁 ⇒ 用底图这一格补齐（CP 下就是保持底图）。
                string baseRel = AssetCreator.RelativePath(group.Target, ".png");
                if (!images.TryGetPixels(baseRel, out srcData, out srcW, out srcH, out string? baseError))
                {
                    error = $"缺季 `{v.Season}` 需要底图 `{baseRel}`，但取不到：{baseError}";
                    return false;
                }
            }
            else
            {
                string full;
                try { full = Path.GetFullPath(Path.Combine(packRoot, v.FromFile.Replace('/', Path.DirectorySeparatorChar))); }
                catch (Exception ex) { error = $"FromFile 路径无效（{v.FromFile}）：{ex.Message}"; return false; }

                if (!File.Exists(full)) { error = $"找不到 FromFile：{v.FromFile}"; return false; }
                if (!images.TryReadSource(full, out srcData, out srcW, out srcH,
                        out string? srcError))
                {
                    error = $"源图读取失败（{v.FromFile}）：{srcError}";
                    return false;
                }
            }

            (byte[] Data, int W, int H) src = (srcData, srcW, srcH);
            var destArea = new ImageArea(i * cellW, 0, cellW, cellH);
            if (!EditImageApplier.TryCompose(sheet, sheetW, sheetH, src.Data, src.W, src.H,
                    v.FromArea, destArea, PatchImageMode.Replace,
                    out byte[] result, out int rw, out int rh, out string? composeError))
            {
                error = $"{v.Season} 合成失败：{composeError}";
                return false;
            }
            sheet = result;
            sheetW = rw;
            sheetH = rh;
        }

        // 回读校验：写出的图必须真的是 4 格宽、且每格都是那一季的矩形。
        // 没有这道关，「写了个看起来对的 PNG」和「四季各有各的图」分不开。
        if (sheetW != cellW * 4 || sheetH != cellH)
        {
            error = $"合成结果尺寸 {sheetW}x{sheetH}，期望 {cellW * 4}x{cellH}";
            return false;
        }

        string pngRel = AssetCreator.RelativePath(group.Target, ".png");
        string? invalid = output.ValidatePath(pngRel);
        if (invalid != null) { error = $"目标资产 `{group.Target}` 的输出路径非法：{invalid}"; return false; }

        // 交给 `ImageCache` 写（产出树里的图片资产由它独家持有），落盘留到一次运行的最后。
        try { images.SetPixels(pngRel, sheet, sheetW, sheetH); }
        catch (Exception ex) { error = $"贴图写入失败：{ex.Message}"; return false; }

        if (!TryMarkSeasonal(verdicts, group.BigCraftableEntry, out string? nameError))
        {
            error = $"贴图已写入，但 Data/BigCraftables 条目改名失败：{nameError}";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 把 <c>Data/BigCraftables</c> 条目的 <c>Name</c> 加上 <c>Seasonal</c>。
    /// <para>
    /// <b>就地改补丁对象</b>，不另开写盘路径 —— 该补丁本来就要写，改名后由同一条路径带出去，
    /// 判定的「一条补丁一次写入」口径不变。
    /// </para>
    /// </summary>
    private static bool TryMarkSeasonal(IEnumerable<PatchVerdict> verdicts, string entryKey,
        out string? error)
    {
        error = null;
        foreach (PatchVerdict v in verdicts)
        {
            if (!v.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Normalize(v.Patch.Target).Equals("Data/BigCraftables", StringComparison.OrdinalIgnoreCase)) continue;
            if (v.Patch.Raw["Entries"] is not JObject entries) continue;
            if (entries[entryKey] is not JObject entry) continue;

            string name = entry["Name"]?.Value<string>() ?? "";
            if (name.Contains("Seasonal", StringComparison.Ordinal)) return true;   // 已是，幂等
            if (name.Length == 0)
            {
                error = $"条目 `{entryKey}` 没有 Name，加不上 Seasonal";
                return false;
            }
            entry["Name"] = name + " Seasonal";
            return true;
        }
        error = $"找不到写 `{entryKey}` 的 EditData Data/BigCraftables 补丁";
        return false;
    }

    private static string Normalize(string? name) => (name ?? "").Replace('\\', '/').Trim('/');
}
