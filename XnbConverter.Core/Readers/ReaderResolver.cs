using System.Text;
using XnbConverter.Utilities;

namespace XnbConverter.Readers;

public class ReaderResolver
{
    /**
     * 用于使用读取器读取XNB类型的类
     * @class
     */
    private BaseReader[] readerArr;

    private readonly string _typeIndex;
    public BufferWriter bufferWriter;
    public BufferReader bufferReader;

    /**
     * 创建ReaderResolver的新实例
     * @constructor
     * @param {List<BaseReader>} readers BaseReader的数组
     */
    public ReaderResolver()
    {
    }

    public ReaderResolver(BaseReader[] readerArr, BufferWriter bufferWriter, string typeIndex)
        : this(readerArr, null, bufferWriter, typeIndex)
    {
    }

    public ReaderResolver(BaseReader[] readerArr, BufferReader bufferReader, string typeIndex)
        : this(readerArr, bufferReader, null, typeIndex)
    {
    }


    public ReaderResolver(BaseReader[] readerArr, BufferReader bufferReader, BufferWriter bufferWriter)
    {
        this.readerArr = readerArr;
        this.bufferReader = bufferReader;
        this.bufferWriter = bufferWriter;
        var sb = new StringBuilder();
        for (var i = 0; i < readerArr.Length; i++) sb.Append(i).Append('@').Append(readerArr[i]).Append('@');

        _typeIndex = sb.ToString();
        Init();
    }

    public ReaderResolver(BaseReader[] readerArr, BufferReader bufferReader, BufferWriter bufferWriter,
        string typeIndex)
    {
        this.readerArr = readerArr;
        this.bufferReader = bufferReader;
        this.bufferWriter = bufferWriter;
        _typeIndex = typeIndex;
        Init();
    }

    private void Init()
    {
        foreach (var t in readerArr) t.Init(this);
    }

    /**
     * 读取XNB文件内容
     * @method read
     * @public
     * @param {BufferReader} buffer 要读取的缓冲区
     * @returns {object} 返回由读取器解析的对象
     */
    public object Read(int i)
    {
        // 读取要使用的读取器的索引
        var index = bufferReader.Read7BitNumber() - 1;
        if (index != i)
            throw new XnbError("无效的读取器索引 {0}", index);
        var a = readerArr[index];
        // 使用选定的读取器读取缓冲区
        return a.Read();
    }

    public T ReadValue<T>(int i)
    {
        return (T)readerArr[i].Read();
    }

    public object Read_Null(int i)
    {
        // 读取要使用的读取器的索引
        var index = bufferReader.Read7BitNumber() - 1;
        if (index < 0)
            return default;
        if (index != i)
            throw new XnbError("无效的读取器索引 {0}", index);
        var a = readerArr[index];
        // 使用选定的读取器读取缓冲区
        return a.Read();
    }

    public object ReadValue(int i)
    {
        return readerArr[i].Read();
    }

    public int GetIndex(Type t)
    {
        var readerStr = '@' + t.ToString() + '@';
        var i = _typeIndex.AsSpan().IndexOf(readerStr);
        if (i == -1 || i > _typeIndex.Length) throw new AggregateException();
        return _typeIndex[i - 1] - '0';
    }

    /**
     * 将此读取器的索引写入缓冲区
     * @param {BufferWriter} buffer
     * @param {ReaderResolver} resolver
     */
    public void Write(int i, object item)
    {
        if (i < 0 || i > readerArr.Length)
            throw new XnbError("找不到读取器, 索引：{0}", i);
        //写入读取器的索引
        bufferWriter.Write7BitNumber(i + 1);
        readerArr[i].Write(item);
    }

    public void Write_Null(int i, object? item)
    {
        if (i > readerArr.Length)
            throw new XnbError("找不到读取器, 索引：{0}", i);
        if (i == -1 || item is null)
        {
            bufferWriter.Write7BitNumber(0);
            return;
        }

        //写入读取器的索引
        bufferWriter.Write7BitNumber(i + 1);
        readerArr[i].Write(item);
    }


    public void WriteValue(int i, object item)
    {
        readerArr[i].Write(item);
    }
}