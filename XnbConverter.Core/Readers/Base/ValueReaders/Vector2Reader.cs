using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Base.ValueReaders;

public class Vector2Reader : BaseReader
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
		float x = bufferReader.ReadSingle();
		float y = bufferReader.ReadSingle();
		Vector2 vector = default(Vector2);
		vector.X = x;
		vector.Y = y;
		return vector;
	}

	public override void Write(object content)
	{
		Vector2 vector = (Vector2)content;
		bufferWriter.WriteSingle(vector.X);
		bufferWriter.WriteSingle(vector.Y);
	}
}
