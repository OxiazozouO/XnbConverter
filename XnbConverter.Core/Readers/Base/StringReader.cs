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
		// 走 ReadOnly(span) 直解：不拷贝字节数组，且长度 0 时天然得到空串
		//（不能用 ReadString(0)，那个值会走"读到 0 字节为止"的分支）
		return Encoding.Default.GetString(bufferReader.ReadOnly(bufferReader.Read7BitNumber()));
	}

	public string ReadBy7Bit()
	{
		return Encoding.Default.GetString(bufferReader.ReadOnly(bufferReader.Read7BitNumber()));
	}

	public string ReadByInt32()
	{
		int count = bufferReader.ReadInt32();
		return Encoding.Default.GetString(bufferReader.Read(count));
	}

	public static string ReadValueBy7Bit(BufferReader bufferReader)
	{
		return Encoding.Default.GetString(bufferReader.ReadOnly(bufferReader.Read7BitNumber()));
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

}
