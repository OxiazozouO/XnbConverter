namespace XnbConverter.Xact.WaveBank.Entity;

public class FactChunk
{
    public const uint Size = WaveMarks.Size + sizeof(uint) + sizeof(uint);
    
    
    public string ChunkID = WaveMarks.fact;//fact
    public uint ChunkSize = sizeof(uint);
    public uint DataFactSize; //数据转换为PCM格式后的大小。;
}