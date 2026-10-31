using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Workspace;
using SdvCpTool.Core.Conditions;
using XnbConverter.Tbin.Entity;
using XnbConverter.Tbin.Readers;

namespace SdvCpTool.Core.Tests;

public class PatchJudgeTests : IDisposable
{
    private readonly string _packDir = Path.Combine(Path.GetTempPath(), "sdvjpk_" + Guid.NewGuid().ToString("N"));
    private readonly string _treeDir = Path.Combine(Path.GetTempPath(), "sdvjtr_" + Guid.NewGuid().ToString("N"));

    public PatchJudgeTests()
    {
        Directory.CreateDirectory(_packDir);
        Directory.CreateDirectory(_treeDir);
    }

    public void Dispose()
    {
        Directory.Delete(_packDir, recursive: true);
        Directory.Delete(_treeDir, recursive: true);
    }

    private void Tree(string relative, string content)
    {
        string full = Path.Combine(_treeDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void PackFile(string relative, string content)
    {
        string full = Path.Combine(_packDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>写一张最小的 4×4 地图；<paramref name="withWarp"/> 决定 0,0 是不是传送入口。</summary>
    private void Map(string relative, bool withWarp)
    {
        var tiles = new List<BaseTile>();
        for (int i = 0; i < 16; i++)
            tiles.Add(new StaticTile
            {
                TileSheet = "",
                TileIndex = 0,
                Properties = new List<Propertie>(),
            });
        var map = new TBin10
        {
            Format = "tBIN10",
            Id = "",
            Description = "",
            Properties = new List<Propertie>(),
            TileSheets = new List<TileSheet>(),
            Layers = new List<Layer>
            {
                new()
                {
                    Id = "Back",
                    Visible = 1,
                    Description = "",
                    LayerSize = new XnbConverter.Entity.Mono.IntVector2 { X = 4, Y = 4 },
                    TileSize = new XnbConverter.Entity.Mono.IntVector2 { X = 16, Y = 16 },
                    Properties = new List<Propertie>(),
                    Tiles = tiles,
                    Index = Enumerable.Repeat('S', 16).ToList(),
                    _sizeArr = new List<int>(),
                    _currTileSheet = new List<string> { "" },
                },
            },
        };
        if (withWarp)
            map.Properties.Add(new Propertie { Key = "Warp", Type = 3, Value = "0 0 Town 1 1" });

        string full = Path.Combine(_treeDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        TBin10Reader.Create(Array.Empty<byte>()).Save(map, full);
    }

    private PatchVerdict Judge(string patchJson, VanillaAssetIndex? vanilla = null)
    {
        var raw = JObject.Parse(patchJson);
        var patch = new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            FromFile = raw["FromFile"]?.Value<string>(),
            Update = raw["Update"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = Path.Combine(_packDir, "content.json")
        };
        return new PatchJudge(new ResourceTree(_treeDir), _packDir, vanilla).Judge(patch);
    }

    /// <summary>带叶子求值器（常量折叠）的判定，用于验证「折叠发生在判定之前」。</summary>
    private PatchVerdict JudgeWithFold(string patchJson, Func<WhenNode, bool?> evaluateLeaf)
    {
        var raw = JObject.Parse(patchJson);
        var patch = new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            FromFile = raw["FromFile"]?.Value<string>(),
            Update = raw["Update"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = Path.Combine(_packDir, "content.json")
        };
        return new PatchJudge(new ResourceTree(_treeDir), _packDir, null, evaluateLeaf).Judge(patch);
    }

    /// <summary>按 KeyName 查表的叶子求值器；不在表里 = 求不了值（null）。</summary>
    private static Func<WhenNode, bool?> LeafEval(params (string Key, bool Value)[] known)
    {
        var map = known.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        return n => map.TryGetValue(n.KeyName ?? "", out bool v) ? v : null;
    }

    private VanillaAssetIndex WriteVanillaHashes(params string[] assetNames)
    {
        string path = Path.Combine(_packDir, "ContentHashes.json");
        var obj = new JObject();
        foreach (string n in assetNames) obj[n + ".xnb"] = "hash";
        File.WriteAllText(path, obj.ToString());
        return new VanillaAssetIndex(path);
    }

    [Fact]
    public void 无条件的数据修改是ConvertedNoCondition()
    {
        Tree("Data/Objects.json", """{ "a": "b" }""");

        var v = Judge("""{ "Action": "EditData", "Target": "Data/Objects" }""");

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    /// <summary>
    /// ★ <b>判定必须与写入用同一个翻译器</b>：判据只留一处，不能「按名字」另写一张表。
    /// <para>
    /// 实测漏过三种（都表现为「判定说 <c>Converted</c>、写入时翻译失败」）：
    /// <list type="bullet">
    /// <item>令牌嵌在**值**里：<c>"Hearts:Sophia": "{{Range: 6, 10}}"</c>
    ///   —— 旧判据只查了 <c>Arguments</c>，没查 <c>Value</c>；</item>
    /// <item><c>Weather: island</c> —— GSQ 的第一参只认 Here/Target/地点名，表达不了 island/valley；</item>
    /// <item>值里还有别的不可译写法。</item>
    /// </list>
    /// </para>
    /// </summary>
    [Fact]
    public void 值里嵌令牌时判定与写入一致地拒绝()
    {
        Tree("Data/Events/seedshop.json", """{ "1/f Sophia 250": "script" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Events/seedshop",
          "Entries": { "9/t 600 1200": "x" },
          "When": { "Hearts:Sophia": "{{Range: 6, 10}}", "HasSeenEvent |contains=191393": true } }
        """);

        // 关键：**不能**是 Converted —— 写入器用同一个翻译器，它必然失败。
        Assert.NotEqual(Verdict.Converted, v.Verdict);
        Assert.Contains("翻译不成 GSQ", v.Reason);
    }

    /// <summary>
    /// ★ **落点不认识**那一格不能**断言**「条件可翻译」—— 那是没查过就下的结论。
    /// 有 blocker 就照实说，否则读报告的人会以为只剩「落点」一个问题。
    /// </summary>
    [Fact]
    public void 结构不认识时也照实说条件翻不了()
    {
        // 结构认不出来的资产（不是全字符串、也没有已知条件字段）⇒ 落点 Unknown
        Tree("Data/Weird.json", """{ "a": { "B": 1 } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Weird",
          "Entries": { "a": { "B": 2 } },
          "When": { "Hearts:Abigail": "{{Range: 1, 2}}" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
        Assert.Contains("翻译不成 GSQ", v.Reason);
        Assert.DoesNotContain("条件可翻译", v.Reason);
    }

    /// <summary>
    /// ★ 对话资产的条件**本身就翻不了**时，理由必须说**真正拦住它的那一条**，
    /// 不能退回「对话键只支持季节/地点/好感/topic 等受限语法」—— 那是**键语法**，
    /// 而这里真正的拦阻是「条件的**值**里嵌了令牌，翻译不成 GSQ」。
    /// <para>实测语料 23 条属于这一类（`"Hearts:Shane": "{{Range: 9, 14}}"` 这种写法）。</para>
    /// </summary>
    [Fact]
    public void 对话键的条件翻不了时说的是真原因()
    {
        Tree("Characters/Dialogue/Shane.json", """{ "Mon": "hi" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/Dialogue/Shane",
          "Entries": { "AcceptGift_(O)74": "新文案" },
          "When": { "Hearts:Shane": "{{Range: 9, 14}}" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
        Assert.Contains("翻译不成 GSQ", v.Reason);
        Assert.DoesNotContain("受限语法", v.Reason);
    }

    /// <summary>`Weather` 带地点上下文同理：老判据按名字放行，写入却翻不出来。</summary>
    [Fact]
    public void 天气带地点上下文时判定与写入一致地拒绝()
    {
        Tree("Data/Locations.json", """{ "Town": { "Fish": [ { "Condition": "x" } ] } }""");

        // 落点用「空操作」那种形态（判据只看条件本身），避免落点问题先一步拦下、盖住要测的这一条。
        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Weather: island": "Sun" } }
        """);

        Assert.NotEqual(Verdict.Converted, v.Verdict);
        Assert.Contains("翻译不成 GSQ", v.Reason);
    }

    /// <summary>
    /// ★ 没有 <c>Entries</c>/<c>Fields</c> 的补丁 = **空操作**（CP 下同样什么都不做）——
    /// <b>写入器不能失败</b>。
    /// <para>
    /// 原先 <c>PatchApplier</c> 对「算不出条件落点」直接 <c>Fail</c>，于是这类补丁
    /// 判定说 <c>Converted</c>、写入却失败，报告里表现为**写入失败**，
    /// 正是 <c>ConditionWriter.RefusalFor</c> 注释里点名要避免的
    /// 「把判定不准伪装成写入出问题」（实测语料 2 条）。
    /// 这条测试把「写入器对空操作不失败」钉住，与 <c>RefusalFor</c> 的口径绑在一起。
    /// </para>
    /// </summary>
    [Fact]
    public void 空操作补丁写入不失败()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "SEASON spring" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);

        // 判定说能写，写入就必须能写 —— 两处口径由同一个 RefusalFor 保证。
        Assert.Null(ConditionWriter.RefusalFor(
            JObject.Parse("""{ "Town": { "Forage": [ { "Condition": "x" } ] } }"""),
            JObject.Parse("""{ "Action": "EditData", "Target": "Data/Locations" }"""),
            LandingKind.EntryConditionField, "Data/Locations"));
    }

    [Fact]
    public void 条件可翻译且落点是Condition字段()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "SEASON spring" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(LandingKind.EntryConditionField.ToString(), v.SuggestedChannel);
    }

    [Fact]
    public void HasFlag条件可翻译且落点是Condition字段()
    {
        // Task 10k-1：CP 的 HasFlag 不是私有存储 —— TokenSaveReader.GetFlags 返回
        // mailReceived ∪ mailForTomorrow ∪ mailbox ∪ worldStateIDs，等价于 GSQ 的 PLAYER_HAS_MAIL；
        // 第 3 参必须走**缺省的 any**（= hasOrWillReceiveMail），与并集语义一致 ——
        // 绝不能写 Received（那是 HasReadLetter 的语义）。这条是回归护栏：两者不得串味。
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "SEASON spring" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "HasFlag: hostPlayer |contains=GH1letter": true } }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(LandingKind.EntryConditionField.ToString(), v.SuggestedChannel);

        string gsq = GsqTranslator.Translate(WhenParser.Parse(v.Patch.When)).Gsq!;
        Assert.Equal("PLAYER_HAS_MAIL Host GH1letter Any", gsq);
        Assert.DoesNotContain("Received", gsq);

        // 该写 Received 的是 HasReadLetter —— 也不能被"顺手统一"成 any。
        Assert.Equal("PLAYER_HAS_MAIL Current x Received",
            GsqTranslator.Translate(WhenParser.Parse(JObject.Parse("""{"HasReadLetter": "x"}"""))).Gsq);
    }

    [Fact]
    public void 条件可翻译且落点是事件键()
    {
        Tree("Data/Events/Town.json", """{ "100/t 600 1200": "continue/64 15/farmer 1 1 1" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Events/Town",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(LandingKind.EntryKeyConditions.ToString(), v.SuggestedChannel);
    }

    [Fact]
    public void 键维度认不出时是NeedsReview而不是NoLandingPoint()
    {
        // 字符串字典，但键既不是事件键、也不含已确证维度 —— 我们不知道有没有键维度，
        // 于是**不能**断言"无处承载条件"。必须是 NeedsReview（Unknown），不是 NoLandingPoint。
        Tree("Data/Festivals/spring13.json", """{ "name": "Egg Festival", "conditions": "Town/900 1400" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Festivals/spring13",
          "When": { "HasSeenEvent": "123" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
        Assert.Equal(LandingKind.Unknown, v.Landing);
    }

    [Fact]
    public void 条件被键名蕴含时转成原生键写入()
    {
        Tree("Data/mail.json", """{ "spring_13": "原版信", "fall_5": "秋信" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/mail",
          "Entries": { "spring_13": "新信" },
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.NativeKey, v.Channel);
        Assert.True(SdvCpTool.Core.Merging.WritePolicy.ShouldWrite(v));
    }

    [Fact]
    public void 键维度在但条件不被蕴含时是NeedsReview()
    {
        // 键 spring_13 命中了季节维度（落点 KeyEncodedVariant），但条件要的是 fall ——
        // 键名蕴含不了该条件，不转换，交人工。
        Tree("Data/mail.json", """{ "spring_13": "原版信" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/mail",
          "Entries": { "spring_13": "新信" },
          "When": { "Season": "fall" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
        Assert.Equal(LandingKind.KeyEncodedVariant, v.Landing);
        Assert.False(SdvCpTool.Core.Merging.WritePolicy.ShouldWrite(v));
    }

    [Fact]
    public void 含HasMod时走烘焙通道()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "HasMod": "|contains=Foo.Bar" } }
        """);

        Assert.Equal(Channel.Bake, v.Channel);
        Assert.NotEqual(Verdict.NonTranslatableCondition, v.Verdict);
    }

    [Fact]
    public void 含自定义mod令牌时走烘焙通道()
    {
        // 命名空间令牌的值取自注册它的 mod 的 config.json，与 HasMod/HasFile 同属
        // 「读 Mods/ 即可离线求值」，因此应并入通道 C，而不是在判定阶段被短路。
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals": true } }
        """);

        Assert.Equal(Channel.Bake, v.Channel);
        Assert.NotEqual(Verdict.NonTranslatableCondition, v.Verdict);
    }

    [Fact]
    public void 自定义mod令牌与季节混合时仍不是Converted()
    {
        // 本任务只开闸门、不做常量折叠：混合条件放行后仍会走 Bake，但求值阶段
        // 因含无法静态翻译的 Season 叶子而失败，绝不能静默判成 Converted。
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring", "Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals": true } }
        """);

        Assert.NotEqual(Verdict.Converted, v.Verdict);
    }

    [Fact]
    public void 含HasFile时仍走烘焙通道()
    {
        // 回归保护：并入命名空间令牌不得改变 HasMod/HasFile 的原判定行为。
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "HasFile": "assets/foo.json" } }
        """);

        Assert.Equal(Channel.Bake, v.Channel);
        Assert.NotEqual(Verdict.NonTranslatableCondition, v.Verdict);
    }

    [Fact]
    public void 无条件补丁的Update不阻塞()
    {
        // CP 的 Update 默认就是 OnDayStart，且换日时一律重算（PatchLoader.cs:130-151 /
        // PatchManager.cs:519-538）—— 无条件补丁没有条件要重算，Update 与判定无关，
        // 不得再无条件判 NeedsRuntime。
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town", "Update": "OnLocationChange" }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void Update为OnDayStart视同默认不阻塞()
    {
        // OnDayStart 是 CP 的默认值，等同于「没写 Update」—— 落点可写就该判 Converted。
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "SEASON spring" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring" }, "Update": "OnDayStart" }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(LandingKind.EntryConditionField.ToString(), v.SuggestedChannel);
    }

    [Fact]
    public void 非默认Update在GSQ落点上仍是NeedsRuntime()
    {
        // 非默认值只是"额外再多算几个时刻"，但写进原生 GSQ 落点的条件由游戏在读取该资产时
        // 求值，表达不了这些时刻 —— 这一处仍需阻塞（Update 判据已下移到落点探测之后）。
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "SEASON spring" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring" }, "Update": "OnTimeChange" }
        """);

        Assert.Equal(Verdict.NeedsRuntime, v.Verdict);
    }

    [Fact]
    public void 非默认Update不遮蔽落点判定()
    {
        // Update 判据不再抢在落点探测之前：目标没有可承载条件的落点时，如实报 NoLandingPoint。
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Maps/Town", "Update": "OnLocationChange",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
    }

    /// <summary>
    /// 无落点的理由必须**说对是哪一类**：图片/地图资产和空 JSON 是两回事，处置方式也不同。
    /// <para>
    /// 回归护栏：旧文案对所有情形都印「如 Data/Festivals 类资产名写死」，而实测
    /// <see cref="Verdict.NoLandingPoint"/> 的**绝大多数是图片资产的条件 EditImage** ——
    /// 那句话与它们毫无关系，会把读报告的人引到错的方向。
    /// </para>
    /// </summary>
    [Fact]
    public void 图片资产的无落点理由说图片()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Maps/Town",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("地图（tbin）", v.Reason);
        Assert.DoesNotContain("Data/Festivals", v.Reason);
    }

    /// <summary>
    /// <b>图片的条件改图不是「没有机制」</b> —— 引擎里真有，本工具也已实现两条
    /// （四季 BigCraftable 索引、图集文件名季节前缀）。拒绝的真实理由是
    /// **消费方确认不了**，理由必须这么说，不能笼统印「图片没有条件位」——
    /// 那与我们已经做出来的东西自相矛盾。
    /// </summary>
    [Fact]
    public void 图片资产的无落点理由要说清是消费方确认不了()
    {
        Tree("LooseSprites/Cursors.png", "x");

        var v = Judge("""
        { "Action": "EditImage", "Target": "LooseSprites/Cursors",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("确认消费方", v.Reason);
        Assert.DoesNotContain("没有条件位", v.Reason);
    }

    [Fact]
    public void 空JSON资产的无落点理由说空JSON()
    {
        Tree("Data/Empty.json", "{}");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Empty",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("空 JSON", v.Reason);
        Assert.DoesNotContain("Data/Festivals", v.Reason);
    }

    /// <summary>
    /// 顶层是**数组**的资产（`Data/MoviesReactions` 是 82KB 的数组）不能被说成「空 JSON」——
    /// 「不是 JObject」不等于「空」，那也是说错。
    /// </summary>
    [Fact]
    public void 列表资产的无落点理由说列表()
    {
        Tree("Data/MoviesReactions.json", """[ { "NPCName": "Penny", "Reactions": [] } ]""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/MoviesReactions",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("列表资产", v.Reason);
        Assert.DoesNotContain("空 JSON", v.Reason);
    }

    [Fact]
    public void 非默认Update不阻塞原生键改写()
    {
        // 原生键（键即条件）由游戏按当前状态自行读取，天然在运行时求值，Update 不构成阻塞。
        Tree("Data/mail.json", """{ "spring_13": "原版信" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/mail",
          "Entries": { "spring_13": "新信" },
          "When": { "Season": "spring" }, "Update": "OnTimeChange" }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.NativeKey, v.Channel);
    }

    /// <summary>
    /// ★ `FarmType: X` 而目标就是 `Maps/Farm_X` ⇒ 条件**由目标地图自证**（那张图只会在玩家选了 X 农场时
    /// 被加载）⇒ 按无条件改 tbin。⚠ 只对同一张图成立：改的是别的地图（如 `Maps/Town`）时不能这么算，
    /// 那些地图与农场类型无关 —— 走**烘焙通道**按确定化后的农场类型求值（Standard 为真、其余为假）。
    /// </summary>
    [Fact]
    public void 目标就是该农场类型的图时FarmType条件自证成立()
    {
        Tree("Maps/Farm_FrontierFarm.tbin", "x");
        Tree("Maps/Town.tbin", "x");

        var ok = Judge("""
        { "Action": "EditMap", "Target": "Maps/Farm_FrontierFarm",
          "When": { "FarmType": "FrontierFarm" } }
        """);
        Assert.Equal(Verdict.ConvertedNoCondition, ok.Verdict);

        var other = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "When": { "FarmType": "FrontierFarm" } }
        """);
        Assert.Equal(Verdict.NeedsReview, other.Verdict);
        Assert.Equal(Channel.Bake, other.Channel);
    }

    [Fact]
    public void EditMap带条件是LocalEdit()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "When": { "Time": "600 1900" } }
        """);

        Assert.Equal(Verdict.LocalEdit, v.Verdict);
    }

    /// <summary>
    /// `EditMap` 的 `FromFile` + 纯雨天条件 —— 原版 DayTiles/NightTiles 能近似，
    /// 不再整条判 LocalEdit。
    /// </summary>
    [Fact]
    public void 雨天FromFile叠加层走DayNightTiles()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "FromFile": "assets/mist.tbin",
          "ToArea": { "X": 0, "Y": 0, "Width": 2, "Height": 2 },
          "When": { "Weather": "Rain, Storm" } }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
        Assert.Equal(Channel.DayNightTiles, v.Channel);
        Assert.Contains("近似", v.Reason);
    }

    /// <summary>
    /// 纯夜间时间区间也能落到 NightTiles；白天区间不接（否则雨天会走反方向）。
    /// </summary>
    [Fact]
    public void 纯夜间时间叠加层走DayNightTiles()
    {
        Tree("Maps/Town.tbin", "x");

        var night = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "FromFile": "assets/mist.tbin",
          "When": { "Time": "{{Range: 1900, 2500}}" } }
        """);
        Assert.Equal(Verdict.ConvertedNoCondition, night.Verdict);
        Assert.Equal(Channel.DayNightTiles, night.Channel);

        var day = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "FromFile": "assets/mist.tbin",
          "When": { "Time": "600 1900" } }
        """);
        Assert.Equal(Verdict.ConvertedNoCondition, day.Verdict);
        Assert.Equal(Channel.DayNightTiles, day.Channel);
        Assert.True(day.DayNightVisibleAtDay);
    }

    [Fact]
    public void 绿雨也能走NightTiles()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "FromFile": "assets/mist.tbin",
          "When": { "Weather": "GreenRain" } }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
        Assert.Equal(Channel.DayNightTiles, v.Channel);
    }

    [Fact]
    public void 白天时间加晴天天气走Day模式白天加雨天不走()
    {
        Tree("Maps/Town.tbin", "x");

        var sun = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "FromFile": "assets/mist.tbin",
          "When": { "Time": "600 1850", "Weather": "Sun, Wind" } }
        """);
        Assert.Equal(Verdict.ConvertedNoCondition, sun.Verdict);
        Assert.Equal(Channel.DayNightTiles, sun.Channel);
        Assert.True(sun.DayNightVisibleAtDay);

        var rain = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "FromFile": "assets/mist.tbin",
          "When": { "Time": "600 1850", "Weather": "Rain" } }
        """);
        Assert.Equal(Verdict.LocalEdit, rain.Verdict);
        Assert.False(rain.DayNightVisibleAtDay);
    }

    [Fact]
    public void 纯AmbientLight昼夜条件走地图环境光通道()
    {
        Tree("Maps/Town.tbin", "x");

        var day = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "MapProperties": { "AmbientLight": "255 255 255" },
          "When": { "Time": "600 1900" } }
        """);
        Assert.Equal(Verdict.ConvertedNoCondition, day.Verdict);
        Assert.Equal(Channel.DayNightAmbient, day.Channel);
        Assert.True(day.DayNightVisibleAtDay);

        var night = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "MapProperties": { "AmbientLight": "110 110 110" },
          "When": { "Time": "1900 2600" } }
        """);
        Assert.Equal(Verdict.ConvertedNoCondition, night.Verdict);
        Assert.Equal(Channel.DayNightAmbient, night.Channel);
        Assert.False(night.DayNightVisibleAtDay);
    }

    [Fact]
    public void 非雨天天气不能走DayNightTiles()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "FromFile": "assets/mist.tbin",
          "When": { "Weather": "Sunny" } }
        """);

        Assert.Equal(Verdict.LocalEdit, v.Verdict);
    }

    [Fact]
    public void 非雨天条件不能因为只看叶子而误判()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "FromFile": "assets/mist.tbin",
          "When": { "NOT": { "Weather": "Rain" } } }
        """);

        Assert.Equal(Verdict.LocalEdit, v.Verdict);
    }

    [Fact]
    public void 雨天叠加层带其它地图改动时仍不走DayNightTiles()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "FromFile": "assets/mist.tbin",
          "MapProperties": { "Message": "x" },
          "When": { "Weather": "Rain" } }
        """);

        Assert.Equal(Verdict.LocalEdit, v.Verdict);
    }

    [Fact]
    public void 条件化牌子文字先近似无条件写入()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "When": { "Time": "600 1900" },
          "TextOperations": [ { "Operation": "Append", "Target": [ "MapProperties", "Message" ], "Value": "x" } ] }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
        Assert.Contains("近似", v.Reason);
        Assert.Null(v.DoorGateGsq);
    }

    /// <summary>局部改动 + 持久型条件 ⇒ 门禁通道（GSQ 交给写入侧）。</summary>
    [Fact]
    public void 局部条件化地图改动走门禁通道()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "When": { "HasFlag": "communityUpgradeShortcuts" },
          "MapTiles": [ { "Position": "1,1" } ] }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
        Assert.Equal("PLAYER_HAS_MAIL Current communityUpgradeShortcuts Any", v.DoorGateGsq);
    }

    /// <summary>
    /// 改动落在**开放地面/装饰**上时不写门禁：那会在广场/灯柱上留下看不见的锁。
    /// 实测 SVE 在酒吧前石板 + Joja 灯柱上都有这种情况。
    /// </summary>
    [Fact]
    public void 条件化地图改动落在开放地面时不落门禁()
    {
        Map("Maps/Plaza.tbin", withWarp: false);

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Plaza",
          "When": { "HasFlag": "communityUpgradeShortcuts" },
          "MapTiles": [ { "Position": "1,1" } ] }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
        Assert.Null(v.DoorGateGsq);
        Assert.Contains("开放地面", v.Reason);
    }

    [Fact]
    public void 条件化地图改动落在传送格上仍落门禁()
    {
        Map("Maps/Gate.tbin", withWarp: true);

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Gate",
          "When": { "HasFlag": "communityUpgradeShortcuts" },
          "MapTiles": [ { "Position": "0,0" } ] }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
        Assert.Equal("PLAYER_HAS_MAIL Current communityUpgradeShortcuts Any", v.DoorGateGsq);
    }

    /// <summary>
    /// ★ 配偶房间地图**不设门禁**：婚房是由 `ApplyMapOverride` 复制进农舍的，
    /// `TouchAction=ConditionalDoor` 会被一起复制（`FarmHouse.cs:1988-2065`）。
    /// </summary>
    [Fact]
    public void 配偶房间地图不落门禁()
    {
        Tree("Data/Characters.json", """
        { "TestNPC": { "SpouseRoom": { "MapAsset": "Custom_Room" } } }
        """);
        Tree("Maps/Custom_Room.tbin", "x");
        Tree("Maps/spouseRooms.tbin", "x");

        var custom = Judge("""
        { "Action": "EditMap", "Target": "Maps/Custom_Room",
          "When": { "HasFlag": "communityUpgradeShortcuts" },
          "ToArea": { "X": 0, "Y": 0, "Width": 3, "Height": 3 } }
        """);
        Assert.Equal(Verdict.ConvertedNoCondition, custom.Verdict);
        Assert.Null(custom.DoorGateGsq);
        Assert.Contains("配偶房间", custom.Reason);

        var shared = Judge("""
        { "Action": "EditMap", "Target": "Maps/spouseRooms",
          "When": { "HasFlag": "communityUpgradeShortcuts" },
          "ToArea": { "X": 0, "Y": 0, "Width": 3, "Height": 3 } }
        """);
        Assert.Equal(Verdict.ConvertedNoCondition, shared.Verdict);
        Assert.Null(shared.DoorGateGsq);
    }

    /// <summary>
    /// ★ **面积上限**：整片变体不能走门禁 —— 逐格弹回会把原有通路一起挡住（整张图走不了）。
    /// 实测 Frontier Farm 的 `Custom_FerngillRepublicFrontier` 有 24336 格的整图变体。
    /// </summary>
    [Fact]
    public void 大范围条件化地图改动_初始不成立时不写()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "When": { "HasFlag": "communityUpgradeShortcuts" },
          "ToArea": { "X": 0, "Y": 0, "Width": 100, "Height": 100 } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
        Assert.Null(v.DoorGateGsq);
        Assert.Contains("不提前烘焙", v.Reason);
    }

    /// <summary>大范围但条件初始就成立（这里用 `|contains=...: false` 的取反）⇒ 可以按初始状态写。</summary>
    [Fact]
    public void 大范围条件化地图改动_初始成立时按无条件写()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""
        { "Action": "EditMap", "Target": "Maps/Town",
          "When": { "HasFlag |contains=communityUpgradeShortcuts": "false" },
          "ToArea": { "X": 0, "Y": 0, "Width": 100, "Height": 100 } }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
        Assert.Null(v.DoorGateGsq);
        Assert.Contains("初始存档就成立", v.Reason);
    }

    [Fact]
    public void 目标不在树里是TargetMissing()
    {
        // 原版资产索引里确认是原版资产，但资源树中没有 —— 这才是真正的 TargetMissing
        var vanilla = WriteVanillaHashes("Data/NoSuchAsset");

        var v = Judge("""{ "Action": "EditData", "Target": "Data/NoSuchAsset" }""", vanilla);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
    }

    [Fact]
    public void 未知Action是UnsupportedAction()
    {
        var v = Judge("""{ "Action": "Frobnicate", "Target": "Data/Objects" }""");

        Assert.Equal(Verdict.UnsupportedAction, v.Verdict);
    }

    [Fact]
    public void Load的FromFile存在时是Converted()
    {
        Tree("Characters/New.png", "PNG");
        PackFile("Assets/a.png", "PNG");

        var v = Judge("""
        { "Action": "Load", "Target": "Characters/New",
          "FromFile": "Assets/a.png" }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
    }

    [Fact]
    public void Load的FromFile缺失是TargetMissing()
    {
        var v = Judge("""
        { "Action": "Load", "Target": "Characters/New",
          "FromFile": "Assets/nope.png" }
        """);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
    }

    // ---- Task 10b：判定与写入必须用同一套 FromFile 字段引用令牌展开 ----

    [Fact]
    public void Load的FromFile含TargetWithoutPath时按目标展开后判定为Converted()
    {
        // 真实数据里的形态：Target=LooseSprites/RSVNinjaBoard，
        // FromFile=Assets/LooseSprites/{{TargetWithoutPath}}.png。
        // 修复前判定拿原始字符串去查文件存在性，误判 TargetMissing（实测 101 条）。
        PackFile("Assets/LooseSprites/RSVNinjaBoard.png", "PNG");

        var v = Judge("""
        { "Action": "Load", "Target": "LooseSprites/RSVNinjaBoard",
          "FromFile": "Assets/LooseSprites/{{TargetWithoutPath}}.png" }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
    }

    [Fact]
    public void Load的FromFile含TargetPathOnly时按目标展开后判定为Converted()
    {
        PackFile("Assets/LooseSprites/Board.png", "PNG");

        var v = Judge("""
        { "Action": "Load", "Target": "LooseSprites/Board",
          "FromFile": "Assets/{{TargetPathOnly}}/Board.png" }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
    }

    [Fact]
    public void Load逗号多目标每个展开结果都在时是Converted()
    {
        PackFile("Assets/Characters/RSVTourists/Guest1.png", "PNG");
        PackFile("Assets/Characters/RSVTourists/Guest2.png", "PNG");

        var v = Judge("""
        { "Action": "Load",
          "Target": "Characters/RSVTourists/Guest1, Characters/RSVTourists/Guest2",
          "FromFile": "Assets/Characters/RSVTourists/{{TargetWithoutPath}}.png" }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
    }

    [Fact]
    public void Load逗号多目标里有一个展开结果缺失时是TargetMissing()
    {
        // 部分成功不能放过：与 CP 一致，多目标中任一目标取不到文件，整条报错。
        PackFile("Assets/Characters/RSVTourists/Guest1.png", "PNG");

        var v = Judge("""
        { "Action": "Load",
          "Target": "Characters/RSVTourists/Guest1, Characters/RSVTourists/Guest2",
          "FromFile": "Assets/Characters/RSVTourists/{{TargetWithoutPath}}.png" }
        """);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
        Assert.Contains("Guest2", v.Reason);
    }

    [Fact]
    public void Load的FromFile含Random令牌时仍是TargetMissing()
    {
        // 回归保护：{{Random}} 是运行时逐次随机，本任务不新增展开能力，仍判失败。
        PackFile("Assets/a.png", "PNG");

        var v = Judge("""
        { "Action": "Load", "Target": "Characters/New",
          "FromFile": "Assets/{{Random:1, 2, 3, 4}}.png" }
        """);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
    }

    [Fact]
    public void Load的FromFile含FirstValidFile令牌时仍是TargetMissing()
    {
        // 回归保护：{{FirstValidFile:...}} 依赖运行时状态，本任务不展开。
        PackFile("Assets/a.png", "PNG");

        var v = Judge("""
        { "Action": "Load", "Target": "Characters/New",
          "FromFile": "Assets/{{FirstValidFile: a.png, b.png}}/{{Language}}.json" }
        """);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
    }

    [Fact]
    public void 对话资产的落点是NeedsReview()
    {
        Tree("Characters/Dialogue/Aguar.json", """{ "aguar1_1": "你好" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/Dialogue/Aguar",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }

    [Fact]
    public void 无条件的地图修改是ConvertedNoCondition()
    {
        Tree("Maps/Town.tbin", "x");

        var v = Judge("""{ "Action": "EditMap", "Target": "Maps/Town" }""");

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void 无条件贴图修改是ConvertedNoCondition()
    {
        Tree("Characters/Abigail.png", "PNG");

        var v = Judge("""{ "Action": "EditImage", "Target": "Characters/Abigail" }""");

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void 不认识的令牌降级为NeedsReview()
    {
        // 落点没问题（Data/Locations 有 Condition 字段），但令牌我们不认识，
        // 无法确认它能不能翻译成 GSQ —— 绝不能当成 Converted。
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "NoSuchToken": "x" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }

    [Fact]
    public void 纯计算令牌降级为NeedsReview()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "HasValue": "x" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }

    [Fact]
    public void 参数里嵌套令牌时降级为NeedsReview()
    {
        // 真实数据里有 `Query: {{Day}} < 15` 这种把令牌嵌在**参数**里的写法。
        // 键名本身是可翻译的，只看 KeyName 会漏掉，必须扫 Arguments。
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "HasSeenEvent: {{Day}}": "123" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }

    [Fact]
    public void Load带条件时是NeedsReview()
    {
        // Load 走的是独立分支，这条分支之前完全没有测试覆盖
        Tree("Characters/New.png", "PNG");
        PackFile("Assets/a.png", "PNG");

        var v = Judge("""
        { "Action": "Load", "Target": "Characters/New",
          "FromFile": "Assets/a.png",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }

    [Fact]
    public void 逗号多目标全部命中时正常判定()
    {
        Tree("Maps/Farm.tbin", "x");
        Tree("Maps/Farm_Foraging.tbin", "x");

        var v = Judge("""{ "Action": "EditMap", "Target": "Maps/Farm, Maps/Farm_Foraging" }""");

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
    }

    [Fact]
    public void 逗号多目标部分缺失时是TargetMissing()
    {
        Tree("Maps/Farm.tbin", "x");

        var v = Judge("""{ "Action": "EditMap", "Target": "Maps/Farm, Maps/NoSuchMap" }""");

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
        Assert.Contains("NoSuchMap", v.Reason);
    }

    [Fact]
    public void Target含令牌时是TargetContainsTokens()
    {
        var v = Judge("""{ "Action": "EditData", "Target": "{{Some.Mod/Token}}" }""");

        Assert.Equal(Verdict.TargetContainsTokens, v.Verdict);
    }

    [Fact]
    public void 非原版资产缺失且是EditData时是CreatesNewAsset()
    {
        var vanilla = WriteVanillaHashes("Maps/Town", "Data/Locations");

        var v = Judge("""{ "Action": "EditData", "Target": "Data/Festivals/winter28" }""", vanilla);

        Assert.Equal(Verdict.CreatesNewAsset, v.Verdict);
    }

    [Fact]
    public void 原版资产缺失时仍是TargetMissing()
    {
        var vanilla = WriteVanillaHashes("Maps/Town", "Data/Locations");

        var v = Judge("""{ "Action": "EditData", "Target": "Data/Locations" }""", vanilla);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
    }

    [Fact]
    public void EditMap缺失时不是CreatesNewAsset()
    {
        var vanilla = WriteVanillaHashes("Maps/Town");

        var v = Judge("""{ "Action": "EditMap", "Target": "Maps/Custom_Mod_Map" }""", vanilla);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);
    }

    [Fact]
    public void 无GSQ等价物的条件降级为NonTranslatableCondition()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "DailyLuck": "0.02" } }
        """);

        Assert.Equal(Verdict.NonTranslatableCondition, v.Verdict);
    }

    [Fact]
    public void 只能近似的条件降级为NeedsReview()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "DayEvent": "Egg Festival" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
    }

    [Fact]
    public void 可烘焙的条件走通道C()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "HasFile": "config.json" } }
        """);

        // 未提供 mods/pack 上下文时无法求值，但通道已确定
        Assert.NotEqual(Verdict.NonTranslatableCondition, v.Verdict);
        Assert.Equal(Channel.Bake, v.Channel);
    }

    [Fact]
    public void 语言条件走通道B1()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "I18n": "zh" } }
        """);

        Assert.Equal(Channel.LocaleSuffix, v.Channel);
        Assert.Equal(Verdict.NeedsReview, v.Verdict);   // 内容要写进 .zh-CN 资产，需人工确认
    }

    [Fact]
    public void GSQ通道的记录通道为Gsq()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Channel.Gsq, v.Channel);
    }

    [Fact]
    public void 无条件补丁的通道是None()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""{ "Action": "EditData", "Target": "Data/Locations" }""");

        Assert.Equal(Channel.None, v.Channel);
    }

    // ---- Task 8b：新建资产的条件不得被静默丢弃 ----

    [Fact]
    public void 新建资产条件可挂到条目字段时走GSQ并写入()
    {
        // 目标不在树里 → CreatesNewAsset。**条目自己**有 Condition 字段 → 条件可以挂上去，
        // 不能把它当成"无条件存在"的新资产写出去。
        // ⚠ 条目形状要与写入器真的落得下去的一致：`Condition` 要在该条目**这一层**。
        //   `Data/Locations` 那种把条件放在 `Forage[]` **数组元素**里的形状写入器穿不进数组 ——
        //   见下一条用例。
        var v = Judge("""
        { "Action": "EditData", "Target": "Data/SpecialOrders",
          "When": { "Season": "spring" },
          "Entries": { "NewOrder": { "Name": "x", "Condition": null } } }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.Gsq, v.Channel);
        Assert.Equal(LandingKind.EntryConditionField, v.Landing);
        Assert.True(SdvCpTool.Core.Merging.WritePolicy.ShouldWrite(v));
    }

    /// <summary>
    /// 条目形状**落不下去**时必须判 <see cref="Verdict.NoLandingPoint"/>，不得判 Converted。
    /// <para>
    /// 回归护栏：`Data/Locations` 的条件在 <c>Forage[]/Condition</c>（<b>数组元素</b>里），
    /// 而写入器只穿对象、不穿数组。判定若放行，写入时必被落点校验拦下 ——
    /// 报告里就变成「写入失败」，把<b>判定不准</b>伪装成<b>写入出问题</b>
    /// （实测 SVE 64 条写入失败里 32 条、RSV 59 条里 3 条都是这个来源）。
    /// </para>
    /// </summary>
    [Fact]
    public void 条目里落不下去时判无落点而不是可转换()
    {
        // 资产**存在**（走主判定路径，不是新建资产那条），它的条件只在 Forage[] 数组元素里。
        Tree("Data/Locations.json", """{ "NewTown": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring" },
          "Fields": { "NewTown": { "Forage": [ { "Id": "a" } ] } } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Equal(Channel.None, v.Channel);
        Assert.Contains("不接受 Condition", v.Reason);
    }

    /// <summary>
    /// 落点核对必须落在**合并之后**的数据上 —— 写入器就是「先合并补丁、再把条件写进合并结果」。
    /// <para>
    /// 回归护栏：补丁**自己新建**的条目（`Data/Weddings` 的 <c>Attendees.&lt;NPC&gt;</c>）在合并前不存在，
    /// 拿合并前的数据核对会判成「定位不到条件落点条目」，把本该写出去的补丁误杀
    /// （实测 RSV 因此少写 17 条）。
    /// </para>
    /// </summary>
    [Fact]
    public void 补丁自己新建条目时落点核对不算拒绝()
    {
        Tree("Data/Weddings.json", """{ "Attendees": { "Abigail": { "Condition": "x" } } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Weddings",
          "When": { "Season": "spring" },
          "Fields": { "Attendees": { "Olga": { "Setup": "y" } } } }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.Gsq, v.Channel);
        Assert.Equal(LandingKind.EntryConditionField, v.Landing);
    }

    /// <summary>新建资产路径上同样要拦住 —— 那里判 NeedsReview（人工处置方式与已有资产不同）。</summary>
    [Fact]
    public void 新建资产条目里落不下去时判需人工复核()
    {
        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring" },
          "Entries": { "NewTown": { "Forage": [ { "Id": "a", "Condition": null } ] } } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
        Assert.Contains("落点核对不通过", v.Reason);
    }

    [Fact]
    public void 新建资产条件可挂到事件键时走GSQ并写入()
    {
        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Events/Custom_Mod_NewMap",
          "When": { "Season": "spring" },
          "Entries": { "100/t 600 1200": "continue/64 15/x" } }
        """);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.Gsq, v.Channel);
        Assert.Equal(LandingKind.EntryKeyConditions, v.Landing);
        Assert.True(SdvCpTool.Core.Merging.WritePolicy.ShouldWrite(v));
    }

    [Fact]
    public void 新建资产条件挂不上时判无落点且不写()
    {
        // Characters/Schedules 的键是季节/星期名，没有承载条件的位置 →
        // 绝不能写成"无条件存在"的日程（那对没见过事件的存档语义就错了）。
        // ★ 2026-10-11：这类**条件容量封闭**的族（日程/节日）改成判 **NoLandingPoint（确证无处安放）**——
        //   原先判 NeedsReview（"需人工确认落点"）会让读报告的人去找一个并不存在的机制。
        //   口径见 `Assets/ConditionCapacity.cs`。
        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/Schedules/Acorn",
          "When": { "HasSeenEvent": 123 },
          "Entries": { "spring": "0 Map 1 1 2" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("确证无处安放", v.Reason);
        Assert.DoesNotContain("无法承载条件", v.Reason);      // 理由换成容量清单，不再含糊
        Assert.False(SdvCpTool.Core.Merging.WritePolicy.ShouldWrite(v));
    }

    [Fact]
    public void 新建资产条件令牌不认识时判NeedsReview且不写()
    {
        // 落点可挂（事件键），但令牌不认识 → 不能挂，也不能无条件写。
        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Events/Custom_Mod_NewMap",
          "When": { "NoSuchToken": "x" },
          "Entries": { "100/t 600 1200": "continue/64 15/x" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
        Assert.False(SdvCpTool.Core.Merging.WritePolicy.ShouldWrite(v));
    }

    [Fact]
    public void 新建资产无条件时仍是CreatesNewAsset不回归()
    {
        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/Schedules/Acorn",
          "Entries": { "spring": "2400 Map 1 1 2" } }
        """);

        Assert.Equal(Verdict.CreatesNewAsset, v.Verdict);
        Assert.True(SdvCpTool.Core.Merging.WritePolicy.ShouldWrite(v));
    }

    // ------------------------------------------------- 常量折叠（判定前折叠 + 存树）

    [Fact]
    public void 折叠把混合条件化为纯GSQ条件()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = JudgeWithFold("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring", "HasMod": "|contains=Foo.Bar" } }
        """, LeafEval(("HasMod", true)));

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.Gsq, v.Channel);
        Assert.NotNull(v.FoldedWhen);
        // 判定与写入用同一棵折叠树：残条件只剩 Season，能翻成原生 GSQ。
        Assert.Equal("SEASON spring", GsqTranslator.Translate(v.FoldedWhen).Gsq);
    }

    [Fact]
    public void 折叠后只剩原生关键字的真实形态()
    {
        // 真实数据（Data/Festivals/.../EggFestival.json）：两个配置开关 + HasSeenEvent
        // → 开关折成常量后整条塌缩成一个原版关键字。
        Tree("Data/Events/Town.json", """{ "100/t 600 1200": "continue/64 15/farmer 1 1 1" }""");

        var v = JudgeWithFold("""
        { "Action": "EditData", "Target": "Data/Events/Town",
          "When": { "HasSeenEvent": 75160249,
                    "Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals": true,
                    "Rafseazz.RidgesideVillage/ExpandedFestivalMaps": true } }
        """, LeafEval(
            ("Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals", true),
            ("Rafseazz.RidgesideVillage/ExpandedFestivalMaps", true)));

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.Gsq, v.Channel);
        Assert.Equal("PLAYER_HAS_SEEN_EVENT Current 75160249",
            GsqTranslator.Translate(v.FoldedWhen).Gsq);
    }

    [Fact]
    public void 折叠成恒真时按无条件处理()
    {
        Tree("Data/Objects.json", """{ "a": "b" }""");

        var v = JudgeWithFold("""
        { "Action": "EditData", "Target": "Data/Objects",
          "When": { "HasMod": "|contains=Foo.Bar" } }
        """, LeafEval(("HasMod", true)));

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
        Assert.True(SdvCpTool.Core.Merging.WritePolicy.ShouldWrite(v));
    }

    /// <summary>
    /// ★ <b>多目标里「一部分不在树里」不该毒死整条补丁</b>。
    /// <para>
    /// CP 的 <c>PatchLoader.SplitPatches</c> 会把逗号分隔的 <c>Target</c> 拆成**互相独立**的补丁
    /// （<c>PatchLoader.cs:165-200</c>，Target × FromFile 的笛卡尔积），缺的那个由 EditData 新建。
    /// 我们原先是「只要有一个缺就整条 `TargetMissing`」—— 实测代价：SVE 3 条补丁
    /// （`Target` 是 47 个 <c>Characters/Dialogue/*</c>，其中 6 个是 SVE 自己的 NPC）
    /// **连 41 个存在的目标一起丢掉**。
    /// </para>
    /// </summary>
    [Fact]
    public void 多目标里部分缺失不整条判死()
    {
        Tree("Characters/Dialogue/Alex.json", """{ "a": "b" }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/Dialogue/Alex, Characters/Dialogue/Sophia",
          "Entries": { "reject_X": "no thanks" } }
        """);

        Assert.Equal(Verdict.CreatesNewAsset, v.Verdict);   // 不是 TargetMissing
        Assert.Contains("Sophia", v.Reason);
        Assert.Contains("Alex", v.Reason);
    }

    [Fact]
    public void 多目标里的缺失项若是原版资产仍判缺失()
    {
        Tree("Characters/Dialogue/Alex.json", """{ "a": "b" }""");
        VanillaAssetIndex vanilla = WriteVanillaHashes("Characters/Dialogue/Sophia");

        var v = Judge("""
        { "Action": "EditData", "Target": "Characters/Dialogue/Alex, Characters/Dialogue/Sophia",
          "Entries": { "x": "y" } }
        """, vanilla);

        Assert.Equal(Verdict.TargetMissing, v.Verdict);     // 树不完整，不能当成"新建"
    }

    /// <summary>
    /// ★ **条目的值要跟资产声明的类型对得上**：CP 按声明类型反序列化，转不了就跳过那个条目
    /// （`EditDataPatch` 的 `WarnForRecord("failed converting … to the expected type")`）。
    /// 我们在 JSON 层合并，看不见类型 —— 不查的话会把**对象**写进
    /// `Dictionary&lt;int, string&gt;` 这类资产里，**回包时直接失败**（实测 Cornucopia 12 个资产）。
    /// </summary>
    [Fact]
    public void 值形状与资产声明的标量元素类型不符时不写()
    {
        Tree("Data/Ext.json", """{ "1": "a" }""");
        Tree("Data/Ext.config", """
        { "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.Int32, mscorlib, Version=4.0.0.0]],[[System.String, mscorlib, Version=4.0.0.0]]" },
                        { "Type": "Microsoft.Xna.Framework.Content.Int32Reader" },
                        { "Type": "Microsoft.Xna.Framework.Content.StringReader" } ] }
        """);

        // 先把两段中间量钉住，避免「检查没触发」到底是哪一步的问题
        Assert.Equal(SdvCpTool.Core.Validating.ConfigLeafTypes.LeafKind.Scalar,
            SdvCpTool.Core.Validating.ConfigLeafTypes.ReadLeafKind(
                Path.Combine(_treeDir, "Data", "Ext.config")));

        var bad = Judge("""
        { "Action": "EditData", "Target": "Data/Ext", "Entries": { "408": { "CategoryTextOverride": "Nut" } } }
        """);
        Assert.Equal(Verdict.NeedsReview, bad.Verdict);
        Assert.Contains("声明的元素类型是**标量**", bad.Reason);

        // 值本来就是字符串 ⇒ 照常
        var good = Judge("""
        { "Action": "EditData", "Target": "Data/Ext", "Entries": { "408": "Nut" } }
        """);
        Assert.Equal(Verdict.ConvertedNoCondition, good.Verdict);
    }

    [Fact]
    public void 折叠成恒假时判不适用且不写()
    {
        Tree("Data/Objects.json", """{ "a": "b" }""");

        var v = JudgeWithFold("""
        { "Action": "EditData", "Target": "Data/Objects",
          "When": { "HasMod": "|contains=Foo.Bar" } }
        """, LeafEval(("HasMod", false)));

        Assert.Equal(Verdict.NotApplicable, v.Verdict);       // 明确的判定，不是 NeedsReview
        Assert.Null(v.Written);
        Assert.False(SdvCpTool.Core.Merging.WritePolicy.ShouldWrite(v));
    }

    /// <summary>
    /// ★ 判「不适用」时**必须说清是哪片叶子、折成了什么** —— 语料里 **804 条（12%）** 靠折叠判死，
    /// 而依据（`HasMod` 看已装模组、配置令牌看 `config.json`、命名空间令牌看提供方的 `config.json`）
    /// 原先只落进一个布尔，报告里看不到。判错一条就是**静默丢内容**，事后无从核对。
    /// </summary>
    /// <summary>
    /// ★ 折叠**前**必须把 `When` 叶子里的**字段引用令牌**展开 —— CP 允许补丁的 `When` 用 `{{FromFile}}`
    /// （`PatchLoader.cs:617-625`：只要该补丁有 `FromFile` 字段就允许，逐补丁绑定）。
    /// <para>
    /// 不展开的话 `HasFile` 会拿**字面量** `{{FromFile}}` 去 `File.Exists` ⇒ 恒为假 ⇒
    /// **整条补丁被静默丢掉**。实测代价：语料里 **20 条**（SVE 季节性服装的 `EditImage` 守卫）。
    /// </para>
    /// </summary>
    [Fact]
    public void 折叠前展开When里的字段引用令牌()
    {
        Tree("Data/Objects.json", """{ "a": "b" }""");

        var seen = new List<string>();
        JudgeWithFold("""
        { "Action": "EditImage", "Target": "Characters/Claire", "FromFile": "assets/Claire.png",
          "When": { "HasFile:{{FromFile}}": true } }
        """, n => { seen.Add($"{n.KeyName}|{n.Arguments}"); return null; });

        Assert.Contains("HasFile|assets/Claire.png", seen);   // 展开成真路径，而不是字面量
        Assert.DoesNotContain("HasFile|{{FromFile}}", seen);

        // 报告里也要印**展开后**的路径：印 `HasFile {{FromFile}} = 假` 的话，
        // 读者不知道是哪个文件不存在（实测 13 条就是这样被印成字面量的）。
        var v = JudgeWithFold("""
        { "Action": "EditImage", "Target": "Characters/Claire", "FromFile": "assets/Claire.png",
          "When": { "HasFile:{{FromFile}}": true } }
        """, _ => false);
        Assert.Contains("`HasFile assets/Claire.png = True` = 假", v.Reason);   // 声明值 True 也印出来
        Assert.DoesNotContain("{{FromFile}}", v.Reason);
    }

    [Fact]
    public void 展开字段引用令牌后HasFile能折成真()
    {
        Tree("Data/Objects.json", """{ "a": "b" }""");
        string img = Path.Combine(_packDir, "assets", "Claire.png");
        Directory.CreateDirectory(Path.GetDirectoryName(img)!);
        File.WriteAllText(img, "x");

        // 用**真的** HasFile 语义：参数是包内相对路径，文件在 ⇒ 真 ⇒ 该补丁照常可写
        Func<WhenNode, bool?> eval = n =>
            n.KeyName == "HasFile"
                ? File.Exists(Path.Combine(_packDir, (n.Arguments ?? "").Replace('/', Path.DirectorySeparatorChar)))
                : null;

        var v = JudgeWithFold("""
        { "Action": "EditImage", "Target": "Characters/Claire", "FromFile": "assets/Claire.png",
          "When": { "HasFile:{{FromFile}}": true } }
        """, eval);

        Assert.NotEqual(Verdict.NotApplicable, v.Verdict);
    }

    [Fact]
    public void 判不适用时列出叶子求值依据()
    {
        Tree("Data/Objects.json", """{ "a": "b" }""");

        var v = JudgeWithFold("""
        { "Action": "EditData", "Target": "Data/Objects",
          "When": { "HasMod": "|contains=Foo.Bar", "Weather": "rain" } }
        """, n => n.KeyName == "HasMod" ? false : null);

        Assert.Equal(Verdict.NotApplicable, v.Verdict);
        Assert.Contains("HasMod", v.Reason);
        Assert.Contains("= 假", v.Reason);
        Assert.Contains("Weather", v.Reason);
        Assert.Contains("不知道", v.Reason);      // 求不了值的叶子也要列，且**不得**被当成假
    }

    [Fact]
    public void 回归_Season与配置令牌混合不得被折叠放行()
    {
        // 硬要求：折叠不得把「求不了值」的叶子当常量。Season 必须原样保留、仍走 GSQ；
        // 配置令牌能求值则折掉 —— 但整条**绝不能**被折成恒真或恒假。
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = JudgeWithFold("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring",
                    "Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals": true } }
        """, LeafEval(("Rafseazz.RidgesideVillage/RSVNPCSAttendFestivals", true)));

        Assert.NotEqual(Verdict.NotApplicable, v.Verdict);
        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.Gsq, v.Channel);
        Assert.NotNull(v.FoldedWhen);
        Assert.NotEqual(WhenNodeKind.Literal, v.FoldedWhen!.Kind);   // 没被折成常量
        Assert.Equal("SEASON spring", GsqTranslator.Translate(v.FoldedWhen).Gsq);
    }

    [Fact]
    public void 不给求值器时不折叠_保持旧行为()
    {
        Tree("Data/Locations.json", """{ "Town": { "Forage": [ { "Condition": "x" } ] } }""");

        var v = Judge("""
        { "Action": "EditData", "Target": "Data/Locations",
          "When": { "Season": "spring", "HasMod": "|contains=Foo.Bar" } }
        """);

        Assert.Null(v.FoldedWhen);
        Assert.Equal(Channel.Bake, v.Channel);   // 与折叠前一致
    }

    // --------------------------------------------------- 自定义命名空间下的"新建资产"

    /// <summary>指名本包 UniqueID 的判定（用来分辨目标挂在**自己**还是**别人**名下）。</summary>
    private PatchVerdict JudgeAs(string uniqueId, string patchJson)
    {
        var raw = JObject.Parse(patchJson);
        var patch = new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            FromFile = raw["FromFile"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = Path.Combine(_packDir, "content.json"),
        };
        return new PatchJudge(new ResourceTree(_treeDir), _packDir, null, null, null, null, uniqueId)
            .Judge(patch);
    }

    /// <summary>
    /// ★ **本包自己的**命名空间（`Mods/&lt;自己的UID&gt;/…`）**不能**说成"别的 mod 的"。
    /// <para>
    /// 判定（`NoLandingPoint`）两边一样 —— 依据是"类型无从得知 ⇒ 写出来打不了包"；
    /// 但措辞必须分：说成别人的，读报告的人会以为"这本来就不是我们的东西"，而它其实是
    /// **本包自己的内容、是真损失**。（2026-10-11 复核 §7.2 那条错报时加的。）
    /// </para>
    /// </summary>
    [Fact]
    public void 自己的命名空间下新建资产_措辞不说是别人的()
    {
        var v = JudgeAs("Cornucopia.MoreCrops", """
        { "Action": "EditData", "Target": "Mods/Cornucopia.MoreCrops/SomeData",
          "Entries": { "a": "b" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("本包自己的", v.Reason);
        Assert.DoesNotContain("别的 mod 注册的自定义资产", v.Reason);
    }

    /// <summary>光秃秃的 UID（`&lt;自己的UID&gt;/…`，`{{ModId}}` 解析后的形状）同样认得出。</summary>
    [Fact]
    public void 不带Mods前缀也认得出是自己()
    {
        var v = JudgeAs("Cornucopia.MoreCrops", """
        { "Action": "EditData", "Target": "Cornucopia.MoreCrops/SomeData",
          "Entries": { "a": "b" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("本包自己的", v.Reason);
    }

    /// <summary>**带条件**的那条路（`JudgeConditionalNewAsset`）同样按归属措辞。</summary>
    [Fact]
    public void 自己的命名空间下带条件新建资产_措辞也不说是别人的()
    {
        var v = JudgeAs("Cornucopia.MoreCrops", """
        { "Action": "EditData", "Target": "Mods/Cornucopia.MoreCrops/SomeData",
          "Entries": { "a": "b" },
          "When": { "Season": "Spring" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("本包自己的", v.Reason);
        Assert.DoesNotContain("别的 mod 注册的自定义资产", v.Reason);
    }

    /// <summary>别人的命名空间照旧（**判定与措辞都不变** —— 这一档语料里 63 条都在这边）。</summary>
    [Fact]
    public void 别人的命名空间措辞不变()
    {
        var v = JudgeAs("Cornucopia.MoreCrops", """
        { "Action": "EditData", "Target": "Mods/spacechase0.SpaceCore/ObjectExtensionData",
          "Entries": { "a": "b" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("别的 mod 注册的自定义资产", v.Reason);
        Assert.DoesNotContain("本包自己的", v.Reason);
    }

    /// <summary>不知道自己的 UID 时（没给参数）一律按"别人的"措辞 —— **不敢乱认**。</summary>
    [Fact]
    public void 不知道自己的UID时不乱认()
    {
        var v = Judge("""
        { "Action": "EditData", "Target": "Mods/Cornucopia.MoreCrops/SomeData",
          "Entries": { "a": "b" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
        Assert.Contains("别的 mod 注册的自定义资产", v.Reason);
    }

    // --------------------------------------------------- 旧格式包（Format < 2.0）的 1.6 迁移

    private PatchVerdict JudgeAtFormat(Version? format, string patchJson)
    {
        var raw = JObject.Parse(patchJson);
        var patch = new ChangePatch
        {
            Raw = raw,
            Action = raw["Action"]!.Value<string>()!,
            Target = raw["Target"]?.Value<string>(),
            FromFile = raw["FromFile"]?.Value<string>(),
            When = raw["When"] as JObject,
            SourceFile = Path.Combine(_packDir, "content.json"),
        };
        return new PatchJudge(new ResourceTree(_treeDir), _packDir, null, null, null, null, null, format)
            .Judge(patch);
    }

    /// <summary>
    /// ★ 声明旧 `Format`（&lt; 2.0.0）的包打到**1.6 之前的资产名**上 ⇒ CP 会先跑 `Migration_2_0`
    /// 把它改名/改形状，我们没实现迁移器 ⇒ 必须判 `NeedsReview` 并不写
    /// （照字面写会造出一个游戏不读的资产：静默写错）。
    /// </summary>
    [Fact]
    public void 旧格式包打到老资产名_判需人工且说明迁移器()
    {
        var v = JudgeAtFormat(new Version(1, 29), """
        { "Action": "EditData", "Target": "Data/ObjectInformation",
          "Entries": { "24": "Old Slash/Delimited/String" } }
        """);

        Assert.Equal(Verdict.NeedsReview, v.Verdict);
        Assert.Contains("Migration_2_0", v.Reason);
        Assert.Contains("Data/Objects", v.Reason);       // 改名后的名字要点出来
        Assert.Contains("Format 1.29", v.Reason);
    }

    /// <summary>
    /// 反向边界：**名字在 1.6 里还在**的那些（`Data/Crops`/`Data/Locations`…）只有形状变，
    /// 现代格式的包写它们**是对的** ⇒ 不许拦。旧格式包才拦。
    /// </summary>
    [Fact]
    public void 现代格式包写同名资产_不拦()
    {
        var modern = JudgeAtFormat(new Version(2, 0), """
        { "Action": "EditData", "Target": "Data/Crops",
          "Entries": { "273": { "SeedName": "x" } } }
        """);
        Assert.NotEqual(Verdict.NeedsReview, modern.Verdict);

        var legacy = JudgeAtFormat(new Version(1, 25), """
        { "Action": "EditData", "Target": "Data/Crops",
          "Entries": { "273": "Old/Shape/Here" } }
        """);
        Assert.Equal(Verdict.NeedsReview, legacy.Verdict);
        Assert.Contains("1.6 之前的老形状", legacy.Reason);
    }

    /// <summary>读不到 `Format`（`null`）时**什么都不说** —— 不知道就不猜（FTM 包的补丁走这条路）。</summary>
    [Fact]
    public void 没有Format时不猜()
    {
        var v = JudgeAtFormat(null, """
        { "Action": "EditData", "Target": "Data/ObjectInformation",
          "Entries": { "a": "b" } }
        """);

        Assert.NotEqual(Verdict.NeedsReview, v.Verdict);
    }

    /// <summary>
    /// ★ **图已被 `Data/Characters` 的 `Appearance[]` 条件引用** ⇒ 图本身无条件写出即可（不再判无落点）。
    /// <para>
    /// 实测那 92 条 `Portraits/&lt;NPC&gt;_&lt;变体&gt;`（East Scarp / Lunna 的季节立绘）就是这么被误拒的：
    /// 外观发射器按 NPC 名分组（提取出的是 `Aideen_spring` ⇒ 不认得）。
    /// </para>
    /// </summary>
    [Fact]
    public void 被Appearance引用的图无条件写出()
    {
        Tree("Data/Characters.json", """
        { "Aideen": { "Appearance": [
            { "Id": "S", "Condition": "SEASON Spring", "Sprite": "Characters/Aideen_Spring",
              "Portrait": "Portraits/Aideen_spring" } ] } }
        """);
        PackFile("a.png", "");
        Tree("Portraits/Aideen_spring.png", "");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Portraits/Aideen_spring", "FromFile": "a.png",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.ConvertedNoCondition, v.Verdict);
        Assert.Contains("Appearance[]", v.Reason);
    }

    /// <summary>反向边界：**没人引用**的条件图仍然该拒（原行为不变）。</summary>
    [Fact]
    public void 无人引用的条件图仍拒()
    {
        Tree("Data/Characters.json", """{ "Aideen": { "Appearance": [] } }""");
        PackFile("a.png", "");
        Tree("Portraits/Aideen_spring.png", "");

        var v = Judge("""
        { "Action": "EditImage", "Target": "Portraits/Aideen_spring", "FromFile": "a.png",
          "When": { "Season": "spring" } }
        """);

        Assert.Equal(Verdict.NoLandingPoint, v.Verdict);
    }
}
