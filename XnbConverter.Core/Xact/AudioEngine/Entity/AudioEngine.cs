using System.Collections.Generic;

namespace XnbConverter.Xact.AudioEngine.Entity;

public class AudioEngine
{
    public AudioCategory[] _categories;

    //AudioCategory的名称索引，用于快速查找AudioCategory的索引。
    public Dictionary<string, int> _categoryLookup = new();
    public RpcVariable[] _cueVariables;

    //混响曲线的集合，包含了所有的混响曲线数据。
    public RpcCurve[] _reverbCurves;

    //混响设置，包含了混响效果的参数和配置。
    public ReverbSettings _reverbSettings;

    //RpcVariable的名称索引，用于快速查找RpcVariable的索引。
    public Dictionary<string, int> _variableLookup = new();
    public RpcVariable[] _variables;
    public uint catNameIndexOffset; //AudioCategory名称索引的偏移量.
    public uint catNamesOffset; //AudioCategory名称的偏移量
    public uint catsOffset; //AudioCategory的偏移量.
    public ushort Crc; //文件的校验码，用于验证文件的完整性和准确性。
    public uint dspParamsOffset; //DSP参数的偏移量
    public uint dspPresetOffset; //DSP预设的偏移量
    public ushort FormatVersion; //文件格式版本号
    public uint LastModifiedHigh; //文件的最后修改时间的高位部分。
    public uint LastModifiedLow; //文件的最后修改时间的低位部分。

    public string? Magic; //用于识别文件类型或标识。
    public ushort numCats; //AudioCategory的数量
    public ushort numDspParams; //DSP参数的数量，表示文件中DSP参数的个数。
    public ushort numDspPresets; //DSP预设的数量，表示文件中DSP预设的个数。
    public ushort numRpc; //RpcCurve的数量
    public ushort numVars; //RpcVariable的数量
    public RpcCurve[] RpcCurves; //RpcCurve的集合
    public uint rpcOffset; //RpcCurve的偏移量
    public ushort ToolVersion; //工具版本号
    public List<byte[]> Unkns = new(); //未知数据
    public uint varNameIndexOffset; //RpcVariable名称索引的偏移量
    public uint varNamesOffset; //RpcVariable名称的偏移量
    public uint varsOffset; //RpcVariable的偏移量.

    #region 用于音频引擎的类和枚举

    // 音频引擎常量
    public const int XGSF_FORMAT = 0x2A; // XGSF格式

    public enum RpcPointType //用于指定RpcPoint的插值方式。
    {
        Linear, // 线性
        Fast, // 快速
        Slow, // 缓慢
        SinCos // 正余弦
    }

    public enum RpcParameter
    {
        Volume, // 音量
        Pitch, // 音高
        ReverbSend, // 混响发送
        FilterFrequency, // 滤波器频率
        FilterQFactor, // 滤波器Q因子
        NumParameters
    }


    public class RpcVariable
    {
        public enum ControlType
        {
            Local,
            NonMonitored,
            Monitored
        }

        public byte Flags; //标志位
        public double InitValue; // 初始值
        public bool IsGlobal; // 是否全局，全局变量在整个声音引擎中都可访问。
        public bool IsPublic; // 是否公开。公开变量可以被其他模块或代码访问和使用。
        public bool IsReadOnly; // 是否只读
        public bool IsReserved; // 是否保留。保留变量是为将来的用途而保留的
        public double MaxValue; // 最大值
        public double MinValue; // 最小值
        public string Name; // 名称
        public double Value; // 值
    }

    public class RpcPoint
    {
        public RpcPointType Type = RpcPointType.Linear; // 类型
        public double X; // X坐标
        public double Y; // Y坐标
    }

    public class RpcCurve
    {
        public uint FileOffset;
        public bool IsGlobal;
        public RpcParameter Parameter = RpcParameter.Volume; // 参数
        public RpcPoint[] Points; // 点集合
        public int Variable; // 变量
    }

    #endregion
}