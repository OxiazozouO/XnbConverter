namespace XnbConverter.Readers.Base.ValueReaders;

public class SingleReader : BaseReader
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
        return bufferReader.ReadSingle();
    }

    public override void Write(object content)
    {
        var input = (float)content;
        bufferWriter.WriteSingle(input);
    }
}