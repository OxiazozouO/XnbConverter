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
		Dictionary<string, List<VariationView>> byCue =
			new Dictionary<string, List<VariationView>>(StringComparer.OrdinalIgnoreCase);
		foreach (VariationView variation in Variations)
		{
			if (variation.FileOffset != 0 || string.IsNullOrEmpty(variation.Cue))
			{
				continue;
			}

			if (!byCue.TryGetValue(variation.Cue, out List<VariationView>? list))
			{
				byCue[variation.Cue] = list = new List<VariationView>();
			}

			list.Add(variation);
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

			if (!byCue.TryGetValue(sound.Cue, out List<VariationView>? variations) || variations.Count == 0)
			{
				throw new XnbError($"新增的 Cue「{sound.Cue}」没有对应的变体（Variations 里缺少同名条目）。");
			}

			bool loop = LoopedCues.Contains(sound.Cue, StringComparer.OrdinalIgnoreCase);
			if (loop || variations.Count > 1)
			{
				AddSimpleCue(soundBank, sound, variations, loop);
				continue;
			}

			VariationView variation = variations[0];
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
	/// （<c>Flags=0x01</c>、<c>NumClips=1</c>）。
	/// <para>
	/// 单条变体照抄原版音乐：类型 1 事件、<c>LoopCount=255</c>。
	/// 多条变体（CP 的多个 <c>FilePaths</c>）写成一个类型 6 事件，事件里列出
	/// 所有 <c>(TrackIndex, WaveBankIndex, weight)</c>，变体类型 = Random；
	/// MonoGame 的 <c>PlayWaveEvent</c> 会在每次 Play 时按权重随机挑一条。
	/// </para>
	/// </summary>
	private static void AddSimpleCue(SoundBank soundBank, SoundView view,
		IReadOnlyList<VariationView> variations, bool loop)
	{
		if (soundBank._sounds.ContainsKey(view.Cue))
		{
			throw new XnbError($"新增的 Cue「{view.Cue}」与已有的 Cue 重名。");
		}

		XactClip.WaveIndex wave;
		if (variations.Count == 1)
		{
			VariationView variation = variations[0];
			wave = new XactClip.WaveIndex
			{
				EventInfo = 0x20000001,   // 类型 1：单条播放
				EventFlags = 0x0C,
				LoopCount = (byte)(loop ? 255 : 0),
				WaveBankIndex = variation.WaveBankIndex,
				TrackIndex = variation.TrackIndex,
			};
			wave.unkn.Add(new byte[] { 0xFF });
		}
		else
		{
			wave = new XactClip.WaveIndex
			{
				EventInfo = 0x20000006,   // 类型 6：多轨随机变体
				EventFlags = 0x0C,
				LoopCount = (byte)(loop ? 255 : 0),
				NumTracks = (ushort)variations.Count,
				VariationFlags = 0,
				MoreFlags = 2,             // VariationType.Random
				Tracks = variations.Select(v => (int)v.TrackIndex).ToArray(),
				WaveBanks = variations.Select(v => (int)v.WaveBankIndex).ToArray(),
				// MonoGame 的选择是 Next(totalWeight + 1)，不是标准的按权重区间取模：
				// 全 1 权重会让第一个变体拿到 2/(N+1) 而不是 1/N。
				// 用 [1, 2, 2, …] 把总权重凑成 2N−1、结果数 2N，每个变体刚好 2 个结果。
				Weights = variations.Select((_, i) => i == 0 ? new byte[] { 0, 1 } : new byte[] { 0, 2 }).ToArray(),
				MinVolumeDecibels = 180,
				MaxVolumeDecibels = 180,
			};
			// 类型 6 读取器会各吃掉 1、1、5 个未使用字节；MonoGame 也直接跳过。
			wave.unkn.Add(new byte[] { 0xFF });
			wave.unkn.Add(new byte[] { 0 });
			wave.unkn.Add(new byte[5]);
		}

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
