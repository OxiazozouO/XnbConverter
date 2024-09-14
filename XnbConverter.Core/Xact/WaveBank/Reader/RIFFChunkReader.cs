using System;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact.WaveBank.Reader;

public class RIFFChunkReader : BaseReader, IReaderFileUtil<RIFFChunk>
{
	public void Save(RIFFChunk riffChunk)
	{
		bufferWriter.WriteAsciiString(riffChunk.ChunkID);
		bufferWriter.WriteUInt32(riffChunk.ChunkSize);
		bufferWriter.WriteAsciiString(riffChunk.Format);
	}

	public RIFFChunk Load()
	{
		RIFFChunk rIFFChunk = new RIFFChunk();
		rIFFChunk.ChunkID = bufferReader.ReadString(4);
		rIFFChunk.ChunkSize = bufferReader.ReadUInt32();
		rIFFChunk.Format = bufferReader.ReadString(4);
		if (rIFFChunk.ChunkID != "RIFF")
		{
			throw new XnbError(Error.RIFFChunkReader_1, rIFFChunk.ChunkID);
		}
		if (rIFFChunk.Format != "WAVE")
		{
			throw new XnbError(Error.RIFFChunkReader_2, rIFFChunk.Format);
		}
		return rIFFChunk;
	}

	public override bool IsValueType()
	{
		throw new NotImplementedException();
	}

	public override object Read()
	{
		return new RIFFChunk
		{
			ChunkSize = bufferReader.ReadUInt32()
		};
	}

	public override void Write(object input)
	{
		RIFFChunk rIFFChunk = (RIFFChunk)input;
		bufferWriter.WriteUInt32(rIFFChunk.ChunkSize);
	}
}
