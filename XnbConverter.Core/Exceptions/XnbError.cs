namespace XnbConverter.Exceptions;

public class XnbError : Exception
{
	public XnbError(string message) : base(message)
	{
	}

	public XnbError(string message, params object[] str) : base(string.Format(message, str))
	{
	}
}
