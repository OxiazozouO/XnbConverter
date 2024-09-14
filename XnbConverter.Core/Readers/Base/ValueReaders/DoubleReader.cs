namespace XnbConverter.Readers.Base.ValueReaders;

public class DoubleReader : BaseReader
{
	public override void Init(ReaderResolver resolver)
	{
		bufferReader = resolver.bufferReader;
		bufferWriter = resolver.bufferWriter;
	}

	public override bool IsValueType()
	{
		return true;
	}

	public override object Read()
	{
		return bufferReader.ReadDouble();
	}

	public override void Write(object content)
	{
		double number = (double)content;
		bufferWriter.WriteDouble(number);
	}
}
