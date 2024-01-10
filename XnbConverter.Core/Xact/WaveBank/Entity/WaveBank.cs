using System.Text;
using Newtonsoft.Json;
using XnbConverter.Entity;
using XnbConverter.Utilities;
using static XnbConverter.Entity.XnbObject.TargetTags;

namespace XnbConverter.Xact.WaveBank.Entity;

public class WaveBank
{
    #region Constants
    // from xact3wb.h
    public const int AdpcmMiniWaveFormatBlockAlignConversionOffset = 22;
    
    private const int HEADER_VERSION = 43; // Current wavebank file version

    public const int BankNameLength = 64; // Wave bank friendly name length, in characters
    public const int EntryNameLength = 64; // Wave bank entry friendly name length, in characters

    public const uint MaxDataSegmentSize = 0xFFFFFFFF; // Maximum wave bank data segment size, in bytes

    public const uint MaxCompactDataSegmentSize = 0x001FFFFF; // Maximum compact wave bank data segment size, in bytes
    
    private const int BIT_DEPTH8 = 0x0; // 8-bit data (PCM only)
    private const int BIT_DEPTH16 = 0x1; // 16-bit data (PCM only)
    //
    // DVD data sizes
    //
    private const int DVD_SECTOR_SIZE = 2048;
    private const int DVD_BLOCK_SIZE = DVD_SECTOR_SIZE * 16;
    //
    // Bank alignment presets
    //
    private const int ALIGNMENT_MIN = 4; // Minimum alignment
    private const int ALIGNMENT_DVD = DVD_SECTOR_SIZE; // DVD-optimized alignment
    
    
    
    
    private const int ENTRY_NAMES = 0x00010000; // bank includes entry names
    private const int COMPACT = 0x00020000; // bank uses compact format
    private const int SYNC_DISABLED = 0x00040000; // bank is disabled for audition sync
    private const int SEEK_TABLES = 0x00080000; // bank includes seek tables
    private const int MASK = 0x000F0000;


    //
    // Bank flags
    //
    
    // public static class Flags
    // {
    //     public const int Buffer = 0x00000000; // In-memory buffer
    //     public const int Streaming = 0x00000001; // Streaming
    //     public const int TypeMask = 0x00000001;
    //     public const int EntryNames = 0x00010000; // Bank includes entry names
    //     public const int Compact = 0x00020000; // Bank uses compact format
    //     public const int SyncDisabled = 0x00040000; // Bank is disabled for audition sync
    //     public const int SeekTables = 0x00080000; // Bank includes seek tables.
    //     public const int Mask = 0x000F0000;
    // }
    
    //
    // Entry flags
    //
    // public static class EntryFlags
    // {
    //     public const int ReadAhead = 0x00000001; // Enable stream read-ahead
    //     public const int LoopCache = 0x00000002; // One or more looping sounds use this wave
    //     public const int RemoveLoopTail = 0x00000004; // Remove data after the end of the loop region
    //     public const int IgnoreLoop = 0x00000008; // Used internally when the loop region can't be used
    //     public const int Mask = 0x00000008;
    // }
    
    [Flags]
    public enum Flags : uint
    {
        Buffer = 0x00000000, // In-memory buffer
        Streaming = 0x00000001, // Streaming
        TypeMask = 0x00000001,
        EntryNames = 0x00010000, // Bank includes entry names
        Compact = 0x00020000, // Bank uses compact format
        SyncDisabled = 0x00040000, // Bank is disabled for audition sync
        SeekTables = 0x00080000, // Bank includes seek tables.
        Mask = 0x000F0000
    }

    //
    // Entry wave format identifiers
    //
    public enum WaveBankFormats
    {
        Pcm   = 0x0,
        Xma   = 0x1,
        AdpcmMs = 0x2,////microsoft adpcm
        Wma   = 0x3,
        Unknown = -1
    }
    
    //
    // Wave bank segment identifiers
    //
    public enum SegmentIndex
    {
        BankData = 0, // Bank data
        EntryMetaData, // Entry meta-data
        SeekTables, // Storage for seek tables for the encoded waves.
        EntryNames, // Entry friendly names
        EntryWaveData, // Entry wave data
        Count
    }
    #endregion
    
    
    public readonly WaveBankHeader Header = new WaveBankHeader();
    public readonly List<WaveBankEntry> Entries = new List<WaveBankEntry>();
    public readonly WaveBankData Data = new WaveBankData();

