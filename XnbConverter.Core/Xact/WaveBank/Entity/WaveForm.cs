namespace XnbConverter.Xact.WaveBank.Entity;

public class WaveForm
{
    public DATAChunk dataChunk = new();
    public FactChunk? factChunk = null;
    public FmtChunk fmtChunk = new();
    public RIFFChunk riffChunk = new();
}