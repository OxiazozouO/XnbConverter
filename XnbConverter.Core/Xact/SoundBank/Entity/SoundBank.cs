using System.Collections.Generic;

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
	}

	public class SoundCue
	{
		public class CueVariation
		{
			public byte BWeightMax;

			public byte BWeightMin;

			public uint Flags;

			public float FWeightMax;

			public float FWeightMin;

			public uint SoundOffset;

			public ushort TrackIndex;

			public byte WaveBankIndex;
		}

		public List<CueVariation> CueVariations = new List<CueVariation>();

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

	public List<string> WaveBankNames = new List<string>();
}
