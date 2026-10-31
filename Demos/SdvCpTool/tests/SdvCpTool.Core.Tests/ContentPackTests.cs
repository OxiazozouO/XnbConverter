using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;

namespace SdvCpTool.Core.Tests;

public class ContentPackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvcp_" + Guid.NewGuid().ToString("N"));

    public ContentPackTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string relative, string content)
    {
        string full = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void 读单个content_json()
    {
        Write("content.json", """
        {
          "Format": "2.0.0",
          "Changes": [
            { "Action": "EditData", "Target": "Data/Objects", "Entries": { "a": "b" } }
          ]
        }
        """);

        var pack = ContentPack.Load(_dir);

        var patch = Assert.Single(pack.Patches);
        Assert.Equal("EditData", patch.Action);
        Assert.Equal("Data/Objects", patch.Target);
        Assert.Equal(0, patch.Index);
    }

    /// <summary>
    /// ★ 目标名结尾的 `.xnb` 要剥掉 —— CP 就是这么做的（`PatchLoader.cs:151-158`）。
    /// <para>
    /// 不剥的代价实测过：包「佳能友好对话扩展」8 条 `"Target": "Data/Festivals/spring13.xnb"`
    /// 在树里查不到（树里是 `spring13.json`）⇒ 被当成**新建**资产，
    /// 报告里对一个并不存在的资产说落点。而 `FromFile` 是**真路径**，不能碰。
    /// </para>
    /// </summary>
    [Fact]
    public void 目标名结尾的xnb要剥掉()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "EditData", "Target": "Data/Festivals/spring13.xnb", "Entries": { "a": "b" } },
            { "Action": "EditImage", "Target": "Maps/Town.XNB", "FromFile": "town.xnb" },
            { "Action": "EditData", "Target": "Data/A.xnb, Data/B.xnb", "Entries": { "a": "b" } }
          ]
        }
        """);

        var pack = ContentPack.Load(_dir);

        Assert.Equal("Data/Festivals/spring13", pack.Patches[0].Target);
        Assert.Equal("Maps/Town", pack.Patches[1].Target);
        Assert.Equal("town.xnb", pack.Patches[1].FromFile);   // 路径不剥（这条 FromFile 本来就叫这名）
        Assert.Equal("Data/A,Data/B", pack.Patches[2].Target); // 逗号多值逐段剥
    }

    [Fact]
    public void Include的FromFile相对内容包根目录()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "Include", "FromFile": "Data/extra.json" },
            { "Action": "EditImage", "Target": "Characters/Abigail" }
          ]
        }
        """);
        Write("Data/extra.json", """
        {
          "Changes": [
            { "Action": "EditData", "Target": "Data/Fish" }
          ]
        }
        """);

        var pack = ContentPack.Load(_dir);

        Assert.Equal(2, pack.Patches.Count);
        Assert.DoesNotContain(pack.Patches, p => p.Action == "Include");
        Assert.Contains(pack.Patches, p => p.Target == "Data/Fish");
    }

    [Fact]
    public void Include支持逗号分隔的多个文件()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "Include", "FromFile": "a.json, b.json" }
          ]
        }
        """);
        Write("a.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/A" } ] }""");
        Write("b.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/B" } ] }""");

        var pack = ContentPack.Load(_dir);

        Assert.Equal(2, pack.Patches.Count);
    }

    /// <summary>
    /// Include 的文件列表在解析阶段展开，早于补丁级配置令牌代换 ⇒ 这里必须自己代。
    /// 实测 Bear Family 的 `Data/{{SeasonalOutfits}}_NPCData.json`：不代则整个 Include 被当成
    /// 「目标不存在」丢掉，Sigurd/Gunnar/Teren 三条 `Data/Characters` 跟婚姻内容一起消失。
    /// </summary>
    [Fact]
    public void Include的FromFile里的配置令牌用config值代换()
    {
        Write("content.json", """
        {
          "ConfigSchema": { "Style": { "AllowValues": "full, lite", "Default": "full" } },
          "Changes": [
            { "Action": "Include", "FromFile": "Data/{{Style}}_NPCData.json" }
          ]
        }
        """);
        Write("config.json", """{ "Style": "lite" }""");
        Write("Data/lite_NPCData.json",
            """{ "Changes": [ { "Action": "EditData", "Target": "Data/Lite" } ] }""");
        Write("Data/full_NPCData.json",
            """{ "Changes": [ { "Action": "EditData", "Target": "Data/Full" } ] }""");

        var pack = ContentPack.Load(_dir);

        Assert.Equal("Data/Lite", Assert.Single(pack.Patches).Target);
        Assert.Empty(pack.Warnings);
    }

    /// <summary>
    /// Include 路径里也可以写**动态令牌**：Load 时还没有求值器，代不掉；
    /// `ResolveIncludes` 必须带展开器重建补丁列表（Rose and the Alchemist 的
    /// `Data/Locations/{{Animal_Shop_Owner}}_Animals.json`，整份动物数据）。
    /// </summary>
    [Fact]
    public void Include的FromFile里的动态令牌在ResolveIncludes时展开()
    {
        Write("content.json", """
        {
          "Format": "2.0.0",
          "DynamicTokens": [
            { "Name": "Which", "Value": "A" }
          ],
          "Changes": [
            { "Action": "Include", "FromFile": "Data/{{Which}}.json" }
          ]
        }
        """);
        Write("Data/A.json",
            """{ "Changes": [ { "Action": "Load", "Target": "Data/A", "FromFile": "x.json" } ] }""");

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);
        DynamicTokenResolver tokens = DynamicTokenResolver.Load(_dir, null);

        // 空 When 必须自己按无条件展开：交给求值器会被当“判不了”并内联，
        // 内联里的 `Load` 又会被跳过（Rose 的动物素材就是这么丢的）。
        pack.ResolveIncludes(_ => IncludeDecision.Unknown("空 When 不该问求值器"), path =>
        {
            DynamicTokenResolver.Expansion expanded = tokens.Expand(path);
            return expanded.Unresolved.Count == 0 ? expanded.Text : null;
        });

        Assert.Equal("Data/A", Assert.Single(pack.Patches).Target);
        Assert.DoesNotContain(pack.Warnings, w => w.Contains("目标不存在"));
    }

    [Fact]
    public void Include的FromFile相对内容包根而非当前文件()
    {
        Write("content.json", """{ "Changes": [ { "Action": "Include", "FromFile": "sub/a.json" } ] }""");
        Write("sub/a.json", """{ "Changes": [ { "Action": "Include", "FromFile": "sub/b.json" } ] }""");
        Write("sub/b.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/Deep" } ] }""");

        var pack = ContentPack.Load(_dir);

        Assert.Equal("Data/Deep", Assert.Single(pack.Patches).Target);
    }

    [Fact]
    public void Include文件缺失时记录警告且继续()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "Include", "FromFile": "missing.json" },
            { "Action": "EditData", "Target": "Data/Ok" }
          ]
        }
        """);

        var pack = ContentPack.Load(_dir);

        Assert.Single(pack.Patches);
        Assert.Single(pack.Warnings);
    }

    [Fact]
    public void Include成环不死循环()
    {
        Write("content.json", """{ "Changes": [ { "Action": "Include", "FromFile": "loop.json" } ] }""");
        Write("loop.json", """{ "Changes": [ { "Action": "Include", "FromFile": "loop.json" } ] }""");

        var pack = ContentPack.Load(_dir);

        Assert.Empty(pack.Patches);
    }

    [Fact]
    public void 保留When原文()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "EditData", "Target": "Data/Objects",
              "When": { "Season": "spring", "HasSeenEvent": "123" } }
          ]
        }
        """);

        var pack = ContentPack.Load(_dir);

        Assert.NotNull(Assert.Single(pack.Patches).When);
    }

    [Fact]
    public void 读manifest的UniqueID与Name()
    {
        Write("manifest.json", """{ "Name": "Test Pack", "UniqueID": "Tester.Test" }""");
        Write("content.json", """{ "Changes": [] }""");

        var pack = ContentPack.Load(_dir);

        Assert.Equal("Test Pack", pack.Name);
        Assert.Equal("Tester.Test", pack.UniqueId);
    }

    [Fact]
    public void 缺content_json时抛异常()
    {
        Assert.Throws<FileNotFoundException>(() => ContentPack.Load(_dir));
    }

    // ---- Task 4c：Priority 解析（取值见 SMAPI AssetEditPriority / AssetLoadPriority）----

    [Theory]
    [InlineData(null, 0)]            // EditData 缺省 = Default
    [InlineData("Early", -1000)]
    [InlineData("Late", 1000)]
    [InlineData("Default", 0)]
    [InlineData("default + 2", 2)]   // 偏移写法，大小写不敏感
    [InlineData("Late - 10", 990)]
    public void 解析EditData的Priority(string? priority, int expected)
    {
        string field = priority == null ? "" : $", \"Priority\": \"{priority}\"";
        Write("content.json",
            $$"""{ "Changes": [ { "Action": "EditData", "Target": "Data/X"{{field}} } ] }""");

        var pack = ContentPack.Load(_dir);

        Assert.Equal(expected, Assert.Single(pack.Patches).Priority);
        Assert.Empty(pack.Warnings);
    }

    [Theory]
    [InlineData(null, int.MaxValue)]  // Load 缺省 = Exclusive
    [InlineData("Low", -1000)]
    [InlineData("Medium", 0)]
    [InlineData("High", 1000)]
    [InlineData("Exclusive", int.MaxValue)]
    [InlineData("High + 2", 1002)]
    public void 解析Load的Priority(string? priority, int expected)
    {
        string field = priority == null ? "" : $", \"Priority\": \"{priority}\"";
        Write("content.json",
            $$"""{ "Changes": [ { "Action": "Load", "Target": "Data/X", "FromFile": "x.json"{{field}} } ] }""");

        var pack = ContentPack.Load(_dir);

        Assert.Equal(expected, Assert.Single(pack.Patches).Priority);
        Assert.Empty(pack.Warnings);
    }

    [Fact]
    public void Priority非法时记警告并按缺省处理()
    {
        // "Normal" 不是合法取值（CP 没有 Normal）；纯数字也不合法。
        Write("content.json",
            """{ "Changes": [ { "Action": "EditData", "Target": "Data/X", "Priority": "Normal" } ] }""");

        var pack = ContentPack.Load(_dir);

        Assert.Equal(0, Assert.Single(pack.Patches).Priority);
        Assert.Single(pack.Warnings);
    }

    // ---- Task 7c：Include 上的 When 求值 ----

    [Fact]
    public void 默认Load仍无条件展开带When的Include()
    {
        // 向后兼容：不主动求值时（如 translate 静态清单）维持旧行为，全部展开。
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "gated.json", "When": { "Season": "spring" } } ] }
        """);
        Write("gated.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/Gated" } ] }""");

        var pack = ContentPack.Load(_dir);

        Assert.Equal("Data/Gated", Assert.Single(pack.Patches).Target);
        Assert.Empty(pack.IncludeResolutions);
    }

    [Fact]
    public void defer模式下带When的Include不立即展开()
    {
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "gated.json", "When": { "HasMod": "Some.Mod" } } ] }
        """);
        Write("gated.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/Gated" } ] }""");

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);

        Assert.Empty(pack.Patches);                 // 未求值前不得展开
        Assert.Empty(pack.IncludeResolutions);      // 也还没结论
    }

    [Fact]
    public void defer模式下无When的Include照常展开()
    {
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "plain.json" } ] }
        """);
        Write("plain.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/Plain" } ] }""");

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);

        Assert.Equal("Data/Plain", Assert.Single(pack.Patches).Target);
    }

    [Fact]
    public void Include条件为真时展开()
    {
        Write("content.json", """
        { "Changes": [ { "Action": "EditData", "Target": "Data/Before" },
                       { "Action": "Include", "LogName": "Gated", "FromFile": "gated.json",
                         "When": { "HasMod |contains=Some.Mod": false } },
                       { "Action": "EditData", "Target": "Data/After" } ] }
        """);
        Write("gated.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/Gated" } ] }""");

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);
        var resolutions = pack.ResolveIncludes(_ => IncludeDecision.Yes());

        // 顺序必须保持 Include 在原文中的位置
        Assert.Equal(new[] { "Data/Before", "Data/Gated", "Data/After" },
            pack.Patches.Select(p => p.Target).ToArray());
        var res = Assert.Single(resolutions);
        Assert.Equal(IncludeOutcome.Expanded, res.Outcome);
        Assert.Equal("Gated", res.LogName);
    }

    [Fact]
    public void Include条件为假时跳过()
    {
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "gated.json", "When": { "HasMod": "Some.Mod" } },
                       { "Action": "EditData", "Target": "Data/Other" } ] }
        """);
        Write("gated.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/Gated" } ] }""");

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);
        var resolutions = pack.ResolveIncludes(_ => IncludeDecision.No("该模组没装"));

        Assert.Equal("Data/Other", Assert.Single(pack.Patches).Target);
        Assert.Equal(IncludeOutcome.Skipped, Assert.Single(resolutions).Outcome);
    }

    [Fact]
    public void Include条件判不了时内联并把条件带上()
    {
        // ★ 判不了**不等于丢**：CP 是「条件成立才加载 children」，效果等价于
        //   「children 各自带上那个条件」。丢掉会白丢内容，而这些条件里有可译的
        //   （`HasSeenEvent`/`HasWalletItem` 都有原版 GSQ）。
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "gated.json",
                         "When": { "HasSeenEvent": "123" } } ] }
        """);
        Write("gated.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/Gated" } ] }""");

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);
        var resolutions = pack.ResolveIncludes(_ => IncludeDecision.Unknown("`HasSeenEvent` 不是可烘焙令牌"));

        var patch = Assert.Single(pack.Patches);
        Assert.Equal("Data/Gated", patch.Target);
        Assert.Equal("123", patch.When!["HasSeenEvent"]!.Value<string>());   // 条件带上了

        var res = Assert.Single(resolutions);
        Assert.Equal(IncludeOutcome.Inlined, res.Outcome);
        Assert.Contains("HasSeenEvent", res.Reason);
    }

    /// <summary>子补丁自己的 <c>When</c> 与 Include 的**相与**（两边键都留着）。</summary>
    [Fact]
    public void 内联时与子补丁自己的When相与()
    {
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "gated.json",
                         "When": { "HasSeenEvent": "123" } } ] }
        """);
        Write("gated.json", """
        { "Changes": [ { "Action": "EditData", "Target": "Data/Gated",
                         "When": { "Season": "spring" } } ] }
        """);

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);
        pack.ResolveIncludes(_ => IncludeDecision.Unknown("判不了"));

        var patch = Assert.Single(pack.Patches);
        Assert.Equal("123", patch.When!["HasSeenEvent"]!.Value<string>());
        Assert.Equal("spring", patch.When["Season"]!.Value<string>());
    }

    /// <summary>键冲突（同一键、不同值）时**不内联** —— 相与要算集合交集，那是猜。</summary>
    [Fact]
    public void 内联时键冲突则跳过并记警告()
    {
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "gated.json",
                         "When": { "Season": "fall" } } ] }
        """);
        Write("gated.json", """
        { "Changes": [ { "Action": "EditData", "Target": "Data/Gated",
                         "When": { "Season": "spring" } } ] }
        """);

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);
        pack.ResolveIncludes(_ => IncludeDecision.Unknown("判不了"));

        Assert.Empty(pack.Patches);
        Assert.Contains(pack.Warnings, w => w.Contains("冲突"));
    }

    /// <summary>
    /// 条件化 Include 里的 <c>Load</c> **不内联**：`Load` 是创建资产，
    /// 资产「有或没有」，表达不了「有条件地有」—— 写成无条件版本就是写错。
    /// </summary>
    [Fact]
    public void 内联时Load子补丁被跳过()
    {
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "gated.json",
                         "When": { "HasSeenEvent": "123" } } ] }
        """);
        Write("gated.json", """
        { "Changes": [
            { "Action": "Load",     "Target": "Data/Gated", "FromFile": "blank.json" },
            { "Action": "EditData", "Target": "Data/Other" } ] }
        """);

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);
        pack.ResolveIncludes(_ => IncludeDecision.Unknown("判不了"));

        var patch = Assert.Single(pack.Patches);          // EditData 进了，Load 没进
        Assert.Equal("Data/Other", patch.Target);
        Assert.Contains(pack.Warnings, w => w.Contains("无法带条件"));
    }

    /// <summary>
    /// ★ `Target` 里的 `<c>{{ModId}}</c>` 换成**本包的 UniqueID** —— 它是不可变令牌、静态可求值，
    /// 不换的话整条补丁会被判「资产名含令牌」而拒（实测语料 8 个包共 1037 处用这个写法）。
    /// </summary>
    [Fact]
    public void Target里的ModId换成包的UniqueID()
    {
        Write("manifest.json", """
        { "Name": "Test", "Author": "me", "Version": "1.0.0",
          "UniqueID": "Aimon.WitchSwamp", "ContentPackFor": { "UniqueID": "Pathoschild.ContentPatcher" } }
        """);
        Write("content.json", """
        { "Changes": [
            { "Action": "Load",     "Target": "Mods/{{ModId}}/Objects", "FromFile": "assets/o.png" },
            { "Action": "EditImage", "Target": "Characters/{{modid}}_Zic" },
            { "Action": "EditImage", "Target": "Characters/{{spouse}}" } ] }
        """);

        var pack = ContentPack.Load(_dir);

        Assert.Equal("Mods/Aimon.WitchSwamp/Objects", pack.Patches[0].Target);
        Assert.Equal("Characters/Aimon.WitchSwamp_Zic", pack.Patches[1].Target);   // 大小写不敏感
        Assert.Equal("Characters/{{spouse}}", pack.Patches[2].Target);             // 运行时的令牌不动
    }

    [Fact]
    public void 求值器收到的是Include上的When()
    {
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "gated.json",
                         "When": { "HasMod |contains=The.Mod": false } } ] }
        """);
        Write("gated.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/Gated" } ] }""");

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);
        JObject? seen = null;
        pack.ResolveIncludes(when => { seen = when; return IncludeDecision.Yes(); });

        Assert.NotNull(seen);
        Assert.False(seen!["HasMod |contains=The.Mod"]!.Value<bool>());
    }

    [Fact]
    public void 展开的Include内部的无条件Include一并展开()
    {
        Write("content.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "gated.json", "When": { "HasMod": "Some.Mod" } } ] }
        """);
        Write("gated.json", """
        { "Changes": [ { "Action": "Include", "FromFile": "inner.json" } ] }
        """);
        Write("inner.json", """{ "Changes": [ { "Action": "EditData", "Target": "Data/Inner" } ] }""");

        var pack = ContentPack.Load(_dir, deferConditionalIncludes: true);
        pack.ResolveIncludes(_ => IncludeDecision.Yes());

        Assert.Equal("Data/Inner", Assert.Single(pack.Patches).Target);
    }

    /// <summary>
    /// ★ CP 的 <c>CustomLocations</c>（<c>content.json</c> 根上的独立字段，不在 <c>Changes</c> 里）
    /// 要展开成**两条**东西：<c>Maps/&lt;Name&gt;</c> 的 <c>Load</c>，
    /// 以及 <c>Data/Locations</c> 里补的 <c>CreateOnLoad</c> 登记。
    /// <para>
    /// 依据 CP <c>CustomLocationManager.cs</c>：<c>:184</c> 的
    /// <c>e.LoadFrom(… ModContent.Load&lt;Map&gt;(FromMapFile) …)</c>（加载优先级最大）与
    /// <c>:140-172</c> 的 <c>Data/Locations</c> 登记（编辑优先级 -1000）。
    /// 缺任何一个，游戏要么取不到地图、要么根本不创建这个地点。
    /// </para>
    /// </summary>
    [Fact]
    public void CustomLocations展开成Load与地点登记()
    {
        Write("content.json", """
        { "Format": "2.0.0",
          "Changes": [],
          "CustomLocations": [
            { "Name": "Custom_AndyHouse", "FromMapFile": "assets/Maps/NewLocations/AndyHouse.tbin",
              "MigrateLegacyNames": [ "AndyHouseOld" ] } ] }
        """);
        ContentPack pack = ContentPack.Load(_dir);

        int added = pack.ExpandCustomLocations();

        Assert.Equal(1, added);
        ChangePatch load = Assert.Single(pack.Patches, p => p.Action == "Load");
        Assert.Equal("Maps/Custom_AndyHouse", load.Target);
        Assert.Equal("assets/Maps/NewLocations/AndyHouse.tbin", load.FromFile);

        ChangePatch data = Assert.Single(pack.Patches, p => p.Action == "EditData");
        Assert.Equal("Data/Locations", data.Target);
        Assert.Equal("Maps/Custom_AndyHouse",
            data.Raw["Entries"]!["Custom_AndyHouse"]!["CreateOnLoad"]!["MapPath"]!.Value<string>());
        Assert.Equal("AndyHouseOld",
            data.Raw["Entries"]!["Custom_AndyHouse"]!["FormerLocationNames"]![0]!.Value<string>());
    }

    /// <summary>
    /// ⚠ 名字已在 <c>Data/Locations</c> 里时**跳过** —— CP 是「只记警告、不覆盖」
    /// （<c>CustomLocationManager.cs:152-160</c>）。照 <c>EditData</c> 的常规语义写下去，
    /// 会把原版那条地点数据**整个替换掉**。
    /// </summary>
    [Fact]
    public void CustomLocations名字撞车时跳过()
    {
        Write("content.json", """
        { "Format": "2.0.0", "Changes": [],
          "CustomLocations": [ { "Name": "Town", "FromMapFile": "a.tbin" } ] }
        """);
        ContentPack pack = ContentPack.Load(_dir);

        int added = pack.ExpandCustomLocations(name => name == "Town");

        Assert.Equal(0, added);
        Assert.DoesNotContain(pack.Patches, p => p.Action == "EditData");
    }

    /// <summary>
    /// ★ CP 的 <c>Enabled</c>：现代格式（≥ 1.25.0）里该字段**已废弃** —— 只要不是字面 <c>true</c>，
    /// 整条补丁被**跳过**（<c>PatchLoader.cs:359-364</c>，理由是「该字段已废弃，应当删除」，连警告都不打）。
    /// 不读它的后果是**把作者禁用的补丁写进产物**。
    /// </summary>
    [Fact]
    public void Enabled不是true时整条跳过()
    {
        Write("content.json", """
        { "Format": "2.0.0", "Changes": [
          { "Action": "EditData", "Target": "Data/A", "Enabled": false },
          { "Action": "EditData", "Target": "Data/B", "Enabled": "{{HasMod |contains=X}}" },
          { "Action": "EditData", "Target": "Data/C", "Enabled": true },
          { "Action": "EditData", "Target": "Data/D" } ] }
        """);

        ContentPack pack = ContentPack.Load(_dir);

        Assert.Equal(new[] { "Data/C", "Data/D" }, pack.Patches.Select(p => p.Target).ToArray());
        Assert.Contains(pack.Warnings, w => w.Contains("Enabled"));
    }

    /// <summary>旧格式（&lt; 1.25.0）的口径不同：只认字面 <c>false</c>。</summary>
    [Fact]
    public void 旧格式的Enabled只认字面false()
    {
        Write("content.json", """
        { "Format": "1.23.0", "Changes": [
          { "Action": "EditData", "Target": "Data/A", "Enabled": false },
          { "Action": "EditData", "Target": "Data/B", "Enabled": "{{HasMod}}" } ] }
        """);

        ContentPack pack = ContentPack.Load(_dir);

        Assert.Equal(new[] { "Data/B" }, pack.Patches.Select(p => p.Target).ToArray());
    }


    // ---------------------------------------------------------- 补丁字段守卫

    /// <summary>
    /// ★ 补丁里有**我们不认识的键**要报出来（别静默按"它不存在"处理）。
    /// 这条是 FTM 那次的教训的直接产物：漏读了 `SpawnEnabled`/`File_Conditions` 两个字段，
    /// 语料里恰好不触发才没出事。
    /// </summary>
    [Fact]
    public void 不认识的补丁字段会报警()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "EditData", "Target": "Data/Objects", "Entries": { "a": "b" },
              "Whatever": "zh-CN" }
          ]
        }
        """);

        ContentPack pack = ContentPack.Load(_dir);

        Assert.Contains(pack.Warnings, w => w.Contains("不认识的字段") && w.Contains("Whatever"));
        Assert.Single(pack.Patches);                 // 仍然照常收进补丁表（只是报出来）
    }

    /// <summary>
    /// ★ CP 认识、**我们没实现**的字段要**单独**报一条（认识它 ≠ 处理它）——
    /// 不然读者会把"没报警"当成"处理了"。代表例子就是 `TargetLocale`。
    /// </summary>
    [Fact]
    public void 认识但没实现的字段单独报一条()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "EditData", "Target": "Data/Objects", "Entries": { "a": "b" },
              "TargetLocale": "zh-CN" }
          ]
        }
        """);

        ContentPack pack = ContentPack.Load(_dir);

        Assert.Contains(pack.Warnings, w => w.Contains("TargetLocale") && w.Contains("没生效"));
        Assert.DoesNotContain(pack.Warnings, w => w.Contains("不认识的字段"));
    }

    /// <summary>认识的字段**不许**报警（误报会把报告淹掉）。</summary>
    [Fact]
    public void 认识的补丁字段不报警()
    {
        Write("content.json", """
        {
          "Changes": [
            { "LogName": "x", "Action": "EditData", "Target": "Data/Objects",
              "When": { "Season": "Spring" }, "Update": "OnDayStart",
              "Entries": { "a": "b" } },
            { "Action": "EditImage", "Target": "Characters/Abigail", "FromFile": "a.png",
              "FromArea": { "X": 0, "Y": 0, "Width": 16, "Height": 32 },
              "ToArea": { "X": 0, "Y": 0, "Width": 16, "Height": 32 },
              "PatchMode": "Replace" }
          ]
        }
        """);

        ContentPack pack = ContentPack.Load(_dir);

        Assert.DoesNotContain(pack.Warnings, w => w.Contains("不认识的字段"));
    }


    /// <summary>
    /// ★ 字段名**大小写不敏感**（CP 是反序列化进 `PatchConfig`，JSON.NET 的属性匹配不区分大小写）。
    /// 实测语料里就有：SVE 写 `"Patchmode": "Overlay"` 与 `"when": {…}` ——
    /// 不规范化的话前者会被当成默认的 `Replace`（**整块覆盖**），后者整条条件被丢掉。
    /// </summary>
    [Fact]
    public void 字段名大小写不敏感()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "EditData", "Target": "Data/Objects",
              "when": { "Season": "Spring" }, "entries": { "a": "b" } },
            { "Action": "EditImage", "Target": "Characters/Abigail", "FromFile": "a.png",
              "Patchmode": "Overlay" }
          ]
        }
        """);

        ContentPack pack = ContentPack.Load(_dir);

        Assert.Equal("Spring", pack.Patches[0].When!["Season"]!.Value<string>());       // `when` 认了
        Assert.NotNull(pack.Patches[0].Raw["Entries"]);                                 // `entries` 认了
        Assert.Equal("Overlay", pack.Patches[1].Raw["PatchMode"]!.Value<string>());     // `Patchmode` → 规范拼写
        Assert.DoesNotContain(pack.Warnings, w => w.Contains("不认识的字段"));           // 规范化之后不该再报
    }


    /// <summary>
    /// `MigrateLegacyNames` 这类**属于别的配置对象**的字段要如实说明"CP 自己也不读"
    /// —— 免得有人把它当成我们的漏读去"修"（SVE 有 13 处把它写在 `Load` 补丁上）。
    /// </summary>
    [Fact]
    public void 属于别处的字段标明CP也不读()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "Load", "Target": "Maps/X", "FromFile": "a.tbin",
              "MigrateLegacyNames": [ "OldName" ] }
          ]
        }
        """);

        ContentPack pack = ContentPack.Load(_dir);

        Assert.Contains(pack.Warnings, w => w.Contains("MigrateLegacyNames") && w.Contains("CP 自己也不会读"));
        Assert.DoesNotContain(pack.Warnings, w => w.Contains("我们不认识的字段"));
    }


    /// <summary>
    /// ★ 同一条补丁里 `when` 与 `When` **并存**时，按 CP 的口径**最后一个赢**
    /// （CP 反序列化进 `PatchConfig`，Newtonsoft 对重名键取最后一个）。
    /// 审查复现过：只"跳过已存在的规范键"会把**先**出现的值留下，与 CP 不一致。
    /// </summary>
    [Fact]
    public void 同名字段并存时最后一个赢()
    {
        Write("content.json", """
        {
          "Changes": [
            { "Action": "EditData", "Target": "Data/Objects", "Entries": { "a": "b" },
              "when": { "Season": "Spring" }, "When": { "Season": "Summer" } }
          ]
        }
        """);

        ContentPack pack = ContentPack.Load(_dir);

        Assert.Equal("Summer", pack.Patches[0].When!["Season"]!.Value<string>());   // 后来者赢
        Assert.Equal(1, pack.Patches[0].Raw.Properties().Count(p =>
            p.Name.Equals("When", StringComparison.OrdinalIgnoreCase)));            // 只剩一个
    }

    /// <summary>
    /// ★ **令牌的版本门槛**：声明旧 `Format` 的包用了它那个版本还没有的令牌
    /// ⇒ CP 会拒收整份 content.json（`BaseMigration.cs:32-41`）。我们照转就是“作者那边根本不生效的内容”。
    /// <para>正面对照：语料里 `{{ModId}}` 的使用者**全部**声明 `Format ≥ 2.0` —— 而 `ModId` 正是 2.0 的新增令牌。</para>
    /// </summary>
    [Fact]
    public void 旧格式包用了新令牌要报出来()
    {
        Write("content.json", """
        {
          "Format": "1.29.0",
          "Changes": [
            { "Action": "Load", "Target": "{{ModId}}/Thing", "FromFile": "x.png" }
          ]
        }
        """);

        var pack = ContentPack.Load(_dir);

        Assert.Contains(pack.Warnings, w => w.Contains("ModId") && w.Contains("拒收整份文件"));
    }

    /// <summary>同一个令牌，版本够新就不该报（语料里那 8 个包就是这种）。</summary>
    [Fact]
    public void 版本够新时不报令牌门槛()
    {
        Write("content.json", """
        {
          "Format": "2.0.0",
          "Changes": [
            { "Action": "Load", "Target": "{{ModId}}/Thing", "FromFile": "x.png" }
          ]
        }
        """);

        var pack = ContentPack.Load(_dir);

        Assert.DoesNotContain(pack.Warnings, w => w.Contains("令牌版本门槛"));
    }
}
