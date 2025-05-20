using System.Text;

namespace XnbConverter.Readers.Base;

public class StringReader : BaseReader
{
	public override void Init(ReaderResolver resolver)
	{
		bufferReader = resolver.bufferReader;
		bufferWriter = resolver.bufferWriter;
	}

	public override object Read()
	{
		int count = bufferReader.Read7BitNumber();
		return Encoding.Default.GetString(bufferReader.Read(count));
	}

	public string ReadBy7Bit()
	{
		int count = bufferReader.Read7BitNumber();
		return Encoding.Default.GetString(bufferReader.Read(count));
	}

	public string ReadByInt32()
	{
		int count = bufferReader.ReadInt32();
		return Encoding.Default.GetString(bufferReader.Read(count));
	}

	public static string ReadValueBy7Bit(BufferReader bufferReader)
	{
		int count = bufferReader.Read7BitNumber();
		return Encoding.Default.GetString(bufferReader.Read(count));
	}

	public override void Write(object content)
	{
		WriteBy7Bit(content);
	}

	public void WriteBy7Bit(object content)
	{
		string text = (string)content;
		byte[] array = new byte[text.Length * 4];
		int bytes = Encoding.UTF8.GetBytes(text, 0, text.Length, array, 0);
		bufferWriter.Write7BitNumber(bytes);
		bufferWriter.Write(array[..bytes]);
	}

	public void WriteByInt32(string input)
	{
		byte[] bytes = new byte[input.Length * 4];
		int bytes2 = Encoding.UTF8.GetBytes(input, 0, input.Length, bytes, 0);
		bufferWriter.WriteInt32(bytes2);
		bufferWriter.Write(bytes, 0, bytes2);
	}

	public static void WriteValueBy7Bit(BufferWriter bufferWriter, object content)
	{
		string text = (string)content;
		byte[] array = new byte[text.Length * 4];
		int bytes = Encoding.UTF8.GetBytes(text, 0, text.Length, array, 0);
		bufferWriter.Write7BitNumber(bytes);
		bufferWriter.Write(array[..bytes]);
	}

	public override bool IsValueType()
	{
		return false;
	}
}
