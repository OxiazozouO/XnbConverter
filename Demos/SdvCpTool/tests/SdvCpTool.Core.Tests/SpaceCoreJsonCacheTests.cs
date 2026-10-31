using System.Text;
using SdvCpTool.Core.Frameworks;

namespace SdvCpTool.Core.Tests;

public class SpaceCoreJsonCacheTests
{
    [Fact]
    public void 目标跨64K扫描块边界仍能命中()
    {
        string root = Path.Combine(Path.GetTempPath(), "sc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string target = SpaceCoreObjectExtensions.Target;
            // 让目标字符串横跨第一块（64 KiB）的末尾，走 carry 重叠路径。
            int filler = 64 * 1024 - 11 - target.Length / 2;
            File.WriteAllText(Path.Combine(root, "a.json"),
                "{\"filler\":\"" + new string('x', filler) + target + "\"}");
            File.WriteAllText(Path.Combine(root, "b.json"), "{\"a\":1}");

            IReadOnlyList<SpaceCoreJsonCache.CachedFile> files =
                new SpaceCoreJsonCache().GetFiles(root);

            SpaceCoreJsonCache.CachedFile hit = Assert.Single(files);
            Assert.Contains(target, hit.Targets);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UTF16带BOM的文件退回文本扫描()
    {
        string root = Path.Combine(Path.GetTempPath(), "sc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string target = SpaceCoreCropExtensions.Target;
            File.WriteAllText(Path.Combine(root, "utf16.json"),
                "{\"Target\":\"" + target + "\"}", Encoding.Unicode);

            IReadOnlyList<SpaceCoreJsonCache.CachedFile> files =
                new SpaceCoreJsonCache().GetFiles(root);

            SpaceCoreJsonCache.CachedFile hit = Assert.Single(files);
            Assert.Contains(target, hit.Targets);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
