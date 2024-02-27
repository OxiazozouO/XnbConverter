using XnbConverter.Utilities;
using static XnbConverter.Xact.WaveBank.Entity.FmtChunk.AudioFormats;

namespace XnbConverter.Xact.WaveBank.Entity;

public class FmtChunk
{
    public static class Tags
    {
        private const short Bit_DEPTH8 = 0x0; // 8-bit data (PCM only)
        private const short BIT_DEPTH16 = 0x1; // 16-bit data (PCM only)
    }

    public enum AudioFormats : short
    {
        Pcm = 1,
        AdpcmMs = 2, //microsoft adpcm
        Ieee = 3,
        Ima4 = 17
    }


    public bool CheckFmtID(string typeName)
    {
        bool b;
        switch (typeName)
        {
            case "SoundEffect":
                b = FmtTag is Pcm;
                if (!b) throw new XnbError("对于SoundEffect仅支持Pcm格式的wav, 目前的格式为{0}", FmtTag.ToString());
                break;
            case "Xact":
                b = FmtTag is Pcm or AdpcmMs;
                if (!b) throw new XnbError("对于Xact，仅支持Pcm或AdpcmMs格式的wav, 目前的格式为{0}", FmtTag.ToString());
                break;
            case "All":
                b = FmtTag is Pcm or AdpcmMs or Ieee or Ima4;
                if (!b) throw new XnbError("不是游戏支持的类型！");
                break;
            default:
                throw new NotImplementedException();
        }

        return b;
    }

    public const uint defSize = 16;

    public string FmtID = WaveMarks.fmt; //fmt
    public uint FmtSize = defSize;
    public AudioFormats FmtTag;
    public ushort NumChannels = 0;
    public uint SampleRate = 0;
    public uint ByteRate = 0; //ByteRate == AvgBytesPerSec = SampleRate * FmtChannels * BitsPerSample/8
    public ushort BlockAlign = 0;
    public ushort BitsPerSample = 0;


    public ushort? CbSize = null; //额外信息大小的计数（以字节为单位）

    //当WAV文件使用的不是PCM编码方式时，
    //就需要扩展格式块，它是在基本的Format chunk又添加一段数据。
    //该数据的前两个字节，表示的扩展块的长度。
    // 紧接其后的是扩展的数据区，含有扩展的格式信息，
    // 其具体的长度取决于压缩编码的类型。当某种编码方式（如 ITU G.711 a-law）
    // 使扩展区的长度为0，扩展区的长度字段还必须保留，只是其值设置为0。
    public byte[] ExtensionChunkData; //额外信息
    public uint ExtensionChunkSize => (uint)ExtensionChunkData.Length + 2u /*sizeof(CbSize) == 2*/;


    public const ushort AdpcmMsCbSize = 32;

    private static readonly byte[] AdpcmMsBuff = new byte[AdpcmMsCbSize]
    {
        0, 0, 7, 0, // == (ushort, ushort) == (ValidBitsPerSample, sizeof(WaveFormatEX))
        0, 1, 0, 0,
        0, 2, 0, 255,
        0, 0, 0, 0,
        192, 0, 64, 0,
        240, 0, 0, 0,
        204, 1, 48, 255,
        136, 1, 24, 255
    };

    public void SetAdpcmMsExtensionChunk(ushort value)
    {
        CbSize = AdpcmMsCbSize;
        ExtensionChunkData = new byte[AdpcmMsBuff.Length];
        Array.Copy(AdpcmMsBuff, ExtensionChunkData, AdpcmMsBuff.Length);
        BitConverter.GetBytes(value).CopyTo(ExtensionChunkData, 0);
    }
}