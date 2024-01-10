
namespace XnbConverter.Xact.WaveBank.Entity;

public class RIFFChunk
{
    public const int DefRiffSize = WaveMarks.Size + sizeof(uint) + WaveMarks.Size;
    
    public string ChunkID = WaveMarks.RIFF;//RIFF fact
    public uint ChunkSize;
    public string Format = WaveMarks.WAVE;//WAVE
}