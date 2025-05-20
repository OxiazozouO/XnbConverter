using XnbConverter.Readers;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact.WaveBank.Reader;

public class ChunkReader : BaseReader, IReaderFileUtil<Chunk>
{
	public void Save(Chunk chunk)
	{
		bufferWriter.WriteAsciiString(chunk.Id);
		bufferWriter.WriteUInt32(chunk.Size);
	}

	public Chunk Load()
	{
		return new Chunk
		{
			Id = bufferReader.ReadString(4),
			Size = bufferReader.ReadUInt32()
		};
	}

	public override bool IsValueType()
	{
		throw new NotImplementedException();
	}

	public override object Read()
	{
		return new Chunk
		{
			Id = bufferReader.ReadString(4),
			Size = bufferReader.ReadUInt32()
		};
	}

	public override void Write(object input)
	{
		Chunk chunk = (Chunk)input;
		bufferWriter.WriteAsciiString(chunk.Id);
		bufferWriter.WriteUInt32(chunk.Size);
	}
}
