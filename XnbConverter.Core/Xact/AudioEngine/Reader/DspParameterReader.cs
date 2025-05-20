using XnbConverter.Readers;
using XnbConverter.Xact.AudioEngine.Entity;

namespace XnbConverter.Xact.AudioEngine.Reader;

public class DspParameterReader : BaseReader
{
	public override bool IsValueType()
	{
		throw new NotImplementedException();
	}

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
		throw new NotImplementedException();
	}
}
