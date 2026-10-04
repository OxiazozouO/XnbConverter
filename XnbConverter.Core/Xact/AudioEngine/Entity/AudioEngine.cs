namespace XnbConverter.Xact.AudioEngine.Entity;

public class AudioEngine
{
	public enum RpcPointType
	{
		Linear,
		Fast,
		Slow,
		SinCos
	}

	public enum RpcParameter
	{
		Volume,
		Pitch,
		ReverbSend,
		FilterFrequency,
		FilterQFactor,
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

		public byte Flags;

		/// <summary>在归档中的序号；读入时按 13 字节步长排列，写回要靠它定位。</summary>
		public int Index;

		public double InitValue;

		public bool IsGlobal;

		public bool IsPublic;

		public bool IsReadOnly;

		public bool IsReserved;

		public double MaxValue;

		public double MinValue;

		public string Name;

		public double Value;
	}

	public class RpcPoint
	{
		public RpcPointType Type;

		public double X;

		public double Y;
	}

	public class RpcCurve
	{
		public uint FileOffset;

		public bool IsGlobal;

		public RpcParameter Parameter;

		public RpcPoint[] Points;

		public int Variable;
	}

	public AudioCategory[] _categories;

	public Dictionary<string, int> _categoryLookup = new Dictionary<string, int>();

	public RpcVariable[] _cueVariables;

	public RpcCurve[] _reverbCurves;

	public ReverbSettings _reverbSettings;

	public Dictionary<string, int> _variableLookup = new Dictionary<string, int>();

	public RpcVariable[] _variables;

	public uint catNameIndexOffset;

	public uint catNamesOffset;

	public uint catsOffset;

	public ushort Crc;

	public uint dspParamsOffset;

	public uint dspPresetOffset;

	public ushort FormatVersion;

	public uint LastModifiedHigh;

	public uint LastModifiedLow;

	public string? Magic;

	public ushort numCats;

	public ushort numDspParams;

	public ushort numDspPresets;

	public ushort numRpc;

	public ushort numVars;

	public RpcCurve[] RpcCurves;

	public uint rpcOffset;

	public ushort ToolVersion;

	public List<byte[]> Unkns = new List<byte[]>();

	/// <summary>
	/// 读入时的原始字节。写回采用「同尺寸原地替换」：以它为模板，只把已建模的结构
	/// （类别、变量、RPC 曲线、DSP 参数）按各自记录的原偏移重写回去，
	/// 名字索引表、填充区等未建模部分保持原样。
	/// </summary>
	public byte[] OriginalBytes;

	public uint varNameIndexOffset;

	public uint varNamesOffset;

	public uint varsOffset;

	public const int XGSF_FORMAT = 42;
}
