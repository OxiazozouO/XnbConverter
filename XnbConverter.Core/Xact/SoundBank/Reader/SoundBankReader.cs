using System.Text;
using XnbConverter.Configurations;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Xact.SoundBank.Entity;

namespace XnbConverter.Xact.SoundBank.Reader;

public class SoundBankReader : BaseReader
{
	public XactSoundReader xactSoundReader = new XactSoundReader();

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		xactSoundReader.Init(resolver);
	}

	public static XnbConverter.Xact.SoundBank.Entity.SoundBank Read(string path)
	{
		SoundBankReader soundBankReader = new SoundBankReader();
		soundBankReader.Init(new ReaderResolver
		{
			bufferReader = BufferReader.FormFile(path)
		});
		return (XnbConverter.Xact.SoundBank.Entity.SoundBank)soundBankReader.Read();
	}


	public override object Read()
	{
		XnbConverter.Xact.SoundBank.Entity.SoundBank soundBank = new XnbConverter.Xact.SoundBank.Entity.SoundBank();
		soundBank.OriginalBytes = bufferReader.Buffer.AsSpan(0, bufferReader.Size).ToArray();
		XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundBankHeader header = soundBank.Header;
		header.Magic = bufferReader.ReadString(4);
		if (header.Magic != "SDBK")
		{
			throw new XnbError(Error.AudioEngineReader_1, header.Magic);
		}
		header.ToolVersion = bufferReader.ReadUInt16();
		header.FormatVersion = bufferReader.ReadUInt16();
		Logger.Debug(Error.AudioEngineReader_3, header.FormatVersion);
		Logger.Debug(Error.AudioEngineReader_2, header.ToolVersion);
		if (header.FormatVersion != 43)
		{
			Logger.Warn(Error.SoundBankReader_25, header.FormatVersion);
		}
		header.Crc = bufferReader.ReadUInt16();
		Logger.Debug(Error.SoundBankReader_1, header.Crc);
		header.LastModifiedLow = bufferReader.ReadUInt32();
		header.LastModifiedHigh = bufferReader.ReadUInt32();
		header.Platform = bufferReader.ReadByte();
		Logger.Debug(Error.SoundBankReader_2, header.LastModifiedLow, header.LastModifiedHigh);
		Logger.Debug(Error.SoundBankReader_3, header.Platform);
		header.NumSimpleCues = bufferReader.ReadUInt16();
		header.NumComplexCues = bufferReader.ReadUInt16();
		header.Unknowns.Add(bufferReader.Read(2));
		header.NumTotalCues = bufferReader.ReadUInt16();
		header.NumWaveBanks = bufferReader.ReadByte();
		header.NumSounds = bufferReader.ReadUInt16();
		header.CueNameTableLen = bufferReader.ReadUInt16();
		header.Unknowns.Add(bufferReader.Read(2));
		Logger.Debug(Error.SoundBankReader_4, header.NumSimpleCues);
		Logger.Debug(Error.SoundBankReader_5, header.NumComplexCues);
		Logger.Debug(Error.SoundBankReader_6, header.NumTotalCues);
		Logger.Debug(Error.SoundBankReader_7, header.NumWaveBanks);
		Logger.Debug(Error.SoundBankReader_8, header.NumSounds);
		Logger.Debug(Error.SoundBankReader_9, header.CueNameTableLen);
		header.SimpleCuesOffset = bufferReader.ReadUInt32();
		header.ComplexCuesOffset = bufferReader.ReadUInt32();
		header.CueNamesOffset = bufferReader.ReadUInt32();
		header.Unknowns.Add(bufferReader.Read(4));
		header.VariationTablesOffset = bufferReader.ReadUInt32();
		header.Unknowns.Add(bufferReader.Read(4));
		header.WaveBankNameTableOffset = bufferReader.ReadUInt32();
		header.CueNameHashTableOffset = bufferReader.ReadUInt32();
		header.CueNameHashValsOffset = bufferReader.ReadUInt32();
		header.SoundsOffset = bufferReader.ReadUInt32();
		header.Name = bufferReader.ReadString(64);
		Logger.Debug(Error.SoundBankReader_10, header.SimpleCuesOffset);
		Logger.Debug(Error.SoundBankReader_11, header.ComplexCuesOffset);
		Logger.Debug(Error.SoundBankReader_12, header.CueNamesOffset);
		Logger.Debug(Error.SoundBankReader_13, header.VariationTablesOffset);
		Logger.Debug(Error.SoundBankReader_14, header.WaveBankNameTableOffset);
		Logger.Debug(Error.SoundBankReader_15, header.CueNameHashTableOffset);
		Logger.Debug(Error.SoundBankReader_16, header.CueNameHashValsOffset);
		Logger.Debug(Error.SoundBankReader_17, header.SoundsOffset);
		Logger.Debug(Error.SoundBankReader_18, header.Name);
		bufferReader.BytePosition = (int)header.WaveBankNameTableOffset;
		soundBank.WaveBankNames = new List<string>();
		for (int i = 0; i < header.NumWaveBanks; i++)
		{
			soundBank.WaveBankNames.Add(bufferReader.ReadString(64));
		}
		Logger.Debug(Error.SoundBankReader_19, soundBank.WaveBankNames.ToJoinStr());
		bufferReader.BytePosition = (int)header.CueNamesOffset;
		string[] array = bufferReader.ReadString(header.CueNameTableLen).Split('\0');
		if (header.NumSimpleCues > 0)
		{
			bufferReader.BytePosition = (int)header.SimpleCuesOffset;
			for (int j = 0; j < header.NumSimpleCues; j++)
			{
				XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry soundEntry = new XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry
				{
					FileOffset = (uint)bufferReader.BytePosition,
					Name = array[j],
					Flags = bufferReader.ReadByte(),
					SoundOffset = bufferReader.ReadUInt32()
				};
				int bytePosition = bufferReader.BytePosition;
				bufferReader.BytePosition = (int)soundEntry.SoundOffset;
				XactSound xactSound = (XactSound)xactSoundReader.Read();
				bufferReader.BytePosition = bytePosition;
				soundBank.SoundEntrys.Add(soundEntry);
				soundBank._sounds.Add(array[j], new XactSound[1] { xactSound });
			}
		}
		Logger.Debug(Error.SoundBankReader_20, array.ToJoinStr());
		int num = 0;
		if (header.NumComplexCues > 0)
		{
			bufferReader.BytePosition = (int)header.ComplexCuesOffset;
			for (int k = 0; k < header.NumComplexCues; k++)
			{
				XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue soundCue = new XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue();
				soundCue.FileOffset = (uint)bufferReader.BytePosition;
				soundCue.Flags = bufferReader.ReadByte();
				if (((uint)(soundCue.Flags >> 2) & (true ? 1u : 0u)) != 0)
				{
					soundCue.SoundOffset = bufferReader.ReadUInt32();
					soundCue.Unknowns.Add(bufferReader.Read(4));
					Logger.Debug(Error.SoundBankReader_21, array[header.NumSimpleCues + k]);
					int bytePosition2 = bufferReader.BytePosition;
					bufferReader.BytePosition = (int)soundCue.SoundOffset;
					XactSound xactSound2 = (XactSound)xactSoundReader.Read();
					bufferReader.BytePosition = bytePosition2;
					soundBank._sounds.Add(array[header.NumSimpleCues + k], new XactSound[1] { xactSound2 });
					num++;
				}
				else
				{
					soundCue.VariationTableOffset = bufferReader.ReadUInt32();
					soundCue.TransitionTableOffset = bufferReader.ReadUInt32();
					int bytePosition3 = bufferReader.BytePosition;
					bufferReader.BytePosition = (int)soundCue.VariationTableOffset;
					soundCue.NumEntries = bufferReader.ReadUInt16();
					soundCue.VariationFlags = bufferReader.ReadUInt16();
					soundCue.Unknowns.Add(bufferReader.Read(4));
					XactSound[] array2 = new XactSound[soundCue.NumEntries];
					Logger.Debug(Error.SoundBankReader_22, array[header.NumSimpleCues + k], soundCue.NumEntries);
					int num2 = (soundCue.VariationFlags >> 3) & 7;
					for (int l = 0; l < soundCue.NumEntries; l++)
					{
						XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue.CueVariation cueVariation = new XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue.CueVariation();
						cueVariation.FileOffset = (uint)bufferReader.BytePosition;
						switch (num2)
						{
						case 0:
							cueVariation.TrackIndex = bufferReader.ReadUInt16();
							cueVariation.WaveBankIndex = bufferReader.ReadByte();
							cueVariation.BWeightMin = bufferReader.ReadByte();
							cueVariation.BWeightMax = bufferReader.ReadByte();
							Logger.Debug(Error.SoundBankReader_23, cueVariation.WaveBankIndex);
							array2[l] = new XactSound
							{
								CueVariation = cueVariation
							};
							break;
						case 1:
						{
							cueVariation.SoundOffset = bufferReader.ReadUInt32();
							cueVariation.BWeightMin = bufferReader.ReadByte();
							cueVariation.BWeightMax = bufferReader.ReadByte();
							int bytePosition5 = bufferReader.BytePosition;
							bufferReader.BytePosition = (int)cueVariation.SoundOffset;
							array2[l] = (XactSound)xactSoundReader.Read();
							array2[l].CueVariation = cueVariation;
							bufferReader.BytePosition = bytePosition5;
							num++;
							break;
						}
						case 3:
						{
							cueVariation.SoundOffset = bufferReader.ReadUInt32();
							cueVariation.FWeightMin = bufferReader.ReadSingle();
							cueVariation.FWeightMax = bufferReader.ReadSingle();
							cueVariation.Flags = bufferReader.ReadUInt32();
							int bytePosition4 = bufferReader.BytePosition;
							bufferReader.BytePosition = (int)cueVariation.SoundOffset;
							array2[l] = (XactSound)xactSoundReader.Read();
							array2[l].CueVariation = cueVariation;
							bufferReader.BytePosition = bytePosition4;
							num++;
							break;
						}
						case 4:
							cueVariation.TrackIndex = bufferReader.ReadUInt16();
							cueVariation.WaveBankIndex = bufferReader.ReadByte();
							Logger.Debug(Error.SoundBankReader_23, cueVariation.WaveBankIndex);
							array2[l] = new XactSound
							{
								CueVariation = cueVariation
							};
							break;
						default:
							throw new NotImplementedException("未实现表类型");
						}
						soundCue.CueVariations.Add(cueVariation);
					}
					bufferReader.BytePosition = bytePosition3;
					soundBank._sounds.Add(array[header.NumSimpleCues + k], array2);
				}
				soundCue.InstanceLimit = bufferReader.ReadByte();
				soundCue.FadeInSec = bufferReader.ReadUInt16();
				soundCue.FadeOutSec = bufferReader.ReadUInt16();
				soundCue.InstanceFlags = bufferReader.ReadByte();
				soundCue.Name = array[header.NumSimpleCues + k];
				soundBank.Cues.Add(soundCue);
			}
		}
		Logger.Debug(Error.SoundBankReader_24, num);
		return soundBank;
	}

	public override void Write(object input)
	{
		Build((XnbConverter.Xact.SoundBank.Entity.SoundBank)input);
	}

	/// <summary>
	/// 写回 .xsb：以读入时的原始字节为模板，把已建模的记录（声库名表、简单/复杂 Cue、
	/// Cue 变体、各 Sound 的头部字段）按各自记录的原偏移就地修补。
	/// 声调表里未被解析的 ExtraData、哈希表等部分原样保留，因此只支持同尺寸修改。
	/// </summary>
	public static byte[] Build(XnbConverter.Xact.SoundBank.Entity.SoundBank soundBank)
	{
		if (soundBank.OriginalBytes == null)
		{
			throw new XnbError("缺少原始字节，无法写回（请先用 Read 读入）");
		}

		// FileOffset 为 0 的复杂 Cue 是新增的 ⇒ 走追加路径
		if (HasAddedCues(soundBank))
		{
			return BuildAppendingCues(soundBank);
		}

		return ApplyInPlace(soundBank);
	}

	/// <summary>是否有 FileOffset 为 0 的新增记录（复杂 Cue 或简单 Cue）。</summary>
	private static bool HasAddedCues(XnbConverter.Xact.SoundBank.Entity.SoundBank soundBank)
	{
		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue cue in soundBank.Cues)
		{
			if (cue.FileOffset == 0)
			{
				return true;
			}
		}

		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry entry in soundBank.SoundEntrys)
		{
			if (entry.FileOffset == 0)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// 写回 .xsb：以读入时的原始字节为模板，把已建模的记录（声库名表、简单/复杂 Cue、
	/// Cue 变体、各 Sound 的头部字段）按各自记录的原偏移就地修补。
	/// 声调表里未被解析的 ExtraData、哈希表等部分原样保留，因此只支持同尺寸修改。
	/// 要**加 Cue** 请走 <see cref="BuildAppendingCues"/>（<see cref="Build"/> 会自动分派）。
	/// </summary>
	private static byte[] ApplyInPlace(XnbConverter.Xact.SoundBank.Entity.SoundBank soundBank)
	{
		int existingSimpleCues = 0;
		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry entry in soundBank.SoundEntrys)
		{
			if (entry.FileOffset != 0)
			{
				existingSimpleCues++;
			}
		}

		if (existingSimpleCues != soundBank.Header.NumSimpleCues)
		{
			throw new XnbError($"原有简单 Cue 数 {existingSimpleCues} 与头部记录的 {soundBank.Header.NumSimpleCues} 不符");
		}

		int existingComplexCues = 0;
		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue cue in soundBank.Cues)
		{
			if (cue.FileOffset != 0)
			{
				existingComplexCues++;
			}
		}

		if (existingComplexCues != soundBank.Header.NumComplexCues)
		{
			throw new XnbError($"复杂 Cue 数由 {soundBank.Header.NumComplexCues} 变为 {existingComplexCues}，当前只支持同尺寸修改");
		}

		if (soundBank.WaveBankNames.Count != soundBank.Header.NumWaveBanks)
		{
			throw new XnbError($"声库数由 {soundBank.Header.NumWaveBanks} 变为 {soundBank.WaveBankNames.Count}，当前只支持同尺寸修改");
		}

		byte[] array = (byte[])soundBank.OriginalBytes.Clone();
		BufferWriter bufferWriter = new BufferWriter(array);
		SoundBankReader soundBankReader = new SoundBankReader();
		soundBankReader.Init(new ReaderResolver
		{
			bufferWriter = bufferWriter
		});

		bufferWriter.BytePosition = (int)soundBank.Header.WaveBankNameTableOffset;
		foreach (string waveBankName in soundBank.WaveBankNames)
		{
			WriteFixedName(bufferWriter, waveBankName, 64);
		}

		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry soundEntry in soundBank.SoundEntrys)
		{
			if (soundEntry.FileOffset == 0)
			{
				continue;   // 新增的简单 Cue 不在原文件里，由追加路径处理
			}

			bufferWriter.BytePosition = (int)soundEntry.FileOffset;
			bufferWriter.WriteByte(soundEntry.Flags);
			bufferWriter.WriteUInt32(soundEntry.SoundOffset);
		}

		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue soundCue in soundBank.Cues)
		{
			if (soundCue.FileOffset == 0)
			{
				continue;   // 新增的 Cue 不在原文件里，由追加路径处理
			}

			int bytePosition = (int)soundCue.FileOffset;
			bufferWriter.BytePosition = bytePosition;
			bufferWriter.WriteByte(soundCue.Flags);
			bytePosition++;
			if (((uint)(soundCue.Flags >> 2) & 1u) != 0)
			{
				bufferWriter.WriteUInt32(soundCue.SoundOffset);
				// 紧随其后的 4 字节未解析数据保持原样
				bytePosition += 8;
			}
			else
			{
				bufferWriter.WriteUInt32(soundCue.VariationTableOffset);
				bufferWriter.WriteUInt32(soundCue.TransitionTableOffset);
				bytePosition += 8;
			}

			bufferWriter.BytePosition = bytePosition;
			bufferWriter.WriteByte(soundCue.InstanceLimit);
			bufferWriter.WriteUInt16(soundCue.FadeInSec);
			bufferWriter.WriteUInt16(soundCue.FadeOutSec);
			bufferWriter.WriteByte(soundCue.InstanceFlags);

			int num = (soundCue.VariationFlags >> 3) & 7;
			foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue.CueVariation cueVariation in soundCue.CueVariations)
			{
				bufferWriter.BytePosition = (int)cueVariation.FileOffset;
				switch (num)
				{
				case 0:
					bufferWriter.WriteUInt16(cueVariation.TrackIndex);
					bufferWriter.WriteByte(cueVariation.WaveBankIndex);
					bufferWriter.WriteByte(cueVariation.BWeightMin);
					bufferWriter.WriteByte(cueVariation.BWeightMax);
					break;
				case 1:
					bufferWriter.WriteUInt32(cueVariation.SoundOffset);
					bufferWriter.WriteByte(cueVariation.BWeightMin);
					bufferWriter.WriteByte(cueVariation.BWeightMax);
					break;
				case 3:
					bufferWriter.WriteUInt32(cueVariation.SoundOffset);
					bufferWriter.WriteSingle(cueVariation.FWeightMin);
					bufferWriter.WriteSingle(cueVariation.FWeightMax);
					bufferWriter.WriteUInt32(cueVariation.Flags);
					break;
				case 4:
					bufferWriter.WriteUInt16(cueVariation.TrackIndex);
					bufferWriter.WriteByte(cueVariation.WaveBankIndex);
					break;
				default:
					throw new XnbError($"未实现的变体表类型 {num}，无法写回");
				}
			}
		}

		foreach (KeyValuePair<string, XactSound[]> sound in soundBank._sounds)
		{
			XactSound[] value = sound.Value;
			foreach (XactSound xactSound in value)
			{
				if (xactSound == null || xactSound.FileOffset == 0)
				{
					// 新增的 Sound 不在原文件里 —— 它由追加路径整条写出。
					// 漏了这句，它会被当成「就地修补」写在写入游标的当前位置，把原文件中间的字节糊掉。
					continue;
				}

				soundBankReader.xactSoundReader.Write(xactSound);
			}
		}

		return array;
	}

	/// <summary>
	/// 追加新 Cue：新内容一律追加到文件末尾，**已有字节一个不动**
	/// （所以各表里已有的绝对偏移都不用修正）。布局：
	/// <code>
	/// [原文件][新变体段…][新复杂 Cue 表（原有 + 新增）][新名字表（原有 + 新增）]
	/// </code>
	/// 只改头部的 NumComplexCues / NumTotalCues / CueNameTableLen /
	/// ComplexCuesOffset / CueNamesOffset 这五处，SimpleCues、Sounds、
	/// WaveBankNames、HashTable、HashVals 全都保持原位。
	///
	/// 新 Cue 一律写成「复杂 Cue + 变体类型 0」—— 那个类型把 (trackIndex, waveBankIndex)
	/// 直接内联在变体表里，**不需要 sound 记录**，所以不用碰 sounds 段。
	/// </summary>
	private static byte[] BuildAppendingCues(XnbConverter.Xact.SoundBank.Entity.SoundBank soundBank)
	{
		List<XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue> existing =
			new List<XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue>();
		List<XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue> added =
			new List<XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue>();
		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue cue in soundBank.Cues)
		{
			(cue.FileOffset == 0 ? added : existing).Add(cue);
		}

		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue cue in added)
		{
			if (string.IsNullOrEmpty(cue.Name))
			{
				throw new XnbError("追加的 Cue 缺少名字。");
			}

			if (cue.CueVariations.Count != 1)
			{
				throw new XnbError($"追加的 Cue「{cue.Name}」目前只支持单个变体。");
			}

			if (((cue.Flags >> 2) & 1) != 0)
			{
				throw new XnbError($"追加的 Cue「{cue.Name}」不支持内联 Sound 的写法。");
			}

			if (((cue.VariationFlags >> 3) & 7) != 0)
			{
				throw new XnbError($"追加的 Cue「{cue.Name}」的变体类型只支持 0（直接引用波形）。");
			}
		}

		if (existing.Count != soundBank.Header.NumComplexCues)
		{
			throw new XnbError($"原有复杂 Cue 数 {existing.Count} 与头部记录的 {soundBank.Header.NumComplexCues} 不符。");
		}

		// 新增的**简单** Cue：循环音乐走这条（简单 Cue → 带 Clip 的 Sound → LoopCount=255）。
		// 原版就是这样（实测 spring_day_ambient 是「简单 Cue + Flags=0x01 + 1 个 Clip + LoopCount=255」）；
		// 复杂 Cue + 变体类型 0 那条路表达不了循环。
		var addedSimple = new List<XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry>();
		var existingSimple = new List<XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry>();
		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry entry in soundBank.SoundEntrys)
		{
			(entry.FileOffset == 0 ? addedSimple : existingSimple).Add(entry);
		}

		if (existingSimple.Count != soundBank.Header.NumSimpleCues)
		{
			throw new XnbError($"原有简单 Cue 数 {existingSimple.Count} 与头部记录的 {soundBank.Header.NumSimpleCues} 不符");
		}

		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry entry in addedSimple)
		{
			if (string.IsNullOrEmpty(entry.Name))
			{
				throw new XnbError("追加的简单 Cue 缺少名字。");
			}

			if (!soundBank._sounds.TryGetValue(entry.Name, out XactSound[] sounds)
			    || sounds.Length != 1 || sounds[0] == null)
			{
				throw new XnbError($"追加的简单 Cue「{entry.Name}」在 _sounds 里没有唯一对应的 Sound 记录。");
			}
		}

		if (soundBank.WaveBankNames.Count != soundBank.Header.NumWaveBanks)
		{
			throw new XnbError($"声库数由 {soundBank.Header.NumWaveBanks} 变为 {soundBank.WaveBankNames.Count}，当前只支持同尺寸修改");
		}

		int newSimpleCues = soundBank.Header.NumSimpleCues + addedSimple.Count;
		int newComplexCues = soundBank.Header.NumComplexCues + added.Count;
		int newTotalCues = soundBank.Header.NumTotalCues + addedSimple.Count + added.Count;
		if (newSimpleCues > ushort.MaxValue || newComplexCues > ushort.MaxValue
		    || newTotalCues > ushort.MaxValue)
		{
			throw new XnbError("Cue 数超出 u16 上限。");
		}

		// 已有记录先就地修补，追加区建在它后面
		byte[] src = ApplyInPlace(soundBank);
		int oldLen = src.Length;

		uint oldNamesOffset = HeaderU32(src, CueNamesOffsetField);
		int oldNamesLen = HeaderU16(src, CueNameTableLenField);

		// 新 Sound 记录先写到暂存区，只为量出它有多长 ——
		// 里面的 ClipOffset 是绝对偏移，得知道最终落点才算得对，所以下面要在正式缓冲里重写一遍。
		byte[] scratch = new byte[1 << 16];
		BufferWriter scratchWriter = new BufferWriter(scratch);
		XactSoundReader soundWriter = new XactSoundReader();
		soundWriter.Init(new ReaderResolver { bufferWriter = scratchWriter });

		int[] soundOffsets = new int[addedSimple.Count];
		for (int i = 0; i < addedSimple.Count; i++)
		{
			soundOffsets[i] = scratchWriter.BytePosition;
			soundWriter.Write(soundBank._sounds[addedSimple[i].Name][0]);
		}

		int soundLen = scratchWriter.BytePosition;

		// 追加区布局：
		//   [新 Sound 记录（含 Clip）][新的整张简单 Cue 表][新变体段][新复杂 Cue 表][名字表]
		// 简单 Cue 表在文件里是**位置索引**（第 j 条 ↔ 名字表第 j 个），所以整张表搬到末尾重写，
		// 头部 SimpleCuesOffset 指过来即可（老位置留着不碍事，没人再引用它）。
		bool hasNewSimple = addedSimple.Count > 0;
		int soundsBase = oldLen;
		int simpleBase = hasNewSimple ? soundsBase + soundLen : -1;
		int varBase = hasNewSimple
			? simpleBase + (existingSimple.Count + addedSimple.Count) * SimpleCueRecordSize
			: soundsBase + soundLen;
		int varLen = added.Count * (VariationTableHeaderSize + VariationEntrySize);
		int cuesBase = varBase + varLen;
		int namesBase = cuesBase + newComplexCues * ComplexCueRecordSize;

		// 名字表：**[原简单名][新简单名][原复杂名][新复杂名]**
		//   「第 j 条简单 Cue ↔ 名字表第 j 个」是位置约定，所以新简单名必须插在复杂名**之前**。
		//   没有新增简单 Cue 时走原来的「原样照抄 + 追加」路径，一个字节都不动。
		var names = new List<byte>(oldNamesLen + 64 * (addedSimple.Count + added.Count));
		if (hasNewSimple)
		{
			string[] oldNames = Encoding.UTF8.GetString(src, (int)oldNamesOffset, oldNamesLen).Split('\0');
			for (int i = 0; i < soundBank.Header.NumSimpleCues; i++)
			{
				AddName(names, oldNames[i]);
			}

			foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry entry in addedSimple)
			{
				AddName(names, entry.Name);
			}

			for (int i = 0; i < soundBank.Header.NumComplexCues; i++)
			{
				AddName(names, oldNames[soundBank.Header.NumSimpleCues + i]);
			}
		}
		else
		{
			for (int i = 0; i < oldNamesLen; i++)
			{
				names.Add(src[oldNamesOffset + i]);
			}

			if (names.Count > 0 && names[names.Count - 1] != 0)
			{
				names.Add(0);
			}
		}

		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue cue in added)
		{
			AddName(names, cue.Name);
		}

		if (names.Count > ushort.MaxValue)
		{
			throw new XnbError($"Cue 名表长度 {names.Count} 超出 u16 上限。");
		}

		byte[] result = new byte[namesBase + names.Count];
		Array.Copy(src, result, oldLen);

		// ① 新 Sound 记录（这次写进正式缓冲：位置是绝对的，Clip 内部偏移自然算对）
		BufferWriter writer = new BufferWriter(result);
		soundWriter.Init(new ReaderResolver { bufferWriter = writer });
		for (int i = 0; i < addedSimple.Count; i++)
		{
			writer.BytePosition = soundsBase + soundOffsets[i];
			soundWriter.Write(soundBank._sounds[addedSimple[i].Name][0]);
			addedSimple[i].SoundOffset = (uint)(soundsBase + soundOffsets[i]);
		}

		// ② 新的简单 Cue 表：原有条目照抄（它们的 SoundOffset 没变），新增的接在后面。
		//    没有新增简单 Cue 时整段不写，头部字段也不动 —— 老位置那张表就是对的。
		if (hasNewSimple)
		{
			int simpleAt = simpleBase;
			foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry entry in existingSimple)
			{
				result[simpleAt] = entry.Flags;
				WriteU32(result, simpleAt + 1, entry.SoundOffset);
				simpleAt += SimpleCueRecordSize;
			}

			foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundEntry entry in addedSimple)
			{
				result[simpleAt] = entry.Flags;
				WriteU32(result, simpleAt + 1, entry.SoundOffset);
				simpleAt += SimpleCueRecordSize;
			}
		}

		// ③ 新变体段：每个新复杂 Cue 一段，紧跟其后放它的复杂 Cue 记录
		uint[] variationOffsets = new uint[added.Count];
		int at = varBase;
		for (int i = 0; i < added.Count; i++)
		{
			XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue cue = added[i];
			XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue.CueVariation variation = cue.CueVariations[0];
			variationOffsets[i] = (uint)at;
			WriteU16(result, at, 1);
			WriteU16(result, at + 2, cue.VariationFlags);
			WriteU32(result, at + 4, 0);
			WriteU16(result, at + 8, variation.TrackIndex);
			result[at + 10] = variation.WaveBankIndex;
			result[at + 11] = variation.BWeightMin;
			result[at + 12] = variation.BWeightMax;
			at += VariationTableHeaderSize + VariationEntrySize;
		}

		// ④ 新复杂 Cue 表：原有的照抄（它们的 offset 仍指老地方），新的接在后面
		at = cuesBase;
		foreach (XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue cue in existing)
		{
			WriteComplexCue(result, at, cue);
			at += ComplexCueRecordSize;
		}

		for (int i = 0; i < added.Count; i++)
		{
			WriteComplexCue(result, at, added[i], variationOffsets[i]);
			at += ComplexCueRecordSize;
		}

		Array.Copy(names.ToArray(), 0, result, namesBase, names.Count);

		if (hasNewSimple)
		{
			WriteU16(result, NumSimpleCuesField, (ushort)newSimpleCues);
			WriteU32(result, SimpleCuesOffsetField, (uint)simpleBase);
		}

		WriteU16(result, NumComplexCuesField, (ushort)newComplexCues);
		WriteU16(result, NumTotalCuesField, (ushort)newTotalCues);
		WriteU16(result, CueNameTableLenField, (ushort)names.Count);
		WriteU32(result, ComplexCuesOffsetField, (uint)cuesBase);
		WriteU32(result, CueNamesOffsetField, (uint)namesBase);
		return result;
	}

	/// <summary>往名字表里加一个以 NUL 结尾的名字。</summary>
	private static void AddName(List<byte> names, string name)
	{
		names.AddRange(Encoding.UTF8.GetBytes(name ?? string.Empty));
		names.Add(0);
	}

	/// <summary>把一条复杂 Cue 记录写进缓冲；variationOffset 为 null 时用记录自带的值。</summary>
	private static void WriteComplexCue(byte[] b, int at,
		XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue cue, uint? variationOffset = null)
	{
		b[at] = cue.Flags;
		if (((cue.Flags >> 2) & 1) != 0)
		{
			WriteU32(b, at + 1, cue.SoundOffset);
			byte[] unknown = cue.Unknowns.Count > 0 ? cue.Unknowns[0] : new byte[4];
			Array.Copy(unknown, 0, b, at + 5, Math.Min(4, unknown.Length));
		}
		else
		{
			WriteU32(b, at + 1, variationOffset ?? cue.VariationTableOffset);
			WriteU32(b, at + 5, cue.TransitionTableOffset);
		}

		b[at + 9] = cue.InstanceLimit;
		WriteU16(b, at + 10, cue.FadeInSec);
		WriteU16(b, at + 12, cue.FadeOutSec);
		b[at + 14] = cue.InstanceFlags;
	}

	private static ushort HeaderU16(byte[] b, int offset)
	{
		return (ushort)(b[offset] | (b[offset + 1] << 8));
	}

	private static uint HeaderU32(byte[] b, int offset)
	{
		return (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));
	}

	private static void WriteU16(byte[] b, int offset, ushort value)
	{
		b[offset] = (byte)value;
		b[offset + 1] = (byte)(value >> 8);
	}

	private static void WriteU32(byte[] b, int offset, uint value)
	{
		b[offset] = (byte)value;
		b[offset + 1] = (byte)(value >> 8);
		b[offset + 2] = (byte)(value >> 16);
		b[offset + 3] = (byte)(value >> 24);
	}

	// .xsb 头部的固定字段位置（见 Read 的读取顺序）
	private const int NumSimpleCuesField = 19;

	private const int NumTotalCuesField = 25;

	private const int SimpleCuesOffsetField = 34;

	/// <summary>简单 Cue 记录：Flags(1) + SoundOffset(4)。</summary>
	private const int SimpleCueRecordSize = 5;
	private const int CueNameTableLenField = 30;
	private const int ComplexCuesOffsetField = 38;
	private const int CueNamesOffsetField = 42;

	private const int NumComplexCuesField = 21;

	/// <summary>复杂 Cue 记录：Flags(1) + 两个 u32(8) + InstanceLimit(1) + FadeIn/Out(4) + InstanceFlags(1)。</summary>
	private const int ComplexCueRecordSize = 15;

	/// <summary>变体表头：NumEntries(2) + VariationFlags(2) + 未解析的 4 字节。</summary>
	private const int VariationTableHeaderSize = 8;

	/// <summary>变体表里「类型 0」一条的字节数：TrackIndex(2) + WaveBankIndex(1) + 两个权重(2)。</summary>
	private const int VariationEntrySize = 5;

	/// <summary>把名字写进定长槽（不足补 0，超长截断），与 ReadString(count) 的读法对应。</summary>
	private static void WriteFixedName(BufferWriter bufferWriter, string name, int length)
	{
		byte[] bytes = Encoding.Default.GetBytes(name ?? string.Empty);
		Span<byte> span = new byte[length];
		bytes.AsSpan(0, Math.Min(bytes.Length, length)).CopyTo(span);
		bufferWriter.Write(span);
	}

	/// <summary>写回 .xsb 文件。</summary>
	public static void Write(XnbConverter.Xact.SoundBank.Entity.SoundBank soundBank, string path)
	{
		File.WriteAllBytes(path, Build(soundBank));
	}
}
