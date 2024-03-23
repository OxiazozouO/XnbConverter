using System;
using System.Collections.Generic;

namespace XnbConverter.Readers.Base;

/**
 * List Reader
 * @class
 * @extends BaseReader
 */
public class ListReader<TK, K> : BaseReader where TK : BaseReader, new()
{
    private bool _bK;
    private int _reader;

    /**
     * 从缓冲区读取List。
     * @param {BufferReader} buffer 缓冲区读取器
     * @param {ReaderResolver} resolver 解析器
     * @returns {Array} 包含List的对象
     */
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        _reader = readerResolver.GetIndex(typeof(K));
        _bK = new TK().IsValueType();
    }
    public override List<K> Read()
    {
        var size = bufferReader.ReadUInt32();
        var list = new List<K>();
        object value;

        if (_bK)
        {
            while (size-- > 0)
            {
                value = readerResolver.ReadValue(_reader);
                list.Add((K)value);
            }
        }
        else
        {
            var index = 0;
            while (size-- > 0)
            {
                value = readerResolver.Read(_reader);
                list.Add((K)value);
            }
        }

        return list;
    }

    /// <summary>
    ///     将List写入缓冲区。
    /// </summary>
    /// <param name="bufferWriter">缓冲区写入器</param>
    /// <param name="input">包含List的对象</param>
    /// <param name="readerResolver">解析器</param>
    public override void Write(object content)
    {
        var input = (List<K>)content;
        bufferWriter.WriteUInt32((uint)input.Count);
        if (_bK)
            foreach (var t in input)
                readerResolver.WriteValue(_reader, t);
        else
            foreach (var t in input)
                readerResolver.Write(_reader, t);
    }

    /**
     * 返回类型是否通常需要特殊的读取器。
     * @public
     * @method
     * @returns {Boolean} 如果类型是原始类型，则返回true。
     */
    public override bool IsValueType()
    {
        return false;
    }
}