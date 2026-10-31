using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Tests;

public class EditDataMergerTests
{
    private static JToken Apply(string targetJson, string patchJson) =>
        EditDataMerger.Apply(JToken.Parse(targetJson), JObject.Parse(patchJson)).Data!;

    [Fact]
    public void Entries新增条目()
    {
        var result = Apply("""{ "a": "1" }""",
            """{ "Entries": { "b": "2" } }""");

        Assert.Equal("1", result["a"]!.Value<string>());
        Assert.Equal("2", result["b"]!.Value<string>());
    }

    /// <summary>
    /// ★ 数组条目的键语义照抄 CP <c>ListKeyValueEditor</c>：
    /// <c>#&lt;整数&gt;</c> 是**下标**（越界/负数不命中 ⇒ 追加），其余按键匹配 ——
    /// 元素是**字符串**时**键就是字符串本身**。
    /// <para>
    /// <b>旧实现的代价</b>：非对象值被 `as JObject` 成 null，于是造了个 `new JObject()` 再填 `Id`，
    /// 把 <c>"berry_item"</c> 这种字符串元素写成了 <c>{"Id": "#-1"}</c> ——
    /// 值丢了，还写出游戏读不懂的元素（`Data/Objects` 的 `ContextTags` 是字符串数组，
    /// 打包直接失败：Cornucopia 两个包 23 + 20 个资产）。
    /// </para>
    /// <para>
    /// <b>命中就是整格替换，没有「逐字段合并」</b>：CP 的 <c>ApplyRecords</c> 一律
    /// <c>value = record.Value.ToObject(entryType); editor.SetEntry(key, value);</c>，
    /// 而 <c>ListKeyValueEditor.SetEntry</c> 命中走 <c>Data[index] = value</c>、
    /// 未命中走 <c>Data.Add(value)</c> —— 两条都是**整值**（反编译 <c>ContentPatcher.dll</c> 2.9.1 核实）。
    /// 逐字段合并只出现在 <c>Fields</c>（那边用 <c>Serializer.Populate</c> 灌进已有条目）。
    /// </para>
    /// </summary>
    [Fact]
    public void 数组条目支持井号下标与追加()
    {
        // `#-1`（负数，不命中）⇒ 追加**原字符串**。
        // 注意要用 `TargetField` 把 `Entries` 指到**那个数组**上 —— 这正是真实包的写法
        // （`{"TargetField": ["258","ContextTags"], "Entries": {"#-1":"berry_item"}}`）。
        var appended = Apply("""{ "16": { "ContextTags": [ "color_brown" ] } }""",
            """{ "TargetField": [ "16", "ContextTags" ], "Entries": { "#-1": "berry_item" } }""");
        Assert.Equal(new[] { "color_brown", "berry_item" },
            appended["16"]!["ContextTags"]!.Select(t => t.Value<string>()).ToArray());

        // `#0` 命中 ⇒ 整格替换
        var replaced = Apply("""{ "16": { "ContextTags": [ "old" ] } }""",
            """{ "TargetField": [ "16", "ContextTags" ], "Entries": { "#0": "new" } }""");
        Assert.Equal("new", replaced["16"]!["ContextTags"]![0]!.Value<string>());

        // `#999` 越界 ⇒ 同样追加（CP 的 `SetEntry` 走 `Data.Add`）
        var overflow = Apply("""{ "16": { "ContextTags": [ "a" ] } }""",
            """{ "TargetField": [ "16", "ContextTags" ], "Entries": { "#999": "b" } }""");
        Assert.Equal(2, overflow["16"]!["ContextTags"]!.Count());

        // 非下标键：字符串元素**按键匹配本身** ⇒ 替换
        var byValue = Apply("""{ "16": { "ContextTags": [ "old" ] } }""",
            """{ "TargetField": [ "16", "ContextTags" ], "Entries": { "old": "new" } }""");
        Assert.Equal("new", byValue["16"]!["ContextTags"]![0]!.Value<string>());

        // 值为 null ⇒ 删掉命中的那一格
        var removed = Apply("""{ "16": { "ContextTags": [ "old", "keep" ] } }""",
            """{ "TargetField": [ "16", "ContextTags" ], "Entries": { "#0": null } }""");
        Assert.Equal(new[] { "keep" }, removed["16"]!["ContextTags"]!.Select(t => t.Value<string>()).ToArray());
    }

    [Fact]
    public void Entries覆盖同键条目()
    {
        var result = Apply("""{ "a": "old" }""",
            """{ "Entries": { "a": "new" } }""");

        Assert.Equal("new", result["a"]!.Value<string>());
    }

    [Fact]
    public void Entries的null值表示删除条目()
    {
        // CP 语义：值为 null 即删除该键
        var result = Apply("""{ "a": "1", "b": "2" }""",
            """{ "Entries": { "a": null } }""");

        Assert.Null(result["a"]);
        Assert.Equal("2", result["b"]!.Value<string>());
    }

    /// <summary>
    /// `Data/Characters` 整条替换时，新条目没写 `Appearance` ⇒ 保住已有的。
    /// 实测：`[CP] East Scarp NPCs` 的整条 `CharacterData`（默认优先级）覆盖掉
    /// `Seasonal Cute Characters` 的 `Priority: Late` 外观（Eyvinder 7 条全丢）。
    /// CP 全局按 Priority 排序，外观补丁在最后；我们按包顺序跑，只能在替换时保字段。
    /// </summary>
    [Fact]
    public void Characters整条替换时保留已有Appearance()
    {
        var target = JObject.Parse("""
        { "Eyvinder": { "DisplayName": "旧", "Appearance": [ { "Id": "Spring" } ] } }
        """);
        var patch = JObject.Parse("""
        { "Entries": { "Eyvinder": { "DisplayName": "新" } } }
        """);

        MergeOutcome result = EditDataMerger.Apply(target, patch, "Data/Characters", takeOwnership: true);

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal("新", target["Eyvinder"]!["DisplayName"]!.Value<string>());
        Assert.Equal("Spring", target["Eyvinder"]!["Appearance"]![0]!["Id"]!.Value<string>());
        Assert.Contains(result.Notes, n => n.Contains("Appearance"));
    }

