namespace XnbConverter.Readers.Base.ValueReaders;

public class DoubleReader : BaseReader
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

    /**
     * 从缓冲区读取Double值。
     * @param {BufferReader} buffer 缓冲区读取器
     * @returns {Number} Double值
     */
    public override object Read()
    {
        return bufferReader.ReadDouble();
    }

    /**
     * 将Double值写入缓冲区。
     * @param {BufferWriter} buffer 缓冲区写入器
     * @param {Mixed} data Double值
     * @param {ReaderResolver} 解析器
     */
    public override void Write(object content)
    {
        var input = (double)content;
        bufferWriter.WriteDouble(input);
    }
}