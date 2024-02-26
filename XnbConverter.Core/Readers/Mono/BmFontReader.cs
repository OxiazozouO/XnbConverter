using XnbConverter.Entity.Mono;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Readers.Mono;

public class BmFontReader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        bufferReader = readerResolver.bufferReader;
        bufferWriter = readerResolver.bufferWriter;
    }

    public override BmFont Read()
    {
        var xml = StringReader.ReadValueBy7Bit(bufferReader);
        return new BmFont() { Data = xml };
    }

    public override void Write(object input)
    {
        var bmFont = (BmFont)input;
        StringReader.WriteValueBy7Bit(bufferWriter, bmFont.Data);
    }

    public override bool IsValueType()
    {
        return false;
    }

    public override Type GetResultType()
    {
        return typeof(BmFont);
    }
}