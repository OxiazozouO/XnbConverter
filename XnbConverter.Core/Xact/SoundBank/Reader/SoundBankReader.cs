using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.SoundBank.Entity;
using XnbConverter.Xact.WaveBank;
using static XnbConverter.Xact.SoundBank.Entity.SoundBank;
using static XnbConverter.Xact.SoundBank.Entity.SoundBank.SoundCue;

namespace XnbConverter.Xact.SoundBank.Reader;

public class SoundBankReader : BaseReader
{
    public XactSoundReader xactSoundReader = new XactSoundReader();

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        xactSoundReader.Init(readerResolver);
    }
    public new static SoundBank.Entity.SoundBank Read(string path)
    {
        SoundBankReader waveBankReader = new SoundBankReader();
        waveBankReader.Init(new ReaderResolver(){
            bufferReader = BufferReader.FormFile(path)
        });
        return waveBankReader.Read();
    }

    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override SoundBank.Entity.SoundBank Read()
    {
        var result = new SoundBank.Entity.SoundBank();
        var header = result.Header;
        header.Magic = bufferReader.ReadString(4); // 读取魔术值
        if (header.Magic != "SDBK")
            throw new XnbError("发现无效的魔术值, {0}", header.Magic);

        // 读取工具版本和格式版本
        header.ToolVersion = bufferReader.ReadUInt16();
        header.FormatVersion = bufferReader.ReadUInt16();

        Log.Debug("格式版本: {0}", header.FormatVersion);
        Log.Debug("工具版本: {0}", header.ToolVersion);
        if (header.FormatVersion != SDBK_FORMAT_VERSION)
            Log.Warn("不支持的SoundBank格式 {0}.", header.FormatVersion);

        // 读取CRC校验值
        header.Crc = bufferReader.ReadUInt16();
        Log.Debug("CRC: {0}", header.Crc);

        // 读取最后修改时间和平台
        header.LastModifiedLow = bufferReader.ReadUInt32();
        header.LastModifiedHigh = bufferReader.ReadUInt32();
        header.Platform = bufferReader.ReadByte();

        Log.Debug("最后修改时间低位: {0}, 最后修改时间高位: {1}", header.LastModifiedLow, header.LastModifiedHigh);
        Log.Debug("平台: {0}", header.Platform);

        // 读取简单提示、复杂提示、总提示数、波形库数和声音数
        header.NumSimpleCues = bufferReader.ReadUInt16();//353
        // header.NumSimpleCues++;
        header.NumComplexCues = bufferReader.ReadUInt16();//50
        header.Unknowns.Add(bufferReader.Read(2));//0
        header.NumTotalCues = bufferReader.ReadUInt16();//403
        // header.NumTotalCues++;
        header.NumWaveBanks = bufferReader.ReadByte();//2
        header.NumSounds = bufferReader.ReadUInt16();//431
        header.CueNameTableLen = bufferReader.ReadUInt16();//4423
        header.Unknowns.Add(bufferReader.Read(2));//0

        Log.Debug("简单提示数:    {0}", header.NumSimpleCues);
        Log.Debug("复杂提示数:    {0}", header.NumComplexCues);
        Log.Debug("总提示数:      {0}", header.NumTotalCues);
        Log.Debug("波形库数:      {0}", header.NumWaveBanks);
        Log.Debug("声音数:        {0}", header.NumSounds);
        Log.Debug("Cue名称表长度: {0}", header.CueNameTableLen);

        header.SimpleCuesOffset = bufferReader.ReadUInt32();//18651
        header.ComplexCuesOffset = bufferReader.ReadUInt32();//20416
        header.CueNamesOffset = bufferReader.ReadUInt32();//25182
        header.Unknowns.Add(bufferReader.Read(4));//255 255 255 255
        header.VariationTablesOffset = bufferReader.ReadUInt32();//21166
        header.Unknowns.Add(bufferReader.Read(4));//255 255 255 255
        header.WaveBankNameTableOffset = bufferReader.ReadUInt32();//138
        header.CueNameHashTableOffset = bufferReader.ReadUInt32();//21958
        header.CueNameHashValsOffset = bufferReader.ReadUInt32();//22764
        header.SoundsOffset = bufferReader.ReadUInt32();//266
        header.Name = bufferReader.ReadString(64);

        Log.Debug("简单提示音偏移量:      {0}", header.SimpleCuesOffset); // 简单提示音偏移量
        Log.Debug("复杂提示音偏移量:      {0}", header.ComplexCuesOffset); // 复杂提示音偏移量
        Log.Debug("提示音名称偏移量:      {0}", header.CueNamesOffset); // 提示音名称偏移量
        Log.Debug("变化表偏移量:         {0}", header.VariationTablesOffset); // 变化表偏移量
        Log.Debug("波形库名称表偏移量:    {0}", header.WaveBankNameTableOffset); // 波形库名称表偏移量
        Log.Debug("提示音名称哈希表偏移量: {0}", header.CueNameHashTableOffset); // 提示音名称哈希表偏移量
        Log.Debug("提示音名称哈希值偏移量: {0}", header.CueNameHashValsOffset); // 提示音名称哈希值偏移量
        Log.Debug("声音偏移量:           {0}", header.SoundsOffset); // 声音偏移量
        Log.Debug("名字: {0}", header.Name); // 名称

        // 解析wave bank name table 138
        bufferReader.BytePosition = (int)header.WaveBankNameTableOffset;
        result.WaveBankNames = new List<string>();
        for (int i = 0; i < header.NumWaveBanks; i++)
            result.WaveBankNames.Add(bufferReader.ReadString(64));

        Log.Debug("Wave Banks: {0}", string.Join(", ", result.WaveBankNames)); // 波形库名称

        // 解析cue name table
        bufferReader.BytePosition = (int)header.CueNamesOffset;//266
        string[] CueNames = bufferReader.ReadString(header.CueNameTableLen).Split('\0');


        if (header.NumSimpleCues > 0)
        {
            bufferReader.BytePosition = (int)header.SimpleCuesOffset;
            for (int i = 0; i < header.NumSimpleCues; i++)
            {
                var se = new SoundEntry()
                {
                    Flags = bufferReader.ReadByte(),
                    SoundOffset = bufferReader.ReadUInt32()//266 
                };
                
                int oldPos = bufferReader.BytePosition;
                bufferReader.BytePosition = (int)se.SoundOffset;
                XactSound sound = xactSoundReader.Read();
                bufferReader.BytePosition = oldPos;

                result.SoundEntrys.Add(se);
                if(result._sounds.ContainsKey(CueNames[i]))
                    Console.WriteLine();
                result._sounds.Add(CueNames[i], new XactSound[] { sound });
                // result._probabilities.Add(CueNames[i], result.dedefaultProbability);
            }
        }
        

        Log.Debug($"Cues: {string.Join(", ", CueNames)}"); // 提示音
        int totalCueCount = 0;

        if (header.NumComplexCues > 0)
        {
            bufferReader.BytePosition = (int)header.ComplexCuesOffset;

            for (int i = 0; i < header.NumComplexCues; i++)
            {
                SoundCue soundCue = new SoundCue();
                soundCue.Flags = bufferReader.ReadByte();
                if (((soundCue.Flags >> 2) & 1) != 0)
                {
                    // 不确定 :/
                    soundCue.SoundOffset = bufferReader.ReadUInt32();
                    soundCue.Unknowns.Add(bufferReader.Read(4));
                    Log.Debug("发现 {0}", CueNames[header.NumSimpleCues + i]);
                    {
                        var ord = bufferReader.BytePosition;
                        bufferReader.BytePosition = (int)soundCue.SoundOffset;
                        XactSound sound = xactSoundReader.Read();
                        bufferReader.BytePosition = ord;
                        if(result._sounds.ContainsKey(CueNames[header.NumSimpleCues + i]))
                            Console.WriteLine();
                        result._sounds.Add(CueNames[header.NumSimpleCues + i], new XactSound[] { sound });
                        // result._probabilities.Add(CueNames[header.NumSimpleCues + i], result.dedefaultProbability);
                    }

                    totalCueCount++;
                }
                else
                {
                    soundCue.VariationTableOffset = bufferReader.ReadUInt32();
                    soundCue.TransitionTableOffset = bufferReader.ReadUInt32();


                    // 解析variation table
                    int savePos = bufferReader.BytePosition;
                    bufferReader.BytePosition = (int)soundCue.VariationTableOffset;

                    soundCue.NumEntries = bufferReader.ReadUInt16();
                    soundCue.VariationFlags = bufferReader.ReadUInt16();
                    soundCue.Unknowns.Add(bufferReader.Read(4));

                    XactSound[] cueSounds = new XactSound[soundCue.NumEntries];
                    // float[] probs = new float[soundCue.NumEntries];
                    Log.Debug("为 {0} 发现 {1} 个提示音", CueNames[header.NumSimpleCues + i], soundCue.NumEntries);

                    int tableType = (soundCue.VariationFlags >> 3) & 0x7;
                    for (int j = 0; j < soundCue.NumEntries; j++)
                    {
                        var c = new CueVariation();
                        switch (tableType)
                        {
                            case 0: // Wave
                            {
                                c.TrackIndex = bufferReader.ReadUInt16();
                                c.WaveBankIndex = bufferReader.ReadByte();
                                c.BWeightMin = bufferReader.ReadByte();
                                c.BWeightMax = bufferReader.ReadByte();
                                Log.Debug("WaveBank Index: {0}", c.WaveBankIndex);
                                cueSounds[j] = new XactSound() { CueVariation = c };
                                break;
                            }
                            case 1:
                            {
                                c.SoundOffset = bufferReader.ReadUInt32();
                                c.BWeightMin = bufferReader.ReadByte();
                                c.BWeightMax = bufferReader.ReadByte();

                                var old = bufferReader.BytePosition;
                                bufferReader.BytePosition = (int)c.SoundOffset;
                                cueSounds[j] = xactSoundReader.Read();
                                cueSounds[j].CueVariation = c;
                                bufferReader.BytePosition = old;

                                totalCueCount++;
                                break;
                            }
                            case 3:
                            {
                                c.SoundOffset = bufferReader.ReadUInt32();
                                c.FWeightMin = bufferReader.ReadSingle();
                                c.FWeightMax = bufferReader.ReadSingle();
                                c.Flags = bufferReader.ReadUInt32();
                                {
                                    var ord = bufferReader.BytePosition;
                                    bufferReader.BytePosition = (int)c.SoundOffset;
                                    cueSounds[j] = xactSoundReader.Read();
                                    cueSounds[j].CueVariation = c;
                                    bufferReader.BytePosition = ord;
                                }
                                totalCueCount++;
                                break;
                            }
                            case 4: // CompactWave
                            {
                                c.TrackIndex = bufferReader.ReadUInt16();
                                c.WaveBankIndex = bufferReader.ReadByte();
                                Log.Debug("WaveBank Index: {0}", c.WaveBankIndex);

                                cueSounds[j] = new XactSound() { CueVariation = c };
                                break;
                            }
                            default:
                                throw new NotImplementedException("未实现表类型");
                        }

                        soundCue.CueVariations.Add(c);
                    }

                    bufferReader.BytePosition = savePos;
                    if(result._sounds.ContainsKey(CueNames[header.NumSimpleCues + i]))
                        Console.WriteLine();
                    result._sounds.Add(CueNames[header.NumSimpleCues + i], cueSounds);
                    // result._probabilities.Add(CueNames[header.NumSimpleCues + i], probs);

                }

                // instance limit
                soundCue.InstanceLimit = bufferReader.ReadByte();
                soundCue.FadeInSec = bufferReader.ReadUInt16();
                soundCue.FadeOutSec = bufferReader.ReadUInt16();
                soundCue.InstanceFlags = bufferReader.ReadByte();
                result.Cues.Add(soundCue);
            }
        }

        Log.Debug("发现 {0} 个提示音!", totalCueCount);
        return result;
    }

    public override void Write(object input)
    {
        throw new NotImplementedException();
    }
}