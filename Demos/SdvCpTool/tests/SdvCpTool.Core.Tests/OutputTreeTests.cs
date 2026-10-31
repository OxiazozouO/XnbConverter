using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class OutputTreeTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "sdvsrc_" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvout_" + Guid.NewGuid().ToString("N"));

    public OutputTreeTests() => Directory.CreateDirectory(_src);

    private OutputTree? _tree;

    /// <summary>断言产出之前先把暂存刷下去 —— 写现在**只有延迟写**一条路。</summary>
    private void Flush()
    {
        _tree?.Flush();
    }

    public void Dispose()
    {
        Directory.Delete(_src, recursive: true);
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void Touch(string rel, string content = "{}")
    {
        string full = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void 构造时整树拷贝()
    {
        Touch("Data/Town.json", "a");
        Touch("Maps/Town.tbin", "b");
        Touch("Data/Town.config", "c");

        _ = new OutputTree(_src, _out);

        Assert.True(File.Exists(Path.Combine(_out, "Data", "Town.json")));
        Assert.True(File.Exists(Path.Combine(_out, "Maps", "Town.tbin")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(_out, "Data", "Town.json")));
    }

    [Fact]
    public void 输出目录不存在时自动创建()
    {
        Touch("Data/Town.json");

        _ = new OutputTree(_src, _out);

        Assert.True(Directory.Exists(_out));
    }

    [Fact]
    public void 原树不被修改()
    {
        Touch("Data/Town.json", "original");

        var tree = _tree = new OutputTree(_src, _out);
        tree.WriteText("Data/Town.json", "changed");
        tree.Flush();

        Assert.Equal("original", File.ReadAllText(Path.Combine(_src, "Data", "Town.json")));
        Assert.Equal("changed", File.ReadAllText(Path.Combine(_out, "Data", "Town.json")));
    }

    [Fact]
    public void 写入不存在的相对路径时自动建目录()
    {
        Touch("Data/Town.json");

        var tree = _tree = new OutputTree(_src, _out);
        tree.WriteText("Data/Events/New.json", "{}");
        tree.Flush();

        Assert.True(File.Exists(Path.Combine(_out, "Data", "Events", "New.json")));
    }

    [Fact]
    public void 相对路径越界被拒绝()
    {
        Touch("Data/Town.json");
        var tree = _tree = new OutputTree(_src, _out);

        // 防止 `..` 逃逸出输出目录
        Assert.Throws<ArgumentException>(() => tree.WriteText("../evil.json", "x"));
    }

    [Fact]
    public void 列出输入树的文件()
    {
        Touch("Data/Town.json");
        Touch("Maps/Town.tbin");

        var tree = _tree = new OutputTree(_src, _out);

        Assert.Contains("Data/Town.json", tree.InputFiles);
        Assert.Contains("Maps/Town.tbin", tree.InputFiles);
    }

    // ---------------------------------------------------------------- 输出路径校验（Task 16）

    [Fact]
    public void 路径含非法字符时被拒绝()
    {
        Touch("Data/Town.json");
        var tree = _tree = new OutputTree(_src, _out);

        // SVE 的 `**Martin**` 就是这一形态（markdown 加粗被当成资产名）
        string? error = tree.ValidatePath("Characters/Dialogue/**Martin**.json");

        Assert.NotNull(error);
        Assert.Contains("非法字符", error);
    }

    [Fact]
    public void 路径是保留设备名时被拒绝()
    {
        Touch("Data/Town.json");
        var tree = _tree = new OutputTree(_src, _out);

        Assert.Contains("保留设备名", tree.ValidatePath("Data/NUL.json"));
        Assert.Contains("保留设备名", tree.ValidatePath("Data/Con"));
        Assert.Contains("保留设备名", tree.ValidatePath("Data/aux.json"));
        Assert.Null(tree.ValidatePath("Data/Console.json"));   // 前缀相同但非保留名，不得误伤
    }

    [Fact]
    public void 正常路径校验通过()
    {
        Touch("Data/Town.json");
        var tree = _tree = new OutputTree(_src, _out);

        Assert.Null(tree.ValidatePath("Data/Events/New.json"));
        Assert.Null(tree.ValidatePath("Characters/Dialogue/Abigail.json"));
    }

    [Fact]
    public void TryWriteText遇坏路径返回失败不抛异常()
    {
        Touch("Data/Town.json");
        var tree = _tree = new OutputTree(_src, _out);

        bool ok = tree.TryWriteText("Characters/Dialogue/**Martin**.json", "{}", out string? error);

        Assert.False(ok);
        Assert.Contains("非法字符", error);
    }

    [Fact]
    public void 一条坏路径不影响其余写入()
    {
        Touch("Data/Town.json");
        var tree = _tree = new OutputTree(_src, _out);

        // 回归钉子：坏的那条失败，好的那条照常落盘 —— 单条坏路径绝不得终止整次运行
        Assert.False(tree.TryWriteText("Characters/Dialogue/**Martin**.json", "{}", out _));
        Assert.True(tree.TryWriteText("Data/Good.json", """{ "ok": true }""", out string? error));

        Assert.Null(error);
        tree.Flush();
        Assert.True(File.Exists(Path.Combine(_out, "Data", "Good.json")));
    }

    /// <summary>
    /// 追加模式：`--out` 已存在且非空时**不报错、也不重新拷树**，直接在它上面续写。
    /// 给「多个包依次进同一棵树」用 —— 链式的正确形态是同一棵树上依次应用补丁，
    /// 第 2 个包起再拷一遍原版树（600 MB 上下）纯属白做工。
    /// </summary>
    [Fact]
    public void 追加模式下不重拷已有树()
    {
        Touch("Data/A.json", "源里的");
        var first = _tree = new OutputTree(_src, _out);
        Assert.True(first.TryWriteText("Data/A.json", "第一个包改过的", out _));
        first.Flush();

        // 源树在两次之间变了 —— 追加模式**不该**把它重拷进输出（那会把第一个包的成果冲掉）
        File.WriteAllText(Path.Combine(_src, "Data", "A.json"), "源里改过了");
        Touch("Data/B.json", "源里新增的");

        var second = _tree = new OutputTree(_src, _out);
        Assert.True(second.Append);

        Assert.Equal("第一个包改过的", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
        // 没有重拷 ⇒ 源里新增的 B 不会自己冒出来
        Assert.False(File.Exists(Path.Combine(_out, "Data", "B.json")));
        // InputFiles 登记的是**已有树**，不是源树
        Assert.Contains("Data/A.json", second.InputFiles);
        Assert.DoesNotContain("Data/B.json", second.InputFiles);
    }

    [Fact]
    public void 输出目录不存在时按建树处理()
    {
        Touch("Data/A.json", "源里的");
        var tree = _tree = new OutputTree(_src, _out);

        Assert.False(tree.Append);
        Assert.Equal("源里的", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
        Assert.Contains("Data/A.json", tree.InputFiles);
    }

    // ---- 延迟写（deferWrites）：所有补丁先在内存里算完，最后一次性落盘 ----
    // 两个作用：① 图片/地图不再每个补丁拷一遍；② 判定用的 ResourceTree 读磁盘，
    // 延迟写让它看不到同一次运行里刚写的（与「判定读输入树、写另一棵树」的旧行为一致）。

    [Fact]
    public void 延迟写模式下写只进内存直到落盘()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        tree.WriteText("Data/A.json", "改过");
        // 还没落盘 ⇒ 磁盘上仍是原版
        Assert.Equal("原版", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
        // 但同一运行内读得到（后续补丁要能看到前面写的）
        Assert.Equal("改过", tree.ReadTextOrNull("Data/A.json"));
        Assert.True(tree.Exists("Data/A.json"));

        tree.Flush();
        Assert.Equal("改过", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
        Assert.Contains("Data/A.json", tree.FlushedFiles);
    }

    [Fact]
    public void 延迟写模式下后写的覆盖先写的()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        tree.WriteText("Data/A.json", "第一次");
        tree.WriteText("Data/A.json", "第二次");
        tree.Flush();

        Assert.Equal("第二次", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
    }

    /// <summary>
    /// 产出文件系统是 Windows，**大小写不同是同一个文件** —— 所以暂存必须按大小写不敏感合并，
    /// 「谁能赢」得由**写入顺序**定，而不是由 <c>Flush()</c> 的字典序定。
    /// <para>
    /// 实测：SVE 的一条 <c>Load</c> 把同一份对话按三种大小写写了 10 次，用序号比较会记成
    /// 三个槽位 ⇒ 137 键的那份被 4 键的那份盖掉，100 个文件对不上旧基线。
    /// </para>
    /// </summary>
    [Fact]
    public void 大小写不同的路径算同一个文件且后写的赢()
    {
        Touch("Characters/Dialogue/Andy.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        tree.WriteText("characters/dialogue/andy.json", "小写写的");
        tree.WriteText("characters/dialogue/Andy.json", "混合写的");
        tree.WriteText("Characters/Dialogue/Andy.json", "最后写的");   // 后写 ⇒ 必须赢

        tree.Flush();
        Assert.Equal("最后写的", File.ReadAllText(Path.Combine(_out, "Characters", "Dialogue", "Andy.json")));
        Assert.Single(tree.FlushedFiles);      // 只算落了一个文件
    }

    [Fact]
    public void 延迟写模式下暂存拷贝只留最终来源()
    {
        Touch("Data/A.json", "原版");
        string s1 = Path.Combine(_src, "s1.png");
        string s2 = Path.Combine(_src, "s2.png");
        File.WriteAllText(s1, "图1");
        File.WriteAllText(s2, "图2");

        var tree = _tree = new OutputTree(_src, _out);
        tree.StageCopy("Maps/X.png", s1);
        tree.StageCopy("Maps/X.png", s2);      // 后一个来源胜出（CP 的语义）
        tree.Flush();

        Assert.Equal("图2", File.ReadAllText(Path.Combine(_out, "Maps", "X.png")));
    }

    [Fact]
    public void 延迟写模式下同一个路径改成写文本会顶掉先前的暂存拷贝()
    {
        Touch("Data/A.json", "原版");
        string s1 = Path.Combine(_src, "s1.png");
        File.WriteAllText(s1, "图1");

        var tree = _tree = new OutputTree(_src, _out);
        tree.StageCopy("Maps/X.png", s1);
        tree.WriteText("Maps/X.png", "其实是文本");
        tree.Flush();

        Assert.Equal("其实是文本", File.ReadAllText(Path.Combine(_out, "Maps", "X.png")));
    }

    // ---- 给「缓存层自己落盘」用的三个查询/撤销（ResolveReadablePath / ReadBytesOrNull / DropPending）

    [Fact]
    public void 可读路径在延迟写下指向暂存拷贝的源文件()
    {
        Touch("Maps/Town.tbin", "盘上的旧内容");
        string staged = Path.Combine(_src, "新地图.tbin");
        File.WriteAllText(staged, "暂存的新内容");

        var tree = _tree = new OutputTree(_src, _out);
        tree.StageCopy("Maps/Town.tbin", staged);

        // 直接读盘会拿到旧的 —— 这正是缓存层必须走这个 API 的原因
        Assert.Equal("暂存的新内容", File.ReadAllText(tree.ResolveReadablePath("Maps/Town.tbin")!));
    }

    [Fact]
    public void 可读路径在没暂存时指向磁盘并在缺失时返回空()
    {
        Touch("Maps/Town.tbin", "盘上的");
        var tree = _tree = new OutputTree(_src, _out);

        Assert.Equal("盘上的", File.ReadAllText(tree.ResolveReadablePath("Maps/Town.tbin")!));
        Assert.Null(tree.ResolveReadablePath("Maps/没有这张.tbin"));
    }

    [Fact]
    public void 读字节优先暂存的二进制再落回磁盘()
    {
        Touch("Maps/Town.tbin", "盘上的");
        Touch("Data/A.json", "盘上的");
        var tree = _tree = new OutputTree(_src, _out);

        tree.StageBytes("Maps/Town.tbin", new byte[] { 7, 7, 7 });
        Assert.Equal(new byte[] { 7, 7, 7 }, tree.ReadBytesOrNull("Maps/Town.tbin"));

        tree.StageCopy("Maps/Other.tbin", Path.Combine(_src, "Maps", "Town.tbin"));
        Assert.Equal("盘上的", System.Text.Encoding.UTF8.GetString(tree.ReadBytesOrNull("Maps/Other.tbin")!));

        Assert.Equal("盘上的", System.Text.Encoding.UTF8.GetString(tree.ReadBytesOrNull("Data/A.json")!));
        Assert.Null(tree.ReadBytesOrNull("Data/没有这个.json"));
    }

    // ---- 性能改动钉的钉子：索引化 / 首次写法 / 并行落盘的账本顺序

    /// <summary>
    /// `HasInputFile` 是 O(1) 集合查询 —— 它替掉的是
    /// `InputFiles.Contains(rel, StringComparer.OrdinalIgnoreCase)` 那个**线性扫描**
    /// （`LoadApplier.FindMapConfig` 因此是 O(N²)，把 SVE 的 Load 阶段拖到 49 秒）。
    /// </summary>
    [Fact]
    public void 输入文件查询不区分大小写()
    {
        Touch("Maps/Town.tbin");
        var tree = _tree = new OutputTree(_src, _out);

        Assert.True(tree.HasInputFile("Maps/Town.tbin"));
        Assert.True(tree.HasInputFile("maps/town.TBIN"));
        Assert.False(tree.HasInputFile("Maps/没有这张.tbin"));
    }

    /// <summary>
    /// `ConfigsUnder`（按祖先目录预建索引）必须与「每次前缀扫一遍 InputFiles」**同集合、同顺序**。
    /// 这是选 `.config` 模板的输入 —— 顺序变了就会挑到别的模板（静默换掉 XNB Reader）。
    /// </summary>
    [Fact]
    public void config目录索引与逐次前缀扫描同集合同顺序()
    {
        Touch("Data/A.config");
        Touch("Data/B.json");
        Touch("Data/Events/C.config");
        Touch("Data/Events/Deep/D.config");
        Touch("Characters/E.config");
        Touch("Characters/Dialogue/F.config");
        Touch("Root.config");

        var tree = _tree = new OutputTree(_src, _out);

        string[] prefixes = { "", "Data/", "Data/Events/", "Data/Events/Deep/", "Characters/", "Characters/Dialogue/", "Nope/" };
        foreach (string prefix in prefixes)
        {
            List<string> expected = tree.InputFiles
                .Where(f => f.EndsWith(".config", StringComparison.OrdinalIgnoreCase))
                .Where(f => prefix.Length == 0 || f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Equal(expected, tree.ConfigsUnder(prefix));
        }
    }

    /// <summary>
    /// 产出文件系统是 Windows：往**已存在的文件**里写不会改名字，所以目录项的大小写由
    /// **第一次写它的那次**决定 —— 不能让它被「落盘批次顺序」决定。
    /// <para>
    /// 实测 SVE：先 <c>Load Data/Events/JojaMart</c>（大写）、后有几十条
    /// <c>EditData data/events/jojamart</c>（小写），产出树里该是大写。
    /// </para>
    /// </summary>
    [Fact]
    public void 文件名跟着第一次写它的那次的大小写()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        tree.WriteText("data/events/jojamart.json", "第一次写");         // 小写先来
        tree.WriteText("Data/Events/JojaMart.json", "后来写的");          // 大写后到
        tree.Flush();

        Assert.True(File.Exists(Path.Combine(_out, "data", "events", "jojamart.json")));
        Assert.Equal("后来写的",
            File.ReadAllText(Path.Combine(_out, "data", "events", "jojamart.json")));
    }

    /// <summary>落盘是并行的，但账本顺序必须还是**排序后的顺序**（复现性靠它）。</summary>
    [Fact]
    public void 并行落盘的账本顺序按字典序()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        string[] rels = { "Data/c.json", "Data/a.json", "Data/b.json", "Data/e.json", "Data/d.json" };
        foreach (string rel in rels) tree.WriteText(rel, rel);
        tree.Flush();

        Assert.Equal(new[] { "Data/a.json", "Data/b.json", "Data/c.json", "Data/d.json", "Data/e.json" },
            tree.FlushedFiles);
        foreach (string rel in rels) Assert.True(File.Exists(Path.Combine(_out, rel)));
    }

    // ---- 四种暂存形态（文本 / JSON / 二进制 / 拷贝）必须**互斥**：同一路径只留最后设的那种

    [Fact]
    public void 同一路径先暂存JSON后写文本时文本赢()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        tree.StageJson("Data/A.json", Newtonsoft.Json.Linq.JObject.Parse("""{ "staged": 1 }"""));
        tree.WriteText("Data/A.json", "后写的文本");
        tree.Flush();

        // 少了这条互斥，Flush 里 JSON 排在文本**后面**写，会把文本盖回成 `{ "staged": 1 }`
        Assert.Equal("后写的文本", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
    }

    [Fact]
    public void 同一路径先写文本后暂存JSON时JSON赢()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        tree.WriteText("Data/A.json", "先写的文本");
        tree.StageJson("Data/A.json", Newtonsoft.Json.Linq.JObject.Parse("""{ "staged": 1 }"""));
        tree.Flush();

        Assert.Equal(
            Newtonsoft.Json.Linq.JObject.Parse("""{ "staged": 1 }""")
                .ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r", ""),
            File.ReadAllText(Path.Combine(_out, "Data", "A.json")).Replace("\r", ""));
    }

    // ---- 懒文本（内容等到落盘才生成）：按语言展开的多份变体走它

    /// <summary>
    /// 同一个路径被反复暂存懒文本时，**只有最后一次的生成函数会被调用** ——
    /// 这正是它的用途：大资产被几十条补丁反复改，每条都把 9 个语言各序列化一遍是纯浪费。
    /// </summary>
    [Fact]
    public void 懒文本同一个路径只生成最后那一次()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        int calls = 0;
        tree.StageLazyText("Data/A.json", () => { calls++; return "第一次"; });
        tree.StageLazyText("Data/A.json", () => { calls++; return "第二次"; });
        Assert.Equal(0, calls);     // 没落盘就一次都不该生成

        tree.Flush();
        Assert.Equal(1, calls);
        Assert.Equal("第二次", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
    }

    /// <summary>中途有人读这个路径：就地生成一次、留用给 <c>Flush</c>（别生成两遍）。</summary>
    [Fact]
    public void 懒文本中途被读时就地生成一次并留用()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        int calls = 0;
        tree.StageLazyText("Data/A.json", () => { calls++; return "生成的内容"; });

        Assert.True(tree.Exists("Data/A.json"));
        Assert.Equal("生成的内容", tree.ReadTextOrNull("Data/A.json"));
        tree.Flush();

        Assert.Equal(1, calls);
        Assert.Equal("生成的内容", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
    }

    /// <summary>与另外四种暂存形态互斥：后来居上，先设的懒文本（连生成函数）一起丢掉。</summary>
    [Fact]
    public void 懒文本与其它暂存形态互斥()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        int calls = 0;
        tree.StageLazyText("Data/A.json", () => { calls++; return "懒文本"; });
        tree.WriteText("Data/A.json", "后写的文本");
        tree.Flush();

        Assert.Equal(0, calls);
        Assert.Equal("后写的文本", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));

        tree.StageLazyText("Data/B.json", () => { calls++; return "懒文本"; });
        tree.StageJson("Data/B.json", Newtonsoft.Json.Linq.JObject.Parse("""{ "staged": 1 }"""));
        tree.Flush();

        Assert.Equal(0, calls);
        Assert.Contains("staged", File.ReadAllText(Path.Combine(_out, "Data", "B.json")));
    }

    /// <summary>懒文本也要进落盘账本、也要能被 <see cref="OutputTree.DropPending"/> 撤掉。</summary>
    [Fact]
    public void 懒文本进账本且能被撤销()
    {
        Touch("Data/A.json", "原版");
        var tree = _tree = new OutputTree(_src, _out);

        tree.StageLazyText("Data/A.json", () => "懒文本");
        Assert.Equal(WriteKind.Text, tree.PlannedWrites["Data/A.json"]);

        tree.DropPending("Data/A.json");
        Assert.False(tree.PlannedWrites.ContainsKey("Data/A.json"));

        tree.Flush();   // 撤掉之后落盘不该再动它 ⇒ 磁盘上还是原版
        Assert.Equal("原版", File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
    }

    // ---- 落盘账本：「先汇总要操作的文件、再执行」里那份汇总

    [Fact]
    public void 落盘账本按形态汇总且后写的形态覆盖先写的()
    {
        Touch("Data/A.json");
        var tree = _tree = new OutputTree(_src, _out);

        tree.WriteText("Data/A.json", "{}");
        tree.WriteText("Data/B.json", "{}");
        tree.StageBytes("Maps/X.tbin", new byte[] { 1 });
        tree.StageCopy("Maps/Y.png", Path.Combine(_src, "Data", "A.json"));
        tree.WriteText("Maps/X.tbin", "其实是文本");     // 同一个路径换形态 ⇒ 只记最后那个

        Assert.Equal(Workspace.WriteKind.Text, tree.PlannedWrites["Data/A.json"]);
        Assert.Equal(Workspace.WriteKind.Text, tree.PlannedWrites["Data/B.json"]);
        Assert.Equal(Workspace.WriteKind.Copy, tree.PlannedWrites["Maps/Y.png"]);
        Assert.Equal(Workspace.WriteKind.Text, tree.PlannedWrites["Maps/X.tbin"]);
        Assert.Equal(4, tree.PlannedWrites.Count);
    }

    [Fact]
    public void 撤掉暂存的文件从账本里划掉()
    {
        Touch("Data/A.json");
        var tree = _tree = new OutputTree(_src, _out);

        tree.StageBytes("Maps/X.tbin", new byte[] { 1 });
        tree.NoteWrite("Maps/Y.tbin", Workspace.WriteKind.Map);
        Assert.Equal(2, tree.PlannedWrites.Count);

        // 落盘权交给内存模型 ⇒ 产出树不再写它
        tree.DropPending("Maps/X.tbin");
        Assert.DoesNotContain("Maps/X.tbin", tree.PlannedWrites.Keys);

        // 内存模型自己登记的（没有暂存）不受 DropPending 影响
        Assert.Contains("Maps/Y.tbin", tree.PlannedWrites.Keys);
    }

    // ---- `.config` 特征缓存：选模板时要把整个目录的候选问一遍，不缓存就把它们反复读+解析

    [Fact]
    public void config特征含Reader类型与同名json的形状()
    {
        Touch("Data/A.config",
            """{ "Readers": [ { "Type": "Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.String, mscorlib],[System.String, mscorlib]]" } ] }""");
        Touch("Data/A.json", """{ "K": "V" }""");
        var tree = _tree = new OutputTree(_src, _out);

        OutputTree.ConfigFacts facts = tree.GetConfigFacts("Data/A.config");

        Assert.Contains("DictionaryReader", facts.ReaderType);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Object, facts.JsonRootType);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.String, facts.JsonFirstValueType);
    }

    [Fact]
    public void 同名json被写过之后config特征作废重算()
    {
        Touch("Data/A.config", """{ "Readers": [ { "Type": "R" } ] }""");
        Touch("Data/A.json", """{ "K": "V" }""");
        var tree = _tree = new OutputTree(_src, _out);

        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Object,
            tree.GetConfigFacts("Data/A.config").JsonRootType);

        // 改形状（对象 → 数组）—— 再问必须看到新的，否则会挑错模板
        tree.WriteText("Data/A.json", """[ "x" ]""");

        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Array,
            tree.GetConfigFacts("Data/A.config").JsonRootType);
    }

    [Fact]
    public void 没有同名json时形状为空()
    {
        Touch("Data/A.config", """{ "Readers": [ { "Type": "R" } ] }""");
        var tree = _tree = new OutputTree(_src, _out);

        OutputTree.ConfigFacts facts = tree.GetConfigFacts("Data/A.config");
        Assert.Equal("R", facts.ReaderType);
        Assert.Null(facts.JsonRootType);
        Assert.Null(facts.JsonFirstValueType);
    }

    [Fact]
    public void 流式JSON落盘与ToString逐字节一致()
    {
        Touch("Data/A.json", "原版");
        var token = JObject.Parse("""
        {
          "Name": "测试 / 中文",
          "Number": 1.25,
          "Array": [ 1, 2, { "Nested": true } ],
          "Empty": {},
          "Null": null
        }
        """);
        var tree = _tree = new OutputTree(_src, _out);
        tree.StageJson("Data/A.json", token);
        tree.Flush();

        Assert.Equal(token.ToString(Formatting.Indented),
            File.ReadAllText(Path.Combine(_out, "Data", "A.json")));
    }

    [Fact]
    public void 撤掉暂存后按磁盘内容算()
    {
        Touch("Maps/Town.tbin", "盘上的");
        var tree = _tree = new OutputTree(_src, _out);
        tree.StageBytes("Maps/Town.tbin", new byte[] { 1 });

        Assert.True(tree.Exists("Maps/Town.tbin"));
        tree.DropPending("Maps/Town.tbin");

        Assert.Equal("盘上的", System.Text.Encoding.UTF8.GetString(tree.ReadBytesOrNull("Maps/Town.tbin")!));
        tree.Flush();
        Assert.Equal("盘上的", File.ReadAllText(Path.Combine(_out, "Maps", "Town.tbin")));
        Assert.Empty(tree.FlushedFiles);
    }
}
