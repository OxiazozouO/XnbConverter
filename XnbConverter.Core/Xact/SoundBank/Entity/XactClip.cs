using System.Collections.Generic;

namespace XnbConverter.Xact.SoundBank.Entity;

public class XactClip
{
	public class WaveIndex
	{
		public byte EventFlags;

		public uint EventInfo;

		public byte LoopCount;

		public float MaxFrequency;

		public float MaxPitch;

		public float MaxQ;

		public byte MaxVolumeDecibels;

		public float MinFrequency;

		public float MinPitch;

		public float MinQ;

		public byte MinVolumeDecibels;

		public byte MoreFlags;

		public ushort NumTracks;

		public float PanAngle;

		public float PanArc;

		public float RandomOffset;

		public int TrackIndex;

		public int[] Tracks;

		public List<byte[]> unkn = new List<byte[]>();

		public byte VariationFlags;

		public int WaveBankIndex;

		public int[] WaveBanks;

		public byte[][] Weights;
	}

	public uint ClipOffset;

	public ushort FilterFrequency;

	public ushort FilterQAndFlags;

	public byte VolumeDb;

	public WaveIndex[] WaveIndexs;
}
