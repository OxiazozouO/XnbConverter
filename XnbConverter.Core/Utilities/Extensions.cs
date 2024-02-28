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
}