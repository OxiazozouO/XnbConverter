namespace XnbConverter.Readers.Base.ValueReaders;

public class UInt32Reader : BaseReader
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
		return bufferReader.ReadUInt32();
	}

	public override void Write(object content)
	{
		uint number = (uint)content;
		bufferWriter.WriteUInt32(number);
	}
}
