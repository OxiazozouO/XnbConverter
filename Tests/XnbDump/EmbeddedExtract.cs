using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

/// <summary>
/// 只用 PE/元数据读取器枚举程序集里的嵌入资源，不执行目标程序的任何代码。
/// 用法: XnbDump embedded &lt;assembly&gt; [输出目录]
///       XnbDump embedded --probe &lt;assembly&gt;
///       XnbDump embedded --patch &lt;assembly&gt; &lt;输出目录&gt;
/// </summary>
public static class EmbeddedExtract
{
    public static void Run(string[] args)
    {

// 只用 PE/元数据读取器枚举程序集里的嵌入资源，不执行目标程序集的任何代码。
// 用法: EmbeddedExtract <assembly> [输出目录]
if (args.Length >= 2 && args[0] == "--probe")
{
    Probe.Run(args[1]);
    return;
}

if (args.Length >= 3 && args[0] == "--patch")
{
    Patch.Run(args[1], args[2]);
    return;
}

string path = args[0];
string outDir = args.Length > 1 ? args[1] : "extracted";
Directory.CreateDirectory(outDir);

using FileStream stream = File.OpenRead(path);
using PEReader pe = new PEReader(stream);
MetadataReader md = pe.GetMetadataReader();
CorHeader corHeader = pe.PEHeaders.CorHeader
                      ?? throw new InvalidOperationException("不是托管程序集");

// 嵌入式资源的数据放在 CLI 头指向的 Resources 目录里，每项前 4 字节是长度
PEMemoryBlock block = pe.GetSectionData(corHeader.ResourcesDirectory.RelativeVirtualAddress);

Console.WriteLine($"程序集: {Path.GetFileName(path)}  资源数: {md.ManifestResources.Count}");
Console.WriteLine();

foreach (ManifestResourceHandle handle in md.ManifestResources)
{
    ManifestResource resource = md.GetManifestResource(handle);
    string name = md.GetString(resource.Name);
    if (!resource.Implementation.IsNil)
    {
        Console.WriteLine($"  [外部程序集] {name}（跳过）");
        continue;
    }

    int offset = (int)resource.Offset;
    int length = block.GetReader(offset, 4).ReadInt32();
    byte[] bytes = block.GetContent(offset + 4, length).ToArray();

    bool isPE = bytes.Length > 2 && bytes[0] == 0x4D && bytes[1] == 0x5A;
    int metadataOffset = isPE ? IndexOf(bytes, "BSJB"u8.ToArray()) : -1;
    string fileName = isPE ? LastSegment(name) : name.Replace('.', '_') + ".bin";
    File.WriteAllBytes(Path.Combine(outDir, fileName), bytes);
    Console.WriteLine($"  [提取] {name,-60} {bytes.Length,10} 字节  MZ={isPE}  BSJB@{metadataOffset}");
}

static string LastSegment(string name)
{
    int i = name.LastIndexOf('.');
    int j = name.LastIndexOf('.', Math.Max(i - 1, 0));
    return j < 0 ? name : name[(j + 1)..];
}

static int IndexOf(byte[] haystack, byte[] needle)
{
    for (int i = 0; i + needle.Length <= haystack.Length; i++)
    {
        bool hit = true;
        for (int j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j])
            {
                hit = false;
                break;
            }
        }

        if (hit)
        {
            return i;
        }
    }

    return -1;
}

return;

    }
}
