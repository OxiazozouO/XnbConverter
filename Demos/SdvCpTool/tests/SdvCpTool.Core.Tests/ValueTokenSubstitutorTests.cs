using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 把补丁**值**里**能精确解析**的令牌代成值（包内 DynamicTokens / 配置字段 / `{{ModId}}`）。
/// <para>
/// 不代的话它们会原样落进产物，而游戏不认识 `{{…}}`：`Condition` 里的是非法 GSQ（条目静默失效）、
/// `Sprite`/`Texture` 里取不到图、**事件键**里的事件永不触发。
/// 实测语料 12/32 包中招、**575 处影响行为**。
/// </para>
/// </summary>
public class ValueTokenSubstitutorTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "vts_" + Guid.NewGuid().ToString("N"));

    public ValueTokenSubstitutorTests() => Directory.CreateDirectory(_pack);

    public void Dispose() => Directory.Delete(_pack, recursive: true);

    private DynamicTokenResolver Resolver(string dynamicTokensJson)
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"),
            $$"""{ "Format": "2.0.0", "DynamicTokens": {{dynamicTokensJson}}, "Changes": [] }""");
        return DynamicTokenResolver.Load(_pack, null);
    }

    private static ChangePatch Patch(string json)
    {
        JObject raw = JObject.Parse(json);
        return new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = "content.json",
        };
    }

    [Fact]
    public void ModId代成包的UniqueID()
    {
        ChangePatch p = Patch("""{ "Action": "Load", "Target": "Characters/X", "Entries": { "a": "{{ModId}}.Foo" } }""");

        ValueTokenSubstitutor.Apply(new[] { p }, Resolver("[]"), "My.Pack");

        Assert.Equal("My.Pack.Foo", p.Raw["Entries"]!["a"]!.Value<string>());
    }

    /// <summary>
    /// ★ <c>{{ModId}}</c> 的**大小写必须不敏感** —— CP 的令牌表是 `InvariantDictionary`（大小写不敏感），
    /// 于是 <c>{{ModID}}</c> 与 <c>{{ModId}}</c> 是同一个令牌。实测 SVE 就写的 <c>{{ModID}}</c>
    /// （用在 `data/TriggerActions` 的**条目键**里），旧实现只认精确的 `ModId` ⇒ 整条被判「代不掉的令牌」。
    /// </summary>
    [Fact]
    public void ModId令牌大小写不敏感()
    {
        ChangePatch p = Patch("""{ "Action": "EditData", "Target": "data/TriggerActions", "Entries": { "{{ModID}}_X": "v" } }""");

        ValueTokenSubstitutor.Apply(new[] { p }, Resolver("[]"), "My.Pack");

        Assert.Equal("v", p.Raw["Entries"]!["My.Pack_X"]!.Value<string>());
    }

    [Fact]
    public void 包内动态令牌代成值()
    {
        var r = Resolver("""[ { "Name": "Outfit", "Value": "Winter" } ]""");
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Characters",
          "Entries": { "Claire": { "Appearance": [ { "Sprite": "Characters/Claire_{{Outfit}}" } ] } } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        Assert.Equal("Characters/Claire_Winter",
            p.Raw["Entries"]!["Claire"]!["Appearance"]![0]!["Sprite"]!.Value<string>());
    }

    /// <summary>★ **事件键**里的令牌也要代 —— 键带令牌 ⇒ 前置条件段非法 ⇒ 事件永不触发。</summary>
    [Fact]
    public void 事件键里的令牌也代()
    {
        var r = Resolver("""[ { "Name": "Kid", "Value": "Leo" } ]""");
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Events/Town",
          "Entries": { "172426014/{{Kid}}/t 0900 1800": "script" } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        Assert.NotNull(p.Raw["Entries"]!["172426014/Leo/t 0900 1800"]);
    }

    /// <summary>⚠ 解析不了的（`{{Random}}`/`{{Spouse}}`…）**原样保留** —— 猜一个就是改语义。</summary>
    [Fact]
    public void 解析不了的令牌原样保留()
    {
        // `{{Spouse}}` 是玩家存档状态、`{{PlayerName}}` 是运行时值 —— 都**代不了**，原样留着。
        // （`{{Random}}` 不在此列：它**代得掉**，见下一个用例。）
        var r = Resolver("[]");
        ChangePatch p = Patch("""{ "Action": "Load", "Target": "X", "Entries": { "a": "{{Spouse}}/{{PlayerName}}" } }""");

        Assert.Equal(0, ValueTokenSubstitutor.Apply(new[] { p }, r, null));
        Assert.Equal("{{Spouse}}/{{PlayerName}}", p.Raw["Entries"]!["a"]!.Value<string>());
    }

    /// <summary>
    /// ★ <c>{{Random}}</c> 是**确定化**（取第一个备选），不是「代不了」。
    /// 依据 CP <c>RandomValueProvider</c>：取值由 <c>(BaseSeed, hash(补丁路径))</c> 决定，
    /// <c>BaseSeed</c> 又含**存档 id 与天数**、且每次上下文更新都会重掷 ⇒ **没有「正确的那一个」**。
    /// 语料实测 46 条补丁卡在它上面（一条婚姻日程里「走到随机一个坐标」被整条丢掉 = 那天没有日程）。
    /// </summary>
    [Fact]
    public void Random取第一个备选并报出确定化()
    {
        var r = Resolver("[]");
        ChangePatch p = Patch("""{ "Action": "Load", "Target": "X", "Entries": { "a": "{{Random: 22, 23, 24}}" } }""");

        var notes = new List<string>();
        Assert.Equal(1, ValueTokenSubstitutor.Apply(new[] { p }, r, null, (_, n) => notes.Add(n)));

        Assert.Equal("22", p.Raw["Entries"]!["a"]!.Value<string>());
        Assert.Contains(notes, n => n.Contains("Random") && n.Contains("确定化"));
    }

    /// <summary><c>{{Range: a,b}}</c> 是**确定的**（CP <c>RangeValueProvider</c>：给出 a..b 的多值集合）。</summary>
    [Fact]
    public void Range展开成多值集合()
    {
        var r = Resolver("[]");
        ChangePatch p = Patch("""{ "Action": "Load", "Target": "X", "Entries": { "a": "{{Range: 1, 4}}" } }""");

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        Assert.Equal("1, 2, 3, 4", p.Raw["Entries"]!["a"]!.Value<string>());
    }

    /// <summary>⚠ <c>Lossy</c>（条件判不了、落到无条件默认块）的值是**猜**的 ⇒ 整处不动。</summary>
    [Fact]
    public void Lossy的值不动()
    {
        File.WriteAllText(Path.Combine(_pack, "content.json"), """
        { "Format": "2.0.0",
          // ⚠ 顺序要紧：解析器的约定是「**无条件默认块在列表最前，条件块在后**」
          //   （它从后往前找最后一个确定的匹配），写反了就走不到那条有损路径。
          "DynamicTokens": [
            { "Name": "A", "Value": "兜底的" },
            { "Name": "A", "Value": "有条件的", "When": { "Season": "spring" } } ],
          "Changes": [] }
        """);
        // 不提供求值器 ⇒ 带 When 的块判不了 ⇒ 落到兜底块并被标 Lossy
        var r = DynamicTokenResolver.Load(_pack, null);
        ChangePatch p = Patch("""{ "Action": "Load", "Target": "X", "Entries": { "a": "{{A}}" } }""");

        Assert.Equal(0, ValueTokenSubstitutor.Apply(new[] { p }, r, null));
    }

    /// <summary>
    /// ★ **有损但结果恰好是布尔字面量**时放行：关系/剧情令牌的默认块常是 `true`/`false`。
    /// <para>
    /// 实测 `[CP] Hurst`：`CanBeRomanced` / `ItemDeliveryQuests` / `WinterStarParticipant` /
    /// `FlowerDanceCanDance` 都是 `{{HurstSocialStatus}}` 这类令牌。原先有损一律拒 ⇒
    /// **整个 `WoodsmanHurst` 条目**不写 ⇒ `Data/Characters` 里根本没有这个 NPC。
    /// 纯 XNB 里"少一个 NPC"比"按默认块少一项行为"糟得多；其它类型（贴图路径/事件键）仍不猜。
    /// </para>
    /// </summary>
    [Fact]
    public void Lossy的布尔值按默认块代掉()
    {
        var r = Resolver("""
            [ { "Name": "HurstSocialStatus", "Value": true },
              { "Name": "HurstSocialStatus", "Value": false, "When": { "HasFlag": "sprinton.hurst.HurstCanBeRomanced" } } ]
            """);
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Data/Characters",
              "Entries": { "WoodsmanHurst": { "CanBeRomanced": "{{HurstSocialStatus}}" } } }
            """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        Assert.Equal("true",
            p.Raw["Entries"]!["WoodsmanHurst"]!["CanBeRomanced"]!.Value<string>());
    }

    /// <summary>
    /// ★ 逐条问「这条补丁写出去的值里还有没有没展开的令牌」——
    /// 资产级的自检点不到补丁，写入路径上能，于是「要不要整条不写」这笔账才算得准。
    /// </summary>
    [Fact]
    public void 逐条问得出还有没有未展开令牌()
    {
        var r = Resolver("""[ { "Name": "S", "Value": "x" } ]""");

        ChangePatch bad = Patch("""{ "Action": "Load", "Target": "X", "Entries": { "a": "{{Random: 1,2}}" } }""");
        Assert.True(ValueTokenSubstitutor.HasUnresolvedToken(bad));

        ChangePatch good = Patch("""{ "Action": "Load", "Target": "X", "Entries": { "a": "{{S}}" } }""");
        ValueTokenSubstitutor.Apply(new[] { good }, r, "My.Pack");
        Assert.False(ValueTokenSubstitutor.HasUnresolvedToken(good));

        // `{{i18n:…}}` 不算 —— 那是通道 B1 自己处理的键
        ChangePatch i18n = Patch("""{ "Action": "Load", "Target": "X", "Entries": { "a": "{{i18n:K}}" } }""");
        Assert.False(ValueTokenSubstitutor.HasUnresolvedToken(i18n));

        // ★ **键名里**的令牌也算 —— 实测 13 处未展开令牌全在 `Data/Events` 的**事件键**里
        ChangePatch inKey = Patch("""{ "Action": "Load", "Target": "X", "Entries": { "123/e 456{{PlayerName}}": "x" } }""");
        Assert.True(ValueTokenSubstitutor.HasUnresolvedToken(inKey));

        // `When` 不参与（条件走 GSQ 那条路，不写进产物）
        ChangePatch inWhen = Patch("""{ "Action": "Load", "Target": "X", "When": { "HasFlag": "{{Random:1,2}}" } }""");
        Assert.False(ValueTokenSubstitutor.HasUnresolvedToken(inWhen));
    }

    /// <summary>
    /// ★ <b>i18n 的具名参数里</b>的内嵌令牌也要报 —— 它会被代入译文，变成字面花括号。
    /// <para>
    /// 实测（谢恩 / SVE，语料 13 处）：源文件写
    /// <c>speak Jas "{{i18n:ICS.Shane13Heart.Part1.event59443124.0 |JasAuncleToken={{JasAuncleToken}}}}"</c>，
    /// <c>TranslationApplier</c> 把参数值原样代进译文的 <c>{{JasAuncleToken}}</c> 占位符，
    /// 于是产出的 <c>Data/Events/Farm.json</c> 里是字面的
    /// <c>早上好，@{{JasAuncleToken}} ！</c>。
    /// </para>
    /// <para>
    /// 漏网的根因是**配对**：旧实现找第一个 <c>}}</c>，而那正是**内层**令牌的结尾，
    /// 于是整段被当成「一个 i18n 令牌」放行。纯 i18n（无内嵌）仍要放行，见上一个用例。
    /// </para>
    /// </summary>
    [Fact]
    public void i18n具名参数里的内嵌令牌算未展开()
    {
        ChangePatch nested = Patch("""
        { "Action": "EditData", "Target": "Data/Events/Farm",
          "Entries": { "ShaneHello": "textAboveHead Shane \"{{i18n:K |PlayerName={{PlayerName}}}}\"" } }
        """);
        Assert.True(ValueTokenSubstitutor.HasUnresolvedToken(nested));

        // ★ 参数值里**能代的令牌会被代掉**（`DynamicTokenResolver` 现在走词法器，
        //   会递归进令牌的输入参数 —— CP 的输入参数本来就是一棵令牌树）。
        //   代掉之后就没有残渣了 ⇒ 这一条不该再报。
        ChangePatch resolvable = Patch("""
        { "Action": "EditData", "Target": "Data/Events/Farm",
          "Entries": { "x": "{{i18n:K |Season={{Season}}}}" } }
        """);
        var r = Resolver("""[ { "Name": "Season", "Value": "spring" } ]""");
        ValueTokenSubstitutor.Apply(new[] { resolvable }, r, null);
        Assert.Equal("{{i18n:K |Season=spring}}",
            resolvable.Raw["Entries"]!["x"]!.Value<string>());
        Assert.False(ValueTokenSubstitutor.HasUnresolvedToken(resolvable));

        // 代不掉的（`{{PlayerName}}` 是运行时令牌）仍要报，且**不打散外层**
        ChangePatch runtime = Patch("""
        { "Action": "EditData", "Target": "Data/Events/Farm",
          "Entries": { "x": "{{i18n:K |PlayerName={{PlayerName}}}}" } }
        """);
        ValueTokenSubstitutor.Apply(new[] { runtime }, r, null);
        Assert.Equal("{{i18n:K |PlayerName={{PlayerName}}}}",
            runtime.Raw["Entries"]!["x"]!.Value<string>());
        Assert.True(ValueTokenSubstitutor.HasUnresolvedToken(runtime));

        // 键里的**字段引用令牌**由写入侧在键上展开，不算残渣
        // （否则会误杀 SVE 大量 {{i18n:{{TargetWithoutPath}}.X}}）
        ChangePatch fieldKey = Patch("""
        { "Action": "EditData", "Target": "Data/Events/Farm",
          "Entries": { "x": "{{i18n:{{TargetWithoutPath}}.Greeting}}" } }
        """);
        Assert.False(ValueTokenSubstitutor.HasUnresolvedToken(fieldKey));
    }

    /// <summary>
    /// ★ **展开后的键名撞车时不能覆盖**：原键 `A/{{S}}` 展开成 `A/x`，而同对象里已经有 `A/x` ——
    /// 直接赋值会把**已有那条静默丢掉**。撞车时**不改名**（原键带着令牌留下，自检会报出来）。
    /// </summary>
    [Fact]
    public void 展开后键名撞车时不覆盖已有条目()
    {
        var r = Resolver("""[ { "Name": "S", "Value": "x" } ]""");
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Events/Town",
          "Entries": { "A/x": "已有的", "A/{{S}}": "新来的" } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        JObject entries = (JObject)p.Raw["Entries"]!;
        Assert.Equal("已有的", entries["A/x"]!.Value<string>());      // 没被覆盖
        Assert.Equal("新来的", entries["A/{{S}}"]!.Value<string>());   // 带令牌的留在原地
    }

    /// <summary>`When` / `Target` / `FromFile` 不碰（条件走 GSQ 那条路；路径字段已另有处理）。</summary>
    [Fact]
    public void When与路径字段不碰()
    {
        var r = Resolver("""[ { "Name": "S", "Value": "spring" } ]""");
        ChangePatch p = Patch("""
        { "Action": "Load", "Target": "Maps/{{S}}_town", "FromFile": "a/{{S}}.png",
          "When": { "Season": "{{S}}" }, "Entries": { "k": "v" } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        Assert.Equal("Maps/{{S}}_town", p.Raw["Target"]!.Value<string>());
        Assert.Equal("a/{{S}}.png", p.Raw["FromFile"]!.Value<string>());
        Assert.Equal("{{S}}", p.Raw["When"]!["Season"]!.Value<string>());
    }

    /// <summary>
    /// 婚姻目标：`CanBeRomanced` 挂着判不了的关系状态令牌、但条目带 `SpouseRoom` ⇒ 冻结为 `true`。
    /// 语料依据：S&amp;S 的 Mateo/Hector/Cirrus、Bear Family 的 Sigurd/Gunnar/Teren —— 默认块是 false，
    /// 只有 `HasFlag` 块是 true；不冻结的话整条 `EditData` 被 `Lossy` 拒掉，NPC 根本不存在。
    /// </summary>
    [Fact]
    public void 带婚房的关系状态令牌冻结为可婚()
    {
        var r = Resolver("""
        [
          { "Name": "MateoRelationshipStatus", "Value": "false" },
          { "Name": "MateoRelationshipStatus", "Value": "true", "When": { "HasFlag": "MateoRomanticFlag" } }
        ]
        """);
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Characters", "Entries": {
            "Mateo": { "CanBeRomanced": "{{MateoRelationshipStatus}}",
                       "SpouseRoom": { "MapAsset": "MateoSpouseRoom" } } } }
        """);
        var notes = new List<string>();

        ValueTokenSubstitutor.Apply(new[] { p }, r, null, (_, n) => notes.Add(n));

        JToken entry = p.Raw["Entries"]!["Mateo"]!;
        Assert.Equal(JTokenType.Boolean, entry["CanBeRomanced"]!.Type);
        Assert.True(entry["CanBeRomanced"]!.Value<bool>());
        Assert.Contains(notes, n => n.Contains("MateoRelationshipStatus"));
    }

    /// <summary>
    /// 有损展开的结果**只剩 `{{i18n:…}}`** 时放行：译文通道会展开它，猜的只是用哪个键。
    /// 实测 S&amp;S 的 `TrueName`（`{{i18n:Core.Hector.Name}}` / 看过事件后 `…Biróg.Name`）
    /// 让整条 Hector 条目被拒。
    /// </summary>
    [Fact]
    public void 有损展开只剩i18n时放行()
    {
        var r = Resolver("""
        [
          { "Name": "TrueName", "Value": "{{i18n:Core.Hector.Name}}" },
          { "Name": "TrueName", "Value": "{{i18n:Core.Birog.Name}}",
            "When": { "HasSeenEvent": "SnS.Ch2.Hector.16" } }
        ]
        """);
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Characters", "Entries": {
            "Hector": { "DisplayName": "{{TrueName}}" } } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        Assert.Equal("{{i18n:Core.Hector.Name}}",
            p.Raw["Entries"]!["Hector"]!["DisplayName"]!.Value<string>());
        Assert.False(ValueTokenSubstitutor.HasUnresolvedToken(p));
    }

    /// <summary>
    /// 角色条目的 `Appearance[].Condition` / `Home[].Condition` 里带 CP 令牌 ⇒ 删掉那条变体。
    /// 原版该字段是 GSQ，令牌代出来通常不是合法 GSQ；留着会因「值里有无法展开的令牌」拒掉整条 NPC。
    /// </summary>
    [Fact]
    public void 角色条目里带令牌的条件变体被删掉()
    {
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Characters", "Entries": {
            "Sigurd": {
              "CanBeRomanced": true,
              "Appearance": [
                { "Id": "ok", "Condition": "SEASON Spring", "Sprite": "Characters/A" },
                { "Id": "bad", "Condition": "IS_EVENT festival_fall27, {{Query: {{Year}} % 2 = 1 }}",
                  "Sprite": "Characters/B" }
              ],
              "Home": [
                { "Location": "Town", "Tile": { "X": 1, "Y": 2 },
                  "Condition": "PLAYER_HAS_SEEN_EVENT x" }
              ]
            } } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, Resolver("[]"), null);

        JArray appearance = (JArray)p.Raw["Entries"]!["Sigurd"]!["Appearance"]!;
        Assert.Equal("ok", Assert.Single(appearance)["Id"]!.Value<string>());
        Assert.Single((JArray)p.Raw["Entries"]!["Sigurd"]!["Home"]!);
    }

    /// <summary>
    /// `Home[].Location` / `Tile.X/Y` 的关系地图令牌：判不了时取默认块（有损，但比整条 NPC 消失好）。
    /// </summary>
    [Fact]
    public void 住所地图令牌有损确定化到默认块()
    {
        var r = Resolver("""
        [
          { "Name": "GunnarMap", "Value": "BearLodge" },
          { "Name": "GunnarMap", "Value": "LoadingZone", "When": { "HasFlag": "Gunnar7DayAbsence" } },
          { "Name": "GunnarMapX", "Value": 2 },
          { "Name": "GunnarMapY", "Value": 5 }
        ]
        """);
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Characters", "Entries": {
            "Gunnar": { "Home": [ { "Location": "{{GunnarMap}}",
                                    "Tile": { "X": "{{GunnarMapX}}", "Y": "{{GunnarMapY}}" } } ] } } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        JToken home = p.Raw["Entries"]!["Gunnar"]!["Home"]![0]!;
        Assert.Equal("BearLodge", home["Location"]!.Value<string>());
        Assert.Equal("2", home["Tile"]!["X"]!.Value<string>());
        Assert.Equal("5", home["Tile"]!["Y"]!.Value<string>());
    }

    /// <summary>
    /// `Data/Characters` 的条件里 CP 布尔令牌代出的 `true`/`false` 不是合法 GSQ
    /// （游戏只认大写的 `TRUE`/`FALSE`）⇒ 折成合法条件；恒假的变体删掉。
    /// </summary>
    [Fact]
    public void 角色外观条件里的布尔字面量折成GSQ()
    {
        var r = Resolver("""[ { "Name": "On", "Value": "true" }, { "Name": "Off", "Value": "false" } ]""");
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Characters", "Entries": {
            "A": { "Appearance": [
              { "Id": "a", "Condition": "SEASON Spring, {{On}}" },
              { "Id": "b", "Condition": "SEASON Fall, {{Off}}" },
              { "Id": "c", "Condition": "SEASON Summer, !{{Off}}" },
              { "Id": "d", "Condition": "ANY \"IS_EVENT x, {{On}}\" \"SEASON Fall\"" }
            ] } } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        JArray appearance = (JArray)p.Raw["Entries"]!["A"]!["Appearance"]!;
        Assert.Equal(3, appearance.Count);
        Assert.Equal("SEASON Spring", appearance[0]!["Condition"]!.Value<string>());
        Assert.Equal("SEASON Summer", appearance[1]!["Condition"]!.Value<string>());
        Assert.Equal("ANY \"IS_EVENT x\" \"SEASON Fall\"", appearance[2]!["Condition"]!.Value<string>());

        // 形态 B：`TargetField: ["A","Appearance"]`，Entries 的值就是变体本身
        // （SCC East Scarp 的实际写法）。
        ChangePatch p2 = Patch("""
        { "Action": "EditData", "Target": "Data/Characters", "TargetField": [ "A", "Appearance" ],
          "Entries": {
            "x": { "Id": "x", "Condition": "SEASON Spring, {{On}}" },
            "y": { "Id": "y", "Condition": "SEASON Fall, {{Off}}" } } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p2 }, r, null);

        JObject e2 = (JObject)p2.Raw["Entries"]!;
        // 条件能代成合法 GSQ ⇒ 保留原条件；恒假的那条删掉。
        Assert.Equal("SEASON Spring", e2["x"]!["Condition"]!.Value<string>());
        Assert.Null(e2["y"]);
    }

    /// <summary>
    /// 形态 B（`TargetField: ["A","Appearance"]`）里**所有变体的条件都代不掉**时，
    /// 至少保留第一条并去掉 Condition —— 否则这个 NPC 没有任何外观。
    /// </summary>
    [Fact]
    public void 形态B全部条件代不掉时保留第一条()
    {
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Characters", "TargetField": [ "A", "Appearance" ],
          "Entries": {
            "x": { "Id": "x", "Condition": "SEASON Spring, {{On}}" },
            "y": { "Id": "y", "Condition": "SEASON Fall, {{Off}}" } } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, Resolver("[]"), null);

        JObject e = (JObject)p.Raw["Entries"]!;
        Assert.NotNull(e["x"]);
        Assert.Null(e["x"]!["Condition"]);
        Assert.Null(e["y"]);
    }

    /// <summary>没有婚房的关系状态令牌（Hurst/Nora/Rodney/Cambria）不动 —— 冻结会让人娶进默认房间。</summary>
    [Fact]
    public void 没有婚房的关系状态令牌冻结成默认块()
    {
        var r = Resolver("""
        [
          { "Name": "HurstRomanceStatus", "Value": "false" },
          { "Name": "HurstRomanceStatus", "Value": "true", "When": { "HasFlag": "HurstRomanticFlag" } }
        ]
        """);
        ChangePatch p = Patch("""
        { "Action": "EditData", "Target": "Data/Characters", "Entries": {
            "Hurst": { "CanBeRomanced": "{{HurstRomanceStatus}}" } } }
        """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        // 冻结成默认块 `false`：NPC **存在**、只是不能结婚。原先留着令牌 ⇒ 写入闸拒绝
        // 整条条目 ⇒ `Data/Characters` 里根本没有 Hurst（玩家见不到他）。
        // 带婚房的条目由 `FreezeRomanceFlags` 先铸成 `true`（见上一个测试），不会走到这里。
        Assert.Equal("false",
            p.Raw["Entries"]!["Hurst"]!["CanBeRomanced"]!.Value<string>());
    }

    /// <summary>
    /// ★ <c>TextOperations</c> 的 <c>Target</c> 是**键路径**，不是补丁根上的 `Target` 字段 ——
    /// 不能一起跳过，否则 `{{FestivalYearCheck}}` 会原样写进产出键
    /// （实测 10 个节日文件出现 `Set-Up_additionalCharacters{{FestivalYearCheck}}`）。
    /// 条件判不了时取**默认块**（空串），比留裸令牌好。
    /// </summary>
    [Fact]
    public void TextOperations路径里的有损令牌取默认块()
    {
        DynamicTokenResolver r = Resolver("""
            [ { "Name": "FestivalYearCheck", "Value": "" },
              { "Name": "FestivalYearCheck", "Value": "_y2",
                "When": { "Query: {{Year}} % 2 = 0": true } } ]
            """);
        ChangePatch p = Patch("""
            { "Action": "EditData", "Target": "Data/Festivals/spring13",
              "TextOperations": [ { "Operation": "Append",
                "Target": [ "Entries", "Set-Up_additionalCharacters{{FestivalYearCheck}}" ],
                "Value": "SenS 37 96 2", "Delimiter": "/" } ] }
            """);

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        JArray path = (JArray)p.Raw["TextOperations"]![0]!["Target"]!;
        Assert.Equal("Set-Up_additionalCharacters", path[1]!.Value<string>());
    }

    /// <summary>同样地，<c>Entries</c> 的**键名**里带判不了的令牌时取默认块。</summary>
    [Fact]
    public void Entries键名里的有损令牌取默认块()
    {
        DynamicTokenResolver r = Resolver("""
            [ { "Name": "FestivalYear", "Value": "" },
              { "Name": "FestivalYear", "Value": "_y2",
                "When": { "Query: {{Year}} % 2 = 0": true } } ]
            """);
        ChangePatch p = Patch("""{ "Action": "EditData", "Target": "Data/X", "Entries": { "K{{FestivalYear}}": "v" } }""");

        ValueTokenSubstitutor.Apply(new[] { p }, r, null);

        Assert.NotNull(p.Raw["Entries"]!["K"]);
        Assert.Null(p.Raw["Entries"]!["K{{FestivalYear}}"]);
    }
}
