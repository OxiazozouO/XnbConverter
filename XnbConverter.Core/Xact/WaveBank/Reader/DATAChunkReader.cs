using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact.WaveBank.Reader;

public class DATAChunkReader: BaseReader,IReaderFileUtil<DATAChunk>
{
    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override DATAChunk Read()
    {
        DATAChunk result = new DATAChunk();

        result.DataSize = bufferReader.ReadUInt32();
        result.Data = bufferReader.Read((int)result.DataSize);
        
        return result;
    }

    public override void Write(object input)
    {
        DATAChunk dataChunk = (DATAChunk)input;
        
        bufferWriter.WriteUInt32(dataChunk.DataSize);
        bufferWriter.Write(dataChunk.Data);
    }

    public void Save(DATAChunk dataChunk)
    {
        bufferWriter.WriteAsciiString(dataChunk.DataID);
        bufferWriter.WriteUInt32(dataChunk.DataSize);
        bufferWriter.Write(dataChunk.Data);
    }

    public DATAChunk Load()
    {
        DATAChunk result = new DATAChunk();
        result.DataSize = bufferReader.ReadUInt32();
        result.Data = bufferReader.Read((int)result.DataSize);
        return result;
    }
}