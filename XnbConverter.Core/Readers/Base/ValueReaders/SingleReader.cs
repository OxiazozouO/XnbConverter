namespace XnbConverter.Readers.Base.ValueReaders;

public class SingleReader : BaseReader
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
		return bufferReader.ReadSingle();
	}

	public override void Write(object content)
	{
		float number = (float)content;
		bufferWriter.WriteSingle(number);
	}
}
