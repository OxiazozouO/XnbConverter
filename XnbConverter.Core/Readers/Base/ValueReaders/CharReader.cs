using System.Text;

namespace XnbConverter.Readers.Base.ValueReaders;

/**
 * Char Reader
 * @class
 * @extends BaseReader
 */
public class CharReader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        bufferReader = readerResolver.bufferReader;
        bufferWriter = readerResolver.bufferWriter;
    }

    public override bool IsValueType()
    {
        return true;
    }

    /**
     * 从缓冲区读取字符。
     * @param {BufferReader} buffer 缓冲区读取器
     * @returns {String} 字符
     */
    public override object Read()
    {
        var b = bufferReader.PeekByte();
        var charSize = GetCharSize(b);
        var charBytes = bufferReader.Read(charSize);
        var c = Encoding.UTF8.GetChars(charBytes)[0];
        return c;
    }

    /**
     * 将字符写入缓冲区。
     * @param {BufferWriter} buffer 缓冲区写入器
     * @param {Mixed} data 字符
     * @param {ReaderResolver} 解析器
     */
    public override void Write(object content)
    {
        var input = content.ToString();
        var buf = Encoding.Default.GetBytes(input);
        bufferWriter.Write(buf);
    }

    /**
     * 获取特定字符的大小，对于一些占用多个字节的特殊字符。
     * @param {Number} byte 字节值
     * @returns {Number} 字符大小
     */
    private static int GetCharSize(byte b)
    {
        return (int)(((0xE5000000 >> ((b >> 3) & 0x1e)) & 3) + 1);
    }
}