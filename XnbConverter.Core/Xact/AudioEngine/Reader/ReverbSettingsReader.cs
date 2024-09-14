using System;
using XnbConverter.Readers;
using XnbConverter.Xact.AudioEngine.Entity;

namespace XnbConverter.Xact.AudioEngine.Reader;

public class ReverbSettingsReader : BaseReader
{
	private readonly DspParameterReader dspParameterReader = new DspParameterReader();

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		dspParameterReader.Init(resolver);
	}

	public override bool IsValueType()
	{
		throw new NotImplementedException();
	}

	public override object Read()
	{
		ReverbSettings reverbSettings = new ReverbSettings();
		reverbSettings.Parameters = new DspParameter[22]
		{
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read(),
			(DspParameter)dspParameterReader.Read()
		};
		return reverbSettings;
	}

	public override void Write(object input)
	{
		throw new NotImplementedException();
	}
}
