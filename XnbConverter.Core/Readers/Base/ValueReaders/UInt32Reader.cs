namespace XnbConverter.Readers.Base.ValueReaders;

public class UInt32Reader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        bufferReader = readerResolver.bufferReader;
        bufferWriter = readerResolver.bufferWriter;
    }

    public override bool IsValueType()
    {
        return true;
    }

    public override object Read()
    {
        return bufferReader.ReadUInt32();
    }

    public override void Write(object content)
    {
        var input = (uint)content;
        bufferWriter.WriteUInt32(input);
    }
}