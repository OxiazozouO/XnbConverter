using System.Text;
using XnbConverter.Configurations;
using XnbConverter.Readers;
using XnbConverter.Utilities;
using static XnbConverter.Xact.WaveBank.Entity.WaveBank;

namespace XnbConverter.Xact.WaveBank.Reader;

public class WaveBankReader : BaseReader
{
    public const string XwbSignI = "WBND";

    public const string XwbSignB = "DNBW";

    public const string WBASIGNi = "HVSIWBA\0";

    public const string WBASIGNb = "ISVH\0ABW";

    private int _index1;

    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public static XnbConverter.Xact.WaveBank.Entity.WaveBank Read(string path)
    {
        WaveBankReader waveBankReader = new WaveBankReader();
        waveBankReader.Init(new ReaderResolver
        {
            bufferReader = BufferReader.FormFile(path)
        });
        return (XnbConverter.Xact.WaveBank.Entity.WaveBank)waveBankReader.Read();
    }

    public override object Read()
    {
        XnbConverter.Xact.WaveBank.Entity.WaveBank waveBank = new XnbConverter.Xact.WaveBank.Entity.WaveBank();
        StringBuilder stringBuilder = new StringBuilder();
        List<byte[]> list = new List<byte[]>();
        stringBuilder.Append("开始0\n");
        waveBank.Header.Signature = bufferReader.ReadString(4);
        string signature = waveBank.Header.Signature;
        if (signature != "WBND")
        {
            if (signature != "DNBW")
            {
                throw new NotImplementedException();
            }

            Logger.Info(Error.WaveBankReader_1, waveBank.Header.Signature);
        }
        else
        {
            Logger.Info(Error.WaveBankReader_2, waveBank.Header.Signature);
        }

        waveBank.Header.Version = bufferReader.ReadUInt32();
        Logger.Info(Error.WaveBankReader_3, waveBank.Header.Version);
        int num = 4;
        if (waveBank.Header.Version != 1)
        {
            if (waveBank.Header.Version <= 3)
            {
                num = 3;
            }
            else if (waveBank.Header.Version >= 42)
            {
                waveBank.Header.SkipHeaderVersion = bufferReader.ReadUInt32();
            }

            var segments = waveBank.Header.Segments;
            for (int i = 0; i <= num; i++)
            {
                segments[i].Offset = bufferReader.ReadUInt32();
                segments[i].Length = bufferReader.ReadUInt32();
                Logger.Info(Error.WaveBankReader_6, i, segments[i].Offset, segments[i].Length,
                    FileUtils.FileSize(segments[i].Length));
            }

            stringBuilder.Append("读取Segments后" + bufferReader.BytePosition).Append('\n');
            bufferReader.BytePosition = (int)waveBank.Header.Segments[0].Offset;
            stringBuilder.Append("跳转到BankData索引读取" + bufferReader.BytePosition).Append('\n');
        }

        waveBank.Data.Flag = (Flags)bufferReader.ReadUInt32();
        waveBank.Data.EntryCount = bufferReader.ReadUInt32();
        uint version = waveBank.Header.Version;
        if (version - 2 <= 1)
        {
            waveBank.Data.BankName = bufferReader.ReadString(16);
        }
        else
        {
            waveBank.Data.BankName = bufferReader.ReadString(64);
        }

        uint num2;
        if (waveBank.Header.Version == 1)
        {
            num2 = (uint)bufferReader.BytePosition;
            waveBank.Data.EntryMetaDataElementSize = 20u;
        }
        else
        {
            waveBank.Data.EntryMetaDataElementSize = bufferReader.ReadUInt32();
            waveBank.Data.EntryNameElementSize = bufferReader.ReadInt32();
            waveBank.Data.Alignment = bufferReader.ReadUInt32();
            num2 = waveBank.Header.Segments[1].Offset;
        }

        if ((waveBank.Data.Flag & Flags.Compact) != 0)
        {
            waveBank.Header.CompactFormat = bufferReader.ReadUInt32();
        }

        waveBank.Data.PrintLog();
        stringBuilder.Append("读取完waveBankData" + bufferReader.BytePosition).Append('\n');
        int num3 = (int)num2 - bufferReader.BytePosition;
        if (num3 > 0)
        {
            list.Add(bufferReader.Read(num3));
            stringBuilder.Append("未知数据：").Append(BitConverter.ToString(list[^1])).Append('\n');
        }

        int num4 = (int)waveBank.Header.Segments[num].Offset;
        if (num4 == 0)
        {
            num4 = (int)(num2 + waveBank.Data.EntryCount * waveBank.Data.EntryMetaDataElementSize);
        }

        int num5 = 2;
        if (waveBank.Header.Version >= 42)
        {
            num5 = 3;
        }

        Region region = waveBank.Header.Segments[num5];
        if (region.Offset != 0 && region.Length != 0 && waveBank.Data.EntryNameElementSize == -1)
        {
            waveBank.Data.EntryNameElementSize = 0;
        }

        uint entryCount = waveBank.Data.EntryCount;
        if ((waveBank.Data.Flag & Flags.Compact) != 0)
        {
            for (int j = 0; j < entryCount; j++)
            {
                WaveBankEntry waveBankEntry =
                    new WaveBankEntry();
                stringBuilder.Append("开始读取Entry头" + bufferReader.BytePosition).Append('\n');
                uint num6 = (uint)bufferReader.ReadInt32();
                stringBuilder.Append("读取Entry头结束" + bufferReader.BytePosition).Append('\n');
                waveBankEntry.Format = waveBank.Header.CompactFormat;
                waveBankEntry.PlayRegion.Offset = (num6 & 0x1FFFFF) * waveBank.Data.Alignment;
                waveBank.Entries.Add(waveBankEntry);
            }

            for (int k = 0; k < entryCount; k++)
            {
                uint num7 = waveBank.Entries[k - 1].PlayRegion.Offset;
                if (k == entryCount - 1)
                {
                    num7 = waveBank.Header.Segments[num].Length;
                }

                waveBank.Entries[k].PlayRegion.Length = num7 - waveBank.Entries[k].PlayRegion.Offset;
                waveBank.Entries[k].PlayRegion.Offset += (uint)num4;
            }
        }
        else
        {
            uint num7 = num2;
            for (int l = 0; l < entryCount; l++)
            {
                bufferReader.BytePosition = (int)num7;
                num7 += waveBank.Data.EntryMetaDataElementSize;
                stringBuilder.Append("开始读取Entry头" + bufferReader.BytePosition).Append('\n');
                WaveBankEntry waveBankEntry2 =
                    new WaveBankEntry();
                if (waveBank.Header.Version == 1)
                {
                    waveBankEntry2.Format = bufferReader.ReadUInt32();
                    waveBankEntry2.PlayRegion.Offset = bufferReader.ReadUInt32();
                    waveBankEntry2.PlayRegion.Length = bufferReader.ReadUInt32();
                    waveBankEntry2.LoopRegion.Offset = bufferReader.ReadUInt32();
                    waveBankEntry2.LoopRegion.Length = bufferReader.ReadUInt32();
                }
                else
                {
                    uint entryMetaDataElementSize = waveBank.Data.EntryMetaDataElementSize;
                    if (entryMetaDataElementSize >= 4)
                    {
                        waveBankEntry2.FlagsAndDuration = bufferReader.ReadUInt32();
                    }

                    if (entryMetaDataElementSize >= 8)
                    {
                        waveBankEntry2.Format = bufferReader.ReadUInt32();
                    }

                    if (entryMetaDataElementSize >= 12)
                    {
                        waveBankEntry2.PlayRegion.Offset = bufferReader.ReadUInt32();
                    }

                    if (entryMetaDataElementSize >= 16)
                    {
                        waveBankEntry2.PlayRegion.Length = bufferReader.ReadUInt32();
                    }

                    if (entryMetaDataElementSize >= 20)
                    {
                        waveBankEntry2.LoopRegion.Offset = bufferReader.ReadUInt32();
                    }

                    if (entryMetaDataElementSize >= 24)
                    {
                        waveBankEntry2.LoopRegion.Length = bufferReader.ReadUInt32();
                    }
                }

                if (waveBank.Data.EntryMetaDataElementSize < 24 && waveBankEntry2.PlayRegion.Length == 0)
                {
                    waveBankEntry2.PlayRegion.Length = waveBank.Header.Segments[num].Length;
                }
                else if (waveBank.Data.EntryMetaDataElementSize > 40)
                {
                    stringBuilder.Append("跳转前" + bufferReader.BytePosition).Append('\n');
                    bufferReader.BytePosition = (int)num7;
                    stringBuilder.Append("跳转后" + bufferReader.BytePosition).Append('\n');
                }

                waveBankEntry2.PlayRegion.Offset += (uint)num4;
                stringBuilder.Append("读取Entry头结束" + bufferReader.BytePosition).Append('\n');
                waveBank.Entries.Add(waveBankEntry2);
            }
        }

        WaveBankEntry waveBankEntry3 = null;
        foreach (WaveBankEntry entry in waveBank.Entries)
        {
            bufferReader.BytePosition = (int)entry.PlayRegion.Offset;
            stringBuilder.Append("开始读取Entry数据" + bufferReader.BytePosition).Append('\n');
            entry.Data = bufferReader.Read((int)entry.PlayRegion.Length);
            if (waveBankEntry3 != null)
            {
                uint num8 = entry.PlayRegion.Offset -
                            (waveBankEntry3.PlayRegion.Offset + waveBankEntry3.PlayRegion.Length);
                if (num8 != 0)
                {
                    list.Add(bufferReader.Read((int)num8));
                    stringBuilder.Append("未知Entry数据：").Append(BitConverter.ToString(list[^1])).Append('\n');
                }
            }

            waveBankEntry3 = entry;
            stringBuilder.Append("读取Entry数据结束" + bufferReader.BytePosition).Append('\n');
        }

        stringBuilder.Append("读取结束" + bufferReader.BytePosition).Append('\n');
        return waveBank;
    }

