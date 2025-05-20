using Point = XnbConverter.Entity.Mono.Point;

namespace XnbConverter.Readers.Mono;

public class PointReader : BaseReader
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
		Point point = default(Point);
		point.X = x;
		point.Y = y;
		return point;
	}

	public override void Write(object content)
	{
		Point point = (Point)content;
		bufferWriter.WriteInt32(point.X);
		bufferWriter.WriteInt32(point.Y);
	}
}
