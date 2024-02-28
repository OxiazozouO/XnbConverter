using System.Globalization;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace XnbConverter.Utilities;

public static class Helpers
{
    public static class SysPath
    {
        public const string Config = @".\.config\config.json";
        public const string I18N = @".\.config\i18n\{0}.json";
        public const string Dll = @".\.config\custom_dll.json";
        public static readonly string FFmpeg = Path.GetFullPath(@".config\bin\ffmpeg\ffmpeg.exe");
    }


    private static Configuration? _config;
    private static Configuration? _configTmp;

    public static Configuration Config
    {
        get
        {
            _config ??= GetObject<Configuration>(SysPath.Config) ?? throw new InvalidOperationException();
            return _config;
        }
    }

    public static void EnableMultithreading()
    {
        _configTmp = _config;
        _config = _configTmp.Copy(2 | 4, 2 | 4, _configTmp.Concurrency);
    }

    public static void TurnOffMultithreading()
    {
        _config = _configTmp;
    }

    private static Dictionary<string, string>? _textTip;

    public static Dictionary<string, string> I18N
    {
        get
        {
            if (_textTip is not null) return _textTip;
            var filename = "error";
            if (Config.Locale == "auto")
            {
                filename += '.' + CultureInfo.InstalledUICulture.ToString();
            }

            _textTip ??= GetObject<Dictionary<string, string>>(string.Format(SysPath.I18N, filename));

            return _textTip;
        }
    }

    private static T GetObject<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileLoadException($"配置文件{path}不存在！");

        return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ??
               throw new FileLoadException("读取json失败！");
    }

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

        [JsonProperty] public string Locale;
    }
}