    public class Region
    {
        public uint Offset = 0; // Region offset, in bytes
        public uint Length = 0; // Region length, in bytes
    }
    public class WaveBankHeader
    {
        public string  Signature; // (uint32_t -> char[4]) File signature
        public uint    Version = 0; // Version of the tool that created the file
        public uint    SkipHeaderVersion = 0;
        public uint    CompactFormat = 0;

        public readonly Region[] Segments = new Region[(int)SegmentIndex.Count]
        {
            new Region(),
            new Region(),
            new Region(),
            new Region(),
            new Region()
        }; // Segment lookup table
    }

    public class WaveBankEntry
    {
        //
        // Arbitrary fixed sizes
        //
        private const int XMA_STREAMS_MAX = 3; // enough for 5.1 channel audio
        private const int XMA_CHANNELS_MAX = 6; // enough for 5.1 channel audio (cf. XAUDIOCHANNEL_SOURCEMAX)

        public string? FilePath;
        public string? FileName = null;
        public string? FileExt;
        public string GetPath() => FilePath + FileName + FileExt;
        
        public uint FlagsAndDuration = 0; // dwFlags:4 and Duration:28
        [JsonIgnore] public byte[] Data;
        
        [JsonIgnore] public uint Format = 0; // Entry format
        public readonly Region PlayRegion = new Region(); // Region within the wave data segment that contains this entry
        public readonly Region LoopRegion = new Region(); // Region within the wave data that should loop

        // XMA loop region
        // Note: this is not the same memory layout as the XMA loop region
        // passed to the XMA driver--it is more compact. The named values
        // map correctly and there are enough bits to store the entire
        // range of values that XMA considers valid, with one exception:
        // valid values for nSubframeSkip are 1, 2, 3, or 4. In order to
        // store this in two bits, XACT subtracts 1 from the value, then adds
        // -------------------------------------------------------------
        // public readonly XMALoopRegion[] XmaLoopRegions = new XMALoopRegion[XMA_STREAMS_MAX];
        // -------------------------------------------------------------
        // The last element in the union is an array that aliases the
        // entire union so we can byte-reverse the whole thing.
        // -------------------------------------------------------------
        // Region[] LoopRegionAlias = new Region[XMA_STREAMS_MAX];
        //-------------------------------------------------------------
        // public WaveBankEntry()
        // {
        //     for (int i = 0; i < XmaLoopRegions.Length; i++)
        //         XmaLoopRegions[i] = new XMALoopRegion();
        //     
        //     for (int i = 0; i < LoopRegionAlias.Length; i++)
        //         LoopRegionAlias[i] = new Region();
        // }
        //-------------------------------------------------------------
        // public class XMALoopRegion
        // {
        //     public uint StartOffset = 0; // loop start offset (in bits)
        //
        //     public uint nSubframeSkip_nSubframeEnd_dwEndOffset = 0;
        //     //uint    SubframeSkip   : 2;    // needed by XMA decoder. Valid values for XMA are 1-4; XACT converts to 0-3 for storage. Add 1 to this value before passing to driver.
        //     //uint    SubframeEnd    : 2;    // needed by XMA decoder
        //     //uint    EndOffset     : 28;   // loop end offset (in bits)
        // }

        public string SetFileExt(WaveBankFormats code)
        {
            switch (code)
            {
                case WaveBankFormats.Pcm:
                    FileExt = ".wav";
                    break;
                case WaveBankFormats.Xma:
                    FileExt = ".wav";
                    break;
                case WaveBankFormats.AdpcmMs:
                    FileExt = ".wav";
                    break;
                case WaveBankFormats.Wma:
                    FileExt = ".wma";
                    break;
                default:
                    FileExt = ".bin";
                    code = WaveBankFormats.Unknown;
                    break;
            }
            return code.ToString();
        }

