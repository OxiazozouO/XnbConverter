using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 代码模组（<c>EntryDll</c>）的命名空间令牌，值来自它自己的 <c>config.json</c>。
/// 实测 GiEX：<c>ExcludeFishes</c> → <c>ExcludeFromFishingCollection</c>、
/// <c>ExcludeAnimalProduce</c> → <c>ExcludeAnimalProduceFromShippingCollection</c>；
/// 不代则 28 个鱼 + 31 个动物产物定义整条不写。
/// </summary>
public class DllConfigTokensTests : IDisposable
{
    private readonly string _mods = Path.Combine(Path.GetTempPath(), "dllcfg_" + Guid.NewGuid().ToString("N"));

    public DllConfigTokensTests()
    {
        Mod("mistyspring.giexredux.code", entryDll: "Mod.dll",
            """{"ExcludeAnimalProduceFromShippingCollection":false,"ExcludeFromFishingCollection":true,"Portrait":"Vanilla","Long":"a,b"}""");
        Mod("Content.Only", entryDll: null, """{ "ExcludeFishes": true }""");
        // 真实语料：Fish Info 的 manifest 写的是 `EntryDLL`（SMAPI 照常加载）。
        Mod("Fish.Info", entryDll: "FishInfo.dll", """{ "ExcludeFishes": true }""",
            entryProperty: "EntryDLL");
    }

    public void Dispose() => Directory.Delete(_mods, recursive: true);

    private void Mod(string id, string? entryDll, string config,
        string entryProperty = "EntryDll")
    {
        string dir = Path.Combine(_mods, id);
        Directory.CreateDirectory(dir);
        var manifest = new JObject
        {
            ["Name"] = id,
            ["UniqueID"] = id,
            ["Version"] = "1.0.0",
        };
        if (entryDll != null) manifest[entryProperty] = entryDll;
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest.ToString());
        File.WriteAllText(Path.Combine(dir, "config.json"), config);
    }

    [Fact]
    public void 精确白名单与前缀映射()
    {
        var index = new InstalledModIndex(_mods);

        Assert.Equal("True", DllConfigTokens.Resolve("mistyspring.giexredux.code/ExcludeFishes", null, index));
        Assert.Equal("False", DllConfigTokens.Resolve("mistyspring.giexredux.code/ExcludeAnimalProduce", null, index));
        Assert.Equal("Vanilla", DllConfigTokens.Resolve("mistyspring.giexredux.code/PortraitStyle", null, index));
        Assert.Null(DllConfigTokens.Resolve("mistyspring.giexredux.code/Unknown", null, index));
        Assert.Null(DllConfigTokens.Resolve("mistyspring.giexredux.code/ExcludeAnimalProduce",
            "|contains=x", index));
    }

    [Fact]
    public void 没有EntryDll的内容包不猜()
    {
        var index = new InstalledModIndex(_mods);

        Assert.Null(DllConfigTokens.Resolve("Content.Only/ExcludeFishes", null, index));
    }

    /// <summary>字段名大小写不同（真实语料 `Fish Info` 写 `EntryDLL`）也认。</summary>
    [Fact]
    public void 大小写不同的EntryDLL也认()
    {
        var index = new InstalledModIndex(_mods);

        Assert.Equal("True", DllConfigTokens.Resolve("Fish.Info/ExcludeFishes", null, index));
    }
}
