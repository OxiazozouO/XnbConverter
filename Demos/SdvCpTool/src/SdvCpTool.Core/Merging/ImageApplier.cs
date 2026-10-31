using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Merging;

/// <summary>
/// CP <c>Load</c> 的图片分支：把内容包里的 <c>.png</c> <b>逐字节</b>复制到输出树的对应资产名，
/// 并从同类图片资产复制一份 <c>.config</c> 模板（根 Reader 是 <c>Texture2DReader</c>）。
/// <para>
/// 图片层不需要 XNB 解析 —— 解包树里图片资产就是 <c>.png</c> + <c>.config</c>，
/// 因此这里只做 <see cref="File.Copy(string,string,bool)"/>，绝不重新编码 PNG。
/// </para>
/// </summary>
public sealed class ImageApplier
{
    private readonly OutputTree _output;
    private readonly string _packRoot;
    private readonly ImageCache _images;

    public ImageApplier(OutputTree output, string packRoot, ImageCache images)
    {
        _output = output;
        _packRoot = packRoot;
        _images = images;
    }

    /// <summary>
    /// 应用一条图片 <c>Load</c>。<paramref name="assetName"/> 是 <c>Target</c>；
    /// <paramref name="fromFile"/> 是相对内容包根目录的 <c>.png</c> 路径，可含字段引用令牌；
    /// <paramref name="tokenTarget"/> 省略时等同 <paramref name="assetName"/>。
    /// </summary>
    public MergeOutcome Apply(string assetName, string fromFile, string? tokenTarget = null)
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
            return MergeOutcome.Fail(
                $"FromFile `{resolved}` 不是图片资产（扩展名 {ext}），图片层只处理 .png");

        string pngRel = AssetCreator.RelativePath(asset, ".png");

        // 目标位置上已经压着**另一类**资产的描述符（地图 `.tbin` / 数据 `.json`）⇒ 不许盖：
        // XNB 的类型由 `.config` 说了算，盖掉之后游戏会拿 Texture2D 当地图 / 数据用。
        // 实测 `Maps/Coop`：原版地图，被包内图集 `Coop.png` 盖成贴图 ⇒ 进鸡舍就炸。
        if (AssetCreator.NonImageOccupant(_output, asset) is string occupant)
            return MergeOutcome.Fail(
                $"目标 `{asset}` 处已有非图片资产（`{asset}{occupant}`），图片不能盖上去：" +
                "同一个内容键上不能同时是图片与地图 / 数据两类资产");

        // 坏路径（非法字符 / 保留设备名 / 超长）降级为该条补丁写入失败，不抛异常、不终止整次运行。
        string? invalidPath = _output.ValidatePath(pngRel);
        if (invalidPath != null)
            return MergeOutcome.Fail($"目标资产 `{asset}` 的输出路径非法：{invalidPath}");

        bool isNew = !_images.Exists(pngRel);

        // 新建图片资产必须先确认有可复制的图片 .config —— 否则不写任何文件，避免产出回不了包的半成品
        if (isNew && AssetCreator.FindImageConfigTemplate(_output, asset) == null)
            return MergeOutcome.Fail($"新建图片资产 `{asset}` 找不到可复制的图片 .config 模板（Texture2DReader）");

        try
        {
            // 逐字节，与源文件一致，不重新编码。
            // ⚠ 交给 `ImageCache` 而不是 `StageCopy`：只**记下最终来源**，到 `ImageCache.Flush()` 才拷。
            //   图片资产由 ImageCache 独家持有 —— 同一次运行里 `EditImage` 要读它、还要覆盖它，
            //   两条路径各写各的就会互相盖。
            _images.NoteWholeFile(pngRel, full);
        }
        catch (Exception ex)
        {
            return MergeOutcome.Fail($"写入图片资产 `{asset}` 失败：{ex.Message}");
        }

        if (isNew)
        {
            _output.CreatedFiles.Add(pngRel);
            AssetCreator.CopyImageConfig(_output, asset);
        }

        // ★ 同一个操作也要落到**已存在的语言变体**上：中文存档加载的是
        //   `asset.zh-CN`（`LocalizedContentManager` 先要带语言后缀的那份），只改基础资产
        //   玩家看不到。CP 按基础名匹配、运行时作用在被请求的变体上 —— 静态产出要等价。
        foreach (string localized in LocaleVariants.Existing(_images.Exists, asset, ".png"))
            _images.NoteWholeFile(AssetCreator.RelativePath(localized, ".png"), full);

        return MergeOutcome.Ok();
    }

    private static string Normalize(string name) => name.Replace('\\', '/').Trim('/');
}
