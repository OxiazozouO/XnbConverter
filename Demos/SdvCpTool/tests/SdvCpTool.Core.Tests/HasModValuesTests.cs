using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class HasModValuesTests : IDisposable
{
    private readonly string _mods;

    public HasModValuesTests()
    {
        _mods = Path.Combine(Path.GetTempPath(), "sdvhasmod_" + Guid.NewGuid().ToString("N"));
        foreach (string id in new[] { "Installed.One", "Installed.Two" })
        {
            string dir = Path.Combine(_mods, id.Replace('.', '_'));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"),
                $$"""{ "Name": "X", "UniqueID": "{{id}}" }""");
        }

        // 纯代码 mod：纯 XNB 装机没有 SMAPI，运行时不存在 ⇒ HasMod 必须判 false。
        string dll = Path.Combine(_mods, "Dll_Framework");
        Directory.CreateDirectory(dll);
        File.WriteAllText(Path.Combine(dll, "manifest.json"),
            """{ "Name": "Dll", "UniqueID": "Dll.Framework", "EntryDll": "Dll.dll" }""");

        // 内容包由转换器烘进树，运行时仍算存在。
        string content = Path.Combine(_mods, "Content_Pack");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "manifest.json"),
            """{ "Name": "CP", "UniqueID": "Content.Pack", "ContentPackFor": { "UniqueID": "Pathoschild.ContentPatcher" } }""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_mods)) Directory.Delete(_mods, recursive: true);
    }

    [Fact]
    public void 多值contains任一命中()
    {
        var mods = new InstalledModIndex(_mods);

        Assert.Equal("true",
            HasModValues.Render(mods, "|contains=Missing.Mod, Installed.Two"));
        Assert.Equal("false",
            HasModValues.Render(mods, "|contains=Missing.Mod, Other.Missing"));
    }

    [Fact]
    public void 单值与无参数保持旧行为()
    {
        var mods = new InstalledModIndex(_mods);

        Assert.Equal("true", HasModValues.Render(mods, "|contains=Installed.One"));
        Assert.Contains("Installed.One", HasModValues.Render(mods, null));
    }

    [Fact]
    public void 运行时集合排除纯代码模组但保留已装集合()
    {
        var mods = new InstalledModIndex(_mods);

        // 空值删除保护用运行时集合：DLL 框架在纯 XNB 里不会运行。
        Assert.False(mods.ContainsRuntime("Dll.Framework"));
        Assert.True(mods.ContainsRuntime("Content.Pack"));
        // `HasMod` 仍按传统「盘上装了没有」：判假会让整块 Include/FTM 内容不展开。
        Assert.Equal("true", HasModValues.Render(mods, "|contains=Dll.Framework"));
        Assert.Contains("Dll.Framework", HasModValues.Render(mods, null));
    }
}
