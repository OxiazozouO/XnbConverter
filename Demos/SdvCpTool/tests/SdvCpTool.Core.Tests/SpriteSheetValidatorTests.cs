using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Validating;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 按**游戏自己的贴图寻址公式**核对产出。
/// <para>
/// 公式是逐字抄的（<c>Object.cs:1087-1089</c> / <c>Game1.cs:15897-15905</c>），
/// 所以用例直接钉住「第 index 号格子在哪个像素」—— 化简或改错一处，这里就会红。
/// </para>
/// </summary>
public class SpriteSheetValidatorTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvssv_" + Guid.NewGuid().ToString("N"));

    public SpriteSheetValidatorTests() => Directory.CreateDirectory(_out);

    public void Dispose()
    {
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void Sheet(string assetName, int w, int h)
    {
        string full = Path.Combine(_out, assetName.Replace('/', Path.DirectorySeparatorChar) + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Png.Write(full, new byte[w * h * 4], w, h);
    }

    private void Asset(string name, params (string Key, object Entry)[] entries)
    {
        var obj = new JObject();
        foreach ((string key, object entry) in entries) obj[key] = JToken.FromObject(entry);
        string full = Path.Combine(_out, name.Replace('/', Path.DirectorySeparatorChar) + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, obj.ToString());
    }

    // ---------------------------------------------------------------- BigCraftables（16x32）

    /// <summary>缺省贴图是 <c>TileSheets/Craftables</c>（<c>BigCraftableDataDefinition.cs:44</c>）。</summary>
    [Fact]
    public void 缺省贴图走Craftables()
    {
        Sheet("TileSheets/Craftables", 64, 64);
        Asset("Data/BigCraftables", ("1", new { Name = "X", SpriteIndex = 3 }));

        SpriteSheetValidator.Audit audit = SpriteSheetValidator.Run(_out);

        Assert.Equal(1, audit.Entries);
        Assert.Empty(audit.Findings);
    }

    /// <summary>
    /// 越界要抓得住：<c>16x32</c> 的贴图只放得下第 0 号（真实教训 —— 四季发射若把贴图做成 1 格宽，
    /// 夏/秋/冬三个索引全指到图外）。
    /// </summary>
    [Fact]
    public void 索引越出贴图时报出()
    {
        Sheet("Mods/P/Shrub", 16, 32);
        Asset("Data/BigCraftables", ("K", new { Name = "Shrub", Texture = "Mods\\P\\Shrub", SpriteIndex = 1 }));

        SpriteSheetValidator.Audit audit = SpriteSheetValidator.Run(_out);

        Assert.Equal(1, audit.OutOfBounds);
        Assert.Contains("越出贴图 16x32", audit.Findings[0].Reason);
    }

    /// <summary>四季发射的正常产物：<c>64x32</c>，索引 0 —— 4 个格子都在图里。</summary>
    [Fact]
    public void 四季产物六十四宽时索引零通过()
    {
        Sheet("Mods/P/Shrub", 64, 32);
        Asset("Data/BigCraftables", ("K", new { Name = "Shrub Seasonal", Texture = @"Mods\P\Shrub", SpriteIndex = 0 }));

        Assert.Empty(SpriteSheetValidator.Run(_out).Findings);
    }

    /// <summary>
    /// <c>Name</c> 含 <c>Seasonal</c> 时游戏会在 <b>0..3</b> 之间换索引（<c>Object.cs:1940-1943</c>），
    /// 贴图必须有 <b>≥ 4 格宽</b>。只有 1 格宽时夏/秋/冬三个索引全指到图外 ——
    /// 而数据里 <c>SpriteIndex</c> 看着是 0，光看数据发现不了。
    /// </summary>
    [Fact]
    public void Seasonal命名但贴图不足四格时报出()
    {
        Sheet("Mods/P/Shrub", 16, 32);
        Asset("Data/BigCraftables", ("K", new { Name = "Shrub Seasonal", Texture = @"Mods\P\Shrub", SpriteIndex = 0 }));

        SpriteSheetValidator.Audit audit = SpriteSheetValidator.Run(_out);

        Assert.Equal(1, audit.SeasonalTooNarrow);
        Assert.Contains("Seasonal", audit.Findings[0].Reason);
    }

    /// <summary>不带 <c>Seasonal</c> 的条目不受这条约束 —— 1 格宽也合法。</summary>
    [Fact]
    public void 非Seasonal命名的一格宽贴图合法()
    {
        Sheet("Mods/P/Shrub", 16, 32);
        Asset("Data/BigCraftables", ("K", new { Name = "Shrub", Texture = @"Mods\P\Shrub", SpriteIndex = 0 }));

        Assert.Empty(SpriteSheetValidator.Run(_out).Findings);
    }

    /// <summary>贴图文件不存在 —— 游戏只记日志、物品画不出来，必须抓。</summary>
    [Fact]
    public void 贴图缺失时报出()
    {
        Asset("Data/BigCraftables", ("K", new { Name = "X", Texture = "Mods/P/不存在", SpriteIndex = 0 }));

        SpriteSheetValidator.Audit audit = SpriteSheetValidator.Run(_out);

        Assert.Equal(1, audit.MissingTexture);
        Assert.Contains("找不到这张贴图", audit.Findings[0].Reason);
    }

    /// <summary>
    /// 贴图宽不是格宽整数倍时，游戏的 <c>W/16</c> 列数会算错 —— 单独报，**不混进越界**
    /// （混在一起会让「越界几条」这个数说不清）。
    /// 实测来源：SVE 的 <c>Birch Water</c> 是 15px 宽。
    /// </summary>
    [Fact]
    public void 贴图宽不是格宽整数倍时单独报()
    {
        Sheet("Mods/P/Odd", 15, 16);
        Asset("Data/Objects", ("K", new { Name = "X", Texture = "Mods/P/Odd", SpriteIndex = 0 }));

        SpriteSheetValidator.Audit audit = SpriteSheetValidator.Run(_out);

        Assert.Equal(1, audit.BadSheetWidth);
        Assert.Equal(0, audit.OutOfBounds);
        Assert.Contains("整数倍", audit.Findings[0].Reason);
    }

    // ---------------------------------------------------------------- Objects（16x16）

    /// <summary>缺省贴图是 <c>Maps/springobjects</c>（<c>ObjectDataDefinition.cs:52</c>）。</summary>
    [Fact]
    public void 物件缺省贴图走springobjects()
    {
        Sheet("Maps/springobjects", 16 * 24, 16 * 100);
        Asset("Data/Objects", ("16", new { Name = "Wild Horseradish", SpriteIndex = 16 }));

        SpriteSheetValidator.Audit audit = SpriteSheetValidator.Run(_out);

        Assert.Equal(1, audit.Entries);
        Assert.Empty(audit.Findings);
    }

    /// <summary>物件是 16x16 格 —— 拿 BigCraftable 的 16x32 去量会误报。</summary>
    [Fact]
    public void 物件用十六高格量()
    {
        Sheet("Maps/springobjects", 16, 16);
        Asset("Data/Objects", ("16", new { Name = "X", SpriteIndex = 0 }));

        Assert.Empty(SpriteSheetValidator.Run(_out).Findings);
    }

    [Fact]
    public void 物件索引越界时报出()
    {
        Sheet("Maps/springobjects", 16, 16);
        Asset("Data/Objects", ("16", new { Name = "X", SpriteIndex = 1 }));

        Assert.Equal(1, SpriteSheetValidator.Run(_out).OutOfBounds);
    }

    /// <summary>本地化变体（<c>X.es-ES.png</c>）也算找得到。</summary>
    [Fact]
    public void 只有本地化变体时也算找得到()
    {
        Sheet("Maps/springobjects.es-ES", 16, 16);
        Asset("Data/Objects", ("16", new { Name = "X", SpriteIndex = 0 }));

        Assert.Empty(SpriteSheetValidator.Run(_out).Findings);
    }

    [Fact]
    public void 没有该资产时不报错()
    {
        Assert.Equal(0, SpriteSheetValidator.Run(_out).Assets);
    }

    // ── Data/Characters 的整图引用（Appearance[] 是本工具自己发射的落点）─────────────
    // 这里的引用与别处不同：不是「图集 + 索引」而是**整张资产名**。
    // 本工具按季节/条件换立绘时会**另存独立资产**再在 Appearance[] 里引用 ——
    // 那份资产若没写出去，游戏**画不出人**，而数据形状全对、另两道自检也管不到。

    private void Sprite(string assetName)
    {
        string full = Path.Combine(_out, assetName.Replace('/', Path.DirectorySeparatorChar) + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, Array.Empty<byte>());   // 存在即可，内容无关
    }

    /// <summary>★ 引用了不存在的贴图 ⇒ 必须报出来。</summary>
    [Fact]
    public void 立绘引用不存在的资产时报出来()
    {
        Asset("Data/Characters", ("Abigail", new
        {
            Appearance = new[] { new { Id = "W", Sprite = "Characters/Abigail_Winter", Portrait = "Portraits/Abigail_Winter" } }
        }));
        Sprite("Portraits/Abigail_Winter");   // 只放立绘，行走图缺失

        var a = SpriteSheetValidator.Run(_out);

        SpriteSheetValidator.Finding f = Assert.Single(a.Findings);
        Assert.Equal("Data/Characters", f.Asset);
        Assert.Equal("Abigail", f.EntryKey);
        Assert.Equal("Characters/Abigail_Winter", f.Texture);
        Assert.Contains("Appearance[0].Sprite", f.Reason);
    }

    /// <summary>引用都存在时不报。</summary>
    [Fact]
    public void 立绘引用齐全时不报()
    {
        Asset("Data/Characters", ("Abigail", new
        {
            Appearance = new[] { new { Id = "W", Sprite = "Characters/Abigail_Winter", Portrait = "Portraits/Abigail_Winter" } }
        }));
        Sprite("Characters/Abigail_Winter");
        Sprite("Portraits/Abigail_Winter");

        Assert.Empty(SpriteSheetValidator.Run(_out).Findings);
    }

    /// <summary>NPC 条目自己的 <c>Sprite</c>/<c>Portrait</c>（没有 Appearance 时的缺省引用）同样要查。</summary>
    [Fact]
    public void 条目自身的Sprite也存在性要查()
    {
        Asset("Data/Characters", ("Leo", new { Sprite = "Characters/Leo" }));

        SpriteSheetValidator.Finding f = Assert.Single(SpriteSheetValidator.Run(_out).Findings);
        Assert.Equal("Characters/Leo", f.Texture);
        Assert.Contains("`Sprite`", f.Reason);
    }

    /// <summary>空串/缺字段 = 走缺省名，**不在此列**（缺省名由别的机制决定，这里不猜）。</summary>
    [Fact]
    public void 空引用不报()
    {
        Asset("Data/Characters", ("Leo", new { Sprite = "", Portrait = (string?)null }));

        Assert.Empty(SpriteSheetValidator.Run(_out).Findings);
    }

    /// <summary>
    /// ★ `_Beach` 变体缺失**不是缺陷**（实测 SVE 的 `GuntherSilvian_Beach` 就是这么来的）。
    /// 游戏对这一类是 `DoesAssetExist` 保护 + 回退常服常图（`NPC.cs:1051-1066`）——
    /// 原先它和u771fu7f3au56fe共用一句＂画不出人＂，说错了。
    /// </summary>
    [Fact]
    public void Beach变体缺失不当缺陷()
    {
        Asset("Data/Characters", ("GuntherSilvian", new
        {
            Appearance = new[] { new { Id = "Island", Sprite = "Characters/GuntherSilvian_Beach" } }
        }));

        SpriteSheetValidator.Finding f = Assert.Single(SpriteSheetValidator.Run(_out).Findings);

        Assert.Equal("Characters/GuntherSilvian_Beach", f.Texture);
        Assert.Contains("不是缺陷", f.Reason);
        Assert.DoesNotContain("画不出人", f.Reason);
    }

    /// <summary>真缺图（不是 `_Beach`）的措辞要说清u6e38u620fu4f1au56deu9000u5230u7f3au7701u540d。</summary>
    [Fact]
    public void 真缺图措辞说回退()
    {
        Asset("Data/Characters", ("Alesia", new { Sprite = "Characters/Alesia_Spring" }));

        SpriteSheetValidator.Finding f = Assert.Single(SpriteSheetValidator.Run(_out).Findings);

        Assert.Contains("回退到缺省名", f.Reason);
    }
}
