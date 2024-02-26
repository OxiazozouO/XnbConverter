using XnbConverter.Readers;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact.WaveBank.Reader;

public class FactChunkReader : BaseReader, IReaderFileUtil<FactChunk>
{
    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override FactChunk Read()
    {
        throw new NotImplementedException();
    }

    public override void Write(object input)
    {
        var factChunk = (FactChunk)input;

        throw new NotImplementedException();
    }

    public void Save(FactChunk input)
    {
        bufferWriter.WriteAsciiString(input.ChunkID);
        bufferWriter.WriteUInt32(input.ChunkSize);
        bufferWriter.WriteUInt32(input.DataFactSize);
    }

    public FactChunk Load()
    {
        var result = new FactChunk();
        result.ChunkSize = bufferReader.ReadUInt32();
        result.DataFactSize = bufferReader.ReadUInt32();
        return result;
    }
}