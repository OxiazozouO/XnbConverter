using System.Text;
using XnbConverter.Readers;
using XnbConverter.Utilities;
using static XnbConverter.Xact.WaveBank.Entity.WaveBank;

namespace XnbConverter.Xact.WaveBank.Reader;

public class WaveBankReader : BaseReader
{
    public const string XwbSignI = "WBND"; // intel endian
    public const string XwbSignB = "DNBW"; // network endian
    public const string WBASIGNi = "HVSIWBA\0"; // intel endian
    public const string WBASIGNb = "ISVH\0ABW"; // network endian

    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public new static Entity.WaveBank Read(string path)
    {
        var waveBankReader = new WaveBankReader();
        waveBankReader.Init(new ReaderResolver()
        {
            bufferReader = BufferReader.FormFile(path)
        });
        return waveBankReader.Read();
    }

    private int _index1 = 0;

    public override Entity.WaveBank Read()
    {
        var waveBank = new Entity.WaveBank();
        var list = new StringBuilder();
        List<byte[]> unknown = new();
        list.Append("开始0\n");
        waveBank.Header.Signature = bufferReader.ReadString(4);

        switch (waveBank.Header.Signature)
        {
            case XwbSignI:
                Log.Info("- signature : {0}  little/intel endian values", waveBank.Header.Signature);
                break;
            case XwbSignB:
                Log.Info("- signature : {0}  big/network endian values", waveBank.Header.Signature);
                break;
            default:
                throw new NotImplementedException();
                Log.Info("alert: the sign is invalid, now I scan the file for the needed signature");
                break;
        }

        waveBank.Header.Version = bufferReader.ReadUInt32();
        Log.Info("- version : {0}", waveBank.Header.Version);
        var last_segment = 4;
        if (waveBank.Header.Version != 1)
        {
            if (waveBank.Header.Version <= 3)
                last_segment = 3;
            else if (waveBank.Header.Version >= 42)
                waveBank.Header.SkipHeaderVersion = bufferReader.ReadUInt32(); // Skip trailing bytes of the version

            var sb = new StringBuilder();
            for (var i = 0; i <= last_segment; i++)
            {
                waveBank.Header.Segments[i].Offset = bufferReader.ReadUInt32();
                waveBank.Header.Segments[i].Length = bufferReader.ReadUInt32();

                sb.Append('\n').Append(string.Format("- segment : {0}    offset : {1}    length : {2}", i,
                    waveBank.Header.Segments[i].Offset, waveBank.Header.Segments[i].Length));
            }

            Log.Info(sb.ToString());
            list.Append("读取Segments后" + bufferReader.BytePosition).Append('\n');
            bufferReader.BytePosition = (int)waveBank.Header.Segments[(int)SegmentIndex.BankData].Offset;
            list.Append("跳转到BankData索引读取" + bufferReader.BytePosition).Append('\n');
        }

        waveBank.Data.Flag = (Flags)bufferReader.ReadUInt32();
        waveBank.Data.EntryCount = bufferReader.ReadUInt32();
        if (waveBank.Header.Version is 2 or 3)
            waveBank.Data.BankName = bufferReader.ReadString(16);
        else
            waveBank.Data.BankName = bufferReader.ReadString(BankNameLength);

        uint wavebank_offset;
        if (waveBank.Header.Version is 1)
        {
            wavebank_offset = (uint)bufferReader.BytePosition;
            waveBank.Data.EntryMetaDataElementSize = 20;
        }
        else
        {
            waveBank.Data.EntryMetaDataElementSize = bufferReader.ReadUInt32();
            waveBank.Data.EntryNameElementSize = bufferReader.ReadInt32();
            waveBank.Data.Alignment = bufferReader.ReadUInt32();
            wavebank_offset = waveBank.Header.Segments[(int)SegmentIndex.EntryMetaData].Offset;
        }

        if ((waveBank.Data.Flag & Flags.Compact) != 0) waveBank.Header.CompactFormat = bufferReader.ReadUInt32();

        waveBank.Data.PrintLog();
        list.Append("读取完waveBankData" + bufferReader.BytePosition).Append('\n');
        var unL = (int)wavebank_offset - bufferReader.BytePosition;
        if (unL > 0)
        {
            unknown.Add(bufferReader.Read(unL));
            list.Append("未知数据：").Append(BitConverter.ToString(unknown[^1])).Append('\n');
        }

        /* COMPATIBILITY WORK-AROUNDS, DEBUGGING and ALLOCATION */
        var playregion_offset = (int)waveBank.Header.Segments[last_segment].Offset;
        if (playregion_offset == 0)
            playregion_offset =
                (int)(wavebank_offset + waveBank.Data.EntryCount * waveBank.Data.EntryMetaDataElementSize);

        var segidx_entry_name = 2;
        if (waveBank.Header.Version >= 42)
            segidx_entry_name = 3;
        {
            var entrySegment = waveBank.Header.Segments[segidx_entry_name];
            if (entrySegment.Offset != 0 && entrySegment.Length != 0)
                if (waveBank.Data.EntryNameElementSize == -1)
                    waveBank.Data.EntryNameElementSize = 0;
        }

        uint nextOffset;
        var count = waveBank.Data.EntryCount;
        if ((waveBank.Data.Flag & Flags.Compact) != 0)
        {
            // 从磁盘加载声音数据偏移表
            for (var i = 0; i < count; i++)
            {
                var entry = new WaveBankEntry();
                list.Append("开始读取Entry头" + bufferReader.BytePosition).Append('\n');
                var len = (uint)bufferReader.ReadInt32();
                list.Append("读取Entry头结束" + bufferReader.BytePosition).Append('\n');
                entry.Format = waveBank.Header.CompactFormat;
                entry.PlayRegion.Offset = (len & ((1 << 21) - 1)) * waveBank.Data.Alignment;
                waveBank.Entries.Add(entry);
            }

            // 现在计算声音数据的长度
            for (var i = 0; i < count; i++)
            {
                nextOffset = waveBank.Entries[i - 1].PlayRegion.Offset;
                if (i == count - 1)
                    nextOffset = waveBank.Header.Segments[last_segment].Length;
                // 用于计算长度的当前和下一个偏移量
                waveBank.Entries[i].PlayRegion.Length = nextOffset - waveBank.Entries[i].PlayRegion.Offset;
                waveBank.Entries[i].PlayRegion.Offset += (uint)playregion_offset;
            }
        }
        else
        {
            nextOffset = wavebank_offset;
            for (var currentEntry = 0; currentEntry < count; currentEntry++)
            {
                bufferReader.BytePosition = (int)nextOffset;
                nextOffset += waveBank.Data.EntryMetaDataElementSize;
                list.Append("开始读取Entry头" + bufferReader.BytePosition).Append('\n');
                var entry = new WaveBankEntry();

                if (waveBank.Header.Version == 1)
                {
                    entry.Format = bufferReader.ReadUInt32();
                    entry.PlayRegion.Offset = bufferReader.ReadUInt32();
                    entry.PlayRegion.Length = bufferReader.ReadUInt32();
                    entry.LoopRegion.Offset = bufferReader.ReadUInt32();
                    entry.LoopRegion.Length = bufferReader.ReadUInt32();
                }
                else
                {
                    var size = waveBank.Data.EntryMetaDataElementSize;
                    if (size >= 4) entry.FlagsAndDuration = bufferReader.ReadUInt32();
                    if (size >= 8) entry.Format = bufferReader.ReadUInt32();
                    if (size >= 12) entry.PlayRegion.Offset = bufferReader.ReadUInt32();
                    if (size >= 16) entry.PlayRegion.Length = bufferReader.ReadUInt32();
                    if (size >= 20) entry.LoopRegion.Offset = bufferReader.ReadUInt32();
                    if (size >= 24) entry.LoopRegion.Length = bufferReader.ReadUInt32();
                }

                if (waveBank.Data.EntryMetaDataElementSize < 24 && entry.PlayRegion.Length == 0)
                {
                    entry.PlayRegion.Length = waveBank.Header.Segments[last_segment].Length;
                }
                else if (waveBank.Data.EntryMetaDataElementSize > 40)
                {
                    list.Append("跳转前" + bufferReader.BytePosition).Append('\n');
                    bufferReader.BytePosition = (int)nextOffset;
                    list.Append("跳转后" + bufferReader.BytePosition).Append('\n');
                }

                entry.PlayRegion.Offset += (uint)playregion_offset;
                /* FILE EXTRACTION */
                list.Append("读取Entry头结束" + bufferReader.BytePosition).Append('\n');

                waveBank.Entries.Add(entry);
            }
        }

        WaveBankEntry? entry0 = null;
        foreach (var entry1 in waveBank.Entries)
        {
            bufferReader.BytePosition = (int)entry1.PlayRegion.Offset;
            list.Append("开始读取Entry数据" + bufferReader.BytePosition).Append('\n');
            entry1.Data = bufferReader.Read((int)entry1.PlayRegion.Length);
            if (entry0 != null)
            {
                var un = entry1.PlayRegion.Offset - (entry0.PlayRegion.Offset + entry0.PlayRegion.Length);
                if (un > 0)
                {
                    unknown.Add(bufferReader.Read((int)un));
                    // Log.Warn("未知Wave Bank Entry数据：{0}",BitConverter.ToString(unknown[^1]));
                    list.Append("未知Entry数据：").Append(BitConverter.ToString(unknown[^1])).Append('\n');
                }
            }

            entry0 = entry1;

            list.Append("读取Entry数据结束" + bufferReader.BytePosition).Append('\n');
        }

        list.Append("读取结束" + bufferReader.BytePosition).Append('\n');
        // File.WriteAllText(@"D:.\debug.txt", list.ToString());
        return waveBank;
    }

