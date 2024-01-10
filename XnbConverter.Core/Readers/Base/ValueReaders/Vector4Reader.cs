using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Base.ValueReaders;

public class Vector4Reader : BaseReader
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
        Vector4 result = new Vector4();
        
        result.X = bufferReader.ReadSingle();
        result.Y = bufferReader.ReadSingle();
        result.Z = bufferReader.ReadSingle();
        result.W = bufferReader.ReadSingle();

        return result;
    }

    public override void Write(object content)
    {
        var input = (Vector4)content;

        bufferWriter.WriteSingle(input.X);
        bufferWriter.WriteSingle(input.Y);
        bufferWriter.WriteSingle(input.Z);
        bufferWriter.WriteSingle(input.W);
    }
}