using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Base.ValueReaders;

/**
 * Vector2 Reader Vector2读取器
 * @class
 * @extends BaseReader
 */
public class IntVector2Reader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        this.bufferReader = readerResolver.bufferReader;
        this.bufferWriter = readerResolver.bufferWriter;
    }
    public override bool IsValueType()
    {
        return true;
    }
    /**
     * 从缓冲区读取Vector2。
     * @param {BufferReader} buffer 缓冲区读取器
     * @returns {object} Vector2对象
     */
    public override object Read()
    {
        int x = bufferReader.ReadInt32();
        int y = bufferReader.ReadInt32();

        return new IntVector2{ X = x, Y = y };
    }

    public override void Write(object content)
    {
        var input = (IntVector2)content;
        
        bufferWriter.WriteInt32(input.X);
        bufferWriter.WriteInt32(input.Y);
    }
}