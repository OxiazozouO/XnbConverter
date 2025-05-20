namespace XnbConverter.Readers.Base;

public class ArrayReader<V> : BaseReader where V : new()
{
	private bool flag;

	private int reader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		Type typeFromHandle = typeof(V);
		flag = typeFromHandle.IsValueType;
		reader = resolver.GetIndex(typeFromHandle);
	}

	public override object Read()
	{
		uint num = bufferReader.ReadUInt32();
		List<V> list = new List<V>();
		if (flag)
		{
			while (num-- != 0)
			{
				object obj = readerResolver.ReadValue(reader);
				list.Add((V)obj);
			}
		}
		else
		{
			while (num-- != 0)
			{
				object obj2 = readerResolver.Read_Null(reader);
				list.Add((V)obj2);
			}
		}
		return list.ToArray();
	}

	public override void Write(object content)
	{
		V[] array = (V[])content;
		bufferWriter.WriteUInt32((uint)array.Length);
		if (flag)
		{
			V[] array2 = array;
			foreach (V val in array2)
			{
				readerResolver.WriteValue(reader, val);
			}
		}
		else
		{
			V[] array2 = array;
			foreach (V val2 in array2)
			{
				readerResolver.Write_Null(reader, val2);
			}
		}
	}

	public override bool IsValueType()
	{
		return false;
	}
}
