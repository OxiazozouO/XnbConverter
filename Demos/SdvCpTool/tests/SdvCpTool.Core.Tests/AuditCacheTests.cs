using SdvCpTool.Core.Reporting;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 全树自检的**增量缓存**。链式跑语料时每个包是一个独立进程，进程内的解析缓存帮不到跨包 ——
/// 每个包仍要把整棵树的 JSON 重新解析一遍（实测占整轮 60%），靠它避免重算。
///
/// <para>
/// **新鲜度由调用方说了算**：产出阶段本来就知道「这一轮写了哪些文件」（落盘账本），
/// 所以自检只要「重算这几份、其余沿用上次结论」—— 不碰文件系统、也不靠修改时间猜。
/// 这条契约必须钉死：谁要是拿缓存去回答「改过的文件」，结论就是错的。
/// </para>
/// </summary>
public class AuditCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvac_" + Guid.NewGuid().ToString("N"));

    public AuditCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CachePath => Path.Combine(_dir, "cache.json");

    [Fact]
    public void 存下来再读回去结论不变()
    {
        var cache = AuditCache.Empty();
        cache.Put("condition", "Data/A.json", new AuditCache.Entry(Skipped: false, Checked: true,
            new List<string[]> { new[] { "Data/A", "$.X", "Condition", "…" } }));
        cache.Put("condition", "Data/B.json", new AuditCache.Entry(Skipped: true, Checked: false,
            new List<string[]>()));
        cache.Put("gamedata", "Data/A.json", new AuditCache.Entry(false, true,
            new List<string[]>(), Counts: new[] { 3, 5 }));
        cache.Save(CachePath, @"D:\out", @"D:\in");

        AuditCache back = AuditCache.Load(CachePath, @"D:\out", @"D:\in");

        Assert.True(back.TryGet("condition", "Data/A.json", out AuditCache.Entry hit));
        Assert.True(hit.Checked);
        Assert.False(hit.Skipped);
        Assert.Equal(new[] { "Data/A", "$.X", "Condition", "…" }, Assert.Single(hit.Findings));

        Assert.True(back.TryGet("condition", "Data/B.json", out AuditCache.Entry skipped));
        Assert.True(skipped.Skipped);

        Assert.True(back.TryGet("gamedata", "Data/A.json", out AuditCache.Entry gd));
        Assert.Equal(new[] { 3, 5 }, gd.Counts);

        Assert.False(back.TryGet("shape", "Data/A.json", out _));      // 各道自检各记各的
        Assert.False(back.TryGet("condition", "Data/没有这个.json", out _));
    }

    /// <summary>
    /// 缓存与**那一轮的那棵树**绑定：产出根或原版根对不上就整份作废（否则会把别的树的老结论套上来）。
    /// </summary>
    [Fact]
    public void 换了一棵树缓存就作废()
    {
        var cache = AuditCache.Empty();
        cache.Put("condition", "Data/A.json", new AuditCache.Entry(false, true, new List<string[]>()));
        cache.Save(CachePath, @"D:\out", @"D:\in");

        Assert.False(AuditCache.Load(CachePath, @"D:\别的输出", @"D:\in")
            .TryGet("condition", "Data/A.json", out _));
        Assert.False(AuditCache.Load(CachePath, @"D:\out", @"D:\别的原版")
            .TryGet("condition", "Data/A.json", out _));
    }

    [Fact]
    public void 缓存文件不在或读坏了都当空缓存()
    {
        Assert.False(AuditCache.Load(null, "o", "i").TryGet("condition", "X", out _));
        Assert.False(AuditCache.Load(Path.Combine(_dir, "不存在.json"), "o", "i")
            .TryGet("condition", "X", out _));

        string broken = Path.Combine(_dir, "broken.json");
        File.WriteAllText(broken, "{ 这不是 JSON");
        Assert.False(AuditCache.Load(broken, "o", "i").TryGet("condition", "X", out _));

        // 坏缓存不该让写回也炸
        AuditCache.Load(broken, "o", "i").Save(Path.Combine(_dir, "out.json"), "o", "i");
        Assert.True(File.Exists(Path.Combine(_dir, "out.json")));
    }

    /// <summary>自检要重算的集合 = 这一轮写过的 json（按字典序，好让结论顺序稳定）。</summary>
    [Fact]
    public void 重算集合只含这一轮写过的json()
    {
        string src = Path.Combine(_dir, "in");
        string dst = Path.Combine(_dir, "out");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "A.json"), "{}");
        var tree = new Workspace.OutputTree(src, dst);

        tree.WriteText("Data/Two.json", "{}");
        tree.WriteText("Data/One.config", "{}");     // .config 不是 json 资产
        tree.StageJson("Data/Three.json", Newtonsoft.Json.Linq.JObject.Parse("{}"));
        tree.WriteText("Characters/Dialogue/A.json", "{}");

        Assert.Equal(new[] { "Characters/Dialogue/A.json", "Data/Three.json", "Data/Two.json" },
            AuditCache.JsonWrittenBy(tree));
    }
}
