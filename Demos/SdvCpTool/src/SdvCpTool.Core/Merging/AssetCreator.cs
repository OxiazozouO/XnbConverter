using System.Text;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>新建资产：写数据文件，并从同类资产复制一份 <c>.config</c> 让回包可用。</summary>
public static class AssetCreator
{
    /// <summary>
    /// 按资产名造出相对路径。扩展名由调用方给（JSON 资产用 <c>.json</c>）。
    /// </summary>
    public static string RelativePath(string assetName, string extension) =>
        assetName.Replace('\\', '/').Trim('/') + extension;

    /// <summary>
    /// 这个资产名上已经压着**另一类**资产吗？返回占位文件的扩展名（<c>.tbin</c> 地图 / <c>.json</c> 数据 /
    /// <c>.xnb</c> 打包件），没有则 <c>null</c>。
    /// <para>
    /// <b>为什么要它</b>：一个内容键只能是一类资产。把贴图写到地图的键上（或反过来）**不会报错**，
    /// 只会静默换掉类型 —— 游戏拿 <c>Texture2D</c> 去当 <c>Map</c> 用，进那个地点才炸
    /// （实测 <c>Maps/Coop</c>：原版地图，被包内图集 `Coop.png` 盖成贴图）。
    /// </para>
    /// <para>产出树与**输入树**都要看：输入树那份是原版内容，产出树里那份是这次运行写的。</para>
    /// </summary>
    public static string? NonImageOccupant(OutputTree output, string assetName)
    {
        foreach (string ext in new[] { ".tbin", ".json", ".xnb" })
        {
            string rel = RelativePath(assetName, ext);
            if (output.Exists(rel) || output.HasInputFile(rel)) return ext;
        }
        return null;
    }

    /// <summary>
    /// 找一份可复制的 <c>.config</c> 模板：先同目录，再同目录的父级。
    /// 不按数据形状筛选（形状传 <c>null</c>）。找不到返回 null。
    /// </summary>
    public static string? FindConfigTemplate(OutputTree output, string assetName) =>
        FindConfigTemplate(output, assetName, shape: null);

    /// <summary>
    /// 找一份可复制的 <c>.config</c> 模板。
    /// 优先选<b>同目录、且其同名 <c>.json</c> 的数据形状与 <paramref name="shape"/> 一致</b>的那份 ——
    /// <c>.config</c> 编码的是 XNB 反序列化类型，形状不匹配会让回包出的 xnb 读不出来。
    /// 形状判据先用 <see cref="JTokenType"/>（对象 vs 数组，字典资产与列表资产的根本分野），
    /// 同形状多个候选再比第一条 entry 的值类型做二级排序。
    /// 在此之上还有更优先的一级：<b>解析候选 <c>.config</c> 里 <c>Readers[0].Type</c> 的泛型实参</b>，
    /// 与从 <paramref name="shape"/> 推断出的键/值类型比对 —— 形状判据分不出
    /// <c>Dictionary&lt;Int32,String&gt;</c> 与 <c>Dictionary&lt;String,String&gt;</c>，泛型可以。
    /// 找不到任何匹配的才退到"该目录任一份"，再退到祖先目录（祖先目录同样优先匹配）。
    /// </summary>
    public static string? FindConfigTemplate(OutputTree output, string assetName, JToken? shape)
    {
        foreach (string level in AncestorLevels(assetName))
        {
            string prefix = level.Length == 0 ? "" : level + "/";
            string? hit = PickInDirectory(output, prefix, shape);
            if (hit != null) return hit;
        }

        return null;
    }

    /// <summary>资产名所在目录及其各级祖先目录（含空串根级），自下而上。</summary>
    private static List<string> AncestorLevels(string assetName)
    {
        string assetPath = assetName.Replace('\\', '/').Trim('/');
        string dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? "";

        var levels = new List<string> { dir };
        string cur = dir;
        while (!string.IsNullOrEmpty(cur))
        {
            cur = Path.GetDirectoryName(cur)?.Replace('\\', '/') ?? "";
            levels.Add(cur);
        }
        return levels;
    }

    /// <summary>
    /// 图片资产的 <c>.config</c> 判据：根 Reader 的 <c>Type</c> 是 <c>Texture2DReader</c>。
    /// 图片 <c>.config</c> 里尺寸/格式都是通用值，因此同类任一份都可直接拿来做模板。
    /// </summary>
    public static bool IsImageConfig(OutputTree output, string configRel)
    {
        // 走特征缓存：本方法在选模板时对**整个目录**的候选逐个问一遍。
        string? type = output.GetConfigFacts(configRel).ReaderType;
        return type != null && type.Contains("Texture2DReader", StringComparison.Ordinal);
    }

