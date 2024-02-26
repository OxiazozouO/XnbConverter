using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact.WaveBank.Reader;

public class FmtChunkReader : BaseReader, IReaderFileUtil<FmtChunk>
{
    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override FmtChunk Read()
    {
        var result = new FmtChunk();

        result.FmtSize = bufferReader.ReadUInt32();
        result.FmtTag = (FmtChunk.AudioFormats)bufferReader.ReadInt16();
        result.NumChannels = bufferReader.ReadUInt16();
        result.SampleRate = bufferReader.ReadUInt32();
        result.ByteRate = bufferReader.ReadUInt32();
        result.BlockAlign = bufferReader.ReadUInt16();
        result.BitsPerSample = bufferReader.ReadUInt16();

        uint tmp = bufferReader.PeekUInt16();
        if (tmp == 0)
            result.CbSize = bufferReader.ReadUInt16();
        return result;
    }

    public override void Write(object input)
    {
        var fmtChunk = (FmtChunk)input;
        bufferWriter.WriteUInt32(fmtChunk.FmtSize);
        bufferWriter.WriteInt16((short)fmtChunk.FmtTag);
        bufferWriter.WriteUInt16(fmtChunk.NumChannels);
        bufferWriter.WriteUInt32(fmtChunk.SampleRate);
        bufferWriter.WriteUInt32(fmtChunk.ByteRate);
        bufferWriter.WriteUInt16(fmtChunk.BlockAlign);
        bufferWriter.WriteUInt16(fmtChunk.BitsPerSample);
        if (fmtChunk.CbSize == 0)
            bufferWriter.WriteUInt16(fmtChunk.CbSize.Value);
    }

    public void Save(FmtChunk fmtChunk)
    {
        bufferWriter.WriteAsciiString(fmtChunk.FmtID);
        bufferWriter.WriteUInt32(fmtChunk.FmtSize);
        bufferWriter.WriteInt16((short)fmtChunk.FmtTag);
        bufferWriter.WriteUInt16(fmtChunk.NumChannels);
        bufferWriter.WriteUInt32(fmtChunk.SampleRate);
        bufferWriter.WriteUInt32(fmtChunk.ByteRate);
        bufferWriter.WriteUInt16(fmtChunk.BlockAlign);
        bufferWriter.WriteUInt16(fmtChunk.BitsPerSample);

        switch (fmtChunk.CbSize)
        {
            case null:
                return;
            case 0:
                return;
            default:
                bufferWriter.WriteUInt16(fmtChunk.CbSize.Value);
                bufferWriter.Write(fmtChunk.ExtensionChunkData);
                break;
        }
    }

    public FmtChunk Load()
    {
        var fmtChunk = new FmtChunk();

        fmtChunk.FmtID = bufferReader.ReadString(4);
        fmtChunk.FmtSize = bufferReader.ReadUInt32();
        fmtChunk.FmtTag = (FmtChunk.AudioFormats)bufferReader.ReadInt16();
        fmtChunk.NumChannels = bufferReader.ReadUInt16();
        fmtChunk.SampleRate = bufferReader.ReadUInt32();
        fmtChunk.ByteRate = bufferReader.ReadUInt32();
        fmtChunk.BlockAlign = bufferReader.ReadUInt16();
        fmtChunk.BitsPerSample = bufferReader.ReadUInt16();

        if (fmtChunk.FmtID != WaveMarks.fmt)
            throw new XnbError("无效的wav格式：{0}，应为fmt", fmtChunk.FmtID);
        if (fmtChunk.FmtSize < FmtChunk.defSize)
            throw new Exception("fmt chunk size is too small");

        uint tmp = bufferReader.PeekUInt16();
        switch (tmp)
        {
            case 0:
                fmtChunk.CbSize = bufferReader.ReadUInt16();
                break;
            case 32:
                fmtChunk.CbSize = bufferReader.ReadUInt16();
                fmtChunk.ExtensionChunkData = bufferReader.Read((int)fmtChunk.CbSize);
                break;
        }

        return fmtChunk;
    }
}