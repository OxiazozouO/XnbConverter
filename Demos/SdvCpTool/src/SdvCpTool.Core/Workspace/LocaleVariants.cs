namespace SdvCpTool.Core.Workspace;

/// <summary>
/// 资产的**语言变体**（<c>asset.zh-CN.png</c> / <c>asset.ja-JP.json</c> …）。
///
/// <para>
/// 游戏的 <c>LocalizedContentManager</c> 对**所有资产类型**都是"先要 <c>asset.&lt;语言&gt;</c>、
/// 没有再回落基础资产"。所以中文存档加载的是 <c>Maps/spring_outdoorsTileSheet.zh-CN</c>、
/// <c>Minigames/TitleButtons.zh-CN</c> —— 只改基础资产，玩家**根本看不到**。
/// </para>
/// <para>
/// 实测（2026-10-19，业主报"主界面/季节图块没替换"）：CP 的补丁按**基础名**匹配，
/// 运行时作用在请求的那个语言变体上；静态产出必须把同一个操作也镜像到已存在的变体。
/// </para>
/// </summary>
public static class LocaleVariants
{
    /// <summary>
    /// 本产出**确定化**到的语言（与 <c>Baker.FrozenLanguage = "zh"</c> 对应）。
    /// 数据资产的逐补丁镜像只写这一份 —— 目标存档就是中文；写全 11 种语言只是成倍 IO。
    /// </summary>
    public const string FrozenSuffix = ".zh-CN";

    /// <summary>SDV 1.6 随游戏发布的语言后缀（长码，与资产文件名一致）。</summary>
    public static readonly IReadOnlyList<string> Suffixes = new[]
    {
        ".de-DE", ".es-ES", ".fr-FR", ".hu-HU", ".it-IT", ".ja-JP",
        ".ko-KR", ".pt-BR", ".ru-RU", ".tr-TR", ".zh-CN",
    };

    /// <summary>
    /// 已经存在的语言变体资产名（不含扩展名）。
    /// <paramref name="exists"/> 由调用方给（图片要问 <c>ImageCache</c>，数据问 <c>OutputTree</c>）。
    /// </summary>
    public static IEnumerable<string> Existing(Func<string, bool> exists, string assetWithExt, string ext)
    {
        string stem = assetWithExt.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
            ? assetWithExt[..^ext.Length]
            : assetWithExt;
        foreach (string suffix in Suffixes)
        {
            string candidate = stem + suffix;
            if (exists(candidate + ext)) yield return candidate;
        }
    }
}
