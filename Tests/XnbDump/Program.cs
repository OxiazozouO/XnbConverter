using System.Text;
using LZ4PCL;

// 复现 XNB.Decode 的 LZ4 解压，但用「正确大小」的目标缓冲区，
// 用来判断失败到底出在解压还是出在读取器。
// 用法: XnbDump <xnb 路径> [目标缓冲区模式 wrong|right]

string path = args.Length > 0 ? args[0] : @"D:\XnbConverter\Content\LooseSprites\chatBox.xnb";
if (args.Length > 1 && args[0] == "lzxrt")
{
    LzxRoundTrip.Run(args[1..]);
    return;
}

if (args.Length > 1 && args[0] == "embedded")
{
    EmbeddedExtract.Run(args[1..]);
    return;
}

if (args.Length > 1 && args[0] == "dxtbench")
{
    DxtBench.Run(args[1..]);
    return;
}

if (args.Length > 1 && args[0] == "scan")
{
    Scan.Run(args[1]);
    return;
}

if (args.Length > 2 && args[0] == "dxt")
{
    DxtProbe.Run(int.Parse(args[1]), args[2]);
    return;
}

if (args.Length > 2 && args[0] == "cmp")
{
    Scan.Compare(args[1], args[2]);
    return;
}

if (args.Length > 1 && args[0] == "lz4bench")
{
    Lz4Bench.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 5);
    return;
}

if (args.Length > 1 && args[0] == "lz4cmp")
{
    Lz4Compare.Run(args[1]);
    return;
}

if (args.Length > 2 && args[0] == "pngfilt")
{
    PngFilt.Run(args[1], int.Parse(args[2]));
    return;
}

if (args.Length > 2 && args[0] == "pngzlib")
{
    PngFilt.Zlib(args[1], int.Parse(args[2]));
    return;
}

if (args.Length > 1 && args[0] == "pngtrns")
{
    PngFilt.Trns(args[1]);
    return;
}

if (args.Length > 1 && args[0] == "pngprof")
{
    PngFilt.Profile(args[1]);
    return;
}

if (args.Length > 2 && args[0] == "pnglevel")
{
    PngFilt.Levels(args[1], int.Parse(args[2]));
    return;
}

if (args.Length > 2 && args[0] == "pngfilter")
{
    PngFilt.Filters(args[1], int.Parse(args[2]));
    return;
}

if (args.Length > 2 && args[0] == "pngstat")
{
    PngFilt.Stat(args[1], int.Parse(args[2]));
    return;
}

if (args.Length > 2 && args[0] == "pngcmp")
{
    PngFilt.Cmp(args[1], args[2]);
    return;
}

if (args.Length > 1 && args[0] == "pngbench2")
{
    PngBench2.Run(args[1]);
    return;
}

bool wrongMode = args.Length > 1 && args[1] == "wrong";

byte[] file = File.ReadAllBytes(path);
int flags = file[5];
uint fileSize = BitConverter.ToUInt32(file, 6);
uint unpackedSize = BitConverter.ToUInt32(file, 10);

Console.WriteLine($"文件      : {path}");
Console.WriteLine($"flags     : 0x{flags:X2}   文件大小: {fileSize}   解压后大小: {unpackedSize}");

byte[] compressed = file[14..(int)fileSize];
int targetSize = wrongMode ? 14 + (int)fileSize : 14 + (int)unpackedSize;
byte[] buffer = new byte[targetSize];
compressed.CopyTo(buffer, 14);

Console.WriteLine($"目标缓冲区: {targetSize} (模式 {(wrongMode ? "wrong" : "right")})");

int written;
try
{
    written = LZ4Codec.Decode(compressed, 0, compressed.Length, buffer, 14, (int)unpackedSize);
}
catch (Exception ex)
{
    Console.WriteLine($"LZ4 解码抛异常: {ex.GetType().Name}: {ex.Message}");
    return;
}

Console.WriteLine($"LZ4PCL  解码返回: {written} 字节");

// 用 MonoGame 官方算法再解一遍，逐字节对照
byte[] reference = new byte[targetSize];
int refWritten = Lz4Reference.Decode(compressed, 0, compressed.Length, reference, 14, (int)unpackedSize);
Console.WriteLine($"参考实现 解码返回: {refWritten} 字节");

int diffCount = 0;
int firstDiff = -1;
int n = Math.Min(written, refWritten);
for (int i = 0; i < n; i++)
{
    if (buffer[14 + i] != reference[14 + i])
    {
        if (firstDiff < 0)
        {
            firstDiff = i;
        }

        diffCount++;
    }
}

Console.WriteLine($"两实现差异字节数: {diffCount} / {n}    首个差异偏移: {firstDiff}");

