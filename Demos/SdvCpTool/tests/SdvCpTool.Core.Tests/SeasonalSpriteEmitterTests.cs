using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Normalizing;
using SdvCpTool.Core.Workspace;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// Task 29 的发射器：把四季图排成 4 个连续索引，并把条目 <c>Name</c> 加上 <c>Seasonal</c>。
/// <para>
/// 用<b>合成图</b>验证：源图里 4 个已知色块，发射后逐格比对像素 ——
/// 只看「写出成功」而不看像素，分不开「四季各有各的图」和「四季都是同一张」。
/// </para>
/// </summary>
public class SeasonalSpriteEmitterTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvss_src_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvss_out_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvss_pack_" + Guid.NewGuid().ToString("N"));

    private const string Target = "Mods/TestPack/Shrub";
    private const string EntryKey = "TestPack_Shrub";
    private const int CellW = 16, CellH = 32;

    /// <summary>四季各一个不透明纯色，用来在产物里逐格认人。</summary>
    private static readonly (string Season, byte R, byte G, byte B)[] Seasons =
    {
        ("Spring", 10, 0, 0), ("Summer", 0, 20, 0), ("Fall", 0, 0, 30), ("Winter", 40, 40, 40),
    };

    public SeasonalSpriteEmitterTests()
    {
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_pack);

        // 源图 64x32：四季横排在 x = 0,16,32,48，各是自己那一色。
        var source = new byte[64 * CellH * 4];
        for (int i = 0; i < 4; i++)
            for (int y = 0; y < CellH; y++)
                for (int x = 0; x < CellW; x++)
                {
                    int p = (y * 64 + i * CellW + x) * 4;
                    source[p] = Seasons[i].R; source[p + 1] = Seasons[i].G;
                    source[p + 2] = Seasons[i].B; source[p + 3] = 255;
                }
        Png.Write(Path.Combine(_pack, "SeasonalJA.png"), source, 64, CellH);
    }

    public void Dispose()
    {
        foreach (string d in new[] { _src, _pack, _out })
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    // ---------------------------------------------------------------- 工具

    private static PatchVerdict EditImage(string season, int fromX, string extraConditions = "")
    {
        var raw = JObject.Parse($$"""
        { "Action": "EditImage", "Target": "{{Target}}",
          "FromFile": "SeasonalJA.png",
          "When": { "Season": "{{season}}"{{extraConditions}} },
          "FromArea": { "X": {{fromX}}, "Y": 0, "Width": {{CellW}}, "Height": {{CellH}} },
          "ToArea": { "X": 0, "Y": 0, "Width": {{CellW}}, "Height": {{CellH}} } }
        """);
        return new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Raw = raw, Action = "EditImage", Target = Target,
                When = raw["When"] as JObject, FromFile = "SeasonalJA.png", SourceFile = "content.json",
            },
            Verdict = Verdict.NoLandingPoint,
        };
    }

    private static PatchVerdict BigCraftables(string name = "Shrub", int spriteIndex = 0)
    {
        var raw = new JObject
        {
            ["Action"] = "EditData",
            ["Target"] = "Data/BigCraftables",
            ["Entries"] = new JObject
            {
                [EntryKey] = new JObject
                {
                    ["Name"] = name,
                    ["DisplayName"] = "Shrub",
                    // 反斜杠形态是真实语料的样子（SVE 的 Texture 就写 `Mods\...\X`）。
                    ["Texture"] = @"Mods\TestPack\Shrub",
                    ["SpriteIndex"] = spriteIndex,
                },
            },
        };
        return new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Raw = raw, Action = "EditData", Target = "Data/BigCraftables", SourceFile = "content.json",
            },
            Verdict = Verdict.ConvertedNoCondition,
        };
    }

    private static List<PatchVerdict> Verdicts(string extraConditions = "", string name = "Shrub",
        int spriteIndex = 0)
    {
        var list = new List<PatchVerdict>();
        for (int i = 0; i < 4; i++) list.Add(EditImage(Seasons[i].Season, i * CellW, extraConditions));
        list.Add(BigCraftables(name, spriteIndex));
        return list;
    }

    private OutputTree? _tree;
    private OutputTree NewTree() => _tree ??= new(_src, _out);

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路，
    /// 图片的落盘权又在 `ImageCache` 手里，两个都要刷。</summary>
    private void Flush()
    {
        _images?.Flush();
        _tree?.Flush();
    }

    /// <summary>树 + 它的图片内存模型。图片只在 `Images.Flush()` 时才落盘。</summary>
    private ImageCache? _images;
    private ImageCache Images => _images ??= new ImageCache(NewTree());

    /// <summary>读出产物贴图某格中心的像素，用来断言「这一格就是那一季」。</summary>
    private (byte R, byte G, byte B) PixelAt(string rel, int x, int y)
    {
        Flush();
        byte[] data = Png.Read(Path.Combine(_out, rel), out int w, out int h);
        Assert.True(x < w && y < h, $"({x},{y}) 超出产物尺寸 {w}x{h}");
        int p = (y * w + x) * 4;
        return (data[p], data[p + 1], data[p + 2]);
    }

    // ---------------------------------------------------------------- 发射

    [Fact]
    public void 四季各画进自己的索引且像素正确()
    {
        var verdicts = Verdicts();
        SeasonalSpriteGroup group = Assert.Single(SeasonalSpritePlanner.Plan(verdicts));
        Assert.Null(group.Reject);

        Assert.True(SeasonalSpriteEmitter.TryEmit(NewTree(), Images, _pack, verdicts, group, out string? err), err);

        // 索引 i 的格子 = 第 i 季。索引顺序 Spring=0…Winter=3（(int)Season）。
        for (int i = 0; i < 4; i++)
        {
            var px = PixelAt("Mods/TestPack/Shrub.png", i * CellW + CellW / 2, CellH / 2);
            Assert.Equal((Seasons[i].R, Seasons[i].G, Seasons[i].B), px);
        }
    }

    /// <summary>缺一季时用底图补那一格，其余季照常使用自己的图。</summary>
    [Fact]
    public void 缺一季时用底图补那一格()
    {
        Directory.CreateDirectory(Path.Combine(_src, "Mods", "TestPack"));
        var basePx = new byte[CellW * CellH * 4];
        for (int p = 0; p < basePx.Length; p += 4)
        {
            basePx[p] = 50; basePx[p + 1] = 60; basePx[p + 2] = 70; basePx[p + 3] = 255;
        }
        Png.Write(Path.Combine(_src, "Mods", "TestPack", "Shrub.png"), basePx, CellW, CellH);

        List<PatchVerdict> verdicts = Verdicts();
        verdicts.RemoveAll(v => v.Patch.Action == "EditImage" &&
                                (v.Patch.When?["Season"]?.Value<string>() == "Winter"));
        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];
        Assert.Null(group.Reject);

        Assert.True(SeasonalSpriteEmitter.TryEmit(NewTree(), Images, _pack, verdicts, group, out string? err), err);

        Assert.Equal((10, 0, 0), PixelAt("Mods/TestPack/Shrub.png", CellW / 2, CellH / 2));
        Assert.Equal((50, 60, 70), PixelAt("Mods/TestPack/Shrub.png", 3 * CellW + CellW / 2, CellH / 2));
    }

    [Fact]
    public void 条目Name加上Seasonal()
    {
        var verdicts = Verdicts();
        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];
        Assert.True(SeasonalSpriteEmitter.TryEmit(NewTree(), Images, _pack, verdicts, group, out string? err), err);

        JObject entries = (JObject)verdicts[4].Patch.Raw["Entries"]!;
        Assert.Equal("Shrub Seasonal", entries[EntryKey]!["Name"]!.Value<string>());
    }

    /// <summary>发射不能动条目的其它字段 —— 只加 <c>Seasonal</c>，别的一律原样。</summary>
    [Fact]
    public void 发射不动条目的其它字段()
    {
        var verdicts = Verdicts();
        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];
        Assert.True(SeasonalSpriteEmitter.TryEmit(NewTree(), Images, _pack, verdicts, group, out string? err), err);

        JObject entry = (JObject)((JObject)verdicts[4].Patch.Raw["Entries"]!)[EntryKey]!;
        Assert.Equal("Shrub", entry["DisplayName"]!.Value<string>());
        Assert.Equal(0, entry["SpriteIndex"]!.Value<int>());
        Assert.Equal(@"Mods\TestPack\Shrub", entry["Texture"]!.Value<string>());
    }

    [Fact]
    public void 重复发射幂等不叠加Seasonal()
    {
        var verdicts = Verdicts();
        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];
        OutputTree tree = NewTree();
        Assert.True(SeasonalSpriteEmitter.TryEmit(tree, Images, _pack, verdicts, group, out string? e1), e1);
        Assert.True(SeasonalSpriteEmitter.TryEmit(tree, Images, _pack, verdicts, group, out string? e2), e2);

        JObject entries = (JObject)verdicts[4].Patch.Raw["Entries"]!;
        Assert.Equal("Shrub Seasonal", entries[EntryKey]!["Name"]!.Value<string>());
    }

    /// <summary>被拒的组<b>一格都不许写</b> —— 半套发射会留下「改了图没改 Name」这种坏产物。</summary>
    [Fact]
    public void 被拒的组不发射也不改名()
    {
        var verdicts = Verdicts(spriteIndex: 8);      // 索引非 0 ⇒ 规划阶段就拒
        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];
        Assert.NotNull(group.Reject);

        Assert.False(SeasonalSpriteEmitter.TryEmit(NewTree(), Images, _pack, verdicts, group, out string? err));
        Assert.Equal(group.Reject, err);
        Assert.False(File.Exists(Path.Combine(_out, "Mods/TestPack/Shrub.png")));

        JObject entries = (JObject)verdicts[4].Patch.Raw["Entries"]!;
        Assert.Equal("Shrub", entries[EntryKey]!["Name"]!.Value<string>());
    }

    /// <summary>Source 覆盖整个 16x32 格：只要有一格不是那一季的色，就是索引错位。</summary>
    [Fact]
    public void 产物宽度是四格且四格互不相同()
    {
        var verdicts = Verdicts();
        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];
        Assert.True(SeasonalSpriteEmitter.TryEmit(NewTree(), Images, _pack, verdicts, group, out string? err), err);
