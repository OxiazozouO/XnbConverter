namespace XnbConverter.Xact.SoundBank.Entity;

public class SoundBank
{
	public class SoundBankHeader
	{
		public uint ComplexCuesOffset;

		public ushort Crc;

		public uint CueNameHashTableOffset;

		public uint CueNameHashValsOffset;

		public uint CueNamesOffset;

		public ushort CueNameTableLen;

		public ushort FormatVersion;

		public uint LastModifiedHigh;

		public uint LastModifiedLow;

		public string Magic;

		public string Name;

		public ushort NumComplexCues;

		public ushort NumSimpleCues;

		public ushort NumSounds;

		public ushort NumTotalCues;

		public byte NumWaveBanks;

		public byte Platform;

		public uint SimpleCuesOffset;

		public uint SoundsOffset;

		public ushort ToolVersion;

		public List<byte[]> Unknowns = new List<byte[]>();

		public uint VariationTablesOffset;

		public uint WaveBankNameTableOffset;
	}

	public class SoundEntry
	{
		public byte Flags;

		public uint SoundOffset;

		/// <summary>该条目在 .xsb 中的起始偏移。</summary>
		public uint FileOffset;
	}

	public class SoundCue
	{
		public class CueVariation
		{
			public byte BWeightMax;

			public byte BWeightMin;

			/// <summary>该项在 .xsb 中的起始偏移，写回时据此就地修补。</summary>
			public uint FileOffset;

			public uint Flags;

			public float FWeightMax;

			public float FWeightMin;

			public uint SoundOffset;

			public ushort TrackIndex;

			public byte WaveBankIndex;
		}

		public List<CueVariation> CueVariations = new List<CueVariation>();

		/// <summary>该 Cue 记录在 .xsb 中的起始偏移。</summary>
		public uint FileOffset;

		public ushort FadeInSec;

		public ushort FadeOutSec;

		public byte Flags;

		public byte InstanceFlags;

		public byte InstanceLimit;

		public ushort NumEntries;

		public XactSound Sound;

		public uint SoundOffset;

		public uint TransitionTableOffset;

		public List<byte[]> Unknowns = new List<byte[]>();

		public ushort VariationFlags;

		public uint VariationTableOffset;
	}

	public const ushort SDBK_FORMAT_VERSION = 43;

	public Dictionary<string, XactSound[]> _sounds = new Dictionary<string, XactSound[]>();

	public List<SoundCue> Cues = new List<SoundCue>();

	public float[] dedefaultProbability = new float[1] { 1f };

	public SoundBankHeader Header = new SoundBankHeader();

	public List<SoundEntry> SoundEntrys = new List<SoundEntry>();

	/// <summary>
	/// 读入时的原始字节。写回采用「同尺寸原地替换」：以它为模板，只把已建模的记录
	/// 按各自记录的原偏移就地修补，声调表里未被解析的 ExtraData 等部分保持原样。
	/// </summary>
	public byte[] OriginalBytes;

	public List<string> WaveBankNames = new List<string>();
}
