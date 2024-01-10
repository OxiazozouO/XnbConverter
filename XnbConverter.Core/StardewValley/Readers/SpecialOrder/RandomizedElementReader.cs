using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.SpecialOrder;

public class RandomizedElementReader : BaseReader
{
    private int stringReader;
    private int randomizedElementItemListReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        randomizedElementItemListReader =readerResolver.GetIndex<ListReader<ReflectiveReader<RandomizedElementItemReader>, RandomizedElementItem>>();
    }

    public override RandomizedElement Read()
    {
        var name = readerResolver.Read<string>();
        var values = readerResolver.Read_Null<List<RandomizedElementItem>>();
        return new RandomizedElement
        {
            Name = name,
            Values = values
        };
    }

    public override void Write(object content)
    {
        var input = (RandomizedElement)content;
        
        readerResolver.Write(stringReader, input.Name);
        readerResolver.Write_Null(randomizedElementItemListReader, input.Values);
    }

    public override bool IsValueType()
    {
        return false;
    }
}