namespace XnbConverter.Xact.WaveBank.Entity;

public class RIFFChunk
{
	public const int DefRiffSize = 12;

	public string ChunkID = "RIFF";

	public uint ChunkSize;

	public string Format = "WAVE";
}
