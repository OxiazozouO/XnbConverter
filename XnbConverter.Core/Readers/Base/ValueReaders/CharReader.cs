using System.Text;

namespace XnbConverter.Readers.Base.ValueReaders;

public class CharReader : BaseReader
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
		int charSize = GetCharSize(bufferReader.PeekByte());
		byte[] bytes = bufferReader.Read(charSize);
		return Encoding.UTF8.GetChars(bytes)[0];
	}

	public override void Write(object content)
	{
		string s = content.ToString();
		byte[] bytes = Encoding.UTF8.GetBytes(s);   // 读用 UTF8，写也必须用 UTF8，否则非 ASCII 字符回不去
		bufferWriter.Write(bytes);
	}

	private static int GetCharSize(byte b)
	{
		return ((-452984832 >>> ((b >> 3) & 0x1E)) & 3) + 1;
	}
}
