﻿namespace XnbConverter.Readers.Base;

/**
 * Array Reader
 * @class
 * @extends BaseReader
 */
public class ArrayReader<T> : BaseReader where T : BaseReader, new()
{
    private int reader;
    private bool flag;

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        flag = new T().IsValueType();
        reader = readerResolver.GetIndex(typeof(T));
    }

    public override List<T> Read()
    {
        // 读取数组中的元素数量
        var size = bufferReader.ReadUInt32();
        // 创建本地数组
        List<T> array = new();

        // 循环size次读取数组元素
        if (flag)
        {
            for (var i = 0; i < size; i++)
            {
                // 从缓冲区获取值
                var value = readerResolver.ReadValue(reader);
                // 将值添加到本地数组
                array.Add((T)value);
            }
        }
        else
        {
            for (var i = 0; i < size; i++)
            {
                // 从缓冲区获取值
                var value = readerResolver.Read(reader);
                // 将值添加到本地数组
                array.Add((T)value);
            }
        }

        // 返回数组
        return array;
    }

    public override void Write(object content)
    {
        throw new NotImplementedException();
        var input = (List<T>)content;

        // 写入数组的元素数量
        bufferWriter.WriteUInt32((uint)input.Count);

        // 循环遍历数组以写入数组内容
        if (flag)
            foreach (var item in input)
                readerResolver.Write(reader, item);

        else
            foreach (var item in input)
                readerResolver.WriteValue(reader, item);
    }

    public override bool IsValueType()
    {
        return false;
    }
}