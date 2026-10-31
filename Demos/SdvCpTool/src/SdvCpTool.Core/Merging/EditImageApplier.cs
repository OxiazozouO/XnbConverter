using System.Globalization;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// CP <c>EditImage</c> 的 <c>PatchMode</c>（<c>Conditions\PatchImageMode.cs</c>）。
/// <c>Mask</c> 已被 CP 2.9 禁用（<c>Migrations\Migration_2_9.cs</c> 直接报错），<b>不实现</b>。
/// </summary>
public enum PatchImageMode
{
    Replace,
    Overlay,
}

/// <summary>像素矩形区域（对应 CP 的 <c>FromArea</c> / <c>ToArea</c>）。</summary>
public readonly record struct ImageArea(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public override string ToString() => $"({X},{Y},{Width}x{Height})";
}

/// <summary>一条 <c>EditImage</c> 的（Target, FromFile）组合 —— 逗号笛卡尔积展开后的一个实例。</summary>
public readonly record struct EditImageJob(string Target, string FromFile);

/// <summary>
/// CP <c>EditImage</c> 的合成器：把内容包里的源图 <c>.png</c> 合并进产出树里的目标图片资产。
/// <para>
/// 与 <see cref="ImageApplier"/>（纯 <c>Load</c> 的<b>逐字节复制</b>）职责不同：这里必然要
/// <b>重编码</b> PNG（<c>Png.Read</c> / <c>Png.Write</c>）—— 这是 Task 11 与 Task 10a 硬约束划清的边界。
/// </para>
/// <para>
/// <b>Overlay 必须逐字复刻 SMAPI</b>（<c>AssetDataForImage.PatchImageImpl</c>）：
/// <c>MinOpacity=5</c>（Linux/macOS 上"全透明"像素的 alpha 可能到 4）、源区域整块透明则不动、
/// 首末非透明行裁剪、半透明混合 <c>R = src.R + dst.R*k</c>（<b>不是</b>标准 source-over）、
/// <c>A = max(src.A, dst.A)</c>。复刻错了叠图会有肉眼可见差异。
/// </para>
/// </summary>
public sealed class EditImageApplier
{
    /// <summary>SMAPI 的 <c>MinOpacity</c>：alpha 低于它一律按"透明"处理。</summary>
    public const byte MinOpacity = 5;

    private readonly OutputTree _output;
    private readonly string _packRoot;
    private readonly ImageCache _images;

    public EditImageApplier(OutputTree output, string packRoot, ImageCache images)
    {
        _output = output;
        _packRoot = packRoot;
        _images = images;
    }

    // ------------------------------------------------------------------ 展开

    /// <summary>
    /// CP <c>SplitPatches</c> 的（Target, FromFile）逗号笛卡尔积：两者各自逗号切分、
    /// <b>大小写不敏感去重</b>，再做笛卡尔积 —— 每个组合是一条独立补丁。
    /// <para>
    /// <b>判定（<c>Judging.PatchJudge</c>）与写入（<see cref="Apply"/> 的调用方）必须调用
    /// 这一个实现</b>，否则两处对同一条数据的条数口径不一致（Task 10b 的教训）。
    /// </para>
    /// <para>任一列表为空（如缺 <c>FromFile</c>）时返回空表。</para>
    /// </summary>
    public static IReadOnlyList<EditImageJob> ExpandJobs(ChangePatch patch)
    {
        IReadOnlyList<string> targets = SplitList(patch.Target, normalizeAsset: true);
        IReadOnlyList<string> files = SplitList(patch.FromFile, normalizeAsset: false);

        var jobs = new List<EditImageJob>();
        if (targets.Count == 0 || files.Count == 0) return jobs;

        foreach (string target in targets)
        foreach (string file in files)
            jobs.Add(new EditImageJob(target, file));

        return jobs;
    }

