using SdvCpTool.Core.Reporting;
using Newtonsoft.Json.Linq;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Validating;

/// <summary>
/// <b>按游戏自己的贴图寻址逻辑</b>核对产出：每个物品条目的 <c>Texture</c> + <c>SpriteIndex</c>
/// 在产出树里能不能真的取到一块图。
/// <para>
/// 与另两道自检的分工：<c>OutputShapeAudit</c> 看容器形状、<c>GameDataValidator</c> 看字段类型
/// —— 都只看 <b>JSON 本身</b>。这一道看的是 <b>JSON 指向的资源</b>：
/// 数据形状全对、贴图却找不到或索引指到图外，正是实机测试最容易漏掉的那类（只在特定物品被绘制时才出错）。
/// </para>
/// <para>
/// <b>公式逐字取自游戏</b>，不是我们自己推的：
/// <list type="bullet">
/// <item><c>BigCraftableDataDefinition.cs:44</c>：贴图缺省 <c>TileSheets\Craftables</c>；
///   <c>:72</c> → <c>Object.getSourceRectForBigCraftable</c>（<c>Object.cs:1087-1089</c>）
///   = <c>(index % (W/16) * 16, index*16/W * 16 * 2, 16, 32)</c>。</item>
/// <item><c>ObjectDataDefinition.cs:52</c>：贴图缺省 <c>Maps\springobjects</c>；
///   <c>:67</c> → <c>Game1.getSourceRectForStandardTileSheet(texture, index, 16, 16)</c>
///   （<c>Game1.cs:15897-15905</c>）= <c>(index*16 % W, index*16/W * 16, 16, 16)</c>。</item>
/// <item><c>ParsedItemData.cs:185-200</c>：贴图资产不存在时<b>只写一条 Error 日志然后返回 null</b>，
///   物品照常存在但画不出来 —— <b>静默</b>，实机极难发现。</item>
/// </list>
/// </para>
/// <para><b>只报告，不改产物</b>。</para>
/// </summary>
public static class SpriteSheetValidator
{
    private const string DefaultBigCraftableTexture = "TileSheets/Craftables";
    private const string DefaultObjectTexture = "Maps/springobjects";

    /// <summary>一处取不到图的引用。</summary>
    public sealed record Finding(
        string Asset, string EntryKey, string Texture, int SpriteIndex, string Reason);

    public sealed class Audit
    {
        /// <summary>被核对的资产数。</summary>
        public int Assets;

        /// <summary>被核对的条目数。</summary>
        public int Entries;

        /// <summary>贴图文件在产出树里找不到的条目数。</summary>
        public int MissingTexture;

        /// <summary>精灵格越出贴图边界的条目数。</summary>
        public int OutOfBounds;

        /// <summary>贴图宽不是格宽整数倍 —— 游戏的列数计算（<c>W/16</c>）会错位。</summary>
        public int BadSheetWidth;

        /// <summary><c>Name</c> 含 <c>Seasonal</c> 但贴图不足 4 格宽，第 2..4 季会指到图外。</summary>
        public int SeasonalTooNarrow;

        /// <summary>四类问题之和。</summary>
        public int TotalProblems => MissingTexture + OutOfBounds + BadSheetWidth + SeasonalTooNarrow;

        /// <summary>问题条目（最多留 <see cref="MaxFindings"/> 条，避免报告被刷屏）。</summary>
        public List<Finding> Findings = new();

        public const int MaxFindings = 200;
    }

    public static Audit Run(string outputRoot, AuditParseCache? parseCache = null)
    {
        var audit = new Audit();
        if (!Directory.Exists(outputRoot)) return audit;

        Check(outputRoot, "Data/BigCraftables", DefaultBigCraftableTexture, 16, 32, audit, parseCache);
        Check(outputRoot, "Data/Objects", DefaultObjectTexture, 16, 16, audit, parseCache);
        CheckCharacterAppearances(outputRoot, audit, parseCache);
        return audit;
    }

