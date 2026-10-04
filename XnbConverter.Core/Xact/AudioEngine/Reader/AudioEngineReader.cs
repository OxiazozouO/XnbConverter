using XnbConverter.Configurations;
using XnbConverter.Exceptions;
using XnbConverter.Readers;
using XnbConverter.Xact.AudioEngine.Entity;

namespace XnbConverter.Xact.AudioEngine.Reader;

public class AudioEngineReader : BaseReader
{
	public AudioCategoryReader audioCategoryReader = new AudioCategoryReader();

	public ReverbSettingsReader reverbSettingsReader = new ReverbSettingsReader();

	public override void Init(ReaderResolver resolver)
	{
		base.Init(resolver);
		audioCategoryReader.Init(resolver);
		reverbSettingsReader.Init(resolver);
	}

	public static XnbConverter.Xact.AudioEngine.Entity.AudioEngine Read(string path)
	{
		AudioEngineReader audioEngineReader = new AudioEngineReader();
		audioEngineReader.Init(new ReaderResolver
		{
			bufferReader = BufferReader.FormFile(path)
		});
		return (XnbConverter.Xact.AudioEngine.Entity.AudioEngine)audioEngineReader.Read();
	}

	public override object Read()
	{
		XnbConverter.Xact.AudioEngine.Entity.AudioEngine audioEngine = new XnbConverter.Xact.AudioEngine.Entity.AudioEngine();
		audioEngine.OriginalBytes = bufferReader.Buffer.AsSpan(0, bufferReader.Size).ToArray();
		audioEngine.Magic = bufferReader.ReadString(4);
		if (audioEngine.Magic != "XGSF")
		{
			throw new XnbError(Error.AudioEngineReader_1, audioEngine.Magic);
		}
		audioEngine.ToolVersion = bufferReader.ReadUInt16();
		audioEngine.FormatVersion = bufferReader.ReadUInt16();
		Logger.Debug(Error.AudioEngineReader_2, audioEngine.ToolVersion);
		Logger.Debug(Error.AudioEngineReader_3, audioEngine.FormatVersion);
		if (audioEngine.FormatVersion != 42)
		{
			Logger.Warn(Error.AudioEngineReader_19);
		}
		audioEngine.Crc = bufferReader.ReadUInt16();
		audioEngine.LastModifiedLow = bufferReader.ReadUInt32();
		audioEngine.LastModifiedHigh = bufferReader.ReadUInt32();
		audioEngine.Unkns.Add(bufferReader.Read(1));
		audioEngine.numCats = bufferReader.ReadUInt16();
		audioEngine.numVars = bufferReader.ReadUInt16();
		Logger.Debug(Error.AudioEngineReader_4, audioEngine.numCats, audioEngine.numVars);
		audioEngine.Unkns.Add(bufferReader.Read(4));
		audioEngine.numRpc = bufferReader.ReadUInt16();
		audioEngine.numDspPresets = bufferReader.ReadUInt16();
		audioEngine.numDspParams = bufferReader.ReadUInt16();
		audioEngine.catsOffset = bufferReader.ReadUInt32();
		audioEngine.varsOffset = bufferReader.ReadUInt32();
		Logger.Debug(Error.AudioEngineReader_5, audioEngine.numRpc);
		Logger.Debug(Error.AudioEngineReader_6, audioEngine.numDspPresets);
		Logger.Debug(Error.AudioEngineReader_7, audioEngine.numDspParams);
		Logger.Debug(Error.AudioEngineReader_8, audioEngine.catsOffset);
		Logger.Debug(Error.AudioEngineReader_9, audioEngine.varsOffset);
		audioEngine.Unkns.Add(bufferReader.Read(4));
		audioEngine.catNameIndexOffset = bufferReader.ReadUInt32();
		audioEngine.Unkns.Add(bufferReader.Read(4));
		audioEngine.varNameIndexOffset = bufferReader.ReadUInt32();
		audioEngine.catNamesOffset = bufferReader.ReadUInt32();
		audioEngine.varNamesOffset = bufferReader.ReadUInt32();
		audioEngine.rpcOffset = bufferReader.ReadUInt32();
		audioEngine.dspPresetOffset = bufferReader.ReadUInt32();
		audioEngine.dspParamsOffset = bufferReader.ReadUInt32();
		Logger.Debug(Error.AudioEngineReader_10, audioEngine.catNameIndexOffset);
		Logger.Debug(Error.AudioEngineReader_11, audioEngine.varNameIndexOffset);
		Logger.Debug(Error.AudioEngineReader_12, audioEngine.catNamesOffset);
		Logger.Debug(Error.AudioEngineReader_13, audioEngine.varNamesOffset);
		Logger.Debug(Error.AudioEngineReader_14, audioEngine.rpcOffset);
		Logger.Debug(Error.AudioEngineReader_15, audioEngine.dspPresetOffset);
		Logger.Debug(Error.AudioEngineReader_16, audioEngine.dspParamsOffset);
		bufferReader.BytePosition = (int)audioEngine.catNamesOffset;
		string[] array = new string[audioEngine.numCats];
		for (int i = 0; i < audioEngine.numCats; i++)
		{
			array[i] = bufferReader.ReadString();
		}
		Logger.Debug(Error.AudioEngineReader_17, array.ToJoinStr());
		audioEngine._categories = new AudioCategory[audioEngine.numCats];
		bufferReader.BytePosition = (int)audioEngine.catsOffset;
		for (int j = 0; j < audioEngine.numCats; j++)
		{
			audioEngine._categories[j] = (AudioCategory)audioCategoryReader.Read();
			audioEngine._categories[j].name = array[j];
			audioEngine._categoryLookup.Add(array[j], j);
		}
		bufferReader.BytePosition = (int)audioEngine.varNamesOffset;
		string[] array2 = new string[audioEngine.numVars];
		for (int k = 0; k < audioEngine.numVars; k++)
		{
			array2[k] = bufferReader.ReadString();
		}
		Logger.Debug(Error.AudioEngineReader_18, array2.ToJoinStr());
		List<XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable> list = new List<XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable>();
		List<XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable> list2 = new List<XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable>();
		List<XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable> list3 = new List<XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable>();
		bufferReader.BytePosition = (int)audioEngine.varsOffset;
		for (int l = 0; l < audioEngine.numVars; l++)
		{
			XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable rpcVariable = new XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable();
			rpcVariable.Index = l;
			rpcVariable.Name = array2[l];
			rpcVariable.Flags = bufferReader.ReadByte();
			rpcVariable.InitValue = bufferReader.ReadSingle();
			rpcVariable.MinValue = bufferReader.ReadSingle();
			rpcVariable.MaxValue = bufferReader.ReadSingle();
			rpcVariable.Value = rpcVariable.InitValue;
			list.Add(rpcVariable);
			if (!rpcVariable.IsGlobal)
			{
				list2.Add(rpcVariable);
				continue;
			}
			list3.Add(rpcVariable);
			audioEngine._variableLookup.Add(rpcVariable.Name, list3.Count - 1);
		}
		audioEngine._cueVariables = list2.ToArray();
		audioEngine._variables = list3.ToArray();
		List<XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcCurve> list4 = new List<XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcCurve>();
		audioEngine.RpcCurves = new XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcCurve[audioEngine.numRpc];
		if (audioEngine.numRpc > 0)
		{
			bufferReader.BytePosition = (int)audioEngine.rpcOffset;
			for (int m = 0; m < audioEngine.numRpc; m++)
			{
				XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcCurve rpcCurve = new XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcCurve();
				rpcCurve.FileOffset = (uint)bufferReader.BytePosition;
				XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable variable = list[bufferReader.ReadUInt16()];
				rpcCurve.IsGlobal = variable.IsGlobal;
				if (variable.IsGlobal)
				{
					rpcCurve.Variable = list3.FindIndex((XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable e) => e.Name == variable.Name);
				}
				else
				{
					rpcCurve.Variable = list2.FindIndex((XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable e) => e.Name == variable.Name);
				}
				byte b = bufferReader.ReadByte();
				rpcCurve.Parameter = (XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcParameter)bufferReader.ReadUInt16();
				rpcCurve.Points = new XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcPoint[b];
				for (int n = 0; n < b; n++)
				{
					rpcCurve.Points[n] = new XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcPoint
					{
						X = bufferReader.ReadSingle(),
						Y = bufferReader.ReadSingle(),
						Type = (XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcPointType)bufferReader.ReadByte()
					};
				}
				if (rpcCurve.Parameter - 5 >= XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcParameter.Volume && variable.IsGlobal)
				{
					list4.Add(rpcCurve);
				}
				audioEngine.RpcCurves[m] = rpcCurve;
			}
		}
		audioEngine._reverbCurves = list4.ToArray();
		if (audioEngine.numDspPresets > 0)
		{
			if (audioEngine.numDspPresets != 1)
			{
				throw new Exception("Unexpected number of DSP presets!");
			}
			if (audioEngine.numDspParams != 22)
			{
				throw new Exception("Unexpected number of DSP parameters!");
			}
			bufferReader.BytePosition = (int)audioEngine.dspParamsOffset;
			audioEngine._reverbSettings = (ReverbSettings)reverbSettingsReader.Read();
		}
		Logger.Debug(list4.ToJoinStr());
		return audioEngine;
	}


