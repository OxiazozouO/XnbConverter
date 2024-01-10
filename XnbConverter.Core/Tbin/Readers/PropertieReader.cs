﻿using XnbConverter.Readers;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class PropertieReader: BaseReader
{
    private StringReader stringReader = new();
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader.Init(readerResolver);
    }

    public override bool IsValueType()
    {
        return true;
    }

    public override Propertie Read()
    {
        Propertie result = new Propertie();
        result.Key = stringReader.ReadByInt32();
        result.Type = bufferReader.ReadByte();
        result.Value = result.Type switch
        {
            0 => bufferReader.ReadByte(),
            1 => bufferReader.ReadInt32(),
            2 => bufferReader.ReadSingle(),
            3 => stringReader.ReadByInt32(),
            4 => throw new XnbError("错误的地图属性类型：{0}", result.Type),
            _ => null
        };
        return result;
    }

    public override void Write(object content)
    {
        Propertie input = (Propertie)content;
        stringReader.WriteByInt32(input.Key);
        bufferWriter.WriteByte(input.Type);

        switch (input.Type)
        {
            case 0:
                bufferWriter.WriteByte((byte)input.Value);
                break;
            case 1:
                bufferWriter.WriteInt32((int)input.Value);
                break;
            case 2:
                bufferWriter.WriteSingle((float)input.Value);
                break;
            case 3:
                stringReader.WriteByInt32((string)input.Value);
                break;
            case 4:
                throw new XnbError("错误的地图属性类型：{0}", input.Type);
        }
    }
}