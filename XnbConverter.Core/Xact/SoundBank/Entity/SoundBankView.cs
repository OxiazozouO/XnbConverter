using XnbConverter.Exceptions;

namespace XnbConverter.Xact.SoundBank.Entity;

/// <summary>
/// .xsb 的可编辑视图：把「Cue 名 -&gt; 波形」的映射和每个 Sound 的可调参数摊平成列表。
/// 条目用文件偏移（<see cref="SoundView.FileOffset"/>）作为稳定标识，
/// 打包时按它套回重新解析出的实体，再就地写回。
/// </summary>
public class SoundBankView
{
	public class SoundView
	{
		/// <summary>该 Sound 在 .xsb 中的偏移，作为唯一定位键，不要改。</summary>
		public uint FileOffset;

		/// <summary>所属 Cue 名，仅供阅读。</summary>
		public string Cue;

		public ushort CategoryId;

		public byte VolumeFlag;

		public float Pitch;

		public byte Priority;

		public ushort Filter;
	}

	public class VariationView
	{
		public uint FileOffset;

		public string Cue;

		public ushort WaveBankIndex;

		public ushort TrackIndex;
	}

	public ushort FormatVersion = 43;

	public List<string> WaveBankNames = new List<string>();

	public List<SoundView> Sounds = new List<SoundView>();

	public List<VariationView> Variations = new List<VariationView>();

	public static SoundBankView From(SoundBank soundBank)
	{
		SoundBankView soundBankView = new SoundBankView
		{
			FormatVersion = soundBank.Header.FormatVersion
		};
		soundBankView.WaveBankNames.AddRange(soundBank.WaveBankNames);

		HashSet<uint> hashSet = new HashSet<uint>();
		foreach (KeyValuePair<string, XactSound[]> sound in soundBank._sounds)
		{
			foreach (XactSound xactSound in sound.Value)
			{
				if (xactSound != null && hashSet.Add(xactSound.FileOffset))
				{
					soundBankView.Sounds.Add(new SoundView
					{
						FileOffset = xactSound.FileOffset,
						Cue = sound.Key,
						CategoryId = xactSound.CategoryId,
						VolumeFlag = xactSound.VolumeFlag,
						Pitch = xactSound.Pitch,
						Priority = xactSound.Priority,
						Filter = xactSound.Filter
					});
				}
			}
		}

		foreach (SoundBank.SoundCue cue in soundBank.Cues)
		{
			foreach (SoundBank.SoundCue.CueVariation cueVariation in cue.CueVariations)
			{
				soundBankView.Variations.Add(new VariationView
				{
					FileOffset = cueVariation.FileOffset,
					WaveBankIndex = cueVariation.WaveBankIndex,
					TrackIndex = cueVariation.TrackIndex
				});
			}
		}

		return soundBankView;
	}

	/// <summary>把视图里的值套回实体（实体应当是刚 Read 出来的，偏移完整）。</summary>
	public void ApplyTo(SoundBank soundBank)
	{
		if (WaveBankNames.Count != soundBank.Header.NumWaveBanks)
		{
			throw new XnbError($"声库数由 {soundBank.Header.NumWaveBanks} 变为 {WaveBankNames.Count}，当前只支持同尺寸修改");
		}

		soundBank.WaveBankNames = new List<string>(WaveBankNames);

		Dictionary<uint, SoundView> dictionary = new Dictionary<uint, SoundView>();
		foreach (SoundView sound in Sounds)
		{
			dictionary[sound.FileOffset] = sound;
		}

		int num = 0;
		foreach (KeyValuePair<string, XactSound[]> item in soundBank._sounds)
		{
			foreach (XactSound xactSound in item.Value)
			{
				if (xactSound == null || !dictionary.TryGetValue(xactSound.FileOffset, out SoundView value))
				{
					continue;
				}

				xactSound.CategoryId = value.CategoryId;
				xactSound.VolumeFlag = value.VolumeFlag;
				xactSound.Pitch = value.Pitch;
				xactSound.Priority = value.Priority;
				xactSound.Filter = value.Filter;
				num++;
			}
		}

		Dictionary<uint, VariationView> dictionary2 = new Dictionary<uint, VariationView>();
		foreach (VariationView variation in Variations)
		{
			dictionary2[variation.FileOffset] = variation;
		}

		foreach (SoundBank.SoundCue cue in soundBank.Cues)
		{
			foreach (SoundBank.SoundCue.CueVariation cueVariation in cue.CueVariations)
			{
				if (dictionary2.TryGetValue(cueVariation.FileOffset, out VariationView value2))
				{
					cueVariation.WaveBankIndex = (byte)value2.WaveBankIndex;
					cueVariation.TrackIndex = value2.TrackIndex;
				}
			}
		}
	}
}