    /// <summary>别的资产不做这件事（`Entries` 就是整条替换）。</summary>
    [Fact]
    public void 非Characters资产整条替换不保留字段()
    {
        var target = JObject.Parse("""{ "A": { "X": 1, "Appearance": [ 1 ] } }""");
        var patch = JObject.Parse("""{ "Entries": { "A": { "X": 2 } } }""");

        EditDataMerger.Apply(target, patch, "Data/Objects", takeOwnership: true);

        Assert.Equal(2, target["A"]!["X"]!.Value<int>());
        Assert.Null(target["A"]!["Appearance"]);
    }

    [Fact]
    public void TargetField写入嵌套字段()
    {
        // 真实用例：Characters/Dialogue 的 Entries 要写进 Dialogue 字段
        var result = Apply("""{ "Dialogue": { "old": "x" } }""",
            """{ "TargetField": [ "Dialogue" ], "Entries": { "new": "y" } }""");

        Assert.Equal("x", result["Dialogue"]!["old"]!.Value<string>());
        Assert.Equal("y", result["Dialogue"]!["new"]!.Value<string>());
    }

    [Fact]
    public void TargetField多段路径()
    {
        var result = Apply("""{ "A": { "B": { "c": "1" } } }""",
            """{ "TargetField": [ "A", "B" ], "Entries": { "d": "2" } }""");

        Assert.Equal("2", result["A"]!["B"]!["d"]!.Value<string>());
    }

    [Fact]
    public void TargetField路径不存在时创建()
    {
        var result = Apply("""{ }""",
            """{ "TargetField": [ "A", "B" ], "Entries": { "c": "1" } }""");

        Assert.Equal("1", result["A"]!["B"]!["c"]!.Value<string>());
    }

    // ── TargetField 指到「还不存在的字段」时，容器种类要按**游戏模型**建 ──────────
    // ⚠ 实测踩过：SVE 季节性服装用 `TargetField: ["Alesia","Appearance"]` 往**非原版 NPC**
    //   的 `Appearance` 里写条目。`CharacterData.Appearance` 是
    //   `List<CharacterAppearanceData>`，只按「建个对象」会建成 `{ "<Id>": {...} }` ——
    //   19 个 NPC 的 `Appearance` 全成了对象，游戏按 `List` 反序列化会失败。
    //   同一条补丁打在**原版 NPC**（已有数组）上是对的，差别只在「容器要不要新建」。

    private static JToken ApplyAsset(string assetName, string targetJson, string patchJson) =>
        EditDataMerger.Apply(JToken.Parse(targetJson), JObject.Parse(patchJson), assetName).Data!;

    [Fact]
    public void TargetField指向列表字段且字段不存在时建数组而不是对象()
    {
        var result = ApplyAsset("Data/Characters", """{ "Alesia": { "DisplayName": "Alesia" } }""",
            """
            {
              "TargetField": [ "Alesia", "Appearance" ],
              "Entries": {
                "AlesiaSpring": { "Id": "AlesiaSpring", "Condition": "SEASON spring", "Precedence": -120 }
              }
            }
            """);

        JToken? appearance = result["Alesia"]!["Appearance"];
        Assert.Equal(JTokenType.Array, appearance!.Type);

        JToken item = Assert.Single(appearance.Children());
        Assert.Equal("AlesiaSpring", item["Id"]!.Value<string>());
        Assert.Equal("SEASON spring", item["Condition"]!.Value<string>());
    }

    [Fact]
    public void TargetField指向列表字段且条目也不存在时同样建数组()
    {
        var result = ApplyAsset("Data/Characters", """{ }""",
            """
            {
              "TargetField": [ "Alesia", "Appearance" ],
              "Entries": { "S": { "Id": "S", "Condition": "SEASON spring" } }
            }
            """);

        Assert.Equal(JTokenType.Array, result["Alesia"]!["Appearance"]!.Type);
    }

    /// <summary>字段已经存在且是数组时，照旧按 <c>Id</c> 匹配追加 —— 不能被新逻辑改坏。</summary>
    [Fact]
    public void TargetField指向已存在的列表字段时按Id追加()
    {
        var result = ApplyAsset("Data/Characters",
            """{ "Alesia": { "Appearance": [ { "Id": "Winter", "Precedence": -100 } ] } }""",
            """
            {
              "TargetField": [ "Alesia", "Appearance" ],
              "Entries": { "Spring": { "Id": "Spring", "Condition": "SEASON spring" } }
            }
            """);

        JToken appearance = result["Alesia"]!["Appearance"]!;
        Assert.Equal(2, appearance.Count());
        Assert.Equal("Winter", appearance[0]!["Id"]!.Value<string>());
        Assert.Equal("Spring", appearance[1]!["Id"]!.Value<string>());
    }

    // ── 字段名**大小写不敏感**（打包器与游戏都这么匹配）──────────────────────────
    // ⚠ 实测踩过：SVE 的 `Data/WorldMap` 用 `TargetField: ["Valley","MapAreas","Mountain",
    //   "ToolTips","Mines"]` 改原版地图提示 —— 模型字段是 `Tooltips`（小写 t），mod 写成
    //   `ToolTips`。按大小写敏感查表查不到 ⇒ 当成未知字段**新建了一个对象**，
    //   `Valley.MapAreas[7].ToolTips` 成了 `{ "Mines": {...} }`；
    //   打包器（大小写不敏感）照认这个键、按 `List<WorldMapTooltipData>` 校验 ⇒ **回包失败**。
    //   探针实测：把该对象改成数组（无论保留 `ToolTips` 还是改名 `Tooltips`）都能打出包。

