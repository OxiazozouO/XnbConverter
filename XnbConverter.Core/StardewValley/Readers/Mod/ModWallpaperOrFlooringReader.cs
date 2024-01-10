using XnbConverter.Readers;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.Mod;

public class ModWallpaperOrFlooringReader : BaseReader
{
    private int stringReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
    }

    public override ModWallpaperOrFlooring Read()
    {
        ModWallpaperOrFlooring result = new ModWallpaperOrFlooring();
        
        result.ID = readerResolver.Read_Null<string>();
        result.Texture = readerResolver.Read_Null<string>();
        result.IsFlooring = bufferReader.ReadBoolean();
        result.Count = bufferReader.ReadInt32();

        return result;
    }

    public override void Write(object content)
    {
        var input = (ModWallpaperOrFlooring)content;
        
        readerResolver.Write_Null(stringReader, input.ID);
        readerResolver.Write_Null(stringReader, input.Texture);
        bufferWriter.WriteBoolean(input.IsFlooring);
        bufferWriter.WriteInt32(input.Count);
    }

    public override bool IsValueType()
    {
        return false;
    }
}