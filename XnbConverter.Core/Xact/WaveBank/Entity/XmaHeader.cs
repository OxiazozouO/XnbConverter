namespace XnbConverter.Xact.WaveBank.Entity;

public class XmaHeader
{
    public class Xma2WaveFormAtex
    {
        public WaveFormAtex wfx = new();
        // Meaning of the WAVEFORMATEX fields here:
        //    wFormatTag;        // Audio format type; always WAVE_FORMAT_XMA2
        //    nChannels;         // Channel count of the decoded audio
        //    nSamplesPerSec;    // Sample rate of the decoded audio
        //    nAvgBytesPerSec;   // Used internally by the XMA encoder
        //    nBlockAlign;       // Decoded sample size; channels * wBitsPerSample / 8
        //    wBitsPerSample;    // Bits per decoded mono sample; always 16 for XMA
        //    cbSize;            // Size in bytes of the rest of this structure (34)

        public ushort NumStreams; // Number of audio streams (1 or 2 channels each)

        public uint ChannelMask; // Spatial positions of the channels in this file,

        // stored as SPEAKER_xxx values (see audiodefs.h)
        public uint SamplesEncoded; // Total number of PCM samples the file decodes to
        public uint BytesPerBlock; // XMA block size (but the last one may be shorter)
        public uint PlayBegin; // First valid sample in the decoded audio
        public uint PlayLength; // Length of the valid part of the decoded audio
        public uint LoopBegin; // Beginning of the loop region in decoded sample terms
        public uint LoopLength; // Length of the loop region in decoded sample terms
        public byte LoopCount; // Number of loop repetitions; 255 = infinite
        public byte EncoderVersion; // Version of XMA encoder that generated the file
        public ushort BlockCount; // XMA blocks in file (and entries in its seek table)
    }

    public class WaveFormAtex
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }
}