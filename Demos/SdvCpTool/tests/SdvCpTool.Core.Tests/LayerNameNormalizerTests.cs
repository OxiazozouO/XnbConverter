using SdvCpTool.Core.Maps;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 自定义图层名归一：原版 <c>GameLocation.SortLayers()</c> 只认
/// <c>Back/Buildings/Front/AlwaysFront</c> + **整数**后缀，其余名字整层不画。
/// TMXL 图常见的 <c>Back+1</c>/<c>Always Front</c>/<c>Draw=…</c> 必须落成原版名字。
/// </summary>
public class LayerNameNormalizerTests
{
    private static Layer L(string id, params (string Key, string Value)[] props) => new()
    {
        Id = id,
        LayerSize = new IntVector2 { X = 1, Y = 1 },
        Tiles = new List<BaseTile> { null! },
        Properties = props.Select(p => new Propertie { Key = p.Key, Type = 3, Value = p.Value }).ToList(),
    };

    private static TBin10 Map(params Layer[] layers) => new() { Layers = layers.ToList() };

    [Fact]
    public void 原版会画的名字保持不动()
    {
        TBin10 map = Map(L("Back"), L("Back2"), L("Back-1"), L("Buildings1"),
            L("Front3"), L("AlwaysFront4"), L("Paths"));

        Assert.Equal(0, LayerNameNormalizer.Normalize(map));
        Assert.Equal(new[] { "Back", "Back2", "Back-1", "Buildings1", "Front3", "AlwaysFront4", "Paths" },
            map.Layers.Select(l => l.Id).ToArray());
    }

    /// <summary>
    /// `<c>Back+1</c>`/`<c>Back 2</c>` 其实**原版能解析**（.NET 的 `int.TryParse` 接受正负号与
    /// 前后空白），所以它们本来就画；真正解析不了的是**空格拆在锚点内部**的
    /// `<c>Always Front</c>` —— 只归一这一类。
    /// </summary>
    [Fact]
    public void 锚点内部被空格拆开时才归一()
    {
        TBin10 map = Map(L("Back"), L("Back+1"), L("Back 2"), L("Front+1"),
            L("Buildings 2"), L("Always Front"));

        Assert.Equal(1, LayerNameNormalizer.Normalize(map));

        Assert.Equal(new[] { "Back", "Back+1", "Back 2", "Front+1", "Buildings 2", "AlwaysFront1" },
            map.Layers.Select(l => l.Id).ToArray());
    }

    [Fact]
    public void 目标已被占用时顺延到下一个空位()
    {
        TBin10 map = Map(L("AlwaysFront"), L("AlwaysFront1"), L("Always Front"));

        Assert.Equal(1, LayerNameNormalizer.Normalize(map));

        Assert.Equal("AlwaysFront2", map.Layers[2].Id);
    }

    /// <summary><c>DrawBefore/Draw/DrawAbove</c> 是 TMXL 的锚点约定；名字本身可能完全自定义。</summary>
    [Fact]
    public void Draw属性推断锚点与相对位置()
    {
        TBin10 map = Map(
            L("Back"), L("Buildings", ("DrawBefore", "Front")),
            L("UnderAlwaysFront", ("DrawAbove", "Front")),
            L("CustomClouds", ("Draw", "AlwaysFront")));

        Assert.Equal(2, LayerNameNormalizer.Normalize(map));

        // 目标层自己的名字 `Buildings` 原版会画 -> 不动；自定义层全部落到锚点名下
        Assert.Equal("Buildings", map.Layers[1].Id);
        Assert.StartsWith("Front", map.Layers[2].Id);
        Assert.StartsWith("AlwaysFront", map.Layers[3].Id);
        Assert.All(map.Layers.Skip(2), l => Assert.True(LayerNameNormalizer.IsRenderable(l.Id)));
    }

    [Fact]
    public void 数据层不碰()
    {
        TBin10 map = Map(L("Set-Up"), L("Schedule"), L("MainEvent"), L("RSVSetup"),
            L("Swamp Lurk Spots"), L("all_Crops"), L("Black Background"), L("Path"));

        Assert.Equal(0, LayerNameNormalizer.Normalize(map));
        Assert.Equal(new[] { "Set-Up", "Schedule", "MainEvent", "RSVSetup", "Swamp Lurk Spots", "all_Crops", "Black Background", "Path" },
            map.Layers.Select(l => l.Id).ToArray());
    }
}