    /// <summary>
    /// 给图片资产找一份可复制的 <c>.config</c> 模板：从同目录逐级上溯，取第一份根 Reader 是
    /// <c>Texture2DReader</c> 的 <c>.config</c>。找不到返回 null。
    /// </summary>
    public static string? FindImageConfigTemplate(OutputTree output, string assetName)
    {
        foreach (string level in AncestorLevels(assetName))
        {
            string prefix = level.Length == 0 ? "" : level + "/";
            // 走按目录建好的索引，别再每次前缀扫一遍 InputFiles（见 OutputTree.ConfigsUnder）；
            // 同一前缀的“第一份图片 config”在本包内固定，缓存下来（一张大图集常有几十张图）。
            string? hit = output.ImageConfigUnder(prefix,
                () => output.ConfigsUnder(prefix).FirstOrDefault(f => IsImageConfig(output, f)));
            if (hit != null) return hit;
        }

        return null;
    }

    /// <summary>为新建图片资产补一份图片 <c>.config</c>。返回写入的相对路径，找不到模板返回 null。</summary>
    public static string? CopyImageConfig(OutputTree output, string assetName)
    {
        string? template = FindImageConfigTemplate(output, assetName);
        if (template == null) return null;

        string dest = RelativePath(assetName, ".config");
        if (!output.TryWriteText(dest, output.ReadTextOrNull(template) ?? "", out _)) return null;
        output.CreatedFiles.Add(dest);
        return dest;
    }

    /// <summary>在某个目录（含其子目录）里挑一份 <c>.config</c>：Reader 泛型匹配优先，其次形状匹配，再退到任一份。</summary>
    private static string? PickInDirectory(OutputTree output, string prefix, JToken? shape)
    {
        List<string> candidates = output.ConfigsUnder(prefix).ToList();

        if (candidates.Count == 0) return null;
        if (shape == null) return candidates[0];

        JTokenType? shapeFirst = FirstEntryValueType(shape);
        IReadOnlyList<string?>? shapeArgs = InferDictionaryArgs(shape);
        string? best = null;
        int bestGeneric = int.MinValue;
        int bestShape = -1;
        foreach (string candidate in candidates)
        {
            int shapeScore = ShapeScore(output, candidate, shape, shapeFirst);
            int genericScore = GenericScore(output, candidate, shapeArgs);
            // 泛型为一级判据，形状为二级
            if (best == null
                || genericScore > bestGeneric
                || (genericScore == bestGeneric && shapeScore > bestShape))
            {
                bestGeneric = genericScore;
                bestShape = shapeScore;
                best = candidate;
            }
        }

        // 至少要有一项判据成立，否则退到该目录任一份（判不出不能当失败）
        return bestGeneric > 0 || bestShape > 0 ? best : candidates[0];
    }

    /// <summary>
    /// 给候选 <c>.config</c> 的形状打分：
    /// 2 = 主形状与首 entry 值类型都一致；1 = 仅主形状一致；0 = 主形状不一致或读不出同名 <c>.json</c>。
    /// </summary>
    private static int ShapeScore(OutputTree output, string configRel, JToken shape, JTokenType? shapeFirst)
    {
        // 走特征缓存：`Data/` 有 263 份候选，每建一个新资产都逐个读同名 `.json` 太贵。
        OutputTree.ConfigFacts facts = output.GetConfigFacts(configRel);
        if (facts.JsonRootType == null) return 0;          // 同名 `.json` 不在或解析不出
        if (facts.JsonRootType != shape.Type) return 0;

        return Nullable.Equals(shapeFirst, facts.JsonFirstValueType) ? 2 : 1;
    }

    /// <summary>取第一条 entry 的值类型：对象取首个属性的值，数组取首个元素；空则 null。</summary>
    private static JTokenType? FirstEntryValueType(JToken token) => token switch
    {
        JObject obj => obj.Properties().FirstOrDefault()?.Value?.Type,
        JArray arr => arr.FirstOrDefault()?.Type,
        _ => null,
    };

    /// <summary>
    /// 泛型打分（比形状更硬的一级判据）：
    /// 2 = 候选 Reader 的泛型实参与数据推断出的类型<b>全部</b>一致（如都是 String,String）；
    /// 1 = 比对过的实参都一致，但有的位置数据判不出（如值类型是对象）；
    /// -1 = 明确不一致（如数据是字符串键、模板是 Int32 键 —— 回包器会直接写崩）；
    /// 0 = 候选不是字典 Reader 或解析不出泛型，不参与判据。
    /// </summary>
    private static int GenericScore(OutputTree output, string configRel, IReadOnlyList<string?>? shapeArgs)
    {
        if (shapeArgs == null) return 0;   // 非对象形状（数组资产等）不走泛型判据
        IReadOnlyList<string>? readerArgs = ReadDictionaryArgs(output, configRel);
        if (readerArgs == null) return 0;
        if (readerArgs.Count != shapeArgs.Count) return -1;

        int compared = 0;
        for (int i = 0; i < shapeArgs.Count; i++)
        {
            string? inferred = shapeArgs[i];
            if (inferred == null) continue;   // 数据判不出的位置跳过
            compared++;
            if (!TypeNameEquals(inferred, readerArgs[i])) return -1;
        }

        if (compared == 0) return 0;
        // 每个位置都比对过才算全匹配
        return compared == shapeArgs.Count ? 2 : 1;
    }

