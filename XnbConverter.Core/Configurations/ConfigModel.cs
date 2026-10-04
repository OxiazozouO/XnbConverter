namespace XnbConverter.Configurations;

public class ConfigModel
{
    public string? Locale { get; set; }
    public bool LogTime { get; set; }
    public string? TimeFormat { get; set; }
    public LogLevels LogPrintingOptions { get; set; }
    public LogLevels LogSaveOptions { get; set; }
    public int Concurrency { get; set; }

    /// <summary>
    /// PNG 的 DEFLATE 档位："fastest"（默认，速度优先）或 "optimal"（体积优先）。
    /// 没写这个键时按 fastest 处理。
    ///
    /// 实测（完整 Content，3550 个 xnb，7555 个产出文件）：
    ///   fastest  解包 4.91 s，输出 398 MB（其中 PNG 71.25 MB）
    ///   optimal  解包 5.58 s，输出 378 MB（其中 PNG 51.10 MB）
    /// 代价集中在 Maps（+11.2 MB）和 LooseSprites（+4.2 MB）；Fonts 只涨 15%，
    /// 因为字体图集大多已进调色板，喂给 DEFLATE 的数据本就只剩 1/4~1/8。
    ///
    /// 两者产出的 PNG 都是合法文件，任何 libpng 系解码器（含 MonoGame 3.8 的 StbImageSharp）
    /// 都能读；打包回 XNB 的结果也逐字节相同。
    /// </summary>
    public string? PngCompression { get; set; }

    [Flags]
    public enum LogLevels
    {
        Info = 1,
        Warn = 2,
        Error = 4,
        Debug = 8
    }
}