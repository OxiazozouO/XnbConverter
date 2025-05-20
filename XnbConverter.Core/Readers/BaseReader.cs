namespace XnbConverter.Readers;

public abstract class BaseReader
{
    protected BufferReader bufferReader;

    protected BufferWriter bufferWriter;

    protected ReaderResolver readerResolver;

    public abstract bool IsValueType();

    public virtual void Init(ReaderResolver resolver)
    {
        this.readerResolver = resolver;
        bufferReader = resolver.bufferReader;
        bufferWriter = resolver.bufferWriter;
    }

    public abstract object Read();
    public T? Read<T>() => (T?)Read();

    public abstract void Write(object input);
}