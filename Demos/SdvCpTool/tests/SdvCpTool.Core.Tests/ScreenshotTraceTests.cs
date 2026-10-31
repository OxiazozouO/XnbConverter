using SdvCpTool.Core.Maps;
using SdvCpTool.Core.Tracing;
using XnbConverter.Entity.Mono;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

public class ScreenshotTraceTests : IDisposable
{
    private const int MapW = 40, MapH = 30;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sdvtrace_" + Guid.NewGuid().ToString("N"));
    private readonly TBin10 _map;

    public ScreenshotTraceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Maps"));
        WriteSheet();
        _map = BuildMap();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void WriteSheet()
    {
        const int tiles = 4, size = 16;
        byte[] px = new byte[tiles * size * tiles * size * 4];
        for (int ty = 0; ty < tiles; ty++)
        {
            for (int tx = 0; tx < tiles; tx++)
            {
                byte r = (byte)(30 + tx * 60), g = (byte)(20 + ty * 55), b = (byte)(40 + (tx + ty) * 25);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int i = (((ty * size + y) * tiles * size) + tx * size + x) * 4;
                        px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255;
                    }
                }
            }
        }
        Png.Write(Path.Combine(_root, "Maps", "sheet.png"), px, tiles * size, tiles * size);
    }

    private static TBin10 BuildMap()
    {
        var back = new Layer
        {
            Id = "Back", Visible = 1, Description = "",
            LayerSize = new IntVector2 { X = MapW, Y = MapH },
            TileSize = new IntVector2 { X = 16, Y = 16 },
            Properties = new List<Propertie>(),
            Tiles = new List<BaseTile>(),
            Index = new List<char>(),
            _sizeArr = new List<int>(),
            _currTileSheet = new List<string> { "t" },
        };
        for (int y = 0; y < MapH; y++)
        {
            for (int x = 0; x < MapW; x++)
            {
                back.Tiles.Add(new StaticTile
                {
                    TileSheet = "t",
                    TileIndex = (x * 3 + y * 5) % 16,
                    Properties = new List<Propertie>(),
                });
                back.Index.Add('S');
            }
        }
        var buildings = new Layer
        {
            Id = "Buildings", Visible = 1, Description = "",
            LayerSize = new IntVector2 { X = MapW, Y = MapH },
            TileSize = new IntVector2 { X = 16, Y = 16 },
            Properties = new List<Propertie>(),
            Tiles = Enumerable.Range(0, MapW * MapH).Select(_ => (BaseTile)new StaticTile
            {
                TileSheet = "", TileIndex = 0, Properties = new List<Propertie>(),
            }).ToList(),
            Index = new List<char>(new string('S', MapW * MapH)),
            _sizeArr = new List<int>(),
            _currTileSheet = new List<string> { "" },
        };
        return new TBin10
        {
            Format = "tBIN10", Id = "", Description = "",
            Properties = new List<Propertie>
            {
                new() { Key = "Warp", Type = 3, Value = "10 10 Town 1 1" },
            },
            TileSheets = new List<TileSheet>
            {
                new()
                {
                    Id = "t", Description = "", Image = "sheet",
                    SheetSize = new IntVector2 { X = 4, Y = 4 },
                    TileSize = new IntVector2 { X = 16, Y = 16 },
                    Margin = new IntVector2 { X = 0, Y = 0 },
                    Spacing = new IntVector2 { X = 0, Y = 0 },
                    Properties = new List<Propertie>(),
                },
            },
            Layers = new List<Layer> { back, buildings },
        };
    }

    [Fact]
    public void 渲染与图层检查()
    {
        var renderer = new MapRenderer(_root);
        var rendered = renderer.Render(_map, "Maps/TestMap");
        Assert.Equal(MapW * 16, rendered.Width);
        Assert.Equal(MapH * 16, rendered.Height);

        var inspect = TileInspector.Inspect(renderer, _map, "Maps/TestMap", 10, 10, null, null);
        Assert.Contains(inspect.Layers, l => l.Layer == "Back" && l.Sheet == "t");
        Assert.Contains(inspect.Warps, w => w.Contains("Town"));
    }

    [Fact]
    public void 截图能定位回地图和格子()
    {
        var renderer = new MapRenderer(_root);
        var rendered = renderer.Render(_map, "Maps/TestMap");
        int tw = rendered.Width / 16, th = rendered.Height / 16;
        ulong[] hashes = new ulong[tw * th];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
                hashes[y * tw + x] = TileFingerprint.Compute(rendered.Rgba, rendered.Width,
                    rendered.Height, x * 16, y * 16);
        var index = new MapFingerprintIndex(new[]
        {
            new MapFingerprintIndex.MapEntry("TestMap", tw, th, hashes),
        });

        const int x0 = 37, y0 = 29, cw = 320, ch = 240, scale = 2;
        byte[] shot = new byte[cw * scale * ch * scale * 4];
        for (int y = 0; y < ch * scale; y++)
        {
            for (int x = 0; x < cw * scale; x++)
            {
                int sx = x0 + x / scale, sy = y0 + y / scale;
                int si = (sy * rendered.Width + sx) * 4;
                int di = (y * cw * scale + x) * 4;
                shot[di] = rendered.Rgba[si];
                shot[di + 1] = rendered.Rgba[si + 1];
                shot[di + 2] = rendered.Rgba[si + 2];
                shot[di + 3] = 255;
            }
        }
        for (int y = 90; y < 150; y++)
            for (int x = 120; x < 170; x++)
                if (y < 94 || y >= 146 || x < 124 || x >= 166)
                {
                    int i = (y * cw * scale + x) * 4;
                    shot[i] = 255; shot[i + 1] = 0; shot[i + 2] = 0; shot[i + 3] = 255;
                }
        string image = Path.Combine(_root, "shot.png");
        Png.Write(image, shot, cw * scale, ch * scale);

        var result = ScreenshotLocator.Locate(image, index, new ScreenshotLocator.Options(
            MapFilter: "TestMap", ForceScale: scale, MinScore: 0.2));
        Assert.Equal("matched", result.Status);
        Assert.Equal("TestMap", result.Candidates[0].Map);
        Assert.True(result.Candidates[0].Score >= 0.5);
        Assert.NotNull(result.RedBoxX);
    }

    [Fact]
    public void 补丁覆盖判断()
    {
        string mods = Path.Combine(_root, "Mods");
        string pack = Path.Combine(mods, "Pack1");
        Directory.CreateDirectory(pack);
        File.WriteAllText(Path.Combine(pack, "manifest.json"), """
        { "Name": "Pack1", "UniqueID": "Test.Pack", "ContentPackFor": { "UniqueID": "Pathoschild.ContentPatcher" } }
        """);
        File.WriteAllText(Path.Combine(pack, "content.json"), """
        { "Changes": [
          { "Action": "EditMap", "Target": "Maps/TestMap", "ToArea": { "X": 10, "Y": 10, "Width": 2, "Height": 2 } },
          { "Action": "EditMap", "Target": "Maps/TestMap", "MapTiles": [ { "Position": "20,20" } ] }
        ] }
        """);

        var patches = PatchProvenance.Find(mods, "Maps/TestMap", 10, 10);
        Assert.Contains(patches, p => p.CoversTile && p.FromFile == null
                                      && p.ToArea is { Length: 4 } a && a[0] == 10);
        Assert.Contains(patches, p => !p.CoversTile && p.MapTiles.Contains("20,20"));
    }
}
