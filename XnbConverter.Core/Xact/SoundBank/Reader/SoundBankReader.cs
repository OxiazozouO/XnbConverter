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

	public override bool IsValueType()
	{
		throw new NotImplementedException();
	}

	public override object Read()
	{
		XnbConverter.Xact.SoundBank.Entity.SoundBank soundBank = new XnbConverter.Xact.SoundBank.Entity.SoundBank();
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
				soundBank.Cues.Add(soundCue);
			}
		}
		Logger.Debug(Error.SoundBankReader_24, num);
		return soundBank;
	}

	public override void Write(object input)
	{
		throw new NotImplementedException();
	}
}
