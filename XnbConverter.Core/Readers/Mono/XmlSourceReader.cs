using System;
using XnbConverter.Entity.Mono;
using XnbConverter.Readers.Base;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Readers.Mono;

public class XmlSourceReader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        bufferReader = readerResolver.bufferReader;
        bufferWriter = readerResolver.bufferWriter;
    }

    public override XmlSource Read()
    {
        var xml = StringReader.ReadValueBy7Bit(bufferReader);
        return new XmlSource { Data = xml };
    }

    public override void Write(object input)
    {
        var bmFont = (XmlSource)input;
        StringReader.WriteValueBy7Bit(bufferWriter, bmFont.Data);
    }

    public override bool IsValueType()
    {
        return false;
    }

    public override Type GetResultType()
    {
        return typeof(XmlSource);
    }
}