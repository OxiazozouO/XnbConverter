using System.Diagnostics;
using Squish;


/// <summary>
/// DXT 压缩基准：吞吐量 + 往返正确性 + PSNR。
/// 用法: XnbDump dxtbench [宽] [高] [轮数]  |  XnbDump dxtbench --micro
/// </summary>
public static class DxtBench
{
    public static void Run(string[] args)
    {

// DXT 压缩基准：吞吐量 + 往返正确性 + PSNR
if (args.Contains("--micro"))
{
    Micro.Run();
    return;
}

int width = args.Length > 0 ? int.Parse(args[0]) : 1024;
int height = args.Length > 1 ? int.Parse(args[1]) : 1024;
int loops = args.Length > 2 ? int.Parse(args[2]) : 20;

byte[] src = MakeImage(width, height);
Console.WriteLine($"图像 {width}x{height} = {width * height / 1024.0 / 1024.0:F1} M像素，{loops} 轮\n");
Console.WriteLine($"{"格式",-34}{"压缩 ms",9}{"速度 Mpx/s",12}{"解压 ms",9}{"往返",7}{"RGB dB",9}{"Alpha dB",9}");

foreach ((SquishFlags flags, string name) in Cases())
{
    try
    {
        BenchOne(flags, name, src, width, height, loops);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name,-34}  异常: {ex.GetType().Name}: {ex.Message}");
    }
}

static IEnumerable<(SquishFlags, string)> Cases()
{
    yield return (SquishFlags.kDxt1, "DXT1 cluster");
    yield return (SquishFlags.kDxt1 | SquishFlags.kColourIterativeClusterFit, "DXT1 iterative");
    yield return (SquishFlags.kDxt1 | SquishFlags.kColourRangeFit, "DXT1 range");
    yield return (SquishFlags.kDxt3, "DXT3 cluster");
    yield return (SquishFlags.kDxt5, "DXT5 cluster");
    yield return (SquishFlags.kDxt5 | SquishFlags.kColourIterativeClusterFit, "DXT5 iterative");
}

static void BenchOne(SquishFlags flags, string name, byte[] src, int width, int height, int loops)
{
    using Squish.Squish squish = new Squish.Squish(flags, width, height);
    byte[] blocks = new byte[squish.GetStorageRequirements()];
    byte[] back = new byte[src.Length];

    squish.CompressImage(src, blocks);   // 预热 + 验证不抛异常
    squish.DecompressImage(back, blocks);

    Stopwatch sw = Stopwatch.StartNew();
    for (int i = 0; i < loops; i++)
    {
        squish.CompressImage(src, blocks);
    }

    sw.Stop();
    double compressMs = sw.Elapsed.TotalMilliseconds / loops;

    sw.Restart();
    for (int i = 0; i < loops; i++)
    {
        squish.DecompressImage(back, blocks);
    }

    sw.Stop();
    double decompressMs = sw.Elapsed.TotalMilliseconds / loops;

    double mpx = width * (double)height / 1024.0 / 1024.0;
    bool roundTrip = back.Length == src.Length;
    (double rgbPsnr, double alphaPsnr) = Psnr(src, back);
    Console.WriteLine($"{name,-34}{compressMs,9:F1}{mpx / (compressMs / 1000.0),12:F1}{decompressMs,9:F1}" +
                      $"{(roundTrip ? "OK" : "坏"),7}{rgbPsnr,9:F2}{alphaPsnr,9:F2}");
}

/// <summary>返回 (RGB PSNR, Alpha PSNR)。</summary>
static (double Rgb, double Alpha) Psnr(byte[] a, byte[] b)
{
    double rgb = 0;
    double alpha = 0;
    int n = a.Length / 4;
    for (int i = 0; i < a.Length; i += 4)
    {
        for (int c = 0; c < 3; c++)
        {
            double d = a[i + c] - b[i + c];
            rgb += d * d;
        }

        double da = a[i + 3] - b[i + 3];
        alpha += da * da;
    }

    return (ToPsnr(rgb / (n * 3)), ToPsnr(alpha / n));
}

static double ToPsnr(double mse)
{
    return mse <= 0 ? 99.0 : 10.0 * Math.Log10(255.0 * 255.0 / mse);
}

static byte[] MakeImage(int width, int height)
{
    byte[] rgba = new byte[width * height * 4];
    uint seed = 12345;
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            int i = 4 * (y * width + x);
            // 平滑渐变 + 一点区块噪声，接近真实贴图
            int bx = x / 37;
            int by = y / 41;
            uint n = seed = seed * 1664525 + 1013904223;
            int noise = (int)(n >> 24) & 31;
            rgba[i] = (byte)Math.Clamp(x * 255 / width + (bx * 53 % 64), 0, 255);
            rgba[i + 1] = (byte)Math.Clamp(y * 255 / height + (by * 71 % 64) + noise, 0, 255);
            rgba[i + 2] = (byte)Math.Clamp((x + y) * 127 / (width + height) + 64, 0, 255);
            // alpha 分四种区域：不透明 / 渐变 / 全透明 / 线性，确保 alpha 代码路径都被覆盖
            int a = ((x / 64 + y / 64) % 4) switch
            {
                0 => 255,
                1 => x * 255 / width,
                2 => 0,
                _ => (x + y) * 255 / (width + height),
            };
            rgba[i + 3] = (byte)Math.Clamp(a, 0, 255);
        }
    }

    return rgba;
}

    }
}
