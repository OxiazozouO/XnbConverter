namespace Squish;

public class SourceBlock
{
    public byte End;
    public byte Error;
    public byte Start;

    public SourceBlock(byte s, byte e, byte err)
    {
        Start = s;
        End = e;
        Error = err;
    }
}