using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.SpecialOrder;

public class SpecialOrderDataReader : BaseReader
{
    private int stringReader;
    private int randomizedElementListReader;

    private int specialOrderObjectiveDataReader;

    private int specialOrderRewardDataReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        randomizedElementListReader =
            readerResolver.GetIndex<ListReader<ReflectiveReader<RandomizedElementReader>, RandomizedElement>>();
        specialOrderObjectiveDataReader =
            readerResolver.GetIndex<ListReader<ReflectiveReader<SpecialOrderObjectiveDataReader>, SpecialOrderObjectiveData>>();
        specialOrderRewardDataReader = readerResolver.GetIndex<ListReader<ReflectiveReader<SpecialOrderRewardDataReader>,SpecialOrderRewardData>>();
    }

    public override SpecialOrderData Read()
    {
        SpecialOrderData result = new SpecialOrderData();
        
        result.Name = readerResolver.Read<string>();
        result.Requester = readerResolver.Read<string>();
        result.Duration = readerResolver.Read<string>();
        result.Repeatable = readerResolver.Read<string>();
        result.RequiredTags = readerResolver.Read<string>();
        result.OrderType = readerResolver.Read<string>();
        result.SpecialRule = readerResolver.Read<string>();
        result.Text = readerResolver.Read<string>();
        result.ItemToRemoveOnEnd = readerResolver.Read_Null<string>();
        result.MailToRemoveOnEnd = readerResolver.Read_Null<string>();
        result.RandomizedElements = readerResolver.Read_Null<List<RandomizedElement>>();
        result.Objectives = readerResolver.Read<List<SpecialOrderObjectiveData>>();
        result.Rewards = readerResolver.Read<List<SpecialOrderRewardData>>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (SpecialOrderData)content;

        readerResolver.Write(stringReader, input.Name);
        readerResolver.Write(stringReader, input.Requester);
        readerResolver.Write(stringReader, input.Duration);
        readerResolver.Write(stringReader, input.Repeatable);
        readerResolver.Write(stringReader, input.RequiredTags);
        readerResolver.Write(stringReader, input.OrderType);
        readerResolver.Write(stringReader, input.SpecialRule);
        readerResolver.Write(stringReader, input.Text);
        readerResolver.Write_Null(stringReader, input.ItemToRemoveOnEnd);
        readerResolver.Write_Null(stringReader, input.MailToRemoveOnEnd);
        readerResolver.Write_Null(randomizedElementListReader, input.RandomizedElements);
        
        readerResolver.Write(specialOrderObjectiveDataReader, input.Objectives);
        readerResolver.Write(specialOrderRewardDataReader, input.Rewards);
    }

    public override bool IsValueType()
    {
        return false;
    }
}