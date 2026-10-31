using System.Text;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp.Lexing;

namespace SdvCpTool.Core.Cp;

/// <summary>
/// 展开 CP 的 <c>{{InternalAssetKey: 包内相对路径}}</c>。
/// <para>
/// 语义（SMAPI 4 `ModContentHelper.GetInternalAssetName` → `ModContentManager.GetInternalAssetKey`）：
/// 资产名 = <c>SMAPI/&lt;UniqueID 小写&gt;/&lt;相对路径&gt;</c>，**扩展名保留**
/// （`NormalizeAssetName` 只剥 <c>.xnb</c>）。实测语料 17 个文件用到它
/// （Hurst 的 38 个物品、Rodney 的书/稻草人、Eli and Dylan、Metalcore…），
/// 不代就会让**整条数据补丁**被拒 —— 物品、商店、动物全部消失。
/// </para>
/// <para>
/// 纯 XNB 里没有 SMAPI 去服务这个"包内文件资产"，所以必须把被引用的图片
/// **物化进产出树**（<c>&lt;资产名&gt;.png</c> + <c>.config</c>）；替换阶段只记下清单，
/// 由调用方在写盘阶段用 <c>ImageApplier</c> 落盘。
/// </para>
/// </summary>
public static class InternalAssetKeyExpander
{
    /// <summary>补丁根上不碰的字段（与 <see cref="ValueTokenSubstitutor"/> 同一口径）。</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "When", "Target", "FromFile", "LogName",
    };

    /// <summary>
    /// 就地替换所有 <c>{{InternalAssetKey: …}}</c>；返回替换处数。
    /// <paramref name="assets"/> 收集「资产名 → 包内相对路径」，由调用方物化。
    /// 文件不存在、参数里还有未展开令牌、或不是图片时**原样保留**（下游照旧判失败/报出来）。
    /// </summary>
    public static int Apply(IEnumerable<ChangePatch> patches, string packRoot, string uniqueId,
        IDictionary<string, string> assets, ICollection<string>? notes = null)
    {
        var ctx = new Ctx(packRoot, uniqueId, assets, notes);
        int changed = 0;
        foreach (ChangePatch patch in patches)
            changed += Walk(patch.Raw, patch.Raw, ctx);
        return changed;
    }

    private sealed record Ctx(string PackRoot, string UniqueId, IDictionary<string, string> Assets,
        ICollection<string>? Notes);

    private static int Walk(JToken node, JObject root, Ctx ctx)
    {
        switch (node)
        {
            case JObject obj:
            {
                int changed = 0;
                foreach (JProperty p in obj.Properties().ToList())
                {
                    if (ReferenceEquals(obj, root) && Skipped.Contains(p.Name)) continue;
                    changed += Walk(p.Value, root, ctx);
                }
                return changed;
            }
            case JArray arr:
            {
                int changed = 0;
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i].Type == JTokenType.String)
                    {
                        string? done = Expand(arr[i].Value<string>()!, ctx);
                        if (done != null) { arr[i] = done; changed++; }
                    }
                    else changed += Walk(arr[i], root, ctx);
                }
                return changed;
            }
            case JValue { Type: JTokenType.String } v:
            {
                string? done = Expand(v.Value<string>()!, ctx);
                if (done == null) return 0;
                node.Replace(new JValue(done));
                return 1;
            }
            default:
                return 0;
        }
    }

    private static string? Expand(string text, Ctx ctx)
    {
        if (!text.Contains("InternalAssetKey", StringComparison.OrdinalIgnoreCase)) return null;
        if (!CpLexer.Instance.TryParse(text, out IReadOnlyList<ILexToken> tokens)) return null;

        var hits = new List<(int Start, int End, string Value)>();
        Collect(tokens);
        if (hits.Count == 0) return null;

        var sb = new StringBuilder(text);
        foreach ((int start, int end, string value) in hits.OrderByDescending(h => h.Start))
        {
            sb.Remove(start, end - start);
            sb.Insert(start, value);
        }
        return sb.ToString();

        void Collect(IReadOnlyList<ILexToken> list)
        {
            foreach (ILexToken token in list)
            {
                if (token is not LexTokenToken tk) continue;
                if (tk.InputArgs == null
                    || !tk.Name.Equals("InternalAssetKey", StringComparison.OrdinalIgnoreCase))
                {
                    if (tk.InputArgs != null) Collect(tk.InputArgs.Parts);
                    continue;
                }

                string rel = tk.InputArgs.ToString().Trim().Trim('"').Replace('\\', '/').TrimStart('/');
                // 参数里还有代不掉的令牌（配置/动态令牌没展开）⇒ 不猜。
                if (rel.Length == 0 || rel.Contains("{{", StringComparison.Ordinal)) continue;

                string ext = Path.GetExtension(rel);
                string full;
                try { full = Path.GetFullPath(Path.Combine(ctx.PackRoot, rel)); }
                catch { continue; }

                // 无扩展名：SMAPI 的 `GetModFile` 对 Texture2D 会依次试 `<路径>.png` / `.xnb`
                // （实测 Nora 的 `assets/Characters/nora_sprites_winter_coat`）。
                // 资产名**保持原样不带扩展名**，物化时写 `<资产>.png`，游戏请求的正好是它。
                if (ext.Length == 0)
                {
                    string png = full + ".png";
                    if (!File.Exists(png)) continue;
                    full = png;
                }
                // 目前只物化 PNG：`ImageApplier` 只处理 .png，数据/地图类型还要按资产类型配 .config，先不猜。
                else if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 防目录穿越：模组内容是不可信输入，只许引用包内文件。
                string rootFull = Path.GetFullPath(ctx.PackRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(full)) continue;

                // 归一成相对包根的干净路径（去掉 `./` / 折叠 `..`）。资产名用**引用里的写法**
                // （无扩展名就保持无扩展名），物化源路径用真实文件（补上的 `.png`）。
                string sourceRel = Path.GetRelativePath(ctx.PackRoot, full).Replace('\\', '/');
                string assetRel = ext.Length == 0
                    ? Path.ChangeExtension(sourceRel, null)!.Replace('\\', '/')
                    : sourceRel;
                string asset = "SMAPI/" + ctx.UniqueId.ToLowerInvariant() + "/" + assetRel;
                hits.Add((tk.Start, tk.End, asset));
                if (!ctx.Assets.ContainsKey(asset))
                {
                    ctx.Assets[asset] = sourceRel;
                    ctx.Notes?.Add($"`{{{{InternalAssetKey: {rel}}}}}` → `{asset}`（素材随包物化）");
                }
            }
        }
    }
}