    /// <summary>字段名大小写不同时，要**认出来并复用已有键**，而不是新建一个拼写不同的键。</summary>
    [Fact]
    public void TargetField字段名大小写不同时认已有键()
    {
        var result = ApplyAsset("Data/WorldMap",
            """
            {
              "Valley": {
                "MapAreas": [
                  { "Id": "Mountain", "Tooltips": [ { "Id": "Mines", "PixelArea": { "X": 1, "Y": 2 } } ] }
                ]
              }
            }
            """,
            """
            {
              "TargetField": [ "Valley", "MapAreas", "Mountain", "ToolTips", "Mines" ],
              "Entries": { "PixelArea": { "X": 213, "Y": 14, "Width": 7, "Height": 8 } }
            }
            """);

        JObject area = (JObject)result["Valley"]!["MapAreas"]![0]!;
        Assert.False(area.ContainsKey("ToolTips"));          // 没有多出一个拼写不同的键
        JToken tooltips = area["Tooltips"]!;
        Assert.Equal(JTokenType.Array, tooltips.Type);       // 也没被建成对象
        Assert.Equal(213, tooltips[0]!["PixelArea"]!["X"]!.Value<int>());
    }

    /// <summary>
    /// <b>字典键仍按精确匹配</b>：键是数据本身，大小写不同就是两个键 ——
    /// 不能被上一条的放宽带跑。
    /// </summary>
    [Fact]
    public void TargetField字典键仍精确匹配()
    {
        var result = ApplyAsset("Data/WorldMap", """{ "Valley": { } }""",
            """
            { "TargetField": [ "valley", "MapAreas" ], "Entries": { "#-1": { "Id": "X" } } }
            """);

        Assert.True(result["Valley"]!["MapAreas"] == null);   // 原键没被写
        Assert.Equal(JTokenType.Array, result["valley"]!["MapAreas"]!.Type);
    }

    /// <summary>模型不认得这个资产时保持原行为（字段名照旧精确匹配），不拿猜测覆盖。</summary>
    [Fact]
    public void TargetField模型不认得时不放宽大小写()
    {
        var result = ApplyAsset("Mods/Someone/Thing",
            """{ "A": { "Tooltips": [ 1 ] } }""",
            """{ "TargetField": [ "A", "ToolTips" ], "Entries": { "x": 1 } }""");

        Assert.Equal(1, result["A"]!["Tooltips"]![0]!.Value<int>());   // 原数组没被动
        Assert.Equal(JTokenType.Object, result["A"]!["ToolTips"]!.Type);
    }

    /// <summary>
    /// ★ 落点的值是 JSON <c>null</c> 时，要按「键不存在」处理 —— **按模型建空容器**。
    /// <para>
    /// 实测踩过：原版 <c>Data/FarmAnimals</c> 的 <c>White Cow.Skins</c> 是**键在、值 null**，
    /// 而模型里 <c>Skins</c> 是 <c>List&lt;FarmAnimalSkin&gt;</c>。
    /// SVE 的「可爱的谷仓动物」用 <c>TargetField:["White Cow","Skins"]</c> 往里写皮肤，
    /// 我们原来把 <c>null</c> 原样交回去 ⇒ <c>ApplyEntries</c> 报「不是对象也不是数组（实际 Null）」
    /// ⇒ **那个包 35 条整包写入失败**。
    /// </para>
    /// </summary>
    [Fact]
    public void 落点值是null时按模型建空列表()
    {
        var result = ApplyAsset("Data/FarmAnimals", """{ "White Cow": { "Skins": null } }""",
            """
            {
              "TargetField": [ "White Cow", "Skins" ],
              "Entries": { "MySkin": { "Id": "MySkin", "Texture": "a.png" } }
            }
            """);

        JToken skins = result["White Cow"]!["Skins"]!;
        Assert.Equal(JTokenType.Array, skins.Type);
        Assert.Equal("MySkin", Assert.Single(skins.Children())["Id"]!.Value<string>());
    }

    /// <summary>值是 null、模型说是字典时，建的是对象。</summary>
    [Fact]
    public void 落点值是null且模型是字典时建对象()
    {
        var result = ApplyAsset("Data/Characters", """{ "Abigail": { "CustomFields": null } }""",
            """
            {
              "TargetField": [ "Abigail", "CustomFields" ],
              "Entries": { "k": "v" }
            }
            """);

        Assert.Equal(JTokenType.Object, result["Abigail"]!["CustomFields"]!.Type);
        Assert.Equal("v", result["Abigail"]!["CustomFields"]!["k"]!.Value<string>());
    }

    /// <summary>
    /// ⚠ <b>模型说不清时，仍然把 <c>null</c> 交回去</b>（让上层报「定位不到」），
    /// 而不是猜一个容器 —— 猜错了会把标量字段写成对象。
    /// </summary>
    [Fact]
    public void 模型说不清时null照旧失败()
    {
        var (ok, _) = ApplyNamed("Data/NotInSchema", """{ "A": { "B": null } }""",
            """{ "TargetField": [ "A", "B" ], "Entries": { "c": "1" } }""");

        Assert.False(ok);
    }

    /// <summary>
    /// ⚠ <b>模型不认得的资产仍建对象</b> —— 这是**如实留的缺口**，不是「已经覆盖了」。
    /// 不拿猜测去覆盖：猜错了会比现在更糟。
    /// </summary>
    [Fact]
    public void 模型不认得的资产仍按老办法建对象()
    {
        var result = ApplyAsset("Data/NotInSchema", """{ }""",
            """{ "TargetField": [ "A", "B" ], "Entries": { "c": "1" } }""");

        Assert.Equal(JTokenType.Object, result["A"]!["B"]!.Type);
    }

    [Fact]
    public void Fields改嵌套字段的值()
    {
        var result = Apply("""{ "a": { "name": "old", "price": 1 } }""",
            """{ "Fields": { "a": { "name": "new" } } }""");

        Assert.Equal("new", result["a"]!["name"]!.Value<string>());
        Assert.Equal(1, result["a"]!["price"]!.Value<int>());
    }

