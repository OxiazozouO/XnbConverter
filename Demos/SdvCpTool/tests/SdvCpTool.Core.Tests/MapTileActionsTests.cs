using SdvCpTool.Core.Maps;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// TMXL 的 <c>LoadMap</c> ⇒ 原版 <c>MagicWarp</c>（<see cref="MapTileActions"/>）。
/// <para>
/// 实测来源（2026-10-18，业主报「农场走不出去」）：Grandpa's Farm 的农场图用
/// <c>TouchAction = LoadMap Forest 69 2</c> / <c>LoadMap BusStop 24 20 0</c> 这些图块做出口。
/// <c>LoadMap</c> 是 <c>Platonymous.TMXLoader</c> 注册的动作，纯 XNB 装机没有那个 DLL ⇒
/// 全部是死图块（`GameLocation.performTouchAction` 找不到处理器就什么都不做）。
/// 原版自带的等价物是 <c>Warp &lt;location&gt; &lt;x&gt; &lt;y&gt;</c>（TMXL 与 SVE 的兜底都是**立即**传送）。
/// </para>
/// </summary>
public class MapTileActionsTests
{
    private static TBin10 MapWith(params (string Key, string Value)[] properties)
    {
        var tiles = new List<BaseTile>();
        foreach ((string key, string value) in properties)
        {
            tiles.Add(new StaticTile
            {
                TileIndex = 0,
                Properties = new List<Propertie>
                {
                    new() { Key = key, Type = 0x03, Value = value },
                },
            });
        }
        return new TBin10
        {
            Layers = new List<Layer>
            {
                new()
                {
                    Id = "Back",
                    LayerSize = new IntVector2 { X = properties.Length, Y = 1 },
                    Tiles = tiles,
                },
            },
        };
    }

    private static string Touch(TBin10 map) =>
        (string)((BaseTile)map.Layers[0].Tiles[0]).Properties[0].Value!;

