namespace XnbConverter.Exceptions;

public class TbinError : XnbError
{
	public TbinError(string message) : base(message)
	{
	}

	public TbinError(string message, params object[] str) : base(message, str)
	{
	}
}
