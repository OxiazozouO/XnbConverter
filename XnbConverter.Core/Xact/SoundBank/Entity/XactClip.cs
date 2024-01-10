namespace XnbConverter.Xact.SoundBank.Entity;

public class XactClip
{
    public byte   VolumeDb;//音量的分贝值，表示剪辑的音量。
    public uint   ClipOffset      ;//剪辑的偏移量，表示剪辑数据在文件中的位置。
    public ushort FilterQAndFlags ;//滤波器的Q值和标志
    public ushort FilterFrequency ;//滤波器的频率
    public WaveIndex[] WaveIndexs;//波形索引，包含了剪辑关联的波形的索引和相关信息。
    
    public class WaveIndex
    {
        public List<byte[]> unkn = new List<byte[]>();
        public uint EventInfo;//事件信息，表示波形关联的事件信息。
        public float RandomOffset;//随机偏移量
        public byte EventFlags;//事件标志
        public int TrackIndex;//轨道索引
        public int WaveBankIndex;//Wave Bank索引，表示波形关联的Wave Bank的索引。
        public byte LoopCount ;//循环次数，表示波形的循环次数。
        public float  PanAngle;//平移角度
        public float PanArc    ;//平移弧度
        
        // Pitch variation range
        public float MinPitch;//最小音高
        public float MaxPitch;//最大音高
        
        // Volume variation range
        public byte MinVolumeDecibels;//最小音量的分贝值
        public byte MaxVolumeDecibels;//最大音量的分贝值
        public byte VariationFlags;
        
        // Filter variation
        public float MinFrequency;//最小滤波器频率
        public float MaxFrequency;//最大滤波器频率
        public float MinQ        ;//最小滤波器Q值
        public float MaxQ        ;//最大滤波器Q值
        public ushort NumTracks;//轨道数量，表示波形中包含的轨道的个数。
        
        public byte MoreFlags;//更多标志

        public int[] WaveBanks;//Wave Bank的索引集合
        public int[] Tracks;//轨道的索引集合
        
        public byte[][] Weights;//权重集合，用于存储与轨道关联的权重值。
    }
}