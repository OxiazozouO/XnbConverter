using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
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

    /// <summary>自定义 dll 目录里「程序集简单名 → 文件路径」的索引。</summary>
    private static readonly Dictionary<string, string> ExtDllFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>随工具内置的替身程序集（简单名 → 字节），自定义 dll 目录里没有同名文件时才用。</summary>
    private static readonly Dictionary<string, byte[]> BuiltinAssemblies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>兜底解析已经加载过的程序集，避免同一个名字被反复载入。</summary>
    private static readonly Dictionary<string, Assembly> ExtLoaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>内置替身的简单名（<c>MonoGame.Framework</c>）。</summary>
    private const string BuiltinShim = "MonoGame.Framework";

    /// <summary>替身所在的子目录，相对 exe。见 XnbConverter.XnaShim 的说明。</summary>
    private const string BuiltinShimDir = "xna-shim";

    private static bool _resolverHooked;

    /// <summary>
    /// 程序集一加载就挂好解析兜底、读好内置替身。
    ///
    /// 必须这么早：Point / Rectangle / Vector2 就住在替身程序集里，本程序集任意一个方法签名、
    /// 字段类型都可能触发对它的解析 —— 等到 InitDll 再挂就晚了。
    /// </summary>
    [ModuleInitializer]
    internal static void InitResolver()
    {
        LoadBuiltinShim();
        HookExtDllResolver();
    }

    public void InitDll()
    {
        if (!Directory.Exists(ExDllPath)) return;

        // 1) 把各文件里**内嵌**的托管程序集掏出来（如 Terraria.exe 里的 ReLogic.dll）。
        //    掏出来的东西和自定义 dll 一视同仁 —— 照样参与加载与类型解析。
        string extractedPath = Path.Combine(ExDllPath, "extracted");
        foreach (var file in Directory.GetFiles(ExDllPath))
        {
            EmbeddedAssemblies.Extract(file, extractedPath);
        }

        // 2) 加载目录内的托管程序集（含刚提取出来的），但**跳过 .exe**：
        //    把 exe 放进来只是为了掏它内嵌的 dll，exe 自身缺一堆依赖，加载必然失败、只会刷 warn。
        //    个别文件失败只跳过 —— 之前这里没有 try/catch，目录里放一个原生 dll 就会让整个工具启动失败。
        var candidates = ManagedFiles(ExDllPath);
        if (Directory.Exists(extractedPath))
        {
            candidates.AddRange(ManagedFiles(extractedPath));
        }

        // 3) 先建索引、挂上解析兜底，最后才真正加载。
        //    游戏的数据程序集常引用与游戏本体版本不同的依赖：StardewValley.GameData.dll 引用的是
        //    MonoGame.Framework 3.6.0.862，游戏自带的却是 3.8.0.1641。按默认规则找不到，整个程序集
        //    会被 GetExportedTypes() 的 FileNotFoundException 连带跳过（只留一条 warn），于是
        //    Data/Locations、Data/Objects 这类对象型数据全都解析不出读取器。这里改成只认简单名、忽略版本。
        foreach (var file in candidates)
        {
            try
            {
                if (!EmbeddedAssemblies.IsManagedAssembly(file)) continue;
                string? name = AssemblyName.GetAssemblyName(file).Name;
                if (name != null) ExtDllFiles[name] = file;
            }
            catch (Exception ex)
            {
                Logger.Warn(Error.EmbeddedAssemblies_2, file, ex.Message);
            }
        }

        foreach (var file in candidates)
        {
            try
            {
                if (!EmbeddedAssemblies.IsManagedAssembly(file)) continue;
                TypeReadHelper.InitExtendTypes(File.ReadAllBytes(file));
            }
            catch (Exception ex)
            {
                Logger.Warn(Error.EmbeddedAssemblies_2, file, ex.Message);
            }
        }
    }

    /// <summary>
    /// 目录下的候选程序集。跳过 .exe —— 放 exe 进来是为了提取它内嵌的 dll，
    /// exe 自身（游戏主程序）缺一堆依赖，加载没有意义，只会刷一条 warn。
    /// </summary>
    private static List<string> ManagedFiles(string dir)
    {
        return Directory.GetFiles(dir)
            .Where(f => !f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 把内置替身读进内存备用。自定义 dll 目录里放了 MonoGame.Framework 就用那个 ——
    /// 那个是真货，能顶住 xTile.dll 之类需要更多 MonoGame 类型的程序集。
    /// </summary>
    private static void LoadBuiltinShim()
    {
        if (BuiltinAssemblies.ContainsKey(BuiltinShim)) return;

        string file = Path.Combine(AppContext.BaseDirectory, BuiltinShimDir, BuiltinShim + ".dll");
        if (!File.Exists(file)) return;

        BuiltinAssemblies[BuiltinShim] = File.ReadAllBytes(file);
    }

    /// <summary>
    /// 让运行时在找不到引用时按<b>简单名</b>取自自定义 dll 目录（没有再用内置替身）—— 忽略版本号。
    ///
    /// 游戏的数据程序集常引用与游戏本体版本不同的依赖：StardewValley.GameData.dll 引用的是
    /// MonoGame.Framework 3.6.0.862，游戏自带的却是 3.8.0.1641。按默认规则找不到，整个程序集
    /// 会被 GetExportedTypes() 的 FileNotFoundException 连带跳过（只留一条 warn），于是
    /// Data/Locations、Data/Objects 这类对象型数据全都解析不出读取器。
    /// 程序集都是 PublicKeyToken=null 的非强命名程序集，只认简单名接住是安全的。
    /// </summary>
    private static void HookExtDllResolver()
    {
        if (_resolverHooked) return;
        _resolverHooked = true;

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name == null) return null;
            if (ExtLoaded.TryGetValue(name.Name, out Assembly? loaded)) return loaded;

            byte[]? bytes;
            if (ExtDllFiles.TryGetValue(name.Name, out string? file))
            {
                bytes = File.ReadAllBytes(file);
            }
            else if (!BuiltinAssemblies.TryGetValue(name.Name, out bytes))
            {
                return null;
            }

            Assembly assembly = context.LoadFromStream(new MemoryStream(bytes));
            ExtLoaded[name.Name] = assembly;
            return assembly;
        };
    }
}