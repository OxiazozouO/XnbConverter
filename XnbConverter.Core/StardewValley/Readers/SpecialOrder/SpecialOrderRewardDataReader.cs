using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.SpecialOrder;

public class SpecialOrderRewardDataReader : BaseReader
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

    public override SpecialOrderRewardData Read()
    {
        SpecialOrderRewardData result = new SpecialOrderRewardData();
        
        result.Type = readerResolver.Read<string>();
        result.Data = readerResolver.Read<Dictionary<string, string>>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (SpecialOrderRewardData)content;

        readerResolver.Write(stringReader, input.Type);
        readerResolver.Write(dictionaryReader, input.Data);
    }

    public override bool IsValueType()
    {
        return false;
    }
}