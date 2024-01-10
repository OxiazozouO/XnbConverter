using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.RandomBundle;

public class RandomBundleDataReader : BaseReader
{
    private int stringReader;
    private int bundleSetDataListReader;
    private int bundleDataListReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        if(readerResolver.bufferWriter == null) return;
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
        bundleSetDataListReader = readerResolver.GetIndex<ListReader<ReflectiveReader<BundleSetDataReader>, BundleSetData>>();
        bundleDataListReader = readerResolver.GetIndex<ListReader<ReflectiveReader<BundleDataReader>, BundleData>>();
    }

    public override RandomBundleData Read()
    {
        RandomBundleData result = new RandomBundleData();
        
        result.AreaName = readerResolver.Read<string>();
        result.Keys = readerResolver.Read<string>();
        result.BundleSets = readerResolver.Read<List<BundleSetData>>();
        result.Bundles = readerResolver.Read<List<BundleData>>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (RandomBundleData)content;
        
        readerResolver.Write(stringReader, input.AreaName);
        readerResolver.Write(stringReader, input.Keys);
        readerResolver.Write(bundleSetDataListReader, input.BundleSets);
        readerResolver.Write(bundleDataListReader, input.Bundles);
    }

    public override bool IsValueType()
    {
        return false;
    }
}