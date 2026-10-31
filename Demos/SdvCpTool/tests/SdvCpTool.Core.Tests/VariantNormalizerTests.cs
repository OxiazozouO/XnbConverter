using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Normalizing;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 变体归一化器：把「同一意图的多种实现」量化并查发射表。
/// <para>
/// 最要命的一条：<b>「未查」不得被算成「有」</b>——否则覆盖率会虚报。
/// </para>
/// </summary>
public class VariantNormalizerTests
{
    private static PatchVerdict Verdict(string action, string target, string? when, LandingKind? landing = null)
    {
        JObject raw = JObject.Parse($$"""
        { "Action": "{{action}}", "Target": "{{target}}"{{(when is null ? "" : $", \"When\": {when}")}} }
        """);
        return new PatchVerdict
        {
            Patch = new ChangePatch
            {
                Raw = raw,
                Action = action,
                Target = target,
                When = raw["When"] as JObject,
                SourceFile = "content.json",
            },
            Landing = landing,
        };
    }

    private static PatchVerdict Season(string target, string season) =>
        Verdict("EditData", target, $$"""{ "Season": "{{season}}" }""");

    // ── 发射表：三态必须分开 ──────────────────────────────────────────────

    [Fact]
    public void 未收录的格子是未知而不是无()
    {
        // (AssetName, 好感度) 我们从没查过 —— 必须是 Unknown，不能是 None。
        EmitPlan plan = EmitterTable.Lookup(VariantCarrier.AssetName, VariantDimension.Hearts, "Mods/*");

        Assert.Equal(EmitKind.Unknown, plan.Kind);
    }

    /// <summary>
    /// `Characters/schedules` 的键名**原生支持季节**（`spring` / `spring_13` / `spring_Sun`，
    /// `NPC.cs:5822/5861/5878`）。早先判成 `None` 是错的 —— 且引用引错了
    /// （`Game1.cs:8296` 是邮件键、`GameLocation.cs:16442` 是地图图集前缀）。
    /// </summary>
    [Fact]
    public void 季节打日程键名有原生形式但有前提()
    {
        EmitPlan plan = EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.Season, "Characters/schedules");

