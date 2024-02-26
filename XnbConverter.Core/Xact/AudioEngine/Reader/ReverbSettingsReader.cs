using XnbConverter.Readers;
using XnbConverter.Xact.AudioEngine.Entity;

namespace XnbConverter.Xact.AudioEngine.Reader;

public class ReverbSettingsReader : BaseReader
{
    private DspParameterReader dspParameterReader = new();

    public override void Init(ReaderResolver readerResolver)
    {
        base.Init(readerResolver);
        dspParameterReader.Init(readerResolver);
    }

    public override bool IsValueType()
    {
        throw new NotImplementedException();
    }

    public override ReverbSettings Read()
    {
        var result = new ReverbSettings();
        result.Parameters = new[]
        {
            dspParameterReader.Read(), // ReflectionsDelayMs
            dspParameterReader.Read(), // ReverbDelayMs
            dspParameterReader.Read(), // PositionLeft
            dspParameterReader.Read(), // PositionRight
            dspParameterReader.Read(), // PositionLeftMatrix
            dspParameterReader.Read(), // PositionRightMatrix
            dspParameterReader.Read(), // EarlyDiffusion
            dspParameterReader.Read(), // LateDiffusion
            dspParameterReader.Read(), // LowEqGain
            dspParameterReader.Read(), // LowEqCutoff
            dspParameterReader.Read(), // HighEqGain
            dspParameterReader.Read(), // HighEqCutoff
            dspParameterReader.Read(), // RearDelayMs
            dspParameterReader.Read(), // RoomFilterFrequencyHz
            dspParameterReader.Read(), // RoomFilterMainDb
            dspParameterReader.Read(), // RoomFilterHighFrequencyDb
            dspParameterReader.Read(), // ReflectionsGainDb
            dspParameterReader.Read(), // ReverbGainDb
            dspParameterReader.Read(), // DecayTimeSec
            dspParameterReader.Read(), // DensityPct
            dspParameterReader.Read(), // RoomSizeFeet
            dspParameterReader.Read() // WetDryMixPct
        };

        return result;
    }

    public override void Write(object input)
    {
        throw new NotImplementedException();
    }
}