    [Fact]
    public void 数组型资产也能新增条目()
    {
        var result = Apply("""[ { "Id": "x", "V": 1 } ]""",
            """{ "Entries": { "y": { "Id": "y", "V": 2 } } }""");

        // 数组资产：按 Id 匹配，匹配不到则追加
        Assert.Equal(2, ((JArray)result).Count);
        Assert.Equal(2, result[1]!["V"]!.Value<int>());
    }

    [Fact]
    public void 数组型资产按Id覆盖()
    {
        var result = Apply("""[ { "Id": "x", "V": 1 } ]""",
            """{ "Entries": { "x": { "V": 99 } } }""");

        Assert.Single(result);
        Assert.Equal(99, result[0]!["V"]!.Value<int>());
    }

    /// <summary>
    /// ★ 命中已有元素时是**整格替换**，不是「把补丁的字段并进去」——
    /// 补丁没提的字段**不留**（CP 那边 <c>ToObject</c> 出来是全新的对象）。
    /// 与 <c>Fields</c> 的语义正好相反（那边才是逐字段灌）。
    /// </summary>
    [Fact]
    public void 数组命中已有元素时整格替换不逐字段合并()
    {
        var result = Apply("""[ { "Id": "x", "V": 1, "Keep": "会没" } ]""",
            """{ "Entries": { "x": { "V": 99 } } }""");

        Assert.Single(result);
        Assert.Equal(99, result[0]!["V"]!.Value<int>());
        Assert.Null(result[0]!["Keep"]);        // ← 并进去才是错的
    }

    /// <summary>同理：<c>Fields</c> 才是逐字段灌，两者不能混。</summary>
    [Fact]
    public void Fields命中已有元素时逐字段合并()
    {
        var result = Apply("""{ "x": { "V": 1, "Keep": 2 } }""",
            """{ "Fields": { "x": { "V": 99 } } }""");

        Assert.Equal(99, result["x"]!["V"]!.Value<int>());
        Assert.Equal(2, result["x"]!["Keep"]!.Value<int>());   // Fields 保留其它字段
    }

    /// <summary>
    /// 追加时**不补 `Id`**：CP 是 `Data.Add(value)`，值里写什么就是什么。
    /// （补 <c>Id</c> 会把 <c>{"#-1": {"V":1}}</c> 写成 <c>{"Id":"#-1","V":1}</c> ——
    /// 与先前修掉的字符串元素那个坑是同一类。）
    /// </summary>
    [Fact]
    public void 数组追加对象时不补Id()
    {
        var result = Apply("""[ { "Id": "x", "V": 1 } ]""",
            """{ "Entries": { "#-1": { "V": 2 } } }""");

        Assert.Equal(2, ((JArray)result).Count);
        Assert.Null(result[1]!["Id"]);          // ← 补上才是错的
        Assert.Equal(2, result[1]!["V"]!.Value<int>());
    }

    // ── MoveEntries：把已有条目挪位置（CP `ApplyMoveEntries` + `ListKeyValueEditor.MoveEntry`）──

    private static JToken Move(string targetJson, string patchJson) =>
        EditDataMerger.Apply(JToken.Parse(targetJson), JObject.Parse(patchJson)).Data!;

    private static string[] Ids(JToken arr) =>
        ((JArray)arr).Select(t => t["Id"]!.Value<string>()!).ToArray();

    /// <summary>`ToPosition`：`Top` 挪到最前、`Bottom` 挪到最后。</summary>
    [Fact]
    public void MoveEntries按ToPosition挪()
    {
        const string data = """[ {"Id":"a"}, {"Id":"b"}, {"Id":"c"} ]""";

        Assert.Equal(new[] { "c", "a", "b" },
            Ids(Move(data, """{ "MoveEntries": [ { "Id": "c", "ToPosition": "Top" } ] }""")));
        Assert.Equal(new[] { "b", "c", "a" },
            Ids(Move(data, """{ "MoveEntries": [ { "Id": "a", "ToPosition": "Bottom" } ] }""")));
    }

    /// <summary>
    /// ★ 按锚点挪，且**先删后插的下标要往回挪一位** —— 少了这一步往后挪会差一格。
    /// 语料里 <c>BeforeId/AfterId</c> 与 <c>BeforeID/AfterID</c> 两种写法并存，都得认。
    /// </summary>
    [Fact]
    public void MoveEntries按锚点挪()
    {
        const string data = """[ {"Id":"a"}, {"Id":"b"}, {"Id":"c"}, {"Id":"d"} ]""";

        // 往前挪：c 放到 a 之前
        Assert.Equal(new[] { "c", "a", "b", "d" },
            Ids(Move(data, """{ "MoveEntries": [ { "Id": "c", "BeforeId": "a" } ] }""")));
        // 往后挪：a 放到 c 之后（index<anchorIndex ⇒ 下标回挪，否则会插到 c 前面）
        Assert.Equal(new[] { "b", "c", "a", "d" },
            Ids(Move(data, """{ "MoveEntries": [ { "Id": "a", "AfterId": "c" } ] }""")));
        // 大写拼写同样认
        Assert.Equal(new[] { "c", "a", "b", "d" },
            Ids(Move(data, """{ "MoveEntries": [ { "ID": "c", "BeforeID": "a" } ] }""")));
    }

    /// <summary>`#&lt;整数&gt;` 下标也能当键（与 `Entries` 同一套键语义）。</summary>
    [Fact]
    public void MoveEntries支持井号下标()
    {
        var result = Move("""[ {"Id":"a"}, {"Id":"b"}, {"Id":"c"} ]""",
            """{ "MoveEntries": [ { "Id": "#2", "ToPosition": "Top" } ] }""");

        Assert.Equal(new[] { "c", "a", "b" }, Ids(result));
    }

