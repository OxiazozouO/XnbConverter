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
		ReverbSettings reverbSettings = (ReverbSettings)input;
		for (int i = 0; i < reverbSettings.Parameters.Length; i++)
		{
			dspParameterReader.Write(reverbSettings.Parameters[i]);
		}
	}
}
