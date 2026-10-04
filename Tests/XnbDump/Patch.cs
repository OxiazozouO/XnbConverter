using System.Reflection;
using System.Reflection.PortableExecutable;



/// <summary>
/// 把 CLI 头里的 Requires32Bit 标志清掉再另存。
/// 该标志只约束“运行时可执行位数”，而我们只用反射读结构、不执行游戏代码。
/// </summary>
public static class Patch
{
    private const int CorFlagsOffsetInCliHeader = 16; // cb(4)+major(2)+minor(2)+metadata dir(8)

    public static int Run(string path, string output)
    {
        byte[] bytes = File.ReadAllBytes(path);

        using (MemoryStream stream = new MemoryStream(bytes))
        using (PEReader pe = new PEReader(stream))
        {
            CorHeader cor = pe.PEHeaders.CorHeader!;
            Console.WriteLine($"原始 CorFlags: {cor.Flags}");

            int rva = pe.PEHeaders.PEHeader!.CorHeaderTableDirectory.RelativeVirtualAddress;
            int fileOffset = RvaToOffset(pe, rva);
            if (fileOffset < 0)
            {
                Console.WriteLine("  找不到 CLI 头的文件偏移");
                return 1;
            }

            int flagsOffset = fileOffset + CorFlagsOffsetInCliHeader;
            uint flags = BitConverter.ToUInt32(bytes, flagsOffset);
            Console.WriteLine($"  CLI 头文件偏移 {fileOffset}，Flags=0x{flags:X8}");

            const uint Requires32Bit = 0x00000002;
            if ((flags & Requires32Bit) == 0)
            {
                Console.WriteLine("  本来就不是 32 位限定");
            }

            BitConverter.GetBytes(flags & ~Requires32Bit).CopyTo(bytes, flagsOffset);
        }

        File.WriteAllBytes(output, bytes);
        Console.WriteLine($"  已写出 {output}");

        // 直接试加载
        try
        {
            Assembly asm = Assembly.Load(bytes);
            Console.WriteLine($"  加载成功: {asm.FullName}");
            Type[] types = asm.GetExportedTypes();
            Console.WriteLine($"  导出类型 {types.Length} 个");
            foreach (Type t in types)
            {
                if (t.Name.Contains("SpriteFont"))
                {
                    Console.WriteLine($"    {t.FullName}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  加载失败: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    内部: {ex.InnerException?.Message}");
            return 1;
        }
    }

    private static int RvaToOffset(PEReader pe, int rva)
    {
        foreach (SectionHeader section in pe.PEHeaders.SectionHeaders)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.VirtualSize)
            {
                return rva - section.VirtualAddress + section.PointerToRawData;
            }
        }

        return -1;
    }
}