    /// <summary>
    /// 挪不动的情况只记 <c>note</c>，**不是失败**：条目不在、锚点不在、锚点是它自己 ——
    /// CP 那边也是记一条警告后继续。
    /// </summary>
    [Fact]
    public void MoveEntries挪不动时记note不失败()
    {
        const string data = """[ {"Id":"a"}, {"Id":"b"} ]""";

        var missing = EditDataMerger.Apply(JToken.Parse(data),
            JObject.Parse("""{ "MoveEntries": [ { "Id": "zzz", "ToPosition": "Top" } ] }"""));
        Assert.True(missing.Success);
        Assert.Contains(missing.Notes, n => n.Contains("zzz"));

        var selfAnchor = EditDataMerger.Apply(JToken.Parse(data),
            JObject.Parse("""{ "MoveEntries": [ { "Id": "a", "BeforeId": "a" } ] }"""));
        Assert.True(selfAnchor.Success);
        Assert.Contains(selfAnchor.Notes, n => n.Contains("自己"));
    }

    /// <summary>
    /// <b>字典/模型上不做</b>：CP 的 <c>CanMoveEntries</c> 只有列表编辑器为真，
    /// 打在对象上时它记警告后整条跳过 —— 我们照做，不能把一个条目挪出个所以然来。
    /// </summary>
    [Fact]
    public void MoveEntries打在对象上时整条跳过()
    {
        var outcome = EditDataMerger.Apply(JToken.Parse("""{ "a": {"n":1} }"""),
            JObject.Parse("""{ "MoveEntries": [ { "Id": "a", "ToPosition": "Top" } ] }"""));

        Assert.True(outcome.Success);
        Assert.Equal(1, outcome.Data!["a"]!["n"]!.Value<int>());     // 数据原样
        Assert.Contains(outcome.Notes, n => n.Contains("只对"));
    }

    /// <summary>顺序：`Entries` 先落、`MoveEntries` 后挪 —— 新加的条目也能被挪。</summary>
    [Fact]
    public void MoveEntries在Entries之后应用()
    {
        var result = Move("""[ {"Id":"a"} ]""",
            """
            {
              "Entries": { "#-1": { "Id": "new" } },
              "MoveEntries": [ { "Id": "new", "ToPosition": "Top" } ]
            }
            """);

        Assert.Equal(new[] { "new", "a" }, Ids(result));
    }

    [Fact]
    public void 同时有Entries与Fields时都应用()
    {
        var result = Apply("""{ "a": { "n": 1 }, "b": "x" }""",
            """{ "Entries": { "b": "y" }, "Fields": { "a": { "n": 2 } } }""");

        Assert.Equal("y", result["b"]!.Value<string>());
        Assert.Equal(2, result["a"]!["n"]!.Value<int>());
    }

    // ── Fields 作用于「分隔字符串」条目（CP EditDataPatch.cs:286-346）──────────

    private static (bool ok, JToken? data) ApplyNamed(string asset, string targetJson, string patchJson)
    {
        MergeOutcome r = EditDataMerger.Apply(JToken.Parse(targetJson), JObject.Parse(patchJson), asset);
        return (r.Success, r.Data);
    }

    /// <summary>
    /// 条目是分隔字符串时，字段键是 <b>0 起下标</b>（CP: `int.TryParse(FieldKey)`）。
    /// 早先的实现把它当属性名，会把<b>整条字符串覆盖成 JSON 对象</b> —— 破坏性的。
    /// </summary>
    [Fact]
    public void Fields用下标改分隔字符串的某一段()
    {
        var (ok, data) = ApplyNamed("Data/CookingRecipes",
            """{ "Cookie": "cookie/1 2 3/4/5/6/7/8" }""",
            """{ "Fields": { "Cookie": { "6": "9" } } }""");

        Assert.True(ok);
        Assert.Equal("cookie/1 2 3/4/5/6/7/9", data!["Cookie"]!.Value<string>());
    }

    [Fact]
    public void Fields下标0改第一段()
    {
        var (ok, data) = ApplyNamed("Data/CookingRecipes",
            """{ "Cookie": "cookie/rest" }""",
            """{ "Fields": { "Cookie": { "0": "biscuit" } } }""");

        Assert.True(ok);
        Assert.Equal("biscuit/rest", data!["Cookie"]!.Value<string>());
    }

    /// <summary>`Data/Achievements` 是**唯一**用 `^` 分字段的资产（CP EditDataPatch.cs:499-502）。</summary>
    [Fact]
    public void 成就资产用脱字符分字段()
    {
        var (ok, data) = ApplyNamed("Data/Achievements",
            """{ "a": "name^desc^3^0" }""",
            """{ "Fields": { "a": { "2": "5" } } }""");

        Assert.True(ok);
        Assert.Equal("name^desc^5^0", data!["a"]!.Value<string>());
    }

    [Fact]
    public void Fields下标越界时只跳过这一段()
    {
        // CP `ApplyFields` 对「下标越界」「不是下标」都是 `WarnForField` 后继续：
        // 好字段照落、别的条目照做。原来判整条 Fail，会连带丢掉同一补丁里 `Entries` 已落好的改动。
        var (ok, data) = ApplyNamed("Data/CookingRecipes",
            """{ "Cookie": "a/b", "Other": "x/y" }""",
            """{ "Fields": { "Cookie": { "0": "A", "5": "越界" } }, "Entries": { "Other": "x/z" } }""");

        Assert.True(ok);
        Assert.Equal("A/b", data!["Cookie"]!.Value<string>());    // 好字段落了
        Assert.Equal("x/z", data["Other"]!.Value<string>());      // 别的条目也落了
    }

    [Fact]
    public void Fields对字符串用非数字键时只跳过这一段()
    {
        var (ok, data) = ApplyNamed("Data/CookingRecipes",
            """{ "Cookie": "a/b" }""",
            """{ "Fields": { "Cookie": { "Name": "x" } } }""");

        Assert.True(ok);
        Assert.Equal("a/b", data!["Cookie"]!.Value<string>());    // 原值不变
    }

    [Fact]
    public void Fields指向不存在的条目时只跳过该条()
    {
        var (ok, data) = ApplyNamed("Data/CookingRecipes",
            """{ "Cookie": "a/b", "Other": "x/y" }""",
            """{ "Fields": { "Nope": { "0": "x" } }, "Entries": { "Other": "x/z" } }""");

        Assert.True(ok);
        Assert.Equal("x/z", data!["Other"]!.Value<string>());
    }

