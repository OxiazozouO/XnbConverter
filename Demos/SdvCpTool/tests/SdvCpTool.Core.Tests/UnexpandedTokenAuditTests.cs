using SdvCpTool.Core.Validating;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 产出自检：写出去的字符串里还留着**没展开的 CP 令牌**。
/// <para>
/// 起因：给 `SpriteSheetValidator` 补上 `Data/Characters` 的 `Appearance[]` 引用检查后，
/// 它报出 <c>Sprite: "Characters/Claire_{{Outfit}}"</c> —— 顺着查才发现这一整类。
/// </para>
/// </summary>
public class UnexpandedTokenAuditTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "sdvtok_" + Guid.NewGuid().ToString("N"));

    public UnexpandedTokenAuditTests() => Directory.CreateDirectory(_out);

    public void Dispose()
    {
        if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
    }

    private void Write(string rel, string json)
    {
        string full = Path.Combine(_out, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    /// <summary>★ `Condition`/`Sprite` 里的令牌**会影响行为** ⇒ 计入 Harmful，单独一档。</summary>
    [Fact]
    public void 条件与贴图里的令牌算严重()
    {
        Write("Data/Characters.json", """
        { "Claire": { "Appearance": [
            { "Id": "{{ModId}}.A", "Condition": "IS_EVENT 4269704, {{ExtraOutfits}}",
              "Sprite": "Characters/Claire_{{Outfit}}" } ] } }
        """);

        var a = UnexpandedTokenAudit.Run(_out);

        Assert.Equal(2, a.Harmful);        // Condition 1 + Sprite 1
        Assert.Equal(1, a.IdentifierOnly);  // Id 里的 {{ModId}} 单列
        Assert.Contains(a.Findings, f => f.Field == "Sprite" && f.TokenName == "Outfit");
        Assert.Contains(a.Findings, f => f.Field == "Condition" && f.TokenName == "ExtraOutfits");
    }

    /// <summary>`Id` 是**不透明标识**，带令牌不影响行为 ⇒ 单独一档，免得淹没真问题。</summary>
    [Fact]
    public void 标识字段里的令牌单独一档()
    {
        Write("Data/Characters.json", """{ "Claire": { "Appearance": [ { "Id": "{{ModId}}.A" } ] } }""");

        var a = UnexpandedTokenAudit.Run(_out);

        Assert.Equal(0, a.Harmful);
        Assert.Equal(1, a.IdentifierOnly);
        Assert.True(Assert.Single(a.Findings).IdentifierOnly);
    }

    /// <summary>`{{i18n:…}}` **不算** —— 那是通道 B1 自己处理的键。</summary>
    [Fact]
    public void i18n令牌不报()
    {
        Write("Data/Objects.json", """{ "a": { "DisplayName": "{{i18n:Some.Key}}" } }""");

        Assert.Empty(UnexpandedTokenAudit.Run(_out).Findings);
    }

    [Fact]
    public void 没有令牌时不报()
    {
        Write("Data/Objects.json", """{ "a": { "DisplayName": "普通文本" } }""");

        var a = UnexpandedTokenAudit.Run(_out);
        Assert.Empty(a.Findings);
        Assert.Equal(0, a.CheckedAssets);   // 一处都没有的资产不算「已核对」
    }

    /// <summary>本地化变体与主资产同形，跳过省一半。</summary>
    [Fact]
    public void 本地化变体跳过()
    {
        Write("Data/Objects.de-DE.json", """{ "a": { "DisplayName": "{{ModId}} x" } }""");

        Assert.Empty(UnexpandedTokenAudit.Run(_out).Findings);
    }
}
