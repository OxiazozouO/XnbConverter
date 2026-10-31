using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Normalizing;
using SdvCpTool.Core.Workspace;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// <see cref="AppearanceGroup"/> 的发射器：把按条件换立绘/行走图的 <c>EditImage</c>
/// 写成一整套独立资产 + 一条 <c>Data/Characters/&lt;id&gt;/Appearance[]</c> 条目。
/// <para>
/// <b>两处必须照抄原版、否则静默出错</b>：
/// <list type="bullet">
/// <item><b><c>Precedence</c> 写成 <c>N − i</c>（都 ≥1）</b>。原版自己的外观条目用的是
///   <b>−100 / −1000</b>（越小越优先），所以我们新增的必须**大于**它们 ——
///   否则会抢掉原版的冬季外观。而我们彼此又不能并列：并列会走 <c>Weight</c> 加权随机，
///   结果**不确定**，而 CP 的语义是「后写的赢」——所以越靠后的补丁 Precedence 越小。</item>
/// <item><b>追加而不是覆盖</b>：原版 48 条目里 <b>31 个已有 <c>Appearance</c></b>（冬季外观等）。
///   直接写 <c>Appearance: [...]</c> 会把它们冲掉。</item>
/// </list>
/// </para>
/// </summary>
public static class AppearanceEmitter
{
    /// <summary>
    /// 发射结果：产物资产 + <c>Data/Characters</c> 的改写。
    /// <para>
    /// <paramref name="DirectPatchJson"/> 为 <c>null</c> 表示**已就地追加**到本包那条建条目的补丁上
    /// （由它自己那条写入路径带出去）；非 null 则是给原版 NPC 合成的改写，要由调用方直接施加。
    /// <b>两条路都汇进同一个 <c>ApplyEditData</c></b>，不新开写盘路径。
    /// </para>
    /// </summary>
    public sealed record Result(List<string> Assets, string? DirectPatchJson);

    /// <summary>
    /// 发射一个组。<paramref name="verdicts"/> 里的 <c>EditData Data/Characters</c> 补丁若已建该条目，
    /// 就**就地追加**（与四季那套同一个思路：一条补丁一次写入）；否则读产出树里的现有条目合成一次改写。
    /// </summary>
    public static bool TryEmit(OutputTree output, ImageCache images, string packRoot,
        IEnumerable<PatchVerdict> verdicts,
        AppearanceGroup group, out Result? result, out string? error)
    {
        result = null;
        error = null;
        if (group.Reject != null) { error = group.Reject; return false; }
        if (group.Variants.Count == 0) { error = "组里没有任何变体"; return false; }

        var assets = new List<string>();
        var appearances = new JArray();
        int total = group.Variants.Count;

        foreach (AppearanceVariant v in group.Variants)
        {
            string? sprite = null, portrait = null;

            if (group.SpriteAsset != null && v.SpriteFrom != null)
            {
                if (!TryWriteVariant(output, images, packRoot, group.SpriteAsset, v.SpriteFrom, v.Index,
                        out sprite, out error))
                    return false;
                assets.Add(sprite!);
            }
            if (group.PortraitAsset != null && v.PortraitFrom != null)
            {
                if (!TryWriteVariant(output, images, packRoot, group.PortraitAsset, v.PortraitFrom, v.Index,
                        out portrait, out error))
                    return false;
                assets.Add(portrait!);
            }

            var entry = new JObject
            {
                ["Id"] = $"{group.Npc}_Appearance{v.Index}",
                ["Condition"] = v.Gsq,
            };
            if (sprite != null) entry["Sprite"] = sprite;
            if (portrait != null) entry["Portrait"] = portrait;

            // `Indoors`/`Outdoors` 的 C# 初值都是 true；只有补丁显式说了「仅室外」才动它。
            if (v.IsOutdoors == true)
            {
                entry["Outdoors"] = true;
                entry["Indoors"] = false;
            }

            // ★ 越小越优先：原版是 −100/−1000，我们一律 ≥1（抢不过原版）；
            //   组内越靠后越小（后写赢），且**互不相同**（并列会变成加权随机）。
            entry["Precedence"] = total - v.Index;
            entry["Weight"] = 1;
            appearances.Add(entry);
        }

        // 先落图，再改数据 —— 顺序反了会留下「数据指过去但文件不在」。
        return TryBuildCharacterPatch(output, verdicts, group, appearances, assets, out result, out error);
    }

