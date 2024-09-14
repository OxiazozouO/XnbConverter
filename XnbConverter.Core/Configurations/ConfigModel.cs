namespace XnbConverter.Configurations;

public class ConfigModel
{
    public string? Locale { get; set; }
    public bool LogTime { get; set; }
    public string? TimeFormat { get; set; }
    public LogLevels LogPrintingOptions { get; set; }
    public LogLevels LogSaveOptions { get; set; }
    public int Concurrency { get; set; }

    [Flags]
    public enum LogLevels
    {
        Info = 1,
        Warn = 2,
        Error = 4,
        Debug = 8
    }
}