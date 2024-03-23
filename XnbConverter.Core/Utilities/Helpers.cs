using System.Globalization;
using Newtonsoft.Json;

namespace XnbConverter.Utilities;

public static class Helpers
{
    private static Configuration? _config;
    private static Configuration? _configTmp;

    private static readonly string[] LocaleNames =
    {
        "zh-CN",
        "default",
        "de-DE",
        "es-ES",
        "fr-FR",
        "hu-HU",
        "it-IT",
        "ja-JP",
        "ko-KR",
        "pt-BR",
        "ru-RU",
        "tr-TR"
    };

    private static readonly Dictionary<string, string>? _textTip;

    static Helpers()
    {
        _config ??= SysPath.Cfg.ToEntity<Configuration>();
        string filename = "error";
        if (Config.Locale == "auto")
        {
            string s = CultureInfo.InstalledUICulture.ToString();
            if (LocaleNames.Contains(s))
            {
                filename += '.' + s;
            }
        }
        else if (LocaleNames.Contains(Config.Locale))
        {
            filename += '.' + Config.Locale;
        }

        _textTip ??= string.Format(SysPath.I18NPath, filename).ToEntity<Dictionary<string, string>>();
    }

    public static Configuration Config => _config;

    public static Dictionary<string, string> I18N => _textTip;

    public static void EnableMultithreading()
    {
        _configTmp = _config;
        _config = _configTmp.Copy(2 | 4, 2 | 4, _configTmp.Concurrency);
    }

    public static void TurnOffMultithreading()
    {
        _config = _configTmp;
    }

    public static class SysPath
    {
        public static readonly string Cfg = Path.GetFullPath(@".\.config\config.json");
        public static readonly string I18NPath = Path.GetFullPath(@".\.config\i18n\{0}.json");
        public static readonly string Dll = Path.GetFullPath(@".\.config\custom_dll.json");
        public static readonly string FFmpeg = Path.GetFullPath(@".\.config\ffmpeg\ffmpeg.exe");
    }

    public class Configuration
    {
        [JsonIgnore] private readonly int _concurrency;

        public bool LogTime { get; private init; }
        public string? TimeFormat { get; private init; }

        [JsonProperty]
        private LogLevels LogPrintingOptions
        {
            set
            {
                PInfo = (value & LogLevels.Info) != 0;
                PWarn = (value & LogLevels.Warn) != 0;
                PError = (value & LogLevels.Error) != 0;
                PDebug = (value & LogLevels.Debug) != 0;
            }
        }

        [JsonProperty]
        private LogLevels LogSaveOptions
        {
            set
            {
                SInfo = (value & LogLevels.Info) != 0;
                SWarn = (value & LogLevels.Warn) != 0;
                SError = (value & LogLevels.Error) != 0;
                SDebug = (value & LogLevels.Debug) != 0;
            }
        }

        [JsonProperty]
        public int Concurrency
        {
            get => _concurrency;
            private init => _concurrency = Math.Min(Math.Max(1, value), 15);
        }

        [JsonIgnore] public bool PInfo { get; private set; }
        [JsonIgnore] public bool PWarn { get; private set; }
        [JsonIgnore] public bool PError { get; private set; }
        [JsonIgnore] public bool PDebug { get; private set; }
        [JsonIgnore] public bool SInfo { get; private set; }
        [JsonIgnore] public bool SWarn { get; private set; }
        [JsonIgnore] public bool SError { get; private set; }
        [JsonIgnore] public bool SDebug { get; private set; }

        [JsonProperty]
        // [field: JsonIgnore]
        public string? Locale { get; private init; }

        public Configuration Copy(int printMod, int saveMod, int concurrency)
        {
            var result = new Configuration
            {
                LogTime = LogTime,
                TimeFormat = TimeFormat,
                Concurrency = concurrency,
                LogPrintingOptions = (LogLevels)printMod,
                LogSaveOptions = (LogLevels)saveMod
            };
            return result;
        }

        [Flags]
        private enum LogLevels
        {
            Info = 1,
            Warn = 2,
            Error = 4,
            Debug = 8
        }
    }
}