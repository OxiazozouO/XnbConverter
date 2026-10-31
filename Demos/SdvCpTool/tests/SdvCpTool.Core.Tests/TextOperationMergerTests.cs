using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;

namespace SdvCpTool.Core.Tests;

public class TextOperationMergerTests
{
    private static JToken Apply(string targetJson, string patchJson, string? assetName = null) =>
        EditDataMerger.Apply(JToken.Parse(targetJson), JObject.Parse(patchJson), assetName).Data!;

    [Fact]
    public void Append到空条目时忽略分隔符()
    {
        // 条目不存在 → 按空字符串处理并创建，分隔符不生效
        var result = Apply("""{ }""",
            """{ "TextOperations": [ { "Operation": "Append", "Target": ["Entries", "k"], "Value": "C", "Delimiter": "/" } ] }""");

        Assert.Equal("C", result["k"]!.Value<string>());
    }

    /// <summary>
    /// ★ **一条文本操作落不下去，不该把整条补丁带走** —— CP 就是这么做的：
    /// <c>ApplyTextOperations</c>（<c>EditDataPatch.cs:352-363</c>）对失败的那一条**记警告后继续**，
    /// 其余照常应用。我们原先在第一条失败处整体 <c>Fail</c>，等于把同一补丁里**本来能落**的那几条
    /// 一起丢掉（实测语料 12 条卡在 `Target: ["Fields", "Andy", 3]` —— `Andy` 是别的 mod 的 NPC，
    /// 本包单独转换时资产里没有这个条目）。
    /// </summary>
    [Fact]
    public void 一条操作落不下去时跳过它继续做其余的()
    {
        var outcome = EditDataMerger.Apply(
            JToken.Parse("""{ "k": "A/B" }"""),
            JObject.Parse("""
            { "TextOperations": [
                { "Operation": "Append", "Target": ["Fields", "Andy", 3], "Value": "X", "Delimiter": " " },
                { "Operation": "Append", "Target": ["Entries", "k"], "Value": "C", "Delimiter": "/" } ] }
            """));

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("A/B/C", outcome.Data!["k"]!.Value<string>());     // 第二条照常落
        Assert.Contains(outcome.Notes, n => n.Contains("Andy"));        // 跳过的要报出来
    }

    /// <summary>
    /// ★ <c>Fields</c> 指向的条目不存在时**跳过该条目继续**，其余照做。
    /// 依据 CP <c>ApplyFields</c>（<c>EditDataPatch.cs:290-296</c>）：
    /// <c>WarnForField("there's no record matching key ...")</c> 之后 <c>continue</c>。
    /// </summary>
    [Fact]
    public void Fields指向的条目不存在时跳过它继续()
    {
        var outcome = EditDataMerger.Apply(
            JToken.Parse("""{ "k": "A/B" }"""),
            JObject.Parse("""{ "Fields": { "Nope": { "0": "X" }, "k": { "0": "C" } } }"""));

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("C/B", outcome.Data!["k"]!.Value<string>());
        Assert.Contains(outcome.Notes, n => n.Contains("Nope"));
    }

    /// <summary>全都落不下去 ⇒ 仍判失败（不能报成「写了」，那是在虚报）。</summary>
    [Fact]
    public void 全部操作都落不下去时仍算应用()
    {
        var outcome = EditDataMerger.Apply(
            JToken.Parse("""{ "k": "A/B" }"""),
            JObject.Parse("""
            { "TextOperations": [
                { "Operation": "Append", "Target": ["Fields", "Andy", 3], "Value": "X", "Delimiter": " " } ] }
            """));

        // ★ CP 的 `EditDataPatch.Edit` 没有失败这一说：这一条是**运行期**落空（条目 `Andy`
        //   是别的 mod 的 NPC），CP `WarnForOperation` 后继续、补丁仍算已应用。
        //   判成「写入失败」会与判定侧给的 `ConvertedNoCondition` 打架
        //   —— 正是本仓库点名要避免的「把判定不准伪装成写入出问题」。
        Assert.True(outcome.Success);
        Assert.Contains("Andy", string.Join("；", outcome.Notes));
    }

    [Fact]
    public void Append到非空条目时插入分隔符()
    {
        var result = Apply("""{ "k": "A/B" }""",
            """{ "TextOperations": [ { "Operation": "Append", "Target": ["Entries", "k"], "Value": "C", "Delimiter": "/" } ] }""");

        Assert.Equal("A/B/C", result["k"]!.Value<string>());
    }

