namespace XnbConverter.Readers.Base;

public class NullableReader<T, N> : BaseReader where T : BaseReader, new()
{
	private bool b;

	private int reader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		reader = resolver.GetIndex(typeof(T));
		b = new T().IsValueType();
	}

	public override object Read()
	{
		if (!bufferReader.ReadBoolean())
		{
			return null;
		}
		if (!b)
		{
			return readerResolver.Read_Null(reader);
		}
		return readerResolver.ReadValue(reader);
	}

	public override void Write(object input)
	{
		bool flag = input != null;
		bufferWriter.WriteByte((byte)(flag ? 1u : 0u));
		if (flag)
		{
			if (b)
			{
				readerResolver.WriteValue(reader, input);
			}
			else
			{
				readerResolver.Write(reader, input);
			}
		}
	}

	public override bool IsValueType()
	{
		return false;
	}
}
