using System;
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
        var result = new Chunk();

        result.Id = bufferReader.ReadString(4);
        result.Size = bufferReader.ReadUInt32();

        return result;
    }

    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override Chunk Read()
    {
        var result = new Chunk();

        result.Id = bufferReader.ReadString(4);
        result.Size = bufferReader.ReadUInt32();

        return result;
    }

    public override void Write(object input)
    {
        var chunk = (Chunk)input;
        bufferWriter.WriteAsciiString(chunk.Id);
        bufferWriter.WriteUInt32(chunk.Size);
    }
}