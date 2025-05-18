using XnbConverter.Entity.Mono;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Readers.Mono;

public class XmlSourceReader : BaseReader
{
	public override void Init(ReaderResolver resolver)
	{
		bufferReader = resolver.bufferReader;
		bufferWriter = resolver.bufferWriter;
	}

	public override object Read()
	{
		string data = StringReader.ReadValueBy7Bit(bufferReader);
		return new XmlSource
		{
			Data = data
		};
	}

	public override void Write(object input)
	{
		XmlSource xmlSource = (XmlSource)input;
		StringReader.WriteValueBy7Bit(bufferWriter, xmlSource.Data);
	}

	public override bool IsValueType()
	{
		return false;
	}
}
