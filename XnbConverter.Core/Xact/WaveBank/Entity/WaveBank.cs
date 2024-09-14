using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using XnbConverter.Configurations;
using XnbConverter.Utilities;

namespace XnbConverter.Xact.WaveBank.Entity;

public class WaveBank
{
	public class Region
	{
		public uint Length;

		public uint Offset;
	}

	public class WaveBankHeader
	{
		public readonly Region[] Segments = new Region[5]
		{
			new Region(),
			new Region(),
			new Region(),
			new Region(),
			new Region()
		};

		public uint CompactFormat;

		public string Signature;

		public uint SkipHeaderVersion;

		public uint Version;
	}

	public class WaveBankEntry
	{
		private const int XMA_STREAMS_MAX = 3;

		private const int XMA_CHANNELS_MAX = 6;

		public readonly Region LoopRegion = new Region();

		public readonly Region PlayRegion = new Region();

		[JsonIgnore]
		public byte[] Data;

		public string? FileExt;

		public string? FileName;

		public string? FilePath;

		public uint FlagsAndDuration;

		[JsonIgnore]
		public uint Format;

		public string GetPath()
		{
			return FilePath + FileName + FileExt;
		}

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
			int format = (int)Format;
			if (version == 1)
			{
				code = (WaveBankFormats)(format & 1);
				channels = (format >> 1) & 7;
				rate = (format >> 5) & 0x3FFFF;
				align = (format >> 23) & 0xFF;
				bits = (format >> 31) & 1;
			}
			else
			{
				code = (WaveBankFormats)(format & 3);
				channels = (format >> 2) & 7;
				rate = (format >> 5) & 0x3FFFF;
				align = (format >> 23) & 0xFF;
				bits = (format >> 31) & 1;
			}
			if (version <= 3 && code == WaveBankFormats.Xma)
			{
				code = WaveBankFormats.AdpcmMs;
			}
		}
	}

	public class WaveBankData
	{
		public uint Alignment;

		public string BankName;

		public uint BuildTime;

		public uint CompactFormat;

		public uint EntryCount;

		public uint EntryMetaDataElementSize;

		public int EntryNameElementSize;

		public Flags Flag;

		public void PrintLog()
		{
			if (ConfigHelper.PInfo)
			{
				StringBuilder stringBuilder = new StringBuilder();
				if ((Flag & Flags.Buffer) != 0)
				{
					stringBuilder.Append("in-memory, ");
				}
				if ((Flag & Flags.Streaming) != 0)
				{
					stringBuilder.Append("streaming, ");
				}
				if ((Flag & Flags.EntryNames) != 0)
				{
					stringBuilder.Append("bank+entry_names, ");
				}
				if ((Flag & Flags.Compact) != 0)
				{
					stringBuilder.Append("compact_format, ");
				}
				if ((Flag & Flags.SyncDisabled) != 0)
				{
					stringBuilder.Append("disabled_bank, ");
				}
				Logger.Info(Error.WaveBank_1, stringBuilder.ToString(), EntryCount, BankName, EntryMetaDataElementSize, EntryNameElementSize, Alignment);
				if (EntryMetaDataElementSize < 24)
				{
					Logger.Info(Error.WaveBank_2);
				}
			}
		}
	}

	[Flags]
	public enum Flags : uint
	{
		Buffer = 0u,
		Streaming = 1u,
		TypeMask = 1u,
		EntryNames = 0x10000u,
		Compact = 0x20000u,
		SyncDisabled = 0x40000u,
		SeekTables = 0x80000u,
		Mask = 0xF0000u
	}

	public enum WaveBankFormats
	{
		Pcm = 0,
		Xma = 1,
		AdpcmMs = 2,
		Wma = 3,
		Unknown = -1
	}

	public enum SegmentIndex
	{
		BankData,
		EntryMetaData,
		SeekTables,
		EntryNames,
		EntryWaveData,
		Count
	}

	public readonly WaveBankData Data = new WaveBankData();

	public readonly List<WaveBankEntry> Entries = new List<WaveBankEntry>();

	public readonly WaveBankHeader Header = new WaveBankHeader();

	public const int AdpcmMiniWaveFormatBlockAlignConversionOffset = 22;

	private const int HEADER_VERSION = 43;

	public const int BankNameLength = 64;

	public const int EntryNameLength = 64;

	public const uint MaxDataSegmentSize = uint.MaxValue;

	public const uint MaxCompactDataSegmentSize = 2097151u;

	private const int BIT_DEPTH8 = 0;

	private const int BIT_DEPTH16 = 1;

	private const int DVD_SECTOR_SIZE = 2048;

	private const int DVD_BLOCK_SIZE = 32768;

	private const int ALIGNMENT_MIN = 4;

	private const int ALIGNMENT_DVD = 2048;

	private const int ENTRY_NAMES = 65536;

	private const int COMPACT = 131072;

	private const int SYNC_DISABLED = 262144;

	private const int SEEK_TABLES = 524288;

	private const int MASK = 983040;
}
