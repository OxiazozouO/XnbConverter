using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Judging;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 事件键前置条件的原生改写：把 CP 的 `When` 译成一段 `G &lt;GSQ&gt;` 追加到事件键上。
/// <para>
/// 最要紧的安全线：**只在「新事件 ID」时改写**（语料实测 239 条新事件 vs 33 条覆盖既有事件）。
/// </para>
/// <para>
/// ⚠ 这条安全线的**真正理由**不是「两边都触发」（原注释这么写，是错的）——
/// 游戏在 <c>checkForEvents</c> 里**首个命中就 break**，不会都触发。
/// 拦住它的是<b>另一条入口</b>：<c>GameLocation.findEventById</c>
/// （<c>GameLocation.cs:15680-15718</c>）按 ID **线性取第一个键、根本不看前置条件**。
/// 覆盖既有事件时原键还在 ⇒ 同 ID 两条键 ⇒ 该入口**永远**拿到我们那条，
/// 「条件不成立时走原版内容」这条路径因此不成立。
/// 详见 <c>Merging/EventKeyRewriter</c> 的类注释。
/// </para>
/// </summary>
public class EventKeyRewriterTests
{
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

    private static readonly HashSet<string> NoIds = new();
    private const string Gsq = "PLAYER_HAS_SEEN_EVENT Current 123";

    /// <summary>真实形态：SVE 的 `Data/Events/farm`，键是全新事件 ID。</summary>
    private const string NewEvent = """
    {
      "Action": "EditData",
      "Target": "Data/Events/farm",
      "Entries": { "221971519/": "continue/-1000 -1000/farmer 64 15 2" },
      "When": { "HasSeenEvent": "123" }
    }
    """;

    [Fact]
    public void 新事件把GSQ追加成前置条件段并删掉条件()
    {
        JObject? r = EventKeyRewriter.TryRewrite(Patch(NewEvent), Gsq, NoIds);

        Assert.NotNull(r);
        Assert.Equal("continue/-1000 -1000/farmer 64 15 2",
            r!["Entries"]!["221971519/G " + Gsq]!.Value<string>());
        Assert.Null(r["Entries"]!["221971519/"]);   // 旧键必须消失
        Assert.Null(r["When"]);
    }

