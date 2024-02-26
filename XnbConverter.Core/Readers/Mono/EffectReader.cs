using System.Text;
using XnbConverter.Entity.Mono;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Readers.Mono;

public class EffectReader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        bufferReader = readerResolver.bufferReader;
        bufferWriter = readerResolver.bufferWriter;
    }

    public override Effect Read()
    {
        var result = new Effect();
        var size = bufferReader.ReadUInt32();
        var bytecode = bufferReader.Read((int)size);

        return new Effect { Data = bytecode };
    }

    public override void Write(object content)
    {
        var input = (Effect)content;

        bufferWriter.WriteUInt32((uint)input.Data.Length);
        bufferWriter.Write(input.Data);
    }

    public override bool IsValueType()
    {
        return false;
    }

    public override Type GetResultType()
    {
        return typeof(Effect);
    }
}