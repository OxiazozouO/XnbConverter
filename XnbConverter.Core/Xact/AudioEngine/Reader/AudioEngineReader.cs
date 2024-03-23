using System;
using System.Collections.Generic;
using XnbConverter.Readers;
using XnbConverter.Utilities;
using XnbConverter.Xact.AudioEngine.Entity;

namespace XnbConverter.Xact.AudioEngine.Reader;

/**
 * AudioEngine类用于加载XACT XGS文件
 * @public
 * @class
 */
public class AudioEngineReader : BaseReader
{
    public AudioCategoryReader audioCategoryReader = new();
    public ReverbSettingsReader reverbSettingsReader = new();

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        audioCategoryReader.Init(readerResolver);
        reverbSettingsReader.Init(readerResolver);
    }

    public new static Entity.AudioEngine Read(string path)
    {
        var waveBankReader = new AudioEngineReader();
        waveBankReader.Init(new ReaderResolver
        {
            bufferReader = BufferReader.FormFile(path)
        });
        return waveBankReader.Read();
    }

    /**
     * 处理文件。
     * @private
     * @param {BufferReader} buffer
     */
    public override Entity.AudioEngine Read()
    {
        var result = new Entity.AudioEngine();
        // 读取魔术值
        result.Magic = bufferReader.ReadString(4);
        // 确保魔术值匹配
        if (result.Magic != "XGSF")
            throw new XnbError(Helpers.I18N["AudioEngineReader.1"], result.Magic);

        // 读取工具版本和格式版本
        result.ToolVersion = bufferReader.ReadUInt16();
        result.FormatVersion = bufferReader.ReadUInt16();

        // 记录版本信息
        Log.Debug(Helpers.I18N["AudioEngineReader.2"], result.ToolVersion);
        Log.Debug(Helpers.I18N["AudioEngineReader.3"], result.FormatVersion);
        // 检查是否为已知格式
        if (result.FormatVersion != Entity.AudioEngine.XGSF_FORMAT)
            Log.Warn(Helpers.I18N["AudioEngineReader.19"]);

        // 获取无用的CRC值，我们不关心它
        result.Crc = bufferReader.ReadUInt16();
        // 获取最后修改的低位和高位值
        result.LastModifiedLow = bufferReader.ReadUInt32();
        result.LastModifiedHigh = bufferReader.ReadUInt32();
        // 跳过未知字节（可能是平台相关的）
        result.Unkns.Add(bufferReader.Read(1));

        // 读取类别和变量的数量
        result.numCats = bufferReader.ReadUInt16();
        result.numVars = bufferReader.ReadUInt16();

        Log.Debug(Helpers.I18N["AudioEngineReader.4"], result.numCats, result.numVars);

        // 跳过两个未知的16位整数
        result.Unkns.Add(bufferReader.Read(4));

        // 读取RPC、DSP预设和参数的数量
        result.numRpc = bufferReader.ReadUInt16();
        result.numDspPresets = bufferReader.ReadUInt16();
        result.numDspParams = bufferReader.ReadUInt16();
        // 获取类别和变量的偏移量
        result.catsOffset = bufferReader.ReadUInt32();
        result.varsOffset = bufferReader.ReadUInt32();


        Log.Debug(Helpers.I18N["AudioEngineReader.5"], result.numRpc);
        Log.Debug(Helpers.I18N["AudioEngineReader.6"], result.numDspPresets);
        Log.Debug(Helpers.I18N["AudioEngineReader.7"], result.numDspParams);
        Log.Debug(Helpers.I18N["AudioEngineReader.8"], result.catsOffset);
        Log.Debug(Helpers.I18N["AudioEngineReader.9"], result.varsOffset);

        // 未知的32位无符号整数
        result.Unkns.Add(bufferReader.Read(4));
        // 获取类别名称索引的偏移量
        result.catNameIndexOffset = bufferReader.ReadUInt32();
        // 未知的32位无符号整数
        result.Unkns.Add(bufferReader.Read(4));
        // 获取变量名称索引的偏移量
        result.varNameIndexOffset = bufferReader.ReadUInt32();
        // 读取类别和变量名称的偏移量
        result.catNamesOffset = bufferReader.ReadUInt32();
        result.varNamesOffset = bufferReader.ReadUInt32();
        // 读取RPC、DSP预设和参数的偏移量
        result.rpcOffset = bufferReader.ReadUInt32();
        result.dspPresetOffset = bufferReader.ReadUInt32();
        result.dspParamsOffset = bufferReader.ReadUInt32();

        Log.Debug(Helpers.I18N["AudioEngineReader.10"], result.catNameIndexOffset);
        Log.Debug(Helpers.I18N["AudioEngineReader.11"], result.varNameIndexOffset);
        Log.Debug(Helpers.I18N["AudioEngineReader.12"], result.catNamesOffset);
        Log.Debug(Helpers.I18N["AudioEngineReader.13"], result.varNamesOffset);
        Log.Debug(Helpers.I18N["AudioEngineReader.14"], result.rpcOffset);
        Log.Debug(Helpers.I18N["AudioEngineReader.15"], result.dspPresetOffset);
        Log.Debug(Helpers.I18N["AudioEngineReader.16"], result.dspParamsOffset);

        // 定位到类别名称偏移量以读取类别
        bufferReader.BytePosition = (int)result.catNamesOffset;

        var categoryNames = new string[result.numCats];
        for (var i = 0; i < result.numCats; i++)
            categoryNames[i] = bufferReader.ReadString();

        Log.Debug(Helpers.I18N["AudioEngineReader.17"], categoryNames.ToJoinStr());

        // 获取实际的类别数据
        result._categories = new AudioCategory[result.numCats];
        bufferReader.BytePosition = (int)result.catsOffset;
        for (var i = 0; i < result.numCats; i++)
        {
            result._categories[i] = audioCategoryReader.Read();
            result._categories[i].name = categoryNames[i];

            result._categoryLookup.Add(categoryNames[i], i);
        }


        // 定位到变量名称偏移量
        bufferReader.BytePosition = (int)result.varNamesOffset;
        // 读取变量名称
        var varNames = new string[result.numVars];
        for (var i = 0; i < result.numVars; i++)
            varNames[i] = bufferReader.ReadString();
        Log.Debug(Helpers.I18N["AudioEngineReader.18"], varNames.ToJoinStr());

        // 读取变量本身
        var variables = new List<Entity.AudioEngine.RpcVariable>();
        var cueVariables = new List<Entity.AudioEngine.RpcVariable>();
        var globalVariables = new List<Entity.AudioEngine.RpcVariable>();
        // 定位到变量偏移量
        bufferReader.BytePosition = (int)result.varsOffset;
        // 循环遍历变量
        for (var i = 0; i < result.numVars; i++)
        {
            var v = new Entity.AudioEngine.RpcVariable();
            v.Name = varNames[i];
            v.Flags = bufferReader.ReadByte();
            v.InitValue = bufferReader.ReadSingle();
            v.MinValue = bufferReader.ReadSingle();
            v.MaxValue = bufferReader.ReadSingle();
            v.Value = v.InitValue;

            variables.Add(v);
            if (!v.IsGlobal)
            {
                cueVariables.Add(v);
            }
            else
            {
                globalVariables.Add(v);

                result._variableLookup.Add(v.Name, globalVariables.Count - 1);
            }
        }

        result._cueVariables = cueVariables.ToArray();
        result._variables = globalVariables.ToArray();

        // RPC曲线
        List<Entity.AudioEngine.RpcCurve> reverbCurves = new();
        result.RpcCurves = new Entity.AudioEngine.RpcCurve[result.numRpc];
        if (result.numRpc > 0)
        {
            bufferReader.BytePosition = (int)result.rpcOffset;
            for (var i = 0; i < result.numRpc; i++)
            {
                var curve = new Entity.AudioEngine.RpcCurve();
                curve.FileOffset = (uint)bufferReader.BytePosition;
                var variable = variables[bufferReader.ReadUInt16()];
                curve.IsGlobal = variable.IsGlobal;
                if (variable.IsGlobal)
                    curve.Variable = globalVariables.FindIndex(e => e.Name == variable.Name);
                else
                    curve.Variable = cueVariables.FindIndex(e => e.Name == variable.Name);

                var pointCount = bufferReader.ReadByte();
                curve.Parameter = (Entity.AudioEngine.RpcParameter)bufferReader.ReadUInt16();
                curve.Points = new Entity.AudioEngine.RpcPoint[pointCount];
                for (var j = 0; j < pointCount; j++)
                    curve.Points[j] = new Entity.AudioEngine.RpcPoint
                    {
                        X = bufferReader.ReadSingle(),
                        Y = bufferReader.ReadSingle(),
                        Type = (Entity.AudioEngine.RpcPointType)bufferReader.ReadByte()
                    };

                var dspParameter = curve.Parameter - Entity.AudioEngine.RpcParameter.NumParameters;
                if (dspParameter >= 0 && variable.IsGlobal)
                    reverbCurves.Add(curve);

                result.RpcCurves[i] = curve;
            }
        }

        result._reverbCurves = reverbCurves.ToArray();

        if (result.numDspPresets > 0)
        {
            // Note:  It seemed like MS designed this to support multiple
            // DSP effects, but in practice XACT only has one... Microsoft Reverb.
            //
            // So because of this we know exactly how many presets and
            // parameters we should have.
            if (result.numDspPresets != 1)
                throw new Exception("Unexpected number of DSP presets!");
            if (result.numDspParams != 22)
                throw new Exception("Unexpected number of DSP parameters!");

            bufferReader.BytePosition = (int)result.dspParamsOffset;
            result._reverbSettings = reverbSettingsReader.Read();
        }


        Log.Debug(reverbCurves.ToJoinStr());

        return result;
    }

    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override void Write(object input)
    {
        throw new NotImplementedException();
    }
}