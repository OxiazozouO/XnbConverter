using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using XnbConverter.Utilities.Png;

/// <summary>自研 PNG 编解码 vs ImageSharp 的正面对比（同一批文件、同一批像素）。</summary>
public static class PngBench
{
    public static void Run(string dir)
    {
        List<string> files = Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories)
            .OrderBy(p => p).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine("目录里没有 PNG");
            return;
        }

        string temp = Path.Combine(Path.GetTempPath(), "xnbconv_pngbench");
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
        Directory.CreateDirectory(temp);

        // 先预热 JIT，再正式计时
        foreach (string f in files.Take(Math.Min(8, files.Count)))
        {
            _ = Png.Read(f, out _, out _);
            using Image<Rgba32> warm = Image.Load<Rgba32>(f);
        }

        // ---- 解码 ----
        // 两边都强制把像素摸一遍：确认计时的确是"解完"，而不是惰性解码
        long oursTicks = 0, isharpTicks = 0;
        long bytes = 0;
        long checksum = 0;
        foreach (string f in files)
        {
            Stopwatch sw = Stopwatch.StartNew();
            byte[] px = Png.Read(f, out int w, out int h);
            for (int i = 0; i < px.Length; i += 977)
            {
                checksum += px[i];
            }

            oursTicks += sw.ElapsedTicks;
            bytes += px.Length;

            sw.Restart();
            using (Image<Rgba32> img = Image.Load<Rgba32>(f))
            {
                for (int y = 0; y < img.Height; y += 7)
                {
                    for (int x = 0; x < img.Width; x += 7)
                    {
                        checksum += img[x, y].R;
                    }
                }

                isharpTicks += sw.ElapsedTicks;
            }
        }

        Console.WriteLine($"  (校验和 {checksum})");

        // ---- 编码（输入像素提前备好，计时里只有编码 + 写盘）----
        List<(byte[] Pixels, int W, int H, Image<Rgba32> Image, string Name)> inputs = new();
        foreach (string f in files)
        {
            byte[] px = Png.Read(f, out int w, out int h);
            inputs.Add((px, w, h, Image.Load<Rgba32>(f), Path.GetFileNameWithoutExtension(f)));
        }

        long oursEnc = 0, isharpEnc = 0;
        foreach ((byte[] px, int w, int h, Image<Rgba32> img, string name) in inputs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Png.Write(Path.Combine(temp, name + ".png"), px, w, h);
            oursEnc += sw.ElapsedTicks;

            sw.Restart();
            img.SaveAsPng(Path.Combine(temp, "is_" + name + ".png"));
            isharpEnc += sw.ElapsedTicks;
        }

        foreach ((_, _, _, Image<Rgba32> img, _) in inputs)
        {
            img.Dispose();
        }

        // 再解一遍我们自己写出来的（过滤恒为 None），用来看反过滤占多少
        long ourFileTicks = 0;
        foreach ((_, _, _, _, string name) in inputs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            _ = Png.Read(Path.Combine(temp, name + ".png"), out _, out _);
            ourFileTicks += sw.ElapsedTicks;
        }

        double mb = bytes / 1048576.0;
        double ourSec = oursTicks / (double)Stopwatch.Frequency;
        double ishSec = isharpTicks / (double)Stopwatch.Frequency;
        double ourEncSec = oursEnc / (double)Stopwatch.Frequency;
        double ishEncSec = isharpEnc / (double)Stopwatch.Frequency;

        Console.WriteLine($"{files.Count} 个 PNG，{mb:F1} MB 像素\n");
        Console.WriteLine("解码");
        Console.WriteLine($"  自研      {ourSec,7:F3} s   {mb / ourSec,7:F1} MB/s");
        Console.WriteLine($"  ImageSharp{ishSec,7:F3} s   {mb / ishSec,7:F1} MB/s   自研快 {ishSec / ourSec:F2}x");
        double ourFileSec = ourFileTicks / (double)Stopwatch.Frequency;
        Console.WriteLine($"  自研解自写(过滤全 None) {ourFileSec,7:F3} s   {mb / ourFileSec,7:F1} MB/s");
        Console.WriteLine("编码（含写盘）");
        Console.WriteLine($"  自研      {ourEncSec,7:F3} s   {mb / ourEncSec,7:F1} MB/s");
        Console.WriteLine($"  ImageSharp{ishEncSec,7:F3} s   {mb / ishEncSec,7:F1} MB/s   自研快 {ishEncSec / ourEncSec:F2}x");
    }
}
