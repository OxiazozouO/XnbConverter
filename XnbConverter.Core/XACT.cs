using XnbConverter.Xact.AudioEngine.Entity;
using XnbConverter.Xact.AudioEngine.Reader;
using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;

namespace XnbConverter;

public static class XACT
{
    public static List<WaveBank>? Load(List<(string,string)> files)
    {
        AudioEngine? audioEngine = null;
        SoundBank? soundBank = null;
        List<WaveBank> waveBanks = new List<WaveBank>();
        foreach (var (input, output) in files)
        {
            string ext = Path.GetExtension(input);
            switch (ext)
            {
                case ".xgs":
                    audioEngine = AudioEngineReader.Read(input);
                    break;
                case ".xsb":
                    soundBank = SoundBankReader.Read(input);
                    break;
                case ".xwb":
                    var waveBank = WaveBankReader.Read(input);
                    if (waveBank is not null)
                    {
                        string replace = output + "\\";
                        foreach (var w in waveBank.Entries)
                            w.FilePath = replace;
                        waveBanks.Add(waveBank);
                    }
                    break;
            }
        }
        
        if(waveBanks.Count == 0) return null;
        if (soundBank is null || audioEngine is null)//名字设定为整数
        {
            foreach (var waveBank in waveBanks)
            {
                for (var i = 0; i < waveBank.Entries.Count; i++)
                    waveBank.Entries[i].FileName = $"{i:x8}";
            }
            return waveBanks;
        }

        int size = soundBank.WaveBankNames.Count;
        WaveBank?[] arr = new WaveBank?[size];
        for (var index = 0; index < size; index++)
        {
            var name = soundBank.WaveBankNames[index];
            foreach (var t in waveBanks)
            {
                if (t.Data.BankName == name)
                {
                    arr[index] = t;
                }
            }
        }
        
        var trackSet = new HashSet<(int, int,string)>();
        string[] categoryName = audioEngine._categories.Select(t => t.name).ToArray();
        foreach (var (key,value) in soundBank._sounds)
        {
            trackSet.Clear();
            foreach (var xactSound in value)
            {
                if (xactSound?.SoundClips is not null && xactSound.SoundClips.Length > 0)
                {
                    foreach (var xactClip in xactSound.SoundClips)
                    {
                        if (xactClip.WaveIndexs.Length == 1)
                        {
                            var v = xactClip.WaveIndexs[0];
                            trackSet.Add((v.WaveBankIndex, v.TrackIndex,
                                categoryName[xactSound.CategoryId]
                                ));
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }
                    }
                }
                else
                {
                    trackSet.Add((xactSound.WaveBankIndex, xactSound.TrackIndex,categoryName[xactSound.CategoryId]));
                }
            }
            
            int j = 0;
            foreach (var track in trackSet)
            {
                var entre = arr[track.Item1].Entries[track.Item2];
                if (entre.FileName != null)
                    continue;
                entre.FilePath += track.Item3 + "\\";
                if (!Directory.Exists(entre.FilePath)) //如果不存在就创建文件夹
                    Directory.CreateDirectory(entre.FilePath); //创建该文件夹
                entre.FileName = key;
                if(value.Length > 1)
                    entre.FileName += "_" + j++;
            }
        }
        return waveBanks;
    }

    public static void Save(this List<WaveBank> waveBanks)
    {
        foreach (var waveBank in waveBanks)
            WaveBankReader.Save(waveBank);
    }
}