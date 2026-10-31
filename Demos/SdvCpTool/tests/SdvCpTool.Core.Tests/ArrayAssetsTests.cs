using SdvCpTool.Core.Validating;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 数组型资产的判定。<b>这份名单是从 <c>DataLoader.cs</c> 手抄来的，留着不当实现、只当测试</b>。
/// <para>
/// 判定本身已改为读生成的 <c>GameDataSchema.AssetRootKind</c>（同一份事实不再有两个副本）。
/// 但这份手抄名单**不能删**：它当年正是靠 <c>Data/MakeoverOutfits</c>（走的是
/// <c>content.Load&lt;T&gt;("…")</c> 而非 <c>Load&lt;T&gt;(content, …)</c>）
/// 抓出了生成器**静默漏掉 4 个资产**的 bug。把它留在这里，生成器再漏就会被立刻发现。
/// </para>
/// </summary>
public class ArrayAssetsTests
{
    /// <summary>手抄名单（出处 <c>D:\sdv_src\StardewValley\DataLoader.cs</c>，1.6.15）。</summary>
    private static readonly string[] HandCopied =
    {
        "Data/AdditionalFarms",
        "Data/AdditionalLanguages",
        "Data/AdditionalWallpaperFlooring",
        "Data/Concessions",
        "Data/ConcessionTastes",
        "Data/LostItemsShop",
        "Data/FishPondData",
        "Data/MakeoverOutfits",
        "Data/Movies",
        "Data/MoviesReactions",
        "Data/RandomBundles",
        "Data/TailoringRecipes",
        "Data/TriggerActions",
    };

    /// <summary>
    /// ★ 手抄名单与生成表的数组集合必须**完全相等** —— 不只是「生成表 ⊇ 手抄」。
    /// <para>
    /// 双向都要盯：漏了是生成器漏解析（实测漏过 4 个），多了是生成器解析错。
    /// 少了要在 <c>DataLoader.cs</c> 里补调用形态；多了要查是哪条 <c>Load</c> 被误读。
    /// 无论哪边，**改完手抄名单再改生成器**，让这里重新对齐。
    /// </para>
    /// </summary>
    [Fact]
    public void 手抄名单与生成表的数组集合完全相等()
    {
        var generated = GameDataSchema.AssetRootKind
            .Where(kv => kv.Value == FieldKind.Array)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            HandCopied.OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
            generated.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void 反斜杠与大小写不影响判断() =>
        Assert.True(ArrayAssets.IsArrayAsset(@"Data\TriggerActions".ToUpperInvariant()));

    [Theory]
    [InlineData("Data/Festivals/winter28")]
    [InlineData("Data/Locations")]
    [InlineData("Data/Objects")]
    [InlineData("Data/Festivals/FestivalDates")]
    public void 字典型资产不算数组(string name) => Assert.False(ArrayAssets.IsArrayAsset(name));

    /// <summary>模型不认得的资产不猜 —— 按对象处理。</summary>
    [Fact]
    public void 不认得的资产不算数组() => Assert.False(ArrayAssets.IsArrayAsset("Mods/SomeMod/Data"));
}
