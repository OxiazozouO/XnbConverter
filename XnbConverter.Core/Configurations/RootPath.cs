using XnbConverter.Readers;
using XnbConverter.Utilities;

namespace XnbConverter.Configurations;

public abstract class RootPath
{
    public string? BasePath = null;
    public string? CachePath = null;
    public string ConfigFilePath = null;
    public string ExDllPath = null;
    public string XnbCachePath = null;
    public string UiConfigFilePath = null;
    public string LogPath = null;

    public RootPath() => ConfigHelper.Instance = this;

    public abstract void Init();

    public void TryCreateConfigFile()
    {
        if (File.Exists(ConfigFilePath)) return;
        var json = ConfigHelper.GetJson(ConfigHelper.Default);
        File.WriteAllText(ConfigFilePath, json);
    }

    public void TryReadConfigFile()
    {
        if (File.Exists(ConfigFilePath))
            ConfigHelper.Config = ConfigFilePath.ToEntity<ConfigModel>();
    }

    protected static void TryMkdirs(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (Directory.GetParent(path)?.Exists ?? false)
                Directory.CreateDirectory(path);
        }
    }

    public void InitDll()
    {
        var files = Directory.GetFiles(ExDllPath);
        foreach (var file in files)
        {
            TypeReadHelper.InitExtendTypes(File.ReadAllBytes(file));
        }
    }
}