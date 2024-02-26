namespace XnbConverter.Readers.Base;

/**
 * Dictionary Reader
 * @class
 * @extends BaseReader
 */
public class DictionaryReader<TK, TV, K, V> : BaseReader where TK : BaseReader, new() where TV : BaseReader, new()
{
    private int _keyReader;
    private int _valueReader;
    private bool _bK;
    private bool _bV;

    public override Type GetResultType()
    {
        return typeof(Dictionary<K, V>);
    }

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        _bK = new TK().IsValueType();
        _bV = new TV().IsValueType();
        _keyReader = readerResolver.GetIndex(typeof(TK));
        _valueReader = readerResolver.GetIndex(typeof(TV));
    }

    public override Dictionary<K, V> Read()
    {
        // 创建要返回的字典
        var dictionary = new Dictionary<K, V>();

        // 读取字典的大小
        var size = bufferReader.ReadUInt32();

        // 循环读取字典的数据
        while (size-- > 0)
        {
            // 获取键
            var key = _bK ? readerResolver.ReadValue<K>(_keyReader) : readerResolver.Read<K>();
            // 获取值
            var value = _bV ? readerResolver.ReadValue<V>(_valueReader) : readerResolver.Read<V>();

            // 将键值对添加到字典中
            dictionary.Add(key, value);
        }

        // 返回字典对象
        return dictionary;
    }

    public override void Write(object input)
    {
        // 写入字典的条目数
        var dictionary = (Dictionary<K, V>)input;
        bufferWriter.WriteUInt32((uint)dictionary.Count);
        // 遍历字典的条目
        foreach (var (key, value) in dictionary)
        {
            // 写入键
            if (_bK)
                readerResolver.WriteValue(_keyReader, key);
            else
                readerResolver.Write(_keyReader, key);

            if (_bV)
                readerResolver.WriteValue(_keyReader, value);
            else
                readerResolver.Write(_valueReader, value);
        }
    }

    public override bool IsValueType()
    {
        return false;
    }
}