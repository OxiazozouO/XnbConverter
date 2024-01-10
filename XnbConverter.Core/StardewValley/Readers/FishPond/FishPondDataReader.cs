using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using XnbConverter.StardewValley.Entity.FishPond;

namespace XnbConverter.StardewValley.Readers.FishPond;

public class FishPondDataReader : BaseReader
{
    private int stringListReader;
    private int fishPondRewardListReader;
    private int intDictionaryReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringListReader         = readerResolver.GetIndex<ListReader<XnbConverter.Readers.Base.StringReader, string>>();
        fishPondRewardListReader = readerResolver.GetIndex<ListReader<ReflectiveReader<FishPondRewardReader>, FishPondReward>>();
        intDictionaryReader      = readerResolver.GetIndex<DictionaryReader<Int32Reader, ListReader<XnbConverter.Readers.Base.StringReader, string>, int, List<string>>>();
    }

    public override FishPondData Read()
    {
        FishPondData result = new FishPondData();

        result.RequiredTags = readerResolver.Read_Null<List<string>>();
        result.SpawnTime    = bufferReader.ReadInt32();
        result.ProducedItems   = readerResolver.Read_Null<List<FishPondReward>>();
        result.PopulationGates = readerResolver.Read_Null<Dictionary<int, List<string>>>();

        return result;
    }

    public override void Write(object content)
    {
        var input = (FishPondData)content;
        
        readerResolver.Write_Null(stringListReader, input.RequiredTags);
        bufferWriter.WriteInt32(input.SpawnTime);
        readerResolver.Write_Null(fishPondRewardListReader, input.ProducedItems);
        readerResolver.Write_Null(intDictionaryReader, input.PopulationGates );
    }

    public override bool IsValueType()
    {
        return false;
    }
}