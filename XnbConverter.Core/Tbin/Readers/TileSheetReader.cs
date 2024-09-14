using System.Collections.Generic;
using XnbConverter.Entity.Mono;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Tbin.Entity;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class TileSheetReader : BaseReader
{
	private readonly StringReader stringReader = new StringReader();

	private int intVector2Reader;

	private int propertieListReader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		stringReader.Init(resolver);
		intVector2Reader = resolver.GetIndex(typeof(IntVector2));
		propertieListReader = resolver.GetIndex(typeof(List<Propertie>));
	}

	public override bool IsValueType()
	{
		return true;
	}

	public override object Read()
	{
		return new TileSheet
		{
			Id = stringReader.ReadByInt32(),
			Description = stringReader.ReadByInt32(),
			Image = stringReader.ReadByInt32(),
			SheetSize = readerResolver.ReadValue<IntVector2>(intVector2Reader),
			TileSize = readerResolver.ReadValue<IntVector2>(intVector2Reader),
			Margin = readerResolver.ReadValue<IntVector2>(intVector2Reader),
			Spacing = readerResolver.ReadValue<IntVector2>(intVector2Reader),
			Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader)
		};
	}

	public override void Write(object input)
	{
		TileSheet tileSheet = (TileSheet)input;
		stringReader.WriteByInt32(tileSheet.Id);
		stringReader.WriteByInt32(tileSheet.Description);
		stringReader.WriteByInt32(tileSheet.Image);
		readerResolver.WriteValue(intVector2Reader, tileSheet.SheetSize);
		readerResolver.WriteValue(intVector2Reader, tileSheet.TileSize);
		readerResolver.WriteValue(intVector2Reader, tileSheet.Margin);
		readerResolver.WriteValue(intVector2Reader, tileSheet.Spacing);
		readerResolver.WriteValue(propertieListReader, tileSheet.Properties);
	}
}
