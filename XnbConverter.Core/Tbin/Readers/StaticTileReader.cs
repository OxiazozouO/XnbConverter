using XnbConverter.Readers;
using XnbConverter.Tbin.Entity;

namespace XnbConverter.Tbin.Readers;

public class StaticTileReader : BaseReader
{
	private int propertieListReader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		propertieListReader = resolver.GetIndex(typeof(List<Propertie>));
	}

	public override bool IsValueType()
	{
		return true;
	}

	public override object Read()
	{
		return new StaticTile
		{
			TileIndex = bufferReader.ReadInt32(),
			BlendMode = bufferReader.ReadByte(),
			Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader)
		};
	}

	public override void Write(object content)
	{
		StaticTile staticTile = (StaticTile)content;
		bufferWriter.WriteInt32(staticTile.TileIndex);
		bufferWriter.WriteByte(staticTile.BlendMode);
		readerResolver.WriteValue(propertieListReader, staticTile.Properties);
	}
}
