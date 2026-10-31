using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

public class DynamicTokenResolverTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvdt_" + Guid.NewGuid().ToString("N"));

    public DynamicTokenResolverTests() => Directory.CreateDirectory(_pack);

    public void Dispose() => Directory.Delete(_pack, recursive: true);

    private async Task<DynamicTokenResolver> Load(string dynamicTokens,
        Func<JObject, DynamicTokenResolver.WhenResult>? eval = null)
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"),
            $$"""{ "Format": "2.9.0", "DynamicTokens": [ {{dynamicTokens}} ], "Changes": [] }""");
        return DynamicTokenResolver.Load(_pack, eval);
    }

    /// <summary>只认 HasMod 的简易求值器：isInstalled 里的 mod 视为已装。</summary>
    private static Func<JObject, DynamicTokenResolver.WhenResult> HasMod(params string[] isInstalled) =>
        when =>
        {
            JToken? value = when["HasMod"];
            if (value == null) return new DynamicTokenResolver.WhenResult(null, "测试求值器只认 HasMod");
            string? id = value.Value<string>();
            if (id == null) return new DynamicTokenResolver.WhenResult(null, "HasMod 无值");
            return new DynamicTokenResolver.WhenResult(isInstalled.Contains(id));
        };

    // ------------------------------------------------------------ 带参数的配置字段

    /// <summary>
    /// ★ **配置字段带 `|contains=`** 的用法要折成 `true`/`false`。
    /// <para>
    /// 那条分支上原来写着「配置字段名本身不含 `:` / `|`，所以带参数的一定不是配置字段」——
    /// **字段名**里确实不会有，但**用法**可以带。实测 `[CP] Seasonal Cute Characters` 的
    /// <c>IndoorsWinterOutfitEnabled</c> 值就是
    /// <c>{{WinterClothing |contains=IndoorAndOutdoor, IndoorOnly}}</c>；代不掉时那个令牌的
    /// "值"就是一串字面花括号，拿它当条件必判错（上一版"令牌当条件"改动就栽在这里）。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 配置字段带contains时折成真假()
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"), """
            { "Format": "2.9.0",
              "ConfigSchema": { "WinterClothing": {
                  "AllowValues": "OutdoorOnly, IndoorAndOutdoor, IndoorOnly", "Default": "IndoorAndOutdoor" } },
              "DynamicTokens": [
                { "Name": "IndoorsWinterOutfitEnabled", "Value": "{{WinterClothing |contains=IndoorAndOutdoor, IndoorOnly}}" },
                { "Name": "IndoorsWinterOutfitDisabled", "Value": "{{WinterClothing |contains=OutdoorOnly}}" } ],
              "Changes": [] }
            """);
        var r = DynamicTokenResolver.Load(_pack, null);

        Assert.Equal("true", r.Expand("{{IndoorsWinterOutfitEnabled}}").Text);
        Assert.Equal("false", r.Expand("{{IndoorsWinterOutfitDisabled}}").Text);
    }

    // ------------------------------------------------------------ Query（纯字面量算式）

    /// <summary>
    /// ★ `{{Query: 15 + 2}}` 这种**纯字面量算式**要代掉 —— CP 会算成数字写出去。
    /// <para>
    /// 实测踩过：某包的 `AddWarps` 写成
    /// <c>"26 35 Farm {{Query:58}} {{Query:15 + 2}} 27 35 …"</c>，我们没代 ⇒ 整条 `Warp`
    /// 带着花括号进产物 ⇒ 游戏
    /// `Failed parsing warp … got a non-numeric value for one of the X/Y positions`
    /// ⇒ **那一整串传送全废**（实机日志 10 行，位置 `Custom_ConvenientRoad`）。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Query的纯字面量算式求值()
    {
        DynamicTokenResolver r = await Load("");

        Assert.Equal("58", r.Expand("{{Query:58}}").Text);
        Assert.Equal("17", r.Expand("{{Query:15 + 2}}").Text);
        Assert.Equal("2", r.Expand("{{Query:(3 + 5) / 4}}").Text);      // 整数除法
        Assert.Equal("-3", r.Expand("{{Query:1 - 4}}").Text);
    }

    /// <summary>
    /// 内层令牌**先代、再算**：包里真实写法是
    /// `{{Query:{{Map_Coordinates_Y}} + 2}}`（配置默认 15 ⇒ 17），配 `AddWarps` 用。
    /// 只代内层会把 `{{Query:15 + 2}}` 留成残留 ⇒ 那串传送全废。
    /// </summary>
    [Fact]
    public async Task Query先代内层令牌再求值()
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"),
            """
            { "Format": "2.9.0",
              "ConfigSchema": { "Map_Coordinates_Y": { "Default": 15 } },
              "DynamicTokens": [
                { "Name": "Coordinates_Y", "Value": "{{Query:{{Map_Coordinates_Y}} + 2}}" } ],
              "Changes": [] }
            """);
        DynamicTokenResolver r = DynamicTokenResolver.Load(_pack, null);

        Assert.Equal("17", r.Expand("{{Coordinates_Y}}").Text);
    }

    /// <summary>
    /// 算式里一旦出现**别的东西**（令牌、字母、比较符、除零…）就**不代** —— 运行时语义，猜不得。
    /// 代不掉的原样留着，由审计报出来（同"代不掉就留着"的纪律）。
    /// </summary>
    [Fact]
    public async Task Query只认字面量不认令牌或字母()
    {
        DynamicTokenResolver r = await Load("");

        Assert.Equal("{{Query:{{Day}} + 1}}", r.Expand("{{Query:{{Day}} + 1}}").Text);
        Assert.Equal("{{Query:abc}}", r.Expand("{{Query:abc}}").Text);
        Assert.Equal("{{Query:5 > 3}}", r.Expand("{{Query:5 > 3}}").Text);
        Assert.Equal("{{Query:10 / 0}}", r.Expand("{{Query:10 / 0}}").Text);
    }

    // ------------------------------------------------------------ 令牌当条件

    /// <summary>
    /// ★ `When` 的**键是本包另一个 DynamicToken** 时也要能判（CP 认这种写法）。
    /// 实测语料里这一档有 **973 条补丁条件 + 99 块令牌条件 / 41 个包**（≈8% 的带条件补丁），
    /// 例如 `{"recolour": "Vanilla"}`（DaisyNiko —— `{{pan}}` 靠它才代得掉）、
    /// `{"IsSVEInstalled": false}`（Lurking in the Dark）。
    /// </summary>
    [Fact]
    public async Task 条件里引用另一个令牌时也能判()
    {
        var r = await Load("""
            { "Name": "recolour", "Value": "Vanilla" },
            { "Name": "pan", "Value": "desaturated", "When": { "recolour |contains=Vanilla": false } },
            { "Name": "pan", "Value": "vanilla", "When": { "recolour": "Vanilla" } }
            """);

        Assert.Equal("vanilla", r.Expand("{{pan}}").Text);
    }

    /// <summary>配置字段的 `|contains=A, B` 也是任一命中；值 `B` 应命中期望集合。</summary>
    [Fact]
    public async Task 配置字段条件的contains多值任一命中()
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"), """
            { "Format": "2.9.0",
              "ConfigSchema": { "Style": { "AllowValues": "A, B, C", "Default": "B" } },
              "DynamicTokens": [
                { "Name": "enabled", "Value": "yes", "When": { "Style |contains=A, B": true } },
                { "Name": "enabled", "Value": "no", "When": { "Style |contains=A, B": false } } ],
              "Changes": [] }
            """);
        var r = DynamicTokenResolver.Load(_pack, null);

        Assert.Equal("yes", r.Expand("{{enabled}}").Text);
    }

    /// <summary>
    /// 回归：DaisyNiko's Tilesheets 的 recolour 选择。
    /// <para>
    /// 配置默认 <c>ManualRecolourSelection=Off</c>，第一块是
    /// <c>{"ManualRecolourSelection": "Off, Vanilla"}</c>（集合任一命中），最后一块是
    /// <c>{"ManualRecolourSelection |contains=Off": "false"}</c>（字符串 false = 不命中）。
    /// 原来两处都判错（集合整体比、字符串 false 当"命中"），最后落到 <c>Off</c>，
    /// 于是 <c>assets/{{recolour}}/spring_daisyextras.png</c> 变成不存在的
    /// <c>assets/Off/…</c>，四个季节图集全没写，实机 FarmCave/Town/Greenhouse/Cellar
    /// 等地点全建不起来。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 配置字段集合相等与字符串false的contains()
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"), """
            { "Format": "2.9.0",
              "ConfigSchema": { "ManualRecolourSelection": {
                  "AllowValues": "Off, Vanilla, Earthy", "Default": "Off" } },
              "DynamicTokens": [
                { "Name": "recolour", "Value": "Vanilla", "When": { "ManualRecolourSelection": "Off, Vanilla" } },
                { "Name": "recolour", "Value": "Earthy", "When": { "ManualRecolourSelection": "Earthy" } },
                { "Name": "recolour", "Value": "{{ManualRecolourSelection}}",
                  "When": { "ManualRecolourSelection |contains=Off": "false" } } ],
              "Changes": [] }
            """);
        var r = DynamicTokenResolver.Load(_pack, null);

        Assert.Equal("Vanilla", r.Expand("{{recolour}}").Text);
    }

    /// <summary>字面量是布尔时按「令牌的布尔值等不等它」算（`{"IsSVEInstalled": false}`）。</summary>
    [Fact]
    public async Task 条件里的令牌按布尔比()
    {
        var r = await Load("""
            { "Name": "IsSVEInstalled", "Value": "False" },
            { "Name": "Route", "Value": "sve", "When": { "IsSVEInstalled": true } },
            { "Name": "Route", "Value": "vanilla", "When": { "IsSVEInstalled": false } }
            """);

        Assert.Equal("vanilla", r.Expand("{{Route}}").Text);
    }

    /// <summary>
    /// ⚠ **判不了时要返回"这一块不生效"，而且**不能**把令牌标成 lossy** —— 这是两版回退的病根：
    /// 老路径把这个键交给 `Baker`，它把不认识的键当作不成立（false）⇒ 那块被跳过、令牌**不标 lossy**；
    /// 要是标了 `Lossy`，`ConfigTokenSubstitutor`（"有损一律不代"）就会**整条不代**，
    /// 于是 FromFile 里留着 `{{SeasonalClothes}}` ⇒ 补丁被拒（实测 SCC 2 条农民立绘）。
    /// </summary>
    [Fact]
    public async Task 判不了的令牌条件不把令牌标成有损()
    {
        var r = await Load("""
            { "Name": "SeasonToLoad", "Value": "{{Season}}" },
            { "Name": "Shoes", "Value": "Plain" },
            { "Name": "Shoes", "Value": "Fancy", "When": { "SeasonToLoad": "Spring" } }
            """);

        DynamicTokenResolver.Expansion e = r.Expand("{{Shoes}}");
        Assert.Equal("Plain", e.Text);          // 落到默认块
        Assert.Empty(e.Lossy);                  // ★ 不许标有损
        Assert.Empty(e.Unresolved);
    }

    /// <summary>⚠ 闸一：值本身是"落到默认块"得来的（有损）时，也不拿它当条件。</summary>
    [Fact]
    public async Task 有损判出来的令牌不当条件用()
    {
        var r = await Load("""
            { "Name": "SeasonToLoad", "Value": "Spring" },
            { "Name": "SeasonToLoad", "Value": "Other", "When": { "HasMod": "Nope.X" } },
            { "Name": "Shoes", "Value": "Plain" },
            { "Name": "Shoes", "Value": "Fancy", "When": { "SeasonToLoad": "Spring" } }
            """);

        Assert.Equal("Plain", r.Expand("{{Shoes}}").Text);
    }

    /// <summary>⚠ 闸二：值里还剩没代掉的令牌时也不当条件。</summary>
    [Fact]
    public async Task 值里还有未代令牌时不当条件用()
    {
        var r = await Load("""
            { "Name": "SeasonToLoad", "Value": "{{Season}}" },
            { "Name": "Shoes", "Value": "Plain" },
            { "Name": "Shoes", "Value": "Fancy", "When": { "SeasonToLoad": "Spring" } }
            """);

        Assert.Equal("Plain", r.Expand("{{Shoes}}").Text);
    }

    // ------------------------------------------------------------ 基本展开

    [Fact]
    public async Task 展开无When的令牌()
    {
        var r = await Load("""{ "Name": "Style", "Value": "dry" }""");

        var e = r.Expand("crop-{{Style}}.png");

        Assert.Equal("crop-dry.png", e.Text);
        Assert.Empty(e.Unresolved);
    }

    /// <summary>
    /// ★ 本包之外、但**离线可定**的令牌（目前只有 <c>HasMod</c>）由外部取值器渲染。
    /// <para>
    /// 依据 CP：<c>HasMod</c> 注册成 <c>ImmutableValueProvider("HasMod", installedMods)</c>
    /// （<c>TokenManager.cs:220</c>），值是**已装模组的 id 集合**；
    /// 带 <c>|contains=X</c> 时 <c>Token.GetValues</c> 把值集折成单个 <c>true</c>/<c>false</c>（<c>Token.cs:154-…</c>）。
    /// 实测用途：SVE 在 <c>Data/WorldMap</c>/<c>Data/Characters</c> 的值里写
    /// <c>"Condition": "!{{HasMod |contains=flashshifter.GrandpasFarm}}"</c> —— 那是**游戏数据里的 GSQ**，
    /// 我们要把 <c>{{…}}</c> 代成字面 <c>true</c>/<c>false</c> 才能写出去。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 外部取值器渲染HasMod()
    {
        var r = await LoadWithExternal("""{ "Name": "S", "Value": "x" }""");

        var e1 = r.Expand("!{{HasMod |contains=Foo.Bar}}");
        Assert.Equal("!true", e1.Text);

        var e2 = r.Expand("{{HasMod |contains=Nope}}");
        Assert.Equal("false", e2.Text);

        // 没给外部取值器 ⇒ 原样保留（旧行为）
        var plain = await Load("""{ "Name": "S", "Value": "x" }""");
        Assert.Equal("{{HasMod |contains=Foo.Bar}}", plain.Expand("{{HasMod |contains=Foo.Bar}}").Text);
    }

    /// <summary>只认 <c>HasMod</c> 的外部取值器：装着 Foo.Bar。</summary>
    private async Task<DynamicTokenResolver> LoadWithExternal(string dynamicTokens)
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"),
            $$"""{ "Format": "2.9.0", "DynamicTokens": [ {{dynamicTokens}} ], "Changes": [] }""");
        return DynamicTokenResolver.Load(_pack, null, static (name, args) =>
        {
            if (!name.Equals("HasMod", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.IsNullOrWhiteSpace(args)) return "Foo.Bar, Baz.Qux";
            if (!args.StartsWith("|contains=", StringComparison.OrdinalIgnoreCase)) return null;
            string want = args["|contains=".Length..].Trim();
            return want.Equals("Foo.Bar", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        });
    }

    /// <summary>
    /// ★ 令牌**输入参数里**的内嵌令牌也要代 —— 那也是一棵令牌树（词法器产出 `LexTokenInput`）。
    /// <para>
    /// 旧实现「找第一个 <c>}}</c>」会把 <c>{{i18n:K |Season={{Season}}}}</c> 整段当成一个外部令牌放行，
    /// 于是 <c>i18n</c> 拿到的是**未展开的实参值**，代出来就是字面花括号。
    /// CP 的 `TranslationValueProvider` 恰恰是先把实参解析成值（`Stringify(p.Value)`）再交给 SMAPI 的。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 令牌输入参数里的内嵌令牌也展开()
    {
        var r = await Load("""{ "Name": "Season", "Value": "spring" }""");

        var e = r.Expand("{{i18n:Greeting |Season={{Season}}}}");

        Assert.Equal("{{i18n:Greeting |Season=spring}}", e.Text);   // 外层原样，内层代掉

        // 内层代不掉时**不打散外层**（原样保留，由自检报出来）
        var e2 = r.Expand("{{i18n:Greeting |PlayerName={{PlayerName}}}}");
        Assert.Equal("{{i18n:Greeting |PlayerName={{PlayerName}}}}", e2.Text);
    }

    /// <summary>
    /// 按**原文位置**替换，不重建整串 —— 花括号内的空白必须原样保留
    /// （重建会把 `{{i18n:K }}` 规范化成 `{{i18n:K}}`，而代换结果会写进产物）。
    /// </summary>
    [Fact]
    public async Task 展开时保留花括号内的空白()
    {
        var r = await Load("""{ "Name": "Season", "Value": "spring" }""");

        Assert.Equal("{{i18n:K |s=spring }}", r.Expand("{{i18n:K |s={{Season}} }}").Text);
    }

    [Fact]
    public async Task 未定义的令牌原样保留并报告()
    {
        var r = await Load("""{ "Name": "Style", "Value": "dry" }""");

        // `{{Spouse}}` 是玩家存档状态、代不了 —— 原样保留并报告
        var e = r.Expand("{{Style}}-{{Spouse}}");

        Assert.Equal("dry-{{Spouse}}", e.Text);
        Assert.Contains("Spouse", e.Unresolved);

        // 而 `{{Random}}` 是**确定化**（取第一个备选），不是「代不了」—— 记进 Determinized，不记 Unresolved
        var e2 = r.Expand("{{Style}}-{{Random: 1, 2}}");
        Assert.Equal("dry-1", e2.Text);
        Assert.Contains("Random", e2.Determinized);
        Assert.DoesNotContain("Random", e2.Unresolved);
    }

    [Fact]
    public async Task 令牌名大小写不敏感()
    {
        var r = await Load("""{ "Name": "Style", "Value": "dry" }""");

        Assert.Equal("dry", r.Expand("{{STYLE}}").Text);
    }

    // ------------------------------------------------------------ 嵌套与防环

    [Fact]
    public async Task 嵌套展开()
    {
        var r = await Load("""
            { "Name": "A", "Value": "x" },
            { "Name": "B", "Value": "{{A}}y" }
            """);

        Assert.Equal("xy", r.Expand("{{B}}").Text);
    }

    [Fact]
    public async Task 互相引用成环时原样保留不挂()
    {
        var r = await Load("""
            { "Name": "A", "Value": "{{B}}" },
            { "Name": "B", "Value": "{{A}}" }
            """);

        var e = r.Expand("{{A}}");

        Assert.Contains("{{", e.Text);
        Assert.Contains(e.Unresolved, u => u.Contains("成环"));
    }

    [Fact]
    public async Task 首块自引用仍原样保留()
    {
        var r = await Load("""{ "Name": "A", "Value": "x{{A}}" }""");

        var e = r.Expand("{{A}}");

        Assert.Contains("{{", e.Text);
    }

    /// <summary>
    /// CP 的自引用是**按块追加**，不是环：基础块 + 若干 `When` 块 `{{Self}} …`。
    /// `AddDynamicToken` 用 `GetTokensUsed().GetWithout(name)` 排除自身；实测 Nora 的
    /// `NoraParty_SetUp`、Juliet 的 `Juliet_SetUp`，后者引用前者被误判成环，事件整条丢失。
    /// </summary>
    [Fact]
    public async Task 自引用按前面块的值追加()
    {
        var r = await Load("""
            { "Name": "A", "Value": "base" },
            { "Name": "A", "Value": "{{A}} one", "When": { "HasMod": "Mod.One" } },
            { "Name": "A", "Value": "{{A}} two", "When": { "HasMod": "Mod.Two" } }
            """, HasMod("Mod.One", "Mod.Two"));

        var e = r.Expand("{{A}}");

        Assert.Equal("base one two", e.Text);
        Assert.Empty(e.Unresolved);
    }

    [Fact]
    public async Task 值里的CP内建令牌原样保留()
    {
        // 真实用例：RandomNumber 的值就是 {{Random: ...}} —— **确定化**成第一个备选（不再随机）
        var r = await Load("""{ "Name": "RandomNumber", "Value": "{{Random: 1, 2, 3}}" }""");

        var e = r.Expand("{{RandomNumber}}");

        Assert.Equal("1", e.Text);
        Assert.Contains("Random", e.Determinized);
    }

    /// <summary>
    /// CP 内建 `Lowercase` / `Uppercase`（`LetterCaseValueProvider.cs:41-42`）。
    /// 实测 Bear Family 的婚后对话把 `{{Lowercase:…}}` 塞进 i18n 具名参数；不代 ⇒ 89 条全被拒。
    /// </summary>
    [Fact]
    public async Task Lowercase与Uppercase按CP语义展开()
    {
        var r = await Load("""{ "Name": "Unused", "Value": "x" }""");

        Assert.Equal("abc", r.Expand("{{Lowercase: ABC}}").Text);
        Assert.Equal("ABC", r.Expand("{{Uppercase: abc}}").Text);
        // 内层令牌先展开
        Assert.Equal("hi", r.Expand("{{Lowercase: {{Random: HI, HO}}}}").Text);
    }

    /// <summary>
    /// **令牌名可以含空格**（CP 的 ConfigSchema 键就是令牌名）。实测 S&amp;S 的
    /// <c>"Player Pronouns 1"</c> + <c>{{Lowercase: {{Player Pronouns 1}}}}</c>；
    /// 词法器把整段 literal 当名字（只按 <c>|</c>/<c>:</c> 切参数），这条用来锁住那个行为。
    /// </summary>
    [Fact]
    public async Task 名字含空格的配置令牌可以展开()
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"), """
            { "Format": "2.9.0",
              "ConfigSchema": { "Player Pronouns 1": { "Default": "They", "AllowBlank": false } },
              "DynamicTokens": [
                { "Name": "LPPN1", "Value": "{{Lowercase: {{Player Pronouns 1}}}}" } ],
              "Changes": [] }
            """);
        var r = DynamicTokenResolver.Load(_pack, null);

        Assert.Equal("they", r.Expand("{{LPPN1}}").Text);
    }

    /// <summary>
    /// 多值配置令牌在条件里按**集合**匹配：`AllowMultiple` 值 `A,B` 命中期望 `A`，
    /// 不能拼成 `"A, B"` 后做字符串比较。
    /// </summary>
    [Fact]
    public async Task 多值配置令牌按集合匹配()
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"), """
            { "Format": "2.9.0",
              "ConfigSchema": { "Style": { "AllowValues": "a,b,c", "Default": "a,b", "AllowMultiple": true } },
              "DynamicTokens": [
                { "Name": "T", "Value": "no",  "When": { "Style": "c" } },
                { "Name": "T", "Value": "yes", "When": { "Style": "a" } } ],
              "Changes": [] }
            """);
        var r = DynamicTokenResolver.Load(_pack, null);

        Assert.Equal("yes", r.Expand("{{T}}").Text);
    }

    /// <summary>
    /// 首块回退要跳过**静态为假**的块：第一块 `HasMod` 未装、第二块运行时未知 ⇒ 取第二块。
    /// </summary>
    [Fact]
    public async Task 首块回退跳过静态为假的块()
    {
        var r = await Load("""
            { "Name": "D", "Value": "bad",  "When": { "HasMod": "Not.Installed" } },
            { "Name": "D", "Value": "good", "When": { "HasSeenEvent": "1" } }
            """, HasMod());

        var e = r.Expand("{{D}}");

        Assert.Equal("good", e.Text);
        Assert.Contains("D", e.Lossy);
    }

    /// <summary>令牌 A 的 When 问 B、B 的 When 问 A ⇒ 必须判不了，不能无限递归（StackOverflow）。</summary>
    [Fact]
    public async Task 条件互相引用不会爆栈()
    {
        var r = await Load("""
            { "Name": "A", "Value": "x", "When": { "B": "y" } },
            { "Name": "B", "Value": "y", "When": { "A": "x" } }
            """);

        var e = r.Expand("{{A}}");

        Assert.Contains("A", e.Unresolved);
    }

    /// <summary>
    /// **所有块都因运行时条件判不了**时取第一块（包作者写的默认状态），标有损。
    /// <para>
    /// 实测 Rodney/Nora 的 `*Datability`：两块分别是「没在约会→false」「在约会→true」，
    /// 没有无条件块。原实现判不了就返回 null ⇒ 整个 NPC 条目不写。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 全部块判不了时取第一块并标为有损()
    {
        var r = await Load("""
            { "Name": "Datability", "Value": false,
              "When": { "Relationship:NPC |contains=Dating": false } },
            { "Name": "Datability", "Value": true,
              "When": { "Relationship:NPC |contains=Dating": true } }
            """, HasMod());   // 求值器只认 HasMod → Relationship 判不了

        var e = r.Expand("{{Datability}}");

        Assert.Equal("false", e.Text);
        Assert.Contains("Datability", e.Lossy);
        Assert.Empty(e.Unresolved);
    }

    /// <summary>
    /// **DynamicToken 的 `When` 可以直接引用 ConfigSchema 字段**（CP 允许）。
    /// <para>
    /// 实测 Rodney O'Brien：`Meredith Portrait Style Path` 只有一块 `When: {"Meredith Portrait Style":"Nyapu"}`；
    /// 以前把配置字段交给外部求值器（它不认识）⇒ 判不了 ⇒ 令牌无值 ⇒ **整条 NPC 不写**。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 动态令牌的When可以判配置字段()
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"), """
            { "Format": "2.9.0",
              "ConfigSchema": { "Portrait Style": { "AllowValues": "vanilla, Nyapu", "Default": "Nyapu" } },
              "DynamicTokens": [
                { "Name": "Style Path", "Value": "Vanilla", "When": { "Portrait Style": "vanilla" } },
                { "Name": "Style Path", "Value": "Nyapu",   "When": { "Portrait Style": "Nyapu" } } ],
              "Changes": [] }
            """);
        var r = DynamicTokenResolver.Load(_pack, null);

        Assert.Equal("Nyapu", r.Expand("{{Style Path}}").Text);
    }

    /// <summary>
    /// `{{HasFlag …|contains=…}}` / `{{HasSeenEvent …|contains=…}}` 用在 i18n 键里：存档状态离线不可知，
    /// 两种键（`.true`/`.false`）都在 ⇒ 冻结为 `false` 并记进 `Determinized`。
    /// </summary>
    [Fact]
    public async Task 存档条件令牌冻结为false并记录()
    {
        var r = await Load("""{ "Name": "Unused", "Value": "x" }""");

        var e = r.Expand("{{i18n:HasMetHector.{{HasSeenEvent |contains=SnS.Ch2.Hector.1}}}}");
        Assert.Equal("{{i18n:HasMetHector.false}}", e.Text);
        Assert.Contains(e.Determinized, n => n.StartsWith("HasSeenEvent"));

        Assert.Equal("false", r.Expand("{{HasFlag: anyPlayer|contains=X}}").Text);
        // 没有 `|contains=` 的多值形态不猜
        Assert.Contains("HasFlag", r.Expand("{{HasFlag: anyPlayer}}").Unresolved);
    }

    /// <summary>
    /// <c>{{Random:{{Range:0,5}}}}</c>：CP 把**展平后的位置参数**交给 Random ——
    /// <c>Range</c> 贡献 6 个备选，不是「一个字符串」。所以确定化取的是 <c>0</c>。
    /// </summary>
    [Fact]
    public async Task Random嵌Range时按展平后的备选取值()
    {
        var r = await Load("""{ "Name": "Unused", "Value": "x" }""");

        Assert.Equal("0", r.Expand("{{Random:{{Range:0,5}}}}").Text);
        Assert.Equal("1", r.Expand("{{Random: 1,2.{{Range:3,5}}}}").Text);   // 第一段是字面量 1
    }

    /// <summary>
    /// <c>|inputSeparator=</c>：CP 用它声明**备选之间的分隔符**（默认才是逗号）。
    /// <para>
    /// 实机来源（2026-10-14，`NPC 'RaccoonSBV' failed to parse master schedule 'Mon'`）：
    /// 包里写的是 <c>{{Random: 0 Forest …++0 Forest …|inputSeparator=++}}</c> ——
    /// 我们原来**只按逗号切**、又不认这个具名参数，于是「整串（含分隔符与
    /// <c>|inputSeparator=++</c> 本身）」被当成**一个备选**写进产出 ⇒ 游戏拿到解析不了的日程；
    /// 而残渣里已经没有 <c>{{</c> ⇒ <c>unexpandedTokens</c> 自检也看不见。
    /// 修后：按声明的分隔符切、取第一个备选（与 Random 的确定化同策）。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Random带inputSeparator时按声明的分隔符切()
    {
        var r = await Load("""{ "Name": "Unused", "Value": "x" }""");

        Assert.Equal("A", r.Expand("{{Random: A ++ B |inputSeparator=++}}").Text);
        Assert.Equal("A", r.Expand("{{Random: A && B |inputSeparator=&&}}").Text);
        // 默认仍是逗号，行为不变
        Assert.Equal("A", r.Expand("{{Random: A, B}}").Text);
        // 多个参数：分隔符照做、`key` 忽略（实测来源 `assets/animals/{{Random: Fox_Red, Fox_Red,
        // FoxArctic_{{season}}|key=foxes}}.png` —— 把它当"认不出"会整条 Load 被挡、资产不写）
        Assert.Equal("A", r.Expand("{{Random: A && B |inputSeparator=&& |key=foxes}}").Text);
        Assert.Equal("Fox_Red", r.Expand("{{Random: Fox_Red, Fox_Red, FoxArctic |key=foxes}}").Text);
    }

    /// <summary>
    /// 实机来源（2026-10-14，`[CP] Custom NPC Lani` 崩溃）：
    /// 令牌的**值里**嵌一个带 `|key=` 的 Random —— `Lani.Dialogue.{{Random:0,1,2|key=SyncUpRandoms}}`。
    /// </summary>
    [Fact]
    public async Task 令牌值里嵌带key的Random()
    {
        var r = await Load("""{ "Name": "K", "Value": "Lani.Dialogue.{{Random:0,1,2|key=SyncUpRandoms}}" }""");

        var e = r.Expand("{{K}}");

        Assert.Equal("Lani.Dialogue.0", e.Text);
    }

    /// <summary>再套一层：另一个令牌引用上面那个。</summary>
    [Fact]
    public async Task 两层令牌里嵌带key的Random()
    {
        var r = await Load("""
            { "Name": "D", "Value": "x.{{Random:0,1,2|key=SyncUpRandoms}}" },
            { "Name": "K", "Value": "{{D}}.y" }
            """);

        Assert.Equal("x.0.y", r.Expand("{{K}}").Text);
    }

    /// <summary>
    /// 实机来源（2026-10-14，`[CP] Custom NPC Lani` **致命错误**、71 个资产一个没写出来）：
    /// Random 体内**嵌一个带参数的令牌** —— 剥参数时若不看花括号深度，会从内层令牌中间截断
    /// （切成 `{{N`）⇒ 词法器 `Reached end of input, expected '}}'`。
    /// </summary>
    [Fact]
    public async Task Random体内嵌带参数的令牌不从中间截断()
    {
        var r = await Load("""{ "Name": "N", "Value": "n1" }""");

        Assert.Equal("n1", r.Expand("{{Random: {{N|key=x}}, B}}").Text);
    }

    /// <summary>
    /// 认不出的**具名参数** ⇒ 不代（原样保留、由那道闸拒绝）—— 「宁可少写，不可写错」。
    /// </summary>
    [Fact]
    public async Task Random带认不出的具名参数时不代()
    {
        var r = await Load("""{ "Name": "Unused", "Value": "x" }""");

        var e = r.Expand("{{Random: A, B |bogus=1}}");

        Assert.Contains("Random", e.Unresolved);
        Assert.Contains("{{Random", e.Text);
    }

    // ------------------------------------------------------------ 同名多块

    [Fact]
    public async Task 同名多块最后一个匹配的胜出()
    {
        var r = await Load("""
            { "Name": "Style", "Value": "dry" },
            { "Name": "Style", "Value": "wet" }
            """);

        Assert.Equal("wet", r.Expand("{{Style}}").Text);
    }

    [Fact]
    public async Task 带When的块条件为真时覆盖()
    {
        var r = await Load(
            """
            { "Name": "Tone", "Value": "plain" },
            { "Name": "Tone", "Value": "fancy", "When": { "HasMod": "Hana.GNMTokens" } }
            """,
            HasMod("Hana.GNMTokens"));

        Assert.Equal("fancy", r.Expand("{{Tone}}").Text);
    }

    [Fact]
    public async Task 带When的块条件为假时不生效()
    {
        var r = await Load(
            """
            { "Name": "Tone", "Value": "plain" },
            { "Name": "Tone", "Value": "fancy", "When": { "HasMod": "Hana.GNMTokens" } }
            """,
            HasMod());   // 什么都没装

        Assert.Equal("plain", r.Expand("{{Tone}}").Text);
    }

    /// <summary>
    /// ★ **选中块的值依赖离线不可知的令牌时退回默认块**。
    /// <para>
    /// 实测 `[CP] GiEX redux`：<c>PortraitStyle</c> 先有无条件默认 <c>Vanilla</c>，
    /// 后面一块在 <c>HasMod: mistyspring.GiEXredux.code</c>（已装的 DLL 模组）成立时
    /// 改成 <c>{{mistyspring.GiEXredux.code/PortraitStyle}}</c>。DLL 模组的令牌我们代不了；
    /// 原先把含字面 `{{…}}` 的值返回 ⇒ 判定看到残留令牌 ⇒ **整条 Load 被拒**：
    /// Javi 的 3 张立绘、年轻时 4 张立绘全都不写。退回包自己写的默认块后资产至少存在。
    /// </para>
    /// <para>代价必须记进 <c>Lossy</c>：玩家的实际配置可能不是默认值。</para>
    /// </summary>
    [Fact]
    public async Task 选中块的值代不掉时退回默认块并标为有损()
    {
        var r = await Load(
            """
            { "Name": "PortraitStyle", "Value": "Vanilla" },
            { "Name": "PortraitStyle", "Value": "{{mistyspring.GiEXredux.code/PortraitStyle}}",
              "When": { "HasMod": "mistyspring.GiEXredux.code" } }
            """,
            HasMod("mistyspring.GiEXredux.code"));

        var e = r.Expand("assets/NPC/Portrait_{{PortraitStyle}}/Portrait.png");

        Assert.Equal("assets/NPC/Portrait_Vanilla/Portrait.png", e.Text);
        Assert.Contains("PortraitStyle", e.Lossy);
        Assert.Empty(e.Unresolved);
    }

    [Fact]
    public async Task 真实用例GenderNeutralRSV未装GNM时展开成空串()
    {
        // Ridgeside 的真实定义：一个无条件块给 ""，一个 HasMod 块给 ".GN"
        var r = await Load(
            """
            { "Name": "GenderNeutralRSV", "Value": "" },
            { "Name": "GenderNeutralRSV", "Value": ".GN", "When": { "HasMod": "Hana.GNMTokens" } }
            """,
            HasMod());

        var e = r.Expand("checkup.{{TargetWithoutPath}}{{GenderNeutralRSV}}");

        // GenderNeutralRSV 展开成空串；TargetWithoutPath 不是 DynamicToken → 原样保留
        Assert.Equal("checkup.{{TargetWithoutPath}}", e.Text);
        Assert.Contains("TargetWithoutPath", e.Unresolved);
    }

    // ------------------------------------------------------------ When 判不了

    /// <summary>
    /// **有损确定化**：判不了的条件视为「不匹配」，继续往前找，从而落到**无条件的默认块**。
    /// <para>
    /// 语料的形态就是「无条件默认块在**最前**，条件块在后」——
    /// 一撞上判不了的 `When` 就放弃，会永远落不到那个默认块。
    /// </para>
    /// <para>
    /// <b>代价必须记在 <c>Lossy</c> 里</b>：运行时若条件成立，真值 ≠ 我们选的默认值。
    /// </para>
    /// </summary>
    [Fact]
    public async Task When判不了时落到无条件默认块并标为有损()
    {
        var r = await Load(
            """
            { "Name": "State", "Value": "firstYear" },
            { "Name": "State", "Value": "naomiArrived", "When": { "HasSeenEvent": "75160249" } }
            """,
            HasMod());   // 测试求值器只认 HasMod → HasSeenEvent 判不了

        var e = r.Expand("{{State}}");

        Assert.Equal("firstYear", e.Text);
        Assert.Contains("State", e.Lossy);        // 有损，必须报出来
        Assert.DoesNotContain("State", e.Unresolved);
    }

    [Fact]
    public async Task 没有求值器时也落到无条件默认块并标为有损()
    {
        var r = await Load("""
            { "Name": "Tone", "Value": "plain" },
            { "Name": "Tone", "Value": "fancy", "When": { "HasMod": "X" } }
            """);

        var e = r.Expand("{{Tone}}");

        Assert.Equal("plain", e.Text);
        Assert.Contains("Tone", e.Lossy);
    }

    /// <summary>
    /// **不算有损**的情形：无条件块在判不了的块**之后**（倒序先撞上它）——
    /// 那时「最后一个匹配的块」是确定的，不是猜的。
    /// </summary>
    [Fact]
    public async Task 无条件块在判不了的块之后时不算有损()
    {
        var r = await Load(
            """
            { "Name": "U", "Value": "a", "When": { "HasSeenEvent": "1" } },
            { "Name": "U", "Value": "b" }
            """,
            HasMod());

        var e = r.Expand("{{U}}");

        Assert.Equal("b", e.Text);
        Assert.Empty(e.Lossy);
    }

    [Fact]
    public async Task 覆盖块在后面判不了但后面还有无条件块时以前者为准()
    {
        // 判不了的块之后还有一个无条件块 → 最后匹配的一定是它 → 能定
        var r = await Load(
            """
            { "Name": "T", "Value": "a", "When": { "HasSeenEvent": "1" } },
            { "Name": "T", "Value": "b" }
            """,
            HasMod());

        Assert.Equal("b", r.Expand("{{T}}").Text);
    }

    [Fact]
    public async Task 所有块条件都为假时令牌无值()
    {
        var r = await Load(
            """
            { "Name": "T", "Value": "x", "When": { "HasMod": "A" } }
            """,
            HasMod());

        var e = r.Expand("{{T}}");

        Assert.Contains("{{T}}", e.Text);
        Assert.Contains("T", e.Unresolved);
    }

    // ------------------------------------------------------------ 边界

    [Fact]
    public async Task 未闭合的令牌原样保留()
    {
        var r = await Load("""{ "Name": "A", "Value": "x" }""");

        Assert.Equal("{{A", r.Expand("{{A").Text);
    }

    [Fact]
    public async Task 文本里没有令牌时原样返回()
    {
        var r = await Load("""{ "Name": "A", "Value": "x" }""");

        var e = r.Expand("plain-text");

        Assert.Equal("plain-text", e.Text);
        Assert.Empty(e.Unresolved);
    }

    [Fact]
    public async Task 令牌带参数时按名字匹配()
    {
        // CP 令牌可带 :参数 或 |参数；名字段照常匹配
        var r = await Load("""{ "Name": "A", "Value": "x" }""");

        Assert.Equal("x", r.Expand("{{A: 1, 2}}").Text);
    }

    /// <summary>
    /// ★ 动态令牌的**值**里写 <c>{{Season}}</c> 时也要按确定化政策代成 `spring`。
    /// 实测 `[CP] Seasonal Cute Characters` 的 `CurrentSeasonAttire = {{Season}}`：
    /// 不代 ⇒ `FromFile: …/emojis_{{CurrentSeasonAttire}}.png` 整条 TargetMissing（emoji/日历图缺失）。
    /// </summary>
    [Fact]
    public async Task 动态令牌值里的Season冻结到春天()
    {
        DynamicTokenResolver r = await Load("""
            { "Name": "CurrentSeasonAttire", "Value": "Spring" },
            { "Name": "CurrentSeasonAttire", "Value": "{{Season}}" }
            """);

        DynamicTokenResolver.Expansion e = r.Expand("{{CurrentSeasonAttire}}");

        Assert.Equal("spring", e.Text);
        Assert.Empty(e.Unresolved);
        Assert.Contains("Season", e.Determinized);
    }

    [Fact]
    public async Task 内容包没有DynamicTokens时为空()
    {
        await File.WriteAllTextAsync(Path.Combine(_pack, "content.json"), """{ "Changes": [] }""");

        var r = DynamicTokenResolver.Load(_pack, null);

        Assert.True(r.IsEmpty);
    }

    [Fact]
    public void 内容包没有content_json时不抛异常()
    {
        var r = DynamicTokenResolver.Load(Path.Combine(_pack, "nonexistent"), null);

        Assert.True(r.IsEmpty);
    }
}
