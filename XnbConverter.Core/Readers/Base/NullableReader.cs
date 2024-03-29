﻿namespace XnbConverter.Readers.Base;

public class NullableReader<T, N> : BaseReader where T : BaseReader, new()
{
    private bool b;
    private int reader;

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        reader = readerResolver.GetIndex(typeof(T));
        b = new T().IsValueType();
    }

    public override object Read()
    {
        var hasValue = bufferReader.ReadBoolean();
        return hasValue ? b ? readerResolver.ReadValue(reader) : readerResolver.Read_Null(reader) : null;
    }

    public override void Write(object input)
    {
        var c = input is not null;

        bufferWriter.WriteByte((byte)(c ? 1 : 0));
        if (!c) return;
        if (b)
        {
            readerResolver.WriteValue(reader, input);
        }
        else
        {
            readerResolver.Write(reader, input);
        }
    }

    public override bool IsValueType()
    {
        return false;
    }
}