namespace XnbConverter.Xact.WaveBank.Entity;

public class WaveForm
{
    public RIFFChunk riffChunk = new();
    public FmtChunk fmtChunk = new();
    public FactChunk? factChunk = null;
    public DATAChunk dataChunk = new();
}