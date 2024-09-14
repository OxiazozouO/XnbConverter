using XnbConverter.Utilities;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact;

public class SoundEffect
{
	public class ExData
	{
		public uint DurationMs;

		public uint LoopLength;

		public uint LoopStart;
	}

	public ExData exData = new ExData();

	public WaveForm WaveForm = new WaveForm();

	public void Save(string json, string wavPath)
	{
		exData.ToJson(json);
		SoundEffectReader.Save(this, wavPath);
	}

	public static SoundEffect FormWave(string json, string path)
	{
		SoundEffect soundEffect = SoundEffectReader.FormFile(path);
		soundEffect.exData = json.ToEntity<ExData>();
		return soundEffect;
	}
}
