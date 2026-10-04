using System.Text;
using LZ4PCL;

/// <summary>
/// 批量扫描：解压每个 xnb，按 Texture2D 读出头，验证
/// 「宽 = 字段低 16 位、高 = 字段低 16 位」这条规则是否与 dataSize 自洽。
/// </summary>
public static class Scan
{
    private static readonly int[] BytesPerPixel = { 4, 2, 2, 2, 0, 0, 0 }; // SurfaceFormat 0..6

    public static void Run(string root)
    {
        List<string> lines = new List<string>
        {
            "文件,读取器,格式,宽32,高32,宽16,高16,dataSize,低16位自洽,宽是2的幂,高是2的幂"
        };
        int ok = 0, bad = 0, skipped = 0, trimmed = 0;
        foreach (string path in Directory.EnumerateFiles(root, "*.xnb", SearchOption.AllDirectories))
        {
            try
            {
                byte[] file = File.ReadAllBytes(path);
                if (file.Length < 14 || file[0] != 'X' || file[1] != 'N' || file[2] != 'B')
                {
                    skipped++;
                    continue;
                }

                uint fileSize = BitConverter.ToUInt32(file, 6);
                if ((file[5] & 0x40) == 0)
                {
                    skipped++;
                    continue;
                }

                uint unpacked = BitConverter.ToUInt32(file, 10);
                byte[] buf = new byte[14 + (int)unpacked];
                byte[] comp = file[14..(int)fileSize];
                comp.CopyTo(buf, 14);
                LZ4Codec.Decode(comp, 0, comp.Length, buf, 14, (int)unpacked);

                // 读取器清单的前 55 字节不是固定的（名字长度会变），所以按格式找 Texture2D 头：
                // 用 MonoGame 的方式走一遍
                int p = 14;
                int readerCount = Read7Bit(buf, ref p);
                string lastName = "";
                for (int i = 0; i < readerCount; i++)
                {
                    lastName = ReadString(buf, ref p);
                    p += 4;
                }

                if (!lastName.Contains("Texture2DReader"))
                {
                    skipped++;
                    lines.Add($"{Path.GetFileName(path)},{lastName.Split(',')[0]},,,,,,,,,非纹理");
                    continue;
                }

                Read7Bit(buf, ref p);       // 共享资源数
                Read7Bit(buf, ref p);       // 根对象类型索引

                int format = BitConverter.ToInt32(buf, p);
                int w32 = BitConverter.ToInt32(buf, p + 4);
                int h32 = BitConverter.ToInt32(buf, p + 8);
                int mips = BitConverter.ToInt32(buf, p + 12);
                uint dataSize = BitConverter.ToUInt32(buf, p + 16);

                int w16 = BitConverter.ToUInt16(buf, p + 4);
                int h16 = BitConverter.ToUInt16(buf, p + 8);
                int wHi = BitConverter.ToUInt16(buf, p + 6);
                int hHi = BitConverter.ToUInt16(buf, p + 10);

                long expect = format >= 0 && format < BytesPerPixel.Length && BytesPerPixel[format] > 0
                    ? (long)w16 * h16 * BytesPerPixel[format]
                    : -1;

                bool consistent = expect == dataSize;
                if (consistent)
                {
                    ok++;
                }
                else
                {
                    bad++;
                }

                if (wHi != 0 || hHi != 0)
                {
                    trimmed++;
                }

                static bool IsPot(int v) => v > 0 && (v & (v - 1)) == 0;
                lines.Add(string.Join(',',
                    Path.GetFileName(path), "Texture2D", format, w32, h32, w16, h16, dataSize,
                    consistent, IsPot(w16), IsPot(h16)));
            }
            catch (Exception ex)
            {
                skipped++;
                lines.Add($"ERROR,{Path.GetFileName(path)},{ex.GetType().Name}");
            }
        }

        File.WriteAllLines(@"D:\XnbConverter\tmp_render\scan.csv", lines, Encoding.UTF8);
        Console.WriteLine($"纹理文件: 自洽 {ok} / 不自洽 {bad} / 跳过 {skipped}");
        Console.WriteLine($"其中带「内容尺寸」元数据的(高16位非零): {trimmed}");
    }

    /// <summary>逐文件对比两个目录里同名 xnb 解压后的负载是否逐字节相同。</summary>
    public static void Compare(string dirA, string dirB)
    {
        int same = 0, diff = 0, missing = 0;
        foreach (string path in Directory.EnumerateFiles(dirA, "*.xnb", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dirA, path);
            string other = Path.Combine(dirB, rel);
            if (!File.Exists(other))
            {
                missing++;
                continue;
            }

            byte[] a = Payload(path);
            byte[] b = Payload(other);
            if (a.AsSpan().SequenceEqual(b))
            {
                same++;
            }
            else
            {
                diff++;
                int i = 0;
                while (i < Math.Min(a.Length, b.Length) && a[i] == b[i])
                {
                    i++;
                }

                Console.WriteLine($"  不同 {rel}  A={a.Length} B={b.Length} 首个差异偏移={i}");
            }
        }

        Console.WriteLine($"负载逐字节相同: {same}   不同: {diff}   缺失: {missing}");
    }

    private static byte[] Payload(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        uint fileSize = BitConverter.ToUInt32(file, 6);

        if ((file[5] & 0xC0) == 0) // 未压缩：负载从 10 开始
        {
            return file[10..(int)fileSize];
        }

        uint unpacked = BitConverter.ToUInt32(file, 10);

        if ((file[5] & 0x80) != 0) // LZX（XNA/PC）
        {
            using XnbConverter.Readers.BufferReader reader = XnbConverter.Readers.BufferReader.FormXnbFile(path);
            reader.BytePosition = 14;
            XnbConverter.Utilities.Lzx.Decompress(reader, (int)fileSize - 14, (int)unpacked);
            return reader.Buffer[14..(14 + (int)unpacked)];
        }

        byte[] buf = new byte[14 + (int)unpacked];
        byte[] comp = file[14..(int)fileSize];
        comp.CopyTo(buf, 14);
        LZ4Codec.Decode(comp, 0, comp.Length, buf, 14, (int)unpacked);
        return buf[14..(14 + (int)unpacked)];
    }

    private static int Read7Bit(byte[] b, ref int p)
    {
        int v = 0, shift = 0;
        byte c;
        do { c = b[p++]; v |= (c & 0x7F) << shift; shift += 7; } while ((c & 0x80) != 0);
        return v;
    }

    private static string ReadString(byte[] b, ref int p)
    {
        int len = Read7Bit(b, ref p);
        string s = Encoding.UTF8.GetString(b, p, len);
        p += len;
        return s;
    }
}
