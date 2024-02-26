using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Tbin.Entity;

namespace XnbConverter.Tbin.Readers;

public class StaticTileReader : BaseReader
{
    private int propertieListReader;

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        propertieListReader = readerResolver.GetIndex(typeof(List<Propertie>));
    }

    public override bool IsValueType()
    {
        return true;
    }

    public override object Read()
    {
        var result = new StaticTile();
        result.TileIndex = bufferReader.ReadInt32();
        result.BlendMode = bufferReader.ReadByte();
        result.Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader);

        return result;
    }

    public override void Write(object content)
    {
        var input = (StaticTile)content;

        bufferWriter.WriteInt32(input.TileIndex);
        bufferWriter.WriteByte(input.BlendMode);
        readerResolver.WriteValue(propertieListReader, input.Properties);
    }
}