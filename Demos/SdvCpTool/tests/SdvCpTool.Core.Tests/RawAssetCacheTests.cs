using Newtonsoft.Json.Linq;
using SdvCpTool.Core.Merging;
using SdvCpTool.Core.Reporting;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 两个**只读缓存**的契约 —— 它们都为提速而存在，但都靠「调用方只读」这条约定活着，
/// 所以把约定钉在测试里，别让后来的人无意间就地改坏它。
/// </summary>
public class RawAssetCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvrac_" + Guid.NewGuid().ToString("N"));

    public RawAssetCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>
    /// `Get` **返回的就是缓存里那份**（不再深拷贝）—— 调用方 <c>EditDataMerger.Apply</c>
    /// 内部本来就克隆，同一条链上两次深拷贝纯属浪费。谁要改它，请自己克隆。
    /// </summary>
    [Fact]
    public void Get返回缓存里那个实例()
    {
        var cache = new RawAssetCache();
        var data = JObject.Parse("""{ "K": "V" }""");

        cache.Set("Data/A.json", data);

        Assert.Same(data, cache.Get("Data/A.json"));
        Assert.Null(cache.Get("Data/没有这个.json"));
    }

    /// <summary>
    /// 审计解析缓存：同一份文件只解析一次，拿到的是**同一个** token；
    /// 读不出来（不是 JSON / 文件不在）返回 null，与各处的 <c>TryParse</c> 同义。
    /// </summary>
    [Fact]
    public void 审计解析缓存同一份文件只解析一次()
    {
        string file = Path.Combine(_dir, "A.json");
        File.WriteAllText(file, """{ "K": 1 }""");

        var cache = new AuditParseCache();

        JToken? first = cache.Parse(file);
        JToken? second = cache.Parse(file);

        Assert.NotNull(first);
        Assert.Same(first, second);

        File.WriteAllText(file, "{ 这不是 JSON");
        Assert.Same(first, cache.Parse(file));      // 缓存里那份照旧（一轮内文件不再变）
        Assert.Null(cache.Parse(Path.Combine(_dir, "没有这个.json")));
    }
}
