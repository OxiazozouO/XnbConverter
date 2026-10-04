using XnbConverter.Configurations;
using XnbConverter.Xact.AudioEngine.Entity;
using XnbConverter.Xact.AudioEngine.Reader;
using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.SoundBank.Reader;
using XnbConverter.Xact.WaveBank.Entity;
using XnbConverter.Xact.WaveBank.Reader;
using XnbConverter.Xact.WaveBank.Writer;

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
				waveBank.OutputPath = text;
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
				// 声库可能引用了本次没有一起提供的波形库（或索引越界），跳过而不是直接崩
				WaveBank sourceBank = array[item3.Item1];
				if (sourceBank == null || item3.Item2 < 0 || item3.Item2 >= sourceBank.Entries.Count)
				{
					Logger.Warn($"声库 '{fileName}' 引用了缺失的波形库 #{item3.Item1} 条目 #{item3.Item2}，已跳过");
					continue;
				}

				WaveBank.WaveBankEntry waveBankEntry = sourceBank.Entries[item3.Item2];
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

	/// <summary>
	/// 与 <see cref="Load"/> 对称：按解包目录里的 "*.xwb.config" 清单重建各个 .xwb。
	/// 采用同尺寸替换，条目的 wav 载荷长度必须与清单记录一致。
	/// </summary>
	public static int Pack(IEnumerable<(string ManifestPath, string OutputPath)> files)
	{
		int num = 0;
		foreach ((string manifestPath, string outputPath) in files)
		{
			WaveBankWriter.Write(manifestPath, outputPath);
			num++;
		}

		return num;
	}
}
