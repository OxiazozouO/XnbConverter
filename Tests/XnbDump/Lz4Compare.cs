using System.Reflection;
using LZ4PCL;

/// <summary>
/// 直接对比两套 LZ4 编码器：NuGet 的 LZ4PCL vs Core 里 vendored 的 lz4net。
/// 判据是「能不能逐字节重现 XNB 里原有的压缩流」—— 那正是游戏管线压出来的。
/// vendored 版是 internal，用反射调。
/// </summary>
public static class Lz4Compare
{
    public static void Run(string root)
    {
        MethodInfo? vendoredEncode = null;
        MethodInfo? vendoredMax = null;
        MethodInfo? vendoredDecode = null;
        Type? vendoredType = null;
        foreach (Assembly asm in new[] { Assembly.Load("XnbConverter.Core") }
                     .Concat(AppDomain.CurrentDomain.GetAssemblies()))
        {
            Type? t = asm.GetType("XnbConverter.Utilities.LZ4.LZ4Codec");
            vendoredType ??= t;
            if (t == null)
            {
                continue;
            }

            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (m.Name == "Encode32HC" && m.GetParameters().Length == 6)
                {
                    vendoredEncode = m;
                }
                else if (m.Name == "MaximumOutputLength" && m.GetParameters().Length == 1)
                {
                    vendoredMax = m;
                }
                else if (m.Name == "Decode32" && m.GetParameters().Length == 7)
                {
                    vendoredDecode = m;
                }
            }
        }

        if (vendoredEncode == null || vendoredMax == null)
        {
            Console.WriteLine("找不到 vendored 的 LZ4Codec（Core 未加载？）");
            return;
        }

        Console.WriteLine($"vendored: {vendoredType?.Assembly.GetName().Name}");

        int samePcl = 0, sameVendored = 0, sameDecode = 0, diff = 0, total = 0;
        foreach (string path in Directory.EnumerateFiles(root, "*.xnb", SearchOption.AllDirectories))
        {
            byte[] file = File.ReadAllBytes(path);
            if (file.Length < 14 || (file[5] & 0x40) == 0)
            {
                continue; // 只看 LZ4 的
            }

            uint fileSize = BitConverter.ToUInt32(file, 6);
            uint unpacked = BitConverter.ToUInt32(file, 10);
            byte[] compressed = file[14..(int)fileSize];

            // 解压
            byte[] payload = new byte[(int)unpacked];
            LZ4Codec.Decode(compressed, 0, compressed.Length, payload, 0, (int)unpacked);

            total++;

            // vendored 解码，和 LZ4PCL 的结果比
            byte[] payload2 = new byte[(int)unpacked];
            vendoredDecode!.Invoke(null, new object[] { compressed, 0, compressed.Length, payload2, 0, (int)unpacked, true });
            if (payload2.AsSpan().SequenceEqual(payload))
            {
                sameDecode++;
            }

            // LZ4PCL 重压
            byte[] a = new byte[LZ4Codec.MaximumOutputLength((int)unpacked)];
            int la = LZ4Codec.Encode32HC(payload, 0, payload.Length, a, 0, a.Length);
            if (la == compressed.Length && a.AsSpan(0, la).SequenceEqual(compressed))
            {
                samePcl++;
            }

            // vendored 重压
            byte[] b = new byte[(int)vendoredMax.Invoke(null, new object[] { payload.Length })!];
            int lb = (int)vendoredEncode.Invoke(null, new object[] { payload, 0, payload.Length, b, 0, b.Length })!;
            if (lb == compressed.Length && b.AsSpan(0, lb).SequenceEqual(compressed))
            {
                sameVendored++;
            }
            else if (diff < 3)
            {
                diff++;
                Console.WriteLine($"  差异 {Path.GetFileName(path)}: 原={compressed.Length} PCL={la} vendored={lb}");
            }
        }

        Console.WriteLine($"\nLZ4 文件 {total} 个");
        Console.WriteLine($"  解码结果与 LZ4PCL 一致: {sameDecode}");
        Console.WriteLine($"  用 LZ4PCL  重压逐字节重现原文件: {samePcl}");
        Console.WriteLine($"  用 vendored 重压逐字节重现原文件: {sameVendored}");
    }
}
