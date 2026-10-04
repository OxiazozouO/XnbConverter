using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;



/// <summary>探测给定程序集能否被当前运行时加载并反射出类型。</summary>
public static class Probe
{
    public static int Run(string path)
    {
        Console.WriteLine($"探测: {path}");
        byte[] bytes = File.ReadAllBytes(path);

        DescribePe(bytes);

        try
        {
            Assembly assembly = Assembly.Load(bytes);
            Console.WriteLine($"  [Assembly.Load] 成功: {assembly.FullName}");
            DumpTypes(assembly);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [Assembly.Load] 失败: {ex.GetType().Name}: {ex.Message}");
        }

        return 1;
    }

    private static void DumpTypes(Assembly assembly)
    {
        try
        {
            Type[] types = assembly.GetExportedTypes();
            Console.WriteLine($"    导出类型 {types.Length} 个");
            foreach (Type type in types)
            {
                if (type.Name.Contains("SpriteFont"))
                {
                    Console.WriteLine($"      {type.FullName}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    GetExportedTypes 失败: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DescribePe(byte[] bytes)
    {
        using MemoryStream stream = new MemoryStream(bytes);
        using PEReader pe = new PEReader(stream);
        CorHeader? cor = pe.PEHeaders.CorHeader;
        Console.WriteLine($"  PE: 32位={!pe.PEHeaders.PEHeader!.Magic.ToString().Contains("32Plus")}" +
                          $"  CorFlags={(cor?.Flags.ToString() ?? "无")}" +
                          $"  RuntimeVersion={cor?.MetadataDirectory.Size}x");
        MetadataReader md = pe.GetMetadataReader();
        AssemblyDefinition asm = md.GetAssemblyDefinition();
        Console.WriteLine($"  程序集: {md.GetString(asm.Name)} v{asm.Version}");

        foreach (CustomAttributeHandle handle in asm.GetCustomAttributes())
        {
            CustomAttribute attr = md.GetCustomAttribute(handle);
            if (attr.Constructor.Kind != HandleKind.MemberReference) continue;
            MemberReference ctor = md.GetMemberReference((MemberReferenceHandle)attr.Constructor);
            if (ctor.Parent.Kind != HandleKind.TypeReference) continue;
            string name = md.GetString(md.GetTypeReference((TypeReferenceHandle)ctor.Parent).Name);
            if (name != "TargetFrameworkAttribute") continue;
            BlobReader blob = md.GetBlobReader(attr.Value);
            blob.ReadUInt16();
            Console.WriteLine($"  目标框架: {blob.ReadSerializedString()}");
        }
    }
}
