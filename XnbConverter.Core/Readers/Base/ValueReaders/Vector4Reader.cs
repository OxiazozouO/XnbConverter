using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Base.ValueReaders;

public class Vector4Reader : BaseReader
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
		Vector4 vector = new Vector4();
		vector.X = bufferReader.ReadSingle();
		vector.Y = bufferReader.ReadSingle();
		vector.Z = bufferReader.ReadSingle();
		vector.W = bufferReader.ReadSingle();
		return vector;
	}

	public override void Write(object content)
	{
		Vector4 vector = (Vector4)content;
		bufferWriter.WriteSingle(vector.X);
		bufferWriter.WriteSingle(vector.Y);
		bufferWriter.WriteSingle(vector.Z);
		bufferWriter.WriteSingle(vector.W);
	}
}
