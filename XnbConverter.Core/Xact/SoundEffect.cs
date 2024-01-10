using Newtonsoft.Json;
using XnbConverter.Xact.WaveBank.Entity;

namespace XnbConverter.Xact;

public class SoundEffect
{

    public WaveForm WaveForm = new WaveForm();

    public ExData exData = new ExData();
    public class ExData
    {
        public uint LoopStart;
        public uint LoopLength;
        public uint DurationMs;
    }

    public void Save(string json, string wavPath)
    {
        File.WriteAllText(json, JsonConvert.SerializeObject(exData, Formatting.Indented));
        SoundEffectReader.Save(this,wavPath);
    }

    public static SoundEffect FormWave(string json, string path)
    {
        var soundEffect = SoundEffectReader.FormFile(path);
        soundEffect.exData = JsonConvert.DeserializeObject<ExData>(File.ReadAllText(json));
        return soundEffect;
    }
}