	public override void Write(object input)
	{
		Build((XnbConverter.Xact.AudioEngine.Entity.AudioEngine)input);
	}

	/// <summary>
	/// 写回 .xgs：以读入时的原始字节为模板，把已建模的结构（类别、变量、RPC 曲线、
	/// DSP 参数）按各自记录的原偏移重写。名字索引表、DSP 预设区等未建模部分原样保留。
	/// 因此只支持「同尺寸修改」——结构条目数不能变，写回前会校验。
	/// </summary>
	public static byte[] Build(XnbConverter.Xact.AudioEngine.Entity.AudioEngine audioEngine)
	{
		if (audioEngine.OriginalBytes == null)
		{
			throw new XnbError("缺少原始字节，无法写回（请先用 Read 读入）");
		}

		if (audioEngine._categories == null || audioEngine._categories.Length != audioEngine.numCats)
		{
			throw new XnbError($"类别数由 {audioEngine.numCats} 变为 {audioEngine._categories?.Length}，当前只支持同尺寸修改");
		}

		int num = ((audioEngine._cueVariables?.Length ?? 0) + (audioEngine._variables?.Length ?? 0));
		if (num != audioEngine.numVars)
		{
			throw new XnbError($"变量数由 {audioEngine.numVars} 变为 {num}，当前只支持同尺寸修改");
		}

		if (audioEngine.RpcCurves == null || audioEngine.RpcCurves.Length != audioEngine.numRpc)
		{
			throw new XnbError($"RPC 曲线数由 {audioEngine.numRpc} 变为 {audioEngine.RpcCurves?.Length}，当前只支持同尺寸修改");
		}

		byte[] array = (byte[])audioEngine.OriginalBytes.Clone();
		BufferWriter bufferWriter = new BufferWriter(array);
		AudioEngineReader audioEngineReader = new AudioEngineReader();
		audioEngineReader.Init(new ReaderResolver
		{
			bufferWriter = bufferWriter
		});

		bufferWriter.BytePosition = (int)audioEngine.catsOffset;
		AudioCategory[] categories = audioEngine._categories;
		foreach (AudioCategory audioCategory in categories)
		{
			audioEngineReader.audioCategoryReader.Write(audioCategory);
		}

		WriteVariables(audioEngine._cueVariables);
		WriteVariables(audioEngine._variables);

		foreach (XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcCurve rpcCurve in audioEngine.RpcCurves)
		{
			bufferWriter.BytePosition = (int)rpcCurve.FileOffset;
			XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable rpcVariable = rpcCurve.IsGlobal
				? audioEngine._variables[rpcCurve.Variable]
				: audioEngine._cueVariables[rpcCurve.Variable];
			bufferWriter.WriteUInt16((ushort)rpcVariable.Index);
			bufferWriter.WriteByte((byte)rpcCurve.Points.Length);
			bufferWriter.WriteUInt16((ushort)rpcCurve.Parameter);
			XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcPoint[] points = rpcCurve.Points;
			foreach (XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcPoint rpcPoint in points)
			{
				bufferWriter.WriteSingle((float)rpcPoint.X);
				bufferWriter.WriteSingle((float)rpcPoint.Y);
				bufferWriter.WriteByte((byte)rpcPoint.Type);
			}
		}

		if (audioEngine._reverbSettings != null)
		{
			bufferWriter.BytePosition = (int)audioEngine.dspParamsOffset;
			audioEngineReader.reverbSettingsReader.Write(audioEngine._reverbSettings);
		}

		return array;

		void WriteVariables(XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable[] variables)
		{
			if (variables == null)
			{
				return;
			}

			foreach (XnbConverter.Xact.AudioEngine.Entity.AudioEngine.RpcVariable rpcVariable2 in variables)
			{
				bufferWriter.BytePosition = (int)audioEngine.varsOffset + rpcVariable2.Index * 13;
				bufferWriter.WriteByte(rpcVariable2.Flags);
				bufferWriter.WriteSingle((float)rpcVariable2.InitValue);
				bufferWriter.WriteSingle((float)rpcVariable2.MinValue);
				bufferWriter.WriteSingle((float)rpcVariable2.MaxValue);
			}
		}
	}

	/// <summary>写回 .xgs 文件。</summary>
	public static void Write(XnbConverter.Xact.AudioEngine.Entity.AudioEngine audioEngine, string path)
	{
		File.WriteAllBytes(path, Build(audioEngine));
	}
}
