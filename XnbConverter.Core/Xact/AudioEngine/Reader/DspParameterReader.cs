using XnbConverter.Readers;
using XnbConverter.Xact.AudioEngine.Entity;

namespace XnbConverter.Xact.AudioEngine.Reader;

public class DspParameterReader : BaseReader
{
    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override DspParameter Read()
    {
        DspParameter result = new DspParameter();
        
        result.unkn1 = bufferReader.ReadByte();
        result.Value = bufferReader.ReadSingle();
        result.MinValue = bufferReader.ReadSingle();
        result.MaxValue = bufferReader.ReadSingle();
        result.unkn2 = bufferReader.ReadUInt16();

        return result;
    }

    public override void Write(object input)
    {
        throw new NotImplementedException();
    }
}