    public static void Save(XnbConverter.Xact.WaveBank.Entity.WaveBank waveBank)
    {
        uint entryCount = waveBank.Data.EntryCount;
        List<WaveBankEntry> entries = waveBank.Entries;
        uint version = waveBank.Header.Version;
        if (ConfigHelper.PInfo)
        {
            Logger.Info(Error.WaveBankReader_4);
            Logger.Info(new string('=', 73));
        }

        for (int i = 0; i < entryCount; i++)
        {
            WaveBankEntry waveBankEntry = entries[i];
            waveBankEntry.DecodeFormat(version, out var code, out var channels, out var rate, out var align,
                out var bits);
            string value = waveBankEntry.SetFileExt(code);
            if (ConfigHelper.PInfo)
            {
                Logger.Info(
                    $"{i + 1,5}{FileUtils.FileSize(waveBankEntry.PlayRegion.Length),12}{value,9}{rate,7}{channels,3}{(bits > 0 ? 16 : 8),5}      {waveBankEntry.GetPath()}");
                Logger.Debug(Error.WaveBankReader_5, waveBankEntry.PlayRegion.Offset, waveBankEntry.Format,
                    waveBankEntry.FlagsAndDuration, waveBankEntry.LoopRegion.Offset, waveBankEntry.LoopRegion.Length);
            }

            Directory.CreateDirectory(waveBankEntry.FilePath);
            WaveFormReader.Save(waveBankEntry.Data, waveBankEntry.GetPath(), code, rate, channels, bits, align);
        }
    }

    public override void Write(object input)
    {
        _ = (XnbConverter.Xact.WaveBank.Entity.WaveBank)input;
        throw new NotImplementedException();
    }
}