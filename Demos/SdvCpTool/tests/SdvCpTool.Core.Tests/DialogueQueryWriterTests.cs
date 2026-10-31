using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Assets;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// Task 10h：对话值内联 GSQ（<c>$query &lt;GSQ&gt;#&lt;真&gt;|&lt;假&gt;</c>）。
/// <para>
/// 最要紧的两条安全线：① <c>$query</c> 值只有真假两个分支，同一键上再多一条条件补丁就会
/// 互相嵌套（原版解析不了三岔）；② 只有四个逐条查过调用链的宿主生效，
/// <c>Strings/StringsFromMaps</c>（地图 <c>Action: Message</c>）与事件 <c>message</c> 那条
/// <c>DialogueBox(string)</c> 路径里 <c>$query</c> 是<b>死文本</b>。
/// </para>
/// </summary>
public class DialogueQueryWriterTests : IDisposable
{
    private readonly string _tree = Path.Combine(Path.GetTempPath(), "sdvdq_t_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvdq_p_" + Guid.NewGuid().ToString("N"));
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvdq_s_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvdq_o_" + Guid.NewGuid().ToString("N"));

    public DialogueQueryWriterTests()
    {
        Directory.CreateDirectory(_tree);
        Directory.CreateDirectory(_pack);
        Directory.CreateDirectory(_src);
    }

    private OutputTree? _outTree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush()
    {
        _outTree?.Flush();
    }

    public void Dispose()
    {
        Directory.Delete(_tree, recursive: true);
        Directory.Delete(_pack, recursive: true);
        Directory.Delete(_src, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
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
            SourceFile = "content.json"
        };
    }

