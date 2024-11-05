namespace XnbConverter.Exceptions;

public class Texture2DReaderError : XnbError
{
    public Texture2DReaderError(string message) : base(message)
    {
    }
    
    public Texture2DReaderError(string message, params object[] str) : base(message, str)
    {
    }
}