using System;
using System.Collections.Generic;
using XnbConverter.Exceptions;
using XnbConverter.Utilities;

namespace XnbConverter.Readers;

public class ReaderResolver
{
    private readonly List<int> typeIndex;

    private readonly List<string> _typeName;

    private readonly BaseReader[] readerArr;

    public BufferReader bufferReader;

    public BufferWriter bufferWriter;

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
        for (int i = 0; i < readerArr.Length; i++)
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
        BaseReader[] array = readerArr;
        for (int i = 0; i < array.Length; i++)
        {
            array[i].Init(this);
        }
    }

    public object Read(int i)
    {
        int num = bufferReader.Read7BitNumber() - 1;
        if (num != i)
        {
            throw new XnbError(Error.ReaderResolver_1, num);
        }

        return readerArr[num].Read();
    }

    public T Read<T>(int i) => (T)Read(i);

    public T ReadValue<T>(int i)
    {
        return (T)readerArr[i].Read();
    }

    public object Read_Null(int i)
    {
        int num = bufferReader.Read7BitNumber() - 1;
        if (num < 0)
        {
            return null;
        }

        if (num != i)
        {
            throw new XnbError(Error.ReaderResolver_1, num);
        }

        return readerArr[num].Read();
    }

    public object ReadValue(int i)
    {
        return readerArr[i].Read();
    }

    public int GetIndex(Type t)
    {
        int num = _typeName.IndexOf(t.ToString());
        if (num == -1 || num > typeIndex.Count)
        {
            throw new ArgumentException();
        }

        return typeIndex[num];
    }

    public void Write(int i, object item)
    {
        if (i < 0 || i > readerArr.Length)
        {
            throw new XnbError(Error.ReaderResolver_2, i);
        }

        bufferWriter.Write7BitNumber(i + 1);
        readerArr[i].Write(item);
    }

    public void Write_Null(int i, object? item)
    {
        if (i > readerArr.Length)
        {
            throw new XnbError(Error.ReaderResolver_2, i);
        }

        if (i == -1 || item == null)
        {
            bufferWriter.Write7BitNumber(0);
            return;
        }

        bufferWriter.Write7BitNumber(i + 1);
        readerArr[i].Write(item);
    }

    public void WriteValue(int i, object item)
    {
        readerArr[i].Write(item);
    }
}