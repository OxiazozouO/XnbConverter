namespace XnbConverter.Readers.Base.ValueReaders;

/**
 * Int32 Reader
 * @class
 * @extends BaseReader
 */
public class Int32Reader : BaseReader
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
     * 从缓冲区读取Int32值。
     * @param {BufferReader} buffer 缓冲区读取器
     * @returns {Number} Int32值
     */
    public override object Read()
    {
        return bufferReader.ReadInt32();
    }

    /**
     * 将Int32值写入缓冲区。
     * @param {BufferWriter} buffer 缓冲区写入器
     * @param {Number} content Int32值
     * @param {ReaderResolver} resolver 解析器
     */
    public override void Write(object content)
    {
        var input = (int)content;
        bufferWriter.WriteInt32(input);
    }
}