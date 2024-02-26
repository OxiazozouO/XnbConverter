namespace XnbConverter.Xact.SoundBank.Entity;

public class SoundBank
{
    public const ushort SDBK_FORMAT_VERSION = 0x2B;
    public SoundBankHeader Header = new();

    public List<string> WaveBankNames = new(); //Wave Bank的名称集合
    public List<SoundEntry> SoundEntrys = new(); //声音资源的集合
    public List<SoundCue> Cues = new(); //Cue的集合

    public float[] dedefaultProbability = new float [1] { 1.0f };
    public Dictionary<string, XactSound[]> _sounds = new();
    // public Dictionary<string, float []> _probabilities = new Dictionary<string, float []> ();


    public class SoundBankHeader
    {
        public string Magic; //用于识别文件类型或标识。
        public ushort ToolVersion; //工具版本号
        public ushort FormatVersion; //文件格式版本号
        public ushort Crc; //文件的校验码，用于验证文件的完整性和准确性。
        public uint LastModifiedLow; //文件的最后修改时间的低位部分。
        public uint LastModifiedHigh; //文件的最后修改时间的高位部分。
        public byte Platform; //声音文件的平台标识，表示声音文件适用的平台。
        public ushort NumSimpleCues; //简单Cue的数量
        public ushort NumComplexCues; //复杂Cue的数量
        public List<byte[]> Unknowns = new(); //未知数据
        public ushort NumTotalCues; //总Cue的数量
        public byte NumWaveBanks; //Wave Bank的数量
        public ushort NumSounds; //声音资源的数量
        public ushort CueNameTableLen; //Cue名称表的长度，表示Cue名称表的长度。
        public uint SimpleCuesOffset; //简单Cue的偏移量
        public uint ComplexCuesOffset; //复杂Cue的偏移量
        public uint CueNamesOffset; //Cue名称的偏移量
        public uint VariationTablesOffset; //变化表的偏移量
        public uint WaveBankNameTableOffset; //Wave Bank名称表的偏移量
        public uint CueNameHashTableOffset; //Cue名称哈希表的偏移量
        public uint CueNameHashValsOffset; //Cue名称哈希值的偏移量
        public uint SoundsOffset; //声音资源的偏移量
        public string Name; //SoundBank的名称
    }

    public class SoundEntry
    {
        public byte Flags; //声音资源的标志，用于表示声音资源的属性或特征。
        public uint SoundOffset; //声音资源的偏移量，表示声音资源数据在文件中的位置。
    }

    public class SoundCue
    {
        public byte Flags; //Cue的标志，用于表示Cue的属性
        public uint SoundOffset; //Cue关联的声音资源的偏移量
        public List<byte[]> Unknowns = new();
        public XactSound Sound; //Cue关联的声音资源

        public uint VariationTableOffset; //变化表的偏移量
        public uint TransitionTableOffset; //过渡表的偏移量
        public ushort NumEntries; //Cue的条目数量

        public ushort VariationFlags; //变化标志

        //Cue的变化集合
        public List<CueVariation> CueVariations = new();

        //Cue的实例限制
        public byte InstanceLimit;
        public ushort FadeInSec; //Cue的淡入时间
        public ushort FadeOutSec; //Cue的淡出时间
        public byte InstanceFlags; //实例标志


        public class CueVariation //Cue的变化
        {
            public ushort TrackIndex; //轨道索引，表示Cue变化所在的轨道的索引。
            public byte WaveBankIndex; //Wave Bank索引，表示Cue变化关联的Wave Bank的索引。
            public byte BWeightMin; //最小字节权重
            public byte BWeightMax; //最大字节权重

            public uint SoundOffset; //Cue变化关联的声音资源的偏移量
            public float FWeightMin; //最小浮点权重
            public float FWeightMax; //最大浮点权重
            public uint Flags; //Cue变化的标志，用于表示Cue变化的属性或特征。
        }
    }
}