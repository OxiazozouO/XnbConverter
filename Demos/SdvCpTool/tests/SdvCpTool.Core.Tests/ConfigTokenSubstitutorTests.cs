using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// <c>Target</c>/<c>FromFile</c> 里的 <c>{{&lt;配置字段&gt;}}</c> 代入。
/// <para>
/// 依据 CP <c>TokenString.cs:290</c>：令牌代进字符串时是 <c>string.Join(", ", values)</c>。
/// 多值字段因此展开成逗号分隔 —— 而 <c>Target</c> 本来就按 <c>,</c> 切分，两者正好吻合。
/// </para>
/// </summary>
public class ConfigTokenSubstitutorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvcts_" + Guid.NewGuid().ToString("N"));

    public ConfigTokenSubstitutorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>造一个只有 <c>ConfigSchema</c> 的内容包（不写 config.json ⇒ 全用 Default）。</summary>
    private ConfigSchemaTokens Schema(string schemaJson)
    {
        File.WriteAllText(Path.Combine(_dir, "content.json"), $$"""{ "ConfigSchema": {{schemaJson}} }""");
        return ConfigSchemaTokens.Load(_dir);
    }

    /// <summary>
    /// ★ <c>FromFile</c> 里的 <c>{{Random}}</c> 也**确定化**（取第一个备选）。
    /// <para>
    /// CP 会挑一个文件（种子含存档 id 与天数、每次上下文更新重掷）；我们代不了就整条不写 ——
    /// 而实测野生动物包有 **17 条** <c>Load</c> 就长这样（<c>assets/special/{{Random: DustSpirit_Normal, …}}.png</c>），
    /// 不代等于那些动物**没有贴图**。代成第一个只是「不再随机」。
    /// </para>
    /// </summary>
    [Fact]
    public void FromFile里的Random代成第一个备选()
    {
        ConfigSchemaTokens schema = Schema("""{ }""");
        var resolver = DynamicTokenResolver.Load(_dir, null);
        ChangePatch patch = Patch("Characters/DustSpirit_A_WA",
            "assets/special/{{Random: DustSpirit_Normal, DustSpirit_Arms}}.png");

        ConfigTokenSubstitutor.Apply(new[] { patch }, schema, resolver);

        Assert.Equal("assets/special/DustSpirit_Normal.png", patch.FromFile);
        Assert.Equal("assets/special/DustSpirit_Normal.png", patch.Raw["FromFile"]!.Value<string>());
    }

    /// <summary>
    /// `FromFile` 里的令牌**有损但结果具体到文件**时接受（落到无条件默认块）。
    /// 实测 S&amp;S 的 `Portraits/Hector ← …/{{TrueShape}}/spring_Hector.png`：
    /// 条件块判不了 ⇒ 默认块 `{{Random: male, female}}` ⇒ `male`，是真实文件；
    /// 不代的话 Hector 连立绘都没有，游戏里 `Failed to spawn NPC`。
    /// </summary>
    [Fact]
    public void FromFile里的有损令牌落默认块并走通()
    {
        File.WriteAllText(Path.Combine(_dir, "content.json"), """
        {
          "ConfigSchema": { },
          "DynamicTokens": [
            { "Name": "TrueShape", "Value": "{{Random: male, female|key=hector-gender}}" },
            { "Name": "TrueShape", "Value": "{{Random: male, female, trueshape}}",
              "When": { "HasSeenEvent": "SnS.Ch2.Hector.16" } }
          ]
        }
        """);
        ConfigSchemaTokens schema = ConfigSchemaTokens.Load(_dir);
        var resolver = DynamicTokenResolver.Load(_dir, null);
        var notes = new List<string>();
        ChangePatch patch = Patch("Portraits/Hector", "assets/Portraits/{{TrueShape}}/spring_Hector.png");

        ConfigTokenSubstitutor.Apply(new[] { patch }, schema, resolver, (_, n) => notes.Add(n));

        Assert.Equal("assets/Portraits/male/spring_Hector.png", patch.FromFile);
        Assert.Contains(notes, n => n.Contains("TrueShape"));
    }

    private static ChangePatch Patch(string target, string? fromFile = null)
    {
        var raw = new JObject { ["Action"] = "Load", ["Target"] = target };
        if (fromFile != null) raw["FromFile"] = fromFile;
        return new ChangePatch
        {
            Raw = raw, Action = "Load", Target = target, FromFile = fromFile, SourceFile = "content.json",
        };
    }

    [Fact]
    public void 单值配置字段代入FromFile()
    {
        ConfigSchemaTokens schema = Schema("""{ "White Cow Default": { "Default": "White Cow" } }""");
        ChangePatch p = Patch("Animals/White Cow", "assets/Cow/{{White Cow Default}}_icon.png");

        Assert.Equal(1, ConfigTokenSubstitutor.Apply(new[] { p }, schema));

        Assert.Equal("assets/Cow/White Cow_icon.png", p.FromFile);
        Assert.Equal("assets/Cow/White Cow_icon.png", p.Raw["FromFile"]!.Value<string>());
    }

    /// <summary>多值字段按 CP 语义用 <c>", "</c> 连接 —— 与 <c>Target</c> 的逗号切分正好吻合。</summary>
    [Fact]
    public void 多值配置字段用逗号加空格连接()
    {
        ConfigSchemaTokens schema = Schema("""{ "Spouses": { "AllowMultiple": true, "Default": "Abigail, Leah" } }""");
        ChangePatch p = Patch("Characters/Dialogue/{{Spouses}}");

        Assert.Equal(1, ConfigTokenSubstitutor.Apply(new[] { p }, schema));

        Assert.Equal("Characters/Dialogue/Abigail, Leah", p.Target);
    }

    /// <summary>不是本包配置字段的令牌**一律不动** —— 下游照旧报「含无法解析的令牌」。</summary>
    [Fact]
    public void 非配置字段的令牌不动()
    {
        ConfigSchemaTokens schema = Schema("""{ "Known": { "Default": "x" } }""");
        ChangePatch p = Patch("LooseSprites/{{Unknown}}");

        Assert.Equal(0, ConfigTokenSubstitutor.Apply(new[] { p }, schema));

        Assert.Equal("LooseSprites/{{Unknown}}", p.Target);
    }

    /// <summary>只认配置字段相等的简易 <c>When</c> 求值器（判不了就返回 null，即「有损」）。</summary>
    private static Func<JObject, DynamicTokenResolver.WhenResult> ConfigWhen(ConfigSchemaTokens schema) =>
        when =>
        {
            foreach (JProperty property in when.Properties())
            {
                if (property.Name.Equals("HasMod", StringComparison.OrdinalIgnoreCase))
                    return new DynamicTokenResolver.WhenResult(null, "测试求值器不认 HasMod");
                if (!schema.TryGetValues(property.Name, out IReadOnlyList<string> values))
                    return new DynamicTokenResolver.WhenResult(null, $"不认识的条件字段 {property.Name}");
                return new DynamicTokenResolver.WhenResult(values.Contains(property.Value.Value<string>() ?? ""));
            }
            return new DynamicTokenResolver.WhenResult(true);
        };

    /// <summary>
    /// ★ CP 内建的 <c>{{Season}}</c>（= 当前季节、单值）在 <c>Target</c>/<c>FromFile</c> 里
    /// **确定化到春天**：纯 XNB 里一个资产键只有一份内容，「同一个资产名按季换整张图」表达不了。
    /// <para>
    /// 实测 `[CP] HxW Tilesheets`：<c>FromFile: Assets/{{Coloring}}/{{SeasonWithDefault}}/….png</c>，
    /// 包内令牌展开之后浮出来的正是内建 <c>{{Season}}</c>；不代就是整条落空 ⇒
    /// **引用它的 7 张地图建不出来**。
    /// </para>
    /// </summary>
    [Fact]
    public void 内建Season确定化到春天()
    {
        ConfigSchemaTokens schema = Schema("""{ }""");
        var resolver = DynamicTokenResolver.Load(_dir, null);
        ChangePatch p = Patch("Maps/hime_outdoorplants", "Assets/Vanilla/{{Season}}/hime_outdoorplants.png");
        var notes = new List<string>();

        Assert.Equal(1, ConfigTokenSubstitutor.Apply(new[] { p }, schema, resolver, (_, n) => notes.Add(n)));

        Assert.Equal("Assets/Vanilla/spring/hime_outdoorplants.png", p.FromFile);
        Assert.Contains(notes, n => n.Contains("春天"));
    }

    /// <summary>
    /// ★ CP 内建的 <c>{{Language}}</c> 在 <c>Target</c>/<c>FromFile</c> 里**确定化到 `zh`**。
    /// 实测界面包 <c>[CP]猫露谷物语(Interface)</c> 的
    /// <c>FromFile: assets/{{Target}}_{{language}}.png</c> —— 不代就是 <c>TargetMissing</c>，
    /// 中文界面图整批不写（实机就是"主界面还是英文"）。
    /// </summary>
    [Fact]
    public void 内建Language确定化到中文()
    {
        ConfigSchemaTokens schema = Schema("""{ }""");
        var resolver = DynamicTokenResolver.Load(_dir, null);
        ChangePatch p = Patch("Minigames/TitleButtons", "assets/{{Target}}_{{Language}}.png");
        var notes = new List<string>();

        Assert.Equal(1, ConfigTokenSubstitutor.Apply(new[] { p }, schema, resolver, (_, n) => notes.Add(n)));

        // `{{Target}}` 是字段引用令牌，由下游 `LoadApplier.ExpandFieldTokens` 逐目标展开 —— 这里只管语言。
        Assert.Equal("assets/{{Target}}_zh.png", p.FromFile);
        Assert.Contains(notes, n => n.Contains("中文"));
    }

    /// <summary>包内**自己**定义了 `Season` 令牌时轮不到我们猜 —— 只代包内那个。</summary>
    [Fact]
    public void 包内自有Season令牌时不代成春天()
    {
        File.WriteAllText(Path.Combine(_dir, "content.json"), """
            { "ConfigSchema": { },
              "DynamicTokens": [ { "Name": "Season", "Value": "Summer" } ] }
            """);
        ConfigSchemaTokens schema = ConfigSchemaTokens.Load(_dir);
        var resolver = DynamicTokenResolver.Load(_dir, null);
        ChangePatch p = Patch("Maps/x", "Assets/{{Season}}/x.png");

        ConfigTokenSubstitutor.Apply(new[] { p }, schema, resolver);

        Assert.Equal("Assets/Summer/x.png", p.FromFile);      // 包内令牌优先，没被代成 spring
    }

    /// <summary>
    /// ★ <c>Target</c>/<c>FromFile</c> 里的**包内 <c>DynamicTokens</c>** 也要代。
    /// <para>
    /// 实测 `[CP] DaisyNiko's Tilesheets`：<c>Load Maps/spring_daisyextras, …</c>
    /// <c>← assets/{{recolour}}/{{TargetWithoutPath}}.png</c> —— <c>recolour</c> 是它自己用
    /// 配置字段拼出来的 DynamicToken。不代就是**整条落空**，包内图集进不了产出树，
    /// 引用它的 29 张地图建不出来（`{{TargetWithoutPath}}` 由字段引用那条路另代）。
    /// </para>
    /// </summary>
    [Fact]
    public void DynamicTokens的令牌在FromFile里也代掉()
    {
        File.WriteAllText(Path.Combine(_dir, "content.json"), """
            {
              "ConfigSchema": { "ManualRecolourSelection": { "AllowValues": "Off, Earthy", "Default": "Off" } },
              "DynamicTokens": [
                { "Name": "recolour", "Value": "Earthy", "When": { "ManualRecolourSelection": "Off" } },
                { "Name": "recolour", "Value": "Starblue", "When": { "ManualRecolourSelection": "Starblue" } }
              ]
            }
            """);
        ConfigSchemaTokens schema = ConfigSchemaTokens.Load(_dir);
        var resolver = DynamicTokenResolver.Load(_dir, ConfigWhen(schema));
        ChangePatch p = Patch("Maps/spring_daisyextras", "assets/{{recolour}}/{{TargetWithoutPath}}.png");
        var notes = new List<string>();

        Assert.Equal(1, ConfigTokenSubstitutor.Apply(new[] { p }, schema, resolver, (_, n) => notes.Add(n)));

        Assert.Equal("assets/Earthy/{{TargetWithoutPath}}.png", p.FromFile);
        Assert.Equal("assets/Earthy/{{TargetWithoutPath}}.png", p.Raw["FromFile"]!.Value<string>());
        Assert.Empty(notes);        // 条件判得了 ⇒ 无损，不该有确定化说明
    }

    /// <summary>
    /// **有损**（条件判不了、只好落到无条件默认块）的 DynamicToken：只要默认块给出的是**具体路径**，
    /// 就代掉并报出来 —— 路径不代等于整条 `Load` 落空（资产不存在）。
    /// <para>⚠ 块序按 CP 的形态：**无条件默认块在列表最前**，条件块在后（解析器从后往前找）。</para>
    /// </summary>
    [Fact]
    public void 有损的DynamicToken落到默认块并报出来()
    {
        File.WriteAllText(Path.Combine(_dir, "content.json"), """
            {
              "ConfigSchema": { },
              "DynamicTokens": [
                { "Name": "recolour", "Value": "Starblue" },
                { "Name": "recolour", "Value": "Earthy", "When": { "HasMod": "Nope.X" } }
              ]
            }
            """);
        ConfigSchemaTokens schema = ConfigSchemaTokens.Load(_dir);
        var resolver = DynamicTokenResolver.Load(_dir, ConfigWhen(schema));
        ChangePatch p = Patch("Maps/spring_daisyextras", "assets/{{recolour}}/x.png");
        var notes = new List<string>();

        ConfigTokenSubstitutor.Apply(new[] { p }, schema, resolver, (_, n) => notes.Add(n));

        Assert.Equal("assets/Starblue/x.png", p.FromFile);
        Assert.Contains(notes, n => n.Contains("recolour"));
    }

    /// <summary>
    /// 带参数的令牌（<c>{{X|contains=Y}}</c> / <c>{{X:input}}</c>）不是"整值代入"的形态，不动。
    /// 代进去会得到一个语法上不像资产名的字符串，错得更远。
    /// </summary>
    [Theory]
    [InlineData("LooseSprites/{{Known|contains=Y}}")]
    [InlineData("LooseSprites/{{Known:input}}")]
    public void 带参数的令牌不动(string target)
    {
        ConfigSchemaTokens schema = Schema("""{ "Known": { "Default": "x" } }""");
        ChangePatch p = Patch(target);

        Assert.Equal(0, ConfigTokenSubstitutor.Apply(new[] { p }, schema));
        Assert.Equal(target, p.Target);
    }

    /// <summary>一个字段里出现两个令牌时都要代。</summary>
    [Fact]
    public void 同一字段里多个令牌都代()
    {
        ConfigSchemaTokens schema = Schema("""{ "A": { "Default": "aa" }, "B": { "Default": "bb" } }""");
        ChangePatch p = Patch("Mods/X/{{A}}_{{B}}");

        Assert.Equal(1, ConfigTokenSubstitutor.Apply(new[] { p }, schema));

        Assert.Equal("Mods/X/aa_bb", p.Target);
    }

    /// <summary>没有 <c>ConfigSchema</c> 时**什么都不做**，不是把令牌代成空串。</summary>
    [Fact]
    public void 没有ConfigSchema时不动()
    {
        ChangePatch p = Patch("LooseSprites/{{Whatever}}");

        Assert.Equal(0, ConfigTokenSubstitutor.Apply(new[] { p }, ConfigSchemaTokens.Empty));
        Assert.Equal("LooseSprites/{{Whatever}}", p.Target);
    }

    /// <summary>对话**值**里的令牌不受影响 —— 那只动 Target/FromFile。</summary>
    [Fact]
    public void 只动Target与FromFile()
    {
        ConfigSchemaTokens schema = Schema("""{ "Known": { "Default": "x" } }""");
        var raw = new JObject
        {
            ["Action"] = "EditData",
            ["Target"] = "Data/Things",
            ["Entries"] = new JObject { ["k"] = "value with {{Known}}" },
        };
        var p = new ChangePatch { Raw = raw, Action = "EditData", Target = "Data/Things" };

        ConfigTokenSubstitutor.Apply(new[] { p }, schema);

        Assert.Equal("value with {{Known}}", raw["Entries"]!["k"]!.Value<string>());
    }
}
