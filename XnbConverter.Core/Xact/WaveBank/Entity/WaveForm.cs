namespace XnbConverter.Xact.WaveBank.Entity;

public class WaveForm
{
    public RIFFChunk riffChunk = new RIFFChunk();
    public FmtChunk fmtChunk = new FmtChunk();
    public FactChunk? factChunk = null;
    public DATAChunk dataChunk = new DATAChunk();
}