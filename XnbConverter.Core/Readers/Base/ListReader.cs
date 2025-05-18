namespace XnbConverter.Readers.Base;

public class ListReader<TK, K> : BaseReader where TK : BaseReader, new()
{
	private bool _bK;

	private int _reader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		_reader = resolver.GetIndex(typeof(K));
		_bK = new TK().IsValueType();
	}

	public override object Read()
	{
		uint num = bufferReader.ReadUInt32();
		List<K> list = new List<K>();
		if (_bK)
		{
			while (num-- != 0)
			{
				object obj = readerResolver.ReadValue(_reader);
				list.Add((K)obj);
			}
		}
		else
		{
			while (num-- != 0)
			{
				object obj = readerResolver.Read(_reader);
				list.Add((K)obj);
			}
		}
		return list;
	}

	public override void Write(object content)
	{
		List<K> list = (List<K>)content;
		bufferWriter.WriteUInt32((uint)list.Count);
		if (_bK)
		{
			foreach (K item in list)
			{
				readerResolver.WriteValue(_reader, item);
			}
			return;
		}
		foreach (K item2 in list)
		{
			readerResolver.Write(_reader, item2);
		}
	}

	public override bool IsValueType()
	{
		return false;
	}
}
