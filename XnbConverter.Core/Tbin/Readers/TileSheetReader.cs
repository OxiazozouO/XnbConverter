using XnbConverter.Entity.Mono;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using XnbConverter.Tbin.Entity;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class TileSheetReader: BaseReader
{
    private readonly StringReader stringReader = new ();
    private int vector2Reader;
    private int propertieListReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader.Init(readerResolver);
        vector2Reader = readerResolver.GetIndex<Vector2Reader>();
        propertieListReader = readerResolver.GetIndex<ListReader<PropertieReader, Propertie>>();
    }
    public override bool IsValueType()
    {
        return true;
    }

    public override TileSheet Read()
    {
        TileSheet result = new TileSheet();
        
        result.Id          = stringReader.ReadByInt32();
        result.Description = stringReader.ReadByInt32();
        result.Image       = stringReader.ReadByInt32();
        result.SheetSize   = readerResolver.ReadValue<Vector2>(vector2Reader);
        result.TileSize    = readerResolver.ReadValue<Vector2>(vector2Reader);
        result.Margin      = readerResolver.ReadValue<Vector2>(vector2Reader);
        result.Spacing     = readerResolver.ReadValue<Vector2>(vector2Reader);
        result.Properties  = readerResolver.ReadValue<List<Propertie>>(propertieListReader);
        
        return result;
    }
    
    
    public override void Write(object input)
    {
        TileSheet result = (TileSheet)input;
        stringReader.WriteByInt32(result.Id);
        stringReader.WriteByInt32(result.Description);
        stringReader.WriteByInt32(result.Image);
        readerResolver.WriteValue(vector2Reader, result.SheetSize);
        readerResolver.WriteValue(vector2Reader, result.TileSize);
        readerResolver.WriteValue(vector2Reader, result.Margin);
        readerResolver.WriteValue(vector2Reader, result.Spacing);
        readerResolver.WriteValue(propertieListReader, result.Properties);
    }
}