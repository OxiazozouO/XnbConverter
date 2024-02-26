using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Base.ValueReaders;

/**
 * Rectangle Reader
 * @class
 * @extends BaseReader
 */
public class RectangleReader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        bufferReader = readerResolver.bufferReader;
        bufferWriter = readerResolver.bufferWriter;
    }

    public override Rect Read()
    {
        var result = new Rect();

        result.X = bufferReader.ReadInt32();
        result.Y = bufferReader.ReadInt32();
        result.Width = bufferReader.ReadInt32();
        result.Height = bufferReader.ReadInt32();

        return result;
    }

    public override void Write(object content)
    {
        var input = (Rect)content;

        bufferWriter.WriteInt32(input.X);
        bufferWriter.WriteInt32(input.Y);
        bufferWriter.WriteInt32(input.Width);
        bufferWriter.WriteInt32(input.Height);
    }

    public override bool IsValueType()
    {
        return true;
    }
}