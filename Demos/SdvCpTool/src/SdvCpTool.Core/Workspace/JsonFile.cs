using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SdvCpTool.Core.Workspace;

/// <summary>
/// 统一 JSON 落盘入口：用 <see cref="JsonTextWriter"/> 直接写文件，不先生成整份字符串。
/// <para>
/// 大文档（<c>Data/Objects</c>、SVE 报告）走 <c>ToString(Formatting.Indented)</c> 会多一次
/// 几十 MB 的字符串分配与 UTF-8 编码。缓冲区取 64 KB：4 KB 时小文件建流/Flush 次数偏多，
/// 64 KB 在全量上更稳。输出字节与 <c>ToString(Formatting.Indented)</c> 一致（有单测）。
/// </para>
/// </summary>
public static class JsonFile
{
    public static void Write(string path, JToken token)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None, 1 << 16);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 1 << 16,
            leaveOpen: true);
        using var json = new JsonTextWriter(writer) { Formatting = Formatting.Indented };
        token.WriteTo(json);
    }
}
