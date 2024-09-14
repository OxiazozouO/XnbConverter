namespace XnbConverter.Xact.WaveBank.Entity;

public class WaveForm
{
	public DATAChunk dataChunk = new DATAChunk();

	public FactChunk? factChunk;

	public FmtChunk fmtChunk = new FmtChunk();

	public RIFFChunk riffChunk = new RIFFChunk();
}
