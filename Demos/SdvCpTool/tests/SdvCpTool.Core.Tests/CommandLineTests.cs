using SdvCpTool.Cli;

namespace SdvCpTool.Core.Tests;

public class CommandLineTests
{
    [Fact]
    public void 解析全部参数()
    {
        var o = CommandLine.Parse(new[]
        {
            "convert", "--pack", "P", "--tree", "T", "--out", "O",
            "--report", "R.json"
        })!;

        Assert.Equal("P", o.Pack);
        Assert.Equal("T", o.Tree);
        Assert.Equal("O", o.Out);
        Assert.Equal("R.json", o.Report);
    }

    [Fact]
    public void 缺少pack时返回null()
    {
        Assert.Null(CommandLine.Parse(new[] { "convert", "--tree", "T", "--out", "O" }));
    }

    [Fact]
    public void 缺少tree时返回null()
    {
        Assert.Null(CommandLine.Parse(new[] { "convert", "--pack", "P", "--out", "O" }));
    }

    [Fact]
    public void 未知子命令返回null()
    {
        Assert.Null(CommandLine.Parse(new[] { "frobnicate" }));
    }

    [Fact]
    public void 空参数返回null()
    {
        Assert.Null(CommandLine.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void 默认报告写到资源树外()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "merged_out");
        string packDir = Path.Combine(Path.GetTempPath(), "MyPack");

        var o = CommandLine.Parse(new[]
        {
            "convert", "--pack", packDir, "--tree", "T", "--out", outDir
        })!;

        Assert.Equal(Path.Combine(Path.GetTempPath(), "MyPack.report.json"), o.Report);
        Assert.False(Path.GetFullPath(o.Report!)
            .StartsWith(Path.GetFullPath(outDir) + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void 解析contentHashes参数()
    {
        var o = CommandLine.Parse(new[]
        {
            "convert", "--pack", "P", "--tree", "T", "--out", "O",
            "--content-hashes", "H.json"
        })!;

        Assert.Equal("H.json", o.ContentHashes);
    }

    [Fact]
    public void 解析mods参数()
    {
        var o = CommandLine.Parse(new[]
        {
            "convert", "--pack", "P", "--tree", "T", "--out", "O",
            "--mods", "M"
        })!;

        Assert.Equal("M", o.Mods);
    }

    /// <summary>以前那些开关（`--append` / `--dry-run` / `--no-audit` / `--audit-cache` / `--mod-config`）
    /// 与 `translate` 子命令都撤了 —— 再给出来必须**判非法**，别让老脚本"看着跑成功"。</summary>
    [Theory]
    [InlineData("--append")]
    [InlineData("--dry-run")]
    [InlineData("--no-audit")]
    [InlineData("--mod-config")]
    public void 撤掉的开关会被判非法(string flag) =>
        Assert.Null(CommandLine.Parse(new[] { "convert", "--pack", "P", "--tree", "T", "--out", "O", flag }));

    [Fact]
    public void translate子命令撤掉了() =>
        Assert.Null(CommandLine.Parse(new[] { "translate", "--pack", "P" }));
}
