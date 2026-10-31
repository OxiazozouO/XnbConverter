using SdvCpTool.Core.Reporting;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 产出树自检：**地点的类来自 mod 的程序集** ⇒ 纯 XNB 装机里没有那个程序集，
/// 游戏建地点时直接抛 `Invalid type for location` ⇒ 地点根本不存在
/// （它的地图、从别处指着它的传送、挂在它下面的内容**全都到不了**）。
/// <para>
/// 实测（2026-10-13，`Cape Stardew`）：三个 `Custom_CapeKeyMaze*` 的
/// `CreateOnLoad.Type` 是 `CapeStardewCode.CustomLavaLogic, CapeStardewCode`，
/// 实机日志里就是 `System.Exception: Invalid type for location Custom_CapeKeyMaze: …`。
/// 这一类以前在报告里是**静默**的。
/// </para>
/// </summary>
public class LocationClassAuditTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sdvlocclass_" + Guid.NewGuid().ToString("N"));

    public LocationClassAuditTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private LocationClassAudit.Audit Run(string json)
    {
        string full = Path.Combine(_root, "Data", "Locations.json");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
        return LocationClassAudit.Run(_root);
    }

    /// <summary>不写程序集的（原版那种 `StardewValley.Locations.Town`）与写明游戏程序集的都放行。</summary>
    [Fact]
    public void 游戏自己的类不报()
    {
        var audit = Run("""
        {
          "Town":     { "CreateOnLoad": { "MapPath": "Maps\\Town" } },
          "Farmhouse":{ "CreateOnLoad": { "MapPath": "Maps\\FarmHouse",
                                          "Type": "StardewValley.Locations.FarmHouse" } },
          "Mine":     { "CreateOnLoad": { "MapPath": "Maps\\Mine",
                                          "Type": "StardewValley.Locations.MineShaft, StardewValley" } }
        }
        """);

        Assert.Empty(audit.Findings);
        Assert.Equal(3, audit.CheckedLocations);
    }

    [Fact]
    public void mod程序集的地点报出来()
    {
        var audit = Run("""
        {
          "Custom_CapeKeyMaze": { "CreateOnLoad": {
              "MapPath": "Maps\\Custom_CapeKeyMaze",
              "Type": "CapeStardewCode.CustomLavaLogic, CapeStardewCode" } },
          "Town": { "CreateOnLoad": { "MapPath": "Maps\\Town" } }
        }
        """);

        LocationClassAudit.Finding f = Assert.Single(audit.Findings);
        Assert.Equal("Custom_CapeKeyMaze", f.Location);
        Assert.Equal("CapeStardewCode.CustomLavaLogic", f.Type);
        Assert.Equal("CapeStardewCode", f.Assembly);
        Assert.Equal("Maps\\Custom_CapeKeyMaze", f.MapPath);
    }

    /// <summary>没有 `Data/Locations.json`（或读不出来）时什么都不报，别把"没这份资产"当问题。</summary>
    [Fact]
    public void 没有地点数据时不报()
    {
        Assert.Empty(LocationClassAudit.Run(Path.Combine(_root, "不存在")).Findings);
    }
}
