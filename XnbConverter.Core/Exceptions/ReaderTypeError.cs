namespace XnbConverter.Exceptions;

public class ReaderTypeError : XnbError
{
    public ReaderTypeError(string message) : base(message)
    {
    }

    public ReaderTypeError(string message, params object[] str) : base(message, str)
    {
    }
}