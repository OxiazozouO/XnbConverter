using XnbConverter.Entity.Mono;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Readers.Base.ValueReaders;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities;

namespace XnbConverter.Tbin.Readers;

public class TBin10Reader : BaseReader
{
	private readonly XnbConverter.Readers.Base.StringReader stringReader = new XnbConverter.Readers.Base.StringReader();

	private bool isRemoveTileSheetsExtension;

	private int layerListReader;

	private int propertieListReader;

	private int tileSheetListReader;

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		stringReader.Init(resolver);
		tileSheetListReader = resolver.GetIndex(typeof(List<TileSheet>));
		propertieListReader = resolver.GetIndex(typeof(List<Propertie>));
		layerListReader = resolver.GetIndex(typeof(List<Layer>));
	}

	public override bool IsValueType()
	{
		return true;
	}

	public override object Read()
	{
		TBin10 tBin = new TBin10();
		tBin.Format = bufferReader.ReadString(6);
		tBin.Id = stringReader.ReadByInt32();
		tBin.Description = stringReader.ReadByInt32();
		tBin.Properties = readerResolver.ReadValue<List<Propertie>>(propertieListReader);
		tBin.TileSheets = readerResolver.ReadValue<List<TileSheet>>(tileSheetListReader);
		if (!isRemoveTileSheetsExtension)
		{
			tBin.Layers = readerResolver.ReadValue<List<Layer>>(layerListReader);
		}
		return tBin;
	}

	public static void RemoveTileSheetsExtension(ref byte[] data)
	{
		byte[] array = data;
		Span<byte> span = array.AsSpan();
		TBin10Reader tBin10Reader = Create(data);
		tBin10Reader.isRemoveTileSheetsExtension = true;
		TBin10 tBin = (TBin10)tBin10Reader.Read();
		int bytePosition = tBin10Reader.bufferReader.BytePosition;
		int num = tBin10Reader.bufferReader.ReadInt32();
		if (num > 100)
		{
			throw new XnbError(Error.TBin10Reader_1, 100, num);
		}
		tBin.RemoveTileSheetsExtension();
		tBin10Reader.Write(tBin);
		int bytePosition2 = tBin10Reader.bufferWriter.BytePosition;
		int num2 = bytePosition2 + (array.Length - bytePosition);
		if (bytePosition == bytePosition2)
		{
			data = array;
			return;
		}
		if (bytePosition > bytePosition2)
		{
			byte[] array2 = new byte[num2];
			Span<byte> destination = array2.AsSpan();
			span.Slice(0, bytePosition2).CopyTo(destination);
			int num3 = bytePosition;
			Span<byte> span2 = span.Slice(num3, span.Length - num3);
			int num4 = bytePosition2;
			span2.CopyTo(destination.Slice(num4, destination.Length - num4));
			data = array2;
			return;
		}
		throw new XnbError(Error.TBin10Reader_2);
	}

	public static TBin10Reader Create(byte[] data)
	{
		TBin10Reader tBin10Reader = new TBin10Reader();
		new ReaderResolver(new BaseReader[11]
		{
			tBin10Reader,
			new TileSheetReader(),
			new ListReader<TileSheetReader, TileSheet>(),
			new PropertieReader(),
			new ListReader<PropertieReader, Propertie>(),
			new LayerReader(),
			new ListReader<LayerReader, Layer>(),
			new Vector2Reader(),
			new IntVector2Reader(),
			new StaticTileReader(),
			new AnimatedTilerReader()
		}, new Type[11]
		{
			typeof(TBin10),
			typeof(TileSheet),
			typeof(List<TileSheet>),
			typeof(Propertie),
			typeof(List<Propertie>),
			typeof(Layer),
			typeof(List<Layer>),
			typeof(Vector2),
			typeof(IntVector2),
			typeof(StaticTile),
			typeof(AnimatedTile)
		}, new BufferReader(data), new BufferWriter(data));
		return tBin10Reader;
	}

	public override void Write(object content)
	{
		TBin10 tBin = (TBin10)content;
		bufferWriter.WriteAsciiString(tBin.Format);
		stringReader.WriteByInt32(tBin.Id);
		stringReader.WriteByInt32(tBin.Description);
		readerResolver.WriteValue(propertieListReader, tBin.Properties);
		readerResolver.WriteValue(tileSheetListReader, tBin.TileSheets);
		if (!isRemoveTileSheetsExtension)
		{
			readerResolver.WriteValue(layerListReader, tBin.Layers);
		}
	}

	public void Save(TBin10 input, string path)
	{
		bufferWriter.Buffer = Pool.RentByte(10485760);
		Write(input);
		File.WriteAllBytes(path, bufferWriter.Buffer[..bufferWriter.BytePosition]);
		Pool.Return(bufferWriter.Buffer);
	}

	public static TBin10 FormFile(string path)
	{
		return (TBin10)Create(File.ReadAllBytes(path)).Read();
	}

	public void FormTmx(string path)
	{
	}
}
