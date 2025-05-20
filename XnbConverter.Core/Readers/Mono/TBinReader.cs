using XnbConverter.Tbin.Entity;

namespace XnbConverter.Readers.Mono;

public class TBinReader : BaseReader
{
	public override void Init(ReaderResolver resolver)
	{
		bufferReader = resolver.bufferReader;
		bufferWriter = resolver.bufferWriter;
	}

	public override object Read()
	{
		int count = bufferReader.ReadInt32();
		byte[] data = bufferReader.Read(count);
		return new TBin10
		{
			Data = data
		};
	}

	public override void Write(object input)
	{
		TBin10 tBin = (TBin10)input;
		bufferWriter.WriteInt32(tBin.Data.Length);
		bufferWriter.Write(tBin.Data);
	}

	public override bool IsValueType()
	{
		return false;
	}
}
