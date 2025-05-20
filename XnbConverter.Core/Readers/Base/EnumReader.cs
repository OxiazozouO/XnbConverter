using XnbConverter.Exceptions;

namespace XnbConverter.Readers.Base;

public class EnumReader<T> : BaseReader where T : Enum
{
	private bool b;

	private int reader;

	private Type _buildType;

	private bool _isFlags;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		reader = resolver.GetIndex(Enum.GetUnderlyingType(typeof(T)));
		_buildType = typeof(T);
		_isFlags = _buildType.GetCustomAttributes(typeof(FlagsAttribute), inherit: false).Length != 0;
	}

	public override bool IsValueType()
	{
		return true;
	}

	public override object Read()
	{
		object obj = readerResolver.ReadValue(reader);
		if (Enum.IsDefined(_buildType, obj))
		{
			return (T)obj;
		}
		if (_isFlags)
		{
			return (T)Enum.Parse(_buildType, obj.ToString());
		}
		throw new XnbError(Error.EnumReader_1, obj);
	}

	public override void Write(object content)
	{
		T val = (T)content;
		if (Enum.IsDefined(typeof(T), val) || _isFlags)
		{
			readerResolver.WriteValue(reader, val);
			return;
		}
		throw new XnbError(Error.EnumReader_1, val);
	}
}
