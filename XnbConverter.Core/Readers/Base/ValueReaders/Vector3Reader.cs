using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Base.ValueReaders;

public class Vector3Reader : BaseReader
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

    public override object Read()
    {
        float x = bufferReader.ReadSingle();
        float y = bufferReader.ReadSingle();
        float z = bufferReader.ReadSingle();
        return new Vector3 { X = x, Y = y, Z = z };
    }

    public override void Write(object content)
    {
        var input = (Vector3)content;

        bufferWriter.WriteSingle(input.X);
        bufferWriter.WriteSingle(input.Y);
        bufferWriter.WriteSingle(input.Z);
    }
}