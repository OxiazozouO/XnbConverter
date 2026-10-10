using XnbConverter.Audio;

namespace XactRoundTrip;

/// <summary>
/// 移植进来的 NVorbis 解得对不对：拿 ffmpeg 转出的同参数 PCM 当参照物比。
///
/// ffmpeg 那边**不能加 <c>-ac</c>/<c>-ar</c>**（那就是重采样了，参数一变就没法逐样本比），
/// 让它按文件原本的声道/采样率转。
///
/// 用法: XactRoundTrip --ogg2pcm &lt;.ogg&gt; &lt;参照 wav&gt;
/// </summary>
public static class OggToPcmCheck
{
    public static int Run(string ogg, string referenceWav)
    {
        var (channels, rate, pcm) = OggVorbis.ToPcm16(ogg);
        (int rChannels, int rRate, int rBits, int rOffset, int rLength) = ReadPcmWav(referenceWav);
        byte[] reference = new byte[rLength];
        Array.Copy(File.ReadAllBytes(referenceWav), rOffset, reference, 0, rLength);

        bool shapeOk = channels == rChannels && rate == rRate && rBits == 16
                       && pcm.Length == reference.Length;

        int maxDiff = -1;
        long sumDiff = 0;
        if (shapeOk)
        {
            for (int i = 0; i + 1 < pcm.Length; i += 2)
            {
                short a = BitConverter.ToInt16(pcm, i);
                short b = BitConverter.ToInt16(reference, i);
                int d = Math.Abs(a - b);
                if (d > maxDiff)
                {
                    maxDiff = d;
                }

                sumDiff += d;
            }
        }

        long samples = pcm.Length / 2;
        double avg = samples > 0 ? (double)sumDiff / samples : 0;

        // 判据：形状必须一模一样；样本上**不要求逐位相同** —— NVorbis 与 ffmpeg 是两台
        // 各自实现的解码器，MDCT / 窗函数的浮点路径不同，个别陡峭处会有几十 LSB 的尖峰
        // （16bit 满量程 32767，听不出来），但**平均差**应当贴近 0（实测 0.4~0.6 LSB），
        // 那才说明两边解出来的是同一段音频。平均差若到 1 以上，就该怀疑是解码错了。
        bool closeEnough = avg <= 1.0 && maxDiff <= 64;

        Console.WriteLine($"  NVorbis：{channels}ch {rate}Hz，PCM {pcm.Length} 字节");
        Console.WriteLine($"  ffmpeg ：{rChannels}ch {rRate}Hz {rBits}bit，PCM {rLength} 字节");
        Console.WriteLine($"  形状一致 {(shapeOk ? "✓" : "✗")}   平均差 {avg:F4} LSB" +
                          $"（阈值 1.0）   最大差 {maxDiff} LSB（阈值 64，陡峭处的浮点尖峰）" +
                          $"  {(closeEnough ? "✓" : "✗")}");

        return shapeOk && closeEnough ? 0 : 1;
    }

    /// <summary>标准 PCM wav：RIFF/WAVE + fmt + data。</summary>
    private static (int channels, int rate, int bits, int dataOffset, int dataLength) ReadPcmWav(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int pos = 12;
        int channels = 0;
        int rate = 0;
        int bits = 0;
        while (pos + 8 <= b.Length)
        {
            int id = BitConverter.ToInt32(b, pos);
            int size = BitConverter.ToInt32(b, pos + 4);
            if (id == 0x20746d66)
            {
                channels = BitConverter.ToInt16(b, pos + 10);
                rate = BitConverter.ToInt32(b, pos + 12);
                bits = BitConverter.ToInt16(b, pos + 22);
            }
            else if (id == 0x61746164)
            {
                return (channels, rate, bits, pos + 8, size);
            }

            pos += 8 + size + (size % 2);
        }

        throw new InvalidOperationException("参照 wav 里没有 data 块。");
    }
}