    /// <summary>
    /// <c>Data/Characters</c> 的 <b>整图引用</b>必须取得到：NPC 自己的 <c>Sprite</c>/<c>Portrait</c>，
    /// 以及 <c>Appearance[]</c> 每一项上的 <c>Sprite</c>/<c>Portrait</c>。
    /// <para>
    /// <b>为什么必须加这一道</b>：这里的引用**和别处不同** —— 不是「图集 + 索引」，
    /// 而是**整张资产名**（`Characters/Alesia_Spring` 这种）。而
    /// <c>Appearance[]</c> 正是本工具**自己发射**的落点（Task 32：按季节/条件换立绘），
    /// 发射器会把贴图**另存为独立资产**再在 <c>Appearance[]</c> 里引用 ——
    /// 一旦那份资产没写出去，游戏**画不出人**，而这是<b>静默</b>的：数据形状全对、
    /// 另外两道自检（BigCraftables / Objects）也管不到这里。
    /// </para>
    /// <para>
    /// 引用名与游戏的解析口径一致：<c>\</c> → <c>/</c>、去首尾斜杠、缺省 <c>.png</c>；
    /// 找不到精确名时退到本地化变体（<c>X.es-ES.png</c>）—— 与 <see cref="ReadSize"/> 同一套。
    /// </para>
    /// </summary>
    private static void CheckCharacterAppearances(string outputRoot, Audit audit,
        AuditParseCache? parseCache)
    {
        string path = Path.Combine(outputRoot, "Data", "Characters.json");
        if (!File.Exists(path)) return;

        JObject? root = parseCache?.Parse(path) as JObject;
        if (parseCache == null)
            try { root = JObject.Parse(File.ReadAllText(path)); }
            catch { root = null; }   // 解析不了是别处的职责
        if (root == null) return;

        audit.Assets++;
        foreach (JProperty npc in root.Properties())
        {
            if (npc.Value is not JObject entry) continue;
            audit.Entries++;

            foreach ((string field, string? value) in References(entry))
            {
                if (string.IsNullOrWhiteSpace(value)) continue;   // 空 = 用缺省名，不在此列

                string asset = value.Replace('\\', '/').Trim('/');
                if (asset.Length == 0) continue;
                // 已经带扩展名的按原样查；其余补 `.png`。
                if (asset.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    asset = asset[..^4];

                if (ImageExists(outputRoot, asset)) continue;

                // ⚠ `_Beach` 是**海岛装的可选变体**（约定俗成由别的模组提供），游戏对它是
                //   `DoesAssetExist` 保护 + 回退常服/常图（`NPC.cs:1051-1066` 的
                //   `if (!flag4 && isWearingIslandAttire) { ... DoesAssetExist ... }`，
                //   最后落到 `TryLoadSprites("Characters/" + getTextureName())`）——
                //   **NPC 照常画得出来**（只是穿常服）。原先这条和"真缺图"共用一句
                //   "画不出人（静默）"，**说错了**（实测 SVE 的 `GuntherSilvian_Beach` 就是这么来的）。
                bool optionalVariant = asset.EndsWith("_Beach", StringComparison.OrdinalIgnoreCase);
                Add(audit, new Finding("Data/Characters", npc.Name, asset, 0,
                    optionalVariant
                        ? $"`{field}` 指向 `{asset}` —— 产出树里没有，但这是 **`_Beach` 可选变体**："
                          + "游戏用 `DoesAssetExist` 保护这一类并回退常服/常图（`NPC.cs:1051-1066`），"
                          + "**不是缺陷**（这类变体通常由别的模组提供）"
                        : $"`{field}` 引用的贴图资产在产出树里不存在 —— 游戏只会记一条日志然后回退到缺省名"
                          + "（`NPC.cs:1020-1072`）；缺省名也没图时才是真的画不出人"));
            }
        }
    }

    /// <summary>一个 NPC 条目上所有的整图引用。</summary>
    private static IEnumerable<(string Field, string? Value)> References(JObject entry)
    {
        yield return ("Sprite", entry["Sprite"]?.Value<string>());
        yield return ("Portrait", entry["Portrait"]?.Value<string>());

        if (entry["Appearance"] is not JArray appearances) yield break;
        for (int i = 0; i < appearances.Count; i++)
        {
            if (appearances[i] is not JObject a) continue;
            yield return ($"Appearance[{i}].Sprite", a["Sprite"]?.Value<string>());
            yield return ($"Appearance[{i}].Portrait", a["Portrait"]?.Value<string>());
        }
    }

    /// <summary>产出树里有没有这张图（精确名优先，再退到本地化变体）。</summary>
    private static bool ImageExists(string outputRoot, string asset)
    {
        string basePath = Path.Combine(outputRoot, asset.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(basePath + ".png")) return true;

        string? dir = Path.GetDirectoryName(basePath);
        string name = Path.GetFileName(basePath);
        return dir != null && Directory.Exists(dir)
            && Directory.EnumerateFiles(dir, name + ".*.png").Any();
    }

    private static void Check(string outputRoot, string assetName, string defaultTexture,
        int cellW, int cellH, Audit audit, AuditParseCache? parseCache)
    {
        string path = Path.Combine(outputRoot, assetName.Replace('/', Path.DirectorySeparatorChar) + ".json");
        if (!File.Exists(path)) return;

        JObject? root = parseCache?.Parse(path) as JObject;
        if (parseCache == null)
            try { root = JObject.Parse(File.ReadAllText(path)); }
            catch { root = null; }   // 解析不了是别处的职责，这里不重复报
        if (root == null) return;

        audit.Assets++;
        var sizes = new Dictionary<string, (int W, int H)?>(StringComparer.OrdinalIgnoreCase);

        foreach (JProperty p in root.Properties())
        {
            if (p.Value is not JObject entry) continue;   // 数组型资产不在此列
            audit.Entries++;

            string texture = (entry["Texture"]?.Value<string>() ?? defaultTexture)
                .Replace('\\', '/').Trim('/');
            int index = entry["SpriteIndex"]?.Value<int?>() ?? 0;

            if (!sizes.TryGetValue(texture, out (int W, int H)? size))
            {
                size = ReadSize(outputRoot, texture);
                sizes[texture] = size;
            }

            if (size == null)
            {
                audit.MissingTexture++;
                Add(audit, new Finding(assetName, p.Name, texture, index,
                    "产出树里找不到这张贴图 —— 游戏只记一条 Error 日志，物品画不出来（静默）"));
                continue;
            }

            (int w, int h) = size.Value;
            if (w <= 0 || h <= 0 || w % cellW != 0)
            {
                audit.BadSheetWidth++;
                Add(audit, new Finding(assetName, p.Name, texture, index,
                    $"贴图宽 {w} 不是格宽 {cellW} 的整数倍，游戏的列数计算（W/{cellW}）会错位"));
                continue;
            }

            // 逐字复刻游戏公式，别自己化简。
            int x = index % (w / cellW) * cellW;
            int y = index * cellW / w * cellH;
            if (x + cellW > w || y + cellH > h)
            {
                audit.OutOfBounds++;
                Add(audit, new Finding(assetName, p.Name, texture, index,
                    $"索引 {index} 落在 ({x},{y},{cellW},{cellH})，越出贴图 {w}x{h}"));
                continue;
            }

            // `Name` 含 `Seasonal` 时游戏在 **0..3** 之间换索引（Object.cs:1940-1943），
            // 贴图得 ≥ 4 格宽。数据里 SpriteIndex 看着是 0，光看数据发现不了这条。
            if (entry["Name"]?.Value<string>()?.Contains("Seasonal", StringComparison.Ordinal) == true
                && w / cellW < 4)
            {
                audit.SeasonalTooNarrow++;
                Add(audit, new Finding(assetName, p.Name, texture, index,
                    $"Name 含 Seasonal 但贴图只有 {w / cellW} 格宽 —— 游戏会在 0..3 之间取索引"
                    + "（Object.cs:1940-1943），第 2..4 季会指到图外"));
            }
        }
    }

    /// <summary>贴图尺寸。先找精确名，再退到本地化变体（<c>X.es-ES.png</c>）。</summary>
    private static (int W, int H)? ReadSize(string outputRoot, string texture)
    {
        string basePath = Path.Combine(outputRoot, texture.Replace('/', Path.DirectorySeparatorChar));
        string exact = basePath + ".png";
        string? file = File.Exists(exact) ? exact : null;

        if (file == null)
        {
            string? dir = Path.GetDirectoryName(basePath);
            string name = Path.GetFileName(basePath);
            if (dir != null && Directory.Exists(dir))
                file = Directory.EnumerateFiles(dir, name + ".*.png").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        }

        if (file == null) return null;
        // 只读 IHDR 头拿尺寸 —— 原先 `Png.Read` 会把整张图解出来，实测每包几百张图、0.4 秒上下
        return PngHeader.TryReadSize(file, out int w, out int h) ? (w, h) : null;
    }

    private static void Add(Audit audit, Finding f)
    {
        if (audit.Findings.Count < Audit.MaxFindings) audit.Findings.Add(f);
    }
}
