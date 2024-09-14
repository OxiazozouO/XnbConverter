using XnbConverter.Entity.Mono;

namespace XnbConverter.Readers.Base.ValueReaders;

public class Vector3Reader : BaseReader
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
		float z = bufferReader.ReadSingle();
		Vector3 vector = new Vector3();
		vector.X = x;
		vector.Y = y;
		vector.Z = z;
		return vector;
	}

	public override void Write(object content)
	{
		Vector3 vector = (Vector3)content;
		bufferWriter.WriteSingle(vector.X);
		bufferWriter.WriteSingle(vector.Y);
		bufferWriter.WriteSingle(vector.Z);
	}
}
