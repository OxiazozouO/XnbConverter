using XnbConverter.Xact.AudioEngine.Entity;
using XnbConverter.Xact.AudioEngine.Reader;
using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;

namespace XnbConverter;

public static class XACT
{
	public static List<WaveBank>? Load(List<(string, string)> files)
	{
		AudioEngine audioEngine = null;
		SoundBank soundBank = null;
		List<WaveBank> list = new List<WaveBank>();
		foreach (var (path, text) in files)
		{
			switch (Path.GetExtension(path))
			{
			case ".xgs":
				audioEngine = AudioEngineReader.Read(path);
				break;
			case ".xsb":
				soundBank = SoundBankReader.Read(path);
				break;
			case ".xwb":
			{
				WaveBank waveBank = WaveBankReader.Read(path);
				if (waveBank == null)
				{
					break;
				}
				string filePath = text + "\\";
				foreach (WaveBank.WaveBankEntry entry in waveBank.Entries)
				{
					entry.FilePath = filePath;
				}
				list.Add(waveBank);
				break;
			}
			}
		}
		if (list.Count == 0)
		{
			return null;
		}
		if (soundBank == null || audioEngine == null)
		{
			foreach (WaveBank item in list)
			{
				for (int i = 0; i < item.Entries.Count; i++)
				{
					item.Entries[i].FileName = $"{i:x8}";
				}
			}
			return list;
		}
		int count = soundBank.WaveBankNames.Count;
		WaveBank[] array = new WaveBank[count];
		for (int j = 0; j < count; j++)
		{
			string text2 = soundBank.WaveBankNames[j];
			foreach (WaveBank item2 in list)
			{
				if (item2.Data.BankName == text2)
				{
					array[j] = item2;
				}
			}
		}
		HashSet<(int, int, string)> hashSet = new HashSet<(int, int, string)>();
		AudioCategory[] categories = audioEngine._categories;
		string[] array2 = new string[categories.Length];
		for (int k = 0; k < array2.Length; k++)
		{
			array2[k] = categories[k].name;
		}
		foreach (KeyValuePair<string, XactSound[]> sound in soundBank._sounds)
		{
			sound.Deconstruct(out var key, out var value);
			string fileName = key;
			XactSound[] array3 = value;
			hashSet.Clear();
			value = array3;
			foreach (XactSound xactSound in value)
			{
				if (xactSound?.SoundClips != null && xactSound.SoundClips.Length != 0)
				{
					XactClip[] soundClips = xactSound.SoundClips;
					foreach (XactClip xactClip in soundClips)
					{
						if (xactClip.WaveIndexs.Length == 1)
						{
							XactClip.WaveIndex waveIndex = xactClip.WaveIndexs[0];
							hashSet.Add((waveIndex.WaveBankIndex, waveIndex.TrackIndex, array2[xactSound.CategoryId]));
							continue;
						}
						throw new NotImplementedException();
					}
				}
				else
				{
					hashSet.Add((xactSound.WaveBankIndex, xactSound.TrackIndex, array2[xactSound.CategoryId]));
				}
			}
			int num = 0;
			foreach (var item3 in hashSet)
			{
				WaveBank.WaveBankEntry waveBankEntry = array[item3.Item1].Entries[item3.Item2];
				if (waveBankEntry.FileName == null)
				{
					waveBankEntry.FilePath = waveBankEntry.FilePath + item3.Item3 + "\\";
					if (!Directory.Exists(waveBankEntry.FilePath))
					{
						Directory.CreateDirectory(waveBankEntry.FilePath);
					}
					waveBankEntry.FileName = fileName;
					if (array3.Length > 1)
					{
						waveBankEntry.FileName = waveBankEntry.FileName + "_" + num++;
					}
				}
			}
		}
		return list;
	}

	public static void Save(this List<WaveBank> waveBanks)
	{
		foreach (WaveBank waveBank in waveBanks)
		{
			WaveBankReader.Save(waveBank);
		}
	}
}
