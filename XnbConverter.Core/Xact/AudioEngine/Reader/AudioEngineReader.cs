using Newtonsoft.Json;
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
    public AudioCategoryReader audioCategoryReader = new AudioCategoryReader();
    public ReverbSettingsReader reverbSettingsReader = new ReverbSettingsReader();

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        audioCategoryReader.Init(readerResolver);
        reverbSettingsReader.Init(readerResolver);
    }
    
    public new static Entity.AudioEngine Read(string path)
    {
        AudioEngineReader waveBankReader = new AudioEngineReader();
        waveBankReader.Init(new ReaderResolver(){
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
        Entity.AudioEngine result = new Entity.AudioEngine();
        // 读取魔术值
        result.Magic = bufferReader.ReadString(4);
        // 确保魔术值匹配
        if (result.Magic != "XGSF")
            throw new XnbError("发现无效的魔术值，{0}", result.Magic);

        // 读取工具版本和格式版本
        result.ToolVersion   = bufferReader.ReadUInt16();
        result.FormatVersion = bufferReader.ReadUInt16();
        
        // 记录版本信息
        Log.Debug("工具版本：{0}", result.ToolVersion  );
        Log.Debug("格式版本：{0}", result.FormatVersion);
        // 检查是否为已知格式
        if (result.FormatVersion != Entity.AudioEngine.XGSF_FORMAT)
            Log.Warn("不支持的XGS格式！");

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

        Log.Debug("类别：{0}, 变量：{1}", result.numCats, result.numVars);
        
        // 跳过两个未知的16位整数
        result.Unkns.Add(bufferReader.Read(4));

        // 读取RPC、DSP预设和参数的数量
        result.numRpc        = bufferReader.ReadUInt16();
        result.numDspPresets = bufferReader.ReadUInt16();
        result.numDspParams  = bufferReader.ReadUInt16();
        // 获取类别和变量的偏移量
        result.catsOffset = bufferReader.ReadUInt32();
        result.varsOffset = bufferReader.ReadUInt32();
        
        
        Log.Debug("RPC: {0}", result.numRpc);
        Log.Debug("DSP预设: {0}", result.numDspPresets);
        Log.Debug("DSP参数: {0}", result.numDspParams);
        Log.Debug("类别偏移量: {0}", result.catsOffset);
        Log.Debug("变量偏移量: {0}", result.varsOffset);

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
        result.rpcOffset       = bufferReader.ReadUInt32();
        result.dspPresetOffset = bufferReader.ReadUInt32();
        result.dspParamsOffset = bufferReader.ReadUInt32();
        
        Log.Debug("类别名称索引偏移量: {0}", result.catNameIndexOffset);
        Log.Debug("变量名称索引偏移量: {0}", result.varNameIndexOffset);
        Log.Debug("类别名称偏移量: {0}", result.catNamesOffset);
        Log.Debug("变量名称偏移量: {0}", result.varNamesOffset);
        Log.Debug("RPC偏移量:    {0}", result.rpcOffset);
        Log.Debug("DSP预设偏移量: {0}", result.dspPresetOffset);
        Log.Debug("DSP参数偏移量: {0}", result.dspParamsOffset);

        // 定位到类别名称偏移量以读取类别
        bufferReader.BytePosition = (int)result.catNamesOffset;
        
        string[] categoryNames = new string[result.numCats];
        for (int i = 0; i < result.numCats; i++)
            categoryNames[i] = bufferReader.ReadString();
        
        Log.Debug("类别: {0}", string.Join(", ", categoryNames));
        
        // 获取实际的类别数据
        result._categories = new AudioCategory[result.numCats];
        bufferReader.BytePosition = (int)result.catsOffset;
        for (int i = 0; i < result.numCats; i++)
        {
            result._categories[i] = audioCategoryReader.Read();
            result._categories[i].name = categoryNames[i];
            
            result._categoryLookup.Add (categoryNames[i], i);
        }


        // 定位到变量名称偏移量
        bufferReader.BytePosition = (int)result.varNamesOffset;
        // 读取变量名称
        string[] varNames = new string[result.numVars];
        for (int i = 0; i < result.numVars; i++)
            varNames[i] = bufferReader.ReadString();
        Log.Debug("变量: {0}", string.Join(", ", varNames));

        // 读取变量本身
        var variables = new List<Entity.AudioEngine.RpcVariable>();
        var cueVariables = new List<Entity.AudioEngine.RpcVariable>();
        var globalVariables = new List<Entity.AudioEngine.RpcVariable>();
        // 定位到变量偏移量
        bufferReader.BytePosition = (int)result.varsOffset;
        // 循环遍历变量
        for (var i=0; i < result.numVars; i++)
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
                cueVariables.Add(v);
            else
            {
                globalVariables.Add(v);
                
                result._variableLookup.Add(v.Name, globalVariables.Count - 1);
            }
        }
        result._cueVariables = cueVariables.ToArray();
        result._variables = globalVariables.ToArray();
        
        // RPC曲线
        List<Entity.AudioEngine.RpcCurve> reverbCurves = new List<Entity.AudioEngine.RpcCurve>();
        result.RpcCurves = new Entity.AudioEngine.RpcCurve[result.numRpc];
        if (result.numRpc > 0)
        {
            bufferReader.BytePosition = (int)result.rpcOffset;
            for (int i = 0; i < result.numRpc; i++)
            {
                Entity.AudioEngine.RpcCurve curve = new Entity.AudioEngine.RpcCurve();
                curve.FileOffset = (uint)bufferReader.BytePosition;
                var variable = variables[bufferReader.ReadUInt16()];
                curve.IsGlobal = variable.IsGlobal;
                if (variable.IsGlobal)
                {
                    curve.Variable = globalVariables.FindIndex(e => e.Name == variable.Name);
                }
                else
                {
                    curve.Variable = cueVariables.FindIndex(e => e.Name == variable.Name);
                }
                
                byte pointCount = bufferReader.ReadByte();
                curve.Parameter =  (Entity.AudioEngine.RpcParameter)bufferReader.ReadUInt16();
                curve.Points = new Entity.AudioEngine.RpcPoint[pointCount];
                for (int j = 0; j < pointCount; j++)
                {
                    curve.Points[j] = new Entity.AudioEngine.RpcPoint
                    {
                        X = bufferReader.ReadSingle(),
                        Y = bufferReader.ReadSingle(),
                        Type = (Entity.AudioEngine.RpcPointType)bufferReader.ReadByte()
                    };
                }
                
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

        
        Log.Debug(JsonConvert.SerializeObject(reverbCurves, Formatting.Indented));

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