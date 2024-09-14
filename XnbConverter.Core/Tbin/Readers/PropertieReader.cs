using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Readers.Base;
using XnbConverter.Tbin.Entity;
using XnbConverter.Utilities;
using StringReader = XnbConverter.Readers.Base.StringReader;

namespace XnbConverter.Tbin.Readers;

public class PropertieReader : BaseReader
{
	private readonly StringReader stringReader = new StringReader();

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		stringReader.Init(resolver);
	}

	public override bool IsValueType()
	{
		return true;
	}

	public override object Read()
	{
		Propertie propertie = new Propertie();
		propertie.Key = stringReader.ReadByInt32();
		propertie.Type = bufferReader.ReadByte();
		Propertie propertie2 = propertie;
		propertie2.Value = propertie.Type switch
		{
			0 => bufferReader.ReadByte(), 
			1 => bufferReader.ReadInt32(), 
			2 => bufferReader.ReadSingle(), 
			3 => stringReader.ReadByInt32(), 
			4 => throw new XnbError(Error.PropertieReader_1, propertie.Type), 
			_ => null, 
		};
		return propertie;
	}

	public override void Write(object content)
	{
		Propertie propertie = (Propertie)content;
		stringReader.WriteByInt32(propertie.Key);
		bufferWriter.WriteByte(propertie.Type);
		switch (propertie.Type)
		{
		case 0:
			bufferWriter.WriteByte((byte)propertie.Value);
			break;
		case 1:
			bufferWriter.WriteInt32((int)propertie.Value);
			break;
		case 2:
			bufferWriter.WriteSingle((float)propertie.Value);
			break;
		case 3:
			stringReader.WriteByInt32((string)propertie.Value);
			break;
		case 4:
			throw new XnbError(Error.PropertieReader_1, propertie.Type);
		}
	}
}
