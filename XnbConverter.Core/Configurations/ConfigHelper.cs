using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XnbConverter.Utilities;

namespace XnbConverter.Configurations;

public static class ConfigHelper
{
    public static readonly ConfigModel Default = new()
    {
        Locale = CultureInfo.CurrentCulture.Name,
        LogTime = false,
        TimeFormat = "MM-dd HH:mm:ss",
        LogPrintingOptions = ConfigModel.LogLevels.Info | ConfigModel.LogLevels.Warn | ConfigModel.LogLevels.Error,
        LogSaveOptions = ConfigModel.LogLevels.Error,
        Concurrency = 15
    };

    public static string GetJson(ConfigModel model)
    {
        var obj = JsonConvert.DeserializeObject<JObject>(JsonConvert.SerializeObject(model, FileUtils.Settings),
            FileUtils.Settings);

        return JsonConvert.SerializeObject(obj, FileUtils.Settings);
    }

    private static ConfigModel? _configTmp;


    #region 对外

    private static ConfigModel? _config;

    public static ConfigModel Config
    {
        set => _config = value;
        get
        {
            _config ??= Default;
            return _config;
        }
    }


    private static CultureInfo _cultureInfo = CultureInfo.CurrentCulture;

    public static CultureInfo Culture
    {
        get
        {
            _cultureInfo = string.IsNullOrEmpty(Config.Locale) || Config.Locale == "auto"
                ? CultureInfo.CurrentCulture
                : CultureInfo.GetCultureInfo(Config.Locale);
            return _cultureInfo;
        }
    }

    public static bool LogTime => Config.LogTime;

    public static string? TimeFormat => Config.TimeFormat;

    public static int Concurrency => Math.Min(Math.Max(1, Config.Concurrency), 15);

    public static bool PInfo => (Config.LogPrintingOptions & ConfigModel.LogLevels.Info) != 0;
    public static bool PWarn => (Config.LogPrintingOptions & ConfigModel.LogLevels.Warn) != 0;
    public static bool PError => (Config.LogPrintingOptions & ConfigModel.LogLevels.Error) != 0;
    public static bool PDebug => (Config.LogPrintingOptions & ConfigModel.LogLevels.Debug) != 0;

    public static bool SInfo => (Config.LogSaveOptions & ConfigModel.LogLevels.Info) != 0;
    public static bool SWarn => (Config.LogSaveOptions & ConfigModel.LogLevels.Warn) != 0;
    public static bool SError => (Config.LogSaveOptions & ConfigModel.LogLevels.Error) != 0;
    public static bool SDebug => (Config.LogSaveOptions & ConfigModel.LogLevels.Debug) != 0;

    #endregion

    private static ConfigModel Copy(this ConfigModel root, int printMod, int saveMod, int concurrency)
    {
        return new ConfigModel
        {
            LogTime = root.LogTime,
            TimeFormat = root.TimeFormat,
            Concurrency = concurrency,
            LogPrintingOptions = (ConfigModel.LogLevels)printMod,
            LogSaveOptions = (ConfigModel.LogLevels)saveMod
        };
    }

    public static void EnableMultithreading()
    {
        _configTmp = Config;
        Config = _configTmp.Copy(6, 6, _configTmp.Concurrency);
    }

    public static void TurnOffMultithreading()
    {
        Config = _configTmp;
    }


    public static RootPath? Instance;

    public static string? BasePath => Instance?.BasePath;
    public static string? CachePath => Instance?.CachePath;
    public static string ConfigFilePath => Instance?.ConfigFilePath ?? "";
    public static string ExDllPath => Instance?.ExDllPath ?? "";

    public static string XnbCachePath = Instance?.XnbCachePath ?? "";
    public static string UiConfigFilePath = Instance?.UiConfigFilePath ?? "";
}