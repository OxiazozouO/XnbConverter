using XnbConverter.Readers;
using XnbConverter.StardewValley.Entity;

namespace XnbConverter.StardewValley.Readers.RandomBundle;

public class BundleDataReader : BaseReader
{
    private int stringReader;
    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        stringReader = readerResolver.GetIndex<XnbConverter.Readers.Base.StringReader>();
    }

    public override BundleData Read()
    {
        BundleData result = new BundleData();
        
        result.Name = readerResolver.Read<string>();
        result.Index = bufferReader.ReadInt32();
        result.Sprite = readerResolver.Read<string>();
        result.Color = readerResolver.Read<string>();
        result.Items = readerResolver.Read<string>();
        result.Pick = bufferReader.ReadInt32();
        result.RequiredItems = bufferReader.ReadInt32();
        result.Reward = readerResolver.Read<string>();
        
        return result;
    }

    public override void Write(object content)
    {
        var input = (BundleData)content;
        
        readerResolver.Write(stringReader, input.Name);
        bufferWriter.WriteInt32(input.Index);
        readerResolver.Write(stringReader, input.Sprite);
        readerResolver.Write(stringReader, input.Color );
        readerResolver.Write(stringReader, input.Items );
        bufferWriter.WriteInt32(input.Pick);
        bufferWriter.WriteInt32(input.RequiredItems);
        readerResolver.Write(stringReader, input.Reward );
    }

    public override bool IsValueType()
    {
        return false;
    }
}