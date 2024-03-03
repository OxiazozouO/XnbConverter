namespace XnbConverter.Xact.WaveBank.Entity;

public class DATAChunk : RIFFChunk
{
    public byte[] Data; //sample 1(byte l1, byte r1)...sample 1(byte ln, byte rn)
    public string DataID = WaveMarks.data; //data
    public uint DataSize;
}