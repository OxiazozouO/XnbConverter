using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class TranslationApplierTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvtrp_" + Guid.NewGuid().ToString("N"));
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvtrs_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvtro_" + Guid.NewGuid().ToString("N"));

    public TranslationApplierTests()
    {
        Directory.CreateDirectory(Path.Combine(_pack, "i18n"));
        Directory.CreateDirectory(_src);
    }

    private OutputTree? _tree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush()
    {
        _tree?.Flush();
    }

    public void Dispose()
    {
        Directory.Delete(_pack, recursive: true);
        Directory.Delete(_src, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void I18n(string file, string json) =>
        File.WriteAllText(Path.Combine(_pack, "i18n", file), json);

    private PackTranslations Translations() => PackTranslations.Load(_pack);

    /// <summary>去掉行尾的 CR —— 换行风格不该影响「内容是否相同」的判定。</summary>
    private static string NoCr(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
            if (c != '\r') sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// 把一段数据按某个语言翻出来 —— 走**生产那条唯一漏斗**
    /// （<see cref="TranslationApplier.WriteLocalized"/>），再从产出树把文件读回来。
    /// <para>
    /// ⚠ 以前这里直接调 <c>TranslationApplier.Apply</c>：那是「返回整份新文档」的另一份实现，
    /// 与 <c>WriteLocalized</c> 做的是同一件事 —— 已撤掉，省得两份遍历逻辑各自跑偏。
    /// </para>
    /// </summary>
    private TranslationOutcome Apply(JToken data, PackTranslations translations, string locale,
        DynamicTokenResolver? tokens = null, string? tokenTarget = null)
    {
        // 每次一个独立的小产出树：同一个类里的测试互不串内容
        string dir = Path.Combine(_out, Guid.NewGuid().ToString("N"));
        var tree = _tree = new OutputTree(_src, dir);
        string stem = tokenTarget ?? "Data/X";
        MergeOutcome written = TranslationApplier.WriteLocalized(tree, stem, data, translations, isNew: false, tokens);
        if (!written.Success) return TranslationOutcome.Fail(new[] { written.FailureReason! });
        tree.Flush();

        string rel = locale == PackTranslations.DefaultLocale ? stem + ".json" : stem + "." + locale + ".json";
        string text = File.ReadAllText(Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar)));
        Directory.Delete(dir, recursive: true);
        return TranslationOutcome.Ok(JToken.Parse(text));
    }

    private void Touch(string rel, string content)
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // ------------------------------------------------------------ 令牌替换

    [Fact]
    public void 替换简单令牌()
    {
        I18n("default.json", """{ "EoR.01": "Happy New Year!" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:EoR.01}}" }"""), Translations(), "default");

        Assert.True(outcome.Success);
        Assert.Equal("Happy New Year!", outcome.Data!["x"]!.Value<string>());
    }

    /// <summary>
    /// ★ **键名里的 `{{i18n:…}}` 也要按语言替换**（CP 把 JSON 键也当令牌串）。
    /// <para>
    /// 实测 `[CP] 鸟类观察协会` 的 `Data/CraftingRecipes` 把配方名写在**键**上；
    /// 只翻译值 ⇒ 产出里是字面 <c>{{i18n:…}}</c> 的配方键（制作菜单直接显示令牌）。
    /// 同一条目的**值**也含 i18n，两者必须一起按同一语言替换（键名改动的实现要保住
    /// value 节点的对象同一性，否则值会翻译进孤儿节点）。
    /// </para>
    /// </summary>
    [Fact]
    public void 键名里的i18n也按语言替换且值同步()
    {
        I18n("default.json", """{ "name": "Bird Bath", "desc": "A bath." }""");
        I18n("zh.json", """{ "name": "鸟浴盆", "desc": "一个浴盆。" }""");

        var data = JObject.Parse("""{ "{{i18n:name}}": "{{i18n:desc}}" }""");

        var baseOut = Apply(data, Translations(), "default");
        Assert.True(baseOut.Success);
        Assert.Equal("A bath.", baseOut.Data!["Bird Bath"]!.Value<string>());

        var zh = Apply(data, Translations(), "zh-CN");
        Assert.True(zh.Success);
        Assert.Equal("一个浴盆。", zh.Data!["鸟浴盆"]!.Value<string>());
    }

    [Fact]
    public void 只替换i18n不动其他令牌()
    {
        I18n("default.json", """{ "k": "v" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "a {{i18n:k}} b {{Random: 1, 2}} c {{Target}}" }"""),
            Translations(), "default");

        Assert.True(outcome.Success);
        Assert.Equal("a v b {{Random: 1, 2}} c {{Target}}", outcome.Data!["x"]!.Value<string>());
    }

    [Fact]
    public void 递归替换数组与嵌套对象()
    {
        I18n("default.json", """{ "k": "v" }""");

        var outcome = Apply(
            JObject.Parse("""{ "a": [ "{{i18n:k}}", { "b": "{{i18n:k}}x" } ] }"""),
            Translations(), "default");

        Assert.True(outcome.Success);
        Assert.Equal("v", outcome.Data!["a"]![0]!.Value<string>());
        Assert.Equal("vx", outcome.Data!["a"]![1]!["b"]!.Value<string>());
    }

    /// <summary>
    /// **缺键不等于失败** —— CP 的 `TranslationValueProvider.GetValues` 是优雅降级：
    /// 先试 `|defaultKeys=`、再试 `|default=`，都没有才给占位符（SMAPI 的
    /// `(no translation:key)`）。CP **不会**因为缺键让整条补丁失效。
    /// <para>
    /// 实测代价：先前判整条补丁失败，SVE 一个 `{{i18n:Garden}}` 缺键就让**装着 57 个地点**
    /// 的那条补丁整个没写；而游戏里 CP 照常显示占位符。
    /// </para>
    /// </summary>
    [Fact]
    public void 缺键降级为占位符而不是失败()
    {
        I18n("default.json", "{}");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:没有这个键}}" }"""), Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("(no translation:没有这个键)", outcome.Data!["x"]!.Value<string>());
    }

    /// <summary>CP 的 `|default=` 优先于占位符。</summary>
    [Fact]
    public void default参数优先于占位符()
    {
        I18n("default.json", "{}");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:缺 |default=兜底}}" }"""), Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("兜底", outcome.Data!["x"]!.Value<string>());
    }

    /// <summary>CP 的 `|defaultKeys=` 按顺序试，命中就用。</summary>
    [Fact]
    public void defaultKeys按顺序试()
    {
        I18n("default.json", """{ "第二个": "命中" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:缺 |defaultKeys=没有, 第二个}}" }"""), Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("命中", outcome.Data!["x"]!.Value<string>());
    }

    /// <summary>
    /// 非 `defaultKeys`/`default` 的具名参数是 **SMAPI 的 `{{name}}` 占位符**
    /// （CP 的 `AllowAnyNamedArguments = true`）—— 替换掉，**不是**判失败。
    /// </summary>
    [Fact]
    public void 具名参数作为占位符替换()
    {
        I18n("default.json", """{ "k": "你好，{{name}}！共 {{count}} 个" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:k |name=张三|count=3}}" }"""), Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("你好，张三！共 3 个", outcome.Data!["x"]!.Value<string>());
    }

    /// <summary>
    /// 参数值本身是 `{{i18n:…}}` 时要先翻掉，再代入译文。
    /// 实测 Bear Family：令牌展开后参数是 `|LewisOrLouise={{i18n:LewisOrLouise.false}}`，
    /// 不翻就会把字面 `{{i18n:…}}` 写进台词（最终变成 `(no translation:…)`）。
    /// </summary>
    [Fact]
    public void 参数值里的i18n先翻译再代入()
    {
        I18n("default.json", """{ "k": "%{{who}} looks worried.", "who.false": "Lewis" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:k |who={{i18n:who.false}}}}" }"""), Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("%Lewis looks worried.", outcome.Data!["x"]!.Value<string>());
    }

    /// <summary>
    /// 译文自身含 `{{i18n:…}}` 时继续展开（CP 的 TranslationValueProvider 返回的还是 token string）。
    /// 实测 Bear Family 的 `LewisCTGunnartheLost` 等条目。
    /// </summary>
    [Fact]
    public void 译文里的嵌套i18n继续展开()
    {
        I18n("default.json", """{ "k": "你好，{{i18n:name}}。", "name": "刘易斯" }""");

        var outcome = Apply(JObject.Parse("""{ "x": "{{i18n:k}}" }"""), Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("你好，刘易斯。", outcome.Data!["x"]!.Value<string>());
    }

    /// <summary>大小写不敏感，且**认不出的占位符原样保留**（SMAPI 语义，不是清空）。</summary>
    [Fact]
    public void 占位符大小写不敏感且认不出的原样保留()
    {
        I18n("default.json", """{ "k": "{{Name}} 与 {{unknown}}" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:k |name=李四}}" }"""), Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("李四 与 {{unknown}}", outcome.Data!["x"]!.Value<string>());
    }

    /// <summary>
    /// ★ 参数切分按**词法器**做（`SplitLexically` 切不动令牌内部）。
    /// <para>
    /// 裸 <c>Split('|')</c> 会把 <c>{{HasSeenEvent|contains=1234}}</c> 切成两段，
    /// 第二段名是 <c>contains</c> —— 一个**假占位符**，随后被判「值里含未展开令牌」而整条失败。
    /// 实测 SVE 的 Claire 婚姻对话 4 处都栽在这里。
    /// </para>
    /// </summary>
    [Fact]
    public void 令牌内部的竖线不算参数分隔符()
    {
        I18n("default.json", """{ "k": "有 {{a}} 条" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:k |a={{HasSeenEvent|contains=1234}}}}" }"""),
            Translations(), "default");

        // 参数名只有 `a`（值里那个 `|contains=` 属于内层令牌，不是 i18n 的参数）
        Assert.False(outcome.Success);                     // 内层令牌代不掉 ⇒ 按规矩不写
        Assert.Contains("具名参数的值里含无法静态解析的令牌", outcome.FailureReason);
        Assert.Contains("`a`", outcome.FailureReason);
        Assert.DoesNotContain("contains", outcome.FailureReason);   // ★ 不再冒出假占位符
    }

    /// <summary>多层 <c>|default={{i18n:…}}</c> 嵌套时，参数仍只有 <c>default</c> 这一个。</summary>
    [Fact]
    public void 多层嵌套default不被切碎()
    {
        I18n("default.json", """{ "A": "外层" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:A |default={{i18n:B |default={{i18n:C}}}}}}" }"""),
            Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);   // 键 A 查得到，用译文
        Assert.Equal("外层", outcome.Data!["x"]!.Value<string>());
    }

    [Fact]
    public void 内嵌令牌判失败()
    {
        I18n("default.json", """{ "k": "v" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:k{{GenderNeutralRSV}}}}" }"""), Translations(), "default");

        Assert.False(outcome.Success);
        Assert.Contains("内嵌令牌", outcome.FailureReason);
    }

    /// <summary>
    /// ★ <b>具名参数的「值」里</b>带代不掉的令牌 ⇒ 整条不写。
    /// <para>
    /// 依据：CP 的 <c>TranslationValueProvider</c> 把实参先经**令牌解析**
    /// （<c>Stringify(p.Value)</c> = 解析后的值），再交给 SMAPI 代入译文里的占位符。
    /// 我们代不掉 <c>{{JasAuncleToken}}</c> ⇒ 代入的就是**字面**令牌。
    /// 实测（谢恩 / SVE，语料 13 处）产出 <c>Data/Events/Farm.json</c> 里是
    /// <c>早上好，@{{JasAuncleToken}} ！</c>，游戏不认识 <c>{{…}}</c> ⇒ 玩家看到字面花括号。
    /// </para>
    /// <para>
    /// ⚠ <b>只查参数值，不查代入后的整串</b>：译文里**没有对应实参**的 <c>{{unknown}}</c>，
    /// CP 也是原样保留（SMAPI <c>Translation.FormatText</c> 认不出就留着）—— 那一条照抄才对。
    /// 见 <see cref="占位符大小写不敏感且认不出的原样保留"/>。
    /// </para>
    /// </summary>
    [Fact]
    public void 具名参数的值里带未展开令牌则失败()
    {
        I18n("default.json", """{ "event0": "早上好，@{{JasAuncleToken}} ！" }""");

        var outcome = Apply(
            JObject.Parse(
                """{ "x": "speak Jas \"{{i18n:event0 |JasAuncleToken={{JasAuncleToken}}}}\"" }"""),
            Translations(), "default");

        Assert.False(outcome.Success);
        Assert.Contains("具名参数的值里含无法静态解析的令牌", outcome.FailureReason);
        Assert.Contains("JasAuncleToken", outcome.FailureReason);

        // 参数值能被精确代掉时照常通过（`default=` 那条路也不受影响）
        var ok = Apply(
            JObject.Parse("""{ "x": "{{i18n:event0 |JasAuncleToken=叔叔}}" }"""), Translations(), "default");
        Assert.True(ok.Success, ok.FailureReason);
        Assert.Equal("早上好，@叔叔 ！", ok.Data!["x"]!.Value<string>());
    }

    [Fact]
    /// <summary>
    /// 具名参数是**占位符**（CP 的 `AllowAnyNamedArguments = true`），不是失败。
    /// 译文中没有对应 `{{language}}` 时，参数就是无害的多余项 —— 照常取值。
    /// </summary>
    public void 具名参数不影响取值()
    {
        I18n("default.json", """{ "k": "v" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:k |language=fr}}" }"""), Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("v", outcome.Data!["x"]!.Value<string>());
    }

    /// <summary>
    /// `|` 在**嵌套令牌内部**时不是 i18n 的参数。RSV 真实的
    /// `{{i18n:GuestDialogue.{{Random: 1,2,3|key=RSVTouristDialogue}}}}` 曾被裸
    /// `Contains('|')` 误杀成「带参数」—— 那个 `|` 属于内层的 `Random`。
    /// </summary>
    [Fact]
    public void 嵌套令牌里的竖线不算i18n参数()
    {
        I18n("default.json", """{ "k": "v" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:GuestDialogue.{{Random: 1,2,3|key=RSVTouristDialogue}}}}" }"""),
            Translations(), "default");

        // 仍会失败（嵌套的 Random 静态展开不了），但**不能**再误报成「带参数」。
        Assert.DoesNotContain("带参数", outcome.FailureReason ?? "");
    }

    [Fact]
    public void 顶层竖线后的具名参数按占位符处理()
    {
        I18n("default.json", """{ "k": "v" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:k|language=fr}}" }"""), Translations(), "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("v", outcome.Data!["x"]!.Value<string>());
    }

    [Fact]
    public void 译文里的引号与反斜杠经json往返无损()
    {
        I18n("default.json", """{ "k": "He said \"hi\" \\ bye" }""");

        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:k}}" }"""), Translations(), "default");

        Assert.True(outcome.Success);
        string text = outcome.Data!.ToString();
        Assert.Equal("He said \"hi\" \\ bye", JObject.Parse(text)["x"]!.Value<string>());
    }

    [Fact]
    public void 语言不同译文不同()
    {
        I18n("default.json", """{ "k": "English" }""");
        I18n("zh.json", """{ "k": "中文" }""");

        var t = Translations();
        Assert.Equal("English", Apply(JObject.Parse("""{ "x": "{{i18n:k}}" }"""), t, "default").Data!["x"]!.Value<string>());
        Assert.Equal("中文", Apply(JObject.Parse("""{ "x": "{{i18n:k}}" }"""), t, "zh-CN").Data!["x"]!.Value<string>());
    }

    [Fact]
    public void 检测是否含i18n令牌()
    {
        Assert.True(TranslationApplier.ContainsToken(JObject.Parse("""{ "x": "{{i18n:k}}" }""")));
        Assert.False(TranslationApplier.ContainsToken(JObject.Parse("""{ "x": "{{Random: 1}}" }""")));
        Assert.False(TranslationApplier.ContainsToken(JArray.Parse("""[ "plain" ]""")));
    }

    // ------------------------------------------------------------ 按语言展开写入

    private OutputTree NewOutput() => _tree = new(_src, _out);

    [Fact]
    public void 含i18n的已有资产产出基础与语言变体()
    {
        Touch("Data/Festivals/winter28.json", """{ "x": "{{i18n:k}}" }""");
        Touch("Data/Festivals/winter28.config", """{"Readers":[]}""");
        I18n("default.json", """{ "k": "English" }""");
        I18n("zh.json", """{ "k": "中文" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/Festivals/winter28",
            JObject.Parse("""{ "x": "{{i18n:k}}" }"""), Translations(), isNew: false);

        Assert.True(outcome.Success);
        Flush();
        Assert.Equal("English", JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Festivals", "winter28.json")))["x"]!.Value<string>());
        Assert.Equal("中文", JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Festivals", "winter28.zh-CN.json")))["x"]!.Value<string>());
        Assert.True(File.Exists(Path.Combine(_out, "Data", "Festivals", "winter28.zh-CN.config")));
        Assert.Contains("Data/Festivals/winter28.zh-CN.json", output.CreatedFiles);
        Assert.DoesNotContain("Data/Festivals/winter28.json", output.CreatedFiles);   // 已有资产不算新建
    }

    /// <summary>
    /// 键名含 i18n 时，语言变体仍走**增量覆盖**：已有本地化资产里补丁没碰的条目（原版译文）
    /// 必须留着，只把本补丁的条目按该语言的键名写进去。
    /// </summary>
    [Fact]
    public void 键名含i18n时语言变体按增量覆盖已有本地化资产()
    {
        Touch("Data/CraftingRecipes.es-ES.json", """{ "原版配方": "keep" }""");
        I18n("default.json", """{ "name": "English Name", "desc": "English desc" }""");
        I18n("es.json", """{ "name": "Nombre", "desc": "Descripción" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/CraftingRecipes",
            JObject.Parse("""{ "{{i18n:name}}": "{{i18n:desc}}" }"""),
            Translations(), isNew: false, tokens: null, changedKeys: new[] { "{{i18n:name}}" });

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();

        JObject es = JObject.Parse(File.ReadAllText(
            Path.Combine(_out, "Data", "CraftingRecipes.es-ES.json")));
        Assert.Equal("keep", es["原版配方"]!.Value<string>());   // 已有本地化内容保留
        Assert.Equal("Descripción", es["Nombre"]!.Value<string>()); // 新条目按西语键名写入
    }

    /// <summary>
    /// ★ **没有 `{{i18n}}` 的补丁也要镜像到中文本地化资产**：CP 会把补丁应用到
    /// 游戏实际请求的 `asset.zh-CN`（中文存档）；只改基础资产 ⇒ 模组加的对话/事件
    /// 在中文下根本不出现。
    /// </summary>
    [Fact]
    public void 无i18n的补丁也镜像到中文本地化资产()
    {
        Touch("Data/Events/Farm.json", """{ "vanilla": "Vanilla event", "gone": "To be removed" }""");
        Touch("Data/Events/Farm.zh-CN.json",
            """{ "vanilla": "中文事件", "old": "原版另一条", "gone": "要删掉的" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/Events/Farm",
            JObject.Parse("""{ "vanilla": "Vanilla event", "newEvent": "A new event" }"""),
            Translations(), isNew: false);

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();

        JObject zh = JObject.Parse(File.ReadAllText(
            Path.Combine(_out, "Data", "Events", "Farm.zh-CN.json")));
        Assert.Equal("中文事件", zh["vanilla"]!.Value<string>());      // 原版译文保留
        Assert.Equal("原版另一条", zh["old"]!.Value<string>());        // 补丁没碰的键保留
        Assert.Equal("A new event", zh["newEvent"]!.Value<string>());  // 本补丁新增的事件镜像过来
        Assert.Null(zh["gone"]);                                       // 本补丁删掉的键也删掉
    }

    /// <summary>
    /// ★ 本包**没有中文 i18n** 时也要把新增键写进 `.zh-CN`（值是默认语言文本/占位符）：
    /// CP 会把补丁应到中文存档请求的 `asset.zh-CN` 上。
    /// 实测（2026-10-19）：The Last Smoluanu 的 `Dwarf_EngagedRoommate` 在中文 UI 下整条缺失。
    /// </summary>
    [Fact]
    public void 没有中文i18n时新增键也镜像到中文变体()
    {
        Touch("Data/Strings.json", """{ "old": "x" }""");
        Touch("Data/Strings.zh-CN.json", """{ "old": "中文" }""");
        I18n("default.json", """{ "k": "English value" }""");   // 只有默认语言，没有 zh

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/Strings",
            JObject.Parse("""{ "old": "x", "newKey": "{{i18n:k}}" }"""),
            Translations(), isNew: false);

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();

        JObject zh = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Strings.zh-CN.json")));
        Assert.Equal("中文", zh["old"]!.Value<string>());
        Assert.Equal("English value", zh["newKey"]!.Value<string>());
    }

    /// <summary>
    /// 某语言的译文与基础语言逐字相同（包里给了语言文件但没翻译该键）⇒ 不写重复的语言变体，
    /// 让游戏按默认语言回退。实测 Lavender 的 Objects.fr-FR / Locations.zh-CN 与基础完全相同。
    /// </summary>
    [Fact]
    public void 译文与基础相同时不写语言变体()
    {
        Touch("Data/X.json", "{}");
        I18n("default.json", """{ "k": "Same" }""");
        I18n("zh.json", """{ "k": "Same" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/X",
            JObject.Parse("""{ "x": "{{i18n:k}}" }"""), Translations(), isNew: false);

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();

        Assert.False(File.Exists(Path.Combine(_out, "Data", "X.zh-CN.json")));
        Assert.Contains(outcome.Notes, n => n.Contains("省略"));
    }

    /// <summary>
    /// 键名本身是 i18n 令牌、包里又没有中文时，镜像到已有 `.zh-CN` 的键必须是**翻译后的键名**。
    /// `changedKeys` 里给的是原始键 `{{i18n:…}}`，不映射就会被当成"删除了这个键"而漏掉新增键。
    /// </summary>
    [Fact]
    public void 没有中文i18n时键名i18n新增键也镜像到中文变体()
    {
        Touch("Data/CraftingRecipes.json", """{ "old": "x" }""");
        Touch("Data/CraftingRecipes.zh-CN.json", """{ "old": "中文" }""");
        I18n("default.json", """{ "recipeName": "New Recipe" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/CraftingRecipes",
            JObject.Parse("""{ "old": "x", "{{i18n:recipeName}}": "Wheat/1 1/1 1/field/1" }"""),
            Translations(), isNew: false, tokens: null,
            changedKeys: new[] { "{{i18n:recipeName}}" });

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();

        JObject zh = JObject.Parse(File.ReadAllText(
            Path.Combine(_out, "Data", "CraftingRecipes.zh-CN.json")));
        Assert.Equal("中文", zh["old"]!.Value<string>());
        Assert.Equal("Wheat/1 1/1 1/field/1", zh["New Recipe"]!.Value<string>());
        Assert.Null(zh["{{i18n:recipeName}}"]);
    }

    /// <summary>
    /// ★ 条件落点会**改写事件键**（`146351222` → `146351222/G …`）：镜像必须按**最终键**找，
    /// 否则按原始键找不到就当成"删除"，中文事件表会少一条（实测 Cape Stardew）。
    /// </summary>
    [Fact]
    public void 条件改写后的事件键也镜像到中文本地化资产()
    {
        Touch("Data/Events/Farm.json", """{ "old": "x" }""");
        Touch("Data/Events/Farm.zh-CN.json", """{ "old": "中文" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/Events/Farm",
            JObject.Parse("""{ "old": "x", "146351222/G PLAYER_HAS_SEEN_EVENT Current 1": "event" }"""),
            Translations(), isNew: false, tokens: null, changedKeys: new[] { "146351222" });

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();

        JObject zh = JObject.Parse(File.ReadAllText(
            Path.Combine(_out, "Data", "Events", "Farm.zh-CN.json")));
        Assert.Equal("中文", zh["old"]!.Value<string>());
        Assert.Equal("event", zh["146351222/G PLAYER_HAS_SEEN_EVENT Current 1"]!.Value<string>());
    }

    /// <summary>
    /// ★ **已有本地化资产时，变体只覆盖本补丁改到的顶层键**。
    /// 实测（2026-10-19，业主报"界面全是英文"）：Cape Stardew/Crystal Island 只往
    /// <c>Strings/StringsFromCSFiles</c> 加一个 <c>{{i18n}}</c> 条目，却让整份
    /// <c>.zh-CN</c> 从英文基础资产重建 —— 原版 1650 条中文被换成英文。
    /// </summary>
    [Fact]
    public void 已有中文变体时只覆盖本补丁改到的键()
    {
        Touch("Data/Strings.json", """{ "v1": "Vanilla one", "v2": "Vanilla two" }""");
        Touch("Data/Strings.zh-CN.json", """{ "v1": "原版中文一", "v2": "原版中文二" }""");
        I18n("zh.json", """{ "new": "模组新增" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/Strings",
            JObject.Parse("""{ "v1": "Vanilla one", "v2": "Vanilla two", "modKey": "{{i18n:new}}" }"""),
            Translations(), isNew: false);

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();

        JObject zh = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Strings.zh-CN.json")));
        Assert.Equal("原版中文一", zh["v1"]!.Value<string>());   // 没被英文基础资产盖掉
        Assert.Equal("原版中文二", zh["v2"]!.Value<string>());
        Assert.Equal("模组新增", zh["modKey"]!.Value<string>()); // 本补丁的 i18n 生效

        JObject baseDoc = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Strings.json")));
        Assert.Equal("Vanilla one", baseDoc["v1"]!.Value<string>());
    }

    /// <summary>
    /// ★ 落盘前统一**值方言**：CP 的 <c>"28, 116, 16, 32"</c> 必须换成 XNB 树的对象写法，
    /// 否则该资产**打不出包**（实测 Cornucopia 制作包因此失败 10 个文件）。
    /// <para>
    /// 断言落在**基础资产与语言变体两处** —— 语言变体在游戏眼里是独立资产，
    /// 只在基础资产上换、变体漏掉的话回包照样失败。
    /// </para>
    /// </summary>
    [Fact]
    public void 落盘前把结构化标量的字符串换成对象()
    {
        Touch("Data/Buildings.json", "{}");
        Touch("Data/Buildings.config", """{"Readers":[]}""");
        I18n("default.json", """{ "k": "Coop" }""");
        I18n("zh.json", """{ "k": "鸡舍" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/Buildings",
            JObject.Parse("""{ "Coop": { "Name": "{{i18n:k}}", "AnimalDoor": "28, 116, 16, 32" } }"""),
            Translations(), isNew: false);

        Assert.True(outcome.Success);
        foreach (string file in new[] { "Buildings.json", "Buildings.zh-CN.json" })
        {
            Flush();
            JToken door = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", file)))["Coop"]!["AnimalDoor"]!;
            Assert.Equal(JTokenType.Object, door.Type);
            Assert.Equal(28.0, door["X"]!.Value<double>());
            Assert.Equal(32.0, door["Height"]!.Value<double>());
        }
    }

    [Fact]
    public void 无i18n令牌时不产变体()
    {
        Touch("Data/Plain.json", "{}");
        Touch("Data/Plain.config", "{}");
        I18n("default.json", """{ "k": "English" }""");
        I18n("zh.json", """{ "k": "中文" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/Plain",
            JObject.Parse("""{ "x": "plain" }"""), Translations(), isNew: false);

        Assert.True(outcome.Success);
        Assert.True(File.Exists(Path.Combine(_out, "Data", "Plain.json")));
        Assert.False(File.Exists(Path.Combine(_out, "Data", "Plain.zh-CN.json")));
    }

    [Fact]
    public void 翻译失败不落任何文件()
    {
        I18n("default.json", "{}");

        // 资产名不在输入树里 → 输出树里没有拷贝版本，可据此断言"什么都没写"。
        // 用**仍会失败**的形态（`{{i18n:...}}` 未闭合）守这条不变量 ——
        // 缺键与具名参数都已按 CP 语义降级/替换，不再走这条路。
        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/Bad",
            JObject.Parse("""{ "x": "{{i18n:k" }"""), Translations(), isNew: false);

        Assert.False(outcome.Success);
        Assert.False(File.Exists(Path.Combine(_out, "Data", "Bad.json")));
        Assert.False(File.Exists(Path.Combine(_out, "Data", "Bad.zh-CN.json")));
    }

    [Fact]
    public void 多条补丁写同一资产时变体仍完整翻译()
    {
        Touch("Data/Multi.json", """{ "a": "{{i18n:k1}}" }""");
        Touch("Data/Multi.config", "{}");
        I18n("default.json", """{ "k1": "One", "k2": "Two" }""");
        I18n("zh.json", """{ "k1": "一", "k2": "二" }""");

        var output = _tree = new OutputTree(_src, _out);
        var applier = new PatchApplier(output, Translations(), new RawAssetCache());

        Assert.True(applier.ApplyEditData("Data/Multi", """{ "Entries": { "a": "{{i18n:k1}}" } }""").Success);
        // 第二条补丁若读回已翻成英文的基础文件，变体里的 k1 就会退回英文
        Assert.True(applier.ApplyEditData("Data/Multi", """{ "Entries": { "b": "{{i18n:k2}}" } }""").Success);
Flush();

        var zh = JObject.Parse(File.ReadAllText(Path.Combine(_out, "Data", "Multi.zh-CN.json")));
        Assert.Equal("一", zh["a"]!.Value<string>());
        Assert.Equal("二", zh["b"]!.Value<string>());
    }

    /// <summary>
    /// **没有翻译集时照写，不判失败**（2026-10-11 晚**反转**了原来的口径）。
    /// <para>
    /// 原来这条叫 `没有翻译集时含i18n判失败`，理由是"宁可少写不可写错"。但对着 CP/SMAPI 核过：
    /// `TranslationHelper.Get(key)` 在**没有任何译文**时给的就是占位符 `(no translation:key)`
    /// （SMAPI `Translation.PlaceholderText`），CP **照常把资产写出去** ⇒ 判失败是**写少了**。
    /// 实测代价：新语料里 **229 条**补丁整个没写（内容里有 `{{i18n:…}}`、包里却没有 `i18n/`）。
    /// </para>
    /// </summary>
    [Fact]
    public void 没有翻译集时按占位符写出()
    {
        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Data/Bad",
            JObject.Parse("""{ "x": "{{i18n:k}}" }"""), translations: null, isNew: false);

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();                                  // 落盘是延迟的，不 Flush 文件还不存在
        // ⚠ 断言**写出去的那份**（`WriteLocalized` 翻译的是内部工作副本，调用方手里那份只做归一）
        string path = Path.Combine(_out, "Data", "Bad.json");
        Assert.True(File.Exists(path));
        Assert.Equal("(no translation:k)", JObject.Parse(File.ReadAllText(path))["x"]!.Value<string>());
    }

    // ------------------------------------------------------------ 内嵌令牌展开

    private DynamicTokenResolver Tokens(string dynamicTokens)
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"),
            $$"""{ "DynamicTokens": [ {{dynamicTokens}} ] }""");
        return DynamicTokenResolver.Load(_pack, _ => new DynamicTokenResolver.WhenResult(null, "测试不提供求值"));
    }

    [Fact]
    public void 展开键里的动态令牌后再查译文()
    {
        I18n("default.json", """{ "checkup.Keahi": "Checkup!" }""");
        I18n("zh.json", """{ "checkup.Keahi": "检查！" }""");
        // GenderNeutralRSV 值为空串（无条件块），键就静态化成 checkup.Keahi
        var tokens = Tokens("""{ "Name": "GenderNeutralRSV", "Value": "" }""");

        JToken data = JObject.Parse("""{ "x": "{{i18n:checkup.Keahi{{GenderNeutralRSV}}}}" }""");
        var en = Apply(data, Translations(), "default", tokens, "Characters/Dialogue/Keahi");
        var zh = Apply(data, Translations(), "zh-CN", tokens, "Characters/Dialogue/Keahi");

        Assert.True(en.Success, en.FailureReason);
        Assert.Equal("Checkup!", en.Data!["x"]!.Value<string>());
        Assert.True(zh.Success);
        Assert.Equal("检查！", zh.Data!["x"]!.Value<string>());
    }

    [Fact]
    public void 展开键里的TargetWithoutPath后再查译文()
    {
        I18n("default.json", """{ "keahiprank.Alissa": "Prank" }""");
        var tokens = Tokens("""{ "Name": "Unused", "Value": "x" }""");

        JToken data = JObject.Parse("""{ "x": "{{i18n:keahiprank.{{TargetWithoutPath}}}}" }""");
        var outcome = Apply(data, Translations(), "default", tokens, "Characters/Dialogue/Alissa");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("Prank", outcome.Data!["x"]!.Value<string>());
    }

    [Fact]
    public void 展开后仍是动态键时判失败且不留字面量()
    {
        I18n("default.json", """{ "k": "v" }""");
        var tokens = Tokens("""{ "Name": "Unused", "Value": "x" }""");

        // `{{Spouse}}` 是玩家存档状态、展开不了 → 必须判失败
        JToken data = JObject.Parse("""{ "x": "{{i18n:k{{Spouse}}}}" }""");
        var outcome = Apply(data, Translations(), "default", tokens, "Data/Foo");

        Assert.False(outcome.Success);
        Assert.Contains("内嵌令牌", outcome.FailureReason);
    }

    [Fact]
    public void 没有解析器时内嵌令牌仍判失败()
    {
        I18n("default.json", """{ "checkup.Keahi": "x" }""");

        JToken data = JObject.Parse("""{ "x": "{{i18n:checkup.Keahi{{GenderNeutralRSV}}}}" }""");
        var outcome = Apply(data, Translations(), "default");

        Assert.False(outcome.Success);
        Assert.Contains("内嵌令牌", outcome.FailureReason);
    }

    [Fact]
    public void WriteLocalized展开内嵌令牌并产出语言变体()
    {
        Touch("Data/Dialogue.json", "{}");
        Touch("Data/Dialogue.config", "{}");
        I18n("default.json", """{ "greet.Lenna": "Hi" }""");
        I18n("zh.json", """{ "greet.Lenna": "嗨" }""");
        var tokens = Tokens("""{ "Name": "Suffix", "Value": "" }""");

        var output = NewOutput();
        var outcome = TranslationApplier.WriteLocalized(output, "Characters/Dialogue/Lenna",
            JObject.Parse("""{ "x": "{{i18n:greet.{{TargetWithoutPath}}{{Suffix}}}}" }"""),
            Translations(), isNew: false, tokens);

        Assert.True(outcome.Success);
        Flush();
        Assert.Equal("Hi", JObject.Parse(File.ReadAllText(Path.Combine(_out, "Characters", "Dialogue", "Lenna.json")))["x"]!.Value<string>());
        Assert.Equal("嗨", JObject.Parse(File.ReadAllText(Path.Combine(_out, "Characters", "Dialogue", "Lenna.zh-CN.json")))["x"]!.Value<string>());
    }

    /// <summary>
    /// ★ **内容包压根没有 i18n 文件时，不许判失败** —— CP 走 SMAPI 的
    /// `TranslationHelper.Get(key)`，没有译文就给占位符 `(no translation:key)`
    /// （SMAPI `Translation.PlaceholderText`，反编译核过）并照常把资产写出去。
    /// <para>
    /// 实测代价：新语料里 **229 条**补丁因此整个没写（内容里有 `{{i18n:…}}`、包里却没有 `i18n/`）。
    /// </para>
    /// </summary>
    [Fact]
    public void 包里没有i18n时按占位符写出而不是失败()
    {
        // 不写任何 i18n 文件 ⇒ PackTranslations 为空
        var outcome = Apply(
            JObject.Parse("""{ "x": "{{i18n:Some.Key}}", "y": "{{i18n:Some.Key|default=Fallback}}" }"""),
            null, "default");

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("(no translation:Some.Key)", outcome.Data!["x"]!.Value<string>());
        Assert.Equal("Fallback", outcome.Data!["y"]!.Value<string>());
    }
}
