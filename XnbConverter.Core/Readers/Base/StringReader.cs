using static System.Text.Encoding;

namespace XnbConverter.Readers.Base;

public class StringReader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        this.bufferReader = readerResolver.bufferReader;
        this.bufferWriter = readerResolver.bufferWriter;
    }

    public override string Read()
    {
        // 读取字符串的长度
        int length = bufferReader.Read7BitNumber();
        // 读取UTF-8编码的字符串
        return Default.GetString(bufferReader.Read(length));
    }
    
    public string ReadBy7Bit()
    {
        // 读取字符串的长度
        int length = bufferReader.Read7BitNumber();
        // 读取UTF-8编码的字符串
        return Default.GetString(bufferReader.Read(length));
    }
    
    public string ReadByInt32()
    {
        // 读取字符串的长度
        int length = bufferReader.ReadInt32();
        // 读取UTF-8编码的字符串
        return Default.GetString(bufferReader.Read(length));
    }
    
    public static string ReadValueBy7Bit(BufferReader bufferReader)
    {
        // 读取字符串的长度
        int length = bufferReader.Read7BitNumber();
        // 读取UTF-8编码的字符串
        return Default.GetString(bufferReader.Read(length));
    }

    public override void Write(object content)
    {
        WriteBy7Bit(content);
    }
    
    public void WriteBy7Bit(object content)
    {
        var input = (string)content;
        // 创建一个字符串缓冲区，每个UTF8字符额外占用4个字节
        byte[] stringBuffer = new byte[input.Length * 4];
        // 将字符串写入缓冲区并获取大小
        int size = UTF8.GetBytes(input, 0, input.Length, stringBuffer, 0);
        // 写入字符串的长度
        bufferWriter.Write7BitNumber(size);
        // 写入字符串
        bufferWriter.Write(stringBuffer[..size]);
    }
    
    public void WriteByInt32(string input)
    {
        // 创建一个字符串缓冲区，每个UTF8字符额外占用4个字节
        byte[] stringBuffer = new byte[input.Length * 4];
        // 将字符串写入缓冲区并获取大小
        int size = UTF8.GetBytes(input, 0, input.Length, stringBuffer, 0);
        // 写入字符串的长度
        bufferWriter.WriteInt32(size);
        // 写入字符串
        bufferWriter.Write(stringBuffer,0,size);
    }
    
    public static void WriteValueBy7Bit(BufferWriter bufferWriter, object content)
    {
        var input = (string)content;
        // 创建一个字符串缓冲区，每个UTF8字符额外占用4个字节
        byte[] stringBuffer = new byte[input.Length * 4];
        // 将字符串写入缓冲区并获取大小
        int size = UTF8.GetBytes(input, 0, input.Length, stringBuffer, 0);
        // 写入字符串的长度
        bufferWriter.Write7BitNumber(size);
        // 写入字符串
        bufferWriter.Write(stringBuffer[..size]);
    }
    

    public override bool IsValueType()
    {
        return false;
    }
}