using SdvCpTool.Core.Cp;
using SdvCpTool.Core.Reporting;

namespace SdvCpTool.Core.Tests;

/// <summary>
/// 「条件键是**包内令牌**」这一档的自检：我们的 `When` 求值器不认（CP 认）⇒ 那些条件一律当作
/// 不成立。它必须**报出来**，不能只是"某条补丁安静地没生效"。
/// </summary>
public class TokenConditionAuditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdvtca_" + Guid.NewGuid().ToString("N"));

    public TokenConditionAuditTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void Write(string rel, string content)
    {
        string full = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void 条件键是包内令牌时报出来()
    {
        Write("content.json", """
            {
              "Format": "2.0.0",
              "DynamicTokens": [ { "Name": "SeasonalClothes", "Value": "Vanilla" } ],
              "Changes": [
                { "Action": "EditImage", "Target": "Characters/Farmer/farmer_base",
                  "FromFile": "assets/a.png", "When": { "SeasonalClothes": "Cold" } },
                { "Action": "EditImage", "Target": "Characters/Farmer/farmer_girl_base",
                  "FromFile": "assets/b.png", "When": { "Season": "Spring" } },
                { "Action": "EditImage", "Target": "Characters/Other",
                  "FromFile": "assets/c.png", "When": { "HasMod": "Some.Mod" } }
              ]
            }
            """);

        TokenConditionAudit.Audit a = TokenConditionAudit.Run(ContentPack.Load(_dir));

        Assert.Equal(3, a.CheckedPatches);           // 三条都带条件
        Assert.Equal(1, a.ConditionalOnTokens);      // 只有第一条的键是包内令牌
        TokenConditionAudit.Finding f = Assert.Single(a.Findings);
        Assert.Equal("content.json#0", f.Patch);
        Assert.Equal("SeasonalClothes", f.Key);
        Assert.Equal(0, a.CheckedTokenBlocks);       // 这个包的令牌没有条件块
    }

    /// <summary>
    /// ⚠ **令牌定义里的条件块也要看** —— `{{pan}}` 那种卡的就是这里（`{"recolour": "Vanilla"}`
    /// 写在令牌定义里，不在补丁上；只看补丁会误报成 0，实测 DaisyNiko 就是 0）。
    /// </summary>
    [Fact]
    public void 令牌块的条件键是另一个令牌时也报()
    {
        Write("content.json", """
            {
              "Format": "2.0.0",
              "DynamicTokens": [
                { "Name": "recolour", "Value": "Vanilla" },
                { "Name": "pan", "Value": "vanilla", "When": { "recolour": "Vanilla" } }
              ],
              "Changes": []
            }
            """);

        TokenConditionAudit.Audit a = TokenConditionAudit.Run(ContentPack.Load(_dir));

        Assert.Equal(1, a.CheckedTokenBlocks);
        Assert.Equal(1, a.TokenBlocksConditionalOnTokens);
        TokenConditionAudit.Finding f = Assert.Single(a.Findings);
        Assert.Equal("DynamicTokens[pan]", f.Patch);
        Assert.Equal("recolour", f.Key);
    }

    [Fact]
    public void 没有令牌时什么都不报()
    {
        Write("content.json", """
            {
              "Format": "2.0.0",
              "Changes": [
                { "Action": "EditImage", "Target": "Characters/Other",
                  "FromFile": "assets/c.png", "When": { "Season": "Spring" } }
              ]
            }
            """);

        TokenConditionAudit.Audit a = TokenConditionAudit.Run(ContentPack.Load(_dir));

        Assert.Equal(0, a.ConditionalOnTokens);
        Assert.Empty(a.Findings);
    }
}