// 用 MonoGame 原版流（含 64KB 环形窗口）再解一遍
using (MemoryStream input = new MemoryStream(compressed))
using (XnbDump.Mg.Lz4DecoderStream mg = new XnbDump.Mg.Lz4DecoderStream(input))
{
    byte[] mgBuf = new byte[(int)unpackedSize];
    int total = 0;
    while (total < mgBuf.Length)
    {
        int read = mg.Read(mgBuf, total, mgBuf.Length - total);
        if (read <= 0)
        {
            break;
        }

        total += read;
    }

    Console.WriteLine($"MonoGame 原版解码: {total} 字节");
    int mgDiff = 0;
    int mgFirst = -1;
    for (int i = 0; i < Math.Min(total, written); i++)
    {
        if (mgBuf[i] != buffer[14 + i])
        {
            if (mgFirst < 0)
            {
                mgFirst = i;
            }

            mgDiff++;
        }
    }

    Console.WriteLine($"与原版差异字节数: {mgDiff}    首个差异偏移: {mgFirst}");
}

if (Environment.GetEnvironmentVariable("XNB_DUMP") is string dumpPath)
{
    File.WriteAllBytes(dumpPath, buffer[14..(14 + (int)unpackedSize)]);
    Console.WriteLine($"已写出解压后的负载: {dumpPath}");
}

if (Environment.GetEnvironmentVariable("XNB_TRACE") == "1")
{
    Console.WriteLine("\n覆盖输出 0x30..0x60 的 LZ4 token:");
    Lz4Trace.Run(compressed, 0, compressed.Length, (int)unpackedSize, 0x30, 0x60);
}

if (diffCount > 0)
{
    Console.WriteLine("以参考实现为准的头部:");
    for (int i = 14 + Math.Max(0, firstDiff - 16); i < 14 + firstDiff + 32 && i < reference.Length; i += 16)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"{i - 14,4:X4}: ");
        for (int j = 0; j < 16; j++)
        {
            sb.Append($"{reference[i + j],2:X2} ");
        }

        Console.WriteLine(sb.ToString());
    }

    // 有差异就直接照参考实现的字节继续解析
    Array.Copy(reference, buffer, targetSize);
    Console.WriteLine("（下面按参考实现的字节解析）");
}

// 打印解压后前 96 字节
Console.WriteLine("解压后前 96 字节:");
for (int i = 14; i < 14 + 96 && i < buffer.Length; i += 16)
{
    StringBuilder sb = new StringBuilder();
    sb.Append($"{i - 14,4:X4}: ");
    for (int j = 0; j < 16; j++)
    {
        sb.Append($"{buffer[i + j],2:X2} ");
    }

    sb.Append("  ");
    for (int j = 0; j < 16; j++)
    {
        byte b = buffer[i + j];
        sb.Append(b >= 32 && b < 127 ? (char)b : '.');
    }

    Console.WriteLine(sb.ToString());
}

// 手工按 MonoGame Texture2DReader 的布局解析
int p = 14;
int readerCount = buffer[p++];
Console.WriteLine($"\n读取器数量: {readerCount}");
string readerName = Read7BitString(buffer, ref p);
Console.WriteLine($"读取器[0] : {readerName}");
uint readerVersion = BitConverter.ToUInt32(buffer, p);
p += 4;
Console.WriteLine($"读取器版本: {readerVersion}");
int sharedCount = buffer[p++];
Console.WriteLine($"共享资源数: {sharedCount}");
int typeIndex = buffer[p++];
Console.WriteLine($"根对象类型索引: {typeIndex} (7-bit, 减 1 = {typeIndex - 1})");

int format = BitConverter.ToInt32(buffer, p);
int width = BitConverter.ToInt32(buffer, p + 4);
int height = BitConverter.ToInt32(buffer, p + 8);
int mips = BitConverter.ToInt32(buffer, p + 12);
uint dataSize = BitConverter.ToUInt32(buffer, p + 16);
Console.WriteLine($"\n纹理头 (偏移 {p - 14}):");
Console.WriteLine($"  Format   = {format}");
Console.WriteLine($"  Width    = {width}");
Console.WriteLine($"  Height   = {height}");
Console.WriteLine($"  MipCount = {mips}");
Console.WriteLine($"  DataSize = {dataSize}");
Console.WriteLine($"  剩余可用  = {targetSize - 14 - (p + 20)}");

static string Read7BitString(byte[] b, ref int pos)
{
    int len = 0;
    int shift = 0;
    byte cur;
    do
    {
        cur = b[pos++];
        len |= (cur & 0x7F) << shift;
        shift += 7;
    } while ((cur & 0x80) != 0);

    string s = Encoding.UTF8.GetString(b, pos, len);
    pos += len;
    return s;
}
