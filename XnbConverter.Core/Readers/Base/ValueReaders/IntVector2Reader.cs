using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Base.ValueReaders;

public class IntVector2Reader : BaseReader
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
		int x = bufferReader.ReadInt32();
		int y = bufferReader.ReadInt32();
		IntVector2 intVector = default(IntVector2);
		intVector.X = x;
		intVector.Y = y;
		return intVector;
	}

	public override void Write(object content)
	{
		IntVector2 intVector = (IntVector2)content;
		bufferWriter.WriteInt32(intVector.X);
		bufferWriter.WriteInt32(intVector.Y);
	}
}
