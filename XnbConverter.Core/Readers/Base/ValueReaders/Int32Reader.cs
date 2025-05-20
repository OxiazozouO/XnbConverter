namespace XnbConverter.Readers.Base.ValueReaders;

public class Int32Reader : BaseReader
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
		return bufferReader.ReadInt32();
	}

	public override void Write(object content)
	{
		int number = (int)content;
		bufferWriter.WriteInt32(number);
	}
}
