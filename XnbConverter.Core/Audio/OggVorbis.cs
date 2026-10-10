namespace XnbConverter.Audio;

/// <summary>
/// 把 Ogg Vorbis 解成 PCM。给 XACT 波形库的「追加条目」用 ——
/// 波形库只认 PCM / MS-ADPCM / XMA / WMA，模组给的却是 .ogg，所以得先解出来。
///
/// 解码器是本目录下移植的 <c>NVorbis</c>（原库随 MonoGame 一起分发，游戏解 ogg 用的就是它），
/// 不引任何外部依赖、也不调 ffmpeg 之类的外部程序。
/// </summary>
public static class OggVorbis
{
    /// <summary>
    /// 解成 16bit PCM。返回的 <c>Pcm</c> 是**交织**的 little-endian 样本，
    /// 长度 = 每声道样本数 × 声道数 × 2。
    /// </summary>
    public static (int Channels, int SampleRate, byte[] Pcm) ToPcm16(string path)
    {
        using var reader = new NVorbis.VorbisReader(path);
        // 让解码器自己把越界样本夹住，免得 (short) 强转时绕回成反相的爆音
        reader.ClipSamples = true;

        int channels = reader.Channels;
        int rate = reader.SampleRate;
        long total = reader.TotalSamples;

        // TotalSamples 是**每声道**的样本数
        byte[] pcm = new byte[total > 0 ? checked((int)(total * channels * 2)) : 0];
        float[] buffer = new float[channels * 4096];

        int written = 0;
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                short s = (short)(buffer[i] * 32767f);
                if (written + 1 >= pcm.Length)
                {
                    // TotalSamples 只是估计，真读多了就扩容
                    Array.Resize(ref pcm, Math.Max(pcm.Length * 2, written + 4096));
                }

                pcm[written++] = (byte)s;
                pcm[written++] = (byte)(s >> 8);
            }
        }

        if (written != pcm.Length)
        {
            Array.Resize(ref pcm, written);
        }

        return (channels, rate, pcm);
    }
}
