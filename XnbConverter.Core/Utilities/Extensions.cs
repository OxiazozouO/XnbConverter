namespace XnbConverter.Utilities;

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

public class ReaderTypeError : XnbError
{
    public ReaderTypeError(string message) : base(message)
    {
    }

    public ReaderTypeError(string message, params object[] str)
        : base(message, str)
    {
    }

    public ReaderTypeError(Exception ex, string message, params object[] str)
        : base(message, str)
    {
    }
}


public class TbinError : XnbError
{
    public TbinError(string message) : base(message)
    {
    }

    public TbinError(string message, params object[] str)
        : base(message, str)
    {
    }

    public TbinError(Exception ex, string message, params object[] str)
        : base(message, str)
    {
    }
}