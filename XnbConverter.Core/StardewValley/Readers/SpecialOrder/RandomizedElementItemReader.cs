using XnbConverter.Readers;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.SpecialOrder;

public class RandomizedElementItemReader : BaseReader
{
    private int stringReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
    }

    public override RandomizedElementItem Read()
    {
        RandomizedElementItem result = new RandomizedElementItem();
        
        result.RequiredTags = readerResolver.Read<string>();
        result.Value = readerResolver.Read<string>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (RandomizedElementItem)content;
        
        readerResolver.Write(stringReader, input.RequiredTags);
        readerResolver.Write(stringReader, input.Value);
    }

    public override bool IsValueType()
    {
        return false;
    }
}