using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// CP 的配置令牌（<c>content.json</c> 的 <c>ConfigSchema</c> 字段名）：取数链
/// <c>config.json</c> → <c>ConfigSchema.Default</c>，以及 CP 的条件匹配语义
/// （<c>Condition.cs:66</c>：令牌值集合与条件声明值集合<b>有交集即匹配</b>）。
/// </summary>
public class ConfigSchemaTokensTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvcfg_" + Guid.NewGuid().ToString("N"));
    private readonly string _mods = Path.Combine(Path.GetTempPath(), "sdvcfgmods_" + Guid.NewGuid().ToString("N"));

    public ConfigSchemaTokensTests()
    {
        Directory.CreateDirectory(_pack);
        Directory.CreateDirectory(_mods);
    }

    public void Dispose()
    {
        Directory.Delete(_pack, recursive: true);
        Directory.Delete(_mods, recursive: true);
    }

    /// <summary>写 <c>content.json</c>（<paramref name="configSchema"/> 为 <c>ConfigSchema</c> 的内容）。</summary>
    private void Pack(string configSchema, string? configJson = null, string dynamicTokens = "[]")
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"),
            $$"""
            {
              "Format": "2.0.0",
              "ConfigSchema": { {{configSchema}} },
              "DynamicTokens": {{dynamicTokens}},
              "Changes": []
            }
            """);
        if (configJson != null) File.WriteAllText(Path.Combine(_pack, "config.json"), configJson);
    }

    private static WhenNode When(string json) => WhenParser.Parse(JObject.Parse(json))!;

    private Baker Build() => new(new InstalledModIndex(_mods), _pack, "Tester.Pack");

    // ---------------------------------------------------------------- 取数链

    [Fact]
    public void ConfigSchema有Default而无configjson时取Default()
    {
        Pack("""
             "Goat Skins": {
               "AllowValues": "SolidCream, SolidBlonde",
               "AllowMultiple": true,
               "Default": "SolidCream, SolidBlonde"
             }
             """);

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.True(tokens.TryGetValues("Goat Skins", out var values));
        Assert.Equal(new[] { "SolidCream", "SolidBlonde" }, values);
    }

    [Fact]
    public void configjson有值时优先于Default()
    {
        Pack("""
             "Goat Skins": {
               "AllowValues": "SolidCream, SolidBlonde, LongSolidGrey, LongSolidSilver",
               "AllowMultiple": true,
               "Default": "SolidCream, SolidBlonde"
             }
             """,
            """{ "Goat Skins": "LongSolidGrey, LongSolidSilver" }""");

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.True(tokens.TryGetValues("Goat Skins", out var values));
        Assert.Equal(new[] { "LongSolidGrey", "LongSolidSilver" }, values);
    }

    [Fact]
    public void configjson的键名大小写不敏感()
    {
        Pack("""
             "Goat Skins": { "Default": "SolidCream" }
             """,
            """{ "goat skins": "SolidBlonde" }""");

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.True(tokens.TryGetValues("goat skins", out var values));
        Assert.Equal(new[] { "SolidBlonde" }, values);
    }

    [Fact]
    public void configjson里是空串时回退Default()
    {
        Pack("""
             "Goat Skins": { "Default": "SolidCream" }
             """,
            """{ "Goat Skins": "" }""");

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.True(tokens.TryGetValues("Goat Skins", out var values));
        Assert.Equal(new[] { "SolidCream" }, values);
    }

    [Fact]
    public void 单值字段是单值()
    {
        Pack("""
             "WarpLocation": {
               "AllowValues": "default, custom",
               "Default": "default"
             }
             """,
            """{ "WarpLocation": "custom" }""");

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.True(tokens.TryGetValues("WarpLocation", out var values));
        Assert.Equal(new[] { "custom" }, values);
    }

    [Fact]
    public void AllowMultiple为true时逗号分隔值切成多值()
    {
        Pack("""
             "Skins": { "AllowMultiple": true, "Default": "A, B, A" }
             """);

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.True(tokens.TryGetValues("Skins", out var values));
        Assert.Equal(new[] { "A", "B" }, values);   // 去重且保序（CP 用的是集合）
    }

    [Fact]
    public void 包没有ConfigSchema时为空()
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"), """{ "Format": "2.0.0", "Changes": [] }""");

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.True(tokens.IsEmpty);
        Assert.False(tokens.Contains("Goat Skins"));
    }

    [Fact]
    public void 内容包不存在时为空()
    {
        var tokens = ConfigSchemaTokens.Load(Path.Combine(_pack, "没有这个目录"));

        Assert.True(tokens.IsEmpty);
    }

    [Fact]
    public void Default不在AllowValues内时字段被CP丢弃()
    {
        Pack("""
             "Bad": { "AllowValues": "A, B", "Default": "C" }
             """);

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.False(tokens.Contains("Bad"));
    }

    [Fact]
    public void AllowBlank为false且无Default时字段被CP丢弃()
    {
        Pack("""
             "Bad": { "AllowValues": "A, B" }
             """);

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.False(tokens.Contains("Bad"));
    }

    [Fact]
    public void 字段名含冒号或竖线时被CP丢弃()
    {
        Pack("""
             "Bad:Name": { "Default": "A" },
             "Bad|Name": { "Default": "A" }
             """);

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.True(tokens.IsEmpty);
    }

    [Fact]
    public void configjson值超出AllowValues时回退Default()
    {
        Pack("""
             "Skins": { "AllowValues": "A, B", "Default": "A" }
             """,
            """{ "Skins": "Z" }""");

        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.True(tokens.TryGetValues("Skins", out var values));
        Assert.Equal(new[] { "A" }, values);
    }

    // ---------------------------------------------------------------- 条件语义

    [Fact]
    public void 多值字段的裸相等走交集语义()
    {
        // 玩家勾了 4 种山羊皮；`{"Goat Skins": "SolidCream"}` 在 CP 里成立
        // （Condition.cs:66：令牌值集合 ∩ 声明值集合 ≠ ∅）。
        Pack("""
             "Goat Skins": {
               "AllowValues": "SolidCream, SolidBlonde, LongSolidGrey, LongSolidSilver",
               "AllowMultiple": true,
               "Default": "SolidCream, SolidBlonde, LongSolidGrey, LongSolidSilver"
             }
             """);

        Baker.Result hit = Build().Evaluate(When("""{"Goat Skins": "SolidCream"}"""));
        Baker.Result miss = Build().Evaluate(When("""{"Goat Skins": "SpottedBlack"}"""));

        Assert.True(hit.Success);
        Assert.True(hit.Value);
        Assert.True(miss.Success);
        Assert.False(miss.Value);
    }

    [Fact]
    public void 多值字段的contains能命中()
    {
        Pack("""
             "Goat Skins": {
               "AllowValues": "SolidCream, SolidBlonde",
               "AllowMultiple": true,
               "Default": "SolidCream, SolidBlonde"
             }
             """);

        Baker.Result hit = Build().Evaluate(When("""{"Goat Skins |contains=SolidBlonde": true}"""));
        Baker.Result miss = Build().Evaluate(When("""{"Goat Skins |contains=SpottedBlack": true}"""));

        Assert.True(hit.Success && hit.Value);
        Assert.True(miss.Success && !miss.Value);
    }

    [Fact]
    public void contains的声明值为false时取反()
    {
        Pack("""
             "Goat Skins": {
               "AllowValues": "SolidCream, SolidBlonde",
               "AllowMultiple": true,
               "Default": "SolidCream"
             }
             """);

        // `|contains=X` 的声明值是取反开关（CP Token.cs:97-115 + TokenTranslator 同款约定）
        Baker.Result found = Build().Evaluate(When("""{"Goat Skins |contains=SolidCream": false}"""));
        Baker.Result absent = Build().Evaluate(When("""{"Goat Skins |contains=SolidBlonde": false}"""));

        Assert.True(found.Success && !found.Value);
        Assert.True(absent.Success && absent.Value);
    }

    [Fact]
    public void contains的声明值不是布尔时判失败()
    {
        Pack("""
             "Skins": { "Default": "A" }
             """);

        Baker.Result r = Build().Evaluate(When("""{"Skins |contains=A": "yes"}"""));

        Assert.False(r.Success);
    }

    [Fact]
    public void 单值字段相等()
    {
        Pack("""
             "WarpLocation": { "AllowValues": "default, custom", "Default": "default" }
             """);

        Baker.Result hit = Build().Evaluate(When("""{"WarpLocation": "default"}"""));
        Baker.Result miss = Build().Evaluate(When("""{"WarpLocation": "custom"}"""));

        Assert.True(hit.Success && hit.Value);
        Assert.True(miss.Success && !miss.Value);
    }

    [Fact]
    public void 声明值逗号列表是或()
    {
        Pack("""
             "WarpLocation": { "AllowValues": "default, custom", "Default": "default" }
             """);

        // 真实数据 SVE 里就有 `{"WarpLocation": "default, custom"}`
        Baker.Result r = Build().Evaluate(When("""{"WarpLocation": "default, custom"}"""));

        Assert.True(r.Success && r.Value);
    }

    [Fact]
    public void 无configjson时真实数据形态的字段按Default求值()
    {
        // SVE 的形态：磁盘上没有 config.json（SMAPI 只在玩家改过设置后才写），
        // 此时 CP 用 ConfigSchema.Default —— 我们照做，不能判死。
        Pack("""
             "SeasonalEdits": { "AllowValues": "true, false", "Default": "true" },
             "HardSVECommunityBundles": { "AllowValues": "true, false", "Default": "false" }
             """);

        Baker.Result on = Build().Evaluate(When("""{"SeasonalEdits": true}"""));
        Baker.Result off = Build().Evaluate(When("""{"HardSVECommunityBundles": true}"""));

        Assert.True(on.Success && on.Value);
        Assert.True(off.Success && !off.Value);
    }

    [Fact]
    public void 数值型Default照样求值()
    {
        // SVE 的 `"Default": 48` 是 JSON 数字，不是字符串
        Pack("""
             "WarpX": { "AllowValues": "1, 2, 48", "Default": 48 }
             """);

        Baker.Result hit = Build().Evaluate(When("""{"WarpX": "48"}"""));
        Baker.Result miss = Build().Evaluate(When("""{"WarpX": "1"}"""));

        Assert.True(hit.Success && hit.Value);
        Assert.True(miss.Success && !miss.Value);
    }

    // ---------------------------------------------------------------- 回归

    [Fact]
    public void 不是config字段的令牌行为不变()
    {
        Pack("""
             "Skins": { "Default": "A" }
             """);

        Baker.Result r = Build().Evaluate(When("""{"NotAConfigField": "x"}"""));

        Assert.False(r.Success);
    }

    [Fact]
    public void 包没有ConfigSchema时配置形态的条件行为不变()
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"), """{ "Format": "2.0.0", "Changes": [] }""");

        Baker.Result r = Build().Evaluate(When("""{"Goat Skins": "SolidCream"}"""));

        Assert.False(r.Success);
    }

    // ---------------------------------------------------------------- 分类表接线

    [Fact]
    public void 配置字段名不再被判未知而是可烘焙()
    {
        Pack("""
             "Goat Skins": { "Default": "SolidCream" }
             """);
        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.Equal(TokenKind.Bakeable, ConditionVocabulary.Classify("Goat Skins", tokens));
        Assert.Equal(TokenKind.Unknown, ConditionVocabulary.Classify("Goat Skins"));
    }

    [Fact]
    public void 与内建条件同名的配置字段被丢弃()
    {
        // CP 会丢弃与内建条件同名的 ConfigSchema 字段（ConfigFileHandler.cs:84-88），
        // 所以 `Season` 仍然是内建条件、不会被当成配置令牌求值。
        Pack("""
             "Season": { "Default": "spring" }
             """);
        var tokens = ConfigSchemaTokens.Load(_pack);

        Assert.False(tokens.Contains("Season"));
        Assert.Equal(TokenKind.GsqTranslatable, ConditionVocabulary.Classify("Season", tokens));
        // ⚠ `Season` 现在按**季节确定化**可求值（2026-10-12，与值上的 `{{Season}}` 同一条政策），
        //   所以这里断言的是"求得出、且按春天算"，不再是"求不了值"。
        Assert.True(Build().Evaluate(When("""{"Season": "spring"}""")).Value);
    }

    // ---------------------------------------------------------------- DynamicTokens

    [Fact]
    public void 配置字段在DynamicTokens键里按逗号拼串展开()
    {
        Pack("""
             "Enable Extended Crops Pack": {
               "AllowValues": "true, false",
               "AllowMultiple": true,
               "Default": "true, false"
             }
             """);

        DynamicTokenResolver.Expansion e = DynamicTokenResolver.Load(_pack, null)
            .Expand("crop-{{Enable Extended Crops Pack}}.json");

        Assert.Equal("crop-true, false.json", e.Text);
        Assert.Empty(e.Unresolved);
    }

    /// <summary>
    /// ★ **更正**：`|contains=` 不是"带参数所以不认识"，而是 CP 的算子 —— 要折成 `true`/`false`。
    /// <para>
    /// 下面那条（「原样保留」）钉的是**旧注释**里那句错的规则（"配置字段名不含 `|`，所以带参数的
    /// 一定不是配置字段"）。实测 `[CP] Seasonal Cute Characters` 的 `IndoorsWinterOutfitEnabled`
    /// 值就是 `{{WinterClothing |contains=IndoorAndOutdoor, IndoorOnly}}` —— 不折的话那个令牌的
    /// "值"就是一串字面花括号（拿它当条件必判错）。语义（任一命中）与依据见 `Cp.TokenArgs`。
    /// </para>
    /// <para>认不出的算子（`{{Skins:foo}}`）仍**原样保留** —— 不猜。</para>
    /// </summary>
    [Fact]
    public void 配置字段带contains时折成真假()
    {
        Pack("""
             "Skins": { "Default": "A" }
             """);

        var r = DynamicTokenResolver.Load(_pack, null);
        Assert.Equal("true", r.Expand("{{Skins |contains=A, B}}").Text);
        Assert.Equal("false", r.Expand("{{Skins |contains=Z}}").Text);
        Assert.Equal("{{Skins:foo}}", r.Expand("{{Skins:foo}}").Text);
    }

    [Fact]
    public void 带参数的配置字段在DynamicTokens里原样保留()
    {
        Pack("""
             "Skins": { "Default": "A" }
             """);

        DynamicTokenResolver.Expansion e = DynamicTokenResolver.Load(_pack, null)
            .Expand("{{Skins :foo}}");

        Assert.Equal("{{Skins :foo}}", e.Text);
        Assert.Contains("Skins", e.Unresolved);
    }

    [Fact]
    public void 包没有ConfigSchema时未定义令牌仍原样保留()
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"),
            """{ "Format": "2.0.0", "DynamicTokens": [], "Changes": [] }""");

        DynamicTokenResolver.Expansion e = DynamicTokenResolver.Load(_pack, null).Expand("{{Goat Skins}}");

        Assert.Equal("{{Goat Skins}}", e.Text);
        Assert.Contains("Goat Skins", e.Unresolved);
    }
}
