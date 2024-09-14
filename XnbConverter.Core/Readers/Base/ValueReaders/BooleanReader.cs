namespace XnbConverter.Readers.Base.ValueReaders;

public class BooleanReader : BaseReader
{
	public override bool IsValueType()
	{
		return true;
	}

	public override void Init(ReaderResolver resolver)
	{
		bufferReader = resolver.bufferReader;
		bufferWriter = resolver.bufferWriter;
	}

	public override object Read()
	{
		return bufferReader.ReadByte() != 0;
	}

	public override void Write(object input)
	{
		bufferWriter.WriteByte((byte)(((bool)input) ? 1u : 0u));
	}
}