Flush();

        Png.Read(Path.Combine(_out, "Mods/TestPack/Shrub.png"), out int w, out int h);
        Assert.Equal(CellW * 4, w);
        Assert.Equal(CellH, h);

        var seen = new HashSet<(byte, byte, byte)>();
        for (int i = 0; i < 4; i++) seen.Add(PixelAt("Mods/TestPack/Shrub.png", i * CellW + 8, 16));
        Assert.Equal(4, seen.Count);
    }

    /// <summary><c>IsOutdoors</c> 是装不下的维度：仍然发射，但丢掉的维度要能被上层读到（标近似）。</summary>
    [Fact]
    public void 仅室外条件仍发射但记录丢失的维度()
    {
        var verdicts = Verdicts(extraConditions: """, "IsOutdoors": true""");
        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];

        Assert.Null(group.Reject);
        Assert.Equal(new[] { "IsOutdoors" }, group.LostConditions);
        Assert.True(SeasonalSpriteEmitter.TryEmit(NewTree(), Images, _pack, verdicts, group, out string? err), err);
    }

    [Fact]
    public void 找不到源图时发射失败且不落文件()
    {
        var verdicts = Verdicts();
        SeasonalSpriteGroup group = SeasonalSpritePlanner.Plan(verdicts)[0];
        string missing = Path.Combine(_pack, "SeasonalJA.png");
        File.Delete(missing);
        try
        {
            Assert.False(SeasonalSpriteEmitter.TryEmit(NewTree(), Images, _pack, verdicts, group, out string? err));
            Assert.Contains("找不到 FromFile", err);
            Assert.False(File.Exists(Path.Combine(_out, "Mods/TestPack/Shrub.png")));
        }
        finally { Png.Write(missing, new byte[64 * CellH * 4], 64, CellH); }
    }
}
