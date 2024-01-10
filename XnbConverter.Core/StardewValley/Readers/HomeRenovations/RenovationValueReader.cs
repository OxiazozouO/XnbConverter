using XnbConverter.Readers;
using XnbConverter.StardewValley.Entity.HomeRenovations;

namespace XnbConverter.StardewValley.Readers.HomeRenovations;

public class RenovationValueReader : BaseReader
{
    private int stringReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
    }

    public override RenovationValue Read()
    {
        RenovationValue result = new RenovationValue();
        
        result.Type = readerResolver.Read<string>();
        result.Key = readerResolver.Read<string>();
        result.Value = readerResolver.Read<string>();
        
        return result;
    }
    
    public override void Write(object content)
    {
        var input = (RenovationValue)content;
        
        readerResolver.Write(stringReader, input.Type);
        readerResolver.Write(stringReader, input.Key);
        readerResolver.Write(stringReader, input.Value);
    }

    public override bool IsValueType()
    {
        return false;
    }
}