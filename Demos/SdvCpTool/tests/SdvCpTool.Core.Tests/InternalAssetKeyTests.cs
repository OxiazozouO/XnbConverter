using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// CP 的 <c>{{InternalAssetKey: 包内相对路径}}</c>：资产名 = <c>SMAPI/&lt;id 小写&gt;/&lt;路径&gt;</c>
/// （扩展名保留，见 SMAPI `ModContentManager.GetInternalAssetKey`）。
/// <para>
/// 实测语料 17 个文件用它（Hurst 的 38 个物品、Rodney 的书/稻草人、Eli and Dylan、Metalcore…）。
/// 不代的话整条 <c>Data/Objects</c> / <c>Data/Shops</c> 补丁被拒 —— 物品在游戏里根本不存在。
/// </para>
/// </summary>
public class InternalAssetKeyTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "iak_" + Guid.NewGuid().ToString("N"));

    public InternalAssetKeyTests()
    {
        Directory.CreateDirectory(Path.Combine(_pack, "assets"));
        File.WriteAllBytes(Path.Combine(_pack, "assets", "img.png"), new byte[] { 1, 2, 3 });
    }

    public void Dispose() => Directory.Delete(_pack, recursive: true);

    private static ChangePatch Patch(string text)
    {
        JObject raw = JObject.Parse(text);
        return new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            SourceFile = "content.json",
        };
    }

    [Fact]
    public void 包内图片展开成SMAPI资产名并记下素材()
    {
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Data/Objects",
              "Entries": { "X": { "Texture": "{{InternalAssetKey: assets/img.png}}" } } }
            """);
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int n = InternalAssetKeyExpander.Apply(new[] { p }, _pack, "Author.Mod", assets);

        Assert.Equal(1, n);
        Assert.Equal("SMAPI/author.mod/assets/img.png",
            p.Raw["Entries"]!["X"]!["Texture"]!.Value<string>());
        Assert.Equal("assets/img.png", assets["SMAPI/author.mod/assets/img.png"]);
    }

    [Fact]
    public void 路径归一化去掉点点()
    {
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Data/Objects",
              "Entries": { "X": { "Texture": "{{InternalAssetKey: assets/../assets/img.png}}" } } }
            """);
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(1, InternalAssetKeyExpander.Apply(new[] { p }, _pack, "Author.Mod", assets));
        Assert.Equal("SMAPI/author.mod/assets/img.png",
            p.Raw["Entries"]!["X"]!["Texture"]!.Value<string>());
    }

    /// <summary>
    /// 引用可以不带扩展名：SMAPI 对 Texture2D 会依次试 `<路径>.png` / `.xnb`。
    /// 资产名保持无扩展名（游戏请求的就是它），素材按真实 `.png` 物化。
    /// 实测 Nora The Herpetologist 的 `nora_sprites_winter_coat`。
    /// </summary>
    [Fact]
    public void 无扩展名时按png找素材但资产名保持无扩展名()
    {
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Data/Characters",
              "Entries": { "Nora": { "Sprite": "{{InternalAssetKey: assets/img}}" } } }
            """);
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int n = InternalAssetKeyExpander.Apply(new[] { p }, _pack, "Author.Mod", assets);

        Assert.Equal(1, n);
        Assert.Equal("SMAPI/author.mod/assets/img",
            p.Raw["Entries"]!["Nora"]!["Sprite"]!.Value<string>());
        Assert.Equal("assets/img.png", assets["SMAPI/author.mod/assets/img"]);
    }

    [Fact]
    public void 文件不存在时原样保留()
    {
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Data/Objects",
              "Entries": { "X": { "Texture": "{{InternalAssetKey: assets/nope.png}}" } } }
            """);
        var assets = new Dictionary<string, string>();

        Assert.Equal(0, InternalAssetKeyExpander.Apply(new[] { p }, _pack, "Author.Mod", assets));
        Assert.Equal("{{InternalAssetKey: assets/nope.png}}",
            p.Raw["Entries"]!["X"]!["Texture"]!.Value<string>());
    }

    [Fact]
    public void 参数里还有令牌时不猜()
    {
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Data/Objects",
              "Entries": { "X": { "Texture": "{{InternalAssetKey: assets/{{Style}}/img.png}}" } } }
            """);
        var assets = new Dictionary<string, string>();

        Assert.Equal(0, InternalAssetKeyExpander.Apply(new[] { p }, _pack, "Author.Mod", assets));
        Assert.Contains("{{Style}}", p.Raw["Entries"]!["X"]!["Texture"]!.Value<string>());
    }
}
