namespace XnbConverter.Utilities;

public class Extensions
{
}

public class XnbError : Exception
{
    public XnbError(string message) : base(message)
    {
    }

    public XnbError(string message, params object[] str)
        : base(string.Format(message, str))
    {
    }

    public XnbError(Exception ex, string message, params object[] str)
        : base(string.Format(message, str) + '\n' + ex.StackTrace)
    {
    }
}