    /// <summary>
    /// ★ <b>`Fields` 的目标可以是**列表**</b>：CP 用 `ListKeyValueEditor` 按 `Id` 找到元素再合并。
    /// 实测聚宝盆作物有 <c>TargetField: ["SeedShop","Items"]</c> + <c>Fields</c> 的写法，
    /// 我们原来只认对象、落到列表就整条判失败。
    /// </summary>
    [Fact]
    public void Fields可以打在列表条目上()
    {
        var result = Apply("""{ "Items": [ { "Id": "(O)301", "Condition": "SEASON spring", "Price": 100 } ] }""",
            """
            {
              "TargetField": [ "Items" ],
              "Fields": { "(O)301": { "Condition": null, "Price": 250 } }
            }
            """);

        JToken item = result["Items"]![0]!;
        Assert.Equal(250, item["Price"]!.Value<int>());
        // `Fields` 的 null 是**把这个字段设成 null**（CP 用 `Serializer.Populate`），不是删键
        Assert.True(item["Condition"]!.Type == JTokenType.Null);
    }

    /// <summary>列表里按 <c>#&lt;整数&gt;</c> 下标也能落，且找不到的只跳过。</summary>
    [Fact]
    public void Fields打在列表上支持下标与跳过()
    {
        var result = Apply("""{ "Items": [ { "Id": "a", "V": 1 }, { "Id": "b", "V": 2 } ] }""",
            """
            {
              "TargetField": [ "Items" ],
              "Fields": { "#1": { "V": 9 }, "zzz": { "V": 7 } }
            }
            """);

        Assert.Equal(1, result["Items"]![0]!["V"]!.Value<int>());
        Assert.Equal(9, result["Items"]![1]!["V"]!.Value<int>());
    }

    /// <summary>对象条目仍然按键名改 —— 与字符串条目的下标语义并存，互不影响。</summary>
    [Fact]
    public void Fields对对象条目仍按键名改()
    {
        var (ok, data) = ApplyNamed("Data/Objects",
            """{ "a": { "Name": "旧", "Price": 1 } }""",
            """{ "Fields": { "a": { "Price": 42 } } }""");

        Assert.True(ok);
        Assert.Equal(42, data!["a"]!["Price"]!.Value<int>());
        Assert.Equal("旧", data["a"]!["Name"]!.Value<string>());
    }

    // ── Data/Events 的键序（首个命中即赢 ↔ CP 后写赢）──────────────────────

    /// <summary>
    /// 事件资产的新键要**插到最前**。理由有**两条入口**，前插同时满足它们：
    /// <list type="bullet">
    /// <item><c>GameLocation.checkForEvents</c>（<c>:15659-15668</c>）：按文件键序取
    ///   <b>首个前置条件命中</b>的键并 <c>break</c>；</item>
    /// <item><c>GameLocation.findEventById</c>（<c>:15680-15718</c>）：按 ID **线性取第一个键**，
    ///   <b>完全不看前置条件</b>。</item>
    /// </list>
    /// 而 CP 是「**后写赢**」（多条补丁命中同一键时后者覆盖前者）——
    /// 所以 CP 最后写的那条必须排在最前：这样两条入口取到的是**同一条**。
    /// 只满足前者、不管后者的话，同 ID 多键时 <c>findEventById</c> 会拿到错的那条。
    /// </summary>
    private static string[] Keys(JToken? data) =>
        ((JObject)data!).Properties().Select(p => p.Name).ToArray();

    [Fact]
    public void 事件资产的新键插到最前()
    {
        var (ok, data) = ApplyNamed("Data/Events/Town",
            """{ "1/f Abigail 1500": "原版" }""",
            """{ "Entries": { "9/G SEASON spring": "新增" } }""");

        Assert.True(ok);
        Assert.Equal(new[] { "9/G SEASON spring", "1/f Abigail 1500" }, Keys(data));
    }

    /// <summary>逐条前插 → 与处理顺序相反 = CP 顺序的逆序。</summary>
    [Fact]
    public void 事件资产连续两条新键得到逆序()
    {
        var (ok, d1) = ApplyNamed("Data/Events/Town", """{ }""",
            """{ "Entries": { "a/G X": "1" } }""");
        Assert.True(ok);
        var (ok2, d2) = ApplyNamed("Data/Events/Town", d1!.ToString(Newtonsoft.Json.Formatting.None),
            """{ "Entries": { "b/G Y": "2" } }""");
        Assert.True(ok2);

        Assert.Equal(new[] { "b/G Y", "a/G X" }, Keys(d2));
    }

    [Fact]
    public void 非事件资产的新键仍追加在末尾()
    {
        var (ok, data) = ApplyNamed("Data/Objects",
            """{ "a": { "N": 1 } }""",
            """{ "Entries": { "b": { "N": 2 } } }""");

        Assert.True(ok);
        Assert.Equal(new[] { "a", "b" }, Keys(data));
    }

    /// <summary>已存在的键**原地替换**，位置不动 —— 否则会打乱原版键的既有次序。</summary>
    [Fact]
    public void 事件资产覆盖已有键时不改位置()
    {
        var (ok, data) = ApplyNamed("Data/Events/Town",
            """{ "1/f Abigail 1500": "原版", "2/f Sam 500": "原版2" }""",
            """{ "Entries": { "2/f Sam 500": "改后" } }""");

        Assert.True(ok);
        Assert.Equal(new[] { "1/f Abigail 1500", "2/f Sam 500" }, Keys(data));
        Assert.Equal("改后", data!["2/f Sam 500"]!.Value<string>());
    }

    // ── TargetField 穿过数组（CP 的 ListKeyValueEditor 语义）─────────────────

    private static (bool ok, JToken? data) ApplyPath(string targetJson, string patchJson)
    {
        MergeOutcome r = EditDataMerger.Apply(JToken.Parse(targetJson), JObject.Parse(patchJson), "Data/WorldMap");
        return (r.Success, r.Data);
    }