    [Fact]
    public void 已有前置条件段时追加在末尾()
    {
        JObject? r = EventKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Events/Town",
          "Entries": { "172420001/e 172420000/t 0610 2600": "script" },
          "When": { "HasSeenEvent": "123" }
        }
        """), Gsq, NoIds);

        Assert.NotNull(r);
        Assert.NotNull(r!["Entries"]!["172420001/e 172420000/t 0610 2600/G " + Gsq]);
    }

    /// <summary>关键安全用例：覆盖既有事件时改键 = 新增一条，原事件仍会触发。</summary>
    [Fact]
    public void 覆盖既有事件ID时不改写()
    {
        JObject? r = EventKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Events/Town",
          "Entries": { "2000002/f Victor 1500/t 1000 1700": "script" },
          "When": { "HasSeenEvent": "123" }
        }
        """), Gsq, new HashSet<string> { "2000002" });

        Assert.Null(r);
    }

    [Fact]
    public void 资产不在树里时不改写()
    {
        // existingEventIds 为 null = 判不了「覆盖 vs 新增」→ 不猜。
        Assert.Null(EventKeyRewriter.TryRewrite(Patch(NewEvent), Gsq, null));
    }

    [Fact]
    public void 多条目里有一条覆盖既有事件时整条不改写()
    {
        JObject? r = EventKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Events/Town",
          "Entries": { "9999999/": "新", "2000002/f Victor 1500": "覆盖" },
          "When": { "HasSeenEvent": "123" }
        }
        """), Gsq, new HashSet<string> { "2000002" });

        Assert.Null(r);
    }

    [Fact]
    public void GSQ含斜杠时不改写()
    {
        // 段是按 `/` 切的，GSQ 里有 `/` 会被劈成两段。
        Assert.Null(EventKeyRewriter.TryRewrite(Patch(NewEvent), "LOCATION_NAME Here Town/Farm", NoIds));
    }

    [Fact]
    public void 已经带了G段时不改写()
    {
        Assert.Null(EventKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Events/Town",
          "Entries": { "9999999/G SEASON spring": "script" },
          "When": { "HasSeenEvent": "123" }
        }
        """), Gsq, NoIds));
    }

    /// <summary>`didntHear` 这类没有 `/` 的键不是事件键 —— 加 `/G` 会把它变成事件。</summary>
    [Fact]
    public void 非事件键不改写()
    {
        Assert.Null(EventKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Events/Town",
          "Entries": { "didntHear": "script" },
          "When": { "HasSeenEvent": "123" }
        }
        """), Gsq, NoIds));
    }

    [Fact]
    public void Target不是Data_Events时不改写()
    {
        Assert.Null(EventKeyRewriter.TryRewrite(Patch("""
        {
          "Action": "EditData",
          "Target": "Data/Festivals/spring13",
          "Entries": { "Ian/": "x" },
          "When": { "HasSeenEvent": "123" }
        }
        """), Gsq, NoIds));
    }

    [Fact]
    public void GSQ为空时不改写()
    {
        Assert.Null(EventKeyRewriter.TryRewrite(Patch(NewEvent), null, NoIds));
        Assert.Null(EventKeyRewriter.TryRewrite(Patch(NewEvent), "  ", NoIds));
    }

    [Fact]
    public void 改写是纯函数_原补丁不被改动()
    {
        ChangePatch patch = Patch(NewEvent);

        _ = EventKeyRewriter.TryRewrite(patch, Gsq, NoIds);

        Assert.NotNull(patch.Raw["Entries"]!["221971519/"]);
        Assert.NotNull(patch.Raw["When"]);
    }

    // ── 判定接线（判定与写入必须走同一个 TryRewrite）──────────────────────

    private readonly string _tree = Path.Combine(Path.GetTempPath(), "sdvev_t_" + Guid.NewGuid().ToString("N"));
    private readonly string _pack = Path.Combine(Path.GetTempPath(), "sdvev_p_" + Guid.NewGuid().ToString("N"));

    private void TreeFile(string rel, string content)
    {
        string full = Path.Combine(_tree, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private PatchVerdict Judge(string patchJson) =>
        new PatchJudge(new ResourceTree(_tree), _pack).Judge(Patch(patchJson));

    [Fact]
    public void 判定_新事件走原生键通道()
    {
        Directory.CreateDirectory(_tree);
        Directory.CreateDirectory(_pack);
        try
        {
            TreeFile("Data/Events/farm.json", """{ "4/f Abigail 1500/w sunny": "原版脚本" }""");

            PatchVerdict v = Judge("""
            {
              "Action": "EditData",
              "Target": "Data/Events/farm",
              "Entries": { "221971519/": "continue/-1000 -1000/farmer 64 15 2" },
              "When": { "HasSeenEvent": "123" }
            }
            """);

            Assert.Equal(Verdict.Converted, v.Verdict);
            Assert.Equal(Channel.NativeKey, v.Channel);
        }
        finally
        {
            Directory.Delete(_tree, recursive: true);
            Directory.Delete(_pack, recursive: true);
        }
    }

    [Fact]
    public void 判定_覆盖既有事件时保持原判定()
    {
        Directory.CreateDirectory(_tree);
        Directory.CreateDirectory(_pack);
        try
        {
            TreeFile("Data/Events/farm.json", """{ "2000002/f Victor 1500": "原版脚本" }""");

            PatchVerdict v = Judge("""
            {
              "Action": "EditData",
              "Target": "Data/Events/farm",
              "Entries": { "2000002/f Victor 1500": "改后的脚本" },
              "When": { "HasSeenEvent": "123" }
            }
            """);

            Assert.NotEqual(Channel.NativeKey, v.Channel);
        }
        finally
        {
            Directory.Delete(_tree, recursive: true);
            Directory.Delete(_pack, recursive: true);
        }
    }
}