    [Fact]
    public void Append未指定Delimiter时不插入任何分隔符()
    {
        // CP 源码语义：Delimiter 缺省为空字符串（文档所称的 "/" 缺省与源码不符）。
        // 真实模组 Ridgeside 依赖此行为：Value 自带前导 "/" 且不写 Delimiter。
        var result = Apply("""{ "k": "A/B" }""",
            """{ "TextOperations": [ { "Operation": "Append", "Target": ["Entries", "k"], "Value": "C" } ] }""");

        Assert.Equal("A/BC", result["k"]!.Value<string>());
    }

    [Fact]
    public void Prepend到非空条目()
    {
        var result = Apply("""{ "k": "B/C" }""",
            """{ "TextOperations": [ { "Operation": "Prepend", "Target": ["Entries", "k"], "Value": "A", "Delimiter": "/" } ] }""");

        Assert.Equal("A/B/C", result["k"]!.Value<string>());
    }

    [Fact]
    public void 操作名与Target根大小写不敏感()
    {
        // 文档：Operation 取值与 Target 路径段"capitalisation doesn't matter"
        var result = Apply("""{ "k": "A/B" }""",
            """{ "TextOperations": [ { "Operation": "append", "Target": ["entries", "k"], "Value": "C", "Delimiter": "/" } ] }""");

        Assert.Equal("A/B/C", result["k"]!.Value<string>());
    }

    [Fact]
    public void RemoveDelimited默认删除全部匹配()
    {
        var result = Apply("""{ "k": "A a/B/C/A a" }""",
            """{ "TextOperations": [ { "Operation": "RemoveDelimited", "Target": ["Entries", "k"], "Search": "A a", "Delimiter": "/" } ] }""");

        Assert.Equal("B/C", result["k"]!.Value<string>());
    }

    [Fact]
    public void RemoveDelimited只删整段不删子串()
    {
        var result = Apply("""{ "k": "AB/A/ABC" }""",
            """{ "TextOperations": [ { "Operation": "RemoveDelimited", "Target": ["Entries", "k"], "Search": "A", "Delimiter": "/" } ] }""");

        Assert.Equal("AB/ABC", result["k"]!.Value<string>());
    }

    [Fact]
    public void RemoveDelimited_first与last模式()
    {
        var first = Apply("""{ "k": "A/B/A" }""",
            """{ "TextOperations": [ { "Operation": "RemoveDelimited", "Target": ["Entries", "k"], "Search": "A", "Delimiter": "/", "ReplaceMode": "First" } ] }""");
        Assert.Equal("B/A", first["k"]!.Value<string>());

        var last = Apply("""{ "k": "A/B/A" }""",
            """{ "TextOperations": [ { "Operation": "RemoveDelimited", "Target": ["Entries", "k"], "Search": "A", "Delimiter": "/", "ReplaceMode": "Last" } ] }""");
        Assert.Equal("A/B", last["k"]!.Value<string>());
    }

    /// <summary>
    /// ★ <b>`ReplaceDelimited` 不是 CP 的操作</b> —— 反编译确认 `TextOperationType` 只有
    /// `Append` / `Prepend` / `RemoveDelimited` 三个值，全程序集里 `ReplaceDelimited` 一处都没有
    /// （既不是枚举值，也没有哪个迁移把它改名过来）。CP 遇到它会在**解析期** `TrackSkip`：
    /// <b>整条补丁不创建</b>，它的 `Entries`/`Fields` 也一并作废。
    /// <para>
    /// 本工具原先自己实现了这个操作（语料 0 处）—— 等于把 CP 会丢掉的补丁应用了出去，
    /// 与「复刻 CP 的行为」相反，已去掉。
    /// </para>
    /// </summary>
    [Fact]
    public void 未知操作名导致整条补丁跳过()
    {
        var outcome = EditDataMerger.Apply(
            JToken.Parse("""{ "k": "446 100 446" }"""),
            JObject.Parse("""
            {
              "Entries": { "other": "1" },
              "TextOperations": [ { "Operation": "ReplaceDelimited", "Target": ["Entries", "k"], "Search": "446", "ReplaceMode": "First" } ]
            }
            """));

        Assert.False(outcome.Success);                        // 整条作废，连同 Entries
        Assert.Contains("ReplaceDelimited", outcome.FailureReason);
    }

    [Fact]
    public void RemoveDelimited匹配大小写敏感()
    {
        var result = Apply("""{ "k": "a/A" }""",
            """{ "TextOperations": [ { "Operation": "RemoveDelimited", "Target": ["Entries", "k"], "Search": "A", "Delimiter": "/" } ] }""");

        Assert.Equal("a", result["k"]!.Value<string>());
    }