    /// <summary>
    /// `Data/WorldMap` 的 `MapAreas` 是**列表**，元素带 `Id`。
    /// CP 的 `ListKeyValueEditor`：键是 `#&lt;数字&gt;` 时按下标，其余按元素的 `Id` 匹配。
    /// 实测 SVE 的 `Valley.MapAreas.Railroad.ToolTips.Railroad` 就卡在这条上。
    /// </summary>
    [Fact]
    public void TargetField按Id穿过数组()
    {
        var (ok, data) = ApplyPath(
            """{ "Valley": { "MapAreas": [ { "Id": "Town", "V": 1 }, { "Id": "Railroad", "V": 2 } ] } }""",
            """{ "TargetField": [ "Valley", "MapAreas", "Railroad" ], "Entries": { "V": 99 } }""");

        Assert.True(ok);
        Assert.Equal(99, data!["Valley"]!["MapAreas"]![1]!["V"]!.Value<int>());
        Assert.Equal(1, data["Valley"]!["MapAreas"]![0]!["V"]!.Value<int>());   // 另一条不动
    }

    /// <summary>嵌套数组也要能穿：`MapAreas[].ToolTips[]` 同样是列表。</summary>
    [Fact]
    public void TargetField连穿两层数组()
    {
        var (ok, data) = ApplyPath(
            """
            { "Valley": { "MapAreas": [
                { "Id": "Railroad", "ToolTips": [ { "Id": "Railroad", "Text": "旧" } ] } ] } }
            """,
            """{ "TargetField": [ "Valley", "MapAreas", "Railroad", "ToolTips", "Railroad" ], "Entries": { "Text": "新" } }""");

        Assert.True(ok);
        Assert.Equal("新", data!["Valley"]!["MapAreas"]![0]!["ToolTips"]![0]!["Text"]!.Value<string>());
    }

    /// <summary>`#&lt;数字&gt;` 是下标（CP 的 `ParseKey`）。</summary>
    [Fact]
    public void TargetField用井号走下标()
    {
        var (ok, data) = ApplyPath(
            """{ "A": [ { "Id": "x", "V": 1 }, { "Id": "y", "V": 2 } ] }""",
            """{ "TargetField": [ "A", "#1" ], "Entries": { "V": 9 } }""");

        Assert.True(ok);
        Assert.Equal(9, data!["A"]![1]!["V"]!.Value<int>());
    }

    /// <summary>数组里找不到时**不创建**（凭空 append 一条残缺元素就是写错数据）。</summary>
    [Fact]
    public void TargetField数组里找不到时不创建()
    {
        var (ok, _) = ApplyPath(
            """{ "A": [ { "Id": "x" } ] }""",
            """{ "TargetField": [ "A", "不存在" ], "Entries": { "V": 1 } }""");

        Assert.False(ok);
    }

    /// <summary>
    /// `Entries` 里**个别**条目的值含代不掉的 CP 令牌时，**只丢那几条，其余照写**。
    /// <para>
    /// 依据 CP <c>ApplyRecords</c>：逐条 <c>WarnForRecord(…)</c> 后 <c>continue</c>，
    /// 取舍粒度是**条目**（<c>EditDataPatch.cs:13242+</c>）。
    /// </para>
    /// <para>
    /// 实机症状（用户截图）：SVE 的 <c>Strings/StringsFromMaps</c> 一条补丁带 1037 个条目，
    /// 只有 1 个带 <c>{{FarmName}}</c>；整条不写 ⇒ 另 1036 条全丢 ⇒
    /// 蓝月酒庄地图上显示裸键 <c>Strings\StringsFromMaps:BlueMoonVineyard.7</c>。
    /// </para>
    /// </summary>
    [Fact]
    public void 个别条目的值带代不掉的令牌时只丢那几条()
    {
        MergeOutcome r = EditDataMerger.Apply(
            JToken.Parse("""{ "Keep": "old" }"""),
            JObject.Parse("""{ "Entries": { "Good1": "x", "Bad": "{{FarmName}} 的农场", "Good2": "y" } }"""));

        Assert.True(r.Success, r.FailureReason);
        var merged = (JObject)r.Data!;
        Assert.Equal("old", merged["Keep"]!.Value<string>());
        Assert.Equal("x", merged["Good1"]!.Value<string>());
        Assert.Equal("y", merged["Good2"]!.Value<string>());
        Assert.Null(merged["Bad"]);
        Assert.Contains(r.Notes, n => n.Contains("Bad"));
    }

    /// <summary>键本身带代不掉的令牌也一样：只丢那一条。</summary>
    [Fact]
    public void 键带代不掉的令牌时也只丢那一条()
    {
        MergeOutcome r = EditDataMerger.Apply(
            JToken.Parse("""{ }"""),
            JObject.Parse("""{ "Entries": { "ok": "1", "{{FarmName}}_x": "2" } }"""));

        Assert.True(r.Success, r.FailureReason);
        var merged = (JObject)r.Data!;
        Assert.Equal("1", merged["ok"]!.Value<string>());
        Assert.Null(merged["{{FarmName}}_x"]);
    }

    // ── 交所有权（`takeOwnership`）：不克隆整文档、直接原地改 ──────────────────

    /// <summary>默认不交所有权：入参一个字节都不能动（`RawAssetCache` 里那份还要继续用来合并）。</summary>
    [Fact]
    public void 默认模式下不改动入参()
    {
        JToken target = JToken.Parse("""{ "a": "1" }""");
        MergeOutcome r = EditDataMerger.Apply(target, JObject.Parse("""{ "Entries": { "b": "2" } }"""));

        Assert.True(r.Success, r.FailureReason);
        Assert.NotSame(target, r.Data);
        Assert.Null(((JObject)target)["b"]);                        // 原对象没被碰
        Assert.Equal("2", r.Data!["b"]!.Value<string>());
    }

