﻿namespace XnbConverter.Readers;
/**
* 所有读取器的基类。
* @abstract
* @class
 */
public abstract class BaseReader
{
    protected ReaderResolver readerResolver;
    protected BufferReader bufferReader;
    protected BufferWriter bufferWriter;
    
    /**
     * 返回类型是否通常需要特殊的读取器。
     * @public
     * @method
     * @returns {Boolean} 如果类型是原始类型，则返回true。
     */
    public abstract bool IsValueType();

    
    public virtual void Init(ReaderResolver readerResolver)
    {
        this.readerResolver = readerResolver;
        this.bufferReader = readerResolver.bufferReader;
        this.bufferWriter = readerResolver.bufferWriter;
    }
    
    protected void Init(string path)
    {
        Init(new ReaderResolver(){
            bufferReader = BufferReader.FormFile(path)
        });
    }

    /**
     * 根据类型读取器的规范读取缓冲区。
     * @public
     * @param {BufferReader} buffer 要读取的缓冲区。
     * @param {ReaderResolver} resolver 用于解析读取器的内容读取器。
     * @returns {mixed} 返回由类型读取器指定的类型。
     */
    public abstract object Read();

    protected static object Read(string path) => throw new NotImplementedException();
    

    /**
     * 写入缓冲区。
     * @param {BufferWriter} buffer 要写入的缓冲区
     * @param {Mixed} data 要解析并写入缓冲区的数据
     * @param {ReaderResolver} resolver 用于写入非原始类型的读取器解析器
     */
    public abstract void Write(object input);

    /**
     * 获取返回的数据类型
     * @param {BufferWriter} buffer
     * @param {ReaderResolver} resolver
     */
    public virtual Type? GetResultType()
    {
        return null;
    }
}