    /// <summary>
    /// 读候选 <c>.config</c> 的根 Reader（<c>Readers[0].Type</c>）并取出泛型实参。
    /// 根 Reader 不是字典 Reader、或文件读不出/解析失败时返回 null。
    /// </summary>
    private static IReadOnlyList<string>? ReadDictionaryArgs(OutputTree output, string configRel)
    {
        string? type = output.GetConfigFacts(configRel).ReaderType;   // 走特征缓存
        if (type == null || !type.Contains("Dictionary", StringComparison.Ordinal)) return null;
        return ParseGenericArgs(type);
    }

    /// <summary>
    /// 从形如 <c>Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.Int32, mscorlib…],[System.String, mscorlib…]]</c>
    /// 的类型串里取出最外层泛型实参的类型名（含程序集限定名的逗号之后部分丢弃）。
    /// 非泛型或解析不出返回 null。
    /// </summary>
    private static IReadOnlyList<string>? ParseGenericArgs(string type)
    {
        int open = type.IndexOf("[[", StringComparison.Ordinal);
        if (open < 0) return null;

        var args = new List<string>();
        var sb = new StringBuilder();
        int depth = 0;
        bool inArg = false;
        bool atBoundary = false;   // 已到该实参「类型名 / 程序集限定名」的分隔逗号

        for (int i = open; i < type.Length; i++)
        {
            char c = type[i];
            if (c == '[')
            {
                depth++;
                if (depth == 2) { sb.Clear(); inArg = true; atBoundary = false; }
            }
            else if (c == ']')
            {
                if (depth == 2 && inArg) { args.Add(sb.ToString()); inArg = false; }
                depth--;
                if (depth == 0) break;
            }
            else if (inArg && depth >= 2)
            {
                if (c == ',' && depth == 2) atBoundary = true;   // 之后是程序集信息
                else if (!atBoundary) sb.Append(c);
            }
        }

        return args.Count > 0 ? args : null;
    }

    /// <summary>
    /// 从被写数据推断字典泛型实参：<c>[键类型, 值类型]</c>，判不出的位置为 null。
    /// 非对象形状返回 null。键类型看属性名是否全为整数，值类型看首条 entry。
    /// </summary>
    private static IReadOnlyList<string?>? InferDictionaryArgs(JToken shape)
    {
        if (shape is not JObject obj) return null;

        string? key = !obj.HasValues ? null
            : obj.Properties().All(p => int.TryParse(p.Name, out _)) ? "Int32" : "String";
        string? value = DeduceValueType(obj.Properties().FirstOrDefault()?.Value);
        return new List<string?> { key, value };
    }

    /// <summary>JSON 值类型映射到 XNB Reader 的类型名；映射不可靠（浮点/容器/对象）返回 null。</summary>
    private static string? DeduceValueType(JToken? value) => value?.Type switch
    {
        JTokenType.String => "String",
        JTokenType.Integer => "Int32",
        JTokenType.Boolean => "Boolean",
        _ => null,
    };

    private static bool TypeNameEquals(string a, string b) =>
        string.Equals(NormalizeTypeName(a), NormalizeTypeName(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>取类型名的简单名：丢掉泛型后缀、命名空间与嵌套前缀（<c>System.Int32</c> → <c>Int32</c>）。</summary>
    private static string NormalizeTypeName(string raw)
    {
        string s = raw.Trim();
        int mark = s.IndexOfAny(new[] { '[', '`' });
        if (mark >= 0) s = s[..mark];
        int dot = s.LastIndexOf('.');
        if (dot >= 0) s = s[(dot + 1)..];
        int plus = s.LastIndexOf('+');
        if (plus >= 0) s = s[(plus + 1)..];
        return s.Trim();
    }

    /// <summary>
    /// 为新建资产补一份 <c>.config</c>。<paramref name="shape"/> 是待写数据的形状，
    /// 用来挑类型匹配的模板。返回写入的相对路径，失败返回 null。
    /// </summary>
    public static string? CopyConfig(OutputTree output, string assetName, JToken? shape = null)
    {
        string? template = FindConfigTemplate(output, assetName, shape);
        if (template == null) return null;

        string content = output.ReadTextOrNull(template) ?? "";
        string dest = RelativePath(assetName, ".config");
        if (!output.TryWriteText(dest, content, out _)) return null;
        output.CreatedFiles.Add(dest);
        return dest;
    }
}
