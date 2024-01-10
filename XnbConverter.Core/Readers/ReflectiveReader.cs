namespace XnbConverter.Readers;

public class ReflectiveReader<TK> : BaseReader where TK : BaseReader,new()
{
    private TK _reader;

    public override bool IsValueType()
    {
        return false;
    }

    public override void Init(ReaderResolver readerResolver)
    {
        this.readerResolver = readerResolver;
        _reader = new TK();
        _reader.Init(readerResolver);
    }

    public override object Read()
    {
        return _reader.Read();
    }
    
    public override void Write(object input)
    {
        _reader.Write(input);
    }
}