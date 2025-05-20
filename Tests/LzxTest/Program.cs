using LzxTest;

public class Program
{
    public static void Main()
    {
        var reader = BufferReader.FormFile("1.png");
        Lzx.Decompress(reader, 0, reader.Buffer.Length);
        File.WriteAllBytes("2.bin", reader.Buffer);
        reader = BufferReader.FormFile("2.bin");
        
    }
}