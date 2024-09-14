namespace XnbConverter.Xact.WaveBank.Entity;

public class FactChunk
{
	public const uint Size = 12u;

	public string ChunkID = "fact";

	public uint ChunkSize = 4u;

	public uint DataFactSize;
}
