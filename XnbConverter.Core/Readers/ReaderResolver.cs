using XnbConverter.Utilities;

namespace XnbConverter.Readers;

public class ReaderResolver
{
    /**
     * 用于使用读取器读取XNB类型的类
     * @class
     */
    private BaseReader[] readerArr;
    private string[] readerName;
    public BufferWriter bufferWriter;
    public BufferReader bufferReader;

    /**
     * 创建ReaderResolver的新实例
     * @constructor
     * @param {List<BaseReader>} readers BaseReader的数组
     */
    
    public ReaderResolver(){}
    
    public ReaderResolver(BaseReader[] readerArr, BufferWriter bufferWriter)
        : this(readerArr, null, bufferWriter)
    {
    }
    
    public ReaderResolver(BaseReader[] readerArr, BufferReader bufferReader)
        : this(readerArr, bufferReader, null)
    {
    }
    
    
    public ReaderResolver(BaseReader[] readerArr,  BufferReader bufferReader, BufferWriter bufferWriter)
    {
        this.readerArr = readerArr;
        this.bufferReader = bufferReader;
        this.bufferWriter = bufferWriter;
        Init();
    }
    
    private void Init()
    {
        readerName = new string[readerArr.Length];
        for (var index = 0; index < readerArr.Length; index++)
            readerName[index] = readerArr[index].ToString();

        foreach (var t in readerArr)
            t.Init(this);
    }

    /**
     * 读取XNB文件内容
     * @method read
     * @public
     * @param {BufferReader} buffer 要读取的缓冲区
     * @returns {object} 返回由读取器解析的对象
     */
    public T Read<T>()
    {
        // 读取要使用的读取器的索引
        int index = bufferReader.Read7BitNumber() - 1;
        if (index < 0 || index >= readerArr.Length)
            throw new XnbError("无效的读取器索引 {0}", index);
        var a = readerArr[index];
        // 使用选定的读取器读取缓冲区
        return (T)a.Read();
    }
    
    public T? Read_Null<T>()
    {
        // 读取要使用的读取器的索引
        var index = bufferReader.Read7BitNumber() - 1;
        if (index < 0)
            return default;
            
        if (index >= readerArr.Length)
            throw new XnbError("无效的读取器索引 {0}", index);
        var a = readerArr[index];
        // 使用选定的读取器读取缓冲区
        return (T?)a.Read();
    }
    
    public T ReadValue<T>(int i)
    {
        return (T)readerArr[i].Read();
    }

    /**
     * 写入XNB文件内容
     * @param {BufferWriter} buffer 要写入的缓冲区
     * @param {object} content 要写入缓冲区的内容
     */
    public void Write(object content)
    {
        Write(0, content);
    }

    public int GetIndex<T>()
    {
        var readerStr = typeof(T).ToString();
        for (var i = 0; i < readerName.Length; i++)
        {
            if (readerStr != readerName[i]) continue;
            return i;
        }
        return -1;
    }
    /**
     * 将此读取器的索引写入缓冲区
     * @param {BufferWriter} buffer
     * @param {ReaderResolver} resolver
     */
    
    public void Write(int i, object item)
    {
        if(i  < 0 || i > readerArr.Length)
            throw new XnbError("找不到读取器, 索引：{0}", i);
        //写入读取器的索引
        bufferWriter.Write7BitNumber(i + 1);
        readerArr[i].Write(item);
    }
    
    public void Write_Null(int i, object? item)
    {
        if(i > readerArr.Length)
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