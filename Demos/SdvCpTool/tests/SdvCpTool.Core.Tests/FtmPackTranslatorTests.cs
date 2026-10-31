using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// **FTM（Farm Type Manager）包 → CP 形状的补丁**。
/// <para>
/// FTM 的 `content.json` 是另一套格式；其中**觅食 / 埋藏物**两类原版有现成落点
/// （`Data/Locations.&lt;地图&gt;.Forage` / `.ArtifactSpots`），翻译器把它们翻成普通 `EditData`，
/// 后面整条流水线原样复用。
/// </para>
/// <para>
/// ⚠ 这里钉的都是**实测踩过**的点：
/// <list type="bullet">
/// <item>`Days` 是**几号**（`1..28` 与 `13-15` 这类区间），不是星期几 —— 一时错标成 `DayOfWeek`，
/// 结果写出 `DAY_OF_WEEK [\r\n "2" "4" …]` 那种废条件；</item>
/// <item>值必须**拼成逗号串**再交给 CP→GSQ 翻译器，塞 JArray 会被 `ToString()` 成上面那种废条件；</item>
/// <item>`ArtifactSpots` **没有 `Season` 槽**，季节只能进 `Condition`（`SEASON &lt;季节&gt;`），
/// 否则"只在春季埋"会变成四季都有；</item>
/// <item>刷怪 / 刷矿 / 大件三段没有原版落点，**整段跳过并报数**（不是静默丢）。</item>
/// </list>
/// </para>
/// </summary>
public class FtmPackTranslatorTests : IDisposable
{
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvftm_" + Guid.NewGuid().ToString("N"));

    public FtmPackTranslatorTests() => Directory.CreateDirectory(_pack);

    public void Dispose()
    {
        if (Directory.Exists(_pack)) Directory.Delete(_pack, recursive: true);
    }