    [Fact]
    public void Fields作用于分隔字符串的下标()
    {
        // 条目是 "/" 分隔的字符串，字段键是下标
        var result = Apply("""{ "set-up": "fallFest/27 60/loadActors Set-Up" }""",
            """{ "TextOperations": [ { "Operation": "Append", "Target": ["Fields", "set-up", 2], "Value": "/animate Ariah false" } ] }""");

        Assert.Equal("fallFest/27 60/loadActors Set-Up/animate Ariah false", result["set-up"]!.Value<string>());
    }

    [Fact]
    public void Fields越界下标会补空字段()
    {
        // CP 语义：给定 "a/b"，写下标 4 得 "a/b///x"
        var result = Apply("""{ "e": "a/b" }""",
            """{ "TextOperations": [ { "Operation": "Append", "Target": ["Fields", "e", 4], "Value": "x" } ] }""");

        Assert.Equal("a/b///x", result["e"]!.Value<string>());
    }

    [Fact]
    public void Fields在DataAchievements用脱字符分隔()
    {
        var result = Apply("""{ "e": "a^b" }""",
            """{ "TextOperations": [ { "Operation": "Append", "Target": ["Fields", "e", 1], "Value": "c", "Delimiter": "^" } ] }""",
            assetName: "Data/Achievements");

        Assert.Equal("a^b^c", result["e"]!.Value<string>());
    }

    [Fact]
    public void Fields在对象条目上写字段()
    {
        var result = Apply("""{ "e": { "Name": "old" } }""",
            """{ "TextOperations": [ { "Operation": "Append", "Target": ["Fields", "e", "Name"], "Value": "X", "Delimiter": " " } ] }""");

        Assert.Equal("old X", result["e"]!["Name"]!.Value<string>());
    }

    [Fact]
    public void 多个文本操作按顺序应用()
    {
        var result = Apply("""{ "k": "A" }""",
            """{ "TextOperations": [ { "Operation": "Append", "Target": ["Entries", "k"], "Value": "B", "Delimiter": "/" }, { "Operation": "Append", "Target": ["Entries", "k"], "Value": "C", "Delimiter": "/" } ] }""");

        Assert.Equal("A/B/C", result["k"]!.Value<string>());
    }

    [Fact]
    public void TextOperations作用于TargetField之下()
    {
        var result = Apply("""{ "Dialogue": { "k": "A" } }""",
            """{ "TargetField": [ "Dialogue" ], "TextOperations": [ { "Operation": "Append", "Target": ["Entries", "k"], "Value": "B", "Delimiter": "/" } ] }""");

        Assert.Equal("A/B", result["Dialogue"]!["k"]!.Value<string>());
    }

    [Fact]
    public void null操作条目被跳过()
    {
        var result = Apply("""{ "k": "A" }""",
            """{ "TextOperations": [ null, { "Operation": "Append", "Target": ["Entries", "k"], "Value": "B", "Delimiter": "/" } ] }""");

        Assert.Equal("A/B", result["k"]!.Value<string>());
    }

    [Fact]
    public void 未知操作类型导致失败()
    {
        var outcome = EditDataMerger.Apply(JToken.Parse("""{ "k": "A" }"""),
            JObject.Parse("""{ "TextOperations": [ { "Operation": "Frobnicate", "Target": ["Entries", "k"], "Value": "B" } ] }"""));

        Assert.False(outcome.Success);
    }

    [Fact]
    public void RemoveDelimited缺少Delimiter导致失败()
    {
        var outcome = EditDataMerger.Apply(JToken.Parse("""{ "k": "A/B" }"""),
            JObject.Parse("""{ "TextOperations": [ { "Operation": "RemoveDelimited", "Target": ["Entries", "k"], "Search": "A" } ] }"""));

        Assert.False(outcome.Success);
    }

    /// <summary>
    /// ★ 「解析期」那级校验要**走在所有改动之前**（CP 也是在补丁创建前把整张表验一遍）。
    /// 排在中间的一条形状非法 ⇒ 整条补丁失败，**它前面那条也不能落下去** ——
    /// 否则失败后留下的是「改到一半」的文档，而 `EditDataMerger.Apply` 的原地改
    /// （`takeOwnership`）正是靠「要么原样失败、要么改完成功」才敢不克隆。
    /// </summary>
    [Fact]
    public void 中间一条操作非法时前面那条也不落下()
    {
        JToken data = JToken.Parse("""{ "A": "x/y" }""");
        var outcome = EditDataMerger.Apply(data, JObject.Parse("""
            { "TextOperations": [
                { "Operation": "Append", "Target": [ "A" ], "Value": "z" },
                { "Operation": "RemoveDelimited" }
            ] }
            """), takeOwnership: true);

        Assert.False(outcome.Success);
        Assert.Equal("x/y", ((JObject)data)["A"]!.Value<string>());
    }
}
