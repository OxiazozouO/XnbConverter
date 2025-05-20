namespace XnbConverter.Xact.WaveBank.Entity;

public class DATAChunk : RIFFChunk
{
	public byte[] Data;

	public string DataID = "data";

	public uint DataSize;
}
