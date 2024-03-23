using System;
using System.Collections.Generic;

namespace XnbConverter.Readers.Base;

/**
 * Array Reader
 * @class
 * @extends BaseReader
 */
public class ArrayReader<V> : BaseReader where V : new()
{
    private bool flag;
    private int reader;

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        Type tmp = typeof(V);
        flag = tmp.IsValueType;
        reader = readerResolver.GetIndex(tmp);
    }

    public override V[] Read()
    {
        // 读取数组中的元素数量
        var size = bufferReader.ReadUInt32();
        // 创建本地数组
        List<V> array = new();

        // 循环size次读取数组元素
        if (flag)
            while (size --> 0)
            {
                // 从缓冲区获取值
                var value = readerResolver.ReadValue(reader);
                // 将值添加到本地数组
                array.Add((V)value);
            }
        else
            while (size --> 0)
            {
                // 从缓冲区获取值
                var value = readerResolver.Read_Null(reader);
                // 将值添加到本地数组
                array.Add((V)value);
            }

        // 返回数组
        return array.ToArray();
    }

    public override void Write(object content)
    {
        var input = (V[])content;

        // 写入数组的元素数量
        bufferWriter.WriteUInt32((uint)input.Length);

        // 循环遍历数组以写入数组内容
        if (flag)
        {
            foreach (var t in input)
            {
                readerResolver.WriteValue(reader, t);
            }
        }
        else
        {
            foreach (var t in input)
            {
                readerResolver.Write_Null(reader, t);
            }
        }
    }

    public override bool IsValueType()
    {
        return false;
    }
}