using XnbConverter.Configurations;
using Environment = Android.OS.Environment;

namespace XnbConverter.Android.Configurations;

public class AndroidRootPath : RootPath
{
    public static void Build() => new AndroidRootPath().Init();

    public override void Init()
    {
        var root = Application.Context;
        BasePath ??= Environment.ExternalStorageState == Environment.MediaMounted
            ? Environment.ExternalStorageDirectory?.AbsolutePath
            : Environment.DirectoryDownloads ?? Environment.DataDirectory?.AbsolutePath;
        CachePath = Path.Combine(BasePath, root.GetString(Resource.String.app_name)+".cache");

        LogPath = CachePath;
        XnbCachePath = Path.Combine(CachePath, ".xnb file cache");
        UiConfigFilePath = Path.Combine(XnbCachePath, "ui config.json");
        ConfigFilePath = Path.Combine(CachePath, "config.json");
        ExDllPath = Path.Combine(CachePath, "custom dlls");

        TryMkdirs(CachePath, XnbCachePath, ExDllPath);

        TryCreateConfigFile();
        TryReadConfigFile();
        InitDll();
    }
}