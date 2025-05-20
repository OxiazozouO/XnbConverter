namespace XnbConverter.Exceptions;

public class LoadReaderError : XnbError
{
    public LoadReaderError(string message) : base(message)
    {
    }

    public LoadReaderError(string message, params object[] str) : base(message, str)
    {
    }
}