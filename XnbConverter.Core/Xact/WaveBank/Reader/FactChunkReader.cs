using XnbConverter.Readers;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact.WaveBank.Reader;

public class FactChunkReader : BaseReader, IReaderFileUtil<FactChunk>
{
	public void Save(FactChunk input)
	{
		bufferWriter.WriteAsciiString(input.ChunkID);
		bufferWriter.WriteUInt32(input.ChunkSize);
		bufferWriter.WriteUInt32(input.DataFactSize);
	}

	public FactChunk Load()
	{
		return new FactChunk
		{
			ChunkSize = bufferReader.ReadUInt32(),
			DataFactSize = bufferReader.ReadUInt32()
		};
	}

	public override bool IsValueType()
	{
		throw new NotImplementedException();
	}

	public override object Read()
	{
		throw new NotImplementedException();
	}

	public override void Write(object input)
	{
		_ = (FactChunk)input;
		throw new NotImplementedException();
	}
}
