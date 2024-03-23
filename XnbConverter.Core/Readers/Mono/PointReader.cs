using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Mono;

public class PointReader : BaseReader
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
        var x = bufferReader.ReadInt32();
        var y = bufferReader.ReadInt32();
        return new Point { X = x, Y = y };
    }

    public override void Write(object content)
    {
        var input = (Point)content;

        bufferWriter.WriteInt32(input.X);
        bufferWriter.WriteInt32(input.Y);
    }
}