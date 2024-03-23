using System;
using System.Text;
using XnbConverter.Utilities;

namespace XnbConverter.Readers;

public class ReaderResolver
{
    // private readonly string _typeIndex;
    private readonly List<int> typeIndex;
    
    private readonly List<string> _typeName;

    /**
     * 用于使用读取器读取XNB类型的类
     * @class
     */
    private readonly BaseReader[] readerArr;

    public BufferReader bufferReader;
    public BufferWriter bufferWriter;

    /**
     * 创建ReaderResolver的新实例
     * @constructor
     * @param {List
     * <BaseReader>} readers BaseReader的数组
     */
    public ReaderResolver()
    {
    }

    public ReaderResolver(BaseReader[] readerArr, BufferWriter bufferWriter, List<string> typeName, List<int> typeIndex)
        : this(readerArr, null, bufferWriter, typeName, typeIndex)
    {
    }

    public ReaderResolver(BaseReader[] readerArr, BufferReader bufferReader, List<string> typeName, List<int> typeIndex)
        : this(readerArr, bufferReader, null, typeName, typeIndex)
    {
    }


    public ReaderResolver(BaseReader[] readerArr, Type[] names, BufferReader bufferReader, BufferWriter bufferWriter)
    {
        this.readerArr = readerArr;
        this.bufferReader = bufferReader;
        this.bufferWriter = bufferWriter;
        typeIndex = new List<int>(readerArr.Length);
        _typeName = new List<string>(readerArr.Length);
        for (var i = 0; i < readerArr.Length; i++)
        {
            typeIndex.Add(i);
            _typeName.Add(names[i].ToString());
        }

        Init();
    }

    public ReaderResolver(BaseReader[] readerArr, BufferReader bufferReader, BufferWriter bufferWriter,
        List<string> typeName, List<int> typeIndex)
    {
        this.readerArr = readerArr;
        this.bufferReader = bufferReader;
        this.bufferWriter = bufferWriter;
        _typeName = typeName;
        this.typeIndex = typeIndex;
        Init();
    }

    private void Init()
    {
        foreach (var t in readerArr)
        {
            t.Init(this);
        }
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
            throw new XnbError(Helpers.I18N["ReaderResolver.1"], index);
        // 使用选定的读取器读取缓冲区
        return readerArr[index].Read();
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
            throw new XnbError(Helpers.I18N["ReaderResolver.1"], index);
        // 使用选定的读取器读取缓冲区
        return readerArr[index].Read();
    }

    public object ReadValue(int i)
    {
        return readerArr[i].Read();
    }

    public int GetIndex(Type t)
    {
        int index = _typeName.IndexOf(t.ToString());
        if (index == -1 || index > typeIndex.Count)
        {
            throw new ArgumentException();
        }
        return typeIndex[index];
    }

    /**
     * 将此读取器的索引写入缓冲区
     * @param {BufferWriter} buffer
     * @param {ReaderResolver} resolver
     */
    public void Write(int i, object item)
    {
        if (i < 0 || i > readerArr.Length)
            throw new XnbError(Helpers.I18N["ReaderResolver.2"], i);
        //写入读取器的索引
        bufferWriter.Write7BitNumber(i + 1);
        readerArr[i].Write(item);
    }

    public void Write_Null(int i, object? item)
    {
        if (i > readerArr.Length)
            throw new XnbError(Helpers.I18N["ReaderResolver.2"], i);
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