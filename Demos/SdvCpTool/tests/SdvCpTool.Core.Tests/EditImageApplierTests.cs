using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;
using XnbConverter.Utilities.Png;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// <c>EditImage</c> 合成器。<c>Overlay</c> 必须逐字复刻 SMAPI 的近似混合 ——
/// 尤其是 <c>A = max(src.A, dst.A)</c>（<b>不是</b>标准 source-over）与 <c>MinOpacity = 5</c> 的首末行裁剪。
/// 混合公式的用例直接跑纯函数 <see cref="EditImageApplier.TryCompose"/>，不造 PNG。
/// </summary>
public class EditImageApplierTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvei_src_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvei_out_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvei_pack_" + Guid.NewGuid().ToString("N"));

    private const string ImageConfig =
        """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0" } ] }""";

    public EditImageApplierTests()
    {
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_pack);
    }

    private OutputTree? _tree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush()
    {
        _tree?.Flush();
    }

    public void Dispose()
    {
        Directory.Delete(_src, recursive: true);
        Directory.Delete(_pack, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>按 R,G,B,A 顺序展开像素。</summary>
    private static byte[] Px(params byte[] rgba) => rgba;

    private static ImageArea Area(int x, int y, int w, int h) => new(x, y, w, h);

    private static bool Compose(byte[] target, int tw, int th, byte[] source, int sw, int sh,
        ImageArea from, ImageArea to, PatchImageMode mode,
        out byte[] result, out int rw, out int rh, out string? error) =>
        EditImageApplier.TryCompose(target, tw, th, source, sw, sh, from, to, mode,
            out result, out rw, out rh, out error);

    private static ChangePatch Patch(string json)
    {
        var raw = JObject.Parse(json);
        return new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            FromFile = raw["FromFile"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = "content.json",
        };
    }

    private void Tree(string rel, byte[] png, int width, int height)
    {
        // 长度与尺寸对不上就是夹具写错了 —— 让它在**这里**炸，而不是写出一张坏 PNG 让后面的断言莫名其妙地红
        Assert.Equal(width * height * 4, png.Length);
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Png.Write(full, png, width, height);
    }

    private void TreeConfig(string rel, string content)
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void Pack(string rel, byte[] png, int width, int height)
    {
        Assert.Equal(width * height * 4, png.Length);
        string full = Path.Combine(_pack, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Png.Write(full, png, width, height);
    }

    // ---------------------------------------------------------------- Overlay 精确语义

    [Fact]
    public void Overlay源全透明时目标不变()
    {
        byte[] target = Px(10, 20, 30, 255);
        byte[] source = Px(99, 99, 99, 0);

        Assert.True(Compose(target, 1, 1, source, 1, 1, Area(0, 0, 1, 1), Area(0, 0, 1, 1),
            PatchImageMode.Overlay, out byte[] result, out _, out _, out _));
        Assert.Equal(Px(10, 20, 30, 255), result);
    }

    [Fact]
    public void Overlay源alpha低于5按透明处理()
    {
        // MinOpacity = 5：Linux/macOS 上"全透明"像素的 alpha 可能到 4，必须一并按透明处理。
        byte[] target = Px(10, 20, 30, 255);
        byte[] source = Px(99, 99, 99, 4);

        Assert.True(Compose(target, 1, 1, source, 1, 1, Area(0, 0, 1, 1), Area(0, 0, 1, 1),
            PatchImageMode.Overlay, out byte[] result, out _, out _, out _));
        Assert.Equal(Px(10, 20, 30, 255), result);
    }

    [Fact]
    public void Overlay源A255时目标等于源()
    {
        byte[] target = Px(10, 20, 30, 255);
        byte[] source = Px(99, 88, 77, 255);

        Assert.True(Compose(target, 1, 1, source, 1, 1, Area(0, 0, 1, 1), Area(0, 0, 1, 1),
            PatchImageMode.Overlay, out byte[] result, out _, out _, out _));
        Assert.Equal(Px(99, 88, 77, 255), result);
    }

    [Fact]
    public void Overlay目标A小于5时目标等于源()
    {
        byte[] target = Px(10, 20, 30, 0);
        byte[] source = Px(40, 50, 60, 200);

        Assert.True(Compose(target, 1, 1, source, 1, 1, Area(0, 0, 1, 1), Area(0, 0, 1, 1),
            PatchImageMode.Overlay, out byte[] result, out _, out _, out _));
        Assert.Equal(Px(40, 50, 60, 200), result);
    }

    [Fact]
    public void Overlay半透明时A取两者最大而不是标准source_over()
    {
        // 钉子：SMAPI 的近似混合 A = max(src.A, dst.A)。
        // 源 A=100 < 目标 A=200，标准 source-over 会得到 A ≈ 221（100 + 200*(1-100/255)），
        // 而 SMAPI 是 max = 200。写成标准混合就是错的。
        byte[] target = Px(100, 100, 100, 200);
        byte[] source = Px(50, 50, 50, 100);

        Assert.True(Compose(target, 1, 1, source, 1, 1, Area(0, 0, 1, 1), Area(0, 0, 1, 1),
            PatchImageMode.Overlay, out byte[] result, out _, out _, out _));

        Assert.Equal((byte)200, result[3]);                 // A = max(100, 200)
        Assert.NotEqual((byte)221, result[3]);              // 明确排除标准 source-over
        // R = src.R + dst.R*k，k = 1 - 100/255 → 50 + 100*155/255 = 110.78… → 110
        Assert.Equal((byte)110, result[0]);
        Assert.Equal((byte)110, result[1]);
        Assert.Equal((byte)110, result[2]);
    }

    [Fact]
    public void Overlay只在源首末非透明行之间合成()
    {
        // 源 1x3：上下两行全透明，只有中间一行不透明。
        // 裁剪后 targetArea 重定位到 [1,1] 行 —— 目标第 0、2 行必须原封不动。
        byte[] source = Px(0, 0, 0, 0, 10, 20, 30, 255, 0, 0, 0, 0);
        byte[] target = Px(1, 1, 1, 255, 2, 2, 2, 255, 3, 3, 3, 255);

        Assert.True(Compose(target, 1, 3, source, 1, 3, Area(0, 0, 1, 3), Area(0, 0, 1, 3),
            PatchImageMode.Overlay, out byte[] result, out int rw, out int rh, out _));

        Assert.Equal(1, rw);
        Assert.Equal(3, rh);
        Assert.Equal(Px(1, 1, 1, 255), result[0..4]);       // 首行之外：不变
        Assert.Equal(Px(10, 20, 30, 255), result[4..8]);    // 裁剪后合成的那一行
        Assert.Equal(Px(3, 3, 3, 255), result[8..12]);      // 末行之外：不变
    }

    // ---------------------------------------------------------------- Replace

    [Fact]
    public void Replace即使源全透明也覆盖目标()
    {
        byte[] target = Px(1, 2, 3, 255);
        byte[] source = Px(9, 8, 7, 0);

        Assert.True(Compose(target, 1, 1, source, 1, 1, Area(0, 0, 1, 1), Area(0, 0, 1, 1),
            PatchImageMode.Replace, out byte[] result, out _, out _, out _));
        Assert.Equal(Px(9, 8, 7, 0), result);   // alpha 一并覆盖
    }

    // ---------------------------------------------------------------- 约束

    [Fact]
    public void 宽高不相等时失败()
    {
        byte[] target = Px(1, 1, 1, 255);
        byte[] source = Px(1, 1, 1, 255, 2, 2, 2, 255);

        Assert.False(Compose(target, 1, 1, source, 2, 1, Area(0, 0, 2, 1), Area(0, 0, 1, 1),
            PatchImageMode.Replace, out _, out _, out _, out string? error));
        Assert.Contains("尺寸不相等", error);
    }

    [Fact]
    public void 目标区域右边超出目标宽度时失败()
    {
        byte[] target = Px(1, 1, 1, 255, 2, 2, 2, 255);
        byte[] source = Px(9, 9, 9, 255);

        Assert.False(Compose(target, 2, 1, source, 1, 1, Area(0, 0, 1, 1), Area(2, 0, 1, 1),
            PatchImageMode.Replace, out _, out _, out _, out string? error));
        Assert.Contains("超出目标宽度", error);
    }

    [Fact]
    public void FromArea缺字段时整条跳过()
    {
        var patch = Patch("""
        { "Action": "EditImage", "Target": "Characters/Abigail",
          "FromArea": { "X": 0, "Y": 0, "Width": 4 } }
        """);

        Assert.False(EditImageApplier.TryParseAreas(patch, out _, out _, out string? error));
        Assert.Contains("缺", error);
    }

    [Fact]
    public void 缺省区域解析为null由合成时补默认()
    {
        var patch = Patch("""{ "Action": "EditImage", "Target": "Characters/Abigail" }""");

        Assert.True(EditImageApplier.TryParseAreas(patch, out ImageArea? from, out ImageArea? to, out _));
        Assert.Null(from);
        Assert.Null(to);
    }

    /// <summary>
    /// 区域坐标写成**字符串**也要收。实测 SVE 的季节灌木整批这么写
    /// （<c>"X": "0", "Y": "192", …</c>），只收 JSON 整数会把它们**整条判失败**，静默不写。
    /// </summary>
    [Fact]
    public void 区域坐标是数字字符串时也解析()
    {
        var patch = Patch("""
        { "Action": "EditImage", "Target": "Mods/TestPack/Shrub",
          "FromArea": { "X": "0", "Y": "192", "Width": "16", "Height": "32" },
          "ToArea":   { "X": "0", "Y": "0",   "Width": "16", "Height": "32" } }
        """);

        Assert.True(EditImageApplier.TryParseAreas(patch, out ImageArea? from, out ImageArea? to, out string? error), error);
        Assert.Equal(new ImageArea(0, 192, 16, 32), from);
        Assert.Equal(new ImageArea(0, 0, 16, 32), to);
    }

    /// <summary>数字字符串与整数混用同样可以。</summary>
    [Fact]
    public void 区域坐标整数与字符串混用时也解析()
    {
        var patch = Patch("""
        { "Action": "EditImage", "Target": "Mods/TestPack/Shrub",
          "FromArea": { "X": 16, "Y": "192", "Width": "16", "Height": 32 } }
        """);

        Assert.True(EditImageApplier.TryParseAreas(patch, out ImageArea? from, out _, out string? error), error);
        Assert.Equal(new ImageArea(16, 192, 16, 32), from);
    }

    [Fact]
    public void 目标不够高时向下扩展画布()
    {
        byte[] target = Px(1, 2, 3, 255);                              // 1x1
        byte[] source = Px(9, 9, 9, 255, 8, 8, 8, 255);                // 1x2

        Assert.True(Compose(target, 1, 1, source, 1, 2, Area(0, 0, 1, 2), Area(0, 0, 1, 2),
            PatchImageMode.Replace, out byte[] result, out int rw, out int rh, out _));

        Assert.Equal(1, rw);
        Assert.Equal(2, rh);                                           // 向下扩展到 toArea.Bottom
        Assert.Equal(Px(9, 9, 9, 255), result[0..4]);
        Assert.Equal(Px(8, 8, 8, 255), result[4..8]);
    }

    // ---------------------------------------------------------------- 模式

    [Theory]
    [InlineData(null, PatchImageMode.Replace)]     // CP 默认 Replace
    [InlineData("Replace", PatchImageMode.Replace)]
    [InlineData("overlay", PatchImageMode.Overlay)]
    public void 解析PatchMode(string? raw, PatchImageMode expected)
    {
        Assert.True(EditImageApplier.TryParseMode(raw, out PatchImageMode mode, out _));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void Mask报错而不是静默降级()
    {
        Assert.False(EditImageApplier.TryParseMode("Mask", out _, out string? error));
        Assert.Contains("Mask", error);
    }

    // ---------------------------------------------------------------- 笛卡尔积

    [Fact]
    public void Target与FromFile做逗号笛卡尔积()
    {
        var patch = Patch("""
        { "Action": "EditImage", "Target": "A/I1, A/I2", "FromFile": "x.png, y.png" }
        """);

        IReadOnlyList<EditImageJob> jobs = EditImageApplier.ExpandJobs(patch);

        Assert.Equal(4, jobs.Count);
        Assert.Equal(new[] { "A/I1", "A/I1", "A/I2", "A/I2" }, jobs.Select(j => j.Target));
        Assert.Equal(new[] { "x.png", "y.png", "x.png", "y.png" }, jobs.Select(j => j.FromFile));
    }

    [Fact]
    public void 逗号列表大小写不敏感去重()
    {
        var patch = Patch("""
        { "Action": "EditImage", "Target": "A/I1, a/i1", "FromFile": "X.PNG, x.png" }
        """);

        IReadOnlyList<EditImageJob> jobs = EditImageApplier.ExpandJobs(patch);

        Assert.Single(jobs);
        Assert.Equal("A/I1", jobs[0].Target);
        Assert.Equal("X.PNG", jobs[0].FromFile);
    }

    [Fact]
    public void 缺FromFile时没有组合()
    {
        var patch = Patch("""{ "Action": "EditImage", "Target": "A/I1" }""");

        Assert.Empty(EditImageApplier.ExpandJobs(patch));
    }

    [Fact]
    public void 令牌内的逗号不是分隔符()
    {
        // 真实回归：Token 实参里带逗号（`{{Random:null, 3}}`），朴素 Split(',')
        // 会把它切成两个都不存在的名字，这条补丁就静默失效了。
        var patch = Patch("""
        { "Action": "EditImage", "Target": "A/I{{Random:null, 3}}", "FromFile": "x.png" }
        """);

        IReadOnlyList<EditImageJob> jobs = EditImageApplier.ExpandJobs(patch);

        Assert.Single(jobs);
        Assert.Equal("A/I{{Random:null, 3}}", jobs[0].Target);
    }

    // ---------------------------------------------------------------- 落盘（重编码）

    [Fact]
    public void 应用Overlay并重编码png()
    {
        TreeConfig("Characters/Abigail.config", ImageConfig);
        Tree("Characters/Abigail.png", Px(0, 0, 0, 0), 1, 1);
        Pack("Assets/Overlay.png", Px(10, 20, 30, 255), 1, 1);

        var output = _tree = new OutputTree(_src, _out);
        var images = new ImageCache(output);
        MergeOutcome r = new EditImageApplier(output, _pack, images).Apply(
            "Characters/Abigail", "Assets/Overlay.png", PatchImageMode.Overlay,
            null, null, "Characters/Abigail");

        Assert.True(r.Success, r.FailureReason);
        images.Flush();

        byte[] written = Png.Read(Path.Combine(_out, "Characters", "Abigail.png"), out int w, out int h);
        Assert.Equal(1, w);
        Assert.Equal(1, h);
        Assert.Equal(Px(10, 20, 30, 255), written);
    }

    [Fact]
    public void 应用Replace并展开字段引用令牌()
    {
        TreeConfig("Characters/Abigail.config", ImageConfig);
        Tree("Characters/RSVIcon.png", Px(1, 1, 1, 255), 1, 1);
        Pack("Assets/LooseSprites/RSVIcon.png", Px(7, 7, 7, 255), 1, 1);

        var output = _tree = new OutputTree(_src, _out);
        var images = new ImageCache(output);
        MergeOutcome r = new EditImageApplier(output, _pack, images).Apply(
            "Characters/RSVIcon", "Assets/LooseSprites/{{TargetWithoutPath}}.png",
            PatchImageMode.Replace, null, null, "Characters/RSVIcon");

        Assert.True(r.Success, r.FailureReason);
        images.Flush();
        Assert.Equal(Px(7, 7, 7, 255),
            Png.Read(Path.Combine(_out, "Characters", "RSVIcon.png"), out _, out _));
    }

    /// <summary>
    /// ★ `EditImage` 也要**镜像到已存在的语言变体**：中文存档加载 `asset.zh-CN`，
    /// 只改基础资产等于没改（主界面/季节图集就是这个症状）。
    /// </summary>
    [Fact]
    public void EditImage镜像到已存在的语言变体()
    {
        TreeConfig("Characters/Abigail.config", ImageConfig);
        TreeConfig("Characters/Abigail.zh-CN.config", ImageConfig);
        Tree("Characters/Abigail.png", Px(0, 0, 0, 255), 1, 1);
        Tree("Characters/Abigail.zh-CN.png", Px(5, 5, 5, 255), 1, 1);
        Pack("Assets/Overlay.png", Px(9, 8, 7, 255), 1, 1);

        var output = _tree = new OutputTree(_src, _out);
        var images = new ImageCache(output);
        MergeOutcome r = new EditImageApplier(output, _pack, images).Apply(
            "Characters/Abigail", "Assets/Overlay.png", PatchImageMode.Replace,
            null, null, "Characters/Abigail");

        Assert.True(r.Success, r.FailureReason);
        images.Flush();
        Assert.Equal(Px(9, 8, 7, 255),
            Png.Read(Path.Combine(_out, "Characters", "Abigail.png"), out _, out _));
        Assert.Equal(Px(9, 8, 7, 255),
            Png.Read(Path.Combine(_out, "Characters", "Abigail.zh-CN.png"), out _, out _));
    }

    /// <summary>
    /// 回归钉子：同包内先 <c>Load</c> 一张图、紧接着 <c>EditImage</c> 同一资产时，后者必须看得到前者。
    /// <para>
    /// 延迟写模式下刚 <c>Load</c> 进来的图**磁盘上还没有**，直接读盘会误判成「目标不存在」。
    /// 实测 SVE 有 7 条这样被打的资产（<c>Mods/FlashShifter…/BlueJunimo</c>、<c>Characters/GuntherSilvian</c> 等），
    /// 修之前它们**全部**报「目标不存在」而不写。
    /// </para>
    /// </summary>
    [Fact]
    public void 同包内刚Load进来的图EditImage看得到()
    {
        TreeConfig("Characters/Abigail.config", ImageConfig);
        Pack("Assets/Base.png", Px(0, 0, 0, 255, 0, 0, 0, 255), 2, 1);   // 2×1 ⇒ 两个像素
        Pack("Assets/Overlay.png", Px(10, 20, 30, 255), 1, 1);

        var output = _tree = new OutputTree(_src, _out);
        var images = new ImageCache(output);

        Assert.True(new ImageApplier(output, _pack, images).Apply("Characters/New", "Assets/Base.png").Success);
        // 目标资产是本次运行里新建的 —— 这会儿磁盘上还没有它
        Assert.False(File.Exists(Path.Combine(_out, "Characters", "New.png")));

        MergeOutcome r = new EditImageApplier(output, _pack, images).Apply(
            "Characters/New", "Assets/Overlay.png", PatchImageMode.Replace,
            Area(0, 0, 1, 1), Area(0, 0, 1, 1), "Characters/New");

        Assert.True(r.Success, r.FailureReason);

        images.Flush();
        byte[] written = Png.Read(Path.Combine(_out, "Characters", "New.png"), out int w, out int h);
        Assert.Equal(2, w);
        Assert.Equal(1, h);
        Assert.Equal(Px(10, 20, 30, 255), written[..4]);     // 被覆盖的那格
        Assert.Equal(Px(0, 0, 0, 255), written[4..]);        // 没被碰的那格还是 Load 进来的底图
    }

    [Fact]
    public void 目标不存在时失败()
    {
        TreeConfig("Characters/Abigail.config", ImageConfig);
        Pack("Assets/Overlay.png", Px(10, 20, 30, 255), 1, 1);

        var output = _tree = new OutputTree(_src, _out);
        var r = new EditImageApplier(output, _pack, new ImageCache(output)).Apply(
            "Characters/NotThere", "Assets/Overlay.png", PatchImageMode.Overlay, null, null, null);

        Assert.False(r.Success);
        Assert.Contains("不存在", r.FailureReason);
    }

    [Fact]
    public void FromFile含无法静态解析的令牌时失败()
    {
        TreeConfig("Characters/Abigail.config", ImageConfig);
        Tree("Characters/Abigail.png", Px(1, 1, 1, 255), 1, 1);

        var output = _tree = new OutputTree(_src, _out);
        var r = new EditImageApplier(output, _pack, new ImageCache(output)).Apply(
            "Characters/Abigail", "Assets/Icon_{{Random: 1, 2}}.png",
            PatchImageMode.Replace, null, null, "Characters/Abigail");

        Assert.False(r.Success);
        Assert.Contains("令牌", r.FailureReason);
    }
}
