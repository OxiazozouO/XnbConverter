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

	/// <summary>
	/// 新增的 Cue 里要**循环播放**的那些（Cue 名）。原版音乐就是循环的 ——
	/// 实测 <c>spring_day_ambient</c> 的形状是「**简单** Cue（<c>SoundEntry.Flags=0x04</c>）→
	/// 带 Clip 的 Sound（<c>Flags=0x01</c>、<c>NumClips=1</c>）→ 一条类型 1 的事件、
	/// <c>LoopCount=255</c>」。
	/// <para>
	/// ⚠ 复杂 Cue + 变体类型 0（直接引用波形）那条路**表达不了循环**：MonoGame 那边拿到的是
	/// 一个不循环的 <c>SoundEffectInstance</c>。所以循环的走简单 Cue 这条路。
	/// </para>
	/// </summary>
	public List<string> LoopedCues = new List<string>();

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

		ApplyAddedCues(soundBank);
	}

	/// <summary>
	/// <see cref="SoundView.FileOffset"/> / <see cref="VariationView.FileOffset"/> 为 0
	/// 的条目表示**新增的 Cue**，append 到 <see cref="SoundBank.Cues"/>（FileOffset 留 0，
	/// 由 <c>SoundBankReader.Build</c> 走追加路径）。
	///
	/// 与既有条目一样用「Cue 名」把 Sound 和它的变体对起来；新 Cue 一律写成
	/// 复杂 Cue + 变体类型 0（直接引用波形），所以只需要一个变体。
	/// </summary>
	private void ApplyAddedCues(SoundBank soundBank)
	{
		Dictionary<string, VariationView> byCue = new Dictionary<string, VariationView>();
		foreach (VariationView variation in Variations)
		{
			if (variation.FileOffset == 0 && !string.IsNullOrEmpty(variation.Cue))
			{
				byCue[variation.Cue] = variation;
			}
		}

		foreach (SoundView sound in Sounds)
		{
			if (sound.FileOffset != 0)
			{
				continue;
			}

			if (string.IsNullOrEmpty(sound.Cue))
			{
				throw new XnbError("新增的 Sound 缺少 Cue 名。");
			}

			if (!byCue.TryGetValue(sound.Cue, out VariationView variation))
			{
				throw new XnbError($"新增的 Cue「{sound.Cue}」没有对应的变体（Variations 里缺少同名条目）。");
			}

			if (LoopedCues.Contains(sound.Cue, StringComparer.OrdinalIgnoreCase))
			{
				AddLoopedSimpleCue(soundBank, sound, variation);
				continue;
			}

			SoundBank.SoundCue cue = new SoundBank.SoundCue
			{
				// FileOffset 保持 0 —— 这是「新增」的标记
				Name = sound.Cue,
				Flags = 0,
				VariationFlags = 0,
				InstanceLimit = 0,
				FadeInSec = 0,
				FadeOutSec = 0,
				InstanceFlags = 0
			};
			cue.CueVariations.Add(new SoundBank.SoundCue.CueVariation
			{
				TrackIndex = variation.TrackIndex,
				WaveBankIndex = (byte)variation.WaveBankIndex,
				BWeightMin = 0,
				BWeightMax = 0
			});
			soundBank.Cues.Add(cue);
		}
	}

	/// <summary>
	/// 追加一条**简单** Cue：<c>SoundEntry</c>（<c>Flags=0x04</c>）+ 带 Clip 的 <c>XactSound</c>
	/// （<c>Flags=0x01</c>、<c>NumClips=1</c>、一条类型 1 且 <c>LoopCount=255</c> 的事件）。
	/// 常量全部照抄原版的音乐 Cue（实测 <c>spring_day_ambient</c>）。
	/// </summary>
	private static void AddLoopedSimpleCue(SoundBank soundBank, SoundView view, VariationView variation)
	{
		if (soundBank._sounds.ContainsKey(view.Cue))
		{
			throw new XnbError($"新增的循环 Cue「{view.Cue}」与已有的 Cue 重名。");
		}

		var wave = new XactClip.WaveIndex
		{
			EventInfo = 0x20000001,   // 类型 1：单条播放
			EventFlags = 0x0C,
			LoopCount = 255,   // XACT 的「无限」（原版音乐就是 255）
			WaveBankIndex = variation.WaveBankIndex,
			TrackIndex = variation.TrackIndex,
		};
		wave.unkn.Add(new byte[] { 0xFF });

		var clip = new XactClip
		{
			VolumeDb = 180,
			FilterQAndFlags = 0x5DC0,
			FilterFrequency = 1000,
			WaveIndexs = new[] { wave },
		};

		var sound = new XactSound
		{
			// FileOffset 保持 0 —— 这是「新增」的标记（追加器会整条写出来）
			Flags = 0x01,
			CategoryId = view.CategoryId,
			VolumeFlag = view.VolumeFlag,
			Pitch = view.Pitch,
			Priority = view.Priority,
			Filter = view.Filter,
			NumClips = 1,
			SoundClips = new[] { clip },
		};

		soundBank.SoundEntrys.Add(new SoundBank.SoundEntry
		{
			Name = view.Cue,
			Flags = 0x04,
			SoundOffset = 0,
			FileOffset = 0,
		});
		soundBank._sounds[view.Cue] = new[] { sound };
	}
}