    /// <summary>
    /// 逗号切分 + trim + 大小写不敏感去重（保持首次出现顺序）。
    /// <para>
    /// 切法走 <see cref="Cp.AssetNameList"/> —— <c>{{Random:null, 3}}</c> 这类令牌**内部**的逗号
    /// 不是分隔符，朴素 <c>Split(',')</c> 会把它拦腰切成两个都不存在的名字。
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> SplitList(string? value, bool normalizeAsset)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string part in Cp.AssetNameList.Split(value))
        {
            string item = part.Trim();
            if (normalizeAsset) item = item.Replace('\\', '/').Trim('/');
            if (item.Length == 0) continue;
            if (seen.Add(item)) result.Add(item);
        }
        return result;
    }

    // ------------------------------------------------------------------ 字段解析

    /// <summary>
    /// 解析 <c>FromArea</c> / <c>ToArea</c>。缺省（字段不存在）返回 <c>null</c>，由合成时补默认值；
    /// 但字段存在却缺 X/Y/Width/Height 任一项 —— 按 CP 语义<b>整条跳过</b>，返回 <c>false</c>。
    /// </summary>
    public static bool TryParseAreas(ChangePatch patch, out ImageArea? fromArea, out ImageArea? toArea,
        out string? error)
    {
        fromArea = null;
        toArea = null;
        return TryParseArea(patch.Raw["FromArea"], "FromArea", out fromArea, out error)
               && TryParseArea(patch.Raw["ToArea"], "ToArea", out toArea, out error);
    }

    private static bool TryParseArea(JToken? token, string field, out ImageArea? area, out string? error)
    {
        area = null;
        error = null;
        if (token == null || token.Type == JTokenType.Null) return true;

        if (token is not JObject obj)
        {
            error = $"{field} 不是对象";
            return false;
        }

        if (!TryGetInt(obj, "X", out int x) || !TryGetInt(obj, "Y", out int y)
            || !TryGetInt(obj, "Width", out int width) || !TryGetInt(obj, "Height", out int height))
        {
            error = $"{field} 缺 X/Y/Width/Height 之一，整条跳过";
            return false;
        }

        area = new ImageArea(x, y, width, height);
        return true;
    }

    /// <summary>
    /// 读一个整数。**数字与数字字符串都收** —— CP 侧就是 <c>int.TryParse</c> 字符串，
    /// 而真实语料两种写法都有（实测 SVE 的 <c>FromArea</c> 全写 <c>"X": "0"</c> 这种字符串）。
    /// 只收 <see cref="JTokenType.Integer"/> 会把它们**整条判失败** —— 静默不写。
    /// </summary>
    private static bool TryGetInt(JObject obj, string name, out int value)
    {
        value = 0;
        JToken? token = obj.Property(name)?.Value;
        if (token == null) return false;
        if (token.Type == JTokenType.Integer) { value = token.Value<int>(); return true; }

        return token.Type == JTokenType.String
               && int.TryParse(token.Value<string>(), NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 解析 <c>PatchMode</c>。缺省 = <c>Replace</c>（<c>PatchLoader.cs:475</c>）。
    /// <c>Mask</c> 在 CP 2.9.1 里**已经不合法** —— <c>Conditions/PatchImageMode.cs</c> 只剩
    /// <c>{ Replace, Overlay }</c>（没有任何 <c>Migration_2_9</c>，枚举本身就少了它），
    /// 所以 CP 的 <c>Enum.TryParse</c> 会失败并**忽略这条补丁**；我们**报错而不是静默降级**
    /// （语料里 0 处用到）。
    /// </summary>
    public static bool TryParseMode(string? raw, out PatchImageMode mode, out string? error)
    {
        error = null;
        mode = PatchImageMode.Replace;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        string text = raw.Trim();
        if (text.Equals("Replace", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("Overlay", StringComparison.OrdinalIgnoreCase))
        {
            mode = PatchImageMode.Overlay;
            return true;
        }

        error = text.Equals("Mask", StringComparison.OrdinalIgnoreCase)
            ? "PatchMode Mask 在 CP 2.9.1 里已经不合法（`PatchImageMode` 枚举只剩 Replace/Overlay，"
              + "CP 会忽略这条补丁），本工具不实现"
            : $"未知的 PatchMode：{raw}";
        return false;
    }

    // ------------------------------------------------------------------ 写入

    /// <summary>
    /// 应用一条 <c>EditImage</c> 的一个（Target, FromFile）组合。
    /// <para>
    /// 目标必须<b>已存在</b>（原版已有资产，或同包内先有 <c>Load</c> 创建）—— CP 无法作用在
    /// 不存在的目标上（<c>GameContentManager.LoadExact</c> 无 load operation 时回落到 <c>RawLoad</c>）。
    /// </para>
    /// </summary>
    public MergeOutcome Apply(string assetName, string fromFile, PatchImageMode mode,
        ImageArea? fromArea, ImageArea? toArea, string? tokenTarget = null)
    {
        string asset = Normalize(assetName);
        string tokenName = Normalize(tokenTarget ?? assetName);

        string resolved = LoadApplier.ResolveFirstValidFile(
            LoadApplier.ExpandFieldTokens(fromFile, tokenName), _packRoot);
        if (resolved.Contains("{{", StringComparison.Ordinal))
            return MergeOutcome.Fail($"FromFile 含无法静态解析的令牌：{fromFile}");

        string full;
        try { full = Path.GetFullPath(Path.Combine(_packRoot, resolved)); }
        catch (Exception ex) { return MergeOutcome.Fail($"FromFile 路径无效：{ex.Message}"); }

        if (!File.Exists(full))
            return MergeOutcome.Fail($"找不到 FromFile：{resolved}");

        string ext = Path.GetExtension(resolved);
        if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return MergeOutcome.Fail($"FromFile `{resolved}` 不是图片（扩展名 {ext}），EditImage 只处理 .png");

        string pngRel = AssetCreator.RelativePath(asset, ".png");

        // 坏路径（非法字符 / 保留设备名 / 超长）降级为该条补丁写入失败，不抛异常、不终止整次运行。
        string? invalidPath = _output.ValidatePath(pngRel);
        if (invalidPath != null)
            return MergeOutcome.Fail($"目标资产 `{asset}` 的输出路径非法：{invalidPath}");

        // 源图走解码缓存：同一张源图常被贴到多个目标上，逐个补丁 `Png.Read` 就是白读。
        if (!_images.TryReadSource(full, out byte[] source, out int sourceWidth, out int sourceHeight,
                out string? sourceError))
            return MergeOutcome.Fail(sourceError!);

        // FromArea 缺省 = 整张源图；ToArea 缺省 = (0,0,FromArea.W,FromArea.H)
        ImageArea sourceArea = fromArea ?? new ImageArea(0, 0, sourceWidth, sourceHeight);
        ImageArea targetArea = toArea ?? new ImageArea(0, 0, sourceArea.Width, sourceArea.Height);

        // 读走 `ImageCache` 而不是磁盘：同一张图被多条补丁改时，后面那条必须看到前面那条的成果，
        // 而磁盘上那份要到本次运行结束才落。目标不存在时**先判**，报的还是原来那句。
        bool ApplyTo(string rel, bool required, out string? failure)
        {
            failure = null;
            if (!_images.Exists(rel))
            {
                failure = $"EditImage 目标 `{asset}` 不存在 —— CP 无法作用在不存在的目标上"
                          + "（目标须是原版资产或同包先 Load 创建）";
                return !required;
            }
            if (!_images.TryGetPixels(rel, out byte[] target, out int targetWidth, out int targetHeight,
                    out string? targetError))
            {
                failure = targetError!;
                return !required;
            }
            if (!TryCompose(target, targetWidth, targetHeight, source, sourceWidth, sourceHeight,
                    sourceArea, targetArea, mode, out byte[] result, out int width, out int height,
                    out string? error))
            {
                failure = error!;
                return !required;
            }
            // Overlay 源区域整块全透明 = 什么都没做：不写回（保持原字节 / 保持 Load 进来的那份）。
            if (!ReferenceEquals(result, target)) _images.SetPixels(rel, result, width, height);
            return true;
        }

        if (!ApplyTo(pngRel, required: true, out string? failure)) return MergeOutcome.Fail(failure!);

        // ★ **已存在的语言变体也要应用同一条补丁**：中文存档加载的是 `asset.zh-CN`
        //   （`LocalizedContentManager` 先要带语言后缀的那份）。只改基础资产，玩家看不到。
        //   变体的尺寸/内容可能不同 —— 合成失败只记 note，不把整条补丁判失败。
        List<string>? notes = null;
        foreach (string localized in LocaleVariants.Existing(_images.Exists, asset, ".png"))
        {
            if (ApplyTo(AssetCreator.RelativePath(localized, ".png"), required: false, out string? why))
                continue;
            (notes ??= new List<string>()).Add($"语言变体 `{localized}` 未镜像：{why}");
        }

        return notes == null ? MergeOutcome.Ok() : MergeOutcome.Ok(null, notes);
    }

    // ------------------------------------------------------------------ 合成（纯函数）

    /// <summary>
    /// 把 <paramref name="source"/> 的 <paramref name="fromArea"/> 区域合成进
    /// <paramref name="target"/> 的 <paramref name="toArea"/> 区域，返回新缓冲区。
    /// <para>
    /// 约束（research §3）：两区域宽高必须相等；<c>toArea.Right</c> 不得超过目标宽度；
    /// 目标不够高时<b>向下扩展画布</b>（补透明行）到 <c>toArea.Bottom</c>。
    /// </para>
    /// <para>
    /// 纯函数、不碰文件 —— 混合公式的精确用例直接测它，不必造 PNG。
    /// </para>
    /// </summary>
    public static bool TryCompose(
        byte[] target, int targetWidth, int targetHeight,
        byte[] source, int sourceWidth, int sourceHeight,
        ImageArea fromArea, ImageArea toArea, PatchImageMode mode,
        out byte[] result, out int resultWidth, out int resultHeight, out string? error)
    {
        result = target;
        resultWidth = targetWidth;
        resultHeight = targetHeight;
        error = null;

        if (fromArea.Width <= 0 || fromArea.Height <= 0
            || fromArea.X < 0 || fromArea.Y < 0
            || fromArea.Right > sourceWidth || fromArea.Bottom > sourceHeight)
        {
            error = $"FromArea {fromArea} 超出源图 {sourceWidth}x{sourceHeight}";
            return false;
        }

        if (toArea.X < 0 || toArea.Y < 0)
        {
            error = $"ToArea {toArea} 的 X/Y 不能为负";
            return false;
        }

        if (toArea.Width != fromArea.Width || toArea.Height != fromArea.Height)
        {
            error = $"FromArea 与 ToArea 尺寸不相等（{fromArea.Width}x{fromArea.Height} vs " +
                    $"{toArea.Width}x{toArea.Height}），整条跳过";
            return false;
        }

        // 宽度不能靠扩展画布解决 —— 超出即报错跳过（高度才可以向下扩展）。
        if (toArea.Right > targetWidth)
        {
            error = $"ToArea 右边 {toArea.Right} 超出目标宽度 {targetWidth}";
            return false;
        }

        // Overlay：先找源区域内首/末非透明行，把合成范围裁到 [minRow, maxRow]。
        int yLo = 0;
        int yHi = fromArea.Height - 1;
        if (mode == PatchImageMode.Overlay)
        {
            int minRow = -1, maxRow = -1;
            for (int y = 0; y < fromArea.Height; y++)
            {
                for (int x = 0; x < fromArea.Width; x++)
                {
                    int alpha = ((fromArea.Y + y) * sourceWidth + (fromArea.X + x)) * 4 + 3;
                    if (source[alpha] >= MinOpacity)
                    {
                        if (minRow < 0) minRow = y;
                        maxRow = y;
                        break;
                    }
                }
            }

            // 源区域整块全透明 → 直接返回，什么都不做。
            if (minRow < 0) return true;
            yLo = minRow;
            yHi = maxRow;
        }

        // 向下扩展画布（补透明行）到合成区域的底边；目标原有的行原样保留。
        // 用**裁剪后**的底边（toArea.Y + yHi + 1），与 SMAPI 先裁首末行、再 ExtendImage 的顺序一致。
        int newHeight = Math.Max(targetHeight, toArea.Y + yHi + 1);
        resultWidth = targetWidth;
        resultHeight = newHeight;
        result = ByteBufferPool.Rent(targetWidth * newHeight * 4);
        Array.Copy(target, result, Math.Min(target.Length, result.Length));

        for (int y = yLo; y <= yHi; y++)
        {
            int sourceRow = fromArea.Y + y;
            int targetRow = toArea.Y + y;
            for (int x = 0; x < fromArea.Width; x++)
            {
                int s = (sourceRow * sourceWidth + (fromArea.X + x)) * 4;
                int d = (targetRow * targetWidth + (toArea.X + x)) * 4;

                if (mode == PatchImageMode.Replace)
                {
                    // Replace 整块直拷（含 alpha）—— 哪怕源像素全透明，也覆盖目标。
                    result[d] = source[s];
                    result[d + 1] = source[s + 1];
                    result[d + 2] = source[s + 2];
                    result[d + 3] = source[s + 3];
                    continue;
                }

                byte sourceAlpha = source[s + 3];
                if (sourceAlpha < MinOpacity) continue;              // 源透明 → 保留目标

                byte targetAlpha = result[d + 3];
                if (sourceAlpha == 255 || targetAlpha < MinOpacity)  // 源不透明 / 目标透明 → 目标 := 源
                {
                    result[d] = source[s];
                    result[d + 1] = source[s + 1];
                    result[d + 2] = source[s + 2];
                    result[d + 3] = source[s + 3];
                    continue;
                }

                // 半透明混合（SMAPI 近似实现，**不是**标准 source-over）：
                //   k = 1 - src.A/255
                //   R = src.R + dst.R*k（G/B 同理）
                //   A = max(src.A, dst.A)
                float k = 1f - sourceAlpha / 255f;
                result[d] = Clamp(source[s] + result[d] * k);
                result[d + 1] = Clamp(source[s + 1] + result[d + 1] * k);
                result[d + 2] = Clamp(source[s + 2] + result[d + 2] * k);
                result[d + 3] = Math.Max(sourceAlpha, targetAlpha);
            }
        }

        return true;
    }

    /// <summary>
    /// 把 <c>src + dst*k</c> 的结果收进 byte —— 与 MonoGame <c>Color(int,int,int,int)</c>
    /// 的 Clamp(0,255) 行为一致（<c>k&gt;0</c> 时该式可超过 255，必须夹紧）。
    /// </summary>
    private static byte Clamp(float value) =>
        value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)value;

    private static string Normalize(string name) => name.Replace('\\', '/').Trim('/');
}
