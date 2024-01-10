using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.RandomBundle;

public class BundleSetDataReader : BaseReader
{
    private int bundleDataListReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        bundleDataListReader = readerResolver.GetIndex<ListReader<ReflectiveReader<BundleDataReader>, BundleData>>();
    }

    public override BundleSetData Read()
    {
        BundleSetData result = new BundleSetData();
        
        result.Bundles = readerResolver.Read<List<BundleData>>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (BundleSetData)content;
        
        readerResolver.Write(bundleDataListReader, input.Bundles);
    }

    public override bool IsValueType()
    {
        return false;
    }
}