    [Fact]
    public void LoadMap_改成_MagicWarp()
    {
        TBin10 map = MapWith(("TouchAction", "LoadMap Forest 69 2"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("Warp Forest 69 2", Touch(map));
    }

    /// <summary>
    /// 第 4 个参数是 TMXL 的**朝向**，而 <c>MagicWarp</c> 的第 4 参是**邮件条件** ——
    /// 原样留着会被当成 mail id（永远不满足）⇒ 必须丢掉。
    /// </summary>
    [Fact]
    public void 朝向参数丢掉()
    {
        TBin10 map = MapWith(("TouchAction", "LoadMap BusStop 24 20 0"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("Warp BusStop 24 20", Touch(map));
    }

    [Fact]
    public void 不是_LoadMap_的不动()
    {
        TBin10 map = MapWith(
            ("TouchAction", "Warp Backwoods 13 24"),
            ("Action", "LoadMap Forest 69 2"),
            ("TouchAction", "LoadMap"));

        Assert.Equal(0, MapTileActions.Rewrite(map));
        Assert.Equal("Warp Backwoods 13 24", Touch(map));
    }

    /// <summary>
    /// RSV 的 <c>RSVTeleport &lt;loc&gt; &lt;x&gt; &lt;y&gt;</c> 是 **Action**；
    /// 原版 Action 没有 <c>ObeliskWarp</c>（只有 Building 的同名方法），
    /// 必须落成 <c>Warp &lt;x&gt; &lt;y&gt; &lt;loc&gt;</c>，否则地图加载报 unknown warp property。
    /// 依据 `SpiritRealm.cs:264-330`（淡出后 `warpFarmer`）。
    /// </summary>
    [Fact]
    public void RSVTeleport换成Warp()
    {
        TBin10 map = MapWith(("Action", "RSVTeleport Custom_Ridgeside_RSVTheHike 60 27"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("Warp 60 27 Custom_Ridgeside_RSVTheHike", Touch(map));
    }

    [Fact]
    public void 旧产出的ObeliskWarp也改回Warp()
    {
        TBin10 map = MapWith(("Action", "ObeliskWarp Custom_Ridgeside_RSVTheHike 60 27"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("Warp 60 27 Custom_Ridgeside_RSVTheHike", Touch(map));
    }

    [Fact]
    public void DuskspireWarp按反编译实现落到EastScarp()
    {
        TBin10 map = MapWith(("Action", "DN.SnS_DuskspireWarp"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("Warp 3 15 EastScarp_DuskspireLair 2", Touch(map));
    }

    /// <summary>Sunberry 的矿洞梯子参数顺序本来就是 <c>&lt;x&gt; &lt;y&gt; &lt;loc&gt;</c>，换名字即可。</summary>
    [Fact]
    public void Sunberry矿洞梯子换成Warp()
    {
        TBin10 map = MapWith(("Action", "SunberryTeam.SBVSMAPI_MineLadder 8 5 Custom_SBV_Mines2"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("Warp 8 5 Custom_SBV_Mines2", Touch(map));
    }

    /// <summary>BETAS 的动作代理：`Spiderbuttons.BETAS_Action Spiderbuttons.BETAS_PlaySound …` → 原版 `playSound …`。</summary>
    [Fact]
    public void BETAS动作代理换成原版playSound()
    {
        TBin10 map = MapWith(("Action", "Spiderbuttons.BETAS_Action Spiderbuttons.BETAS_PlaySound doorClose"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("playSound doorClose", Touch(map));
    }

    /// <summary>
    /// `SVE_PlaySound`（TouchAction）**不替换**：唯一能借的 `PlayEvent` 会启动事件
    /// （淡入淡出、锁玩家），走过即触发会打断移动。
    /// </summary>
    [Fact]
    public void SVE_PlaySound不替换()
    {
        TBin10 map = MapWith(("TouchAction", "SVE_PlaySound waterSlosh"));

        Assert.Equal(0, MapTileActions.Rewrite(map));
        Assert.Equal("SVE_PlaySound waterSlosh", Touch(map));
    }

    /// <summary>原版动作名大小写敏感；**Action** 版的笔误修（TouchAction 版没有 playSound，保持不动）。</summary>
    [Fact]
    public void 大小写笔误修正()
    {
        TBin10 map = MapWith(
            ("Action", "PlaySound doorClose"),
            ("TouchAction", "playsound waterSlosh"),
            ("TouchAction", "ChangeIntoSwimSuit"));

        Assert.Equal(2, MapTileActions.Rewrite(map));
        Assert.Equal("playSound doorClose", Touch(map));
        Assert.Equal("playsound waterSlosh", ((BaseTile)map.Layers[0].Tiles[1]).Properties[0].Value);
        Assert.Equal("ChangeIntoSwimsuit", ((BaseTile)map.Layers[0].Tiles[2]).Properties[0].Value);
    }

    /// <summary>`PoolExit` 没有处理器；原版 `PoolEntrance` 在游泳中踩上去就是出水。</summary>
    [Fact]
    public void PoolExit换成PoolEntrance()
    {
        TBin10 map = MapWith(("TouchAction", "PoolExit"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("PoolEntrance", Touch(map));
    }

    [Fact]
    public void TMXL的Say换成Dialogue()
    {
        TBin10 map = MapWith(("Action", "Say \"Hello there\""));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("Dialogue \"Hello there\"", Touch(map));
    }

    /// <summary>RSV 的商店动作在现装 DLL 里根本没注册；`OpenShop` + RSV 自己的 `Data/Shops` 是可用落点。</summary>
    [Fact]
    public void RSVShop换成OpenShop()
    {
        TBin10 map = MapWith(("Action", "RSVShop RSVJericShop"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("OpenShop RSVJericShop", Touch(map));
    }

    /// <summary>
    /// 图集引用归一成原版写法（相对 `Maps/` 的裸名）。原版 exe **不解析 `..`**：
    /// `../Maps/paths` 会让 `map.LoadTileSheets` 抛异常、地点被 `AddLocations` 跳过
    /// （实测 `Greenhouse` 建不出来 → 新游戏崩在 `RequireLocation("Greenhouse")`）。
    /// SMAPI 会归一，所以之前的探针一直是绿的。
    /// </summary>
    [Fact]
    public void 图集引用归一成原版裸名()
    {
        TBin10 map = MapWith();
        map.TileSheets = new List<TileSheet>
        {
            new() { Id = "a", Image = "../Maps/paths" },
            new() { Id = "b", Image = "Maps\\spring_outdoorsTileSheet.png" },
            new() { Id = "c", Image = "townInterior" },
            new() { Id = "d", Image = "Maps/Mines/mine" },
        };

        Assert.Equal(3, MapTileActions.NormalizeTileSheetImages(map));
        Assert.Equal("paths", map.TileSheets[0].Image);
        Assert.Equal("spring_outdoorsTileSheet", map.TileSheets[1].Image);
        Assert.Equal("townInterior", map.TileSheets[2].Image);
        Assert.Equal("Mines/mine", map.TileSheets[3].Image);
        Assert.Equal(0, MapTileActions.NormalizeTileSheetImages(map));   // 幂等
    }

    /// <summary>Downtown Zuzu 的理发店与原版 `WizardShrine` 同为 500G 角色定制。</summary>
    [Fact]
    public void ZuzuBarber换成WizardShrine()
    {
        TBin10 map = MapWith(("Action", "ZuzuBarber"));

        Assert.Equal(1, MapTileActions.Rewrite(map));
        Assert.Equal("WizardShrine", Touch(map));
    }
}