        public void DecodeFormat(uint version, out WaveBankFormats code, out int channels, out int rate, out int align, out int bits)
        {
            int fo = (int)Format;
            switch (version)
            {
                case 1:
                    // I'm not 100% sure if the following is correct
                    // version 1:
                    // 1 00000000 000101011000100010 0 001 0
                    // | |         |                 | |   |
                    // | |         |                 | |   FormatTag
                    // | |         |                 | Channels
                    // | |         |                 ???
                    // | |         SamplesPerSec
                    // | BlockAlign
                    // BitsPerSample
                    code     = (WaveBankFormats)(fo         & ((1 << 1)  - 1));
                    channels = (fo >> (1))                  & ((1 << 3)  - 1);
                    rate     = (fo >> (1 + 3 + 1))          & ((1 << 18) - 1);
                    align    = (fo >> (1 + 3 + 1 + 18))     & ((1 << 8)  - 1);
                    bits     = (fo >> (1 + 3 + 1 + 18 + 8)) & ((1 << 1)  - 1);
                    break;
                // case 23:
                //     // I'm not 100% sure if the following is correct
                //     // version 23:
                //     // 1000000000 001011101110000000 001 1
                //     // | |        |                  |   |
                //     // | |        |                  |   ???
                //     // | |        |                  Channels?
                //     // | |        SamplesPerSec
                //     // | ???
                //     // !!!UNKNOWN FORMAT!!!
                //
                //     codec = (fo >> (0))              & ((1 << 1)  - 1);
                //     chans = (fo >> (1))              & ((1 << 3)  - 1);
                //     rate  = (fo >> (1 + 3))          & ((1 << 18) - 1);
                //     align = (fo >> (1 + 3 + 18))     & ((1 << 9)  - 1);
                //     bits  = (fo >> (1 + 3 + 18 + 9)) & ((1 << 1)  - 1);
                //     break;
                default:
                    // versions 2, 3, 37, 42, 43, 44 and so on, check WAVEBANKMINIWAVEFORMAT in xact3wb.h
                    // 0 00000000 000111110100000000 010 01
                    // | |        |                  |   |
                    // | |        |                  |   FormatTag
                    // | |        |                  Channels
                    // | |        SamplesPerSec
                    // | BlockAlign
                    // BitsPerSample
                    code    = (WaveBankFormats)(fo      & ((1 << 2 ) - 1));
                    channels = (fo >> (2))              & ((1 << 3 ) - 1);
                    rate     = (fo >> (2 + 3))          & ((1 << 18) - 1);
                    align    = (fo >> (2 + 3 + 18))     & ((1 << 8 ) - 1);
                    bits     = (fo >> (2 + 3 + 18 + 8)) & ((1 << 1 ) - 1);
                    break;
            }

            // this work-around is correct but I don't know what's the latest version that falls in this rule
            if (version > 3) return;
            if (code == WaveBankFormats.Xma)
                code = WaveBankFormats.AdpcmMs;
        }
    }

    public class WaveBankData
    {
        public Flags Flag; // Bank flags
        public uint EntryCount; // Number of entries in the bank
        public string BankName; // Bank friendly name// = new char[BANKNAME_LENGTH]
        public uint EntryMetaDataElementSize; // Size of each entry meta-data element, in bytes
        public int EntryNameElementSize; // Size of each entry name element, in bytes
        public uint Alignment; // Entry alignment, in bytes
        public uint CompactFormat; // Format data for compact bank
        public uint BuildTime; // Build timestamp

        public void PrintLog()
        {
            if(!Helpers.Config.PInfo)return;
            StringBuilder flagsText = new StringBuilder();
            if (0 != (Flag & Flags.Buffer)) flagsText.Append("in-memory, ");
            if (0 != (Flag & Flags.Streaming)) flagsText.Append("streaming, ");
            if (0 != (Flag & Flags.EntryNames)) flagsText.Append("bank+entry_names, ");
            if (0 != (Flag & Flags.Compact)) flagsText.Append("compact_format, ");
            if (0 != (Flag & Flags.SyncDisabled)) flagsText.Append("disabled_bank, ");

            Log.Info(
                "\n- flags   {0}\n- file_num    {1}\n- bank name    {2}\n- entry meta size  {3}\n- entry name size  {4}\n- alignment    {5}",
                flagsText.ToString(),EntryCount,BankName,EntryMetaDataElementSize,EntryNameElementSize,Alignment
            );
            if (EntryMetaDataElementSize < 24)
                Log.Info("- EntryMetaDataElementSize is small");
        }
    }
    
    
}