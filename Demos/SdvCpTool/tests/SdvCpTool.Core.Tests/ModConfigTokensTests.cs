using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// CP 跨模组配置令牌 <c>{{&lt;UniqueID&gt;/&lt;field&gt;}}</c>（<see cref="ModConfigTokens"/>）。
/// <para>
/// 实测来源（2026-10-18）：RSV 的 <c>Data/Characters</c> 用
/// <c>{{Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals |contains=true}}</c>；
/// 认不出就整条 NPC 被跳过（39 条，含 13 个可婚 NPC）。
/// </para>
/// </summary>
public class ModConfigTokensTests : IDisposable
{
    private readonly string _mods;

    public ModConfigTokensTests()
    {
        _mods = Path.Combine(Path.GetTempPath(), "sdvmods_" + Guid.NewGuid().ToString("N"));
        string dir = Path.Combine(_mods, "TestMod");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            """{ "Name": "Test", "UniqueID": "Test.Mod" }""");
        File.WriteAllText(Path.Combine(dir, "config.json"),
            """{ "Bool": true, "Text": "abc", "Number": 3, "List": [ "a", "b" ] }""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_mods)) Directory.Delete(_mods, recursive: true);
    }

    private InstalledModIndex Index() => new(_mods);

    [Fact]
    public void 配置值按原样渲染()
    {
        InstalledModIndex mods = Index();
        Assert.Equal("true", ModConfigTokens.Render(mods, "Test.Mod/Bool", null));
        Assert.Equal("abc", ModConfigTokens.Render(mods, "Test.Mod/Text", null));
        Assert.Equal("3", ModConfigTokens.Render(mods, "Test.Mod/Number", null));
    }

    [Fact]
    public void contains参数折成布尔()
    {
        InstalledModIndex mods = Index();
        Assert.Equal("true", ModConfigTokens.Render(mods, "Test.Mod/Bool", "|contains=true"));
        Assert.Equal("false", ModConfigTokens.Render(mods, "Test.Mod/Bool", "|contains=false"));
        Assert.Equal("true", ModConfigTokens.Render(mods, "Test.Mod/Text", "|contains=abc"));
    }

    /// <summary>多值配置与多值 contains 都按集合“任一命中”，不是整串比较。</summary>
    [Fact]
    public void 多值配置的contains任一命中()
    {
        InstalledModIndex mods = Index();
        Assert.Equal("a, b", ModConfigTokens.Render(mods, "Test.Mod/List", null));
        Assert.Equal("true", ModConfigTokens.Render(mods, "Test.Mod/List", "|contains=b, c"));
        Assert.Equal("false", ModConfigTokens.Render(mods, "Test.Mod/List", "|contains=c, d"));
    }

    [Fact]
    public void 缺字段缺模组或非配置令牌时不猜()
    {
        InstalledModIndex mods = Index();
        Assert.Null(ModConfigTokens.Render(mods, "Test.Mod/Missing", null));
        Assert.Null(ModConfigTokens.Render(mods, "No.Such/Field", null));
        Assert.Null(ModConfigTokens.Render(mods, "HasMod", "|contains=Test.Mod"));
    }
}
