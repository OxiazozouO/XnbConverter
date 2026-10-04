using System.Text;
using Squish;

/// <summary>
/// 单元测试：拿一个 16 字节的 DXT 块，看 libsquish 解出来 / 再压回去结果如何。
/// 用法: dxt 5 "55 55 55 55 00 00 00 00 00 00 00 a0 ff ff 00 00"
/// 参数是 SurfaceFormat：4=Dxt1 5=Dxt3 6=Dxt5
/// </summary>
public static class DxtProbe
{
    public static void Run(int format, string blockHex)
    {
        byte[] block = blockHex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(h => Convert.ToByte(h, 16)).ToArray();
        if (block.Length != 16)
        {
            Console.WriteLine($"块长度应为 16，实际 {block.Length}");
            return;
        }

        SquishFlags flags = format switch
        {
            4 => SquishFlags.kDxt1,
            5 => SquishFlags.kDxt3,
            6 => SquishFlags.kDxt5,
            _ => throw new ArgumentException($"不支持的格式 {format}")
        };

        // 4x4 像素
        byte[] rgba = new byte[4 * 4 * 4];
        global::Squish.Squish squish = new global::Squish.Squish(flags, 4, 4);
        squish.DecompressImage(rgba, block);
        Console.WriteLine("解压结果 (RGBA):");
        for (int y = 0; y < 4; y++)
        {
            StringBuilder sb = new StringBuilder("  ");
            for (int x = 0; x < 4; x++)
            {
                int i = (y * 4 + x) * 4;
                sb.Append($"[{rgba[i],3},{rgba[i + 1],3},{rgba[i + 2],3},{rgba[i + 3],3}] ");
            }

            Console.WriteLine(sb.ToString());
        }

        // 再压回去
        byte[] outBlock = new byte[squish.GetStorageRequirements()];
        squish.CompressImage(rgba, outBlock);
        Console.WriteLine($"原块 : {BitConverter.ToString(block).Replace("-", " ").ToLowerInvariant()}");
        Console.WriteLine($"重压缩: {BitConverter.ToString(outBlock).Replace("-", " ").ToLowerInvariant()}");
        Console.WriteLine($"是否一致: {block.AsSpan().SequenceEqual(outBlock)}");
        squish.Dispose();
    }
}
