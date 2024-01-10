using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.Mod;

public class ModFarmTypeReader : BaseReader
{
    private int stringReader;
    private int dictionaryReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        dictionaryReader = readerResolver.GetIndex<DictionaryReader<XnbConverter.Readers.Base.StringReader, XnbConverter.Readers.Base.StringReader, string, string>>();
    }

    /**
     * 从缓冲区读取反射数据。
     * @param {BufferReader} buffer 缓冲区读取器
     * @returns {Mixed} 反射数据
     */
    public override ModFarmType Read()
    {
        ModFarmType result = new ModFarmType();
        
        result.ID = readerResolver.Read_Null<string>();
        result.TooltipStringPath = readerResolver.Read_Null<string>();
        result.MapName = readerResolver.Read_Null<string>();
        result.IconTexture = readerResolver.Read_Null<string>();
        result.WorldMapTexture = readerResolver.Read_Null<string>();
        result.ModData = readerResolver.Read_Null<Dictionary<string, string>>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (ModFarmType)content;
        
        readerResolver.Write_Null(stringReader, input.ID);
        readerResolver.Write_Null(stringReader, input.TooltipStringPath);
        readerResolver.Write_Null(stringReader, input.MapName);
        readerResolver.Write_Null(stringReader, input.IconTexture);
        readerResolver.Write_Null(stringReader, input.WorldMapTexture);
        readerResolver.Write_Null(dictionaryReader, input.ModData);
    }

    public override bool IsValueType()
    {
        return false;
    }
}