    /// <summary>把一条变体的图（底图 + 该条的替换）写成一份**独立资产**。</summary>
    private static bool TryWriteVariant(OutputTree output, ImageCache images, string packRoot,
        string baseAsset, string fromFile, int index, out string? newAsset, out string? error)
    {
        newAsset = null;
        error = null;

        // 底图走 `ImageCache`：本包刚 Load/EditImage 过的底图磁盘上还没有，
        // 直接读盘会读到旧内容（甚至误判成"找不到底图"）。
        string baseRel = AssetCreator.RelativePath(baseAsset, ".png");
        if (!images.TryGetPixels(baseRel, out byte[] baseData, out int bw, out int bh, out string? baseError))
        {
            error = $"找不到底图 `{baseRel}` —— 变体要从它出发（{baseError}）";
            return false;
        }

        string resolved = LoadApplier.ExpandFieldTokens(fromFile, baseAsset);
        if (resolved.Contains("{{", StringComparison.Ordinal))
        {
            error = $"FromFile 含无法静态解析的令牌：{fromFile}";
            return false;
        }
        string srcPath;
        try { srcPath = Path.GetFullPath(Path.Combine(packRoot, resolved.Replace('/', Path.DirectorySeparatorChar))); }
        catch (Exception ex) { error = $"FromFile 路径无效：{ex.Message}"; return false; }
        if (!File.Exists(srcPath)) { error = $"找不到 FromFile：{resolved}"; return false; }

        if (!images.TryReadSource(srcPath, out byte[] srcData, out int sw, out int sh, out string? srcError))
        {
            error = srcError;
            return false;
        }

        // 整图替换：源多大就换多大（CP 的缺省 FromArea = 整张源图）。
        if (sw > bw)
        {
            error = $"变体源图宽 {sw} 超过底图 {bw} —— CP 的 ToArea 不允许超出宽度，整条跳过（同 CP）";
            return false;
        }

        if (!EditImageApplier.TryCompose(baseData, bw, bh, srcData, sw, sh,
                new ImageArea(0, 0, sw, sh), new ImageArea(0, 0, sw, sh), PatchImageMode.Replace,
                out byte[] composed, out int rw, out int rh, out string? composeError))
        {
            error = composeError;
            return false;
        }

        string name = $"{baseAsset}_Appearance{index}";
        string rel = AssetCreator.RelativePath(name, ".png");
        string? invalid = output.ValidatePath(rel);
        if (invalid != null) { error = $"产物路径非法（{rel}）：{invalid}"; return false; }
        if (images.Exists(rel))
        {
            // ★ 同名生成资产已存在：**复用**，不要整组失败。
            //   名字 `_Appearance<序号>` 是本工具的命名约定，撞名几乎只能是**另一个包/另一个组**
            //   已经为同一个 NPC 生成了这一档外观。实测 Eyvinder：SCC 先发射了行走图组
            //   （`Characters/Eyvinder_Appearance0…`），East Scarp 的组随后要同时写行走图 + 立绘
            //   ⇒ 卡在行走图上，结果**立绘一份都没写**（fix15/16 丢了 3 张肖像）。
            //   复用已生成的那一份、继续写另一份，才是两组都想要的结果。
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"_Appearance\d+$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                newAsset = name;
                return true;
            }
            error = $"产物资产名 `{name}` 已经存在 —— 不覆盖既有资产，整组不发射";
            return false;
        }

