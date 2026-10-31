using SdvCpTool.Core.Workspace;

namespace SdvCpTool.Core.Tests;

public class PackTranslationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sdvi18n_" + Guid.NewGuid().ToString("N"));

    public PackTranslationsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void I18n(string fileName, string content)
    {
        string full = Path.Combine(_root, "i18n", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void 没有i18n目录时为空()
    {
        PackTranslations t = PackTranslations.Load(_root);
        Assert.False(t.HasAny);
        Assert.Empty(t.VariantLocales);
    }

    [Fact]
    public void 短码映射成长码后缀()
    {
        I18n("default.json", """{ "a": "A" }""");
        I18n("zh.json", """{ "a": "甲" }""");

        PackTranslations t = PackTranslations.Load(_root);

        Assert.True(t.HasAny);
        Assert.Equal(new[] { "zh-CN" }, t.VariantLocales);
    }

    /// <summary>
    /// 目录布局（<c>i18n/zh/…</c>、<c>i18n/Zh/…</c>）：语言取第一层目录名，同语言多文件合并。
    /// 实测 S&amp;S 与 East Scarp NPCs 只用这种布局；只读顶层会让 2000+ 条文本变成
    /// `(no translation:key)`。
    /// </summary>
    [Fact]
    public void 目录布局的语言与多文件合并()
    {
        I18n("Default/Dialogue.json", """{ "a": "A", "b": "B" }""");
        I18n("Default/Events.json", """{ "c": "C" }""");
        I18n("Zh/Dialogue.json", """{ "a": "甲" }""");
        I18n("Zh/Events.json", """{ "c": "丙" }""");

        PackTranslations t = PackTranslations.Load(_root);

        Assert.True(t.HasAny);
        Assert.Equal(new[] { "zh-CN" }, t.VariantLocales);
        Assert.Equal("甲", t.GetValue("a", "zh-CN"));
        Assert.Equal("B", t.GetValue("b", "zh-CN"));       // zh 缺键 → default 目录
        Assert.Equal("丙", t.GetValue("c", "zh-CN"));
    }

    [Fact]
    public void 顶层文件与目录布局可以共存()
    {
        I18n("default.json", """{ "root": "R" }""");
        I18n("zh/Dialogue.json", """{ "nested": "乙" }""");

        PackTranslations t = PackTranslations.Load(_root);

        Assert.Equal("R", t.GetValue("root", "default"));
        Assert.Equal("乙", t.GetValue("nested", "zh-CN"));
    }

    [Fact]
    public void 只有default时不产后缀变体()
    {
        I18n("default.json", """{ "a": "A" }""");

        PackTranslations t = PackTranslations.Load(_root);
        Assert.True(t.HasAny);
        Assert.Empty(t.VariantLocales);
    }

    [Fact]
    public void 长码文件与短码文件落到同一后缀且去重()
    {
        I18n("default.json", "{}");
        I18n("pt.json", """{ "a": "pt" }""");
        I18n("pt-BR.json", """{ "a": "ptbr" }""");

        PackTranslations t = PackTranslations.Load(_root);
        Assert.Equal(new[] { "pt-BR" }, t.VariantLocales);
    }

    [Fact]
    public void 回退链长码到短码到default()
    {
        I18n("default.json", """{ "only.default": "D", "shared": "D" }""");
        I18n("zh.json", """{ "only.zh": "中", "shared": "中" }""");

        PackTranslations t = PackTranslations.Load(_root);

        Assert.Equal("中", t.GetValue("only.zh", "zh-CN"));
        Assert.Equal("D", t.GetValue("only.default", "zh-CN"));   // 退回 default
        Assert.Equal("中", t.GetValue("shared", "zh-CN"));        // 短码优先于 default
        Assert.Null(t.GetValue("不存在", "zh-CN"));
    }

    [Fact]
    public void 键查找大小写不敏感()
    {
        // SMAPI 的 i18n 键是大小写不敏感的（Translator.SetTranslations 用 OrdinalIgnoreCase）。
        // 真实用例：i18n 存 `June.june_arrival.alissa`，补丁按 {{TargetWithoutPath}} 查出 `Alissa`。
        I18n("default.json", """{ "June.june_arrival.alissa": "Hi" }""");

        PackTranslations t = PackTranslations.Load(_root);

        Assert.Equal("Hi", t.GetValue("June.june_arrival.Alissa", "default"));
        Assert.Equal("Hi", t.GetValue("JUNE.JUNE_ARRIVAL.ALISSA", "default"));
    }

    [Fact]
    public void 带区域的语言先找长码文件()
    {
        I18n("default.json", """{ "a": "D" }""");
        I18n("pt.json", """{ "a": "pt" }""");
        I18n("pt-BR.json", """{ "a": "ptbr" }""");

        PackTranslations t = PackTranslations.Load(_root);
        Assert.Equal("ptbr", t.GetValue("a", "pt-BR"));
    }

    [Fact]
    public void 能解析带注释与尾逗号的jsonc()
    {
        I18n("default.json",
            """
            {
                // 行注释
                "a": "A" /* 块注释 */,
                "b": "B",
            }
            """);

        PackTranslations t = PackTranslations.Load(_root);
        Assert.Equal("A", t.GetValue("a", "default"));
        Assert.Equal("B", t.GetValue("b", "default"));
    }

    [Theory]
    [InlineData("ja", "ja-JP")]
    [InlineData("ru", "ru-RU")]
    [InlineData("zh", "zh-CN")]
    [InlineData("pt", "pt-BR")]
    [InlineData("es", "es-ES")]
    [InlineData("de", "de-DE")]
    [InlineData("th", "th-TH")]
    [InlineData("fr", "fr-FR")]
    [InlineData("ko", "ko-KR")]
    [InlineData("it", "it-IT")]
    [InlineData("tr", "tr-TR")]
    [InlineData("hu", "hu-HU")]
    [InlineData("pt-BR", "pt-BR")]
    public void 语言短码映射长码(string shortCode, string expected) =>
        Assert.Equal(expected, PackTranslations.LanguageCodeString(shortCode));

    [Theory]
    [InlineData("en")]
    [InlineData("mod")]
    [InlineData("xx")]
    public void 没有后缀的语言码返回null(string code) =>
        Assert.Null(PackTranslations.LanguageCodeString(code));
}
