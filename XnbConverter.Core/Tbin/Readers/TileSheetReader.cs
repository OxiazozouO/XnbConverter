using System.Collections.Generic;
using XnbConverter.Entity.Mono;
using XnbConverter.Readers;
using XnbConverter.Tbin.Entity;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class TileSheetReader : BaseReader
{
    private readonly StringReader stringReader = new();
    private int intVector2Reader;
    private int propertieListReader;

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader.Init(readerResolver);
        intVector2Reader = readerResolver.GetIndex(typeof(IntVector2));
        propertieListReader = readerResolver.GetIndex(typeof(List<Propertie>));
    }

    public override bool IsValueType()
    {
        return true;
    }

    public override TileSheet Read()
    {
        var result = new TileSheet();

        result.Id = stringReader.ReadByInt32();
        result.Description = stringReader.ReadByInt32();
        result.Image = stringReader.ReadByInt32();
        result.SheetSize = readerResolver.ReadValue<IntVector2>(intVector2Reader);
        result.TileSize = readerResolver.ReadValue<IntVector2>(intVector2Reader);
        result.Margin = readerResolver.ReadValue<IntVector2>(intVector2Reader);
        result.Spacing = readerResolver.ReadValue<IntVector2>(intVector2Reader);
        result.Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader);

        return result;
    }


    public override void Write(object input)
    {
        var result = (TileSheet)input;
        stringReader.WriteByInt32(result.Id);
        stringReader.WriteByInt32(result.Description);
        stringReader.WriteByInt32(result.Image);
        readerResolver.WriteValue(intVector2Reader, result.SheetSize);
        readerResolver.WriteValue(intVector2Reader, result.TileSize);
        readerResolver.WriteValue(intVector2Reader, result.Margin);
        readerResolver.WriteValue(intVector2Reader, result.Spacing);
        readerResolver.WriteValue(propertieListReader, result.Properties);
    }
}