    private void Write(string rel, string content)
    {
        string full = Path.Combine(_pack, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void Manifest(string uniqueId) => Write("manifest.json",
        $$"""{ "Name": "T", "Author": "t", "Version": "1.0.0", "UniqueID": "T", "ContentPackFor": { "UniqueID": "{{uniqueId}}" } }""");

    private FtmImportResult Import()
    {
        FtmImportResult? r = FtmPackTranslator.TryImport(_pack);
        Assert.NotNull(r);
        return r!;
    }

    /// <summary>不是 FTM 包 → 返回 null（调用方走原来那条 CP 的路）。</summary>
    [Fact]
    public void 不是FTM包时不动它()
    {
        Manifest("Pathoschild.ContentPatcher");
        Write("content.json", """{ "Format": "1.28.0", "Changes": [] }""");

        Assert.Null(FtmPackTranslator.TryImport(_pack));
    }

    /// <summary>原版下标 / 限定 id / 名字三种写法都认得，并且落到 `Forage` 上带季节与 GSQ 条件。</summary>
    [Fact]
    public void 觅食翻成DataLocations的Forage()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "ForageSpawnEnabled": true,
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "test", "MapName": "Custom_Woods",
            "SpringItemIndex": [ 16, "Mod.Item_Pear", "Big Conch" ],
            "ExtraConditions": { "Days": [ "2", "4" ], "Years": [ "2" ],
                                 "WeatherToday": [ "Rain" ], "Seasons": [] }
          } ] },
          "MonsterSpawnEnabled": true,
          "Monster_Spawn_Settings": { "Areas": [ { "UniqueAreaID": "m", "MapName": "Custom_Woods" } ] }
        }
        """);

        FtmImportResult r = Import();
        JObject patch = (JObject)r.Content["Changes"]![0]!;
        Assert.Equal("Data/Locations", patch["Target"]!.Value<string>());
        Assert.Equal(new[] { "Custom_Woods", "Forage" },
            ((JArray)patch["TargetField"]!).Select(t => t.Value<string>()).ToArray());

        JObject entries = (JObject)patch["Entries"]!;
        Assert.Equal(3, entries.Count);
        JObject inboxed = (JObject)entries.Properties().First().Value;
        Assert.Equal("(O)16", inboxed["ItemId"]!.Value<string>());          // 原版下标 → (O)n
        Assert.Equal("Spring", inboxed["Season"]!.Value<string>());

        string condition = inboxed["Condition"]!.Value<string>()!;
        Assert.Contains("DAY_OF_MONTH 2 4", condition);                     // Days 是**几号**
        Assert.Contains("YEAR 2 2", condition);   // CP 的 `Year: 2` 是"等于 2"，区间要补第二个 max
        Assert.Contains("WEATHER", condition);
        Assert.DoesNotContain("[", condition);                              // 不许出现 JArray 的 ToString
        Assert.DoesNotContain("\n", condition);

        Assert.Contains("Mod.Item_Pear",
            entries.Properties().Select(p => p.Value["ItemId"]!.Value<string>()).ToArray());
        Assert.Contains("Big Conch",
            entries.Properties().Select(p => p.Value["ItemId"]!.Value<string>()).ToArray());

        // 刷怪那段没有落点 —— 要**报出来**，不是静默丢
        Assert.Contains(r.Notes, n => n.Contains("刷怪") && n.Contains("没写"));
    }

    /// <summary>
    /// area 的季节物品表为 **null** 时，FTM 会回退到段级（全局）表
    /// （反编译 `ModEntry.cs:7967-7982`）。实测 Sunflower Acres 两个 area 四季全 null。
    /// </summary>
    [Fact]
    public void area季节表为空时回退到全局表()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "ForageSpawnEnabled": true,
          "Forage_Spawn_Settings": {
            "Areas": [
              { "UniqueAreaID": "a", "MapName": "Custom_A",
                "SpringItemIndex": null, "SummerItemIndex": null,
                "FallItemIndex": null, "WinterItemIndex": null },
              { "UniqueAreaID": "b", "MapName": "Custom_B",
                "SpringItemIndex": null, "SummerItemIndex": null,
                "FallItemIndex": null, "WinterItemIndex": null }
            ],
            "SpringItemIndex": [ 16 ],
            "SummerItemIndex": [ 18 ],
            "FallItemIndex": [ 20 ],
            "WinterItemIndex": [ 22, 24 ]
          }
        }
        """);

        FtmImportResult r = Import();
        var patches = r.Content["Changes"]!.OfType<JObject>()
            .Where(p => p["Target"]?.Value<string>() == "Data/Locations").ToList();

        Assert.Equal(2, patches.Count);                                   // 每张地图一条
        Assert.All(patches, p => Assert.Equal(5, ((JObject)p["Entries"]!).Count));
        Assert.Contains(r.Notes, n => n.Contains("回退到全局"));
    }

    /// <summary>
    /// `ExtraConditions.CPConditions.HasMod` 可以离线判：已装就从条件里去掉；
    /// 交给 GSQ 翻译器只会整条 area 丢掉（实测 Secret Wood Weeds + ItemExtensions）。
    /// </summary>
    [Fact]
    public void area的HasMod条件用已装索引烘焙()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "ForageSpawnEnabled": true,
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "weeds", "MapName": "Woods",
            "SpringItemIndex": [ 16 ],
            "ExtraConditions": { "CPConditions": { "HasMod": "mistyspring.ItemExtensions" } }
          } ] }
        }
        """);

        FtmImportResult r = FtmPackTranslator.TryImport(_pack, id => id == "mistyspring.ItemExtensions")!;
        JObject patch = (JObject)r.Content["Changes"]![0]!;
        JObject entries = (JObject)patch["Entries"]!;

        Assert.Single(entries);
        Assert.Null(entries.Properties().Single().Value["Condition"]);   // HasMod 已被消掉
    }

    /// <summary>
    /// ★ FTM 的 `Days` **可以写区间**（语料实测 `"1-4"` / `"12-14"`），而 GSQ 的 `DAY_OF_MONTH`
    /// 只认单个日子（多参数=任一命中，没有区间语法）⇒ 原样写出去就是非法 GSQ，整条条件失效
    /// （实机日志 `Failed parsing condition 'DAY_OF_MONTH 1-4': '1-4' isn't a valid day of month`，
    /// 产出树 48 条）。要展开成离散天数，语义不变。
    /// </summary>
    [Fact]
    public void 觅食的Days区间展开成离散天数()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "test", "MapName": "Custom_Woods",
            "SpringItemIndex": [ 16 ],
            "ExtraConditions": { "Days": [ "1-4", "13" ] }
          } ] }
        }
        """);

        FtmImportResult r = Import();
        JObject entries = (JObject)((JObject)r.Content["Changes"]![0]!)["Entries"]!;
        string condition = entries.Properties().First().Value["Condition"]!.Value<string>()!;

        Assert.Contains("DAY_OF_MONTH 1 2 3 4 13", condition);
        Assert.DoesNotContain("-", condition);        // 不许再有区间写法
    }

    /// <summary>越界/认不出的形状**原样留着**（宁可让游戏报错，也不替作者猜）。</summary>
    [Fact]
    public void 觅食的Days越界区间原样保留()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "test", "MapName": "Custom_Woods",
            "SpringItemIndex": [ 16 ],
            "ExtraConditions": { "Days": [ "30-40" ] }
          } ] }
        }
        """);

        FtmImportResult r = Import();
        JObject entries = (JObject)((JObject)r.Content["Changes"]![0]!)["Entries"]!;
        string condition = entries.Properties().First().Value["Condition"]!.Value<string>()!;

        Assert.Contains("30-40", condition);
    }

    /// <summary>
    /// ★ FTM 的 `Years` 可以写 `"2+"`（「2 年及以后」）—— 这个缺口**真出现了**（2026-10-15 实机 72 行
    /// `Failed parsing condition 'YEAR 2+ 2+'`：我们把 `2+` 同时当成 min 与 max 写出去）。
    /// 游戏源码里 `YEAR <min> [max]` 的 **max 默认 `int.MaxValue`**（`GameStateQuery.cs:411-423`）
    /// ⇒ 正确写法是 **`YEAR 2`**（上无界）；⚠ 与 CP 的 `Year: 2`（**恰为** 2 年 ⇒ `YEAR 2 2`）不同义。
    /// </summary>
    [Fact]
    public void 觅食的Years加号写成年份下限()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "test", "MapName": "Custom_Woods",
            "SpringItemIndex": [ 16 ],
            "ExtraConditions": { "Years": [ "2+" ] }
          } ] }
        }
        """);

        FtmImportResult r = Import();
        JObject entries = (JObject)((JObject)r.Content["Changes"]![0]!)["Entries"]!;
        string condition = entries.Properties().First().Value["Condition"]!.Value<string>()!;

        Assert.Contains("YEAR 2", condition);
        Assert.DoesNotContain("2+", condition);
        Assert.DoesNotContain("YEAR 2 2", condition);     // 那是"恰为 2 年"，不是"2 年及以后"
    }

    /// <summary>下限与精确值混用 ⇒ 用 GSQ 的 `ANY`（每个参数是一条完整子句）。</summary>
    [Fact]
    public void 觅食的Years下限与精确值混用时用ANY()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "test", "MapName": "Custom_Woods",
            "SpringItemIndex": [ 16 ],
            "ExtraConditions": { "Years": [ "2+", "5" ] }
          } ] }
        }
        """);

        FtmImportResult r = Import();
        JObject entries = (JObject)((JObject)r.Content["Changes"]![0]!)["Entries"]!;
        string condition = entries.Properties().First().Value["Condition"]!.Value<string>()!;

        Assert.Contains("ANY", condition);
        Assert.Contains("\"YEAR 2\"", condition);
        Assert.Contains("\"YEAR 5 5\"", condition);
        Assert.DoesNotContain("2+", condition);
    }

    /// <summary>
    /// 刷新量按 area 声明的 `MaximumSpawnsPerDay` 摊到每条上（原版按 `Chance` 逐条掷骰，
    /// 一圈的期望条数 = 条目数 × 概率）。⚠ 会**钳到 1**：多点刷新不能把概率推到 &gt;1。
    /// </summary>
    [Fact]
    public void 刷新量按MaximumSpawnsPerDay摊到每条()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [
            { "UniqueAreaID": "one", "MapName": "M", "SpringItemIndex": [ 1, 2, 3, 4 ] },
            { "UniqueAreaID": "five", "MapName": "M2", "SpringItemIndex": [ 1, 2, 3, 4 ],
              "MaximumSpawnsPerDay": 2 }
          ] }
        }
        """);

        FtmImportResult r = Import();
        var byMap = r.Content["Changes"]!.OfType<JObject>()
            .ToDictionary(p => ((JArray)p["TargetField"]!)[0]!.Value<string>()!, p => (JObject)p["Entries"]!);

        Assert.Equal(0.25, ((JObject)byMap["M"].Properties().First().Value)["Chance"]!.Value<double>(), 3);
        Assert.Equal(0.5, ((JObject)byMap["M2"].Properties().First().Value)["Chance"]!.Value<double>(), 3);
    }

    /// <summary>
    /// 分母只能是**当季**条目数：FTM 运行时只取当前季节的表。
    /// 用四季总数会把单季期望刷新量压到约 1/4（实测 Forest 1）。
    /// </summary>
    [Fact]
    public void 刷新量分母只算当季条目()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "x", "MapName": "M",
            "SpringItemIndex": [ 1, 2 ], "SummerItemIndex": [ 3 ], "MaximumSpawnsPerDay": 1
          } ] }
        }
        """);

        FtmImportResult r = Import();
        JObject entries = (JObject)((JObject)r.Content["Changes"]![0]!)["Entries"]!;
        foreach (JProperty p in entries.Properties())
            Assert.Equal(
                p.Value["Season"]!.Value<string>() == "Spring" ? 0.5 : 1.0,
                p.Value["Chance"]!.Value<double>(), 3);
    }

    /// <summary>`Buried` → `ArtifactSpots`，且季节写进 `Condition`（那个数组没有 `Season` 槽）。</summary>
    [Fact]
    public void 埋藏物翻成ArtifactSpots且季节进条件()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "buried", "MapName": "Woods",
            "WinterItemIndex": [ { "Category": "Buried", "contents": [ "Mod.ShrubSeed" ] } ]
          } ] }
        }
        """);

        FtmImportResult r = Import();
        JObject patch = (JObject)r.Content["Changes"]![0]!;
        Assert.Equal(new[] { "Woods", "ArtifactSpots" },
            ((JArray)patch["TargetField"]!).Select(t => t.Value<string>()).ToArray());
        JObject entry = (JObject)((JObject)patch["Entries"]!).Properties().First().Value;
        Assert.Equal("Mod.ShrubSeed", entry["ItemId"]!.Value<string>());
        Assert.Null(entry["Season"]);                                        // 这个数组没有 Season 槽
        Assert.Contains("SEASON Winter", entry["Condition"]!.Value<string>()!);
    }

    /// <summary>
    /// ★ `Buried` 可能带**多样**内容（实测 40 条带 2 个，如 `[390, 390]`）—— 取第一个就静默丢一半。
    /// 这里钉「有几样就落几条」，并且**去重**。
    /// </summary>
    [Fact]
    public void 埋藏物带多样内容时都落下来()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "b", "MapName": "Woods",
            "FallItemIndex": [ { "Category": "Buried", "contents": [ 390, 391, 390 ] } ]
          } ] }
        }
        """);

        JObject patch = (JObject)Import().Content["Changes"]![0]!;
        var items = ((JObject)patch["Entries"]!).Properties()
            .Select(p => p.Value["ItemId"]!.Value<string>()).ToList();
        Assert.Equal(new[] { "(O)390", "(O)391" }, items);        // 两样（去重后）
    }

    /// <summary>容器（宝箱 / 木桶）没有原版落点 —— 整类跳过并计数。</summary>
    [Fact]
    public void 宝箱跳过并计数()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "chest", "MapName": "Custom_X",
            "SpringItemIndex": [ { "Category": "Chest", "Contents": [ { "Category": "Weapon", "Name": "Diamond Wand" } ] } ]
          } ] }
        }
        """);

        FtmImportResult r = Import();
        Assert.Empty((JArray)r.Content["Changes"]!);                         // 一条都没落
        Assert.Contains(r.Notes, n => n.Contains("宝箱") && n.Contains("没写"));
    }

    /// <summary>
    /// **限次刷新 = 1** 的一次性 area：原版没有「地图上一次性生成」的机制，但 `Data/TriggerActions`
    /// 的一次性动作（原版 31 条全是 `MarkActionApplied`）能顶上 —— 换成「条件满足**且到过那张地图**时邮寄」。
    /// ⚠ 投递方式变了，所以报告里要**明说**。
    /// </summary>
    [Fact]
    public void 一次性area改成邮寄()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "chest", "MapName": "Custom_HighlandsOutpost",
            "SpringItemIndex": [ 16, "Mod.Pear" ],
            "ExtraConditions": { "LimitedNumberOfSpawns": 1 }
          } ] }
        }
        """);

        FtmImportResult r = Import();
        JObject trigger = r.Content["Changes"]!.OfType<JObject>()
            .First(p => p["Target"]!.Value<string>() == "Data/TriggerActions");
        JObject entry = (JObject)((JObject)trigger["Entries"]!).Properties().First().Value;
        Assert.Equal("DayEnding", entry["Trigger"]!.Value<string>());        // 与官方那 24 条邮件一致
        Assert.True(entry["MarkActionApplied"]!.Value<bool>());              // 只做一次
        string condition = entry["Condition"]!.Value<string>()!;
        Assert.Contains("SEASON Spring", condition);
        Assert.Contains("PLAYER_VISITED_LOCATION Current Custom_HighlandsOutpost", condition);

        JObject mail = r.Content["Changes"]!.OfType<JObject>()
            .First(p => p["Target"]!.Value<string>() == "Data/Mail");
        string text = ((JObject)mail["Entries"]!).Properties().First().Value.Value<string>()!;
        // ⚠ **每样一段**：段里写 `A 1 B 1` 时游戏随机挑一对，等于只给一样
        Assert.Contains("%item id (O)16 1 %%", text);
        Assert.Contains("%item id Mod.Pear 1 %%", text);
        Assert.Contains("[#]chest", text);                                   // 标题

        Assert.Contains(r.Notes, n => n.Contains("邮寄") && n.Contains("投递方式"));
    }

    /// <summary>
    /// ★ 同一张地图同一季**多个**一次性 area：邮件 id 里必须带 area 序号 ——
    /// 只用 `地图_季节` 的话后一个会**覆盖**前一个，前一个的物品静默消失
    /// （实测 `Custom_ForestWest` 有 7 个一次性 area）。
    /// </summary>
    [Fact]
    public void 同图同季多个一次性area各成一封邮件()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [
            { "UniqueAreaID": "A", "MapName": "M", "SpringItemIndex": [ 16 ],
              "ExtraConditions": { "LimitedNumberOfSpawns": 1 } },
            { "UniqueAreaID": "B", "MapName": "M", "SpringItemIndex": [ 18 ],
              "ExtraConditions": { "LimitedNumberOfSpawns": 1 } }
          ] }
        }
        """);

        FtmImportResult r = Import();
        var mails = r.Content["Changes"]!.OfType<JObject>()
            .Where(p => p["Target"]!.Value<string>() == "Data/Mail")
            .Select(p => (JObject)p["Entries"]!)
            .SelectMany(e => e.Properties()).ToList();

        Assert.Equal(2, mails.Count);                                        // 两封，不是一封
        Assert.Contains(mails, m => m.Value.Value<string>()!.Contains("(O)16"));
        Assert.Contains(mails, m => m.Value.Value<string>()!.Contains("(O)18"));
    }

    /// <summary>限次 **&gt; 1** 的没有对应机制（一次性动作只有"一次"）⇒ 整条跳过并报出。</summary>
    [Fact]
    public void 限次大于1时整条area不写()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "limited", "MapName": "Custom_Y",
            "SpringItemIndex": [ 16 ],
            "ExtraConditions": { "LimitedNumberOfSpawns": 3 }
          } ] }
        }
        """);

        FtmImportResult r = Import();
        Assert.Empty((JArray)r.Content["Changes"]!);
        Assert.Contains(r.Notes, n => n.Contains("限次刷新 3") && n.Contains("没写"));
    }

    /// <summary>
    /// 接线：`ContentPack.Load` 遇到 FTM 包要**用翻出来的那份**，并且把说明带出来
    /// （CLI 就是靠这个把"哪些内容拿不到"打给用户看的）。
    /// </summary>
    [Fact]
    public void ContentPack能直接读FTM包()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "a", "MapName": "Custom_Z", "SpringItemIndex": [ 16 ]
          } ] }
        }
        """);

        ContentPack pack = ContentPack.Load(_pack);

        Assert.Single(pack.Patches);
        Assert.Equal("EditData", pack.Patches[0].Action);
        Assert.Equal("Data/Locations", pack.Patches[0].Target);
        Assert.NotEmpty(pack.FtmNotes);
    }

    // ---------------------------------------------------------- 文件级条件 / 段开关

    /// <summary>
    /// ★ 文件级条件 <c>File_Conditions.OtherMods</c> 要**挂到每一条**补丁的 <c>When</c> 上
    /// （CP 形状 ⇒ 交给既有通路；`HasMod` 那类由 Baker 离线烘焙成真/假）。
    /// 不挂的后果：**装没装那个包都会刷**。
    /// </summary>
    [Fact]
    public void 文件级OtherMods挂成When()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "File_Conditions": { "FarmTypes": [], "FarmerNames": [], "SaveFileNames": [],
                               "OtherMods": { "FlashShifter.StardewValleyExpandedCP": true } },
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "a", "MapName": "Custom_X", "SpringItemIndex": [ 16 ]
          } ] }
        }
        """);

        JObject patch = (JObject)Import().Content["Changes"]![0]!;
        Assert.Equal(true, patch["When"]!["HasMod |contains=FlashShifter.StardewValleyExpandedCP"]!.Value<bool>());
    }

    /// <summary><c>FarmTypes</c> 对到 CP 的 <c>FarmType</c>（可翻译）；<c>OtherMods</c> 与它并存。</summary>
    [Fact]
    public void 文件级FarmTypes对成FarmType()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "File_Conditions": { "FarmTypes": [ "FrontierFarm" ], "OtherMods": { "Some.Mod": false } },
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "a", "MapName": "Custom_X", "SpringItemIndex": [ 16 ]
          } ] }
        }
        """);

        JObject when = (JObject)((JObject)Import().Content["Changes"]![0]!)["When"]!;
        Assert.Equal("FrontierFarm", when["FarmType"]!.Value<string>());
        Assert.Equal(false, when["HasMod |contains=Some.Mod"]!.Value<bool>());
    }

    /// <summary>
    /// ★ <c>FarmerNames</c>/<c>SaveFileNames</c> 是**运行时**才知道的（玩家名/存档名）——
    /// 静态表达不了 ⇒ **整份文件都不写**（宁可少写，不可写错），并报出来。
    /// </summary>
    [Fact]
    public void 运行时文件级条件导致整份不写()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "File_Conditions": { "FarmerNames": [ "Jodi" ] },
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "a", "MapName": "Custom_X", "SpringItemIndex": [ 16 ],
            "FallItemIndex": [ 18 ], "ExtraConditions": { "LimitedNumberOfSpawns": 1 }
          } ] }
        }
        """);

        FtmImportResult r = Import();
        Assert.Empty((JArray)r.Content["Changes"]!);          // 觅食 + 一次性邮寄**都不写**
        Assert.Contains(r.Notes, n => n.Contains("FarmerNames") && n.Contains("整份文件都没写"));
    }

    /// <summary><c>ForageSpawnEnabled: false</c> ⇒ 觅食那段**该包自己就没开**（不是我们的损失）。</summary>
    [Fact]
    public void 觅食段被关掉时不写也不报损失()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "ForageSpawnEnabled": false,
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "a", "MapName": "Custom_X", "SpringItemIndex": [ 16 ]
          } ] }
        }
        """);

        FtmImportResult r = Import();
        Assert.Empty((JArray)r.Content["Changes"]!);
        Assert.Contains(r.Notes, n => n.Contains("ForageSpawnEnabled") && n.Contains("自己就没开"));
    }

    /// <summary>同理：<c>MonsterSpawnEnabled: false</c> 时不该出现在"没写"的账里（那不是损失）。</summary>
    [Fact]
    public void 刷怪段被关掉时不报没写()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "MonsterSpawnEnabled": false,
          "Monster_Spawn_Settings": { "Areas": [ { "UniqueAreaID": "m", "MapName": "M" } ] }
        }
        """);

        FtmImportResult r = Import();
        // "没写"那条长这样：`FTM 刷怪 N 个 area（…）**没写**` —— 按"个 area"判别，别只看"没写"二字
        //（"自己就没开"那条里也有"没写"）。
        Assert.DoesNotContain(r.Notes, n => n.Contains("刷怪") && n.Contains("个 area"));
        Assert.Contains(r.Notes, n => n.Contains("MonsterSpawnEnabled") && n.Contains("自己就没开"));
    }


    /// <summary>
    /// ★ FTM 那些**放不下的旋钮**（刷在哪些图块上、至少几个、会不会过期、什么时段）要**报出来**：
    /// 原版 `Forage`/`ArtifactSpots` 是地点级的槽，没有这些字段 ⇒ 物品与季节对得上、**位置与数量这层丢了**。
    /// 不报的话，报告里"落了多少条"会让人以为整条 area 都还原了。
    /// </summary>
    [Fact]
    public void 放不下的旋钮要报出来()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "Forage_Spawn_Settings": {
            "CustomTileIndex": [ 32, 33 ],
            "PercentExtraSpawnsPerForagingLevel": 5,
            "Areas": [ {
              "UniqueAreaID": "a", "MapName": "Custom_X", "SpringItemIndex": [ 16 ],
              "MinimumSpawnsPerDay": 3, "ExcludeCoordinates": [ "1,1" ],
              "IncludeTerrainTypes": [ "Grass" ], "DaysUntilSpawnsExpire": 4,
              "SpawnTiming": "Day"
            } ]
          }
        }
        """);

        FtmImportResult r = Import();

        string note = Assert.Single(r.Notes, n => n.Contains("放不下的旋钮"));
        Assert.Contains("段级 CustomTileIndex", note);
        Assert.Contains("段级 PercentExtraSpawnsPerForagingLevel", note);
        Assert.Contains("MinimumSpawnsPerDay", note);
        Assert.Contains("ExcludeCoordinates", note);
        Assert.Contains("IncludeTerrainTypes", note);
        Assert.Contains("DaysUntilSpawnsExpire", note);
        Assert.Contains("SpawnTiming", note);
        Assert.Contains("位置与数量这层丢了", note);
    }


    /// <summary>
    /// ★ `File_Conditions` 的**形状与 FTM 模板不符**时**整份不转**（别静默当"没有"）——
    /// 当"没有"的后果是把"只在某个玩家/农场类型下才生效"的整份文件写成无条件（over-apply）。
    /// 形状依据：FTM 自己的模板 `data/default.json`（三个是数组、`OtherMods` 是对象）。
    /// </summary>
    [Fact]
    public void 文件级条件形状不符时整份不写()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "File_Conditions": { "OtherMods": [ "Some.Mod" ] },
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "a", "MapName": "Custom_X", "SpringItemIndex": [ 16 ]
          } ] }
        }
        """);

        FtmImportResult r = Import();
        Assert.Empty((JArray)r.Content["Changes"]!);
        Assert.Contains(r.Notes, n => n.Contains("OtherMods") && n.Contains("形状与 FTM 模板不符"));
    }

    /// <summary>`FarmTypes` 写成字符串同样整份不写（形状不符 ⇒ 不知道它要什么条件）。</summary>
    [Fact]
    public void 农场类型不是数组时整份不写()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "File_Conditions": { "FarmTypes": "FrontierFarm" },
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "a", "MapName": "Custom_X", "SpringItemIndex": [ 16 ]
          } ] }
        }
        """);

        FtmImportResult r = Import();
        Assert.Empty((JArray)r.Content["Changes"]!);
        Assert.Contains(r.Notes, n => n.Contains("FarmTypes") && n.Contains("不是数组"));
    }

    /// <summary>
    /// ★ **FTM 包不许套“旧格式迁移”那道守卫**（实测踩过）。
    /// <para>
    /// 翻译器把译出来的 CP 形状根**硬写成 `Format: 1.28.0`**（早于 2.0.0），而 FTM 的产物落点
    /// 恰好是 `Data/Locations` —— 正在 `Migration_2_0` 的表里。于是守卫把**我们自己合成的**
    /// 1.6 形状补丁判成“CP 会迁移它、我们没实现”，26 条觅食**一条都没写**
    /// （产出树 `Data/Locations.json` 从 920 KB 掉到 700 KB，`FarmCave.Forage` 12 项 → 0）。
    /// CP 压根不处理 FTM 包 ⇒ 判据是 <see cref="ContentPack.FormatForCpMigration">（FTM 包恒为 null）。
    /// </para>
    /// </summary>
    [Fact]
    public void FTM包的合成补丁不套旧格式迁移守卫()
    {
        Manifest(FtmPackTranslator.FtmUniqueId);
        Write("content.json", """
        {
          "ForageSpawnEnabled": true,
          "Forage_Spawn_Settings": { "Areas": [ {
            "UniqueAreaID": "test", "MapName": "Custom_Woods", "SpringItemIndex": [ 16 ]
          } ] }
        }
        """);

        var pack = ContentPack.Load(_pack);

        Assert.True(pack.IsFtmPack);
        Assert.Equal(new Version(1, 28, 0), pack.FormatVersion);       // 翻译器硬写的那个
        Assert.Null(pack.FormatForCpMigration);                     // ★ 但迁移守卫看到的是 null

        var judge = new PatchJudge(new ResourceTree(_pack), _pack, null, null, null, null,
            pack.UniqueId, pack.FormatForCpMigration);
        PatchVerdict verdict = judge.Judge(Assert.Single(pack.Patches));

        // 树里没有 Data/Locations（空临时目录）⇒ 判 CreateNewAsset 是对的；
        // 这里要钉的只有**守卫没开火**。
        Assert.NotEqual(Verdict.NeedsReview, verdict.Verdict);
        Assert.DoesNotContain("Migration_2_0", verdict.Reason);
    }
}
