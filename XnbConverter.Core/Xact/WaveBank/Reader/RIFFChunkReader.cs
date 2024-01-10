﻿using System.Text;
using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact.WaveBank.Reader;

public class RIFFChunkReader: BaseReader,IReaderFileUtil<RIFFChunk>
{
    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override RIFFChunk Read()
    {
        RIFFChunk result = new RIFFChunk();
        
        result.ChunkSize = bufferReader.ReadUInt32();
        
        return result;
    }

    public override void Write(object input)
    {
        RIFFChunk chunk = (RIFFChunk)input;
        
        bufferWriter.WriteUInt32(chunk.ChunkSize);
    }

    public void Save(RIFFChunk riffChunk)
    {
        bufferWriter.WriteAsciiString(riffChunk.ChunkID);
        bufferWriter.WriteUInt32(riffChunk.ChunkSize);
        bufferWriter.WriteAsciiString(riffChunk.Format);
    }

    public RIFFChunk Load()
    {
        RIFFChunk result = new RIFFChunk();

        result.ChunkID   = bufferReader.ReadString(4);
        result.ChunkSize = bufferReader.ReadUInt32();
        result.Format = bufferReader.ReadString(4);
        
        if (result.ChunkID !=WaveMarks.RIFF)
            throw new XnbError("无效的wav格式：{0}，应为RIFF",result.ChunkID);
        if (result.Format != WaveMarks.WAVE)
            throw new XnbError("无效的wav格式：{0}，应为WAVE", result.Format);
        return result;
    }
}