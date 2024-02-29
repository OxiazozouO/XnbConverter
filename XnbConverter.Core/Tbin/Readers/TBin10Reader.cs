#region HEADER

/*
 * Author: spacechase0 (https://github.com/spacechase0)
 * Project URL: https://github.com/spacechase0/TbinCSharp
 */

#region MIT License

/*
 * Copyright (c) 2020
 */

#endregion

/*
 * 对于原代码进行了一定适配。
 * 星露谷物语中，图块集后缀名含有”.png"后缀时，游戏实际上读取的是".png.png"，导致读取失败。
 * 此代码主要为了解决这个问题，把图块集读取出来再删除多余的".png"后缀，然后再写回去
 */

#endregion

using XnbConverter.Entity.Mono;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class TBin10Reader : BaseReader
{
    private StringReader stringReader = new();
    private int tileSheetListReader;
    private int propertieListReader;
    private int layerListReader;
    private bool isRemoveTileSheetsExtension;


    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader.Init(readerResolver);
        tileSheetListReader = readerResolver.GetIndex(typeof(List<TileSheet>));
        propertieListReader = readerResolver.GetIndex(typeof(List<Propertie>));
        layerListReader = readerResolver.GetIndex(typeof(List<Layer>));
    }

    public override bool IsValueType()
    {
        return true;
    }

    public override TBin10 Read()
    {
        var result = new TBin10();
        result.Format = bufferReader.ReadString(6);
        result.Id = stringReader.ReadByInt32();
        result.Description = stringReader.ReadByInt32(); //22
        result.Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader);
        result.TileSheets = readerResolver.ReadValue<List<TileSheet>>(tileSheetListReader);
        if (!isRemoveTileSheetsExtension)
            result.Layers = readerResolver.ReadValue<List<Layer>>(layerListReader);
        return result;
    }

    public static void RemoveTileSheetsExtension(ref byte[] data)
    {
        var tmp = data;
        var span = tmp.AsSpan();

        var main = Create(data);

        main.isRemoveTileSheetsExtension = true;
        var tbin = main.Read();
        var ordLen = main.bufferReader.BytePosition;
        var size = main.bufferReader.ReadInt32();
        if (size > TBin10.LayerMax)
            throw new XnbError(Helpers.I18N["TBin10Reader.1"], TBin10.LayerMax, size);
        // Log.BigFileDebug("D:\\1\\input.txt", tmp);
        tbin.RemoveTileSheetsExtension();

        main.Write(tbin);
        var newLen = main.bufferWriter.BytePosition;

        var outLen = newLen + (tmp.Length - ordLen);
        if (ordLen == newLen)
        {
            data = tmp;
        }
        else if (ordLen > newLen)
        {
            var result = new byte[outLen];
            var span2 = result.AsSpan();
            span[..newLen].CopyTo(span2);
            span[ordLen..].CopyTo(span2[newLen..]);
            data = result;
        }
        else
        {
            throw new XnbError(Helpers.I18N["TBin10Reader.2"]);
        }
        // Log.BigFileDebug("D:\\1\\output.txt", tbin.Data);
    }

    public static TBin10Reader Create(byte[] data)
    {
        TBin10Reader main = new TBin10Reader();
        _ = new ReaderResolver(
            new BaseReader[]
            {
                main,
                new TileSheetReader(),
                new ListReader<TileSheetReader, TileSheet>(),
                new PropertieReader(),
                new ListReader<PropertieReader, Propertie>(),
                new LayerReader(),
                new ListReader<LayerReader, Layer>(),
                new Vector2Reader(),
                new IntVector2Reader(),
                new StaticTileReader(),
                new AnimatedTilerReader()
            },
            new Type[]
            {
                typeof(TBin10),
                typeof(TileSheet),
                typeof(List<TileSheet>),
                typeof(Propertie),
                typeof(List<Propertie>),
                typeof(Layer),
                typeof(List<Layer>),
                typeof(Vector2),
                typeof(IntVector2),
                typeof(StaticTile),
                typeof(AnimatedTile),
            },
            new BufferReader(data),
            new BufferWriter(data)
        );
        return main;
    }

    public override void Write(object content)
    {
        var input = (TBin10)content;

        bufferWriter.WriteAsciiString(input.Format);
        stringReader.WriteByInt32(input.Id);
        stringReader.WriteByInt32(input.Description);
        readerResolver.WriteValue(propertieListReader, input.Properties);
        readerResolver.WriteValue(tileSheetListReader, input.TileSheets);
        if (!isRemoveTileSheetsExtension)
            readerResolver.WriteValue(layerListReader, input.Layers);
    }

    public void Save(TBin10 input, string path)
    {
        bufferWriter.Buffer = Pool.RentByte(Pool.LongSize);
        Write(input);
        File.WriteAllBytes(path, bufferWriter.Buffer[..bufferWriter.BytePosition]);
        Pool.Return(bufferWriter.Buffer);
    }

    public static TBin10 FormFile(string path)
    {
        var v = Create(File.ReadAllBytes(path));
        return v.Read();
    }

    public void FormTmx(string path)
    {
    }
}