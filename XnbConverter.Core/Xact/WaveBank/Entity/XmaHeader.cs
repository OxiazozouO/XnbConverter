namespace XnbConverter.Xact.WaveBank.Entity;

public class XmaHeader
{
	public class Xma2WaveFormAtex
	{
		public ushort BlockCount;

		public uint BytesPerBlock;

		public uint ChannelMask;

		public byte EncoderVersion;

		public uint LoopBegin;

		public byte LoopCount;

		public uint LoopLength;

		public ushort NumStreams;

		public uint PlayBegin;

		public uint PlayLength;

		public uint SamplesEncoded;

		public WaveFormAtex wfx = new WaveFormAtex();
	}

	public class WaveFormAtex
	{
		public ushort cbSize;

		public uint nAvgBytesPerSec;

		public ushort nBlockAlign;

		public ushort nChannels;

		public uint nSamplesPerSec;

		public ushort wBitsPerSample;

		public ushort wFormatTag;
	}
}
