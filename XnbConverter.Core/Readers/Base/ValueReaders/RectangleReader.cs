using XnbConverter.Entity.Mono;
using Rectangle = XnbConverter.Entity.Mono.Rectangle;

namespace XnbConverter.Readers.Base.ValueReaders;

public class RectangleReader : BaseReader
{
	public override void Init(ReaderResolver resolver)
	{
		bufferReader = resolver.bufferReader;
		bufferWriter = resolver.bufferWriter;
	}

	public override object Read()
	{
		Rectangle rectangle = default(Rectangle);
		rectangle.X = bufferReader.ReadInt32();
		rectangle.Y = bufferReader.ReadInt32();
		rectangle.Width = bufferReader.ReadInt32();
		rectangle.Height = bufferReader.ReadInt32();
		return rectangle;
	}

	public override void Write(object content)
	{
		Rectangle rectangle = (Rectangle)content;
		bufferWriter.WriteInt32(rectangle.X);
		bufferWriter.WriteInt32(rectangle.Y);
		bufferWriter.WriteInt32(rectangle.Width);
		bufferWriter.WriteInt32(rectangle.Height);
	}

	public override bool IsValueType()
	{
		return true;
	}
}