    /// <summary>
    /// 交了所有权就是**同一个对象**被改。实测「整文档克隆」是 <c>EditData</c> 里最贵的一步
    /// （一个包 108 次 1658 ms，同期真合并合计只要 13 ms），大资产上省的就是这一整块。
    /// </summary>
    [Fact]
    public void 交所有权时原地改并返回同一对象()
    {
        JToken target = JToken.Parse("""{ "a": "1" }""");
        MergeOutcome r = EditDataMerger.Apply(target, JObject.Parse("""{ "Entries": { "b": "2" } }"""),
            takeOwnership: true);

        Assert.True(r.Success, r.FailureReason);
        Assert.Same(target, r.Data);
        Assert.Equal("2", ((JObject)target)["b"]!.Value<string>());
    }

    /// <summary>
    /// ★ **交所有权的前提**：失败绝不留痕（「要么原样失败、要么改完成功」）。
    /// 破了这条，调用方（`RawAssetCache`、产出树暂存里那份）就被改坏了 ——
    /// 后面每条补丁都建在坏数据上，而且**看不出来**。
    /// </summary>
    [Fact]
    public void 交所有权时Entries类型不符失败不留痕()
    {
        JToken target = JToken.Parse("\"裸字符串\"");
        MergeOutcome r = EditDataMerger.Apply(target,
            JObject.Parse("""{ "Entries": { "b": "2" } }"""), takeOwnership: true);

        Assert.False(r.Success);
        Assert.Equal("裸字符串", target.Value<string>());
    }

    /// <summary>
    /// TextOperations 里排在中间的一条形状非法 ⇒ 整条补丁失败，
    /// **它前面那条也不能落下去**（解析期校验必须走在所有改动之前 —— CP 也是在补丁创建前整张表验一遍）。
    /// </summary>
    [Fact]
    public void 交所有权时TextOperation中途非法不留痕()
    {
        JToken data = JToken.Parse("""{ "A": "x/y" }""");
        MergeOutcome r = EditDataMerger.Apply(data, JObject.Parse("""
            { "TextOperations": [
                { "Operation": "Append", "Target": [ "A" ], "Value": "z" },
                { "Operation": "RemoveDelimited" }
            ] }
            """), takeOwnership: true);

        Assert.False(r.Success);
        Assert.Equal("x/y", ((JObject)data)["A"]!.Value<string>());
    }

    /// <summary>TargetField 指到标量上时定位不到，失败也不能在入参上留痕。</summary>
    [Fact]
    public void 交所有权时TargetField定位不到不留痕()
    {
        JToken target = JToken.Parse("""{ "A": "标量" }""");
        MergeOutcome r = EditDataMerger.Apply(target,
            JObject.Parse("""{ "TargetField": [ "A", "B" ], "Entries": { "k": "v" } }"""),
            takeOwnership: true);

        Assert.False(r.Success);
        Assert.Equal("标量", ((JObject)target)["A"]!.Value<string>());
    }

    /// <summary>
    /// ★ <c>TargetField</c> 的下降**先走通整条路径，才把新建的容器挂上去**。
    /// <para>
    /// 原来是边建边挂：`["White Cow", "Skins", "X"]` 会把 `Skins` 先建成空列表
    /// （模型说是 <c>List</c>），再去空列表里找 `X` —— 找不到、整条补丁失败，
    /// 而那个空 `Skins` **已经留在调用方的文档里了**。
    /// 「克隆一份再改、失败就丢」时看不出来；`takeOwnership`（原地改）要的是
    /// 「要么原样失败、要么改完成功」，留半截就不是原样了。
    /// </para>
    /// </summary>
    [Fact]
    public void 交所有权时TargetField中途落空不留半截容器()
    {
        JToken target = JToken.Parse("""{ "White Cow": { } }""");
        MergeOutcome r = EditDataMerger.Apply(target, JObject.Parse("""
            { "TargetField": [ "White Cow", "Skins", "X" ], "Entries": { "k": "v" } }
            """), "Data/FarmAnimals", takeOwnership: true);

        Assert.False(r.Success);
        Assert.Null(((JObject)target)["White Cow"]!["Skins"]);
    }

    /// <summary>
    /// ★ **TextOperations 的形状校验要排在整条合并的最前面**（`ValidateShape` 现在由
    /// <see cref="EditDataMerger.Apply"/> 在任何改动之前先调一遍）。
    /// <para>
    /// 原先它只在 `TextOperationMerger.Apply` 开头 —— 那个位置**已经在 `TargetField` 下降、
    /// `Entries`、`Fields` 之后**。实测（审查时用临时测试跑出来的）：这条补丁返回失败，
    /// 而 target 已经变成 <c>{"A":{"k":"v"}}</c>。克隆形态下看不出来（脏克隆被丢掉），
    /// 原地改要的「要么原样失败、要么改完成功」就破了。
    /// </para>
    /// </summary>
    [Fact]
    public void 交所有权时TextOperation非法也不留Entries与新建容器的痕()
    {
        JToken target = JToken.Parse("""{ }""");
        MergeOutcome r = EditDataMerger.Apply(target, JObject.Parse("""
            {
              "TargetField": [ "A" ],
              "Entries": { "k": "v" },
              "TextOperations": [ { "Operation": "Bogus" } ]
            }
            """), takeOwnership: true);

        Assert.False(r.Success);
        Assert.Empty(((JObject)target).Properties());
    }

    /// <summary>同一条补丁打在**已有内容**的文档上也一样：Entries 的改动不能留下。</summary>
    [Fact]
    public void 交所有权时TextOperation非法不改已有条目()
    {
        JToken target = JToken.Parse("""{ "Keep": "old" }""");
        MergeOutcome r = EditDataMerger.Apply(target, JObject.Parse("""
            {
              "Entries": { "New": "x" },
              "TextOperations": [ { "Operation": "RemoveDelimited" } ]
            }
            """), takeOwnership: true);

        Assert.False(r.Success);
        Assert.Null(((JObject)target)["New"]);
        Assert.Equal("old", ((JObject)target)["Keep"]!.Value<string>());
    }
}