    private void TreeFile(string rel, string content)
    {
        string full = Path.Combine(_tree, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // ------------------------------------------------------------------ Compose（唯一的合成实现）

    [Fact]
    public void 合成_形状为_query空格GSQ井号新竖线旧()
    {
        Assert.Equal("$query PLAYER_HEARTS Current Abigail 8 8#新|旧",
            DialogueQueryWriter.Compose("PLAYER_HEARTS Current Abigail 8 8", "新", "旧"));
    }

    /// <summary>
    /// 绝不能产出 <c>$query Q#新|</c>（假分支为空）。
    /// <para>
    /// ★ 这里要挡住的是一个**看起来很像**的诱惑：「假分支留空 = 条件不成立时就当没这条」。
    /// <b>游戏侧不是这样</b>：<c>NPC.tryToRetrieveDialogue</c> 的候选键链只判 <c>!= null</c>
    /// （<c>NPC.cs:4024-4026</c>/<c>:4032-4034</c>/<c>:4042-4044</c>/<c>:4048-4050</c>/
    /// <c>:4053-4055</c>/<c>:4062-4068</c>），<b>不判空</b>；<c>Dialogue.TryGetDialogue</c>
    /// （<c>Dialogue.cs:447-455</c>）只在**键找不到**时返回 <c>null</c>，值是空串照样构造出
    /// 一个 <c>Dialogue</c>。⇒ 空假分支会**命中并返回一句空话**，而不是继续往下试候选键。
    /// 「条件不成立时不显示」与「显示空对话」是**两种语义**。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(null)]      // 取不到旧值
    [InlineData("")]        // 旧值为空
    public void 合成_旧值缺失或为空时返回null(string? prior)
    {
        Assert.Null(DialogueQueryWriter.Compose("Q", "新", prior));
    }

    [Fact]
    public void 合成_旧值等于新值时返回null()
    {
        // `$query Q#新|新` 毫无意义，且说明旧值取错了。
        Assert.Null(DialogueQueryWriter.Compose("Q", "新", "新"));
    }

    [Fact]
    public void 合成_旧值本身已是query时返回null()
    {
        // 嵌套 $query 会被原版用原始 masterString 重新切分，取到错误分支。
        Assert.Null(DialogueQueryWriter.Compose("Q2", "新", "$query Q1#A|B"));
    }

    [Fact]
    public void 合成_分支含竖线时返回null()
    {
        Assert.Null(DialogueQueryWriter.Compose("Q", "新|带竖线", "旧"));
        Assert.Null(DialogueQueryWriter.Compose("Q", "新", "旧|带竖线"));
    }

    [Fact]
    public void 写入_mod自带原生query值时失败理由指向竖线而不是旧值()
    {
        // 真实回归：「礼物对话」的 AcceptGift_(O)221 第二条补丁，值本身就是 mod 写的
        // 原生 `$query …#$action …|…`。它含 `|` ⇒ 套不进我们的 `$query`。
        // 原先的文案只说「取不到可用的原有值（缺失/空/与新值相同/本身已是 $query）」——
        // 4 项都不对，照着查旧值只会查错方向。
        TreeFile("Characters/Dialogue/Daia.json", """{ "AcceptGift_(O)221": "旧回复" }""");
        Directory.CreateDirectory(Path.Combine(_src, "Characters", "Dialogue"));
        File.WriteAllText(Path.Combine(_src, "Characters", "Dialogue", "Daia.json"),
            File.ReadAllText(Path.Combine(_tree, "Characters", "Dialogue", "Daia.json")));

        var applier = new PatchApplier(_outTree = new OutputTree(_src, _out));
        MergeOutcome outcome = applier.ApplyEditData(
            "Characters/Dialogue/Daia",
            """
            { "Action": "EditData", "Target": "Characters/Dialogue/Daia",
              "Entries": { "AcceptGift_(O)221": "$query !PLAYER_HAS_COOKING_RECIPE Current \"Pink Cake\"#A|B" } }
            """,
            isArrayAsset: false,
            gsq: "DAYS_PLAYED 161",
            conditionLanding: LandingKind.DialogueQuery);

        Assert.False(outcome.Success);
        Assert.Contains("`|`", outcome.FailureReason);
    }

    // ------------------------------------------------------------------ 宿主与键

    /// <summary>
    /// ★ <c>Data/ExtraDialogue</c> 与 <c>Data/EngagementDialogue</c> 是**整片** <c>$query</c> 宿主。
    /// <para>
    /// 依据：把 <c>new Dialogue(</c> / <c>Dialogue.TryGetDialogue(</c> 的实参字面量抓出来数，
    /// 与 <c>drawObjectDialogue(</c>（走 <c>DialogueBox(string)</c>，死路）**分开**统计 ——
    /// 这两个资产**只出现在活路那一侧**（11 处 / 4 处，死路 0 处）。
    /// </para>
    /// <para>
    /// ⚠ 同一轮扫描里 <c>Strings/Characters</c> 活路 18、死路 24，<c>Strings/Locations</c> 死路 121
    /// —— **两条路都有**，所以刻意**不收**。这条测试把「收谁、不收谁」的分界钉住。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Data/ExtraDialogue", "Town_DumpsterDiveComment_Child")]
    [InlineData("Data/EngagementDialogue", "Abigail0")]
    public void 两个新宿主可转(string asset, string key)
    {
        ChangePatch p = Patch($$"""
        {
          "Action": "EditData",
          "Target": "{{asset}}",
          "Entries": { "{{key}}": "新" },
          "When": { "HasSeenEvent": "1" }
        }
        """);

        Assert.NotNull(DialogueQueryWriter.TryRewrite(p, "PLAYER_HAS_SEEN_EVENT Current 1",
            overlap: null, npcNames: null));
    }

    /// <summary>两条路都有的资产**不收** —— 同一个资产里有的键走活路、有的走死路，静态分不出。</summary>
    [Theory]
    [InlineData("Strings/Characters", "SpouseWantsChild")]
    [InlineData("Strings/Locations", "FarmHouse_Bed_GoToSleep")]
    [InlineData("Strings/UI", "SomeKey")]
    public void 两条路都有的资产不当宿主(string asset, string key)
    {
        ChangePatch p = Patch($$"""
        {
          "Action": "EditData",
          "Target": "{{asset}}",
          "Entries": { "{{key}}": "新" },
          "When": { "HasSeenEvent": "1" }
        }
        """);

        Assert.Null(DialogueQueryWriter.TryRewrite(p, "PLAYER_HAS_SEEN_EVENT Current 1",
            overlap: null, npcNames: null, priorLookup: null, out string? refusal));
        Assert.Contains("宿主", refusal!);
    }

    [Fact]
    public void 对话资产_基础加一条条件两条重叠时可转()
    {
        ChangePatch p = Patch("""
        {
          "Action": "EditData",
          "Target": "Characters/Dialogue/Abigail",
          "Entries": { "Mon8": "新" },
          "When": { "Relationship:Abigail": "Dating" }
        }
        """);

        JObject? r = DialogueQueryWriter.TryRewrite(p, "PLAYER_NPC_RELATIONSHIP Current Abigail Dating",
            overlap: null, npcNames: null);

        Assert.NotNull(r);
        Assert.Equal("新", r!["Entries"]!["Mon8"]!.Value<string>());   // 判定阶段不动值
    }

    [Fact]
    public void 判定阶段不改动原补丁()
    {
        ChangePatch p = Patch("""
        {
          "Action": "EditData",
          "Target": "Characters/Dialogue/Abigail",
          "Entries": { "Mon8": "新" },
          "When": { "Season": "spring" }
        }
        """);

        _ = DialogueQueryWriter.TryRewrite(p, "SEASON spring", null, null);

        Assert.Equal("新", p.Raw["Entries"]!["Mon8"]!.Value<string>());
    }

    [Theory]
    [InlineData("Strings/StringsFromMaps")]              // 地图 Action: Message → DialogueBox(string)，$query 是死文本
    [InlineData("Data/Events/MyEvent")]                  // 事件 message → DialogueBox(string)
    [InlineData("Characters/Dialogue")]                  // 少了最后一段，不是具体 NPC 文件
    [InlineData("Characters/Dialogue/Sub/Abigail")]      // 多了一段，不是 <NPC> 文件
    [InlineData("Data/Festivals/spring13")]              // NPC 名集合为 null 时不认（下面另有正例）
    public void 非宿主_绝不套query(string target)
    {
        ChangePatch p = Patch($$"""
        {
          "Action": "EditData",
          "Target": "{{target}}",
          "Entries": { "Message": "新" },
          "When": { "Season": "spring" }
        }
        """);

        Assert.Null(DialogueQueryWriter.TryRewrite(p, "SEASON spring", null, null));
    }

    [Fact]
    public void 事件资产的message键不被套query()
    {
        ChangePatch p = Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Events/Farm",
          "Entries": { "123456/message": "新" },
          "When": { "Season": "spring" }
        }
        """);

        Assert.Null(DialogueQueryWriter.TryRewrite(p, "SEASON spring", null, null));
    }

    [Fact]
    public void 未知宿主不套query()
    {
        ChangePatch p = Patch("""
        {
          "Action": "EditData",
          "Target": "Data/mail",
          "Entries": { "spring_13": "新" },
          "When": { "Season": "spring" }
        }
        """);

        Assert.Null(DialogueQueryWriter.TryRewrite(p, "SEASON spring", null, null));
    }

    // ------------------------------------------------------------------ 重叠计数（三岔表达不了）

    private DialogueQueryOverlap Overlap(params ChangePatch[] patches) => DialogueQueryOverlap.Build(patches);

    private static ChangePatch Base(string target, string key, string value) => Patch($$"""
    {
      "Action": "EditData",
      "Target": "{{target}}",
      "Entries": { "{{key}}": "{{value}}" }
    }
    """);

    private static ChangePatch Cond(string target, string key, string value) => Patch($$"""
    {
      "Action": "EditData",
      "Target": "{{target}}",
      "Entries": { "{{key}}": "{{value}}" },
      "When": { "Season": "spring" }
    }
    """);

    [Fact]
    public void 重叠2_一条基础一条条件时可转()
    {
        ChangePatch conditional = Cond("Characters/Dialogue/Abigail", "Mon8", "新");
        var overlap = Overlap(Base("Characters/Dialogue/Abigail", "Mon8", "旧"), conditional);

        Assert.NotNull(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring", overlap, null));
    }

    [Fact]
    public void 重叠3_不转()
    {
        ChangePatch conditional = Cond("Characters/Dialogue/Torts", "Tue8", "新C");
        var overlap = Overlap(
            Base("Characters/Dialogue/Torts", "Tue8", "基"),
            Cond("Characters/Dialogue/Torts", "Tue8", "新A"),
            conditional,
            Cond("Characters/Dialogue/Torts", "Tue8", "新B"));

        Assert.Null(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring", overlap, null));
    }

    [Fact]
    public void 同一键两条条件补丁_不转()
    {
        // 两条条件补丁都转的话，后一条的假分支是前一条的 $query —— 会嵌套，原版解析不了三岔。
        ChangePatch conditional = Cond("Strings/StringsFromCSFiles", "Event.cs.1527", "新B");
        var overlap = Overlap(
            Cond("Strings/StringsFromCSFiles", "Event.cs.1527", "新A"),
            conditional);

        Assert.Null(DialogueQueryWriter.TryRewrite(conditional, "SEASON fall, DAY_OF_MONTH 20",
            overlap, null));
    }

    [Fact]
    public void 该键没有无条件补丁_取不到旧值不转()
    {
        ChangePatch conditional = Cond("Characters/Dialogue/Daia", "Mon8", "新");
        var overlap = Overlap(conditional);

        Assert.Null(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring", overlap, null));
    }

    /// <summary>
    /// ★ 拒绝时要说明**实际拦住它的那一条**，而不是笼统说「对话键语法受限」。
    /// <para>
    /// 实测语料里的绝大多数 <c>Characters/Dialogue</c> 有条件补丁是被**结构前提**拦下的
    /// （新增键、该键上没有无条件补丁 ⇒ 假分支无值可填），不是被键语法拦下的。
    /// 报错方向错了，读者会照着错的方向去改。
    /// </para>
    /// </summary>
    [Fact]
    public void 新增键没有旧值_缘由说的是没有无条件补丁而不是键语法()
    {
        ChangePatch conditional = Cond("Characters/Dialogue/Daia", "Mon8", "新");

        Assert.Null(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring",
            Overlap(conditional), null, priorLookup: null, out string? refusal));

        Assert.NotNull(refusal);
        Assert.Contains("没有无条件补丁", refusal);
        Assert.DoesNotContain("受限语法", refusal);
    }

    // ── 旧值的**第二个来源**：基础资产本身 ──────────────────────────────────
    // 判定阶段原先只认「同键上的无条件补丁」，而写入器的 PriorString 读的是**合并前的目标数据**
    // （**基础资产也算**）⇒ 判定比写入**严了一档**。实测语料里这类补丁的 73 个条目键中
    // **46 个原版本来就有**（如 Characters/Dialogue/Shane 的 FlowerDance_Accept）。

    /// <summary>★ 没有无条件补丁，但**基础资产里有这个键** ⇒ 转得成。</summary>
    [Fact]
    public void 没有无条件补丁但基础资产里有旧值时仍可转()
    {
        ChangePatch conditional = Cond("Characters/Dialogue/Shane", "FlowerDance_Accept", "新文案");
        var overlap = Overlap(conditional);

        JObject? r = DialogueQueryWriter.TryRewrite(conditional, "SEASON spring", overlap, null,
            priorLookup: null, baseLookup: key => key == "FlowerDance_Accept" ? "原版文案" : null,
            out string? refusal);

        Assert.Null(refusal);
        Assert.NotNull(r);
    }

    /// <summary>基础资产里也没有这个键 ⇒ 照旧拒绝（假分支无处可取）。</summary>
    [Fact]
    public void 基础资产里也没有这个键时仍拒绝()
    {
        ChangePatch conditional = Cond("Characters/Dialogue/Shane", "BrandNewKey", "新");
        var overlap = Overlap(conditional);

        Assert.Null(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring", overlap, null,
            priorLookup: null, baseLookup: _ => null, out string? refusal));

        Assert.NotNull(refusal);
        Assert.Contains("基础资产里也没有这个键", refusal);
    }

    /// <summary>
    /// ★ 判定阶段就把**合成**验一遍：基础旧值与新值相同 ⇒ 现在（判定时）就拒绝，
    /// 而不是判成可写、写入时才失败。
    /// </summary>
    [Fact]
    public void 基础旧值与新值相同时判定阶段就拒绝()
    {
        ChangePatch conditional = Cond("Characters/Dialogue/Shane", "FlowerDance_Accept", "一样的话");
        var overlap = Overlap(conditional);

        Assert.Null(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring", overlap, null,
            priorLookup: null, baseLookup: _ => "一样的话", out string? refusal));

        Assert.NotNull(refusal);
        Assert.Contains("基础资产里的旧值不可用", refusal);
    }

    /// <summary>基础旧值本身已是 `$query` ⇒ 会嵌套 ⇒ 判定阶段就拒绝。</summary>
    [Fact]
    public void 基础旧值已是query时判定阶段就拒绝()
    {
        ChangePatch conditional = Cond("Characters/Dialogue/Shane", "K", "新");
        var overlap = Overlap(conditional);

        Assert.Null(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring", overlap, null,
            priorLookup: null, baseLookup: _ => "$query Q#a|b", out string? refusal));

        Assert.Contains("基础资产里的旧值不可用", refusal!);
    }

    /// <summary>同一键两条条件补丁时，缘由要指明「会嵌套」，不能说成缺旧值。</summary>
    [Fact]
    public void 同一键两条条件补丁_缘由说的是会嵌套()
    {
        ChangePatch conditional = Cond("Characters/Dialogue/Daia", "Mon8", "新B");
        var overlap = Overlap(Cond("Characters/Dialogue/Daia", "Mon8", "新A"), conditional);

        Assert.Null(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring",
            overlap, null, priorLookup: null, out string? refusal));

        Assert.NotNull(refusal);
        Assert.Contains("嵌套", refusal);
    }

    /// <summary>非宿主（地图 <c>Message</c> 走的 <c>Strings/StringsFromMaps</c>）的缘由要指明「不是宿主」。</summary>
    [Fact]
    public void 非宿主_缘由说它不是query宿主()
    {
        ChangePatch conditional = Cond("Strings/StringsFromMaps", "PirateBartender_PirateClothes", "新");

        Assert.Null(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring",
            Overlap(conditional), null, priorLookup: null, out string? refusal));

        Assert.NotNull(refusal);
        Assert.Contains("宿主", refusal);
    }

    // ------------------------------------------------------------------ 写入：真/假分支逐字核对

    [Fact]
    public void 写入_值形如query且假分支是合并前的原值()
    {
        TreeFile("Characters/Dialogue/Abigail.json", """{ "Mon8": "原来的对话" }""");
        Directory.CreateDirectory(Path.Combine(_src, "Characters", "Dialogue"));
        File.Copy(Path.Combine(_tree, "Characters", "Dialogue", "Abigail.json"),
            Path.Combine(_src, "Characters", "Dialogue", "Abigail.json"));

        var applier = new PatchApplier(_outTree = new OutputTree(_src, _out));
        MergeOutcome outcome = applier.ApplyEditData(
            "Characters/Dialogue/Abigail",
            """{ "Action": "EditData", "Target": "Characters/Dialogue/Abigail", "Entries": { "Mon8": "新对话" } }""",
            isArrayAsset: false,
            gsq: "PLAYER_NPC_RELATIONSHIP Current Abigail Dating",
            conditionLanding: LandingKind.DialogueQuery);

        Assert.True(outcome.Success, outcome.FailureReason);
Flush();

        JObject written = JObject.Parse(
            File.ReadAllText(Path.Combine(_out, "Characters", "Dialogue", "Abigail.json")));
        Assert.Equal("$query PLAYER_NPC_RELATIONSHIP Current Abigail Dating#新对话|原来的对话",
            written["Mon8"]!.Value<string>());
    }

    [Fact]
    public void 写入_取不到旧值时整条失败且不产出错误query()
    {
        TreeFile("Characters/Dialogue/Daia.json", """{ "Tue6": "别的键" }""");
        Directory.CreateDirectory(Path.Combine(_src, "Characters", "Dialogue"));
        File.WriteAllText(Path.Combine(_src, "Characters", "Dialogue", "Daia.json"),
            File.ReadAllText(Path.Combine(_tree, "Characters", "Dialogue", "Daia.json")));

        var applier = new PatchApplier(_outTree = new OutputTree(_src, _out));
        MergeOutcome outcome = applier.ApplyEditData(
            "Characters/Dialogue/Daia",
            """{ "Action": "EditData", "Target": "Characters/Dialogue/Daia", "Entries": { "Mon8": "新对话" } }""",
            isArrayAsset: false,
            gsq: "SEASON spring",
            conditionLanding: LandingKind.DialogueQuery);

        Assert.False(outcome.Success);
        // 瞄**具体**缘由（Compose 的 6 个分支各有各的话），不瞄笼统的「原有值」。
        Assert.Contains("取不到旧值", outcome.FailureReason);

        // 产物里那条补丁一个字都没落：Mon8 不存在，更不存在错误的 `$query Q#新|`。
        string written = File.ReadAllText(Path.Combine(_out, "Characters", "Dialogue", "Daia.json"));
        Assert.DoesNotContain("$query", written);
        Assert.Null(JObject.Parse(written)["Mon8"]);
    }

    [Fact]
    public void 写入_不碰StringsFromMaps()
    {
        TreeFile("Strings/StringsFromMaps.json", """{ "Message": "原值" }""");
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(Path.Combine(_src, "Strings"));
        File.WriteAllText(Path.Combine(_src, "Strings", "StringsFromMaps.json"),
            File.ReadAllText(Path.Combine(_tree, "Strings", "StringsFromMaps.json")));

        var applier = new PatchApplier(_outTree = new OutputTree(_src, _out));
        MergeOutcome outcome = applier.ApplyEditData(
            "Strings/StringsFromMaps",
            """{ "Action": "EditData", "Target": "Strings/StringsFromMaps", "Entries": { "Message": "新值" } }""",
            isArrayAsset: false,
            gsq: "SEASON spring",
            conditionLanding: LandingKind.DialogueQuery);

        Assert.False(outcome.Success);
        string written = File.ReadAllText(Path.Combine(_out, "Strings", "StringsFromMaps.json"));
        Assert.DoesNotContain("$query", written);
        Assert.Equal("原值", JObject.Parse(written)["Message"]!.Value<string>());
    }

    [Fact]
    public void 写入_节日NPC键走query_写入阶段也认识NPC名()
    {
        // 回归：写入阶段一度漏传 NPC 名集合，导致判定说"可转"、写入说"不行"（送不进产物）。
        TreeFile("Data/Festivals/spring24.json", """{ "Paula": "原来的对话" }""");
        Directory.CreateDirectory(Path.Combine(_src, "Data", "Festivals"));
        File.WriteAllText(Path.Combine(_src, "Data", "Festivals", "spring24.json"),
            File.ReadAllText(Path.Combine(_tree, "Data", "Festivals", "spring24.json")));

        var npcNames = new HashSet<string>(StringComparer.Ordinal) { "Paula" };
        var applier = new PatchApplier(_outTree = new OutputTree(_src, _out), npcNames: npcNames);
        MergeOutcome outcome = applier.ApplyEditData(
            "Data/Festivals/spring24",
            """{ "Action": "EditData", "Target": "Data/Festivals/spring24", "Entries": { "Paula": "新对话" } }""",
            isArrayAsset: false,
            gsq: "PLAYER_HAS_DIALOGUE_ANSWER Current 75163521",
            conditionLanding: LandingKind.DialogueQuery);

        Assert.True(outcome.Success, outcome.FailureReason);
        Flush();
        JObject written = JObject.Parse(
            File.ReadAllText(Path.Combine(_out, "Data", "Festivals", "spring24.json")));
        Assert.Equal("$query PLAYER_HAS_DIALOGUE_ANSWER Current 75163521#新对话|原来的对话",
            written["Paula"]!.Value<string>());
    }

    [Fact]
    public void 写入_节日结构键mainEvent绝不套query()
    {
        TreeFile("Data/Festivals/spring24.json", """{ "mainEvent": "旧脚本/继续" }""");
        Directory.CreateDirectory(Path.Combine(_src, "Data", "Festivals"));
        File.WriteAllText(Path.Combine(_src, "Data", "Festivals", "spring24.json"),
            File.ReadAllText(Path.Combine(_tree, "Data", "Festivals", "spring24.json")));

        // NPC 名集合里没有 mainEvent → 写入阶段同样拒绝。
        var npcNames = new HashSet<string>(StringComparer.Ordinal) { "Paula" };
        var applier = new PatchApplier(_outTree = new OutputTree(_src, _out), npcNames: npcNames);
        MergeOutcome outcome = applier.ApplyEditData(
            "Data/Festivals/spring24",
            """{ "Action": "EditData", "Target": "Data/Festivals/spring24", "Entries": { "mainEvent": "新脚本/继续" } }""",
            isArrayAsset: false,
            gsq: "SEASON spring",
            conditionLanding: LandingKind.DialogueQuery);

        Assert.False(outcome.Success);
        string written = File.ReadAllText(Path.Combine(_out, "Data", "Festivals", "spring24.json"));
        Assert.DoesNotContain("$query", written);
        Assert.Equal("旧脚本/继续", JObject.Parse(written)["mainEvent"]!.Value<string>());
    }

    // ------------------------------------------------------------------ 判定接线

    private PatchVerdict Judge(string patchJson, params ChangePatch[] all)
    {
        ChangePatch p = Patch(patchJson);
        var overlap = DialogueQueryOverlap.Build(all.Length > 0 ? all : new[] { p });
        return new PatchJudge(new ResourceTree(_tree), _pack, null, null, overlap, null).Judge(p);
    }

    [Fact]
    public void 判定_基础加一条条件时可转且走GSQ通道()
    {
        TreeFile("Characters/Dialogue/Abigail.json", """{ "Mon8": "原来的对话" }""");
        ChangePatch conditional = Patch("""
        {
          "Action": "EditData",
          "Target": "Characters/Dialogue/Abigail",
          "Entries": { "Mon8": "新对话" },
          "When": { "Season": "spring" }
        }
        """);
        var overlap = Overlap(Base("Characters/Dialogue/Abigail", "Mon8", "旧"), conditional);

        PatchVerdict v = new PatchJudge(new ResourceTree(_tree), _pack, null, null, overlap, null)
            .Judge(conditional);

        Assert.Equal(Verdict.Converted, v.Verdict);
        Assert.Equal(Channel.Gsq, v.Channel);
        Assert.Equal(LandingKind.DialogueQuery, v.Landing);
        Assert.True(WritePolicy.ShouldWrite(v));
    }

    [Fact]
    public void 判定_重叠3条条件补丁时保持NeedsReview()
    {
        TreeFile("Characters/Dialogue/Torts.json", """{ "Tue8": "基" }""");
        ChangePatch c1 = Cond("Characters/Dialogue/Torts", "Tue8", "A");
        ChangePatch c2 = Cond("Characters/Dialogue/Torts", "Tue8", "B");
        ChangePatch c3 = Patch("""
        {
          "Action": "EditData",
          "Target": "Characters/Dialogue/Torts",
          "Entries": { "Tue8": "C" },
          "When": { "Season": "spring" }
        }
        """);
        ChangePatch baseline = Base("Characters/Dialogue/Torts", "Tue8", "基");
        var overlap = Overlap(baseline, c1, c2, c3);

        PatchVerdict v = new PatchJudge(new ResourceTree(_tree), _pack, null, null, overlap, null).Judge(c3);

        Assert.NotEqual(Verdict.Converted, v.Verdict);
        Assert.NotEqual(Channel.Gsq, v.Channel);
    }

    [Fact]
    public void 判定_StringsFromMaps永不套query()
    {
        TreeFile("Strings/StringsFromMaps.json", """{ "Message": "原值" }""");

        PatchVerdict v = Judge("""
        {
          "Action": "EditData",
          "Target": "Strings/StringsFromMaps",
          "Entries": { "Message": "新值" },
          "When": { "Season": "spring" }
        }
        """);

        Assert.NotEqual(LandingKind.DialogueQuery, v.Landing);
        Assert.NotEqual(Channel.Gsq, v.Channel);
    }

    // ------------------------------------------------------------------ 其它宿主

    [Fact]
    public void 日程结束语_Strings_schedules_可转()
    {
        ChangePatch conditional = Cond("Strings/schedules/Ian", "Sun.000", "新");

        Assert.NotNull(DialogueQueryWriter.TryRewrite(conditional, "SEASON spring",
            Overlap(Base("Strings/schedules/Ian", "Sun.000", "旧"), conditional), null));
    }

    [Fact]
    public void 节日主持问答_只认Event_cs_1527这一个键()
    {
        ChangePatch conditional = Cond("Strings/StringsFromCSFiles", "Event.cs.1527", "新");
        var overlap = Overlap(Base("Strings/StringsFromCSFiles", "Event.cs.1527", "原"), conditional);
        Assert.NotNull(DialogueQueryWriter.TryRewrite(conditional, "SEASON winter, DAY_OF_MONTH 28", overlap, null));

        ChangePatch other = Cond("Strings/StringsFromCSFiles", "NPC.cs.4274", "新");
        var overlap2 = Overlap(Base("Strings/StringsFromCSFiles", "NPC.cs.4274", "原"), other);
        Assert.Null(DialogueQueryWriter.TryRewrite(other, "SEASON winter", overlap2, null));
    }

    [Fact]
    public void 节日对白_键是NPC名才转_结构键不转()
    {
        var npcNames = new HashSet<string>(StringComparer.Ordinal) { "Paula" };

        ChangePatch conditional = Cond("Data/Festivals/spring24", "Paula", "新");
        var overlap = Overlap(Base("Data/Festivals/spring24", "Paula", "旧"), conditional);
        Assert.NotNull(DialogueQueryWriter.TryRewrite(conditional, "PLAYER_HAS_DIALOGUE_ANSWER Current 75163521",
            overlap, npcNames));

        // mainEvent 是事件脚本，套 $query 会把节日弄坏。
        ChangePatch mainEvent = Cond("Data/Festivals/spring24", "mainEvent", "新脚本");
        var overlap2 = Overlap(Base("Data/Festivals/spring24", "mainEvent", "旧脚本"), mainEvent);
        Assert.Null(DialogueQueryWriter.TryRewrite(mainEvent, "SEASON spring", overlap2, npcNames));
    }

    // ------------------------------------------------------------------ NPC 名集合

    [Fact]
    public void NPC名集合_原版清单与包内新建NPC都收()
    {
        TreeFile("Data/Characters.json", """{ "Abigail": {}, "Marnie": {} }""");
        ChangePatch adds = Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Characters",
          "Entries": { "Paula": { "DisplayName": "Paula" } }
        }
        """);

        IReadOnlySet<string> names = NpcNames.Build(new ResourceTree(_tree), new[] { adds });

        Assert.Contains("Abigail", names);
        Assert.Contains("Marnie", names);
        Assert.Contains("Paula", names);
    }
}
