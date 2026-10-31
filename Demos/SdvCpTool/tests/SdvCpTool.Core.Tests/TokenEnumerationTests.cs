using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// **枚举展开**：把「按季节分块的动态令牌」拆成互斥的几条补丁（<see cref="TokenEnumerationExpander"/>）。
/// <para>
/// 实测来源：SVE 季节性服装的 <c>Aerobics</c> 令牌 ——
/// <c>FromFile: "assets/{{Target}}/{{TargetWithoutPath}}_{{Aerobics}}.png"</c>。
/// 拆之前整条补丁被拒（约 24 个 NPC 的季节服装全丢），拆之后走现成的四季/立绘发射器。
/// </para>
/// <para>
/// ⚠ 这里钉的都是**安全前提**：只在能被证明互斥时拆；证明不了就整条不拆（保持原判）。
/// </para>
/// </summary>
public class TokenEnumerationTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvenum_" + Guid.NewGuid().ToString("N"));

    public TokenEnumerationTests() => Directory.CreateDirectory(_pack);

    public void Dispose()
    {
        if (Directory.Exists(_pack)) Directory.Delete(_pack, recursive: true);
    }

    private void Write(string relative, string content)
    {
        string full = Path.Combine(_pack, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>写一个包（DynamicTokens + Changes）并拆一遍。返回拆分后的补丁。</summary>
    private List<ChangePatch> Expand(string dynamicTokens, string changes,
        out TokenEnumerationExpander.Result result)
    {
        Write("content.json",
            $$"""{ "Format": "2.9.0", "DynamicTokens": [ {{dynamicTokens}} ], "Changes": [ {{changes}} ] }""");

        ContentPack pack = ContentPack.Load(_pack);
        DynamicTokenResolver tokens = DynamicTokenResolver.Load(_pack, null);
        result = TokenEnumerationExpander.Apply(pack.Patches, tokens);
        return pack.Patches;
    }

    /// <summary>一个无条件的默认块 + 三个按季节的条件块 —— 实测那个 <c>Aerobics</c> 的形状。</summary>
    private const string Seasonal =
        """{ "Name": "Aerobics", "Value": "Spring" }, { "Name": "Aerobics", "Value": "Summer", "When": { "Season": "Summer" } }, { "Name": "Aerobics", "Value": "Fall", "When": { "Season": "Fall" } }, { "Name": "Aerobics", "Value": "Winter_Indoor", "When": { "Season": "Winter" } }""";

    private const string AndyPatch =
        """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "assets/{{Target}}/{{TargetWithoutPath}}_{{Aerobics}}.png" }""";

    // ------------------------------------------------------------------ 会拆

    /// <summary>
    /// 形状对 ⇒ 拆成 **4** 条：条件块各自带上自己的季节，默认那块带上**补集**（这里正是 Spring）。
    /// </summary>
    [Fact]
    public void 按季节分块的令牌拆成四季补丁()
    {
        List<ChangePatch> patches = Expand(Seasonal, AndyPatch, out var r);

        Assert.Equal(1, r.Expanded);
        Assert.Equal(3, r.Added);
        Assert.Equal(4, patches.Count);

        var bySeason = patches.ToDictionary(
            p => p.When!.Value<string>("Season")!,
            p => p.FromFile!);

        Assert.Equal(new[] { "Fall", "Spring", "Summer", "Winter" },
            bySeason.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        // ⚠ `{{Target}}`/`{{TargetWithoutPath}}` 由**后面**那套字段引用展开负责，这里只核后缀
        Assert.Equal("assets/{{Target}}/{{TargetWithoutPath}}_Spring.png", bySeason["Spring"]);  // 默认块 = 补集
        Assert.Equal("assets/{{Target}}/{{TargetWithoutPath}}_Summer.png", bySeason["Summer"]);
        Assert.Equal("assets/{{Target}}/{{TargetWithoutPath}}_Fall.png", bySeason["Fall"]);
        Assert.Equal("assets/{{Target}}/{{TargetWithoutPath}}_Winter_Indoor.png", bySeason["Winter"]);

        // `{{Target}}`/`{{TargetWithoutPath}}` **不参与**枚举，原样留着交给后面那套字段引用展开
        Assert.Contains("assets/{{Target}}/{{TargetWithoutPath}}_Spring.png",
            patches.Select(p => p.FromFile));
        Assert.DoesNotContain(patches, p => (p.FromFile ?? "").Contains("{{Aerobics}}"));
    }

    /// <summary>补丁自带的条件与块的条件**同名同值** ⇒ 合并后仍是一条，值取那一块。</summary>
    [Fact]
    public void 同名同值的条件合并而不是丢掉()
    {
        List<ChangePatch> patches = Expand(Seasonal,
            """{ "Action": "EditImage", "Target": "Characters/Andy", "When": { "Season": "Summer" }, "FromFile": "assets/{{Target}}_{{Aerobics}}.png" }""",
            out var r);

        ChangePatch only = Assert.Single(patches);
        Assert.Equal("Summer", only.When!.Value<string>("Season"));
        Assert.Equal("assets/{{Target}}_Summer.png", only.FromFile);
        Assert.Equal(1, r.Expanded);
    }

    /// <summary>
    /// 补丁自带的条件与块的条件**同名不同值** ⇒ 那几条无解，丢掉；留下能成立的那一条。
    /// （CP 的口径：补丁只在夏季生效，那夏季那一块就是唯一活着的。）
    /// </summary>
    [Fact]
    public void 与自带条件冲突的块被丢掉()
    {
        List<ChangePatch> patches = Expand(Seasonal,
            """{ "Action": "EditImage", "Target": "Characters/Andy", "When": { "Season": "Summer" }, "FromFile": "assets/{{Target}}_{{Aerobics}}.png" }""",
            out _);

        Assert.Equal("Summer", Assert.Single(patches).When!.Value<string>("Season"));
    }

    /// <summary>令牌出现在 <c>Target</c> 里也能拆（两个字段一起代）。</summary>
    [Fact]
    public void 令牌在Target里也能拆()
    {
        List<ChangePatch> patches = Expand(Seasonal,
            """{ "Action": "EditImage", "Target": "Characters/Andy_{{Aerobics}}", "FromFile": "assets/Andy.png" }""",
            out var r);

        Assert.Equal(4, patches.Count);
        Assert.Equal(1, r.Expanded);
        var bySeason = patches.ToDictionary(p => p.When!.Value<string>("Season")!, p => p.Target!);
        Assert.Equal("Characters/Andy_Spring", bySeason["Spring"]);
        Assert.Equal("Characters/Andy_Winter_Indoor", bySeason["Winter"]);
    }

    /// <summary>四季**全被条件块占满**时，默认块永远轮不上 ⇒ 只拆 4 条，不补默认。</summary>
    [Fact]
    public void 四季占满时不要默认块()
    {
        List<ChangePatch> patches = Expand(
            """{ "Name": "S", "Value": "none" }, { "Name": "S", "Value": "sp", "When": { "Season": "Spring" } }, { "Name": "S", "Value": "su", "When": { "Season": "Summer" } }, { "Name": "S", "Value": "fa", "When": { "Season": "Fall" } }, { "Name": "S", "Value": "wi", "When": { "Season": "Winter" } }""",
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{S}}.png" }""",
            out _);

        Assert.Equal(4, patches.Count);
        Assert.DoesNotContain(patches, p => p.FromFile == "a_none.png");
    }


    /// <summary>
    /// 默认块的季节是**补集**：条件块只占了两季时，默认那条要写**没被占掉的那两季**（逗号分隔）。
    /// ⚠ 逗号写法必须是我们翻译器认的那一种（CP 的值就是逗号分隔的多值）。
    /// </summary>
    [Fact]
    public void 默认块取两季补集()
    {
        List<ChangePatch> patches = Expand(
            """{ "Name": "S", "Value": "base" }, { "Name": "S", "Value": "su", "When": { "Season": "Summer" } }, { "Name": "S", "Value": "wi", "When": { "Season": "Winter" } }""",
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{S}}.png" }""",
            out _);

        Assert.Equal(3, patches.Count);
        ChangePatch fallback = Assert.Single(patches, p => p.FromFile == "a_base.png");
        string seasons = fallback.When!.Value<string>("Season")!;
        Assert.Contains("Spring", seasons);
        Assert.Contains("Fall", seasons);
        Assert.DoesNotContain("Summer", seasons);
        Assert.DoesNotContain("Winter", seasons);
    }

    // ------------------------------------------------------------------ 不拆

    /// <summary>
    /// ★ 无条件块排在**条件块之后** ⇒ CP 是「最后一个匹配块胜出」，那个无条件块永远赢，
    /// 条件块根本轮不上 —— 拆开就把语义**反**了。整条不动。
    /// </summary>
    [Fact]
    public void 默认块排在条件块之后时不拆()
    {
        List<ChangePatch> patches = Expand(
            """{ "Name": "X", "Value": "a", "When": { "Season": "Summer" } }, { "Name": "X", "Value": "b" }""",
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{X}}.png" }""",
            out var r);

        Assert.Equal(0, r.Expanded);
        Assert.Single(patches);
        Assert.Contains("{{X}}", patches[0].FromFile);
    }

    /// <summary>条件块的键**不是** <c>Season</c> ⇒ 互斥性证不出来，不拆。</summary>
    [Fact]
    public void 条件键不是Season时不拆()
    {
        List<ChangePatch> patches = Expand(
            """{ "Name": "X", "Value": "a" }, { "Name": "X", "Value": "b", "When": { "DayOfWeek": "Friday" } }""",
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{X}}.png" }""",
            out var r);

        Assert.Equal(0, r.Expanded);
        Assert.Single(patches);
    }

    /// <summary>同一季出现两块 ⇒ 谁赢不确知，不拆。</summary>
    [Fact]
    public void 同季重复时不拆()
    {
        List<ChangePatch> patches = Expand(
            """{ "Name": "X", "Value": "a" }, { "Name": "X", "Value": "b", "When": { "Season": "Summer" } }, { "Name": "X", "Value": "c", "When": { "Season": "summer" } }""",
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{X}}.png" }""",
            out var r);

        Assert.Equal(0, r.Expanded);
        Assert.Single(patches);
    }

    /// <summary>块的值里有**展不干净**的令牌 ⇒ 不拆（这里 <c>{{Spouse}}</c> 是运行时令牌）。</summary>
    [Fact]
    public void 块的值展不干净时不拆()
    {
        List<ChangePatch> patches = Expand(
            """{ "Name": "X", "Value": "a" }, { "Name": "X", "Value": "{{Spouse}}", "When": { "Season": "Summer" } }""",
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{X}}.png" }""",
            out var r);

        Assert.Equal(0, r.Expanded);
        Assert.Single(patches);
    }

    /// <summary>一条补丁里**两个**可枚举令牌 ⇒ 不猜（按季节分的不止一个维度）。</summary>
    [Fact]
    public void 两个可枚举令牌时不拆()
    {
        List<ChangePatch> patches = Expand(
            Seasonal + ", { \"Name\": \"Hat\", \"Value\": \"None\" }, { \"Name\": \"Hat\", \"Value\": \"Straw\", \"When\": { \"Season\": \"Summer\" } }",
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{Aerobics}}_{{Hat}}.png" }""",
            out var r);

        Assert.Equal(0, r.Expanded);
        Assert.Single(patches);
    }

    /// <summary>带参数的写法（<c>|contains=…</c>）不在枚举范围内 —— 不猜。</summary>
    [Fact]
    public void 带参数的令牌不拆()
    {
        List<ChangePatch> patches = Expand(Seasonal,
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{Aerobics |contains=Spring}}.png" }""",
            out var r);

        Assert.Equal(0, r.Expanded);
        Assert.Single(patches);
    }

    /// <summary>令牌没在本包 <c>DynamicTokens</c> 里定义 ⇒ 一条不动。</summary>
    [Fact]
    public void 令牌不存在时不动()
    {
        List<ChangePatch> patches = Expand(Seasonal,
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{Nope}}.png" }""",
            out var r);

        Assert.Equal(0, r.Expanded);
        Assert.Single(patches);
    }

    /// <summary>拆出来的每条都保留原始出处（<c>SourceFile</c>/<c>Index</c>/<c>Action</c>）。</summary>
    [Fact]
    public void 拆出来的条目保留出处()
    {
        List<ChangePatch> patches = Expand(Seasonal, AndyPatch, out _);

        Assert.All(patches, p =>
        {
            Assert.Equal("EditImage", p.Action);
            Assert.EndsWith("content.json", p.SourceFile);
            Assert.Equal(0, p.Index);
        });
    }


    // ---------------------------------------------------------- 审查逮到的两个反例

    /// <summary>
    /// ★ **没有无条件默认块**且没占满四季 ⇒ **不拆**（而不是崩）。
    /// 审查拿这个形状复现过一次 `NullReferenceException`：未被占用的那几季里这个令牌**根本没有值**，
    /// CP 那边它代成什么并不确定 —— 硬拆就是把"不确定"当"空串"写出去。
    /// </summary>
    [Fact]
    public void 没有默认块且未占满四季时不拆()
    {
        List<ChangePatch> patches = Expand(
            """{ "Name": "X", "Value": "su", "When": { "Season": "Summer" } }, { "Name": "X", "Value": "wi", "When": { "Season": "Winter" } }""",
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{X}}.png" }""",
            out var r);

        Assert.Equal(0, r.Expanded);
        Assert.Single(patches);                       // 原判保持，一条不动
        Assert.Contains("{{X}}", patches[0].FromFile);
    }

    /// <summary>默认块的值是**空串**时同理（`a_{{X}}.png` 会写成 `a_.png`）—— 宁可少写。</summary>
    [Fact]
    public void 默认块值为空串时不拆()
    {
        List<ChangePatch> patches = Expand(
            """{ "Name": "X", "Value": "" }, { "Name": "X", "Value": "su", "When": { "Season": "Summer" } }""",
            """{ "Action": "EditImage", "Target": "Characters/Andy", "FromFile": "a_{{X}}.png" }""",
            out var r);

        Assert.Equal(0, r.Expanded);
        Assert.Single(patches);
    }

    /// <summary>
    /// ★ 拆出来的那一块的值要**贯穿全条补丁** —— 不只 `Target`/`FromFile`，
    /// **`When` 里的路径也要换**。实测代价（新语料 94 条）：East Scarp 的动画立绘包写
    /// `Target: Portraits/Aideen_{{Season}}` + `When: {HasFile: assets/Aideen/Aideen_{{Season}}.png}`，
    /// 只换了目标名 ⇒ 同一个令牌留在 `When` 里 ⇒ “文件在不在”判不了 ⇒ 整条退回 `NeedsReview`。
    /// </summary>
    [Fact]
    public void 拆分时令牌贯穿全条包括When()
    {
        List<ChangePatch> patches = Expand(Seasonal, """
        { "Action": "EditImage", "Target": "Portraits/Aideen_{{Aerobics}}",
          "FromFile": "assets/Aideen_{{Aerobics}}.png",
          "When": { "HasFile:assets/Aideen/Aideen_{{Aerobics}}.png": true } }
        """, out _);

        Assert.Equal(4, patches.Count);
        foreach (ChangePatch p in patches)
        {
            string when = p.When!.ToString(Newtonsoft.Json.Formatting.None);
            Assert.DoesNotContain("{{", when);                    // When 里的令牌必须换掉
            Assert.DoesNotContain("{{", p.Target);
            Assert.DoesNotContain("{{", p.FromFile!);
            // 三处的季节必须**是同一个**（Target / FromFile / When 的路径）
            // ⚠ 值里本身可能带下划线（实测那个令牌的冬天是 `Winter_Indoor`）⇒ 取 `Aideen_` 之后的**全部**，别用 Split('_').Last()
            string value = p.Target!["Portraits/Aideen_".Length..];
            Assert.Contains("Aideen_" + value, p.FromFile!);
            Assert.Contains("Aideen/" + "Aideen_" + value + ".png", when);
        }
    }

    /// <summary>
    /// ★ 令牌**只出现在 `When` 里**也要能拆。实测（新语料 94 条）：东海岸的动画立绘包写
    /// `Target: Portraits/Aideen_{{Aerobics}}` + `When: { "HasFile:assets/Aideen/Aideen_{{Aerobics}}.png": true }`，
    /// 以前只扫 `Target`/`FromFile` ⇒ 条件里的令牌留着 ⇒ “文件在不在”判不了 ⇒ 整条退回 `NeedsReview`。
    /// </summary>
    [Fact]
    public void 令牌只在When里也要拆()
    {
        List<ChangePatch> patches = Expand(Seasonal, """
        { "Action": "EditImage", "Target": "Portraits/Aideen",
          "FromFile": "assets/Aideen.png",
          "When": { "HasFile:assets/Aideen/Aideen_{{Aerobics}}.png": true } }
        """, out _);

        Assert.Equal(4, patches.Count);
        foreach (ChangePatch p in patches)
        {
            string when = p.When!.ToString(Newtonsoft.Json.Formatting.None);
            Assert.DoesNotContain("{{", when);
            Assert.Contains("HasFile:assets/Aideen/Aideen_", when);      // 键名换了、没被拆坏
        }
    }
}