    public static void Save(Entity.WaveBank waveBank)
    {
        var count = waveBank.Data.EntryCount;
        var entries = waveBank.Entries;
        var version = waveBank.Header.Version;

        if (Helpers.Config.PInfo)
        {
            
            Log.Info("index   length    fmt     freq  c  b     filename");
            string s = "=========================================================================";
            Log.Info(s);
        }

        for (var i = 0; i < count; i++)
        {
            var entry = entries[i];
            entry.DecodeFormat(version, out var code, out var channels, out var rate, out var align, out var bits);

            /* TRY XSB NAME */
            // entry.len[entry.Index] = (uint)entry.FileName.Length;

            /* CODEC / FORMAT */
            var codecStr = entry.SetFileExt(code);
            if (Helpers.Config.PInfo)
            {
                //log
                string s = $"{i,5}{entry.PlayRegion.Length,9}{codecStr,9}{rate,7}{channels,3}{(bits > 0 ? 16 : 8),3}     {entry.GetPath()}";
                Log.Info(s);
                Log.Debug("offset : {0},format : {1},flags : {2},region_offset : {3},region_length : {4}",
                    entry.PlayRegion.Offset, entry.Format, entry.FlagsAndDuration, entry.LoopRegion.Offset,
                    entry.LoopRegion.Length
                );
            }

            /* FILE EXTRACTION */
            Directory.CreateDirectory(entry.FilePath);

            WaveFormReader.Save(entry.Data, entry.GetPath(), code, rate, channels, bits, align);
        }
    }

    public override void Write(object input)
    {
        var waveBank = (Entity.WaveBank)input;
        throw new NotImplementedException();
    }
}