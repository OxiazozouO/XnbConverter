using System.Reflection;
using System.Text;
using XnbConverter.Configurations;

namespace XnbConverter.Cli.Configurations;

public class ConsoleRootPath : RootPath
{
    public static void Build() => new ConsoleRootPath().Init();

    public override void Init()
    {
        BasePath ??= ".";

        CachePath = Path.Combine(BasePath, ".config");
        LogPath = BasePath;
        ConfigFilePath = Path.Combine(CachePath, "config.json");
        ExDllPath = Path.Combine(CachePath, "custom dll");
        XnbCachePath = Path.Combine(CachePath, "xnb cache");
        TryMkdirs(CachePath, ExDllPath);
        // TryMkdirs(CachePath, XnbCachePath);

        TryCreateConfigFile();
        TryReadConfigFile();
        InitDll();

        Error.Culture = ConfigHelper.Culture;

        string? name = Assembly.GetExecutingAssembly().GetName().Name;
        var packedPath = Path.Combine(BasePath, "packed");
        var unpackedPath = Path.Combine(BasePath, "unpacked");
        var packedFilePath = Path.Combine(BasePath, "pack.bat");
        var unpackedFilePath = Path.Combine(BasePath, "unpack.bat");


        var packedBat =
            $"@ECHO OFF&chcp 65001>NUL\r\ncall \"{name}.exe\" \"pack -c -i {unpackedPath} -o {packedPath}\"\r\npause";
        var unpackedBat =
            $"@ECHO OFF&chcp 65001>NUL\r\ncall \"{name}.exe\" \"unpack -c -i {packedPath} -o {unpackedPath}\"\r\npause";
        TryMkdirs(packedPath, unpackedPath);
        File.WriteAllText(packedFilePath, packedBat, Encoding.Default);
        File.WriteAllText(unpackedFilePath, unpackedBat, Encoding.Default);
    }
}