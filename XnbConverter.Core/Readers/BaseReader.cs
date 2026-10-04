namespace XnbConverter.Readers;

public abstract class BaseReader
{
    protected BufferReader bufferReader;

    protected BufferWriter bufferWriter;

    protected ReaderResolver readerResolver;

    /// <summary>
    /// 该 reader 读的元素在 XNB 流里是否**不带类型标号**（即用 ReadValue 而非 Read）。
    /// 注意它不等于 <c>Type.IsValueType</c>：例如 <c>Nullable&lt;T&gt;</c> 在 .NET 里是值类型，
    /// 但 NullableReader 这里返回 false。默认 false，只有值类型 reader 需要重写。
    /// </summary>
    public virtual bool IsValueType()
    {
        return false;
    }

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