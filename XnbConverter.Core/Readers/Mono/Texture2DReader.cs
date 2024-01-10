﻿using SixLabors.ImageSharp.Formats;
using XnbConverter.Entity.Mono;
using XnbConverter.Utilities;
using static System.Math;
using Flag = Squish.SquishFlags;
using Dxt = Squish.Squish;
using Log = XnbConverter.Utilities.Log;

namespace XnbConverter.Readers.Mono;

/**
 * Texture2D Reader
 * @class
 * @extends BaseReader
 */
public class Texture2DReader : BaseReader
{
    public override void Init(ReaderResolver readerResolver)
    {
        this.bufferReader = readerResolver.bufferReader;
        this.bufferWriter = readerResolver.bufferWriter;
    }
    
    public override Texture2D Read()
    {
        Texture2D result = new Texture2D();
        
        result.Format   = bufferReader.ReadInt32();
        result.Width    = bufferReader.ReadInt32();
        result.Height   = bufferReader.ReadInt32();
        result.MipCount = bufferReader.ReadInt32();
        result.DataSize = bufferReader.ReadUInt32();
        
        if (result.MipCount > 1)
            Log.Warn("找到｛0｝的mipcount，将只使用第一个。", result.MipCount);
        Flag flag = result.Format switch
        {
            4 => Flag.kDxt1,
            5 => Flag.kDxt3,
            6 => Flag.kDxt5,
            2 => throw new XnbError("Texture2D格式类型ECT1未实现！"),
            0 => 0,
            _ => throw new XnbError("找到未实现的Texture2D格式类型（{0}）。", result.Format),
        };
        
        if (flag is not 0)
        {
            byte[] numArray = new byte[result.Width * result.Height * 4];
            Dxt dxt = new(flag, result.Width, result.Height);
            dxt.DecompressImage(numArray,bufferReader.ReadOnly((int)result.DataSize));
            dxt.Dispose();
            result.Data = numArray;
        }
        else
        {
            result.Data = bufferReader.Read((int)result.DataSize);
        }
        
        // 将alpha通道添加到图像中
        byte[] data = result.Data;
        for (int i = 0; i < data.Length; i += 4)
        {
            float inverseAlpha = (float)(255.0 / data[i + 3]);
            data[i    ] = (byte)Min(Ceiling(data[i    ] * inverseAlpha), 255);
            data[i + 1] = (byte)Min(Ceiling(data[i + 1] * inverseAlpha), 255);
            data[i + 2] = (byte)Min(Ceiling(data[i + 2] * inverseAlpha), 255);
        }
        return result;
    }

    public override void Write(object content)
    {
        var input = (Texture2D)content;

        int format = input.Format;
        int width = input.Width;
        int height = input.Height;
        byte[] data = input.Data;
        
        Log.Debug("图片 宽: {0}, 高: {1}, 格式:  {2}", width, height, format);
        bufferWriter.WriteInt32(format);
        bufferWriter.WriteInt32(width);
        bufferWriter.WriteInt32(height);
        bufferWriter.WriteInt32(1);

        
        for (int i = 0; i < data.Length; i += 4)
        {
            float alpha = data[i + 3] / 255f;
            data[i    ] = (byte)Floor(data[i    ] * alpha);
            data[i + 1] = (byte)Floor(data[i + 1] * alpha);
            data[i + 2] = (byte)Floor(data[i + 2] * alpha);
        }
        Flag flag = input.Format switch
        {
            4 => Flag.kDxt1,
            5 => Flag.kDxt3,
            6 => Flag.kDxt5,
            _ => 0
        };
        if (flag is not 0)
        {
            Dxt dxt = new(flag, width, height);
            int len = dxt.GetStorageRequirements();
            bufferWriter.WriteUInt32((uint)len);
            dxt.CompressImage(data, bufferWriter.Buffer.AsSpan(bufferWriter.BytePosition, len));
            dxt.Dispose();
            bufferWriter.Skip(len);
        }
        else
        {
            bufferWriter.WriteUInt32((uint)data.Length);
            bufferWriter.Write(data);
        }
    }
    
    public override bool IsValueType()
    {
        return false;
    }
    
    public override Type GetResultType()
    {
        return typeof(Texture2D);
    }
}