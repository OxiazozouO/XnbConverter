namespace Squish;

public class SourceBlock
{
    public byte Start;
    public byte End;
    public byte Error;

    public SourceBlock(byte s, byte e, byte err)
    {
        Start = s;
        End = e;
        Error = err;
    }
}