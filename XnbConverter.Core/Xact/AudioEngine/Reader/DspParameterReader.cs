using XnbConverter.Readers;
using XnbConverter.Xact.AudioEngine.Entity;

namespace XnbConverter.Xact.AudioEngine.Reader;

public class DspParameterReader : BaseReader
{

	public override object Read()
	{
		return new DspParameter
		{
			unkn1 = bufferReader.ReadByte(),
			Value = bufferReader.ReadSingle(),
			MinValue = bufferReader.ReadSingle(),
			MaxValue = bufferReader.ReadSingle(),
			unkn2 = bufferReader.ReadUInt16()
		};
	}

	public override void Write(object input)
	{
		DspParameter dspParameter = (DspParameter)input;
		bufferWriter.WriteByte(dspParameter.unkn1);
		bufferWriter.WriteSingle((float)dspParameter.Value);
		bufferWriter.WriteSingle((float)dspParameter.MinValue);
		bufferWriter.WriteSingle((float)dspParameter.MaxValue);
		bufferWriter.WriteUInt16(dspParameter.unkn2);
	}
}
