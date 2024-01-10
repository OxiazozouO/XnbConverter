namespace XnbConverter.Readers.Base.ValueReaders;
/**
 * Boolean Reader
 * @class
 * @extends BaseReader
 */
public class BooleanReader : BaseReader
{
    public override bool IsValueType()
    {
        return true;
    }

    public override void Init(ReaderResolver readerResolver)
    {
        this.bufferReader = readerResolver.bufferReader;
        this.bufferWriter = readerResolver.bufferWriter;
    }

    /**
     * 从缓冲区读取布尔值。
     * @param {BufferReader} buffer 缓冲区读取器
     * @returns {Boolean} 布尔值
     */
    public override object Read()
    {
        byte value = bufferReader.ReadByte();
        return value != 0;
    }
    
    /**
     * 将布尔值写入缓冲区。
     * @param {BufferWriter} buffer 缓冲区写入器
     * @param {Mixed} data 布尔值
     * @param {ReaderResolver} 解析器
     */
    public override void Write(object input)
    {
        bufferWriter.WriteByte((byte)((bool)input ? 1 : 0));
    }
}