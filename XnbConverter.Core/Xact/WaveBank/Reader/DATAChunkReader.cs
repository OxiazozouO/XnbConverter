using XnbConverter.Readers;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact.WaveBank.Reader;

public class DATAChunkReader : BaseReader, IReaderFileUtil<DATAChunk>
{
	public void Save(DATAChunk dataChunk)
	{
		bufferWriter.WriteAsciiString(dataChunk.DataID);
		bufferWriter.WriteUInt32(dataChunk.DataSize);
		bufferWriter.Write(dataChunk.Data);
	}

	public DATAChunk Load()
	{
		DATAChunk dATAChunk = new DATAChunk();
		dATAChunk.DataSize = bufferReader.ReadUInt32();
		dATAChunk.Data = bufferReader.Read((int)dATAChunk.DataSize);
		return dATAChunk;
	}

	public override bool IsValueType()
	{
		throw new NotImplementedException();
	}

	public override object Read()
	{
		DATAChunk dATAChunk = new DATAChunk();
		dATAChunk.DataSize = bufferReader.ReadUInt32();
		dATAChunk.Data = bufferReader.Read((int)dATAChunk.DataSize);
		return dATAChunk;
	}

	public override void Write(object input)
	{
		DATAChunk dATAChunk = (DATAChunk)input;
		bufferWriter.WriteUInt32(dATAChunk.DataSize);
		bufferWriter.Write(dATAChunk.Data);
	}
}
