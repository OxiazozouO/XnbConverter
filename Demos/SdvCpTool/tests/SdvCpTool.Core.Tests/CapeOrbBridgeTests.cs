using System.Text;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Workspace;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// CapeStardew 的 <c>CapeOrbTouchAction</c> 事件桥：纯计划先算，事件数据写成功后再改地图。
/// 语料形态见 `D:\tmp\cape-decompiled\CapeStardewCode.decompiled.cs` 的
/// <c>OrbRuneWarpManager</c>。
/// </summary>
public class CapeOrbBridgeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cape_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static TBin10 MapWith(params string[] actions)
    {
        var tiles = new List<BaseTile>();
        foreach (string action in actions)
        {
            tiles.Add(new StaticTile
            {
                TileIndex = 0,
                BlendMode = 0,
                Properties = new List<Propertie>
                {
                    new() { Key = "TouchAction", Type = 0x03, Value = action },
                },
            });
        }
        return new TBin10
        {
            Format = "tBIN10",
            Id = "Test",
            Description = "",
            Data = Array.Empty<byte>(),
            Properties = new List<Propertie>(),
            TileSheets = new List<TileSheet>(),
            Layers = new List<Layer>
            {
                new()
                {
                    Id = "Back",
                    Visible = 1,
                    Description = "",
                    LayerSize = new IntVector2 { X = actions.Length, Y = 1 },
                    TileSize = new IntVector2 { X = 16, Y = 16 },
                    Properties = new List<Propertie>(),
                    Tiles = tiles,
                    Index = Enumerable.Repeat('S', actions.Length).ToList(),
                    _sizeArr = new List<int>(),
                    _currTileSheet = new List<string>(),
                },
            },
        };
    }

    private static string Touch(TBin10 map, int index) =>
        (string)((BaseTile)map.Layers[0].Tiles[index]).Properties[0].Value!;

    private string Input(params (string Rel, string Text)[] files)
    {
        string input = Path.Combine(_root, "in");
        foreach ((string rel, string text) in files)
        {
            string full = Path.Combine(input, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        return input;
    }

    private static string Config() => """
        {
          "Content": { "Extension": ".json", "Format": 0, "ContentWidth": 0, "ContentHeight": 0,
                       "ShaderOffset": 0, "ShaderShell": null },
          "Header": { "Target": "Windows", "FormatVersion": 5, "CompressedFlag": "Hidef, Lzx" },
          "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.String, mscorlib],[System.String, mscorlib]], Microsoft.Xna.Framework", "Version": 0 } ]
        }
        """;

    [Fact]
    public void 计划同时处理当前图和跨图()
    {
        TBin10 map = MapWith(
            "CapeOrbTouchAction 17 20",
            "CapeOrbTouchAction Custom_Target 3 4");

        CapeOrbBridge.Plan? plan = CapeOrbBridge.BuildPlan("Maps/Custom_CapeMossy.tbin", map);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Actions.Count);
        Assert.Equal("CapeOrb_Custom_CapeMossy_17_20", plan.Actions[0].EventId);
        Assert.Equal("playSound wand/warp farmer 17 20/end", plan.Actions[0].Script);
        Assert.Equal("CapeOrb_Custom_CapeMossy_to_Custom_Target_3_4", plan.Actions[1].EventId);
        Assert.Contains("changeLocation Custom_Target", plan.Actions[1].Script);
        // 纯计划不能动地图。
        Assert.Equal("CapeOrbTouchAction 17 20", Touch(map, 0));
    }

    [Fact]
    public void 同一事件id的多个图块都要改写()
    {
        TBin10 map = MapWith("CapeOrbTouchAction 8 5", "CapeOrbTouchAction 8 5");

        Assert.Single(CapeOrbBridge.BuildPlan("Maps/Test.tbin", map)!.Actions
            .Select(a => a.EventId).Distinct());
        Assert.Equal(2, CapeOrbBridge.BuildPlan("Maps/Test.tbin", map)!.Actions.Count);
    }

    [Fact]
    public void 写事件成功后才改地图并镜像中文变体()
    {
        string input = Input(
            ("Data/Events/Test.json", "{}"),
            ("Data/Events/Test.config", Config()),
            ("Data/Events/Test.zh-CN.json", "{}"),
            ("Data/Events/Test.zh-CN.config", Config()));
        string output = Path.Combine(_root, "out");
        var tree = new OutputTree(input, output);
        TBin10 map = MapWith("CapeOrbTouchAction 17 20");

        var notes = new List<string>();
        int n = CapeOrbBridge.RewriteAndGenerate(tree, [("Maps/Test.tbin", map)], notes);
        tree.Flush();

        Assert.Equal(1, n);
        Assert.Empty(notes);
        Assert.Equal("PlayEvent CapeOrb_Test_17_20 true false", Touch(map, 0));

        JObject baseEvents = JObject.Parse(File.ReadAllText(Path.Combine(output, "Data/Events/Test.json")));
        Assert.Equal("playSound wand/warp farmer 17 20/end",
            baseEvents["CapeOrb_Test_17_20/HasItem (O)OrbOfTheTides"]!.Value<string>());

        JObject zh = JObject.Parse(File.ReadAllText(Path.Combine(output, "Data/Events/Test.zh-CN.json")));
        Assert.Equal(baseEvents["CapeOrb_Test_17_20/HasItem (O)OrbOfTheTides"]!.Value<string>(),
            zh["CapeOrb_Test_17_20/HasItem (O)OrbOfTheTides"]!.Value<string>());
    }

    [Fact]
    public void 没有事件资产时新建json和config()
    {
        string input = Input(("Data/Events/Template.config", Config()));
        string output = Path.Combine(_root, "out");
        var tree = new OutputTree(input, output);
        TBin10 map = MapWith("CapeOrbTouchAction 2 3");

        var notes = new List<string>();
        int n = CapeOrbBridge.RewriteAndGenerate(tree, [("Maps/NewMap.tbin", map)], notes);
        tree.Flush();

        Assert.Equal(1, n);
        Assert.Empty(notes);
        Assert.True(File.Exists(Path.Combine(output, "Data/Events/NewMap.json")));
        Assert.True(File.Exists(Path.Combine(output, "Data/Events/NewMap.config")));
    }

    [Fact]
    public void 参数不合法时保持原动作()
    {
        TBin10 map = MapWith(
            "CapeOrbTouchAction",
            "CapeOrbTouchAction x y",
            "CapeOrbTouchAction map 1");

        Assert.Null(CapeOrbBridge.BuildPlan("Maps/Test.tbin", map));
    }

    [Fact]
    public void 补扫已落盘但本轮没弄脏的地图()
    {
        string input = Input(
            ("Data/Events/Test.json", "{}"),
            ("Data/Events/Test.config", Config()));
        Directory.CreateDirectory(Path.Combine(input, "Maps"));
        TBin10 map = MapWith("CapeOrbTouchAction 7 9");
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, Path.Combine(input, "Maps/Test.tbin"));

        string output = Path.Combine(_root, "out");
        var tree = new OutputTree(input, output);
        var notes = new List<string>();
        byte[] beforeBytes = File.ReadAllBytes(Path.Combine(output, "Maps/Test.tbin"));
        Assert.Contains("CapeOrbTouchAction", Encoding.ASCII.GetString(beforeBytes));
        TBin10 before = (TBin10)TBin10Reader.Create(beforeBytes).Read();
        Assert.Equal("CapeOrbTouchAction 7 9", Touch(before, 0));

        Assert.Equal(1, CapeOrbBridge.RewriteRemaining(tree, notes));
        tree.Flush();

        // 后续包整份 Load 掉 Data/Events 后，补扫应能从地图上的 PlayEvent id 把事件补回来；
        // 地图已经是 PlayEvent，不再重复计数/改写。
        File.Delete(Path.Combine(output, "Data/Events/Test.json"));
        Assert.Equal(0, CapeOrbBridge.RewriteRemaining(tree, notes));
        tree.Flush();

        Assert.Empty(notes);
        TBin10 back = (TBin10)TBin10Reader.Create(
            File.ReadAllBytes(Path.Combine(output, "Maps/Test.tbin"))).Read();
        Assert.Equal("PlayEvent CapeOrb_Test_7_9 true false", Touch(back, 0));
        JObject events = JObject.Parse(File.ReadAllText(Path.Combine(output, "Data/Events/Test.json")));
        Assert.NotNull(events["CapeOrb_Test_7_9/HasItem (O)OrbOfTheTides"]);
    }
}