        Assert.Equal(EmitKind.Native, plan.Kind);
        Assert.Contains("NPC.cs", plan.Evidence);
        // 前提必须写明：季节与另一维度同时出现时键链不可组合 —— 否则表给出过强的结论。
        Assert.NotEmpty(plan.Precondition);
    }

    /// <summary>
    /// 两种 schedules 的能力**完全不同**：`Characters/schedules` 是纯键链（季节进键名），
    /// `Strings/schedules` 是 `$query` 宿主而 `$query` 只有两分支、装不下四季。
    /// </summary>
    [Fact]
    public void 两种schedules的季节承载不同()
    {
        Assert.Equal(EmitKind.Native, EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.Season, "Characters/schedules").Kind);
        Assert.Equal(EmitKind.CapacityExceeded, EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.Season, "Strings/schedules").Kind);
    }

    [Fact]
    public void 季节打精灵有原生形式且带源码依据()
    {
        EmitPlan plan = EmitterTable.Lookup(VariantCarrier.Sprite, VariantDimension.Season, "Data/BigCraftables");

        Assert.Equal(EmitKind.Native, plan.Kind);
        Assert.Contains("Seasonal", plan.NativeForm);
        Assert.Contains("Object.cs", plan.Evidence);
    }

    /// <summary>
    /// 「看过的剧情」是**二值**维度，`$query` 的两分支正好够用 —— 所以在 `$query` 宿主上是 `Native`。
    /// （四季就装不下，那是 `Strings/schedules × 季节` 的格子。）
    /// </summary>
    [Fact]
    public void 看过的剧情打query宿主是原生形式()
    {
        Assert.Equal(EmitKind.Native, EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.SeenEvent, "Strings/schedules").Kind);
        Assert.Equal(EmitKind.Native, EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Festivals").Kind);
    }

    /// <summary>
    /// ★ **别的 mod 注册的自定义资产**（第一段带点，命名空间约定）归到 `Mods/*` ——
    /// 与 `Mods/...` 是**同一个结论**：原版不认识这个资产名，条目由注册它的 mod 的 C# 消费。
    /// <para>
    /// 不归的话这些格子会落到 `Unknown`（「待查」），而其实早就是查清的结论了。
    /// 实测语料里这一档是 **32 条**（全是 `sophie.ConversationTopics`）。
    /// ⚠ 我一度按「未写入的新建资产带条件」整档估成 249 条 —— 那是把两件事混了：
    /// 其余 217 条用的是**原版资产路径**（`Characters/Dialogue/Claire` 这类），只是那个角色不在树里。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("sophie.ConversationTopics")]
    [InlineData("furyx639.CustomBush/Data")]
    [InlineData("spacechase0.SpaceCore/ObjectExtensions")]
    public void 自定义mod资产归到Mods通配(string asset)
    {
        Assert.Equal("Mods/*", VariantNormalizer.AssetTypeOf(asset));
    }

    /// <summary>原版资产不受影响 —— 别把「第一段带点」判宽了。</summary>
    [Theory]
    [InlineData("Data/Objects")]
    [InlineData("LooseSprites/Cursors_1_6")]
    [InlineData("Characters/Dialogue/Abigail")]
    [InlineData("Maps/Mines/1")]
    public void 原版资产不被误判成自定义(string asset)
    {
        Assert.NotEqual("Mods/*", VariantNormalizer.AssetTypeOf(asset));
    }

    /// <summary>
    /// 没有兜底：查一个没收录的资产类型必须是 `Unknown`（**待查清单**），
    /// 不能因为「这一载体别的资产有」就顺手算成「有」。
    /// </summary>
    [Fact]
    public void 没收录的资产类型不兜底()
    {
        Assert.Equal(EmitKind.Unknown, EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.Marriage, "Data/mail").Kind);
    }

    /// <summary>
    /// ★ <b>结论按消费方走，不按载体走</b>：同是图像资产按季节换图，`Load`（<c>AssetName</c>）
    /// 与 <c>EditImage</c>（<c>Sprite</c>）落到**同一个机制**上 —— 这是表自己早就定下的规矩
    /// （`SeasonalImagePlan` 那三格就是这么合的）。载体不同不构成「另一格」。
    /// </summary>
    [Theory]
    [InlineData(VariantCarrier.AssetName, "Tilesheets/*")]
    [InlineData(VariantCarrier.Sprite, "Tilesheets/*")]
    [InlineData(VariantCarrier.Sprite, "LooseSprites/*")]
    [InlineData(VariantCarrier.Sprite, "Maps")]
    public void 季节换图的结论与载体无关(VariantCarrier carrier, string asset)
    {
        EmitPlan a = EmitterTable.Lookup(carrier, VariantDimension.Season, asset);
        EmitPlan b = EmitterTable.Lookup(VariantCarrier.Sprite, VariantDimension.Season, "Mods/*");

        Assert.Equal(EmitKind.Native, a.Kind);
        Assert.Equal(b.NativeForm, a.NativeForm);
    }

    /// <summary>
    /// ★ <c>Strings/StringsFromMaps</c> 是**装不下**：同一个资产的两个键可能各走一条路 ——
    /// 走 <c>new Dialogue(...)</c> 的（<c>NPC.cs:4696-4698</c>）`$query` 会执行，
    /// 走 <c>DialogueBox(string)</c> 的（<c>Game1.cs:9648-9654</c>）是死文本，
    /// 而键走哪条由**别处**（地图 <c>Action</c>）决定、光看资产数据分不出。
    /// </summary>
    [Theory]
    [InlineData(VariantDimension.Hearts)]
    [InlineData(VariantDimension.Marriage)]
    public void StringsFromMaps是装不下不是有(VariantDimension dim)
    {
        EmitPlan plan = EmitterTable.Lookup(VariantCarrier.Entry, dim, "Strings/StringsFromMaps");

        Assert.Equal(EmitKind.CapacityExceeded, plan.Kind);
        Assert.Contains("部分", plan.NativeForm);
    }

    /// <summary>
    /// `Data/Locations` 的条目级条件：落点**只在数组元素上**，顶层字段（`MusicDefault`）没有。
    /// 判 Native 必须带上这个前提 —— 否则会把 SVE 那条改 `MusicDefault` 的说成能转。
    /// </summary>
    [Fact]
    public void 数据地点条目级条件只在数组元素上()
    {
        EmitPlan plan = EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/Locations");

        Assert.Equal(EmitKind.Native, plan.Kind);
        Assert.Contains("数组元素", plan.Precondition);
    }

    /// <summary>
    /// 「整片都是 <c>$query</c> 宿主」的两个资产（2026-10-06 新查出来的）。
    /// 依据：<c>new Dialogue(</c> / <c>TryGetDialogue(</c> 的实参字面量与
    /// <c>drawObjectDialogue(</c>（死路）**分开**统计 —— 这两个只出现在活路那侧。
    /// </summary>
    [Theory]
    [InlineData("Data/ExtraDialogue")]
    [InlineData("Data/EngagementDialogue")]
    public void 两个新对话宿主是Native(string asset)
    {
        Assert.Equal(EmitKind.Native, EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.SeenEvent, asset).Kind);
    }

    /// <summary>两条路都有的资产**不当宿主**（同一个资产里有的键走活路、有的走死路）。</summary>
    [Theory]
    [InlineData("Strings/Characters")]
    [InlineData("Strings/Locations")]
    public void 两条路都有的资产不判Native(string asset)
    {
        Assert.Equal(EmitKind.Unknown, EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.SeenEvent, asset).Kind);
    }

    /// <summary>
    /// 值被按 <c>/</c> 切字段读、且**没有条目对象**的资产 —— `None` 是结论。
    /// <para>
    /// <c>Data/CraftingRecipes</c> 特别值得一提：它的第 5 段确实叫「解锁条件」，
    /// 但语法是**技能要求** <c>s &lt;技能&gt; &lt;等级&gt;</c>（<c>CraftingRecipe.cs:34, :220-224</c>），
    /// **不是 GSQ** —— 看到一个叫「条件」的段就判 Native 是错的。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Data/Fish")]
    [InlineData("Data/Monsters")]
    [InlineData("Data/CraftingRecipes")]
    [InlineData("Data/mail")]
    [InlineData("Data/ConcessionTastes")]
    [InlineData("Data/NPCGiftTastes")]
    public void 值是斜杠数据串的资产无落点(string asset)
    {
        Assert.Equal(EmitKind.None, EmitterTable.Lookup(
            VariantCarrier.Entry, VariantDimension.SeenEvent, asset).Kind);
    }

    /// <summary>
    /// 曾把「季节 × 地图」判成 `Native`（tilesheet 前缀）—— **判轻了**。
    /// 实测语料里这类补丁**全是** `SetProperties`（改图块内容），
    /// 而 tilesheet 前缀只改**图集指向**，命中≈0。降为「装不下」。
    /// </summary>
    [Fact]
    public void 季节打地图是装不下而不是有()
    {
        Assert.Equal(EmitKind.CapacityExceeded,
            EmitterTable.Lookup(VariantCarrier.Map, VariantDimension.Season, "Maps").Kind);
    }

    [Fact]
    public void 年份打地图是真无()
    {
        Assert.Equal(EmitKind.None,
            EmitterTable.Lookup(VariantCarrier.Map, VariantDimension.Year, "Maps").Kind);
    }

    // ── 载体判定 ──────────────────────────────────────────────────────────

    [Fact]
    public void 地图补丁载体是地图()
    {
        PatchVerdict v = Verdict("EditMap", "Maps/spring_outdoors", """{ "Season": "spring" }""");
        NormalizationReport r = VariantNormalizer.Run(new[] { v });

        Assert.Contains(VariantCarrier.Map, r.CarrierSpread[VariantDimension.Season]);
    }

    /// <summary>`Characters/schedules` 是纯键链，`Strings/schedules` 是 $query 宿主 —— 两者不可混为一类。</summary>
    [Fact]
    public void 两种schedules分属不同载体()
    {
        PatchVerdict keyChain = Verdict("EditData", "Characters/schedules/Abigail", """{ "Season": "spring" }""");
        PatchVerdict queryHost = Verdict("EditData", "Strings/schedules/Abigail", """{ "Season": "spring" }""",
            LandingKind.DialogueQuery);

        Assert.Equal(VariantCarrier.Entry, VariantNormalizer.Run(new[] { keyChain }).Cells[0].Carrier);
        Assert.Equal(VariantCarrier.Entry, VariantNormalizer.Run(new[] { queryHost }).Cells[0].Carrier);
    }

    /// <summary>改图 = 同张图集选矩形（Sprite）；Load = 每个变体一个新资产（AssetName）。按 Action 判，不按名字猜。</summary>
    [Fact]
    public void 改图是格子而Load是资产名()
    {
        PatchVerdict image = Verdict("EditImage", "Mods/mypack/atlas", """{ "Season": "spring" }""");
        PatchVerdict load = Verdict("Load", "Mods/mypack/atlas_spring", """{ "Season": "spring" }""");

        Assert.Equal(VariantCarrier.Sprite, VariantNormalizer.Run(new[] { image }).Cells[0].Carrier);
        Assert.Equal(VariantCarrier.AssetName, VariantNormalizer.Run(new[] { load }).Cells[0].Carrier);
    }

    // ── 分桶与覆盖 ────────────────────────────────────────────────────────

    [Fact]
    public void 四季覆盖同一资产合成一个意图()
    {
        var patches = new[]
        {
            Season("Data/BigCraftables", "spring"),
            Season("Data/BigCraftables", "summer"),
            Season("Data/BigCraftables", "fall"),
            Season("Data/BigCraftables", "winter"),
        };

        NormalizationReport r = VariantNormalizer.Run(patches);

        VariantIntent intent = Assert.Single(r.Intents);
        Assert.Equal(4, intent.PatchCount);
        Assert.Equal(4, intent.Values.Count);
        Assert.True(intent.CoversWholeDimension);
    }

    [Fact]
    public void 只覆盖两季不算完整意图()
    {
        var patches = new[]
        {
            Season("Data/BigCraftables", "spring"),
            Season("Data/BigCraftables", "summer"),
        };

        NormalizationReport r = VariantNormalizer.Run(patches);

        Assert.False(Assert.Single(r.Intents).CoversWholeDimension);
    }

    [Fact]
    public void 单条补丁不成意图()
    {
        NormalizationReport r = VariantNormalizer.Run(new[] { Season("Data/BigCraftables", "spring") });

        Assert.Empty(r.Intents);
        Assert.Equal(1, r.DimensionPatches);   // 但它仍计入熵的统计
    }

    [Fact]
    public void 无条件的补丁不计入()
    {
        var patches = new[]
        {
            Verdict("EditData", "Data/BigCraftables", null),
            Season("Data/BigCraftables", "spring"),
        };

        Assert.Equal(1, VariantNormalizer.Run(patches).DimensionPatches);
    }

    // ── 走位：条件在 KEY 里 ────────────────────────────────────────────────

    /// <summary>CP 的参数写在 KEY 里（`"Spouse |contains=Wizard": false`），不在 value 里。</summary>
    [Fact]
    public void 参数写在KEY里也能认出维度()
    {
        PatchVerdict v = Verdict("EditData", "Data/Festivals/spring24", """{ "Spouse |contains=Wizard": false }""");

        NormalizationReport r = VariantNormalizer.Run(new[] { v });

        Assert.Equal(1, r.DimensionPatches);
        Assert.Contains(VariantCarrier.Entry, r.CarrierSpread[VariantDimension.Marriage]);
    }

    [Fact]
    public void 带冒号的令牌取基名()
    {
        PatchVerdict v = Verdict("EditData", "Data/Characters/Abigail", """{ "Hearts:Abigail": "{{Range: 5, 14}}" }""",
            LandingKind.EntryConditionField);

        Assert.Equal(1, VariantNormalizer.Run(new[] { v }).DimensionPatches);
    }

    // ── 守恒与熵 ──────────────────────────────────────────────────────────

    [Fact]
    public void 四档之和恒等于总数()
    {
        var patches = new[]
        {
            Season("Data/BigCraftables", "spring"),          // Sprite → Native
            Season("Characters/schedules/Abigail", "fall"),   // EntryKey → None
            Verdict("EditData", "Strings/schedules/Abigail", """{ "HasSeenEvent": "1" }""", LandingKind.DialogueQuery),
            Verdict("EditData", "Data/X", """{ "Hearts:Abigail": "5" }"""),
        };

        NormalizationReport r = VariantNormalizer.Run(patches);

        Assert.Equal(r.DimensionPatches, r.Native + r.None + r.CapacityExceeded + r.Unknown);
        Assert.Equal(4, r.DimensionPatches);
    }

    [Fact]
    public void 熵就是载体集合的大小()
    {
        var patches = new[]
        {
            Verdict("EditImage", "Mods/pack/atlas", """{ "Season": "spring" }"""),   // Sprite
            Season("Characters/schedules/A", "spring"),                              // EntryKey
            Verdict("EditMap", "Maps/x", """{ "Season": "spring" }"""),              // Map
        };

        Assert.Equal(3, VariantNormalizer.Run(patches).CarrierSpread[VariantDimension.Season].Count);
    }

    // ── 「近似」判据（实测：SVE 季节灌木 208 条全部带 IsOutdoors）────────────

    private static PatchVerdict SeasonBush(string season, string? extra = null) =>
        Verdict("EditImage", "Mods/SVE/Little Shrub",
            $$"""{ "Season": "{{season}}"{{(extra is null ? "" : $", \"{extra}\": true")}} }""");

    [Fact]
    public void 四季齐备且无额外条件时是无损收敛()
    {
        var patches = new[]
        {
            SeasonBush("Spring"), SeasonBush("Summer"), SeasonBush("Fall"), SeasonBush("Winter"),
        };

        NormalizationReport r = VariantNormalizer.Run(patches);

        Assert.False(Assert.Single(r.Intents).IsApproximate);
        Assert.Equal(0, r.ApproximatePatches);
    }

    /// <summary>
    /// 原版季节机制**不区分室内外**，而 SVE 的 208 条季节灌木**全部**带 `IsOutdoors`。
    /// 认定「有最简形式」还不够 —— 必须同时标出「收敛会丢这一维」。
    /// </summary>
    [Fact]
    public void 带第二维度时收敛只能标近似()
    {
        var patches = new[]
        {
            SeasonBush("Spring", "IsOutdoors"), SeasonBush("Summer", "IsOutdoors"),
            SeasonBush("Fall", "IsOutdoors"), SeasonBush("Winter", "IsOutdoors"),
        };

        NormalizationReport r = VariantNormalizer.Run(patches);

        VariantIntent intent = Assert.Single(r.Intents);
        Assert.True(intent.IsApproximate);
        Assert.Contains("IsOutdoors", intent.OtherConditions);
        Assert.Equal(4, r.ApproximatePatches);
        Assert.Equal(1, r.ApproximateIntents);
    }

    // ── ★ 收敛量（N 个实现 → 1 个最简实现）────────────────────────────────

    /// <summary>
    /// 四季各一条、打在同一资产上 → **4 条 → 1 个最简形式**，收敛量 = 3。
    /// 这就是「多个实现转换为最简实现」的直接量化。
    /// </summary>
    [Fact]
    public void 四季合一才算真合并()
    {
        var patches = new[]
        {
            Verdict("EditImage", "Data/BigCraftables", """{ "Season": "spring" }"""),
            Verdict("EditImage", "Data/BigCraftables", """{ "Season": "summer" }"""),
            Verdict("EditImage", "Data/BigCraftables", """{ "Season": "fall" }"""),
            Verdict("EditImage", "Data/BigCraftables", """{ "Season": "winter" }"""),
        };

        NormalizationReport r = VariantNormalizer.Run(patches);

        Assert.Equal(1, r.MergedIntents);        // 1 个最简形式
        Assert.Equal(3, r.CollapsedPatches);     // 4 条 → 1 个，压掉 3
    }

    /// <summary>
    /// **关键区分**：逐条改写**不减少条数**，不得算进「合并」。
    /// 实测踩过：113 个 NPC 各自的 `&lt;NPC&gt;_spouse` 一个也没少，
    /// 却被算成 314 条「收敛」—— 那是过报。
    /// </summary>
    [Fact]
    public void 逐条改写不算合并()
    {
        var patches = new[]
        {
            Verdict("EditData", "Data/Festivals/spring13",
                """{ "Spouse": "Claire" }"""),
            Verdict("EditData", "Data/Festivals/spring13",
                """{ "Spouse": "Olivia" }"""),
            Verdict("EditData", "Data/Festivals/spring13",
                """{ "Spouse": "Sophia" }"""),
        };

        NormalizationReport r = VariantNormalizer.Run(patches);

        Assert.Equal(3, r.RewrittenPatches);   // 三条都被改写成原生形式
        Assert.Equal(0, r.MergedIntents);      // 但**一条也没合并**
        Assert.Equal(0, r.CollapsedPatches);
    }

    /// <summary>未定的格子**不算**收敛 —— 把「没查」算成「已收敛」就是虚报。</summary>
    [Fact]
    public void 未定格子的意图不计入收敛()
    {
        var patches = new[]
        {
            // `Data/mail` × 季节：我们**没查过**这个格子 → Unknown。
            Verdict("EditData", "Data/mail", """{ "Season": "spring" }"""),
            Verdict("EditData", "Data/mail", """{ "Season": "summer" }"""),
        };

        NormalizationReport r = VariantNormalizer.Run(patches);

        Assert.Single(r.Intents);
        Assert.Equal(0, r.RewrittenPatches);
        Assert.Equal(0, r.MergedIntents);
        Assert.Equal(0, r.CollapsedPatches);
    }

    // ── 资产类型这一轴（同格子不同资产 → 不同最简形式）────────────────────

    /// <summary>
    /// 同是「婚姻 × 条目键」，`Data/Festivals` 用 `<NPC>_spouse`、
    /// `Data/Events` 用事件键的 `O <NPC>` —— 只按 `(载体, 维度)` 查表会判错。
    /// </summary>
    [Fact]
    public void 同格子不同资产类型给不同最简形式()
    {
        EmitPlan festivals = EmitterTable.Lookup(VariantCarrier.Entry, VariantDimension.Marriage, "Data/Festivals");
        EmitPlan events = EmitterTable.Lookup(VariantCarrier.Entry, VariantDimension.Marriage, "Data/Events");

        Assert.Equal(EmitKind.Native, festivals.Kind);
        Assert.Contains("_spouse", festivals.NativeForm);
        Assert.Equal(EmitKind.Native, events.Kind);
        Assert.Contains("O ", events.NativeForm);
        Assert.NotEqual(festivals.NativeForm, events.NativeForm);
    }

    [Fact]
    public void 资产类型没有细分格时退回通用格()
    {
        // `Data/mail` 没有细分格，通用格也没收录 → 仍是「未定」，不是「无」。
        Assert.Equal(EmitKind.Unknown,
            EmitterTable.Lookup(VariantCarrier.Entry, VariantDimension.Marriage, "Data/mail").Kind);
    }

    /// <summary>
    /// 资产名必须归一：大小写不敏感，且「实例名」那一层要去掉 ——
    /// 否则 `Maps/Forest`、`Maps/Town` 会各算一个格子，格子数爆炸、未定虚高。
    /// </summary>
    [Fact]
    public void 资产类型归一到类型那一层()
    {
        Assert.Equal("Characters/schedules", VariantNormalizer.AssetTypeOf("characters/SCHEDULES/Abigail"));
        Assert.Equal("Data/Events", VariantNormalizer.AssetTypeOf(@"Data\Events\Town"));
        Assert.Equal("Data/Festivals", VariantNormalizer.AssetTypeOf("data/festivals/spring13"));
        Assert.Equal("Maps", VariantNormalizer.AssetTypeOf("Maps/Forest"));
        Assert.Equal("Maps", VariantNormalizer.AssetTypeOf(@"Maps\Custom_ForestWest"));
        Assert.Equal("Mods/*", VariantNormalizer.AssetTypeOf("Mods/SVE/Little Shrub"));
        Assert.Equal("Portraits", VariantNormalizer.AssetTypeOf("Portraits/Abigail"));
        Assert.Equal("Characters", VariantNormalizer.AssetTypeOf("Characters/Abigail"));
        // 对话资产是 $query 宿主，落点与立绘完全不同 —— 不能被 characters/ 吞掉
        Assert.Equal("Characters/Dialogue", VariantNormalizer.AssetTypeOf("Characters/Dialogue/Abigail"));
    }

    [Fact]
    public void 事件补丁归到事件资产类型()
    {
        PatchVerdict v = Verdict("EditData", "Data/Events/Town", """{ "HasSeenEvent": "123" }""");

        EmitterCell cell = Assert.Single(VariantNormalizer.Run(new[] { v }).Cells);

        Assert.Equal("Data/Events", cell.AssetType);
    }

    [Fact]
    public void 空输入不产生NaN()
    {
        NormalizationReport r = VariantNormalizer.Run(Array.Empty<PatchVerdict>());

        Assert.Equal(0, r.DimensionPatches);
        Assert.Equal(0, r.NativeRatio);
    }

    /// <summary>
    /// 图片按季节换图的结论**与资产名前缀无关**（按消费方判），三格必须一致。
    /// <para>
    /// 回归护栏：原先表里只写了 <c>Mods/*</c>，于是 <c>LooseSprites/*</c> / <c>Tilesheets/*</c>
    /// 的 12 条落进 <see cref="EmitKind.Unknown"/>（"待查"）—— **表漏条目会把「已查清」误报成「没查」**。
    /// </para>
    /// </summary>
    [Fact]
    public void 图片季节换图三格共用同一个结论()
    {
        EmitPlan mods = EmitterTable.Lookup(VariantCarrier.Sprite, VariantDimension.Season, "Mods/*");
        Assert.Equal(EmitKind.Native, mods.Kind);
        Assert.Equal(mods, EmitterTable.Lookup(VariantCarrier.Sprite, VariantDimension.Season, "LooseSprites/*"));
        Assert.Equal(mods, EmitterTable.Lookup(VariantCarrier.Sprite, VariantDimension.Season, "Tilesheets/*"));
    }

    /// <summary>自定义资产（`Mods/*`）的**数据条目**由注册它的 mod 的 C# 消费，原版不认识 —— 无原生条件位。</summary>
    [Theory]
    [InlineData(VariantDimension.SeenEvent)]
    [InlineData(VariantDimension.Marriage)]
    public void 自定义资产的数据条目无原生条件位(VariantDimension dim)
    {
        Assert.Equal(EmitKind.None, EmitterTable.Lookup(VariantCarrier.Entry, dim, "Mods/*").Kind);
    }

    /// <summary>
    /// 模型里**根本没有条件字段**的三个资产 → <see cref="EmitKind.None"/> 是**结论**，不是「没查」。
    /// 依据是模型类本身（`D:\sdv_gd`），逐个字段读过。
    /// </summary>
    [Theory]
    [InlineData(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/BigCraftables")]
    [InlineData(VariantCarrier.Entry, VariantDimension.SeenEvent, "Data/MoviesReactions")]
    [InlineData(VariantCarrier.Entry, VariantDimension.Marriage, "Data/animationDescriptions")]
    public void 模型无条件字段的资产是None不是未查(VariantCarrier c, VariantDimension d, string a)
    {
        Assert.Equal(EmitKind.None, EmitterTable.Lookup(c, d, a).Kind);
    }

    // ── ★ 实际发射的收敛量（与「按发射表推」分开）──────────────────────────

    /// <summary>
    /// <b>「推的收敛」与「做到的收敛」必须分开。</b>
    /// 发射表只说「这一格原版有没有机制」，发射还有前提（消费方唯一、索引对齐…）。
    /// 把推的当成的报就是虚报。
    /// </summary>
    [Fact]
    public void 实际发射与按表推断分开统计()
    {
        var verdicts = new List<PatchVerdict>
        {
            Verdict("EditImage", "Mods/P/Shrub", """{ "Season": "Spring" }"""),
            Verdict("EditImage", "Mods/P/Shrub", """{ "Season": "Summer" }"""),
            Verdict("EditImage", "Mods/P/Shrub", """{ "Season": "Fall" }"""),
            Verdict("EditImage", "Mods/P/Shrub", """{ "Season": "Winter" }"""),
        };
        NormalizationReport rep = VariantNormalizer.Run(verdicts);

        // 识别出了一个 4 条的意图；**还没发射**，所以实际收敛量是 0。
        VariantIntent intent = Assert.Single(rep.Intents);
        Assert.Equal(4, intent.PatchCount);
        Assert.Equal(0, rep.EmittedPatches);
        Assert.Equal(0, rep.RejectedPatches);
        Assert.Equal("", intent.EmittedAs);

        rep.MarkEmitted("Mods/P/Shrub", VariantCarrier.Sprite, VariantDimension.Season,
            "四季 BigCraftable 索引", emitted: true, patches: 4);

        Assert.Equal(1, rep.EmittedIntents);
        Assert.Equal(4, rep.EmittedPatches);
        Assert.Equal("四季 BigCraftable 索引", rep.Intents[0].EmittedAs);
    }

    /// <summary>被拒的组要**如实留在原判**里，并单独计数 —— 不能并进「没发射」或「发射了」。</summary>
    [Fact]
    public void 被拒的组单独计数()
    {
        var verdicts = new List<PatchVerdict>
        {
            Verdict("EditImage", "Mods/P/Bush", """{ "Season": "Spring" }"""),
            Verdict("EditImage", "Mods/P/Bush", """{ "Season": "Summer" }"""),
        };
        NormalizationReport rep = VariantNormalizer.Run(verdicts);
        rep.MarkEmitted("Mods/P/Bush", VariantCarrier.Sprite, VariantDimension.Season,
            "", emitted: false, patches: 2);

        Assert.Equal(0, rep.EmittedIntents);
        Assert.Equal(1, rep.RejectedIntents);
        Assert.Equal(2, rep.RejectedPatches);
        Assert.Equal("", rep.Intents[0].EmittedAs);
    }

    /// <summary>资产名里的反斜杠/大小写要与意图对齐 —— 否则标记落空，收敛量静默变成 0。</summary>
    [Fact]
    public void 回填时资产名归一()
    {
        var verdicts = new List<PatchVerdict>
        {
            Verdict("EditImage", "Mods/P/Shrub", """{ "Season": "Spring" }"""),
            Verdict("EditImage", "Mods/P/Shrub", """{ "Season": "Summer" }"""),
        };
        NormalizationReport rep = VariantNormalizer.Run(verdicts);

        rep.MarkEmitted(@"mods\p\shrub", VariantCarrier.Sprite, VariantDimension.Season, "X", true, 2);

        Assert.Equal(1, rep.EmittedIntents);
    }
}
