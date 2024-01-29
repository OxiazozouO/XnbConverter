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

using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class TBin10Reader : BaseReader
{
    private StringReader stringReader = new ();
    private int tileSheetListReader;
    private int propertieListReader;
    private int layerListReader;
    private bool isRemoveTileSheetsExtension;
    
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader.Init(readerResolver);
        tileSheetListReader = readerResolver.GetIndex< ListReader<TileSheetReader, TileSheet> >();
        propertieListReader = readerResolver.GetIndex< ListReader<PropertieReader, Propertie> >();
        layerListReader     = readerResolver.GetIndex< ListReader<LayerReader, Layer> >();
    }
    
    public override bool IsValueType()
    {
        return true;
    }

    public override TBin10 Read()
    {
        TBin10 result = new TBin10();
        result.Format = bufferReader.ReadString(6);
        result.Id = stringReader.ReadByInt32();
        result.Description = stringReader.ReadByInt32();//22
        result.Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader);
        result.TileSheets = readerResolver.ReadValue<List<TileSheet>>(tileSheetListReader);
        if (!isRemoveTileSheetsExtension)
            result.Layers = readerResolver.ReadValue<List<Layer>>(layerListReader);
        return result;
    }
    public static void RemoveTileSheetsExtension(ref TBin10 tbin)
    {
        byte[] tmp = tbin.Data;
        Span<byte> span = tmp.AsSpan();
        
        TBin10Reader main = Create(tbin);
        
        main.isRemoveTileSheetsExtension = true;
        tbin = main.Read();
        int ordLen = main.bufferReader.BytePosition;
        int size = main.bufferReader.ReadInt32();
        if(size > TBin10.LayerMax)
            throw new XnbError("Xnb尚未支持超过{0}层的Tbin地图，目前{1}层", TBin10.LayerMax, size);
        // Log.BigFileDebug("D:\\1\\input.txt", tmp);
        foreach (var t in tbin.TileSheets)
            t.Image = t.Image.Replace(".png", "");

        main.Write(tbin);
        int newLen = main.bufferWriter.BytePosition;
        
        int outLen = newLen + (tmp.Length - ordLen);
        tbin = new TBin10();
        if (ordLen == newLen)
        {
            tbin.Data = tmp;
        } else if (ordLen > newLen)
        {
            byte[] result = new byte[outLen];
            Span<byte> span2 = result.AsSpan();
            span[..newLen].CopyTo(span2);
            span[ordLen..].CopyTo(span2[newLen..]);
            tbin.Data = result;
        } else
            throw new XnbError("未知程序错误");
        
        // Log.BigFileDebug("D:\\1\\output.txt", tbin.Data);
    }
    
    public static TBin10Reader Create(TBin10 tbin)
    {
        
        TBin10Reader main = new TBin10Reader();
        _ = new ReaderResolver(
            new BaseReader[]
            {
                main, new TileSheetReader(), new ListReader<TileSheetReader, TileSheet>(),
                new PropertieReader(), new ListReader<PropertieReader, Propertie>(),
                new LayerReader(), new ListReader<LayerReader, Layer>(),
                new Vector2Reader(), new IntVector2Reader(),new StaticTileReader(),
                new AnimatedTilerReader(),
            },
            new BufferReader(tbin.Data),
            new BufferWriter(tbin.Data)
        );
        return main;
    }
    
    public override void Write(object content)
    {
        TBin10 input = (TBin10)content;
        
        bufferWriter.WriteAsciiString(input.Format);
        stringReader.WriteByInt32(input.Id);
        stringReader.WriteByInt32(input.Description);
        readerResolver.WriteValue(propertieListReader, input.Properties);
        readerResolver.WriteValue(tileSheetListReader, input.TileSheets);
        if(!isRemoveTileSheetsExtension)
            readerResolver.WriteValue(layerListReader, input.Layers);
    }
}