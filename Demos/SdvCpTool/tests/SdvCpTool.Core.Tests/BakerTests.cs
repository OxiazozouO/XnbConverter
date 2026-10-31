using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Conditions;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class BakerTests : IDisposable
{
    private readonly string _mods = Path.Combine(Path.GetTempPath(), "sdvmods_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvpack_" + Guid.NewGuid().ToString("N"));

    public BakerTests()
    {
        Directory.CreateDirectory(_mods);
        Directory.CreateDirectory(_pack);
    }

    public void Dispose()
    {
        Directory.Delete(_mods, recursive: true);
        Directory.Delete(_pack, recursive: true);
    }

    private void Mod(string uniqueId, string? configJson = null)
    {
        string dir = Path.Combine(_mods, uniqueId.Replace('.', '_'));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            $$"""{ "Name": "X", "UniqueID": "{{uniqueId}}" }""");
        if (configJson != null)
            File.WriteAllText(Path.Combine(dir, "config.json"), configJson);
    }

    private Baker Build() => new(new InstalledModIndex(_mods), _pack, "Tester.Pack");

    [Fact]
    public void HasMod已装为真()
    {
        Mod("Some.Mod");
        var when = WhenParser.Parse(JObject.Parse("""{"HasMod": "|contains=Some.Mod"}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.True(r.Value);
    }

    [Fact]
    public void HasMod未装为假()
    {
        Mod("Other.Mod");
        var when = WhenParser.Parse(JObject.Parse("""{"HasMod": "|contains=Some.Mod"}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.False(r.Value);
    }

    [Fact]
    public void ModId是本包自己的Id()
    {
        var when = WhenParser.Parse(JObject.Parse("""{"ModId": "Tester.Pack"}"""));

        Assert.True(Build().Evaluate(when!).Value);
    }

    /// <summary>
    /// <c>HasFile</c> 的路径在**输入参数槽**（<c>"HasFile: &lt;路径&gt;"</c>），
    /// 不在比较值槽 —— CP <c>HasFileValueProvider</c> 是
    /// <c>EnableInputArguments(required: true)</c> + <c>input.GetPositionalSegment()</c>。
    /// </summary>
    [Fact]
    public void HasFile看包内文件()
    {
        File.WriteAllText(Path.Combine(_pack, "config.json"), "{}");
        var when = WhenParser.Parse(JObject.Parse("""{"HasFile: config.json": true}"""));

        Assert.True(Build().Evaluate(when!).Value);
    }

    [Fact]
    public void HasFile不存在为假()
    {
        var when = WhenParser.Parse(JObject.Parse("""{"HasFile: nope.json": true}"""));

        Assert.False(Build().Evaluate(when!).Value);
    }

    /// <summary>
    /// <b>回归护栏</b>：曾经读的是**比较值槽**，于是 <c>{"HasFile: x": true}</c> 变成
    /// <c>File.Exists(packRoot + "true")</c> 恒假 —— 实测把 SVE「季节性服装」子包
    /// 21 条 Include 全判「条件为假」，整个包 0 条补丁。
    /// </summary>
    [Fact]
    public void HasFile读的是输入参数槽不是比较值()
    {
        File.WriteAllText(Path.Combine(_pack, "true"), "x");   // 故意造一个名叫 true 的文件
        var when = WhenParser.Parse(JObject.Parse("""{"HasFile: 真不存在.json": true}"""));

        Assert.False(Build().Evaluate(when!).Value);   // 不能因为值槽是 "true" 就命中
    }

    /// <summary>绝对路径：CP 一律判假（<c>Path.IsPathRooted</c> → false），不是报错。</summary>
    [Fact]
    public void HasFile绝对路径判假()
    {
        var when = WhenParser.Parse(JObject.Parse("""{"HasFile: /etc/hosts": true}"""));

        Baker.Result r = Build().Evaluate(when!);
        Assert.True(r.Success);
        Assert.False(r.Value);
    }

    /// <summary>
    /// <c>FirstValidFile</c>：输入是**路径列表**，取**第一个存在的**，比较值是"期望是哪一个"
    /// （CP <c>FirstValidFileValueProvider</c> 的 <c>Where(GetPathExists).Take(1)</c>）。
    /// </summary>
    [Fact]
    public void FirstValidFile取第一个存在的()
    {
        File.WriteAllText(Path.Combine(_pack, "b.png"), "x");
        var when = WhenParser.Parse(JObject.Parse("""{"FirstValidFile: a.png, b.png": "b.png"}"""));

        Assert.True(Build().Evaluate(when!).Value);
    }

    [Fact]
    public void FirstValidFile一个都不存在时为假()
    {
        var when = WhenParser.Parse(JObject.Parse("""{"FirstValidFile: a.png, b.png": "a.png"}"""));

        Baker.Result r = Build().Evaluate(when!);
        Assert.True(r.Success);
        Assert.False(r.Value);
    }

    [Fact]
    public void 与或非组合()
    {
        Mod("A.Mod");
        var when = WhenParser.Parse(JObject.Parse(
            """{"AND": [{"HasMod": "|contains=A.Mod"}, {"NOT": {"HasMod": "|contains=B.Mod"}}]}"""));

        Assert.True(Build().Evaluate(when!).Value);
    }

    [Fact]
    public void 含不可烘焙令牌时失败()
    {
        // ⚠ `Season` 从 2026-10-12 起是**可求值**的（按项目既有的"季节确定化"冻结到春天，见下条），
        //   所以这里换一个**真正**判不了的运行时条件：见没见过的剧情事件。
        var when = WhenParser.Parse(JObject.Parse("""{"HasSeenEvent": "1234567"}"""));

        Assert.False(Build().Evaluate(when!).Success);
    }

    /// <summary>
    /// ★ `Season` 条件按**季节确定化**求值（冻结到春天）—— 与值上的 `{{Season}}` 同一条政策。
    /// 不这么做的话，"季节才出现的路/桥/入口"在地图上会整批消失（实测 9 张原版地图的 warp 变少）。
    /// </summary>
    [Fact]
    public void Season按季节确定化到春天求值()
    {
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse("""{"Season": "spring"}"""))!).Value);
        Assert.False(Build().Evaluate(WhenParser.Parse(JObject.Parse("""{"Season": "Fall"}"""))!).Value);
    }

    /// <summary>
    /// ★ `FarmType` 按**农场类型确定化**冻结到 <c>Standard</c>（与季节冻结到春天同一条政策）。
    /// 理由是各家农场模组对**同一块地图**写互斥变体：全都不写会丢通路，全写则后写覆盖先写 ——
    /// 实测（2026-10-19）Frontier Farm 的 `Maps/BusStop` 变体覆盖了 Grandpa's Farm 的，
    /// 返程传送变成 `Farm 142 16`；本产物的农场图只有 140 宽 ⇒ 实机角色被传到**地图外**卡死。
    /// </summary>
    [Fact]
    public void FarmType按确定化到Standard求值()
    {
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse("""{"FarmType": "Standard"}"""))!).Value);
        Assert.False(Build().Evaluate(WhenParser.Parse(JObject.Parse("""{"FarmType": "FrontierFarm"}"""))!).Value);
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse("""{"FarmType": "standard"}"""))!).Value);

        // `|contains=` 与 Season 同语义：先折成布尔值，再与声明值比。
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse(
            """{"FarmType |contains=Standard, Beach": true}"""))!).Value);
        Assert.False(Build().Evaluate(WhenParser.Parse(JObject.Parse(
            """{"FarmType |contains=FrontierFarm": true}"""))!).Value);
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse(
            """{"FarmType |contains=FrontierFarm": false}"""))!).Value);

        // 声明值本身是没展开的令牌（实测 `Lnh's Farms Convenient Road` 这么写）⇒ 展开后可能是
        // Standard ⇒ 判"不知道"，绝不按字面当成假。
        var unknown = WhenParser.Parse(JObject.Parse(
            """{"FarmType": "{{LNHNewFarmReplacement}}"}"""))!;
        Assert.False(Build().Evaluate(unknown).Success);
    }

    /// <summary>
    /// ★ `Language` 按**语言确定化**冻结到 <c>zh</c>（与 Season/FarmType 同一条政策）。
    /// 实测界面包用 `Language |contains=zh` 分开中英文主界面图；判不了时中文变体永远不生效。
    /// </summary>
    [Fact]
    public void Language按确定化到中文求值()
    {
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse("""{"Language": "zh"}"""))!).Value);
        Assert.False(Build().Evaluate(WhenParser.Parse(JObject.Parse("""{"Language": "en"}"""))!).Value);
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse(
            """{"Language |contains=zh": true}"""))!).Value);
        Assert.False(Build().Evaluate(WhenParser.Parse(JObject.Parse(
            """{"Language |contains=zh": false}"""))!).Value);
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse(
            """{"Language |contains=en, zh": true}"""))!).Value);
    }

    /// <summary>
    /// ★★ **`Season |contains=…` 是"先折成布尔再比"，声明 <c>false</c> 就是取反** ——
    /// 实测踩过（2026-10-13）：`Season` 分支把具名参数丢了，于是
    /// <c>{"Season": "Spring", "Season |contains=Summer, Fall, Winter": false}</c>
    /// 被判成假、整条补丁「本就不适用」。SVE 一个包 **365 处**这么写（CommunityDay 的日程/台词），
    /// 整批内容因此消失 —— 业主问的"季节怎么也丢"里**能修的那一半**。
    /// <para>
    /// 判据照抄 CP：`Token.cs:144-147` ⇒ `ApplyContains` 返回 <c>true</c>/<c>false</c>
    /// **字符串**，再由 `Condition.IsMatch`（`Condition.cs:66`）与声明值求交集
    /// （CP 官方文档 tokens.md 的例子 `"Year |contains=1": "false"` 注释即 `// NOT year 1`）。
    /// </para>
    /// </summary>
    [Fact]
    public void Season的contains参数按CP语义折成布尔再与声明值比()
    {
        // 春天 ∉ {Summer, Fall, Winter} ⇒ contains 折成 false，声明也是 false ⇒ **成立**（SVE 那个写法）
        var sve = WhenParser.Parse(JObject.Parse(
            """{"Season": "Spring", "Season |contains=Summer, Fall, Winter": false}"""))!;
        Assert.True(Build().Evaluate(sve).Value);

        // 春天 ∈ {Spring, Summer} ⇒ contains = true，声明 true ⇒ 成立
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse(
            """{"Season |contains=Spring, Summer": true}"""))!).Value);

        // 春天 ∈ {Spring, Summer} ⇒ contains = true，声明 false ⇒ 不成立（"不是春夏"在春天为假）
        Assert.False(Build().Evaluate(WhenParser.Parse(JObject.Parse(
            """{"Season |contains=Spring, Summer": false}"""))!).Value);

        // 键槽里带 `:` 的写法（Sunberry 那种 `Season: |contains=winter`）走同一条路
        Assert.True(Build().Evaluate(WhenParser.Parse(JObject.Parse(
            """{"Season: |contains=winter": false}"""))!).Value);
    }

    /// <summary>
    /// 带 <c>|contains=</c> 时声明值只能是 true/false（CP `Token.cs:87-98` 会直接判补丁失败）
    /// —— 我们同样 <c>Success=false</c>（"不知道"），绝不把不知道当假。
    /// </summary>
    [Fact]
    public void Season的contains只接受布尔声明值()
    {
        var when = WhenParser.Parse(JObject.Parse("""{"Season |contains=Summer": "Summer"}"""))!;

        Assert.False(Build().Evaluate(when).Success);
    }

    /// <summary>参数写法不认识（不是 <c>|contains=</c>）⇒ 判失败，不猜。</summary>
    [Fact]
    public void Season的不认识参数判失败()
    {
        var when = WhenParser.Parse(JObject.Parse("""{"Season |equals=Summer": true}"""))!;

        Assert.False(Build().Evaluate(when).Success);
    }

    // 真实数据里的写法是 **键槽** 承载 `|contains=`（`"HasMod |contains=X": true`），
    // JSON 值才是 true/false（取反开关，与 TokenTranslator 的 |contains= 处理一致）。
    [Fact]
    public void HasMod键槽contains配合true()
    {
        Mod("Some.Mod");
        var when = WhenParser.Parse(JObject.Parse("""{"HasMod: |contains=Some.Mod": true}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.True(r.Value);
    }

    [Fact]
    public void HasMod键槽contains配合false时取反()
    {
        Mod("Some.Mod");
        var when = WhenParser.Parse(JObject.Parse("""{"HasMod: |contains=Some.Mod": false}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.False(r.Value);
    }

    [Fact]
    public void HasMod键槽contains未装时false取反为真()
    {
        Mod("Other.Mod");
        var when = WhenParser.Parse(JObject.Parse("""{"HasMod: |contains=Missing.Mod": false}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.True(r.Value);
    }

    /// <summary>
    /// `|contains=A, B` 是集合语义，**任一命中**即可；不能把整串当一个 mod id。
    /// 实测 Juliet & Jessie 的 `HasMod |contains=SVE, Nikolai: false`：SVE 已装时应判假。
    /// </summary>
    [Fact]
    public void HasMod键槽contains多值时任一命中()
    {
        Mod("A.Mod");
        var when = WhenParser.Parse(JObject.Parse(
            """{"HasMod |contains=A.Mod, B.Mod": true}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.True(r.Value);
    }

    [Fact]
    public void HasMod键槽contains多值都不装时false取反为真()
    {
        Mod("Other.Mod");
        var when = WhenParser.Parse(JObject.Parse(
            """{"HasMod |contains=A.Mod, B.Mod": false}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.True(r.Value);
    }

    /// <summary>
    /// 包内 DynamicToken 的 `|contains=A, B` 也按任一命中；值本身可能是多值。
    /// </summary>
    [Fact]
    public void 包内令牌条件的contains多值任一命中()
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"), """
            { "Format": "2.9.0",
              "DynamicTokens": [ { "Name": "Multi", "Value": "a, b" } ],
              "Changes": [] }
            """);
        Baker baker = Build();
        baker.DynamicTokens = DynamicTokenResolver.Load(_pack, null);
        var when = WhenParser.Parse(JObject.Parse(
            """{"Multi |contains=b, c": true}"""));

        var r = baker.Evaluate(when!);

        Assert.True(r.Success);
        Assert.True(r.Value);
    }

    [Fact]
    public void 子条件求值失败不被当成假()
    {
        // `HasMod` 为真、混进一个**真正判不了**的运行时条件 —— AND 里出现求值失败必须如实报失败，
        // 不能当成假。（原来这里用 `Season`，它现在是可求值的了。）
        Mod("A.Mod");
        var when = WhenParser.Parse(JObject.Parse(
            """{"AND": [{"HasMod": "|contains=A.Mod"}, {"HasSeenEvent": "1234567"}]}"""));

        var r = Build().Evaluate(when!);

        Assert.False(r.Success);
    }

    // ------------------------------------------------------ 命名空间令牌（config.json）

    // 命名空间 = 提供令牌的 mod 的 UniqueID，值取自它的 config.json。

    [Fact]
    public void 命名空间令牌按提供mod的config求值_字符串true与布尔true()
    {
        // 真实数据：`{"Rafseazz.RidgesideVillage/EnableTouristNPCs": "true"}`（字符串）
        Mod("Rafseazz.RidgesideVillage", """{ "EnableTouristNPCs": true }""");
        var when = WhenParser.Parse(JObject.Parse(
            """{"Rafseazz.RidgesideVillage/EnableTouristNPCs": "true"}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.True(r.Value);
        Assert.Contains("config.json", r.Note);   // 必须标注来源，与 HasMod 烘焙区分
    }

    [Fact]
    public void 命名空间令牌_布尔true与布尔true()
    {
        // 真实数据：`{"Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals": true}`（布尔）
        Mod("Rafseazz.RidgesideVillage", """{ "RSVNPCSAttendFestivals": true }""");
        var when = WhenParser.Parse(JObject.Parse(
            """{"Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals": true}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.True(r.Value);
    }

    [Fact]
    public void 命名空间令牌_config为真而条件期望假时判假()
    {
        // 真实数据：`ExpandedFestivalMaps` 在 config 里是 true，而 Old Festival Placements
        // 的 Include 要求 false —— 必须判假（跳过），不能展开。
        Mod("Rafseazz.RidgesideVillage", """{ "ExpandedFestivalMaps": true }""");
        var when = WhenParser.Parse(JObject.Parse(
            """{"Rafseazz.RidgesideVillage/ExpandedFestivalMaps": false}"""));

        var r = Build().Evaluate(when!);

        Assert.True(r.Success);
        Assert.False(r.Value);
        Assert.Contains("config.json", r.Note);   // 判假也必须标注来源
    }

    [Fact]
    public void 命名空间令牌_未装该mod时判失败()
    {
        Mod("Other.Mod");
        var when = WhenParser.Parse(JObject.Parse("""{"Missing.Mod/Token": true}"""));

        Assert.False(Build().Evaluate(when!).Success);
    }

    [Fact]
    public void 命名空间令牌_没有config_json时判失败()
    {
        Mod("A.B");   // 只有 manifest，没有 config.json
        var when = WhenParser.Parse(JObject.Parse("""{"A.B/Token": true}"""));

        Assert.False(Build().Evaluate(when!).Success);
    }

    [Fact]
    public void 命名空间令牌_config里没有同名键时判失败_不猜()
    {
        Mod("A.B", """{ "SomeOtherSetting": true }""");
        var when = WhenParser.Parse(JObject.Parse("""{"A.B/Token": true}"""));

        Assert.False(Build().Evaluate(when!).Success);
    }

    [Fact]
    public void 命名空间令牌_键名大小写不同时判失败_不猜()
    {
        Mod("A.B", """{ "Token": true }""");
        var when = WhenParser.Parse(JObject.Parse("""{"A.B/token": true}"""));

        Assert.False(Build().Evaluate(when!).Success);
    }

    [Fact]
    public void 命名空间令牌_config值不是标量时判失败()
    {
        Mod("A.B", """{ "Token": [1, 2] }""");
        var when = WhenParser.Parse(JObject.Parse("""{"A.B/Token": true}"""));

        Assert.False(Build().Evaluate(when!).Success);
    }

    [Fact]
    public void 命名空间令牌_条件没给比较值时判失败()
    {
        Mod("A.B", """{ "Token": true }""");
        var when = WhenParser.Parse(JObject.Parse("""{"A.B/Token": ""}"""));

        Assert.False(Build().Evaluate(when!).Success);
    }

    [Fact]
    public void 没有config_json时的失败信息指出唯一ID与键名并提示modconfig()
    {
        Mod("A.B");   // 无 config.json
        var when = WhenParser.Parse(JObject.Parse("""{"A.B/Token": true}"""));

        var r = Build().Evaluate(when!);

        Assert.False(r.Success);
        Assert.Contains("A.B", r.FailureReason);          // UniqueID
        Assert.Contains("Token", r.FailureReason);        // 缺失的键名
        Assert.Contains("config.json", r.FailureReason);  // 可操作的修复提示
    }

    // ------------------------------------------------- 单叶子求值（供常量折叠使用）

    [Fact]
    public void TryEvaluateLeaf_HasMod已装返回真()
    {
        Mod("Some.Mod");
        var key = WhenParser.Parse(JObject.Parse("""{"HasMod: |contains=Some.Mod": true}"""))!
            .Descendants().Single(n => n.Kind == WhenNodeKind.Key);

        Assert.True(Build().TryEvaluateLeaf(key));
    }

    [Fact]
    public void TryEvaluateLeaf_命名空间令牌按config返回真()
    {
        Mod("A.B", """{ "Token": true }""");
        var key = WhenParser.Parse(JObject.Parse("""{"A.B/Token": true}"""))!
            .Descendants().Single(n => n.Kind == WhenNodeKind.Key);

        Assert.True(Build().TryEvaluateLeaf(key));
    }

    /// <summary>
    /// ★ <c>HasFile</c> 的路径里**还有没展开的令牌**时**不得判假** —— 只能判「不知道」。
    /// <para>
    /// 实测：SVE 季节性服装写 <c>"FromFile": "assets/{{NoNose}}/maps/Cursors_{{Season}}.png"</c> +
    /// <c>"When": {"HasFile:{{FromFile}}": true}</c>。<c>{{FromFile}}</c> 代进去之后路径里还剩
    /// <c>{{NoNose}}</c>（配置令牌）和 <c>{{Season}}</c>（**运行时**令牌）。
    /// 拿这种路径去 <c>File.Exists</c> 必然假 ⇒ 整条补丁被静默丢掉；
    /// 而 CP 会先代这些令牌 —— <c>{{Season}}</c> 按当季取值，**文件在不在是随季节变的**。
    /// </para>
    /// </summary>
    [Fact]
    public void HasFile路径里还有令牌时不判假()
    {
        WhenNode key = WhenNode.Key("HasFile", "assets/{{NoNose}}/maps/Cursors_{{Season}}.png", "true");
        Assert.Null(Build().TryEvaluateLeaf(key));
    }

    [Fact]
    public void TryEvaluateLeaf_求不了值的令牌返回null而不是false()
    {
        // 运行时条件不是可烘焙令牌：必须返回 null（"我不知道"），绝不能返回 false。
        // （`Season` 现在按季节确定化可求值了，所以用 `HasSeenEvent` 当样本。）
        var key = WhenParser.Parse(JObject.Parse("""{"HasSeenEvent": "1234567"}"""))!
            .Descendants().Single(n => n.Kind == WhenNodeKind.Key);

        Assert.Null(Build().TryEvaluateLeaf(key));
    }

    [Fact]
    public void TryEvaluateLeaf_命名空间令牌缺config时返回null()
    {
        Mod("A.B");   // 无 config.json
        var key = WhenParser.Parse(JObject.Parse("""{"A.B/Token": true}"""))!
            .Descendants().Single(n => n.Kind == WhenNodeKind.Key);

        Assert.Null(Build().TryEvaluateLeaf(key));
    }
}
