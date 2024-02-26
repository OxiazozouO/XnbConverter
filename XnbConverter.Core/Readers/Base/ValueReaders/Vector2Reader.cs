using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Base.ValueReaders;

/**
 * Vector2 Reader Vector2读取器
 * @class
 * @extends BaseReader
 */
public class Vector2Reader : BaseReader
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
     * 从缓冲区读取Vector2。
     * @param {BufferReader} buffer 缓冲区读取器
     * @returns {object} Vector2对象
     */
    public override object Read()
    {
        var x = bufferReader.ReadSingle();
        var y = bufferReader.ReadSingle();

        return new Vector2 { X = x, Y = y };
    }

    public override void Write(object content)
    {
        var input = (Vector2)content;

        bufferWriter.WriteSingle(input.X);
        bufferWriter.WriteSingle(input.Y);
    }
}