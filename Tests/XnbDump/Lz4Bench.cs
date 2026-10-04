using System.Diagnostics;
using System.Reflection;
using LZ4PCL;

/// <summary>
/// LZ4 HC 编码器的同进程对撞：vendored（改过）vs LZ4PCL（未改的同一份 lz4net）。
/// 同一进程、同一份数据、交替测量，能避开机器噪声；同时校验两边输出逐字节相同。
/// </summary>
public static class Lz4Bench
{
    public static void Run(string root, int rounds)
    {
        // vendored 是 internal，反射拿
        MethodInfo? vendoredEncode = null;
        MethodInfo? vendoredMax = null;
        foreach (Assembly asm in new[] { Assembly.Load("XnbConverter.Core") }
                     .Concat(AppDomain.CurrentDomain.GetAssemblies()))
        {
            Type? t = asm.GetType("XnbConverter.Utilities.LZ4.LZ4Codec");
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
            }
        }

        if (vendoredEncode == null || vendoredMax == null)
        {
            Console.WriteLine("找不到 vendored LZ4Codec");
            return;
        }

        // 收集真实载荷：解压若干 XNB 的负载当输入
        List<byte[]> payloads = new();
        foreach (string path in Directory.EnumerateFiles(root, "*.xnb", SearchOption.AllDirectories))
        {
            if (payloads.Count >= 40)
            {
                break;
            }

            byte[] file = File.ReadAllBytes(path);
            if (file.Length < 14 || (file[5] & 0x40) == 0)
            {
                continue;
            }

            uint fileSize = BitConverter.ToUInt32(file, 6);
            uint unpacked = BitConverter.ToUInt32(file, 10);
            byte[] compressed = file[14..(int)fileSize];
            byte[] payload = new byte[(int)unpacked];
            LZ4Codec.Decode(compressed, 0, compressed.Length, payload, 0, (int)unpacked);
            payloads.Add(payload);
        }

        long bytes = payloads.Sum(p => (long)p.Length);
        Console.WriteLine($"{payloads.Count} 份载荷，共 {bytes / 1048576.0:F1} MB，轮次 {rounds}");

        byte[] buffer = new byte[LZ4Codec.MaximumOutputLength(payloads.Max(p => p.Length))];
        byte[] vendoredBuffer = new byte[buffer.Length];

        // 等价性先确认，不然比速度没意义
        int mismatch = 0;
        foreach (byte[] p in payloads)
        {
            int a = LZ4Codec.Encode32HC(p, 0, p.Length, buffer, 0, buffer.Length);
            int b = (int)vendoredEncode.Invoke(null, new object[] { p, 0, p.Length, vendoredBuffer, 0, vendoredBuffer.Length })!;
            if (a != b || !buffer.AsSpan(0, a).SequenceEqual(vendoredBuffer.AsSpan(0, b)))
            {
                mismatch++;
            }
        }

        Console.WriteLine($"输出逐字节不同: {mismatch} / {payloads.Count}");

        // 预热
        foreach (byte[] p in payloads.Take(3))
        {
            LZ4Codec.Encode32HC(p, 0, p.Length, buffer, 0, buffer.Length);
            vendoredEncode.Invoke(null, new object[] { p, 0, p.Length, vendoredBuffer, 0, vendoredBuffer.Length });
        }

        // 三列：LZ4PCL、LZ4PCL 再测一遍（噪声底）、vendored。
        // 每轮交替进行、取各列最小值 —— 最小值对抗"降频/抢占"这类外部干扰最稳。
        double pclBest = double.MaxValue, pcl2Best = double.MaxValue, venBest = double.MaxValue;
        for (int r = 0; r < rounds; r++)
        {
            pclBest = Math.Min(pclBest, Time(() =>
            {
                foreach (byte[] p in payloads)
                {
                    LZ4Codec.Encode32HC(p, 0, p.Length, buffer, 0, buffer.Length);
                }
            }));

            pcl2Best = Math.Min(pcl2Best, Time(() =>
            {
                foreach (byte[] p in payloads)
                {
                    LZ4Codec.Encode32HC(p, 0, p.Length, buffer, 0, buffer.Length);
                }
            }));

            venBest = Math.Min(venBest, Time(() =>
            {
                foreach (byte[] p in payloads)
                {
                    vendoredEncode.Invoke(null, new object[] { p, 0, p.Length, vendoredBuffer, 0, vendoredBuffer.Length });
                }
            }));
        }

        double mb = bytes / 1048576.0;
        Console.WriteLine();
        Console.WriteLine($"  LZ4PCL  第1列   {pclBest,8:F0} ms");
        Console.WriteLine($"  LZ4PCL  第2列   {pcl2Best,8:F0} ms   ← 同一份代码，噪声底 {Math.Max(pclBest, pcl2Best) / Math.Min(pclBest, pcl2Best):F2}x");
        Console.WriteLine($"  vendored（改）  {venBest,8:F0} ms");
        Console.WriteLine($"  vendored 相对 LZ4PCL：{pclBest / venBest:F2}x");
        Console.WriteLine($"  信号 / 噪声底：{(pclBest / venBest) / (Math.Max(pclBest, pcl2Best) / Math.Min(pclBest, pcl2Best)):F2}");
    }

    private static double Time(Action action)
    {
        Stopwatch sw = Stopwatch.StartNew();
        action();
        return sw.Elapsed.TotalMilliseconds;
    }
}
