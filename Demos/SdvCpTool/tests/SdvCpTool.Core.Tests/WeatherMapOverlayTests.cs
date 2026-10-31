using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 雨天条件地图叠加 → 原版 <c>DayTiles</c>/<c>NightTiles</c>。
/// 用真实地图夹具做目标，源叠加层是合成的小 tbin；只验证写入形态，
/// 实机显示仍需进游戏确认（本工具不启动游戏）。
/// </summary>
public class WeatherMapOverlayTests : IDisposable
{
    private readonly string _in = Path.Combine(Path.GetTempPath(), "sdvweather_in_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvweather_out_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvweather_pack_" + Guid.NewGuid().ToString("N"));
    private readonly OutputTree _output;
    private readonly ImageCache _images;

    public WeatherMapOverlayTests()
    {
        Directory.CreateDirectory(Path.Combine(_in, "Maps"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Island_House_Bin.tbin"),
            Path.Combine(_in, "Maps", "Island_House_Bin.tbin"), overwrite: true);
        File.WriteAllText(Path.Combine(_in, "Maps", "template.config"),
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework", "Version": 0 } ] }""");
        Directory.CreateDirectory(Path.Combine(_pack, "assets", "Maps"));
        _output = new OutputTree(_in, _out);
        _images = new ImageCache(_output);
    }

    public void Dispose()
    {
        Directory.Delete(_in, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        Directory.Delete(_pack, recursive: true);
    }

    private void WriteSource(bool transparentFirstTile, bool outsideSheet = false)
    {
        var rgba = new byte[32 * 32 * 4];
        if (!transparentFirstTile)
        {
            // 四个格子都不透明：迫使写入器追加一行透明像素来找空索引。
            for (int i = 0; i < 32 * 32; i++)
            {
                rgba[i * 4] = 255;
                rgba[i * 4 + 3] = 255;
            }
        }
        else
        {
            // 第 0 格透明，其余三格不透明。
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    if (x < 16 && y < 16) continue;
                    rgba[(y * 32 + x) * 4] = 255;
                    rgba[(y * 32 + x) * 4 + 3] = 255;
                }
            }
        }
        Png.Write(Path.Combine(_pack, "assets", "Maps", "overlay.png"), rgba, 32, 32);
        if (outsideSheet)
            Png.Write(Path.Combine(_pack, "assets", "Maps", "outside.png"), new byte[32 * 16 * 4], 32, 16);

        var width = outsideSheet ? 4 : 2;
        var tiles = new List<BaseTile>();
        int[] inside = { 1, 2, 3, 1 };
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tiles.Add(x < 2
                    ? new StaticTile
                    {
                        TileSheet = "overlay",
                        TileIndex = inside[y * 2 + x],
                        Properties = new List<Propertie>(),
                    }
                    : new StaticTile
                    {
                        TileSheet = "outside",
                        TileIndex = 0,
                        Properties = new List<Propertie>(),
                    });
            }
        }

        var layer = new Layer
        {
            Id = "AlwaysFront4",
            Visible = 1,
            Description = "",
            LayerSize = new IntVector2 { X = width, Y = 2 },
            TileSize = new IntVector2 { X = 16, Y = 16 },
            Properties = new List<Propertie>(),
            Tiles = tiles,
            Index = new List<char>(),
            _sizeArr = new List<int>(),
            _currTileSheet = new List<string>(),
        };
        LayerRle.Rebuild(layer);

        var sheets = new List<TileSheet>
        {
            new()
            {
                Id = "overlay",
                Description = "",
                Image = "overlay.png",
                SheetSize = new IntVector2 { X = 2, Y = 2 },
                TileSize = new IntVector2 { X = 16, Y = 16 },
                Margin = new IntVector2 { X = 0, Y = 0 },
                Spacing = new IntVector2 { X = 0, Y = 0 },
                Properties = new List<Propertie>(),
            },
        };
        if (outsideSheet)
        {
            sheets.Add(new TileSheet
            {
                Id = "outside",
                Description = "",
                Image = "outside.png",
                SheetSize = new IntVector2 { X = 2, Y = 1 },
                TileSize = new IntVector2 { X = 16, Y = 16 },
                Margin = new IntVector2 { X = 0, Y = 0 },
                Spacing = new IntVector2 { X = 0, Y = 0 },
                Properties = new List<Propertie>(),
            });
        }

        var source = new TBin10
        {
            Format = "tBIN10",
            Id = "",
            Description = "",
            Properties = new List<Propertie>(),
            TileSheets = sheets,
            Layers = new List<Layer> { layer },
        };
        TBin10Reader.Create(Array.Empty<byte>()).Save(
            source, Path.Combine(_pack, "assets", "Maps", "mist.tbin"));
    }

    private MergeOutcome Apply(bool clipSource = false, bool visibleAtDay = false)
    {
        string json = clipSource
            ? """
              { "FromFile": "assets/Maps/mist.tbin",
                "FromArea": { "X": 0, "Y": 0, "Width": 2, "Height": 2 },
                "ToArea": { "X": 0, "Y": 0, "Width": 2, "Height": 2 },
                "When": { "Weather": "Rain, Storm" } }
              """
            : """
              { "FromFile": "assets/Maps/mist.tbin",
                "ToArea": { "X": 0, "Y": 0, "Width": 2, "Height": 2 },
                "When": { "Weather": "Rain, Storm" } }
              """;
        return MapApplier.Apply(_output, "Maps/Island_House_Bin", JObject.Parse(json),
            _pack, images: _images, weatherDayNightOverlay: true,
            dayNightVisibleAtDay: visibleAtDay);
    }

    [Fact]
    public void 雨天叠加写入透明底和DayNightTiles()
    {
        WriteSource(transparentFirstTile: true);

        MergeOutcome r = Apply();
        Assert.True(r.Success, r.FailureReason);
        _images.Flush();
        _output.Flush();

        TBin10 map = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        Layer layer = Assert.Single(map.Layers, l => l.Id == "AlwaysFront4");
        Assert.All(layer.Tiles.Where(t => t != null),
            t => Assert.Equal(0, Assert.IsType<StaticTile>(t).TileIndex));

        string day = map.Properties.Single(p => p.Key == "DayTiles").Value!.ToString()!;
        string night = map.Properties.Single(p => p.Key == "NightTiles").Value!.ToString()!;
        Assert.Contains("AlwaysFront4 0 0 0", day);
        Assert.Contains("AlwaysFront4 0 0 1", night);
        Assert.Contains("AlwaysFront4 1 1 1", night);
    }

    [Fact]
    public void 白天模式把原图块写进DayTiles()
    {
        WriteSource(transparentFirstTile: true);

        MergeOutcome r = Apply(visibleAtDay: true);
        Assert.True(r.Success, r.FailureReason);
        _images.Flush();
        _output.Flush();

        TBin10 map = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        string day = map.Properties.Single(p => p.Key == "DayTiles").Value!.ToString()!;
        string night = map.Properties.Single(p => p.Key == "NightTiles").Value!.ToString()!;
        Assert.Contains("AlwaysFront4 0 0 1", day);
        Assert.Contains("AlwaysFront4 0 0 0", night);
    }

    [Fact]
    public void ToArea之外的图块不参与叠加也不带入图集()
    {
        WriteSource(transparentFirstTile: true, outsideSheet: true);

        MergeOutcome r = Apply(clipSource: true);
        Assert.True(r.Success, r.FailureReason);
        _images.Flush();
        _output.Flush();

        TBin10 map = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        Assert.DoesNotContain(map.TileSheets, s => s.Id == "z_outside");
        Layer layer = Assert.Single(map.Layers, l => l.Id == "AlwaysFront4");
        Assert.All(layer.Tiles.Where(t => t != null),
            t => Assert.Equal(0, Assert.IsType<StaticTile>(t).TileIndex));
    }

    [Fact]
    public void 没有透明图块时追加一行透明像素()
    {
        WriteSource(transparentFirstTile: false);

        MergeOutcome r = Apply();
        Assert.True(r.Success, r.FailureReason);
        _images.Flush();
        _output.Flush();

        TBin10 map = TBin10Reader.FormFile(Path.Combine(_out, "Maps", "Island_House_Bin.tbin"));
        TileSheet sheet = Assert.Single(map.TileSheets, s => s.Id == "z_overlay");
        Assert.Equal(3, sheet.SheetSize!.Value.Y);

        Layer layer = Assert.Single(map.Layers, l => l.Id == "AlwaysFront4");
        Assert.All(layer.Tiles, t => Assert.Equal(4, Assert.IsType<StaticTile>(t).TileIndex));

        byte[] pixels = Png.Read(Path.Combine(_out, "Maps", "overlay.png"), out int width, out int height);
        Assert.Equal(32, width);
        Assert.Equal(48, height);
        Assert.All(pixels.Skip(32 * 32 * 4), b => Assert.Equal(0, b));
    }
}
