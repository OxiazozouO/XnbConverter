using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using XnbConverter.Utilities.Png;

/// <summary>
/// 自研 PNG 编解码的正确性验证。
///
/// 解码侧：拿 Python 手工造的 22 个变体（颜色类型 0/2/3/4/6、位深 1/2/4/8/16、
/// tRNS、五种过滤、Adam7、多 IDAT），逐个和期望像素比对。
/// 编码侧：写出来的 PNG 再用 ImageSharp（成熟实现）解一遍，像素必须一致。
/// </summary>
public static class PngCheck
{
    /// <summary>真实语料：把目录里每个 PNG 分别用 ImageSharp 和我们的解码器解，逐像素比。</summary>
    public static void RunReal(string dir)
    {
        int same = 0, diff = 0;
        foreach (string png in Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories))
        {
            try
            {
                using Image<Rgba32> reference = Image.Load<Rgba32>(png);
                byte[] ours = Png.Read(png, out int w, out int h);
                if (w != reference.Width || h != reference.Height)
                {
                    diff++;
                    Console.WriteLine($"  尺寸不同 {png}: {w}x{h} vs {reference.Width}x{reference.Height}");
                    continue;
                }

                int at = 0;
                bool ok = true;
                for (int y = 0; y < h && ok; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Rgba32 p = reference[x, y];
                        if (p.R != ours[at] || p.G != ours[at + 1] || p.B != ours[at + 2]
                            || p.A != ours[at + 3])
                        {
                            Console.WriteLine($"  像素不同 {png} ({x},{y})");
                            ok = false;
                            break;
                        }

                        at += 4;
                    }
                }

                if (ok)
                {
                    same++;
                }
                else
                {
                    diff++;
                }
            }
            catch (Exception ex)
            {
                diff++;
                Console.WriteLine($"  异常 {Path.GetFileName(png)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"\n真实 PNG：一致 {same}   不同 {diff}");
    }

    public static void Run(string dir)
    {
        string temp = Path.Combine(dir, "_ours");
        Directory.CreateDirectory(temp);

        int pass = 0, fail = 0;
        foreach (string png in Directory.EnumerateFiles(dir, "*.png").OrderBy(p => p))
        {
            string name = Path.GetFileNameWithoutExtension(png);
            string expectedPath = Path.ChangeExtension(png, ".rgba");
            if (!File.Exists(expectedPath))
            {
                continue;
            }

            byte[] expected = File.ReadAllBytes(expectedPath);
            int ew = (int)BinaryPrimitives.ReadUInt32BigEndian(expected.AsSpan(0, 4));
            int eh = (int)BinaryPrimitives.ReadUInt32BigEndian(expected.AsSpan(4, 8));
            ReadOnlySpan<byte> ePixels = expected.AsSpan(8);

            List<string> problems = new List<string>();

            // --- 解码侧：我们的解码器 vs 参照 ---
            byte[] decoded;
            int w, h;
            try
            {
                decoded = Png.Read(png, out w, out h);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  解码抛异常 {name}: {ex.GetType().Name}: {ex.Message}");
                fail++;
                continue;
            }

            if (w != ew || h != eh)
            {
                problems.Add($"尺寸 {w}x{h} != {ew}x{eh}");
            }
            else if (!decoded.AsSpan().SequenceEqual(ePixels))
            {
                int at = 0;
                while (at < decoded.Length && decoded[at] == ePixels[at])
                {
                    at++;
                }

                problems.Add($"像素首个差异在第 {at} 字节 (像素 {at / 4}, 通道 {at % 4}): "
                    + $"得 {decoded[at]} 期望 {ePixels[at]}");
            }

            // --- 编码侧：我们写，ImageSharp 解 ---
            string ours = Path.Combine(temp, name + ".png");
            try
            {
                Png.Write(ours, ePixels, ew, eh);
                using Image<Rgba32> image = Image.Load<Rgba32>(ours);
                if (image.Width != ew || image.Height != eh)
                {
                    problems.Add($"ImageSharp 读到的尺寸 {image.Width}x{image.Height} != {ew}x{eh}");
                }
                else
                {
                    int at = 0;
                    bool ok = true;
                    for (int y = 0; y < eh && ok; y++)
                    {
                        for (int x = 0; x < ew; x++)
                        {
                            Rgba32 p = image[x, y];
                            if (p.R != ePixels[at] || p.G != ePixels[at + 1]
                                || p.B != ePixels[at + 2] || p.A != ePixels[at + 3])
                            {
                                problems.Add($"ImageSharp 解出的像素 ({x},{y}) 不一致");
                                ok = false;
                                break;
                            }

                            at += 4;
                        }
                    }
                }

                // --- 自写自读 ---
                byte[] back = Png.Read(ours, out int bw, out int bh);
                if (bw != ew || bh != eh || !back.AsSpan().SequenceEqual(ePixels))
                {
                    problems.Add("自写自读不一致");
                }
            }
            catch (Exception ex)
            {
                problems.Add($"编码抛异常: {ex.GetType().Name}: {ex.Message}");
            }

            if (problems.Count == 0)
            {
                pass++;
                Console.WriteLine($"  OK   {name}");
            }
            else
            {
                fail++;
                Console.WriteLine($"  失败 {name}");
                foreach (string p in problems)
                {
                    Console.WriteLine($"       {p}");
                }
            }
        }

        Console.WriteLine($"\n通过 {pass}   失败 {fail}");
    }
}
