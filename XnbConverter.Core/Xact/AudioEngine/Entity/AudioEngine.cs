using System.Collections.Generic;

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

	public uint varNameIndexOffset;

	public uint varNamesOffset;

	public uint varsOffset;

	public const int XGSF_FORMAT = 42;
}
