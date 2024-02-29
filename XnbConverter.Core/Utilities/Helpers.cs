using System.Globalization;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace XnbConverter.Utilities;

public static class Helpers
{
    static Helpers()
    {
        _config ??= SysPath.Config.ToEntity<Configuration>();
        var filename = "error";
        if (Config.Locale == "auto")
        {
            filename += '.' + CultureInfo.InstalledUICulture.ToString();
        }

        _textTip ??= string.Format(SysPath.I18N, filename).ToEntity<Dictionary<string, string>>();
    }

    public static class SysPath
    {
        public static string Config = Path.GetFullPath(@".\.config\config.json");
        public static string I18N = Path.GetFullPath(@".\.config\i18n\{0}.json");
        public static string Dll = Path.GetFullPath(@".\.config\custom_dll.json");
        public static string FFmpeg = Path.GetFullPath(@".\.config\ffmpeg\ffmpeg.exe");
    }


    private static Configuration? _config;
    private static Configuration? _configTmp;

    public static Configuration Config => _config;

    public static void EnableMultithreading()
    {
        _configTmp = _config;
        _config = _configTmp.Copy(2 | 4, 2 | 4, _configTmp.Concurrency);
    }

    public static void TurnOffMultithreading()
    {
        _config = _configTmp;
    }

    private static readonly Dictionary<string, string>? _textTip;

    public static Dictionary<string, string> I18N => _textTip;
    public static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr handle, out int mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int handle = -11);

        public static void Init()
        {
            var handle = GetStdHandle();
            GetConsoleMode(handle, out var mode);
            SetConsoleMode(handle, mode | 0x4);
        }
    }

    public class Configuration
    {
        [Flags]
        [JsonConverter(typeof(StringEnumConverter))]
        private enum LogLevels
        {
            Info = 1,
            Warn = 2,
            Error = 4,
            Debug = 8
        }

        public Configuration Copy(int printMod, int saveMod, int concurrency)
        {
            var result = new Configuration()
            {
                LogTime = LogTime,
                TimeFormat = TimeFormat,
                Concurrency = concurrency,
                LogPrintingOptions = (LogLevels)printMod,
                LogSaveOptions = (LogLevels)saveMod
            };
            return result;
        }

        public bool LogTime { get; private init; }
        public string TimeFormat { get; private init; }

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

        [JsonIgnore] private readonly int _concurrency;

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
        public string Locale { get; private init; }
    }
}