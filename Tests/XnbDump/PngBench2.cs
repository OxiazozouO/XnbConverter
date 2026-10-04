using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;

/// <summary>把 PNG 解码拆成"取 IDAT + 解压"和其余部分，看时间到底花在哪。</summary>
public static class PngBench2
{
    public static void Run(string dir)
    {
        List<string> files = Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories)
            .OrderBy(p => p).ToList();

        long inflateTicks = 0;
        long compressedBytes = 0;
        long rawBytes = 0;

        // 预热
        foreach (string f in files.Take(8))
        {
            InflateOnly(f, out _, out _);
        }

        foreach (string f in files)
        {
            Stopwatch sw = Stopwatch.StartNew();
            byte[] raw = InflateOnly(f, out int comp, out int rawLen);
            inflateTicks += sw.ElapsedTicks;
            compressedBytes += comp;
            rawBytes += rawLen;
        }

        double sec = inflateTicks / (double)Stopwatch.Frequency;
        Console.WriteLine($"{files.Count} 个文件");
        Console.WriteLine($"  IDAT 合计 {compressedBytes / 1048576.0:F1} MB，解压后 {rawBytes / 1048576.0:F1} MB");
        Console.WriteLine($"  只做 IDAT 解压: {sec:F3} s   按解压后算 {rawBytes / 1048576.0 / sec:F1} MB/s");
    }

    private static byte[] InflateOnly(string path, out int compressedLength, out int rawLength)
    {
        byte[] file = File.ReadAllBytes(path);
        int pos = 8;
        int total = 0;
        while (pos + 8 <= file.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(pos, 4));
            if (file.AsSpan(pos + 4, 4).SequenceEqual("IDAT"u8))
            {
                total += len;
            }
            else if (file.AsSpan(pos + 4, 4).SequenceEqual("IEND"u8))
            {
                break;
            }

            pos += 12 + len;
        }

        byte[] idat = new byte[total];
        pos = 8;
        int at = 0;
        while (pos + 8 <= file.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(pos, 4));
            if (file.AsSpan(pos + 4, 4).SequenceEqual("IDAT"u8))
            {
                file.AsSpan(pos + 8, len).CopyTo(idat.AsSpan(at));
                at += len;
            }
            else if (file.AsSpan(pos + 4, 4).SequenceEqual("IEND"u8))
            {
                break;
            }

            pos += 12 + len;
        }

        compressedLength = total;

        using MemoryStream input = new MemoryStream(idat, writable: false);
        using ZLibStream inflate = new ZLibStream(input, CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream(total * 4 + 4096);
        inflate.CopyTo(output);
        rawLength = (int)output.Length;
        return output.ToArray();
    }
}
