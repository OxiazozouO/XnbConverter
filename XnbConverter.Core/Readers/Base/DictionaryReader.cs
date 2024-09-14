using System.Collections.Generic;

namespace XnbConverter.Readers.Base;

public class DictionaryReader<TK, TV, K, V> : BaseReader where TK : BaseReader, new() where TV : BaseReader, new()
{
	private bool _bK;

	private bool _bV;

	private int _keyReader;

	private int _valueReader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		_bK = new TK().IsValueType();
		_bV = new TV().IsValueType();
		_keyReader = resolver.GetIndex(typeof(TK));
		_valueReader = resolver.GetIndex(typeof(TV));
	}

	public override object Read()
	{
		Dictionary<K, V> dictionary = new Dictionary<K, V>();
		uint num = bufferReader.ReadUInt32();
		while (num-- != 0)
		{
			object obj = (_bK ? ((object)readerResolver.ReadValue<K>(_keyReader)) : readerResolver.Read(_keyReader));
			object obj2 = (_bV ? ((object)readerResolver.ReadValue<V>(_valueReader)) : readerResolver.Read(_valueReader));
			dictionary.Add((K)obj, (V)obj2);
		}
		return dictionary;
	}

	public override void Write(object input)
	{
		Dictionary<K, V> dictionary = (Dictionary<K, V>)input;
		bufferWriter.WriteUInt32((uint)dictionary.Count);
		foreach (var (val3, val4) in dictionary)
		{
			if (_bK)
			{
				readerResolver.WriteValue(_keyReader, val3);
			}
			else
			{
				readerResolver.Write(_keyReader, val3);
			}
			if (_bV)
			{
				readerResolver.WriteValue(_valueReader, val4);
			}
			else
			{
				readerResolver.Write(_valueReader, val4);
			}
		}
	}

	public override bool IsValueType()
	{
		return false;
	}
}