        try
        {
            images.SetPixels(rel, composed, rw, rh);
        }
        catch (Exception ex) { error = $"贴图写入失败：{ex.Message}"; return false; }

        if (!output.CreatedFiles.Contains(rel, StringComparer.OrdinalIgnoreCase))
            output.CreatedFiles.Add(rel);
        AssetCreator.CopyImageConfig(output, name);
        newAsset = name;
        return true;
    }

    /// <summary>
    /// 生成对 <c>Data/Characters</c> 的改写：优先**就地追加**到本包那条建条目的补丁上；
    /// 没有那条补丁（NPC 是原版的）就合成一次 <c>Fields</c> 改写，并把**原版已有的
    /// <c>Appearance</c> 前置回来**——否则会把原版的冬季外观冲掉。
    /// </summary>
    private static bool TryBuildCharacterPatch(OutputTree output, IEnumerable<PatchVerdict> verdicts,
        AppearanceGroup group, JArray ours, List<string> pending, out Result? result, out string? error)
    {
        result = null;
        error = null;

        foreach (PatchVerdict v in verdicts)
        {
            if (!v.Patch.Action.Equals("EditData", StringComparison.OrdinalIgnoreCase)) continue;
            if (!(v.Patch.Target ?? "").Replace('\\', '/').Trim('/')
                    .Equals("Data/Characters", StringComparison.OrdinalIgnoreCase)) continue;
            if (v.Patch.Raw["Entries"] is not JObject entries) continue;
            if (entries[group.Npc] is not JObject entry) continue;

            var merged = new JArray();
            if (entry["Appearance"] is JArray existing)
                foreach (JToken item in existing.ToList()) merged.Add(item);
            else
            {
                // ★ 本条目自己没写 `Appearance`，但**产出树里可能已有**（别的包写的外观，
                //   或者 SCC 的 `Priority: Late` 补丁先加的）。只追加 `ours` 会把这批外观
                //   一起冲掉 —— 实测全量里 Eyvinder 的 12 条显式外观被压成 3 条。
                string? currentJson = output.ReadTextOrNull(AssetCreator.RelativePath("Data/Characters", ".json"));
                if (currentJson != null)
                {
                    try
                    {
                        JObject root = JObject.Parse(currentJson);
                        if (root[group.Npc] is JObject current && current["Appearance"] is JArray prior)
                            foreach (JToken item in prior.ToList()) merged.Add(item);
                    }
                    catch { /* 读不到就只追加本次的 */ }
                }
            }
            foreach (JToken item in ours) merged.Add(item);
            entry["Appearance"] = merged;
            // 这条补丁本来就要写，改动由同一条路径带出去 —— 不新开写盘路径。
            result = new Result(pending, null);
            return true;
        }

        // 原版 NPC：从产出树读现有条目，**把已有的 Appearance 前置回来**。
        string rel = AssetCreator.RelativePath("Data/Characters", ".json");
        // 走产出树读（含本次运行还没落盘的写入）—— 直接读盘会漏掉本包刚写的条目。
        string? existingJson = output.ReadTextOrNull(rel);
        var merged2 = new JArray();
        if (existingJson != null)
        {
            try
            {
                JObject root = JObject.Parse(existingJson);
                if (root[group.Npc] is JObject entry && entry["Appearance"] is JArray existing)
                    foreach (JToken item in existing.ToList()) merged2.Add(item);
            }
            catch (Exception ex) { error = $"读 `Data/Characters` 失败：{ex.Message}"; return false; }
        }
        foreach (JToken item in ours) merged2.Add(item);

        var patch = new JObject
        {
            ["Action"] = "EditData",
            ["Target"] = "Data/Characters",
            ["Fields"] = new JObject { [group.Npc] = new JObject { ["Appearance"] = merged2 } },
        };
        result = new Result(pending, patch.ToString(Newtonsoft.Json.Formatting.None));
        return true;
    }
}
